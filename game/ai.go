package main

import "math"

// Controller drives a player's units. The battle calls StartPhase when the player's phase begins, then
// Step whenever the board is idle (nothing moving or flying) until Step returns false, which ends the phase.
type Controller interface {
	StartPhase()
	Step() bool
}

// World is everything an AI may look at or do. Battle implements it; a new AI only needs these.
// Map and unit queries (Reachable, FOV, CanAttackUnit, ...) are read-only.
type World interface {
	Map() *Map
	Units() []*Unit
	Move(u *Unit, to P) bool
	UseSkill(u *Unit, skill string, target P) bool
}

// TacticalAI replaces the original subsumption behaviours (Attack > Move > Noop). Per unit and step it
// retreats when outmatched, uses its free skills, attacks the most valuable target, or moves to the tile
// with the best shot and the least exposure. It only knows what its own units have seen.
type TacticalAI struct {
	w      World
	p      *Player
	seenAt []int // phase in which an own unit last saw the tile, 0 = never
	phase  int
	moved  map[*Unit]bool
	used   map[*Unit]map[string]bool // skills used this phase
	done   map[*Unit]bool
}

func NewTacticalAI(w World, p *Player) *TacticalAI {
	return &TacticalAI{w: w, p: p, seenAt: make([]int, w.Map().W*w.Map().H)}
}

func (ai *TacticalAI) StartPhase() {
	ai.moved, ai.used, ai.done = map[*Unit]bool{}, map[*Unit]map[string]bool{}, map[*Unit]bool{}
	ai.phase++
}

func (ai *TacticalAI) Step() bool {
	for _, u := range ai.w.Units() {
		if u.Player != ai.p || ai.done[u] || !u.CanAct() {
			continue
		}
		if ai.act(u) {
			return true
		}
		ai.done[u] = true
	}
	return false
}

// seen returns the enemies standing in any own unit's field of view (and notes when tiles were last seen).
func (ai *TacticalAI) seen() []*Unit {
	m := ai.w.Map()
	vis := map[*Unit]bool{}
	for _, u := range ai.w.Units() {
		if u.Player != ai.p {
			continue
		}
		for p := range u.FOV {
			ai.seenAt[m.i(p)] = ai.phase
		}
		for _, e := range ai.w.Units() {
			if e.Player != ai.p && u.FOV[e.Cell] {
				vis[e] = true
			}
		}
	}
	var res []*Unit
	for _, e := range ai.w.Units() { // keep unit order stable
		if vis[e] {
			res = append(res, e)
		}
	}
	return res
}

func (ai *TacticalAI) act(u *Unit) bool {
	enemies := ai.seen()
	if len(enemies) == 0 {
		return !ai.moved[u] && ai.explore(u)
	}
	if !ai.moved[u] && ai.outmatched(u, enemies) {
		ai.moved[u] = true
		ai.done[u] = true // hold the safe spot
		return ai.moveTo(u, ai.safest(u, enemies))
	}
	if ai.useSkills(u, enemies) {
		return true
	}
	if skill, t, v := ai.bestAttack(u, u.Cell, enemies); v > 0 {
		return ai.w.UseSkill(u, skill, t.Cell)
	}
	if !ai.moved[u] {
		ai.moved[u] = true
		if p := ai.bestPosition(u, enemies); p != u.Cell {
			return ai.moveTo(u, p)
		}
	}
	return false
}

func (ai *TacticalAI) moveTo(u *Unit, p P) bool {
	return p != u.Cell && ai.w.Move(u, p)
}

// explore heads for the closest tile never seen, or else the one seen longest ago (where enemies may hide).
func (ai *TacticalAI) explore(u *Unit) bool {
	ai.moved[u] = true
	m := ai.w.Map()
	best, seen, cost := P{}, math.MaxInt, math.Inf(1)
	for _, p := range m.AllPositions() {
		s, c := ai.seenAt[m.i(p)], u.pathCost(p)
		if m.IsWallAt(p) || m.IsUnitOn(p) || math.IsInf(c, 1) || s == ai.phase {
			continue
		}
		if s < seen || s == seen && c < cost {
			best, seen, cost = p, s, c
		}
	}
	if math.IsInf(cost, 1) {
		return false
	}
	return ai.moveTo(u, towards(u, best))
}

