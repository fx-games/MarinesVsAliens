package main

import (
	"embed"
	"fmt"
	"image/color"
	"math"
	"math/rand"
	"slices"
	"sort"
	"strings"
)

//go:embed maps/*.txt
var mapFiles embed.FS

// LoadPremade reads a map exported from the Unity prefab of the same name.
// '#' wall, '.' floor, 'o' decorated floor, 'D'/'d' door sprite 0/1, '0'-'3' spawn floor of that faction.
func LoadPremade(name string) *Map {
	b, err := mapFiles.ReadFile("maps/" + name + ".txt")
	if err != nil {
		panic(err)
	}
	rows := strings.Split(strings.TrimRight(string(b), "\n"), "\n")
	m := NewMap(len(rows[0]), len(rows))
	for i, row := range rows {
		y := len(rows) - 1 - i
		for x, ch := range row {
			p := P{x, y}
			m.floor[m.i(p)] = 0
			switch {
			case ch == '#':
				m.AddWall(p, 0)
			case ch == 'o':
				m.floor[m.i(p)] = 2
			case ch == 'D' || ch == 'd':
				m.CreateDoor(p, map[rune]int{'D': 0, 'd': 1}[ch])
			case ch >= '0' && ch <= '3':
				m.CreateSpawnPoint(p, int(ch-'0'))
			}
		}
	}
	return m
}

// ---- BSPGenerator.cs ----

type rect struct{ X, Y, W, H int }

func (r rect) contains(p P) bool { return p.X >= r.X && p.X < r.X+r.W && p.Y >= r.Y && p.Y < r.Y+r.H }
func (r rect) center() P {
	return P{int(float64(r.X) + float64(r.W)/2), int(float64(r.Y) + float64(r.H)/2)}
}

func (r rect) each(fn func(P)) {
	for y := r.Y; y < r.Y+r.H; y++ {
		for x := r.X; x < r.X+r.W; x++ {
			fn(P{x, y})
		}
	}
}

// rng mirrors UnityEngine.Random.Range(int,int): max exclusive, returns min when the range is empty.
func rng(lo, hi int) int {
	if hi <= lo {
		return lo
	}
	return lo + rand.Intn(hi-lo)
}

type corridor struct {
	hasDoor bool
	door    P
}

type bsp struct {
	m     *Map
	leafs []rect
	adj   map[P]map[P]*corridor // MST adjacency
}

// GenerateBSP builds a skirmish map with spawn zones for `players` players.
func GenerateBSP(w, h, players int) *Map {
	for {
		g := &bsp{m: NewMap(w, h), adj: map[P]map[P]*corridor{}}
		g.m.FillFloors(0)
		g.m.FillWalls(0)
		g.partition(rect{1, 1, w - 2, h - 2})
		for _, r := range g.leafs {
			r.each(g.m.RemoveWallAt)
		}
		g.connectRoomsFromMST()
		if g.chooseRegions(players) {
			g.placeSpawnPoints(players)
			return g.m
		}
	}
}

func (g *bsp) partition(r rect) {
	var a, b rect
	if r.W > r.H {
		split := rng(r.X+2, r.X+r.W-2)
		lw := split - r.X
		a, b = rect{r.X, r.Y, lw, r.H}, rect{split + 1, r.Y, r.W - lw - 1, r.H}
	} else {
		split := rng(r.Y+2, r.Y+r.H-2)
		lh := split - r.Y
		a, b = rect{r.X, r.Y, r.W, lh}, rect{r.X, split + 1, r.W, r.H - lh - 1}
	}
	for _, c := range []rect{a, b} {
		if c.W*c.H > 25 {
			g.partition(c)
		} else {
			g.leafs = append(g.leafs, c)
		}
	}
}

func (g *bsp) room(p P) (rect, bool) {
	for _, r := range g.leafs {
		if r.contains(p) {
			return r, true
		}
	}
	return rect{}, false
}

