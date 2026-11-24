#!/usr/bin/env bash
# GPU image filters vs the CPU, on your photo or a generated 4K test image.
#
#   scripts/demo-imgproc.sh                               # 4K test image: blur 8, bokeh 12, sharpen, edges, grade
#   scripts/demo-imgproc.sh ~/Pictures/photo.heic         # same filters on your photo
#   scripts/demo-imgproc.sh ~/Pictures/photo.jpg bokeh 20 # one filter with arguments
# Outputs go to demo-output/imgproc/; each run prints GPU kernel, copies, CPU and both speedups.
source "$(dirname "$0")/_common.sh"
build samples/SharpCompute.Samples.ImageProcessing
OUT=demo-output/imgproc; mkdir -p "$OUT"
INPUT="${1:-}"
if [[ -z "$INPUT" ]]; then
  INPUT="$OUT/test-3840x2160.png"
  step "Generate a 3840×2160 test image"
  run_project samples/SharpCompute.Samples.ImageProcessing --make-test-image "$INPUT" 3840x2160
fi
if [[ $# -ge 2 ]]; then
  FILTERS=("${*:2}")
else
  FILTERS=("blur 8" "bokeh 12" "sharpen 1.5" "edges" "grade 0.2 1.15 1.2")
fi
for f in "${FILTERS[@]}"; do
  name="${f// /_}"
  step "$f"
  # shellcheck disable=SC2086
  run_project samples/SharpCompute.Samples.ImageProcessing "$INPUT" "$OUT/$name.png" $f --compare
done
open_result "$OUT"
