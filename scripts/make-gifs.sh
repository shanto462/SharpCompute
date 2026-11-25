#!/usr/bin/env bash
# Records the README's GIFs: each game plays itself headless (no window, no sound), and the demos
# whose point is motion record their frames headless; ffmpeg encodes the frames with a shared
# palette (48 colors unless a GIF says otherwise) into docs/<game>.gif and docs/screenshots/<demo>.gif,
# each at most 2,000,000 bytes. A game also gets a still frame as its preview in the README's demo
# grid, docs/screenshots/thumbs/<game>.png.
#   scripts/make-gifs.sh                          # all of them
#   scripts/make-gifs.sh glowfall                 # one: glowfall, pulsewake, computelab-histogram, textures-pingpong
# Needs ffmpeg (brew install ffmpeg).
source "$(dirname "$0")/_common.sh"
command -v ffmpeg >/dev/null || { echo "ffmpeg not found (brew install ffmpeg)" >&2; exit 1; }

FPS=12
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

# [COLORS=n] encode <frame pattern> <out.gif> <seconds> [width] [frame rate of the frames] [frame rate of the GIF]
encode() {
  local filters="${6:+fps=$6,}${4:+scale=$4:-2:flags=lanczos,}"
  ffmpeg -v error -y -framerate "${5:-$FPS}" -i "$1" -t "$3" \
    -vf "${filters}split[a][b];[a]palettegen=max_colors=${COLORS:-48}:stats_mode=full[p];[b][p]paletteuse=dither=bayer:bayer_scale=4:diff_mode=rectangle" \
    -loop 0 "$2"
  local bytes; bytes=$(stat -f %z "$2")
  ((bytes <= MAX_GIF_BYTES)) || { echo "error: $2 is $bytes bytes, over $MAX_GIF_BYTES" >&2; exit 1; }
  echo "  $2 ($bytes bytes)"
}

# still <frame.png> <name>: a frame of a game as its preview in the demo grid.
still() {
  mkdir -p docs/screenshots/thumbs
  png_fit "$1" "docs/screenshots/thumbs/$2.png" "$MAX_THUMB_BYTES" "$THUMB_WIDTH"
}

if [[ $# -eq 0 || " $* " == *" glowfall "* ]]; then
  build samples/SharpCompute.Samples.Platformer
  step "Glowfall: 6 s of the pilot playing Ember Hollow, up to the first lantern"
  run_project samples/SharpCompute.Samples.Platformer --headless --frames "$WORK/glowfall" --seconds 6 --fps "$FPS" --level 1
  COLORS=40 encode "$WORK/glowfall/glowfall-%04d.png" docs/glowfall.gif 6
  still "$WORK/glowfall/glowfall-0041.png" glowfall # the lantern just lit
fi

if [[ $# -eq 0 || " $* " == *" pulsewake "* ]]; then
  build samples/SharpCompute.Samples.Shooter
  step "Pulsewake: 8 s of the autopilot fighting the first wave"
  run_project samples/SharpCompute.Samples.Shooter --headless --frames "$WORK/pulsewake" --seconds 8 --fps "$FPS"
  COLORS=32 encode "$WORK/pulsewake/pulsewake-%04d.png" docs/pulsewake.gif 8
  still "$WORK/pulsewake/pulsewake-0061.png" pulsewake # a wave breaking up
fi

if [[ $# -eq 0 || " $* " == *" computelab-histogram "* ]]; then
  build samples/SharpCompute.Samples.ComputeLab
  step "ComputeLab: 6 s of the Histogram page, the image moving and its histogram following"
  run_project samples/SharpCompute.Samples.ComputeLab --headless --page histogram --record "$WORK/computelab" --seconds 6 --fps "$FPS"
  encode "$WORK/computelab/histogram-%04d.png" docs/screenshots/computelab-histogram.gif 6 600
fi

if [[ $# -eq 0 || " $* " == *" textures-pingpong "* ]]; then
  build samples/SharpCompute.Samples.Textures
  step "Textures: the ping-pong reaction-diffusion growing from a painted stroke, 150 frames"
  run_project samples/SharpCompute.Samples.Textures --headless --out "$WORK/textures" --record
  encode "$WORK/textures/pingpong-%04d.png" docs/screenshots/textures-pingpong.gif 6 480 25 12.5
fi
