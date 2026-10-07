# Marines vs Aliens — TODO

Go/Ebiten port lives in `game/`. Run with `cd game && go run .` (`-debug` shows map generator overlays, cursor coordinates and FPS), test with `go test .`.

## Port housekeeping
- [x] Commit the Go port
- [x] Unity exporter script was not kept; `game/ui.json` is the frozen export
- [x] Copy the assets the game uses into `game/assets` (embedded in the binary; sources in `assets/CREDITS.md`)
- [x] Check the licences in `assets/CREDITS.md` before distributing a build
- [ ] Side-by-side check against a Unity build (text layout, wall damage sprites) — needs someone with Unity
- [ ] Delete the Unity project once the port is accepted (this also drops the obsolete `core.rul`)

## Bugs (fixed in the port)
- [x] Movement reached one step further than AP paid for
- [x] One buff instance was shared by all units hit by a skill
- [x] Selection marker never turned red / gray (checks were in the wrong order)
- [x] Explosions only breached straight wall pieces; corners and rock walls now break into rubble
- [x] AI used the human player's fog; it now only knows what its own units saw
- [x] Removing eliminated players could shift whose turn is next; a player losing its last unit mid-turn now ends that turn
- [x] Right-click always deselected; it now steps back (targeting → selection, locked-in move → preview → deselect)
- [x] Map generator debug output (room colours, lines, distances) and cursor coordinates/FPS only with `-debug`
- [x] Removed the 5 s movement trail (it also showed hidden enemy movement)
- [x] Hovering a unit hidden in the fog showed its stats
- [x] Dungeon generator could stop merging regions early (duplicate region ids)

## Game loop
- [x] Win/lose screen ("MISSION COMPLETE / FAILED", "<player> WINS"), click to continue
- [x] Way back to the main menu (end screen, or Esc twice)
- [x] Turn / current player indicator (top left)
- [x] Fog = union of the team's fields of view, cached per team until one of its units moves (or a door/wall/unit changes); no explored-tile memory
- [x] Mission goal stays elimination; the generator's start / key / end rooms are only a `-debug` overlay
- [x] Campaign: missions play in order, next mission after a win
- [x] Removed the unfinished map block `QUADRANT_ONE`; mission two uses the dungeon generator

## Balance & design (to discuss)
- [x] Factions differ by signature weapon: Birds of Prey Hunter has the Laser Rifle, Legion Rogue the Mechanic Punch (4–5), Travellers Engineer the Plasma Gun; drones use Pummel
- [x] A hit always deals at least 1 damage (rule: there is always a way to deal damage); AI matches no longer stall
- [x] Skills cost AP (`apCost` in the rules; attacks default to the weapon's cost, everything else to 1)
- [x] Unused content now in use (see factions); Shredder buff and multi-tile units dropped
- [x] Missile Launcher weapon lost `radius`: the plain shot hits one target, only the Rocket Launcher skill explodes
- [x] Hot-seat: the handover screen hides the whole map until the next player clicks
- [ ] Online play (after campaign and hot-seat are solid)
- [x] Touch: one finger = click/drag, two-finger tap = back (right-click), pinch = zoom

## AI
- [x] Separate `Controller` / `World` interfaces (`ai.go`); `TacticalAI` is one implementation
- [x] Pick targets by expected damage (hit chance × damage through armour, bonus for kills)
- [x] Choose positions by attack chances vs. exposure to enemy fire; units that can't hurt anyone make room
- [x] Use skills (Aimed Shot, Stun Strike, Deploy Drone (one drone per team), Rocket Launcher with friendly-fire check)
- [x] Retreat when hurt and exposed
- [x] Hunt: go to tiles not seen for longest when no enemy is visible
- [x] Focus fire (wounded targets score higher) and no parking in corridors

## UI & feedback
- [x] Hidden AI turns fast-forward; Tab cycles units with AP; unaffordable or targetless actions are grayed with AP cost pips; tooltips explain targeting and why a tile can't be targeted
- [x] All rules and stats live in `percent.rul` (`options:` block, enum names); walls have 4 HP
- [x] Clear unit info (HP, AP, armour, accuracy, buffs) and AP pips on own units
- [x] Hit-chance breakdown when hovering a target during targeting
- [x] Undo a locked-in move preview (right-click / Esc)
- [x] Keys: F1 help/tutorial, F11 fullscreen, M music, -/= volume; menu and battle music, win/lose jingles
- [x] Hit chance shown over every target before committing ("100%" for explosions)
- [x] Per-weapon projectiles (`projectile:` in the rules) and flash effects for Stun Strike, Aimed Shot, Deploy Drone
- [ ] Dedicated sprites for melee variants, plasma beam, drone deploy
- [x] Volume, music and fullscreen saved to the user config dir (desktop; web starts with defaults)
- [ ] Settings screen (keys only for now)

## Unity dead code
- [x] Cavern smoothing passes, dungeon (maze) generator and map scripts wired up (map generator dropdown: BSP, Cellular Automata, Dungeon; rules `generator: dungeon` etc.)
- [x] Weapon rules `multipleTargets`, `damageDistribution`, `damageLineOfFire`, `radius` now work
- [ ] Dice combat mode — only an enum in the original, nothing to port
