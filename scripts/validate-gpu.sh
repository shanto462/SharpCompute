#!/usr/bin/env bash
# Runs the tests under Metal's validation layers and fails on anything they report.
#
#   scripts/validate-gpu.sh                      the whole suite
#   scripts/validate-gpu.sh --filter <expr>      only some tests (a `dotnet test --filter` expression)
#   scripts/validate-gpu.sh --out <dir>          where the logs and the report go (default: a temp folder)
#   scripts/validate-gpu.sh --abort              stop the test host at the first violation (for a debugger
#                                                or a crash report); the allow-list does not apply
#
# The layers (see `man MetalValidation` and Apple's "Validating your app's Metal API usage" and
# "Validating your app's Metal shader usage"):
#   MTL_DEBUG_LAYER=1          API validation: wrong arguments, offsets, usages, encoder state.
#   MTL_SHADER_VALIDATION=1    GPU validation: every shader is instrumented, so an out-of-bounds buffer,
#                              texture or threadgroup-memory access, a nil or wrongly typed texture and
#                              a non-resident resource are reported, with the shader's name and line.
#
# How a violation becomes a failure. A shader violation does not fail its command buffer (the read is
# zero-filled and the write dropped, MTL_SHADER_VALIDATION_FAIL_MODE=zerofill), so a test can pass
# with one; it is only written to the log, or to stderr with MTL_SHADER_VALIDATION_REPORT_TO_STDERR=1.
# An API violation asserts by default, which kills the test host at the first one; here
# MTL_DEBUG_LAYER_ERROR_MODE=nslog logs it and carries on, so one run reports all of them. `dotnet
# test` does not show the test host's stderr, but its --diag log records every line of it, and that
# is what this script reads. Each report is then matched against scripts/validate-gpu.allow; the
# script exits 1 if a test failed, the test host crashed or a report is not allow-listed, and 2 if the
# run cannot be trusted: the layers' start-up banners are missing (nothing was validated), a diag log
# was rotated, or a shader report could not be parsed.
#
# The suite is heavy and instrumented shaders are slower: set SHARPCOMPUTE_RUN_LOCK to a directory
# path to serialize with other GPU-heavy jobs (scripts/fuzz-differential.sh uses the same variable).
# The script then waits until it can create that directory and removes it when it exits; when the
# variable is unset it takes no lock. VALIDATE_GPU_LOCK, its former name, still works.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
allow="$root/scripts/validate-gpu.allow"
projects=(SharpCompute.Tests SharpCompute.SourceGenerators.Tests SharpCompute.Build.Tests SharpCompute.Vectors.Tests)

filter=""
out=""
abort=0
while [[ $# -gt 0 ]]; do
  case "$1" in
    --filter) filter="$2"; shift 2 ;;
    --out) mkdir -p "$2"; out="$(cd "$2" && pwd)"; shift 2 ;;
    --abort) abort=1; shift ;;
    -h|--help) sed -n '2,30p' "$0"; exit 0 ;;
    *) echo "unknown option: $1" >&2; exit 2 ;;
  esac
done
[[ -n "$out" ]] || out="$(mktemp -d "${TMPDIR:-/tmp}/validate-gpu.XXXXXX")"

cd "$root"
echo "== build"
dotnet build SharpCompute.slnx --nologo -v quiet

# The lock is released only by the shell that took it: the trap is set after mkdir succeeds.
lock="${SHARPCOMPUTE_RUN_LOCK:-${VALIDATE_GPU_LOCK:-}}"
if [[ -n "$lock" ]]; then
  waited=0
  until mkdir "$lock" 2>/dev/null; do
    [[ $waited -eq 1 ]] || echo "== waiting for $lock"
    waited=1
    sleep 20
  done
  trap 'rmdir "$lock"' EXIT
fi

export MTL_DEBUG_LAYER=1
export MTL_DEBUG_LAYER_ERROR_MODE=nslog
export MTL_DEBUG_LAYER_WARNING_MODE=nslog
export MTL_SHADER_VALIDATION=1
export MTL_SHADER_VALIDATION_FAIL_MODE=zerofill
export MTL_SHADER_VALIDATION_REPORT_TO_STDERR=1
if [[ $abort -eq 1 ]]; then
  export MTL_DEBUG_LAYER_ERROR_MODE=assert
  export MTL_SHADER_VALIDATION_ABORT_ON_FAULT=1
fi

tests_failed=0
rotated=0
: > "$out/stderr.txt"
for project in "${projects[@]}"; do
  echo "== test $project (validation layers on)"
  mkdir -p "$out/$project"
  # tracelevel=warning: stderr lines are traced as warnings, and the verbose trace is several times larger.
  args=(test "tests/$project" --no-build --nologo --diag "$out/$project/diag.txt;tracelevel=warning")
  [[ -z "$filter" ]] || args+=(--filter "$filter")
  # dotnet's own status, not grep's (which is 1 when it prints nothing): read before anything else runs.
  set +e
  dotnet "${args[@]}" 2>&1 | tee "$out/$project/test.txt" | grep -Ev '^\s+(Passed|Skipped) '
  test_status=${PIPESTATUS[0]}
  set -e
  [[ $test_status -eq 0 ]] || tests_failed=1
  # vstest traces each line the test host wrote to stderr; nothing else shows them.
  sed -n 's/^.*Test host standard error line: //p' "$out/$project/diag.txt" >> "$out/stderr.txt"
  # vstest starts a new log when one grows past its limit and keeps only the previous one.
  [[ ! -e "$out/$project/diag.bak.txt" ]] || rotated=1