// towards returns the farthest reachable tile on the path to goal.
func towards(u *Unit, goal P) P {
	reach := u.Reachable()
	path := u.Path(goal)
	for i := len(path) - 1; i >= 0; i-- {
		if reach[path[i]] {
			return path[i]
		}
	}
	return u.Cell
}

// ---- evaluation ----

// damageOdds maps each possible damage roll of the weapon to its probability (rollDamage's dice).
func damageOdds(w *Weapon) map[int]float64 {
	odds := map[int]float64{0: 1}
	for _, d := range w.Rules.Damage {
		var faces []int
		switch {
		case len(d) == 1:
			faces = d
		case len(d) == 2:
			for v := d[0]; v <= d[1]; v++ {
				faces = append(faces, v)
			}
		default:
			faces = d
		}
		next := map[int]float64{}
		for sum, p := range odds {
			for _, f := range faces {
				next[sum+f] += p / float64(len(faces))
			}
		}
		odds = next
	}
	return odds
}

// expectedDamage is the average damage that gets through the armour.
func expectedDamage(w *Weapon, armor int) float64 {
	e := 0.0
	for d, p := range damageOdds(w) {
		e += p * float64(max(d-armor, 1)) // hits always deal at least 1
	}
	return e
}

func avgDamage(w *Weapon) float64 { return expectedDamage(w, 0) }

// attackValue is the expected damage of a from `from` on t, doubled when it is likely to kill.
func attackValue(a *Unit, from P, t *Unit, melee bool) float64 {
	w := a.Ranged
	if melee {
		w = a.Melee
	}
	h, ok := hitChance(a, from, t, melee)
	if !ok {
		return 0
	}
	dmg := expectedDamage(w, t.Armor())
	v := float64(h.Total) / 100 * dmg
	if dmg >= float64(t.Health()) {
		v *= 2
	}
	// focus fire: finishing a wounded enemy takes it out of the fight sooner
	return v * (2 - float64(t.Health())/float64(t.Rules.Stats["Health"]))
}

// bestAttack picks the attack skill and target with the highest value, attacking from `from`.
func (ai *TacticalAI) bestAttack(u *Unit, from P, enemies []*Unit) (string, *Unit, float64) {
	var skill string
	var target *Unit
	best := 0.0
	for _, e := range enemies {
		if u.CanAffordMelee() && AreCardinalNeighbors(from, e.Cell) {
			if v := attackValue(u, from, e, true); v > best {
				skill, target, best = "MeleeAttack", e, v
			}
		}
		if u.CanAffordRanged() && u.canShootFrom(from, e.Cell) {
			if v := attackValue(u, from, e, false); v > best {
				skill, target, best = "RangedAttack", e, v
			}
		}
	}
	return skill, target, best
}

// threat is the damage the visible enemies can be expected to deal to u standing at p.
func threat(u *Unit, p P, enemies []*Unit) float64 {
	t := 0.0
	for _, e := range enemies {
		if e.Ranged != nil && e.canShootFrom(e.Cell, p) {
			if h, ok := hitChance(e, e.Cell, u, false); ok {
				t += float64(h.Total) / 100 * expectedDamage(e.Ranged, u.Armor())
			}
		}
		if e.Melee != nil && chebyshev(e.Cell, p) <= e.Rules.Stats["Movement"] {
			t += expectedDamage(e.Melee, u.Armor()) * float64(e.Rules.Stats["Accuracy"]) / 100
		}
	}
	return t
}

// inCorridor: a floor tile passable only in a straight line (walls on both other sides).
func inCorridor(m *Map, p P) bool {
	open := func(d P) bool { return !m.IsWallAt(p.Add(d)) }
	ns, ew := open(North) || open(South), open(East) || open(West)
	return ns != ew
}

func chebyshev(a, b P) int { return max(abs(a.X-b.X), abs(a.Y-b.Y)) }

// walkDist returns the walking distance (through doors, ignoring units) from every tile to the nearest unit.
func walkDist(m *Map, us []*Unit) func(P) float64 {
	var starts []P
	for _, e := range us {
		starts = append(starts, e.Cell)
	}
	d := map[P]int{}
	m.flood(starts, func(p P, n int) { d[p] = n })
	return func(p P) float64 {
		if n, ok := d[p]; ok {
			return float64(n)
		}
		return 1000
	}
}

