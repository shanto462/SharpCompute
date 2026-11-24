#!/usr/bin/env bash
# Renders the deep-zoom Mandelbrot (perturbation theory, down to a view ~1e-30 high) as an mp4.
#
#   scripts/demo-mandelbrot.sh                       # 60 s at 30 fps, 1280x720
#   scripts/demo-mandelbrot.sh --seconds 128         # the whole loop (zoom to ~1e-30, then fade back)
#   scripts/demo-mandelbrot.sh --size 1920x1080 --no-open --keep-frames
#
# Output: demo-output/mandelbrot-deep-zoom.mp4 (needs ffmpeg: brew install ffmpeg).
# Uses the Gallery app: --shader Mandelbrot --frames N --fps 30.
set -euo pipefail

cd "$(dirname "$0")/.."

SECONDS_TOTAL=60
SIZE="1280x720"
FPS=30
OPEN=1
KEEP=0
OUT="demo-output"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --seconds) SECONDS_TOTAL="$2"; shift ;;
    --size) SIZE="$2"; shift ;;
    --no-open) OPEN=0 ;;
    --keep-frames) KEEP=1 ;;
    -h|--help) sed -n '2,10p' "$0"; exit 0 ;;
    *) echo "unknown option: $1 (see --help)" >&2; exit 2 ;;
  esac
  shift
done

if ! [[ "$SECONDS_TOTAL" =~ ^[0-9]+$ ]] || [[ "$SECONDS_TOTAL" -lt 1 ]]; then
  echo "--seconds expects a positive whole number, got '$SECONDS_TOTAL'" >&2
  exit 2
fi

if ! command -v ffmpeg >/dev/null; then
  echo "ffmpeg was not found on PATH; install it (brew install ffmpeg) to make the mp4." >&2
  exit 1
fi

step() { printf '\n\033[1;36m==> %s\033[0m\n' "$*"; }

FRAMES=$((SECONDS_TOTAL * FPS))
WORK="$OUT/mandelbrot-deep-zoom-frames"
VIDEO="$OUT/mandelbrot-deep-zoom.mp4"

step "Build (Release)"
dotnet build samples/SharpCompute.Samples.Gallery -c Release -v quiet -nologo | grep -E "Warn|Error|error" || true

# Stale frames from a longer earlier run would end up in the video.
rm -rf "$WORK"
mkdir -p "$WORK"

step "Render $FRAMES frames ($SECONDS_TOTAL s at $FPS fps, $SIZE)"
dotnet run -c Release --no-build --project samples/SharpCompute.Samples.Gallery -- \
  --shader Mandelbrot --frames "$FRAMES" --fps "$FPS" --size "$SIZE" --outdir "$WORK"

if [[ ! -f "$WORK/Mandelbrot.mp4" ]]; then
  echo "error: the gallery did not produce $WORK/Mandelbrot.mp4" >&2
  exit 1
fi

mv -f "$WORK/Mandelbrot.mp4" "$VIDEO"

if [[ $KEEP -eq 0 ]]; then
  rm -rf "$WORK"
fi

step "Done: $VIDEO"

if [[ $OPEN -eq 1 ]] && command -v open >/dev/null; then
  open "$VIDEO"
fi
