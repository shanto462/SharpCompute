#!/usr/bin/env bash
# Records the README's gameplay GIFs: each game plays itself headless (no window, no sound) and
# the frames are encoded with ffmpeg into docs/<game>.gif with a shared 48-color palette.
#   scripts/make-gifs.sh                # all games
#   scripts/make-gifs.sh glowfall       # one game
# Needs ffmpeg (brew install ffmpeg).
source "$(dirname "$0")/_common.sh"
command -v ffmpeg >/dev/null || { echo "ffmpeg not found (brew install ffmpeg)" >&2; exit 1; }

FPS=12
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

# encode <frame pattern> <out.gif> <seconds>
encode() {
  ffmpeg -v error -y -framerate "$FPS" -i "$1" -t "$3" \
    -vf "split[a][b];[a]palettegen=max_colors=48:stats_mode=full[p];[b][p]paletteuse=dither=bayer:bayer_scale=4:diff_mode=rectangle" \
    -loop 0 "$2"
  echo "  $2 ($(( $(stat -f %z "$2") / 1024 )) KB)"
}

if [[ $# -eq 0 || " $* " == *" glowfall "* ]]; then
  build samples/SharpCompute.Samples.Platformer
  step "Glowfall: 10 s of the pilot playing Ember Hollow"
  run_project samples/SharpCompute.Samples.Platformer --headless --frames "$WORK/glowfall" --seconds 10 --fps "$FPS" --level 1
  encode "$WORK/glowfall/glowfall-%04d.png" docs/glowfall.gif 10
fi

if [[ $# -eq 0 || " $* " == *" pulsewake "* ]]; then
  build samples/SharpCompute.Samples.Shooter
  step "Pulsewake: 10 s of the autopilot fighting the first waves"
  run_project samples/SharpCompute.Samples.Shooter --headless --frames "$WORK/pulsewake" --seconds 10 --fps "$FPS"
  encode "$WORK/pulsewake/pulsewake-%04d.png" docs/pulsewake.gif 10
fi
