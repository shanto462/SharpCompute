#!/usr/bin/env bash
# GPU paint app in a window.
#   drag paint · 1–9 colors · H+drag pick color · [ ] or wheel size · ; ' or Shift+wheel hardness · - = opacity · E eraser
#   B blur · K sharpen · D edges · I invert · P posterize · G grayscale · ⌘Z/⇧⌘Z undo/redo · ⌘S save · C clear
#
#   scripts/demo-paint.sh            # paint yourself
#   scripts/demo-paint.sh --replay   # watch the scripted demo painting draw itself, saved to demo-output/paint.png
source "$(dirname "$0")/_common.sh"
build samples/SharpCompute.Samples.Paint
if [[ "${1:-}" == "--replay" ]]; then
  mkdir -p demo-output
  step "Paint: scripted replay"
  SHARPCOMPUTE_PAINT_OUTPUT="$PWD/demo-output/paint.png" run_project samples/SharpCompute.Samples.Paint --script samples/SharpCompute.Samples.Paint/demo-script.json
  open_result demo-output/paint.png
else
  step "Paint (close the window to exit)"
  run_project samples/SharpCompute.Samples.Paint "$@"
fi