done

# One line per report, so that a pattern can match it:
#   shader: Invalid Metal usage: <what, in which function> | <the resource> | pipeline: <label>
#   api: <the debug layer's message, without the object's address>
# A shader report is a block between two lines of '='; an API report is an NSLog line.
awk '
  /^=+$/ {
    if (in_block && what != "") print "shader: " what (resource != "" ? " | " resource : "") (pipeline != "" ? " | " pipeline : "")
    in_block = !in_block; what = ""; resource = ""; pipeline = ""; next
  }
  in_block {
    if ($0 ~ /^Invalid Metal usage/) { what = $0; sub(/ +$/, "", what) }
    else if (what != "" && resource == "" && $0 ~ /^MTL/) { resource = $0; sub(/, *gpuAddress: [^,]*/, "", resource) }
    else if ($0 ~ /^pipeline: /) { pipeline = $0; sub(/, pipeline UID:.*/, "", pipeline) }
    next
  }
  /^[0-9]{4}-[0-9]{2}-[0-9]{2} [0-9:.]+ [^ ]+\[[0-9]+:[0-9]+\] / {
    sub(/^[^\]]*\] /, "")
    gsub(/: 0x[0-9a-f]+>/, ">")
    if ($0 !~ /^Metal (API|GPU) Validation Enabled$/) print "api: " $0
  }
' "$out/stderr.txt" > "$out/reports.txt"

# The allow-list: `<extended regular expression> ## <reason>` per line; '#' starts a comment line.
: > "$out/patterns.txt"
if [[ -f "$allow" ]]; then
  grep -Ev '^\s*(#|$)' "$allow" | sed -E 's/[[:space:]]+## .*$//' > "$out/patterns.txt" || true
fi
if [[ -s "$out/patterns.txt" && $abort -eq 0 ]]; then
  grep -E -f "$out/patterns.txt" "$out/reports.txt" > "$out/allowed.txt" || true
  grep -Ev -f "$out/patterns.txt" "$out/reports.txt" > "$out/violations.txt" || true
else
  : > "$out/allowed.txt"
  cp "$out/reports.txt" "$out/violations.txt"
fi

count() { wc -l < "$1" | tr -d ' '; }
unique() { sed -E 's/ index [0-9]+/ index N/' "$1" | sort | uniq -c | sort -rn; }
total() { grep -hE '^(Passed|Failed)! ' "$out"/*/test.txt | sed -E "s/.*$1: *([0-9]+).*/\1/" | awk '{ s += $1 } END { print s + 0 }'; }

echo
echo "== summary"
echo "tests: $(total Total) run, $(total Passed) passed, $(total Skipped) skipped, $(total Failed) failed"
echo "reports: $(count "$out/reports.txt") ($(sort -u "$out/reports.txt" | count /dev/stdin) distinct), allow-listed: $(count "$out/allowed.txt"), violations: $(count "$out/violations.txt")"
echo "logs: $out"

status=0
if ! grep -q 'Metal API Validation Enabled' "$out/stderr.txt" || ! grep -q 'Metal GPU Validation Enabled' "$out/stderr.txt"; then
  echo "error: the validation layers did not announce themselves; nothing was validated." >&2
  status=2
fi
if [[ $rotated -ne 0 ]]; then
  echo "error: a diag log was rotated, so reports may be missing; run fewer tests at a time with --filter." >&2
  status=2
fi
if [[ -s "$out/allowed.txt" ]]; then
  echo
  echo "allow-listed (scripts/validate-gpu.allow):"
  unique "$out/allowed.txt"
fi
if [[ -s "$out/violations.txt" ]]; then
  echo
  echo "violations (the full reports are in $out/stderr.txt):" >&2
  unique "$out/violations.txt" >&2
  [[ $status -ne 0 ]] || status=1
fi
# A report whose lines another thread's output interleaved could fail to parse; then the counts differ.
if [[ $(grep -c '^Invalid Metal usage' "$out/stderr.txt" || true) -ne $(grep -c '^shader: ' "$out/reports.txt" || true) ]]; then
  echo "error: not every 'Invalid Metal usage' line in $out/stderr.txt became a report; read it by hand." >&2
  status=2
fi
# Besides dotnet's exit status: the counted failures, and a test host that died without a result line.
if [[ $(total Failed) -gt 0 ]] || grep -qE 'Test host process crashed|The active test run was aborted' "$out"/*/test.txt; then
  tests_failed=1
fi
if [[ $tests_failed -ne 0 ]]; then
  echo "error: tests failed or the test host crashed under the validation layers (see $out/*/test.txt)." >&2
  [[ $status -ne 0 ]] || status=1
fi
exit $status