func (g *bsp) link(a, b P, c *corridor) {
	for _, v := range [][2]P{{a, b}, {b, a}} {
		if g.adj[v[0]] == nil {
			g.adj[v[0]] = map[P]*corridor{}
		}
		g.adj[v[0]][v[1]] = c
	}
}

// connectRoomsFromMST: the original built a Delaunay triangulation and took its MST;
// the Euclidean MST of the full graph is the same tree, so Prim runs on all room centers.
func (g *bsp) connectRoomsFromMST() {
	var pts []P
	seen := map[P]bool{}
	for _, r := range g.leafs {
		if c := r.center(); !seen[c] {
			seen[c] = true
			pts = append(pts, c)
		}
	}
	in := map[P]bool{pts[0]: true}
	for len(in) < len(pts) {
		best, ba, bb := math.Inf(1), P{}, P{}
		for a := range in {
			for _, b := range pts {
				if d := a.Sub(b).Mag(); !in[b] && d < best {
					best, ba, bb = d, a, b
				}
			}
		}
		in[bb] = true
		ra, _ := g.room(ba)
		rb, _ := g.room(bb)
		g.link(ba, bb, g.connectTwoRooms(ra, rb))
	}
}

func (g *bsp) connectTwoRooms(one, two rect) *corridor {
	c := &corridor{}
	horiz := one.X+one.W < two.X || one.X > two.X+two.W
	vert := one.Y+one.H < two.Y || one.Y > two.Y+two.H
	var start, end P
	switch {
	case horiz && !vert:
		y := rng(max(one.Y, two.Y), min(one.Y+one.H, two.Y+two.H))
		start, end = P{one.center().X, y}, P{two.center().X, y}
	case vert && !horiz:
		x := rng(max(one.X, two.X), min(one.X+one.W, two.X+two.W))
		start, end = P{x, one.center().Y}, P{x, two.center().Y}
	default:
		return c // the original carved a stray door at (0,0) here
	}
	bresenham(start, end, func(p P) {
		if g.m.IsWallAt(p) {
			g.m.RemoveWallAt(p)
			if !c.hasDoor {
				c.hasDoor, c.door = true, p
				g.m.CreateDoor(p, 0)
			}
		}
	})
	return c
}

func (g *bsp) mstLeafs() []P {
	var res []P
	for v, n := range g.adj {
		if len(n) == 1 {
			res = append(res, v)
		}
	}
	sort.Slice(res, func(a, b int) bool { return res[a].X < res[b].X || res[a].X == res[b].X && res[a].Y < res[b].Y })
	return res
}

func (g *bsp) chooseRegions(players int) bool {
	leafs := g.mstLeafs()
	if len(leafs) < players {
		return false
	}
	start := leafs[rand.Intn(len(leafs))]
	dist, pred := map[P]int{}, map[P]P{}
	var walk func(v P, d int)
	walk = func(v P, d int) {
		if _, ok := dist[v]; ok {
			return
		}
		dist[v] = d
		for o := range g.adj[v] {
			if _, ok := pred[o]; !ok {
				pred[o] = v
			}
			walk(o, d+1)
		}
	}
	walk(start, 0)
	var byDist []P
	for v := range dist {
		byDist = append(byDist, v)
	}
	sort.SliceStable(byDist, func(a, b int) bool { return dist[byDist[a]] < dist[byDist[b]] })
	key, end := byDist[len(byDist)/2], byDist[len(byDist)-1]
	if debug {
		for v, p := range pred {
			g.m.DrawLine(v, p, rgba(0.8, 0.8, 0, 0.5))
		}
		for _, v := range byDist {
			g.m.labels = append(g.m.labels, mapLabel{v, fmt.Sprint(dist[v])})
		}
	}

	// the door leading to the farthest room gets the second door sprite
	for v := end; v != start; v = pred[v] {
		if c := g.adj[pred[v]][v]; c != nil && c.hasDoor {
			g.m.CreateDoor(c.door, 1)
			break
		}
	}
	for i, v := range []P{start, key, end} {
		if !debug {
			break
		}
		r, _ := g.room(v)
		clr := []color.Color{rgba(0, 0.8, 0, 0.5), rgba(0.8, 0.8, 0, 0.5), rgba(0.8, 0.2, 0, 0.5)}[i]
		r.each(func(p P) { g.m.Highlight(p, clr, 0, 0) })
	}
	return true
}

