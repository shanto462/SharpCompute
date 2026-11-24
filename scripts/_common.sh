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
