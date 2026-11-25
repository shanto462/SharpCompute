#!/usr/bin/env bash
# Threadgroup shared memory and barriers: ComputeLab's shared-memory pages (Reduction, PrefixSum,
# MatMul, Blur). This opens ComputeLab on the first of them; see scripts/demo-computelab.sh.
#
#   scripts/demo-groupshared.sh                 # ComputeLab at its Reduction page
#   scripts/demo-groupshared.sh --page matmul   # or another page
#   scripts/demo-groupshared.sh --headless      # every ComputeLab page headless, in demo-output/computelab/
[[ "${1:-}" == "--headless" ]] && exec "$(dirname "$0")/demo-computelab.sh" "$@"
exec "$(dirname "$0")/demo-computelab.sh" --page reduction "$@"
