package main

import (
	"container/heap"
	"image/color"
	"math"
	"math/rand"
)

type P struct{ X, Y int }

func (a P) Add(b P) P                  { return P{a.X + b.X, a.Y + b.Y} }
func (a P) Sub(b P) P                  { return P{a.X - b.X, a.Y - b.Y} }
func (a P) Mag() float64               { return math.Hypot(float64(a.X), float64(a.Y)) }
func (a P) Center() (float64, float64) { return float64(a.X) + 0.5, float64(a.Y) + 0.5 }

var (
	North, East, South, West = P{0, 1}, P{1, 0}, P{0, -1}, P{-1, 0}
	Cardinal                 = []P{East, West, North, South}
	Diagonal                 = []P{{1, 1}, {-1, 1}, {-1, -1}, {1, -1}}
)

const (
	Floor = iota
	Door
	Wall
)

type highlight struct {
	p       P
	clr     color.Color
	expires float64 // 0 = never
}

type mapLabel struct {
	p    P
	text string
}

type mapLine struct {
	a, b P
	clr  color.Color
}

type Map struct {
	W, H     int
	calc     []int
	floor    []int // -1 none, 0..2 spr.Floors kind, 10+i spawn marker of faction i
	wall     []int // -1 none, else index into spr.WallSets
	wallSpr  []int // cached WallTile sprite choice, see refreshWalls
	rolled   bool  // decorations (north alt pieces) are picked once, on the first refreshWalls
	door     []int // -1 none, else door sprite
	fog      []bool
	fogVis   []bool
	damage   []int
	units    []*Unit
	hl       [4][]highlight
	lines    []mapLine
	markers  []P
	labels   []mapLabel
	Cursor   P
	FOVDirty bool
}

func NewMap(w, h int) *Map {
	n := w * h
	m := &Map{W: w, H: h, calc: make([]int, n), floor: make([]int, n), wall: make([]int, n), wallSpr: make([]int, n),
		door: make([]int, n), fog: make([]bool, n), fogVis: make([]bool, n), damage: make([]int, n), units: make([]*Unit, n)}
	for i := range m.door {
		m.door[i], m.floor[i], m.wall[i] = -1, -1, -1
	}
	return m
}

func (m *Map) i(p P) int          { return p.Y*m.W + p.X }
func (m *Map) Contains(p P) bool  { return p.X >= 0 && p.Y >= 0 && p.X < m.W && p.Y < m.H }
func (m *Map) IsWallAt(p P) bool  { return m.Contains(p) && m.calc[m.i(p)] == Wall }
func (m *Map) IsDoorAt(p P) bool  { return m.Contains(p) && m.calc[m.i(p)] == Door }
func (m *Map) IsFloorAt(p P) bool { return m.Contains(p) && m.calc[m.i(p)] == Floor }
func (m *Map) IsUnitOn(p P) bool  { return m.UnitAt(p) != nil }

func (m *Map) UnitAt(p P) *Unit {
	if !m.Contains(p) {
		return nil
	}
	return m.units[m.i(p)]
}
func (m *Map) IsEmptyFloorTile(p P) bool { return m.IsFloorAt(p) && !m.IsUnitOn(p) }
func (m *Map) InFog(p P) bool            { return m.fog[m.i(p)] }
func (m *Map) InFogVisually(p P) bool    { return m.fogVis[m.i(p)] }

func (m *Map) AllPositions() []P {
	var ps []P
	for x := 0; x < m.W; x++ {
		for y := 0; y < m.H; y++ {
			ps = append(ps, P{x, y})
		}
	}
	return ps
}

func (m *Map) AddWall(p P, set int) {
	if m.Contains(p) {
		m.calc[m.i(p)] = Wall
		m.wall[m.i(p)] = set
	}
}

// Fill mirrors Map.FillLayer for "Floors" / "Walls": the whole map gets that tile.
func (m *Map) FillFloors(kind int) {
	for i := range m.floor {
		m.floor[i], m.calc[i] = kind, Floor
	}
}

func (m *Map) FillWalls(set int) {
	for i := range m.wall {
		m.wall[i], m.calc[i] = set, Wall
	}
}

