#!/usr/bin/env bash
# Runs the SharpCompute GPU-vs-CPU benchmarks and writes benchmarks/RESULTS.md.
#
#   scripts/run-benchmarks.sh            BenchmarkDotNet default job (accurate, slow: ~30-60 min)
#   scripts/run-benchmarks.sh --quick    BenchmarkDotNet ShortRun job (a few minutes)
#   scripts/run-benchmarks.sh --filter '*Saxpy*'   only some benchmarks (the report covers what ran)
#   scripts/run-benchmarks.sh --filter '*Mandelbrot*' --merge <run-data.json>
#                                         re-run some workloads and merge them into an earlier full run
#
# Steps: Release build, then --verify (every workload on CPU and GPU, compared; the script stops
# if any check fails), then --run (verify again for the report, first-dispatch timing in fresh
# processes, BenchmarkDotNet, RESULTS.md). Needs macOS 15+ on Apple Silicon and the .NET 11 SDK.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
project="$root/benchmarks/SharpCompute.Benchmarks"
results="$root/benchmarks/RESULTS.md"

run_args=()
while [[ $# -gt 0 ]]; do
  case "$1" in
    --quick) run_args+=(--quick); shift ;;
    --filter) run_args+=(--filter "$2"); shift 2 ;;
    --merge) run_args+=(--merge "$(cd "$(dirname "$2")" && pwd)/$(basename "$2")"); shift 2 ;;
    -h|--help) sed -n '2,13p' "$0"; exit 0 ;;
    *) echo "unknown option: $1" >&2; exit 2 ;;
  esac
done

echo "== build (Release)"
dotnet build "$project" -c Release --nologo -v quiet

app="$project/bin/Release/net11.0/SharpCompute.Benchmarks"

echo "== verify"
if ! "$app" --verify; then
  echo "verification failed: GPU and CPU results disagree; not benchmarking." >&2
  exit 1
fi

# BenchmarkDotNet writes its artifacts (logs, per-run projects) under the current directory.
cd "$project"
"$app" --run --out "$results" ${run_args[@]+"${run_args[@]}"}

echo "== done: $results"
