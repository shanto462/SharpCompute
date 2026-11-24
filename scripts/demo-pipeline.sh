#!/usr/bin/env bash
# Double-buffered GPU → CPU pipeline: the GPU renders Mandelbrot frame N+1 (ForAsync) while the CPU
# post-processes frame N, compared with the synchronous render-then-process loop.
#
#   scripts/demo-pipeline.sh                            # 120 frames at 1920x1080
#   scripts/demo-pipeline.sh --frames 300 --size 3840x2160
# Prints per-stage and sync vs async timings; saves the last frame to demo-output/pipeline/.
source "$(dirname "$0")/_common.sh"
build samples/SharpCompute.Samples.Pipeline
OUT=demo-output/pipeline; mkdir -p "$OUT"
step "Pipeline: sync vs async (double-buffered)"
run_project samples/SharpCompute.Samples.Pipeline --out "$OUT" "$@" | tee "$OUT/pipeline.txt"
open_result "$OUT/pipeline-last-frame.png"