func (g *bsp) placeSpawnPoints(players int) {
	leafs := g.mstLeafs()
	rand.Shuffle(len(leafs), func(a, b int) { leafs[a], leafs[b] = leafs[b], leafs[a] })
	for f := 0; f < players; f++ {
		r, _ := g.room(leafs[f])
		placed := 0
		r.each(func(p P) {
			if g.m.IsFloorAt(p) && placed < 4 {
				g.m.CreateSpawnPoint(p, f)
				placed++
			}
		})
	}
}

// ---- CavernGenerator.cs ----

// GenerateCavern: random fill, then the cellular automaton passes from the roguebasin article the original
// was based on (they were never called there), then everything outside the largest cave is filled in.
func GenerateCavern(w, h int) *Map {
	m := NewMap(w, h)
	m.FillFloors(1)
	mid := h / 2
	for _, p := range m.AllPositions() {
		if p.X == 0 || p.Y == 0 || p.X == w-1 || p.Y == h-1 || p.X != mid && rng(0, 99) < 40 {
			m.AddWall(p, 1)
		} else {
			m.RemoveWallAt(p)
		}
	}
	for i := 0; i < 7; i++ {
		walls := make([]bool, w*h)
		for _, p := range m.AllPositions() {
			walls[m.i(p)] = m.wallsAround(p, 1) >= 5 || i < 4 && m.wallsAround(p, 2) <= 2
		}
		for _, p := range m.AllPositions() {
			if walls[m.i(p)] {
				m.AddWall(p, 1)
			} else {
				m.RemoveWallAt(p)
			}
		}
	}
	keep := map[P]bool{}
	for _, p := range m.largestRegion() {
		keep[p] = true
	}
	for _, p := range m.AllPositions() {
		if !keep[p] {
			m.AddWall(p, 1)
		}
	}
	return m
}

// wallsAround counts walls in the (2r+1)² box around p, the map edge counting as wall.
func (m *Map) wallsAround(p P, r int) int {
	n := 0
	for x := p.X - r; x <= p.X+r; x++ {
		for y := p.Y - r; y <= p.Y+r; y++ {
			if q := (P{x, y}); !m.Contains(q) || m.IsWallAt(q) {
				n++
			}
		}
	}
	return n
}

// ---- DungeonGenerator.cs: rooms, a maze grown between them, joined by doors, dead ends filled ----

