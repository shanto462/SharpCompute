#!/usr/bin/env bash
# Renders the README's screenshots from each demo's deterministic headless output: about 1200 px
# wide (the gallery mosaic 960), as palette PNGs of at most 400,000 bytes, into
# docs/screenshots/<demo>[-<page>].png, and the previews the README's grids show, 400 px wide and at
# most 100,000 bytes, into docs/screenshots/thumbs/.
#   scripts/make-screenshots.sh                     # every demo
#   scripts/make-screenshots.sh computelab paint    # some demos
#   scripts/make-screenshots.sh --list              # the demos
# Needs ffmpeg (brew install ffmpeg); with pngquant installed (brew install pngquant) the palette is
# dithered by pngquant instead. The viewer has no headless mode: it opens its window for a few
# seconds, so run this from a desktop session.
#
# A demo is a function demo_<name> below plus its name in DEMOS. Every ComputeLab page is picked
# up from the lab's headless output, so a new page needs no change here.
source "$(dirname "$0")/_common.sh"
command -v ffmpeg >/dev/null || { echo "ffmpeg not found (brew install ffmpeg)" >&2; exit 1; }

DEMOS=(textures computelab paint viewer imgproc gallery nativeinterop)

OUT=docs/screenshots
WIDTH=1200
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

# shot <source.png> <name> [ffmpeg filters, applied before the scale]: writes $OUT/<name>.png,
# WIDTH wide and at most MAX_PNG_BYTES (see png_fit in _common.sh).
shot() {
  ffmpeg -v error -y -i "$1" -vf "${3:+$3,}scale=$WIDTH:-2:flags=lanczos" "$WORK/$2-full.png"
  png_fit "$WORK/$2-full.png" "$OUT/$2.png" "$MAX_PNG_BYTES"
}

# thumb <name>: the README grids' preview of $OUT/<name>.png, THUMB_WIDTH wide, in $OUT/thumbs/.
thumb() {
  mkdir -p "$OUT/thumbs"
  png_fit "$WORK/$1-full.png" "$OUT/thumbs/$1.png" "$MAX_THUMB_BYTES" "$THUMB_WIDTH"
}

demo_textures() {
  build samples/SharpCompute.Samples.Textures
  step "Textures: the sampler explorer (two formats) and the ping-pong simulation"
  run_project samples/SharpCompute.Samples.Textures --headless --out "$WORK/textures" >/dev/null
  shot "$WORK/textures/samplers-rgba32.png" textures-samplers
  thumb textures-samplers
  shot "$WORK/textures/samplers-rg16.png" textures-format-rg16
  shot "$WORK/textures/pingpong.png" textures-pingpong
}

demo_computelab() {
  build samples/SharpCompute.Samples.ComputeLab
  step "ComputeLab: every page"
  run_project samples/SharpCompute.Samples.ComputeLab --headless --out "$WORK/computelab" >/dev/null
  local page
  for page in "$WORK"/computelab/*.png; do
    page="$(basename "$page" .png)"
    shot "$WORK/computelab/$page.png" "computelab-${page#*-}" # 2-histogram-privatised -> computelab-histogram-privatised
    thumb "computelab-${page#*-}"
  done
}

demo_paint() {
  build samples/SharpCompute.Samples.Paint
  step "Paint: the scripted replay, as the window shows it"
  SHARPCOMPUTE_PAINT_SCRIPT=samples/SharpCompute.Samples.Paint/demo-script.json SHARPCOMPUTE_PAINT_OUTPUT="$WORK/paint.png" \
    run_project samples/SharpCompute.Samples.Paint --headless >/dev/null
  shot "$WORK/paint-view.png" paint
  thumb paint
}

demo_viewer() {
  build samples/SharpCompute.Samples.Viewer
  step "Viewer: the N-body simulation after 240 frames (opens the window for a few seconds)"
  SHARPCOMPUTE_VIEWER_SCREENSHOT="$WORK/nbody.png" SHARPCOMPUTE_VIEWER_FRAMES=240 \
    run_project samples/SharpCompute.Samples.Viewer --shader NBody >/dev/null
  shot "$WORK/nbody.png" viewer-nbody "crop=1040:585"
  thumb viewer-nbody
}

demo_imgproc() {
  build samples/SharpCompute.Samples.ImageProcessing
  step "ImageProcessing: the 4K test image and three filters of it"
  local imgproc=samples/SharpCompute.Samples.ImageProcessing input="$WORK/imgproc-test.png"
  run_project "$imgproc" --make-test-image "$input" 3840x2160 >/dev/null
  run_project "$imgproc" "$input" "$WORK/imgproc-bokeh.png" bokeh 12 >/dev/null
  run_project "$imgproc" "$input" "$WORK/imgproc-edges.png" edges >/dev/null
  run_project "$imgproc" "$input" "$WORK/imgproc-grade.png" grade 0.2 1.15 1.2 >/dev/null
  # 2 x 2: the input | bokeh / edges | grade.
  ffmpeg -v error -y -i "$input" -i "$WORK/imgproc-bokeh.png" -i "$WORK/imgproc-edges.png" -i "$WORK/imgproc-grade.png" \
    -filter_complex "[0][1][2][3]xstack=inputs=4:layout=0_0|w0_0|0_h0|w0_h0" "$WORK/imgproc.png"
  shot "$WORK/imgproc.png" imgproc
  thumb imgproc
}

# The nine shaders of the gallery mosaic, each at a moment that shows it well. The mosaic is 960 px
# wide: nine detailed shaders at 1200 px do not fit the size limit without heavy banding.
GALLERY=(Octagrams:2 ProteanClouds:2 MengerJourney:2 TerracedHills:2 Mandelbrot:20 ExtrudedTruchetPattern:2 TriangleGridContouring:2 PyramidPattern:2 TwoTiledTruchet:2)

demo_gallery() {
  build samples/SharpCompute.Samples.Gallery
  step "Gallery: nine shaders in a 3 x 3 mosaic"
  local entry inputs=() layout=() i=0
  for entry in "${GALLERY[@]}"; do
    run_project samples/SharpCompute.Samples.Gallery --shader "${entry%%:*}" --time "${entry#*:}" --size 400x225 --out "$WORK/gallery-$i.png" >/dev/null
    inputs+=(-i "$WORK/gallery-$i.png")
    layout+=("$((i % 3 * 400))_$((i / 3 * 225))")
    i=$((i + 1))
  done
  ffmpeg -v error -y "${inputs[@]}" -filter_complex "xstack=inputs=9:layout=$(IFS='|'; echo "${layout[*]}")" "$WORK/gallery.png"
  WIDTH=960 shot "$WORK/gallery.png" gallery
  thumb gallery
}

demo_nativeinterop() {
  build samples/SharpCompute.Samples.NativeInterop
  step "NativeInterop: the host renderer with both SharpCompute passes (its headless frame)"
  run_project samples/SharpCompute.Samples.NativeInterop --screenshot "$WORK/nativeinterop.png" --size 1200x675 >/dev/null
  shot "$WORK/nativeinterop.png" nativeinterop
  thumb nativeinterop
}

if [[ "${1:-}" == "--list" ]]; then
  printf '%s\n' "${DEMOS[@]}"
  exit 0
fi

SELECTED=("$@")
[[ ${#SELECTED[@]} -gt 0 ]] || SELECTED=("${DEMOS[@]}")
mkdir -p "$OUT"

for demo in "${SELECTED[@]}"; do
  [[ " ${DEMOS[*]} " == *" $demo "* ]] || { echo "unknown demo '$demo' (--list shows them)" >&2; exit 2; }
  "demo_$demo"
done