func (m *Map) RemoveWallAt(p P) {
	m.calc[m.i(p)] = Floor
	m.wall[m.i(p)] = -1
}

func (m *Map) CreateDoor(p P, sprite int) {
	m.door[m.i(p)] = sprite
	m.wall[m.i(p)] = -1
	m.calc[m.i(p)] = Door
}

func (m *Map) RemoveDoor(p P) {
	m.FOVDirty = true
	m.door[m.i(p)] = -1
	m.calc[m.i(p)] = Floor
}

func (m *Map) CreateSpawnPoint(p P, faction int) {
	m.calc[m.i(p)] = Floor
	m.floor[m.i(p)] = 10 + faction
}

func (m *Map) ResetFog() {
	for i := range m.fog {
		m.fog[i], m.fogVis[i] = true, true
	}
}

func (m *Map) RemoveAllFog() {
	for i := range m.fog {
		m.fog[i], m.fogVis[i] = false, false
	}
}

// ---- WallTile.cs ----

var wallIndex = map[int]int{0: 0, 2: 1, 10: 2, 8: 3, 4: 4, 5: 5, 1: 6, 6: 7, 12: 8, 3: 9, 9: 10, 15: 11, 14: 12, 13: 13, 7: 14, 11: 15}

// wallMaskIndex: neighbours count only if they hold the same WallTile asset (set).
func (m *Map) wallMaskIndex(p P, set int) int {
	mask := 0
	for bit, d := range []P{North, East, South, West} {
		if q := p.Add(d); m.Contains(q) && m.wall[m.i(q)] == set && !m.isRubble(q) {
			mask |= 1 << bit
		}
	}
	return wallIndex[mask]
}

// isRubble: a destroyed wall piece; its neighbours end there instead of joining it.
func (m *Map) isRubble(p P) bool { return m.isDamaged(p) && m.wallSpr[m.i(p)]/100 == 2 }

func (m *Map) isDamaged(p P) bool { return m.Contains(p) && m.damage[m.i(p)] >= mod.Options.WallHealth }

func (m *Map) hasWallDamagedSprite(p P, set int) bool {
	idx := m.wallMaskIndex(p, set)
	return (idx == 2 || idx == 5) && m.isDamaged(p)
}

// refreshWalls picks a sprite per wall tile (index into spr.Walls, 100+ damaged, 200+ destroyed, 300+ north alt).
func (m *Map) refreshWalls() {
	for _, p := range m.AllPositions() {
		set := m.wall[m.i(p)]
		if set < 0 {
			continue
		}
		ws := spr.WallSets[set]
		idx := m.wallMaskIndex(p, set)
		s := idx
		switch {
		case m.isDamaged(p) && (idx == 2 || idx == 5) && len(ws.Damaged) > 0 && m.damage[m.i(p)] <= 50:
			a, b := m.hasWallDamagedSprite(p.Add(West), set), m.hasWallDamagedSprite(p.Add(East), set)
			base := 0
			if idx == 5 {
				a, b = m.hasWallDamagedSprite(p.Add(North), set), m.hasWallDamagedSprite(p.Add(South), set)
				base = 4
			}
			switch {
			case a && b:
				s = 100 + base + 2
			case !a && !b:
				s = 100 + base
			case a:
				s = 100 + base + 3
			default:
				s = 100 + base + 1
			}
		case m.isDamaged(p): // corners and sets without hole sprites go straight to rubble
			s = 200 + rand.Intn(len(ws.Destroyed))
		case idx == 2 && (m.wallSpr[m.i(p)]/100 == 3 || !m.rolled && rand.Intn(100) < ws.NorthChance):
			s = 300 + rand.Intn(len(ws.NorthAlt))
		}
		if prev := m.wallSpr[m.i(p)]; s/100 >= 2 && prev/100 == s/100 {
			s = prev // keep the random rubble / north variant picked earlier
		}
		m.wallSpr[m.i(p)] = s
	}
	m.rolled = true
}