func GenerateDungeon(w, h int) *Map {
	m := NewMap(w, h)
	m.FillFloors(0)
	m.FillWalls(0)
	region := map[P]int{}
	cur := -1
	carve := func(p P) {
		m.RemoveWallAt(p)
		region[p] = cur
	}
	var rooms []rect
	for i := 0; i < 2000; i++ {
		x, y := rand.Intn(w)|1, rand.Intn(h)|1
		r := rect{x, y, rng(3, 5) | 1, rng(3, 5) | 1}
		if r.X <= 0 || r.X+r.W >= w-1 || r.Y <= 0 || r.Y+r.H >= h-1 {
			continue
		}
		overlaps := false
		for _, o := range rooms {
			overlaps = overlaps || r.X < o.X+o.W && o.X < r.X+r.W && r.Y < o.Y+o.H && o.Y < r.Y+r.H
		}
		if overlaps {
			continue
		}
		rooms = append(rooms, r)
		cur++
		r.each(carve)
	}
	for y := 1; y < h; y += 2 {
		for x := 1; x < w; x += 2 {
			if !m.IsWallAt(P{x, y}) {
				continue
			}
			cur++ // growMaze
			carve(P{x, y})
			cells, last := []P{{x, y}}, P{}
			for len(cells) > 0 {
				c := cells[len(cells)-1]
				var open []P
				for _, d := range Cardinal {
					if e := c.Add(P{d.X * 3, d.Y * 3}); m.Contains(e) && m.IsWallAt(c.Add(P{d.X * 2, d.Y * 2})) {
						open = append(open, d)
					}
				}
				if len(open) == 0 {
					cells, last = cells[:len(cells)-1], P{}
					continue
				}
				d := open[rand.Intn(len(open))]
				for _, o := range open {
					if o == last && rng(0, 100) > 23 { // windingPercent
						d = last
					}
				}
				carve(c.Add(d))
				carve(c.Add(P{d.X * 2, d.Y * 2}))
				cells, last = append(cells, c.Add(P{d.X * 2, d.Y * 2})), d
			}
		}
	}
	// connectRegions: open a door in a random wall between two different regions until all are merged
	conn := map[P][]int{}
	var connectors []P
	for x := 1; x < w-1; x++ {
		for y := 1; y < h-1; y++ {
			p := P{x, y}
			if !m.IsWallAt(p) {
				continue
			}
			seen := map[int]bool{}
			for _, d := range Cardinal {
				if r, ok := region[p.Add(d)]; ok && !seen[r] {
					seen[r] = true
					conn[p] = append(conn[p], r)
				}
			}
			if len(conn[p]) >= 2 {
				connectors = append(connectors, p)
			}
		}
	}
	merged := make([]int, cur+1)
	for i := range merged {
		merged[i] = i
	}
	regionsOf := func(p P) []int { // distinct merged regions; the original kept duplicates and could drop open regions early
		var res []int
		for _, r := range conn[p] {
			if !slices.Contains(res, merged[r]) {
				res = append(res, merged[r])
			}
		}
		return res
	}
	for open := cur + 1; open > 1 && len(connectors) > 0; {
		c := connectors[rand.Intn(len(connectors))]
		m.CreateDoor(c, 0)
		rs := regionsOf(c)
		for i := range merged {
			if slices.Contains(rs[1:], merged[i]) {
				merged[i] = rs[0]
			}
		}
		open -= len(rs) - 1
		connectors = slices.DeleteFunc(connectors, func(p P) bool {
			return c.Sub(p).Mag() < 2 || len(regionsOf(p)) < 2
		})
	}
	// removeDeadEnds
	for done := false; !done; {
		done = true
		for x := 1; x < w-1; x++ {
			for y := 1; y < h-1; y++ {
				p := P{x, y}
				if m.IsWallAt(p) {
					continue
				}
				exits := 0
				for _, d := range Cardinal {
					if !m.IsWallAt(p.Add(d)) {
						exits++
					}
				}
				if exits == 1 {
					done = false
					m.door[m.i(p)] = -1
					m.AddWall(p, 0)
				}
			}
		}
	}
	return m
}

// ---- MapFromScript.cs: rooms, walls and doors from the rules' map blocks ----

