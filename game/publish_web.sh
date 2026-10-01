#!/usr/bin/env bash
# Build the browser version and publish it to https://ruzzoli.de/games/marines-vs-aliens/play/
set -euo pipefail
cd "$(dirname "$0")"
./build_web.sh
rsync -az --delete /Users/felix/Projects/fx-games/site/marines-vs-aliens/play/ ruzzoli.de:/var/www/ruzzoli.de/games/marines-vs-aliens/play/
echo "published: https://ruzzoli.de/games/marines-vs-aliens/play/"
