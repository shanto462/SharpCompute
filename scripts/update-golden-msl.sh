#!/usr/bin/env bash
# Regenerates the golden MSL snapshots in tests/golden-msl: the MSL of every shader the generator
# emits anywhere in the solution, one .metal file per shader.
#
#   scripts/update-golden-msl.sh               rebuild, rewrite the snapshots, list what changed
#   scripts/update-golden-msl.sh -c Release    the same from a Release build (the MSL is the same)
#
# Run it when a change is meant to alter the emitted MSL, then review `git diff tests/golden-msl`:
# that diff is what the change did to the generator's output. A normal test run only compares and
# never writes. Needs no GPU and no Metal toolchain.
source "$(dirname "$0")/_common.sh"

step "Regenerate tests/golden-msl"
SHARPCOMPUTE_UPDATE_GOLDEN=1 dotnet test tests/SharpCompute.GoldenMsl.Tests -nologo --filter EmittedMsl_MatchesTheSnapshots "$@"

step "Changed snapshots"
changed="$(git status --short -- tests/golden-msl)"
if [[ -z "$changed" ]]; then
  echo "none: the emitted MSL is what the snapshots already hold"
else
  echo "$changed"
  echo
  echo "Review them with: git diff tests/golden-msl"
fi