func GenerateFromScript(r *RuleMap) *Map {
	m := NewMap(r.Size[0], r.Size[1])
	m.FillFloors(0)
	walls := func(x0, y0, x1, y1 int) {
		for x := x0; x <= x1; x++ {
			for y := y0; y <= y1; y++ {
				m.AddWall(P{x, y}, 0)
			}
		}
	}
	walls(0, 0, m.W-1, 0) // the original had no outer wall and no floor
	walls(0, m.H-1, m.W-1, m.H-1)
	walls(0, 0, 0, m.H-1)
	walls(m.W-1, 0, m.W-1, m.H-1)
	for _, c := range r.MapScript {
		if !strings.EqualFold(c.Command, "placeBlock") {
			continue
		}
		for _, off := range c.Rects {
			b := mod.MapBlock(c.Blocks[rand.Intn(len(c.Blocks))])
			ox, oy := off[0], off[1]
			for _, rm := range b.Rooms {
				x0, y0, x1, y1 := rm[0]+ox, rm[1]+oy, rm[2]+ox, rm[3]+oy
				walls(x0, y0-1, x1, y0)
				walls(x0, y1, x1, y1+1)
				walls(x0-1, y0, x0, y1)
				walls(x1, y0, x1+1, y1)
			}
			for _, wl := range append(append([][]int{}, b.HorizontalWalls...), b.VerticalWalls...) {
				walls(wl[0]+ox, wl[1]+oy, wl[2]+ox, wl[3]+oy)
			}
			for _, d := range append(append([][]int{}, b.HorizontalDoors...), b.VerticalDoors...) {
				if p := (P{d[0] + ox, d[1] + oy}); m.Contains(p) {
					m.CreateDoor(p, 0)
				}
			}
		}
	}
	return m
}

// MapFromRules builds a campaign map (Map.FromRules / BattleGame.LoadMap).
func MapFromRules(r *RuleMap, players int) *Map {
	switch r.Generator {
	case GenScript:
		return withSpawns(players, func() *Map { return GenerateFromScript(r) })
	case GenDungeon:
		return withSpawns(players, func() *Map { return GenerateDungeon(25, 25) })
	case GenCavern:
		return withSpawns(players, func() *Map { return GenerateCavern(25, 25) })
	}
	return LoadPremade(r.Premade)
}

// withSpawns reruns gen until the map has room for a spawn zone per player.
func withSpawns(players int, gen func() *Map) *Map {
	for i := 0; ; i++ {
		m := gen()
		if len(m.SpawnPoints()) >= players || m.placeSpawns(players) || i == 50 {
			return m
		}
	}
}

// ---- spawn zones for generators that don't place their own ----

// flood visits every non-wall tile reachable from starts, breadth first.
func (m *Map) flood(starts []P, fn func(p P, dist int)) {
	dist := map[P]int{}
	q := append([]P{}, starts...)
	for _, s := range starts {
		dist[s] = 0
	}
	for len(q) > 0 {
		p := q[0]
		q = q[1:]
		fn(p, dist[p])
		for _, d := range Cardinal {
			if n := p.Add(d); m.Contains(n) && !m.IsWallAt(n) {
				if _, ok := dist[n]; !ok {
					dist[n] = dist[p] + 1
					q = append(q, n)
				}
			}
		}
	}
}

func (m *Map) largestRegion() []P {
	seen := map[P]bool{}
	var best []P
	for _, p := range m.AllPositions() {
		if seen[p] || m.IsWallAt(p) {
			continue
		}
		var reg []P
		m.flood([]P{p}, func(q P, _ int) { seen[q] = true; reg = append(reg, q) })
		if len(reg) > len(best) {
			best = reg
		}
	}
	return best
}

// placeSpawns spreads the spawn zones out: each next one goes to the tile farthest from all earlier ones.
func (m *Map) placeSpawns(players int) bool {
	const perZone = 4
	var cands []P
	for _, p := range m.largestRegion() {
		if m.IsFloorAt(p) {
			cands = append(cands, p)
		}
	}
	if len(cands) < players*perZone*2 {
		return false
	}
	centers := []P{cands[rand.Intn(len(cands))]}
	for len(centers) < players {
		far, farD := centers[0], -1
		m.flood(centers, func(p P, d int) {
			if d > farD && m.IsFloorAt(p) {
				far, farD = p, d
			}
		})
		centers = append(centers, far)
	}
	for f, c := range centers {
		placed := 0
		m.flood([]P{c}, func(p P, _ int) {
			if placed < perZone && m.IsFloorAt(p) && m.floor[m.i(p)] < 10 {
				m.CreateSpawnPoint(p, f)
				placed++
			}
		})
	}
	return true
}
