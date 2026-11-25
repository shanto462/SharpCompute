#!/usr/bin/env bash
# Textures in shaders, live: a sampler explorer and a ping-pong simulation.
#   Tab switches between the two · S save PNG · Esc quit
#   Samplers:  one picture magnified through all six samplers (Point/Linear x Clamp/Repeat/Mirror)
#              F next texture format (Shift+F previous) · +/- zoom · drag pans · right-drag zooms ·
#              R toggles a slow rotation · 0 resets the view
#   Ping-pong: a reaction-diffusion between two read-write textures, sampled through AsReadOnly()
#              drag paints · right-drag erases · +/- zoom (zoomed out, the field tiles) ·
#              Space pauses · C restarts
#
#   scripts/demo-textures.sh                    # the window
#   scripts/demo-textures.sh --mode pingpong    # start in the ping-pong mode
#   scripts/demo-textures.sh --format 2         # start with the second format (Rgba32Srgb)
#   scripts/demo-textures.sh --headless         # no window: one PNG per format and mode in demo-output/textures/
source "$(dirname "$0")/_common.sh"
build samples/SharpCompute.Samples.Textures
if [[ "${1:-}" == "--headless" ]]; then
  shift
  mkdir -p demo-output/textures
  step "Textures: headless frames"
  run_project samples/SharpCompute.Samples.Textures --headless --out demo-output/textures "$@"
  open_result demo-output/textures
else
  step "Textures (Esc or close the window to exit)"
  run_project samples/SharpCompute.Samples.Textures "$@"
fi