// AddDamageToTile returns true if a wall got destroyed and FOV needs an update.
func (m *Map) AddDamageToTile(p P, dmg int) bool {
	if !m.Contains(p) {
		return false
	}
	m.damage[m.i(p)] += dmg
	if m.wall[m.i(p)] >= 0 {
		// twice, so neighbours see which pieces just turned to rubble
		m.refreshWalls()
		m.refreshWalls()
		if m.IsWallAt(p) && m.isDamaged(p) { // the original only breached straight pieces with a hole sprite
			m.calc[m.i(p)] = Floor
			m.FOVDirty = true
			return true
		}
	}
	return false
}

// ---- units on the map ----

func (m *Map) CanBePlaced(u *Unit, p P, doorsBlock, friendliesBlock bool) bool {
	for _, off := range u.Occupied {
		q := p.Add(off)
		if !m.Contains(q) || m.IsWallAt(q) || (doorsBlock && m.IsDoorAt(q)) {
			return false
		}
		if o := m.UnitAt(q); o != nil {
			if friendliesBlock || o.Player != u.Player {
				return false
			}
		}
	}
	return true
}

func (m *Map) AddUnit(u *Unit, p P, doorsBlock bool) bool {
	if !m.CanBePlaced(u, p, doorsBlock, true) {
		return false
	}
	for _, off := range u.Occupied {
		m.units[m.i(p.Add(off))] = u
	}
	return true
}

func (m *Map) RemoveUnitAt(p P) {
	if u := m.UnitAt(p); u != nil {
		for _, off := range u.Occupied {
			if q := p.Add(off); m.IsUnitOn(q) {
				m.units[m.i(q)] = nil
			}
		}
	}
}

func (m *Map) RemoveUnit(u *Unit) {
	if m.UnitAt(u.Cell) == u {
		m.RemoveUnitAt(u.Cell)
	}
}

func (m *Map) MoveUnit(u *Unit, from, to P) bool {
	if from == to || !m.AddUnit(u, to, false) {
		return false
	}
	m.RemoveUnitAt(from)
	return true
}

func (m *Map) IsFriendlyUnitOn(u *Unit, p P) bool {
	o := m.UnitAt(p)
	return o != nil && o.Player == u.Player
}

func (m *Map) IsOtherFactionOn(pl *Player, p P) bool {
	o := m.UnitAt(p)
	return o != nil && o.Player != pl
}

func (m *Map) IsOtherUnitOn(u *Unit, p P) bool {
	o := m.UnitAt(p)
	return o != nil && o != u
}

func (m *Map) IsStandingOnDoor(u *Unit, open bool) bool {
	found := false
	for _, off := range u.Occupied {
		if q := u.Cell.Add(off); m.IsDoorAt(q) {
			if open {
				m.RemoveDoor(q)
			}
			found = true
		}
	}
	return found
}

func AreCardinalNeighbors(a, b P) bool {
	return a.X == b.X && abs(a.Y-b.Y) == 1 || a.Y == b.Y && abs(a.X-b.X) == 1
}

func AreDiagonalNeighbors(a, b P) bool { return abs(a.Y-b.Y) == 1 && abs(a.X-b.X) == 1 }

func abs(x int) int {
	if x < 0 {
		return -x
	}
	return x
}

func (m *Map) NeighborsForUnit(u *Unit, p P) []P {
	ns := []P{p.Add(North), p.Add(East), p.Add(South), p.Add(West)}
	if u.Rules.CanMoveDiagonally && mod.Options.AllowDiagonalMovement {
		ns = append(ns, p.Add(P{1, 1}), p.Add(P{-1, 1}), p.Add(P{1, -1}), p.Add(P{-1, -1}))
	}
	return ns
}

func (m *Map) IsNeighborReachable(u *Unit, from, to P) bool {
	if !m.Contains(to) {
		return false
	}
	if from.X != to.X && from.Y != to.Y {
		d := from.Sub(to)
		if m.IsWallAt(P{to.X + d.X, to.Y}) || m.IsWallAt(P{to.X, to.Y + d.Y}) {
			return false
		}
	}
	return m.CanBePlaced(u, to, false, false)
}