// outmatched: badly hurt and standing where the enemy can finish the job.
func (ai *TacticalAI) outmatched(u *Unit, enemies []*Unit) bool {
	hurt := u.Health()*2 <= u.Rules.Stats["Health"]
	_, _, v := ai.bestAttack(u, u.Cell, enemies)
	return hurt && threat(u, u.Cell, enemies) >= float64(u.Health())/2 && v < 1
}

func (ai *TacticalAI) safest(u *Unit, enemies []*Unit) P {
	best, score := u.Cell, threat(u, u.Cell, enemies)
	dist := walkDist(ai.w.Map(), enemies)
	for p := range u.Reachable() {
		if s := threat(u, p, enemies) - dist(p)*0.01; s < score {
			best, score = p, s
		}
	}
	return best
}

// bestPosition scores reachable tiles by the attack still affordable from there, minus the exposure.
func (ai *TacticalAI) bestPosition(u *Unit, enemies []*Unit) P {
	dist := walkDist(ai.w.Map(), enemies)
	harmless := true // e.g. drones against armour 2: they only block the way
	for _, e := range enemies {
		for _, w := range []*Weapon{u.Melee, u.Ranged} {
			if w != nil && expectedDamage(w, e.Armor()) > 0 {
				harmless = false
			}
		}
	}
	m := ai.w.Map()
	scoreAt := func(p P) float64 {
		s := -threat(u, p, enemies) * 0.3
		if p != u.Cell && inCorridor(m, p) {
			s -= 0.4 // don't park in a corridor, teammates need to get past
		}
		if harmless {
			return s - math.Max(3-dist(p), 0)
		}
		if _, _, v := ai.bestAttack(u, p, enemies); v > 0 {
			if float64(u.GetStat("ActionPoints"))-u.pathCost(p) >= float64(u.minAttackCost()*100) {
				return s + 1 + v // a shot this turn beats closing in
			}
			return s + 0.5 + v // a shot next turn
		}
		return s - dist(p)*0.2
	}
	best, score := u.Cell, scoreAt(u.Cell)
	for p := range u.Reachable() {
		if s := scoreAt(p); s > score {
			best, score = p, s
		}
	}
	return best
}

// useSkills fires the unit's special skills when they are worth it, each at most once per phase.
func (ai *TacticalAI) useSkills(u *Unit, enemies []*Unit) bool {
	if ai.used[u] == nil {
		ai.used[u] = map[string]bool{}
	}
	m := ai.w.Map()
	for _, s := range u.Skills {
		if ai.used[u][s] {
			continue
		}
		target, ok := P{}, false
		switch s {
		case "AimedShot":
			_, t, v := ai.bestAttack(u, u.Cell, enemies)
			ok = v > 0 && t != nil && u.canShootFrom(u.Cell, t.Cell)
			target = u.Cell
		case "StunMelee":
			for _, e := range enemies {
				if AreCardinalNeighbors(u.Cell, e.Cell) && !e.hasBuff(BuffStun) {
					target, ok = e.Cell, true
				}
			}
		case "DeployDrone":
			for _, d := range Cardinal {
				if p := u.Cell.Add(d); m.IsEmptyFloorTile(p) {
					target, ok = p, true
					break
				}
			}
		case "RocketLauncher":
			if !u.CanAffordRanged() {
				break
			}
			_, _, direct := ai.bestAttack(u, u.Cell, enemies)
			best := direct
			for p := range u.ValidRangedTargets() {
				v := 0.0
				for _, t := range ai.w.Units() {
					if chebyshev(t.Cell, p) > 1 {
						continue
					}
					if t.Player == u.Player {
						v -= 2 * avgDamage(u.Ranged)
					} else {
						v += expectedDamage(u.Ranged, t.Armor())
					}
				}
				if v > best {
					target, ok, best = p, true, v
				}
			}
		}
		if ok {
			ai.used[u][s] = true
			if ai.w.UseSkill(u, s, target) {
				return true
			}
		}
	}
	return false
}
