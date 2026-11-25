# Shared helpers for the demo scripts (sourced, not executed).
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/.."

step() { printf '\n\033[1;36m==> %s\033[0m\n' "$*"; }

# Builds the given project (Release) quietly; fails loudly on errors.
build() {
  step "Build $1 (Release)"
  dotnet build "$1" -c Release -v quiet -nologo | grep -E "error|Warn|Error" || true
}

# Runs a project that was just built: run_project <project dir> [args...]
run_project() {
  local project="$1"; shift
  dotnet run -c Release --no-build --project "$project" -- "$@"
}

# Opens a file or folder in Finder/Preview unless NO_OPEN=1.
open_result() {
  if [[ "${NO_OPEN:-0}" != 1 ]] && command -v open >/dev/null; then open "$1"; fi
}

# The README's images (scripts/make-screenshots.sh, scripts/make-gifs.sh): byte limits that the README
# states and ReadmeImageTests checks.
MAX_PNG_BYTES=400000
MAX_THUMB_BYTES=100000
MAX_GIF_BYTES=2000000
THUMB_WIDTH=400

# png_fit <in.png> <out.png> <max bytes> [width]: scales to the width (if given) and writes an opaque
# palette PNG (transparency would show GitHub's dark theme through it) with the best palette that fits:
# 256 dithered colors, else 256 plain, else 128 plain. pngquant does the palette when it is installed.
png_fit() {
  local scaled="${TMPDIR:-/tmp}/png_fit-$$-$RANDOM.png"
  ffmpeg -v error -y -i "$1" -vf "${4:+scale=$4:-2:flags=lanczos,}format=rgb24" "$scaled"
  local attempt colors dither
  for attempt in "256 1" "256 0" "128 0"; do
    read -r colors dither <<<"$attempt"
    if command -v pngquant >/dev/null; then
      local floyd=(--floyd=1); [[ $dither -eq 1 ]] || floyd=(--nofs)
      pngquant --force --speed 1 --strip "${floyd[@]}" --output "$2" "$colors" -- "$scaled"
    else
      local mode=sierra2_4a; [[ $dither -eq 1 ]] || mode=none
      ffmpeg -v error -y -i "$scaled" -vf "split[a][b];[a]palettegen=max_colors=$colors:reserve_transparent=0:stats_mode=full[p];[b][p]paletteuse=dither=$mode" "$2"
    fi
    (($(stat -f %z "$2") > $3)) || break
  done
  rm -f "$scaled"
  local bytes; bytes=$(stat -f %z "$2")
  ((bytes <= $3)) || { echo "error: $2 is $bytes bytes, over $3" >&2; exit 1; }
  echo "  $2 ($bytes bytes)"
}
