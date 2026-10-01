#!/bin/sh
# Builds the browser version into the fx-games site. Assets are already go:embed'ed, so no extra step.
set -e
cd "$(dirname "$0")"
OUT=${OUT:-/Users/felix/Projects/fx-games/site/marines-vs-aliens/play}
mkdir -p "$OUT"
GOOS=js GOARCH=wasm go build -trimpath -ldflags="-s -w" -o "$OUT/marines.wasm" .
gzip -9 -kf "$OUT/marines.wasm"
cp "$(go env GOROOT)/lib/wasm/wasm_exec.js" "$OUT/"
