#!/usr/bin/env bash
# GPU-synthesized chiptune audio: renders a few sound effects and a short original loop with
# compute shaders (one GPU thread per sample), then plays them through AudioToolbox for ~10 s at
# half volume.
#
#   scripts/demo-audio.sh                     # play: SFX one by one, then the loop with SFX on top
#   scripts/demo-audio.sh --seconds 6         # play for 6 s
#   scripts/demo-audio.sh --volume 0.3        # quieter (at most 0.5)
#   scripts/demo-audio.sh --wav demo-output/audio   # write WAV files instead of playing
# Without an audio output device it writes the WAV files to demo-output/audio/.
source "$(dirname "$0")/_common.sh"
build samples/SharpCompute.Samples.Audio.Demo
step "Audio: GPU chiptune synthesis"
run_project samples/SharpCompute.Samples.Audio.Demo "$@"

for ((i = 1; i <= $#; i++)); do
  if [[ "${!i}" == --wav ]]; then
    next=$((i + 1))
    open_result "${!next}"
  fi
done
