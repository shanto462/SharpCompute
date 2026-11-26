#!/usr/bin/env bash
# The long mode of the random-expression differential test (tests/SharpCompute.Differential.Tests):
# N random expressions from seed S, built in batches through the real source generator, run on the
# GPU and with ForCpu, and compared as the default mode does. Each mismatch is shrunk while it still
# mismatches and printed as a self-contained repro shader; the script exits 1 if there was one.
#
#   scripts/fuzz-differential.sh --seed S --count N [--batch B] [--keep]
#
# It keeps the GPU and every CPU core busy for minutes. Where heavy local jobs share a machine, set
# SHARPCOMPUTE_RUN_LOCK to a directory path that all of them use as a lock: the script waits until it
# can create that directory and removes it when it exits.
source "$(dirname "$0")/_common.sh"

if [[ -n "${SHARPCOMPUTE_RUN_LOCK:-}" ]]; then
  step "Wait for the lock $SHARPCOMPUTE_RUN_LOCK"
  until mkdir "$SHARPCOMPUTE_RUN_LOCK" 2>/dev/null; do sleep 20; done
  trap 'rmdir "$SHARPCOMPUTE_RUN_LOCK"' EXIT
fi

step "Fuzz: GPU against ForCpu"
python3 -I tools/gen_differential.py fuzz "$@"
