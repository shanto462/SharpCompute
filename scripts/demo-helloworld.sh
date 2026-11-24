#!/usr/bin/env bash
# The basics: MultiplyByTwo, SAXPY over 1M floats, and a 1920×1080 Mandelbrot PNG.
source "$(dirname "$0")/_common.sh"
build samples/SharpCompute.Samples.HelloWorld
mkdir -p demo-output
step "HelloWorld"
run_project samples/SharpCompute.Samples.HelloWorld demo-output/mandelbrot.png
open_result demo-output/mandelbrot.png
