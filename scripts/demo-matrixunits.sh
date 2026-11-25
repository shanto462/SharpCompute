#!/usr/bin/env bash
# The GPU's matrix units: ComputeLab's "Matrix units" page, a 1024 x 1024 and 2048 x 2048 matrix
# product on WaveMatrix8x8 (8 x 8 matrices held by the lanes of a SIMD group) against the
# shared-memory tiles and the naive kernel, in GFLOP/s. V switches the kernel, N the size.
# See scripts/demo-computelab.sh.
#
#   scripts/demo-matrixunits.sh              # ComputeLab at its Matrix units page
#   scripts/demo-matrixunits.sh --headless   # every ComputeLab page headless, in demo-output/computelab/
[[ "${1:-}" == "--headless" ]] && exec "$(dirname "$0")/demo-computelab.sh" "$@"
exec "$(dirname "$0")/demo-computelab.sh" --page matrix-units "$@"
