#!/usr/bin/env bash
# ComputeLab: one page per core compute feature, by topic. Every page runs the feature on the GPU,
# shows the result, checks it against the CPU and shows GPU ms vs CPU ms.
#   Atomics
#     Histogram: a live 256-bin luminance histogram; C folds it into 4 bins to show contention
#     Histogram: privatised: the same histogram counted per threadgroup in [GroupShared] memory and
#       merged, timed against the global-atomics version; C as above
#     Compaction: keep the passing elements of 1M through an AtomicAdd counter; Up/Down threshold
#   Shared memory
#     Reduction: the sum of 16.7M floats by a tree in shared memory; V switches to a serial loop per thread
#     PrefixSum: a work-efficient (Blelloch) scan of 1M flags, for order-preserving compaction; Up/Down threshold
#     MatMul: 1024 x 1024 matrices, 16 x 16 tiles in shared memory vs naive, in GFLOP/s; V switches
#     Blur: a separable blur whose groups load a tile plus apron into shared memory; V switches, Up/Down radius
#   Local arrays
#     LocalArrays: a 3x3 median (9-element stackalloc array, sorting network) and a 5x5 Gaussian (weight table); F switches, N noise
#   SIMD groups
#     SIMD reduction: 16.7M floats summed three ways (WaveActiveSum, after a loop, loops only); V method
#     SIMD compaction: 32,768 particles packed in order by ballots and prefix sums, no atomics; V circle size
#     SIMD shuffle: a signal smoothed inside each SIMD group by WaveShuffleUp/Down; V 3 or 5 taps
#   Dispatch
#     Indirect: compact, then dispatch exactly the survivors; D steps through the full-array alternatives
#   Math
#     Bits: popcount, bit reversal, clz/ctz, float bits and unorm8 packing as patterns
#     FastMath: one kernel with and without [ShaderOptions(FastMath = true)]; F toggles
#   Code reuse
#     Shared code: a path-traced scene built from a static class and structs with methods
#       (ShaderMath, Ray.At, Sphere.Intersect); ForCpu checks every 8th pixel
#   <-/-> switch page · Space pause · S save PNG · Esc quit
#
#   scripts/demo-computelab.sh               # the window
#   scripts/demo-computelab.sh --page histogram-privatised   # start on a page (--list shows them)
#   scripts/demo-computelab.sh --headless    # no window: a PNG per page and summary.txt in demo-output/computelab/
source "$(dirname "$0")/_common.sh"
build samples/SharpCompute.Samples.ComputeLab
if [[ "${1:-}" == "--headless" ]]; then
  shift
  step "ComputeLab: every page headless, GPU checked against the CPU"
  run_project samples/SharpCompute.Samples.ComputeLab --headless --out demo-output/computelab "$@"
  open_result demo-output/computelab
else
  step "ComputeLab (Esc or close the window to exit)"
  run_project samples/SharpCompute.Samples.ComputeLab "$@"
fi
