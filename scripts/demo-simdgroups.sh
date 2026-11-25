#!/usr/bin/env bash
# SIMD-group ("wave") operations: ComputeLab's SIMD-group pages (SIMD reduction, SIMD compaction,
# SIMD shuffle). This opens ComputeLab on the first of them; see scripts/demo-computelab.sh.
#
#   scripts/demo-simdgroups.sh                       # ComputeLab at its SIMD reduction page
#   scripts/demo-simdgroups.sh --page simd-shuffle   # or another page
#   scripts/demo-simdgroups.sh --headless            # every ComputeLab page headless, in demo-output/computelab/
[[ "${1:-}" == "--headless" ]] && exec "$(dirname "$0")/demo-computelab.sh" "$@"
exec "$(dirname "$0")/demo-computelab.sh" --page simd-reduction "$@"
