#!/usr/bin/env bash
# Glowfall: a 2D platformer rendered by SharpCompute shaders (GPU lighting, shadows, particles,
# GPU-synthesized chiptune audio).
#   arrows / A D move · Z Space Up W jump (hold for higher) · Down S drop through ledges
#   R restart level · M mute · Esc quit
#
#   scripts/demo-glowfall.sh                 # play
#   scripts/demo-glowfall.sh --level 2       # start on level 2 (1-3)
#   scripts/demo-glowfall.sh --demo          # watch a simple pilot play
#   scripts/demo-glowfall.sh --mute          # no sound
#   scripts/demo-glowfall.sh --headless      # no window: render PNG frames to demo-output/glowfall/
#   scripts/demo-glowfall.sh --check-levels  # prove every level can be finished (real physics)
source "$(dirname "$0")/_common.sh"
build samples/SharpCompute.Samples.Platformer
if [[ "${1:-}" == "--headless" ]]; then
  shift
  mkdir -p demo-output/glowfall
  step "Glowfall: headless frames"
  run_project samples/SharpCompute.Samples.Platformer --headless --frames demo-output/glowfall "$@"
  open_result demo-output/glowfall
else
  step "Glowfall (Esc or close the window to exit)"
  run_project samples/SharpCompute.Samples.Platformer "$@"
fi
