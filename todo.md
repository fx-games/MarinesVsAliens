# Marines vs Aliens — TODO

Go/Ebiten port lives in `game/`. Run with `cd game && go run .` (`-debug` shows map generator overlays, cursor coordinates and FPS), test with `go test .`.

## Port housekeeping
- [ ] Commit the Go port
- [ ] Commit the Unity exporter script (scene/prefab/font → `game/ui.json`) or decide the Unity project is frozen
- [x] Copy the assets the game uses into `game/assets` (embedded in the binary; sources in `assets/CREDITS.md`)
- [ ] Check the licences in `assets/CREDITS.md` before distributing a build (paid Oryx sprites and fonts; CC BY music needs attribution)
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
- [ ] Use the generator's start / key / end rooms for mission goals, or drop them — see design questions
- [x] Campaign: missions play in order, next mission after a win
- [ ] Mission two's map block `QUADRANT_ONE` in `percent.rul` is unfinished (sealed 3×3 rooms, double walls); mission two uses the dungeon generator until it's redesigned

## Balance & design (to discuss)
- [ ] Make factions different (all four have identical units and stats)
- [x] A hit always deals at least 1 damage (rule: there is always a way to deal damage); AI matches no longer stall
- [x] Skills cost AP (`apCost` in the rules; attacks default to the weapon's cost, everything else to 1)
- [ ] Use or remove unused content: Plasma Gun, Laser Rifle, Heavy Hitter, Pummel, Shredder buff, multi-tile units
- [ ] Rocket launcher weapon (radius 1) now makes the Heavy's normal ranged attack an area attack, overlapping the Rocket Launcher skill
- [x] Hot-seat: the handover screen hides the whole map until the next player clicks
- [ ] Online play (after campaign and hot-seat are solid)

## AI
- [x] Separate `Controller` / `World` interfaces (`ai.go`); `TacticalAI` is one implementation
- [x] Pick targets by expected damage (hit chance × damage through armour, bonus for kills)
- [x] Choose positions by attack chances vs. exposure to enemy fire; units that can't hurt anyone make room
- [x] Use skills (Aimed Shot, Stun Strike, Deploy Drone (one drone per team), Rocket Launcher with friendly-fire check)
- [x] Retreat when hurt and exposed
- [x] Hunt: go to tiles not seen for longest when no enemy is visible
- [ ] Coordinate units (focus fire, don't block corridors) — remaining cause of AI stalls

## UI & feedback
- [x] Clear unit info (HP, AP, armour, accuracy, buffs) and AP pips on own units
- [x] Hit-chance breakdown when hovering a target during targeting
- [x] Undo a locked-in move preview (right-click / Esc)
- [x] Keys: F1 help/tutorial, F11 fullscreen, M music, -/= volume; menu and battle music, win/lose jingles
- [x] Hit chance shown over every target before committing ("100%" for explosions)
- [x] Per-weapon projectiles (`projectile:` in the rules) and flash effects for Stun Strike, Aimed Shot, Deploy Drone
- [ ] Dedicated sprites for melee variants, plasma beam, drone deploy
- [ ] Settings screen and saving settings (keys only for now)

## Unity dead code
- [x] Cavern smoothing passes, dungeon (maze) generator and map scripts wired up (map generator dropdown: BSP, Cellular Automata, Dungeon; rules `generator: 1/2/3`)
- [x] Weapon rules `multipleTargets`, `damageDistribution`, `damageLineOfFire`, `radius` now work
- [ ] Dice combat mode — only an enum in the original, nothing to port
