#!/usr/bin/env bash
# Renders the shader gallery to PNG (and optionally mp4), then opens the folder.
#
#   scripts/demo-gallery.sh                     # every shader as a 1280×720 PNG
#   scripts/demo-gallery.sh --video             # plus a 4 s mp4 per shader (needs ffmpeg)
#   scripts/demo-gallery.sh --shader Octagrams  # just one shader (PNG + mp4 with --video)
#   scripts/demo-gallery.sh --list
source "$(dirname "$0")/_common.sh"
build samples/SharpCompute.Samples.Gallery
OUT=demo-output/gallery; mkdir -p "$OUT"
VIDEO=0; SHADER=""; SIZE="1280x720"
while [[ $# -gt 0 ]]; do
  case "$1" in
    --video) VIDEO=1 ;;
    --shader) SHADER="$2"; shift ;;
    --size) SIZE="$2"; shift ;;
    --list) run_project samples/SharpCompute.Samples.Gallery --list; exit 0 ;;
    *) echo "unknown option: $1" >&2; exit 2 ;;
  esac
  shift
done
TARGET=(--all); [[ -n "$SHADER" ]] && TARGET=(--shader "$SHADER")
step "Stills ($SIZE)"
if [[ -n "$SHADER" ]]; then
  run_project samples/SharpCompute.Samples.Gallery --shader "$SHADER" --time 2 --size "$SIZE" --out "$OUT/$SHADER.png"
else
  run_project samples/SharpCompute.Samples.Gallery --all --time 2 --size "$SIZE" --outdir "$OUT"
fi
if [[ $VIDEO -eq 1 ]]; then
  step "Clips (4 s at 30 fps)"
  run_project samples/SharpCompute.Samples.Gallery "${TARGET[@]}" --frames 120 --fps 30 --size "$SIZE" --outdir "$OUT/video"
fi
step "Benchmark"
BENCH=(--bench --frames 60 --size "$SIZE"); [[ -n "$SHADER" ]] && BENCH+=(--shader "$SHADER")
run_project samples/SharpCompute.Samples.Gallery "${BENCH[@]}"
open_result "$OUT"
