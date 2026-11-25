#!/usr/bin/env bash
# ComputeLab: one page per core compute feature. Every page runs the feature on the GPU, shows the
# result, checks it against the CPU (ForCpu of the same shader) and shows GPU ms vs CPU ms.
#   Histogram (atomics): a live 256-bin luminance histogram; C folds it into 4 bins to show contention
#   Histogram: privatised (threadgroup atomics): the same histogram counted per threadgroup in
#     [GroupShared] memory and merged, timed against the global-atomics version; C as above
#   Compaction (atomics): keep the passing elements of 1M through an AtomicAdd counter; Up/Down threshold
#   Bits: popcount, bit reversal, clz/ctz, float bits and unorm8 packing as patterns
#   Indirect (indirect dispatch): compact, then dispatch exactly the survivors; D steps through the full-array alternatives
#   FastMath (compile options): one kernel with and without [ShaderOptions(FastMath = true)]; F toggles
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
