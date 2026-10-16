# Marines vs Aliens

Turn-based squad tactics, a Go/Ebitengine port of the original Unity game. Everything lives in `game/`.

- Run: `cd game && go run .` (`-debug` for generator overlays, `-server URL` for another lobby)
- Test: `cd game && go test .`
- Web build: `game/build_web.sh`, publish with `game/publish_web.sh` (https://ruzzoli.de/games/marines-vs-aliens/play/)
- Online relay: `game/server`, deploy with `game/server/deploy.sh` (https://ruzzoli.de/mva-battles/)
- Rules and stats: `game/assets/percent.rul`
