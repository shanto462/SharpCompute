#!/usr/bin/env bash
# SharpCompute inside an app that already renders with Metal. A small host renderer, written straight
# against Metal through objc_msgSend, owns the device, the queue, a particle buffer and its textures,
# and draws a textured quad and an instanced particle field with its own render pipeline. SharpCompute
# wraps those objects (GraphicsDevice.FromNative, WrapNativeReadWriteBuffer, WrapNativeReadWriteTexture2D)
# and encodes a particle simulation and a post-process pass (ripples, color split, bloom) into the
# host's own command buffer with ComputeContext.EncodeInto, between the host's passes. The title bar
# shows fps and the GPU time of the whole frame.
#   1 toggles the particle simulation · 2 toggles the post-process · click or drag moves the ripples ·
#   S save PNG · Esc quit
#
#   scripts/demo-nativeinterop.sh               # the window
#   scripts/demo-nativeinterop.sh --headless    # no window: the same four frames every run (both passes, each
#                                               # pass off, host only) as PNGs in demo-output/nativeinterop/
source "$(dirname "$0")/_common.sh"
build samples/SharpCompute.Samples.NativeInterop
if [[ "${1:-}" == "--headless" ]]; then
  shift
  step "NativeInterop: headless frames"
  run_project samples/SharpCompute.Samples.NativeInterop --headless --out demo-output/nativeinterop "$@"
  open_result demo-output/nativeinterop
else
  step "NativeInterop (Esc or close the window to exit)"
  run_project samples/SharpCompute.Samples.NativeInterop "$@"
fi
