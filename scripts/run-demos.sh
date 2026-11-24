#!/usr/bin/env bash
# Runs every SharpCompute demo and collects the output in one folder.
#
#   scripts/run-demos.sh            # samples + gallery stills + benchmark, then open the folder
#   scripts/run-demos.sh --video    # also a 4 s clip per gallery shader + a 12 s Mandelbrot zoom (mp4 needs ffmpeg)
#   scripts/run-demos.sh --test     # run the full test suite first
#   scripts/run-demos.sh --size 1920x1080 --out my-folder --no-open
#
# Gallery shaders are discovered from the registry (`--list`), so newly ported shaders are
# picked up automatically.
set -euo pipefail

cd "$(dirname "$0")/.."

OUT="demo-output"
SIZE="1280x720"
TIME="2"
VIDEO=0
TEST=0
OPEN=1

while [[ $# -gt 0 ]]; do
  case "$1" in
    --video) VIDEO=1 ;;
    --test) TEST=1 ;;
    --no-open) OPEN=0 ;;
    --size) SIZE="$2"; shift ;;
    --time) TIME="$2"; shift ;;
    --out) OUT="$2"; shift ;;
    -h|--help) sed -n '2,10p' "$0"; exit 0 ;;
    *) echo "unknown option: $1 (see --help)" >&2; exit 2 ;;
  esac
  shift
done

step() { printf '\n\033[1;36m==> %s\033[0m\n' "$*"; }

mkdir -p "$OUT"
OUT="$(cd "$OUT" && pwd)"

step "Build (Release)"
dotnet build SharpCompute.slnx -c Release -v quiet -nologo | grep -E "Warn|Error|error" || true

if [[ $TEST -eq 1 ]]; then
  step "Tests"
  dotnet test SharpCompute.slnx -c Release --no-build -v quiet -nologo | grep -E "Passed!|Failed!|error" || true
fi

GALLERY=(dotnet run -c Release --no-build --project samples/SharpCompute.Samples.Gallery --)

step "HelloWorld sample: MultiplyByTwo, SAXPY, Mandelbrot"
dotnet run -c Release --no-build --project samples/SharpCompute.Samples.HelloWorld -- "$OUT/mandelbrot.png"

step "Gallery shaders"
"${GALLERY[@]}" --list

step "Gallery stills ($SIZE, t=$TIME)"
"${GALLERY[@]}" --all --time "$TIME" --size "$SIZE" --outdir "$OUT/gallery"

if [[ $VIDEO -eq 1 ]]; then
  step "Gallery clips (4 s at 30 fps, $SIZE)"
  "${GALLERY[@]}" --all --frames 120 --fps 30 --size "$SIZE" --outdir "$OUT/video"
  step "Mandelbrot zoom (12 s at 30 fps, $SIZE)"
  "${GALLERY[@]}" --shader Mandelbrot --frames 360 --fps 30 --size "$SIZE" --outdir "$OUT/video"
  command -v ffmpeg >/dev/null || echo "ffmpeg not found: PNG frames only (brew install ffmpeg for mp4)"
fi

step "Benchmark ($SIZE)"
"${GALLERY[@]}" --bench --frames 100 --size "$SIZE" | tee "$OUT/bench.txt"

IMGPROC=(dotnet run -c Release --no-build --project samples/SharpCompute.Samples.ImageProcessing --)
mkdir -p "$OUT/imgproc"

step "ImageProcessing: GPU vs CPU (Parallel.For) on a generated 3840×2160 image"
"${IMGPROC[@]}" --make-test-image "$OUT/imgproc/test-3840x2160.png" 3840x2160
"${IMGPROC[@]}" "$OUT/imgproc/test-3840x2160.png" "$OUT/imgproc/blur-8.png" blur 8 --compare | tee "$OUT/imgproc/compare.txt"
"${IMGPROC[@]}" "$OUT/imgproc/test-3840x2160.png" "$OUT/imgproc/bokeh-12.png" bokeh 12 --compare | tee -a "$OUT/imgproc/compare.txt"

step "Pipeline: GPU renders frame N+1 (ForAsync) while the CPU post-processes frame N"
mkdir -p "$OUT/pipeline"
dotnet run -c Release --no-build --project samples/SharpCompute.Samples.Pipeline -- --out "$OUT/pipeline" | tee "$OUT/pipeline/pipeline.txt"

step "Audio: GPU-synthesized sound effects and a chiptune loop, as WAV files"
dotnet run -c Release --no-build --project samples/SharpCompute.Samples.Audio.Demo -- --wav "$OUT/audio"

step "Glowfall: the platformer plays itself headless, frames as PNG"
mkdir -p "$OUT/glowfall"
dotnet run -c Release --no-build --project samples/SharpCompute.Samples.Platformer -- --check-levels | tee "$OUT/glowfall/levels.txt"
dotnet run -c Release --no-build --project samples/SharpCompute.Samples.Platformer -- --headless --frames "$OUT/glowfall" --seconds 8 | tee -a "$OUT/glowfall/levels.txt"

step "Paint: scripted replay (headless)"
SHARPCOMPUTE_PAINT_SCRIPT=samples/SharpCompute.Samples.Paint/demo-script.json SHARPCOMPUTE_PAINT_OUTPUT="$OUT/paint.png" \
  dotnet run -c Release --no-build --project samples/SharpCompute.Samples.Paint -- --headless

step "Done: $OUT"
find "$OUT" -maxdepth 2 \( -name '*.png' -o -name '*.mp4' -o -name '*.txt' \) -not -path '*/video/*/*' | sed "s#^$OUT/#  #" | sort

if [[ $OPEN -eq 1 ]] && command -v open >/dev/null; then
  open "$OUT"
fi
