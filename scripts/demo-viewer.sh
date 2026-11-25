#!/usr/bin/env bash
# Live gallery in a window: all gallery shaders + Game of Life + N-body.
#   ←/→ switch · Space pause · S save PNG · F toggle size · Esc quit
#   Game of Life: drag to draw (right-drag erases), R random, C clear · N-body: drag to orbit, wheel to zoom
source "$(dirname "$0")/_common.sh"
build samples/SharpCompute.Samples.Viewer
step "Viewer (close the window or press Esc to exit)"
run_project samples/SharpCompute.Samples.Viewer "$@"
