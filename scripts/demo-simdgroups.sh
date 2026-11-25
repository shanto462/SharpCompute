#!/usr/bin/env bash
# SIMD-group ("wave") operations: the Gpu.Wave* reductions, votes, prefix scans and lane shuffles, one
# page per algorithm they make possible. Every page runs it on the GPU, shows the result, checks it
# against plain C# on the CPU and shows GPU ms vs CPU ms.
#   Reduction:  the sum of 16.7M floats three ways (WaveActiveSum per SIMD group, the same after a loop
#               per thread, and loops only), with each one's time and rounding error; V picks the method
#   Compaction: 32,768 particles, the ones inside a moving circle packed in order without atomics, by
#               WaveActiveBallot, WaveActiveCountBits and three WavePrefixSums; V circle size
#   Shuffle:    a 1D signal smoothed inside each SIMD group of 32 by WaveShuffleUp/WaveShuffleDown, next
#               to the same filter with no group edges; V switches between 3 and 5 taps
#   <-/-> switch page · Space pause · S save PNG · Esc quit
#
#   scripts/demo-simdgroups.sh                    # the window
#   scripts/demo-simdgroups.sh --page Shuffle     # start on a page (--list shows them)
#   scripts/demo-simdgroups.sh --headless         # no window: a PNG per page and summary.txt in demo-output/simdgroups/
source "$(dirname "$0")/_common.sh"
build samples/SharpCompute.Samples.SimdGroups
if [[ "${1:-}" == "--headless" ]]; then
  shift
  step "SimdGroups: every page headless, GPU checked against the CPU"
  run_project samples/SharpCompute.Samples.SimdGroups --headless --out demo-output/simdgroups "$@"
  open_result demo-output/simdgroups
else
  step "SimdGroups (Esc or close the window to exit)"
  run_project samples/SharpCompute.Samples.SimdGroups "$@"
fi