func (m *Map) IsNeighborVisible(a, b P) bool {
	if !m.Contains(a) || !m.Contains(b) {
		return false
	}
	if m.IsWallAt(a) || m.IsWallAt(b) || m.IsDoorAt(a) || m.IsDoorAt(b) {
		return false
	}
	if a.X != b.X && a.Y != b.Y {
		d := a.Sub(b)
		if m.IsWallAt(P{b.X + d.X, b.Y}) || m.IsWallAt(P{b.X, b.Y + d.Y}) {
			return false
		}
	}
	return true
}

// ---- Raycaster.cs ----

// lineCast returns whether target is visible and, if not, the first blocking cell.
func (m *Map) lineCast(from, to P, observer *Unit, unitsBlock bool) (bool, P) {
	dx, dy := to.X-from.X, to.Y-from.Y
	sx, sy := -1, -1
	if from.X < to.X {
		sx = 1
	}
	if from.Y < to.Y {
		sy = 1
	}
	next, prev := from, from
	dist := math.Sqrt(float64(dx*dx + dy*dy))
	for next != to {
		if !m.IsNeighborVisible(prev, next) {
			return false, next
		}
		if unitsBlock && next != from && m.IsOtherUnitOn(observer, next) {
			return false, next
		}
		prev = next
		if math.Abs(float64(dy*(next.X-from.X+sx)-dx*(next.Y-from.Y)))/dist < 0.5 {
			next.X += sx
		} else if math.Abs(float64(dy*(next.X-from.X)-dx*(next.Y-from.Y+sy)))/dist < 0.5 {
			next.Y += sy
		} else {
			next.X += sx
			next.Y += sy
		}
	}
	if !m.IsNeighborVisible(prev, next) {
		return false, next
	}
	return true, next
}

func bresenham(a, b P, fn func(P)) {
	dx, dy := abs(b.X-a.X), -abs(b.Y-a.Y)
	sx, sy := -1, -1
	if a.X < b.X {
		sx = 1
	}
	if a.Y < b.Y {
		sy = 1
	}
	err := dx + dy
	for {
		fn(a)
		if a == b {
			return
		}
		e2 := 2 * err
		if e2 >= dy {
			err += dy
			a.X += sx
		}
		if e2 <= dx {
			err += dx
			a.Y += sy
		}
	}
}

func (m *Map) FOV(u *Unit, pos P, radius int, clearVisual, clearMech, unitsBlock bool) map[P]bool {
	fov := map[P]bool{}
	radius++
	for i := -radius; i <= radius; i++ {
		for j := -radius; j <= radius; j++ {
			if i*i+j*j >= radius*radius {
				continue
			}
			q := P{pos.X + i, pos.Y + j}
			if ok, _ := m.lineCast(pos, q, u, unitsBlock); ok {
				if clearVisual {
					m.fogVis[m.i(q)] = false
				}
				if clearMech {
					m.fog[m.i(q)] = false
				}
				fov[q] = true
			}
		}
	}
	return fov
}

func (m *Map) IsLineOfFireBetween(attacker *Unit, src, dst P) bool {
	if !mod.Options.AllowDiagonalAdjacentAttacks && AreDiagonalNeighbors(src, dst) {
		return false
	}
	ok, _ := m.lineCast(src, dst, attacker, true)
	return ok
}

// ExplosionReaches reports whether the blast from src reaches p. The tile hit (p, or the wall in the
// way) takes damage, each at most once per explosion (hit tracks them).
func (m *Map) ExplosionReaches(src, p P, dmg int, hit map[P]bool) bool {
	ok, obstacle := m.lineCast(src, p, nil, false)
	t := p
	if !ok {
		t = obstacle
	}
	if !hit[t] {
		hit[t] = true
		m.AddDamageToTile(t, dmg)
	}
	return ok
}

func (m *Map) StraightLine(from, dir P, max int) map[P]bool {
	res := map[P]bool{}
	prev := from
	for i := 0; i < max; i++ {
		next := prev.Add(dir)
		if !m.IsNeighborVisible(prev, next) {
			break
		}
		res[next] = true
		prev = next
	}
	return res
}

func (m *Map) SpawnPoints() [][]P {
	var res [][]P
	for _, p := range m.AllPositions() {
		if f := m.floor[m.i(p)] - 10; f >= 0 && m.IsFloorAt(p) {
			for len(res) <= f {
				res = append(res, nil)
			}
			res[f] = append(res[f], p)
		}
	}
	return res
}

