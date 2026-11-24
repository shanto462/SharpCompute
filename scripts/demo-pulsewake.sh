#!/usr/bin/env bash
# Pulsewake: a neon twin-stick arena shooter drawn by SharpCompute compute shaders (vector glow,
# GPU particles, bloom, shockwave distortion) with GPU-synthesized chiptune audio.
#   WASD move · mouse aims, hold the left button to fire · or arrows aim and fire in eight
#   directions · Space bomb · P pause · M mute · R restart · Esc quit
#
#   scripts/demo-pulsewake.sh                     # play
#   scripts/demo-pulsewake.sh --demo              # watch the autopilot play
#   scripts/demo-pulsewake.sh --mute              # no sound
#   scripts/demo-pulsewake.sh --headless          # no window: PNG frames to demo-output/pulsewake/ and frame times
#   scripts/demo-pulsewake.sh --headless --stress # ...with the 65,536-particle buffer kept full
source "$(dirname "$0")/_common.sh"
build samples/SharpCompute.Samples.Shooter
if [[ "${1:-}" == "--headless" ]]; then
  shift
  mkdir -p demo-output/pulsewake
  step "Pulsewake: headless frames"
  run_project samples/SharpCompute.Samples.Shooter --headless --frames demo-output/pulsewake "$@"
  open_result demo-output/pulsewake
else
  step "Pulsewake (Esc or close the window to exit)"
  run_project samples/SharpCompute.Samples.Shooter "$@"
fi
