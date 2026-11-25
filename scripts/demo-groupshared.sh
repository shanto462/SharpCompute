#!/usr/bin/env bash
# Threadgroup shared memory and barriers: [GroupShared] arrays, Gpu.GroupMemoryBarrierWithGroupSync and
# the group built-ins, one page per algorithm they make possible. Every page runs it on the GPU, shows
# the result, checks it against plain C# on the CPU and shows GPU ms vs CPU ms.
#   Reduction: the sum of 16.7M floats by a tree in shared memory; V switches to a serial loop per thread
#   PrefixSum: a work-efficient (Blelloch) scan of 1M flags, used for order-preserving stream
#              compaction; Up/Down threshold
#   MatMul:    1024 x 1024 matrices, 16 x 16 tiles in shared memory vs naive, in GFLOP/s; V switches
#   Blur:      a separable blur whose groups load a tile plus apron into shared memory, vs naive;
#              V switches, Up/Down radius
#   <-/-> switch page · Space pause · S save PNG · Esc quit
#
#   scripts/demo-groupshared.sh                 # the window
#   scripts/demo-groupshared.sh --page MatMul   # start on a page (--list shows them)
#   scripts/demo-groupshared.sh --headless      # no window: a PNG per page and summary.txt in demo-output/groupshared/
source "$(dirname "$0")/_common.sh"
build samples/SharpCompute.Samples.GroupShared
if [[ "${1:-}" == "--headless" ]]; then
  shift
  step "GroupShared: every page headless, GPU checked against the CPU"
  run_project samples/SharpCompute.Samples.GroupShared --headless --out demo-output/groupshared "$@"
  open_result demo-output/groupshared
else
  step "GroupShared (Esc or close the window to exit)"
  run_project samples/SharpCompute.Samples.GroupShared "$@"
fi