// ---- overlays ----

func (m *Map) Highlight(p P, clr color.Color, layer int, timer float64) {
	h := highlight{p: p, clr: clr}
	if timer > 0 {
		h.expires = now + timer
	}
	m.hl[layer] = append(m.hl[layer], h)
}

func (m *Map) SetHighlightColor(clr color.Color, layer int) {
	for i := range m.hl[layer] {
		m.hl[layer][i].clr = clr
	}
}

func (m *Map) ClearHighlights(layer int)        { m.hl[layer] = nil }
func (m *Map) DrawLine(a, b P, clr color.Color) { m.lines = append(m.lines, mapLine{a, b, clr}) }
func (m *Map) ClearLines()                      { m.lines = nil }
func (m *Map) MarkTile(p P)                     { m.markers = append(m.markers, p) }
func (m *Map) ClearMarkers()                    { m.markers = nil }

// ---- Pathfinder.cs (Dijkstra; goal set was always empty in the original) ----

type Pathfinder struct {
	m                *Map
	card, diag       float64
	dist             []float64
	parent           []int
	MaxReachableCost float64
	Reachable        map[P]bool
}

type pqItem struct {
	idx  int
	dist float64
}
type pq []pqItem

func (q pq) Len() int           { return len(q) }
func (q pq) Less(a, b int) bool { return q[a].dist < q[b].dist }
func (q pq) Swap(a, b int)      { q[a], q[b] = q[b], q[a] }
func (q *pq) Push(x any)        { *q = append(*q, x.(pqItem)) }
func (q *pq) Pop() any          { o := *q; it := o[len(o)-1]; *q = o[:len(o)-1]; return it }

func (pf *Pathfinder) Run(u *Unit, start P) {
	m := pf.m
	n := m.W * m.H
	pf.dist, pf.parent = make([]float64, n), make([]int, n)
	pf.Reachable = map[P]bool{}
	for i := range pf.dist {
		pf.dist[i], pf.parent[i] = math.Inf(1), -1
	}
	closed := make([]bool, n)
	pf.dist[m.i(start)] = 0
	q := &pq{{m.i(start), 0}}
	for q.Len() > 0 {
		it := heap.Pop(q).(pqItem)
		if closed[it.idx] || it.dist > pf.dist[it.idx] {
			continue
		}
		closed[it.idx] = true
		cur := P{it.idx % m.W, it.idx / m.W}
		for _, nb := range m.NeighborsForUnit(u, cur) {
			if !m.IsNeighborReachable(u, cur, nb) || closed[m.i(nb)] {
				continue
			}
			cost := pf.card
			if nb.X != cur.X && nb.Y != cur.Y {
				cost = pf.diag
			}
			nd := pf.dist[it.idx] + cost
			if nd < pf.dist[m.i(nb)] {
				pf.dist[m.i(nb)] = nd
				pf.parent[m.i(nb)] = it.idx
				heap.Push(q, pqItem{m.i(nb), nd})
				if pf.MaxReachableCost > 0 && nd <= pf.MaxReachableCost {
					pf.Reachable[nb] = true
				}
			}
		}
	}
}

func (pf *Pathfinder) Cost(p P) float64 { return pf.dist[pf.m.i(p)] }

func (pf *Pathfinder) Path(goal P) []P {
	i := pf.m.i(goal)
	if pf.parent[i] == -1 {
		return nil
	}
	var path []P
	for ; i != -1; i = pf.parent[i] {
		path = append([]P{{i % pf.m.W, i / pf.m.W}}, path...)
	}
	return path
}

func (m *Map) PosDebugInfo(p P) string {
	s := ""
	for _, f := range []struct {
		on bool
		c  string
	}{{m.IsWallAt(p), "W"}, {m.IsDoorAt(p), "D"}, {m.IsUnitOn(p), "U"}, {!m.IsFloorAt(p), "X"}, {m.Contains(p) && m.InFog(p), "F"}} {
		if f.on {
			s += f.c
		}
	}
	return s
}
