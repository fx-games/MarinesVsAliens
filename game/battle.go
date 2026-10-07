package main

import (
	"fmt"
	"image/color"
	"log"
	"math"
	"math/rand"
	"slices"
	"strings"

	"github.com/hajimehoshi/ebiten/v2"
	"github.com/hajimehoshi/ebiten/v2/inpututil"
	"github.com/hajimehoshi/ebiten/v2/text/v2"
	"github.com/hajimehoshi/ebiten/v2/vector"
)

type GameState int

const (
	StateDefault GameState = iota
	StateMoveTargetSelection
	StateAttackTargetSelection
	StateMovePreConfirmation
	StateWaitForHotSeatPlayer
)

const (
	ModeCampaign = iota
	ModeSkirmish
)

const (
	layerUnits = iota
	layerFog
)

type anim struct {
	layer  int
	frames []*ebiten.Image
	pos    P
	start  float64
	delay  float64
	tint   color.Color
	ppu    float64
	done   func()
}

type blood struct {
	img *ebiten.Image
	p   P
}

type Battle struct {
	m           *Map
	cam         Camera
	mode        int
	units       []*Unit
	players     []*Player
	phase       int
	Turn        int
	PoV         *Player
	campaign    *Player
	selected    *Unit
	marker      *Unit
	markerTgt   bool
	state       GameState
	action      *UnitAction
	preConfAt   P
	lastCursor  P
	lastAI      float64
	roots       []*Node
	skillBtns   []*Node
	schedules   []scheduled
	projectiles []*projectile
	anims       []*anim
	bloods      []blood
	drag        camDrag
	fpsCount    int
	fpsNext     float64
	over        bool
	won         bool  // campaign: the human player won
	mission     int   // campaign: index into mod.Maps
	leaving     bool  // Esc pressed once, waiting for confirmation
	status      *Node // top-left label: turn and player (FPS in debug mode)
	help        bool
	toastID     int // the latest toast; older ones don't clear it
}

type scheduled struct {
	at float64
	fn func()
}

func NewBattle(m *Map, mode int, factions []int) *Battle {
	g := &Battle{m: m, mode: mode, state: StateWaitForHotSeatPlayer, roots: loadScene("BattleGameScene")}
	g.cam = Camera{X: 0.17, Y: 0.42, Size: 7.421875}
	playMusic("music/battle.mp3", true)
	g.label("CenterOfScreenLabel").Text.Enabled = false
	if mode == ModeCampaign {
		g.campaign = NewPlayer(0)
		g.campaign.Faction = mod.Factions[factions[0]]
		g.players = append(g.players, g.campaign)
		for _, f := range mod.Factions {
			if f != g.campaign.Faction {
				p := NewPlayer(len(g.players))
				p.Name, p.Faction = "Computer", f
				p.AI = NewTacticalAI(g, p)
				g.players = append(g.players, p)
			}
		}
	} else {
		for i, f := range factions {
			p := NewPlayer(i)
			p.Faction = mod.Factions[f]
			if skirmishAI[i] {
				p.Name, p.AI = "Computer", NewTacticalAI(g, p)
			}
			g.players = append(g.players, p)
		}
	}
	for _, n := range g.label("Canvas").Children {
		if n.Name == "Text" {
			g.status = n // the scene's FPS counter
		}
	}
	g.status.Pivot, g.status.Pos, g.status.Size[0] = [2]float64{0, 0.5}, [2]float64{8, -16}, 400
	g.m.refreshWalls()
	g.phase = -1
	g.spawnUnits()
	g.Turn++
	g.PrepareNextPhase()
	return g
}

func (g *Battle) CurrentPlayer() *Player { return g.players[g.phase] }

func (g *Battle) schedule(delay float64, fn func()) {
	g.schedules = append(g.schedules, scheduled{now + delay, fn})
}

func (a *anim) draw(dst *ebiten.Image, c *Camera) {
	x, y := a.pos.Center()
	c.drawWorld(dst, a.frames[int((now-a.start)/a.delay)], x, y, a.ppu, false, 0, a.tint)
}

// addAnim spawns a SimpleAnimation effect (explosion and melee hit prefabs sit on the FogOfWar layer).
func (g *Battle) addAnim(frames []*ebiten.Image, p P, delay float64, done func()) *anim {
	a := &anim{layer: layerFog, frames: frames, pos: p, start: now, delay: delay, ppu: 32, done: done}
	g.anims = append(g.anims, a)
	return a
}

// ---- spawning ----

func (g *Battle) spawnUnits() {
	spawns := g.m.SpawnPoints()
	for pi, pl := range g.players {
		if pi >= len(spawns) {
			log.Println(pl, "has no spawn points")
			continue
		}
		idx := 0
	loadouts:
		for _, lo := range pl.Faction.LoadOut {
			for i := 0; i < lo.Count; i++ {
				if lo.DoNotSpawn {
					continue
				}
				if idx >= len(spawns[pi]) {
					log.Println(pl, "ran out of spawn points.")
					break loadouts
				}
				g.SpawnUnit(spawns[pi][idx], lo.Unit, lo, pl)
				idx++
			}
		}
	}
}

func (g *Battle) SpawnUnit(p P, unitType string, lo *RuleLoadout, pl *Player) bool {
	r := mod.units[unitType]
	prefab := r.FactionPrefab
	if prefab == -1 {
		prefab = pl.Faction.FactionPrefab
	}
	u := &Unit{g: g, Cell: p, row: spr.FactionRows[prefab], spriteOff: r.SpriteOffset, origOff: r.SpriteOffset, ShowHealth: true}
	u.animDelay = 0.5 + rand.Float64()*0.2 - 0.1
	u.animStart = now + rand.Float64()*2
	u.LoadRules(unitType, pl, lo)
	if !g.m.AddUnit(u, p, true) {
		log.Println("Could not spawn unit at:", p)
		return false
	}
	g.units = append(g.units, u)
	g.invalidateVision(pl)
	if g.PoV != nil { // mid-battle (e.g. a deployed drone): show it and what it sees right away
		g.GlobalFOVUpdate()
	}
	return true
}

// ---- state changes ----

func (g *Battle) ToDefaultState() {
	g.m.ClearLines()
	g.m.ClearHighlights(0)
	g.m.ClearHighlights(1)
	g.m.ClearMarkers()
	g.marker = nil
	for _, u := range g.units {
		u.labelOn, u.ShowHealth = false, false
	}
	g.state = StateDefault
}

func (g *Battle) DeselectAll() {
	if g.selected != nil {
		g.selected.spriteOff = g.selected.origOff
	}
	g.selected = nil
	g.ToDefaultState()
	g.UpdateUIButtons()
}

func (g *Battle) highlightTiles(ps []P, clr color.Color, layer int) {
	g.m.ClearHighlights(layer)
	g.highlightNoClear(ps, clr, layer)
}

func (g *Battle) highlightNoClear(ps []P, clr color.Color, layer int) {
	if g.CurrentPlayer() == g.PoV {
		for _, p := range ps {
			g.m.Highlight(p, clr, layer, 0)
		}
	}
}

func keys(m map[P]bool) []P {
	var ps []P
	for p := range m {
		ps = append(ps, p)
	}
	return ps
}

func (g *Battle) UnitSelected(p P) {
	u := g.m.UnitAt(p)
	if u.Player == g.CurrentPlayer() {
		g.selected = u
		if u.AP() > 0 {
			g.state = StateMoveTargetSelection
		}
		g.highlightTiles(u.reachableSplit(true), rgba(0, 0.8, 0, 0.2), 0)
		g.highlightNoClear(u.reachableSplit(false), rgba(0.7, 0.8, 0, 0.2), 0)
		if g.CurrentPlayer() == g.PoV {
			g.marker, g.markerTgt = u, false
			a := g.addAnim(spr.Flash, u.Cell, 0.02, func() { g.m.Highlight(u.Cell, color.RGBA{0, 255, 255, 255}, 0, 2) })
			a.layer, a.tint, a.ppu = layerUnits, rgba(0.31007475, 0.9811321, 0.9811321, 1), 32.0/8 // prefab scale 8
			g.UpdateLabel()
		}
		g.UpdateUIButtons()
		if g.CurrentPlayer() == g.PoV {
			playSound(sndSelect, 1)
		}
	}
	for _, o := range g.units {
		o.ShowHealth = o.IsVisible
	}
}

func (g *Battle) previewMovement(u *Unit, target P) {
	path := u.Path(target)
	var tiles []P
	for _, p := range path {
		for _, off := range u.Occupied {
			tiles = append(tiles, p.Add(off))
		}
	}
	if len(tiles) > 0 {
		g.highlightTiles(tiles, rgba(227/255.0, 144/255.0, 36/255.0, 0.5), 1)
	}
}

func (g *Battle) movementPreviewLockedIn(u *Unit, p P) {
	g.m.ClearLines()
	g.preConfAt = p
	g.state = StateMovePreConfirmation
	g.m.SetHighlightColor(rgba(0, 0, 0.7, 0.6), 1)
	for _, e := range g.VisibleEnemies(g.CurrentPlayer()) {
		if g.m.IsLineOfFireBetween(u, p, e.Cell) {
			e.ShowHealth = true
			g.m.DrawLine(p, e.Cell, color.RGBA{255, 0, 0, 255})
		}
	}
}

func (g *Battle) tryMove(u *Unit, p P) bool {
	if u == nil || !g.m.CanBePlaced(u, p, false, true) {
		return false
	}
	path := u.Path(p)
	if len(path) == 0 || u.AP() <= 0 {
		return false
	}
	g.m.ClearHighlights(0)
	g.m.ClearHighlights(1)
	g.m.ClearLines()
	u.Move(path)
	g.state = StateDefault
	return true
}

func (g *Battle) inputReleasedOnMap(p P) {
	if g.state != StateAttackTargetSelection && g.m.IsUnitOn(p) {
		g.ToDefaultState()
		g.UnitSelected(p)
	} else if g.selected != nil {
		switch {
		case g.state == StateAttackTargetSelection && g.isValidTarget(&g.action.tm, g.action.Unit, p):
			if g.action.TargetSelected(p) {
				if g.action.tm.Type == TargetSingle {
					g.action.ConfirmTargets()
				} else {
					playSound(sndConfirm, 1)
				}
			}
		case g.state == StateAttackTargetSelection: // not a valid target: keep aiming, the label says why
			playSound(sndDeny, 1)
		case g.state == StateMoveTargetSelection && g.selected.Reachable()[p] && !g.m.IsUnitOn(p):
			g.movementPreviewLockedIn(g.selected, p)
			playSound(sndConfirm, 1)
		case g.state == StateMovePreConfirmation && g.preConfAt == p:
			g.tryMove(g.selected, p)
			playSound(sndConfirm, 1)
		case g.state == StateMovePreConfirmation:
			g.m.ClearLines()
			g.state = StateMoveTargetSelection
			g.previewMovement(g.selected, p)
			playSound(sndDeny, 1)
		default:
			g.DeselectAll()
		}
	} else {
		g.DeselectAll()
	}
	g.UpdateLabel()
}

func (g *Battle) CheckForDeadUnits() {
	for i := len(g.units) - 1; i >= 0; i-- {
		if u := g.units[i]; u.Health() <= 0 {
			g.killUnit(u)
		}
	}
	for _, u := range g.units {
		u.UpdateReachable()
	}
	cur := g.CurrentPlayer()
	if !g.isMissionOver() && !slices.Contains(g.players, cur) {
		g.PrepareNextPhase() // the acting player lost its last unit
	}
}

func (g *Battle) killUnit(u *Unit) {
	g.invalidateVision(u.Player)
	for _, o := range g.units {
		delete(o.Visible, u)
	}
	g.m.RemoveUnit(u)
	for i, o := range g.units {
		if o == u {
			g.units = append(g.units[:i], g.units[i+1:]...)
			break
		}
	}
	if g.marker == u {
		g.marker = nil
	}
	if g.selected == u {
		g.selected = nil
	}
	g.bloods = append(g.bloods, blood{spr.Blood[rand.Intn(len(spr.Blood))], u.Cell})
	log.Println("Death of", u.Name)
}

// ---- phases ----

func (g *Battle) EndPhase() {
	if g.state == StateWaitForHotSeatPlayer {
		g.startNextPhase()
	} else if !g.isMissionOver() {
		g.PrepareNextPhase()
	}
}

// isMissionOver drops eliminated players and ends the mission once it is decided. The phase index is
// moved along so the player after the current one still goes next (the original could skip one).
func (g *Battle) isMissionOver() bool {
	if g.over {
		return true
	}
	counts := map[*Player]int{}
	for _, u := range g.units {
		counts[u.Player]++
	}
	var alive []*Player
	phase := -1
	for i, p := range g.players {
		if counts[p] > 0 {
			alive = append(alive, p)
			if i <= g.phase {
				phase++
			}
		} else {
			log.Println(p, "has been eliminated.")
		}
	}
	g.players, g.phase = alive, phase // -1 until PrepareNextPhase if the first player just died
	lost := g.mode == ModeCampaign && counts[g.campaign] == 0
	if len(alive) > 1 && !lost {
		return false
	}
	g.over, g.won, g.phase = true, !lost, 0
	if len(alive) == 0 {
		g.players = []*Player{g.PoV} // keep CurrentPlayer valid on the end screen
	}
	g.m.RemoveAllFog()
	for _, u := range g.units {
		u.IsVisible = true
	}
	g.selected, g.marker = nil, nil
	g.ToDefaultState()
	g.UpdateUIButtons()
	g.UpdateLabel()
	msg := "DRAW"
	switch {
	case g.mode == ModeCampaign && lost:
		msg = "MISSION FAILED"
	case g.mode == ModeCampaign && g.mission == len(mod.Maps)-1:
		msg = "CAMPAIGN COMPLETE"
	case g.mode == ModeCampaign:
		msg = "MISSION COMPLETE"
	case len(alive) == 1:
		msg = alive[0].Name + " WINS"
	}
	g.setCenter(msg + "\nclick to continue")
	if g.mode == ModeSkirmish || g.won {
		playMusic("music/victory.mp3", false)
	} else {
		playMusic("music/defeat.mp3", false)
	}
	return true
}

// leave is the click on the end screen or a confirmed Esc: next campaign mission or back to the menu.
func (g *Battle) leave() {
	if g.over && g.won && g.mode == ModeCampaign && g.mission+1 < len(mod.Maps) {
		startMission(g.campaign.Faction, g.mission+1)
		return
	}
	scene = newMainMenu()
}

func (g *Battle) PrepareNextPhase() {
	g.ToDefaultState()
	g.phase++
	if g.phase > len(g.players)-1 {
		g.phase = 0
		g.Turn++
	}
	if cp := g.CurrentPlayer(); cp.AI == nil || g.PoV == nil || g.PoV.AI != nil || !slices.Contains(g.players, g.PoV) { // the AI plays under the last human's fog
		g.PoV = cp
	}
	if g.mode == ModeCampaign {
		g.PoV = g.campaign
	}
	g.UpdateUIButtons()
	g.selected, g.marker = nil, nil
	for _, u := range g.units {
		u.IsVisible = false
	}
	if g.CurrentPlayer() != g.PoV || !g.hotSeat() { // only a handover between humans needs "ready?"
		g.state = StateDefault
		g.startNextPhase()
		return
	}
	g.state = StateWaitForHotSeatPlayer
	g.setCenter("NEXT PLAYER\n" + g.CurrentPlayer().String())
	g.m.ResetFog()
	g.UpdateLabel()
	g.UpdateUIButtons()
	g.cam.smoothTo(float64(g.m.W/2)+0.5, float64(g.m.H/2)+0.5)
}

// hotSeat: more than one human shares this screen.
func (g *Battle) hotSeat() bool {
	humans := 0
	for _, p := range g.players {
		if p.AI == nil {
			humans++
		}
	}
	return humans > 1
}

func (g *Battle) startNextPhase() {
	g.state = StateDefault
	g.setCenter("")
	if g.CurrentPlayer() != g.PoV {
		g.setCenter("HIDDEN MOVEMENT")
	}
	g.GlobalFOVUpdate()
	first := false
	for _, u := range g.units {
		if u.Player != g.CurrentPlayer() {
			continue
		}
		u.PrepareNewPhase()
		if first || g.CurrentPlayer() != g.PoV {
			continue
		}
		first = true
		g.UnitSelected(u.Cell)
		g.cam.smoothTo(u.Cell.Center())
	}
	if ai := g.CurrentPlayer().AI; ai != nil {
		ai.StartPhase()
	}
}

// GlobalFOVUpdate shows the viewing team's visible tiles: the union of what its units see. Each team's
// set is cached until one of its units moves or the map/units change (see invalidateVision).
func (g *Battle) GlobalFOVUpdate() {
	if g.over {
		return // the end screen shows everything
	}
	if g.m.FOVDirty { // a door opened or a wall broke
		g.invalidateVision(nil)
		g.m.FOVDirty = false
	}
	for _, p := range g.players {
		if p.vis != nil {
			continue
		}
		p.vis = make([]bool, g.m.W*g.m.H)
		for _, u := range g.units {
			if u.Player == p {
				u.UpdateFoV(false, false)
				for q := range u.FOV {
					p.vis[g.m.i(q)] = true
				}
			}
		}
	}
	for i, v := range g.PoV.vis {
		g.m.fog[i], g.m.fogVis[i] = !v, !v
	}
	for _, u := range g.units {
		u.IsVisible = u.Player == g.PoV || g.PoV.vis[g.m.i(u.Cell)]
	}
}

// invalidateVision drops the cached visible set of p, or of every team when p is nil
// (doors, broken walls, spawned or killed units change everyone's view).
func (g *Battle) invalidateVision(p *Player) {
	for _, o := range g.players {
		if p == nil || o == p {
			o.vis = nil
		}
	}
}

func (g *Battle) UpdateVisibleUnits(src *Unit) {
	for _, u := range g.units {
		vis := u.Player == g.PoV || !g.m.InFogVisually(u.Cell)
		if vis && u.Player != g.CurrentPlayer() {
			u.TurnTowards(src.Cell)
		}
		u.IsVisible = vis
	}
}

func (g *Battle) unitStepCompleted(u *Unit) {
	g.m.IsStandingOnDoor(u, true)
	if g.state == StateWaitForHotSeatPlayer {
		return
	}
	prev := u.Visible
	g.invalidateVision(u.Player)
	g.GlobalFOVUpdate()
	g.UpdateVisibleUnits(u)
	appeared := false
	for e := range u.Visible {
		appeared = appeared || !prev[e]
	}
	if appeared && g.m.UnitAt(u.Cell) == u {
		u.EndMovement()
	}
}

func (g *Battle) unitFinishedMoving(u *Unit) {
	if g.state == StateWaitForHotSeatPlayer {
		return
	}
	for _, o := range g.units {
		o.UpdateReachable()
		o.UpdateTargets()
	}
	g.GlobalFOVUpdate()
	if g.selected == u && u.AP() > 0 {
		g.state = StateMoveTargetSelection
		g.highlightTiles(keys(u.Reachable()), rgba(0, 0.8, 0, 0.2), 0)
	}
	g.UpdateUIButtons()
}

// ---- commands used by buttons and AI ----

func (g *Battle) startTargeting(skill string) {
	if g.selected != nil {
		g.action = g.newAction(skill, g.selected)
		g.action.StartTargeting()
	}
}

func (g *Battle) skillButton(skill string) func() {
	return func() {
		if g.selected == nil || g.CurrentPlayer() != g.PoV {
			return
		}
		if g.state == StateAttackTargetSelection && g.action.tm.Type == TargetMultiple {
			g.action.ConfirmTargets()
		} else {
			g.startTargeting(skill)
		}
	}
}

// ---- World (the AI's view of the battle) ----

func (g *Battle) Map() *Map      { return g.m }
func (g *Battle) Units() []*Unit { return g.units }

func (g *Battle) Move(u *Unit, p P) bool {
	g.UnitSelected(u.Cell)
	return g.tryMove(u, p)
}

// UseSkill selects u, targets p with the skill and fires; false if p is not a valid target.
func (g *Battle) UseSkill(u *Unit, skill string, p P) bool {
	g.UnitSelected(u.Cell)
	g.action = g.newAction(skill, u)
	if u.AP() < g.action.cost() {
		return false
	}
	if g.action.tm.Type == TargetSelf {
		g.action.StartTargeting()
		return true
	}
	g.action.StartTargeting()
	if g.state != StateAttackTargetSelection || !g.action.TargetSelected(p) {
		g.ToDefaultState()
		return false
	}
	g.action.ConfirmTargets()
	return true
}

// busy is true while something is still moving or in flight; the AI waits for it.
func (g *Battle) busy() bool {
	for _, u := range g.units {
		if u.Moving {
			return true
		}
	}
	return len(g.projectiles) > 0 || len(g.schedules) > 0
}

// hiddenOnly: an AI is playing and nothing it does right now is in the player's view.
func (g *Battle) hiddenOnly() bool {
	if g.over || g.state == StateWaitForHotSeatPlayer || g.CurrentPlayer().AI == nil {
		return false
	}
	seen := func(p P) bool { return g.m.Contains(p) && !g.m.InFog(p) }
	for _, u := range g.units {
		if u.Moving && (seen(u.Cell) || u.visIdx+1 < len(u.path) && seen(u.path[u.visIdx+1])) {
			return false
		}
	}
	for _, p := range g.projectiles {
		if seen(P{int(math.Floor(p.x)), int(math.Floor(p.y))}) || seen(P{int(math.Floor(p.tx)), int(math.Floor(p.ty))}) {
			return false
		}
	}
	for _, a := range g.anims {
		if seen(a.pos) {
			return false
		}
	}
	return true
}

func (g *Battle) VisibleEnemies(p *Player) []*Unit {
	var res []*Unit
	for _, u := range g.units {
		if u.Player != p && !g.m.InFog(u.Cell) {
			res = append(res, u)
		}
	}
	return res
}

func (g *Battle) isNextToDoor(u *Unit) bool {
	for _, off := range u.Occupied {
		for _, d := range Cardinal {
			if g.m.IsDoorAt(u.Cell.Add(off).Add(d)) {
				return true
			}
		}
	}
	return false
}

func (g *Battle) label(name string) *Node { return findIn(g.roots, name) }

// toast shows a short message in the middle of the screen for a moment.
func (g *Battle) toast(t string) {
	g.setCenter(t)
	g.toastID++
	id := g.toastID
	g.schedule(1.5, func() {
		if g.toastID == id {
			g.setCenter("")
		}
	})
}

func (g *Battle) setCenter(t string) {
	l := g.label("CenterOfScreenLabel")
	l.Text.Enabled = t != ""
	if t != "" {
		l.SetText(t)
	}
}

// call dispatches the scene's button OnClick handlers (BattleGame.*ButtonClicked).
func (g *Battle) call(c uiCall, _ *Node) {
	playSound(sndButton, 1)
	switch c.Method {
	case "EndTurnButtonClicked":
		if g.CurrentPlayer() == g.PoV && !g.over {
			g.EndPhase()
		}
	case "OpenDoorButtonClicked":
		if g.CurrentPlayer() == g.PoV {
			g.startTargeting("OpenDoor")
		}
	case "RangedAttackButtonClicked":
		g.skillButton("RangedAttack")()
	case "MeleeAttackButtonClicked":
		g.skillButton("MeleeAttack")()
	}
}

func (g *Battle) UpdateUIButtons() {
	canvas := g.label("Canvas")
	var shown []*Node
	u := g.selected
	mine := g.CurrentPlayer() == g.PoV && !g.over // no buttons during AI / enemy turns
	if !mine {
		u = nil
	}
	for _, b := range []struct {
		name       string
		show, gray bool
	}{
		{"EndTurnButton", mine, false},
		{"RangedAttackButton", u != nil && u.Ranged != nil, u != nil && (!u.CanAffordRanged() || !g.hasTargets(g.newAction("RangedAttack", u)))},
		{"MeleeAttackButton", u != nil && u.Melee != nil, u != nil && (!u.CanAffordMelee() || !g.hasTargets(g.newAction("MeleeAttack", u)))},
		{"OpenDoorButton", u != nil && g.isNextToDoor(u), u != nil && u.AP() < g.newAction("OpenDoor", u).cost()},
	} {
		n := g.label(b.name)
		n.Active, n.Disabled = b.show, b.gray
		if b.show {
			shown = append(shown, n)
		}
	}
	need := 0
	if u != nil {
		need = len(u.Skills)
	}
	for len(g.skillBtns) < need {
		b := clonePrefab("SkillButton")
		canvas.Children = append(canvas.Children, b)
		g.skillBtns = append(g.skillBtns, b)
	}
	for i, b := range g.skillBtns {
		b.Active = u != nil && i < need
		if b.Active {
			a := g.newAction(u.Skills[i], u)
			b.Disabled = u.AP() < a.cost() || g.skillBlocked(u, u.Skills[i]) != "" || a.tm.Type != TargetSelf && !g.hasTargets(a)
			skill := u.Skills[i]
			b.OnClick = func() { playSound(sndButton, 1); g.skillButton(skill)() }
			b.Find("Text").SetText(mod.skills[skill].DisplayName)
			shown = append(shown, b)
		}
	}
	for i, b := range shown {
		b.Pos = [2]float64{29 + 45*float64(i), 24}
	}
}

func (g *Battle) UpdateLabel() {
	var t string
	hover := g.m.UnitAt(g.m.Cursor)
	if hover != nil && !hover.IsVisible { // the original leaked units hidden in the fog
		hover = nil
	}
	switch {
	case g.state == StateWaitForHotSeatPlayer || g.over:
	case g.state == StateAttackTargetSelection && g.m.Contains(g.m.Cursor) && !g.isValidTarget(&g.action.tm, g.action.Unit, g.m.Cursor):
		t = "CAN'T TARGET THIS TILE\n" + g.whyNotTarget(&g.action.tm, g.action.Unit, g.m.Cursor)
	case g.state == StateAttackTargetSelection && hover != nil && hover.Player != g.CurrentPlayer() && g.action.Skill.IsOffensiveAction:
		t = g.hitBreakdown(g.action, hover)
	case hover != nil && hover != g.selected:
		t = hover.String() + "\n" + hover.WeaponDescription()
	case g.selected != nil:
		t = g.selected.String() + "\n" + g.selected.WeaponDescription()
		if g.state == StateAttackTargetSelection && g.action.tm.Type == TargetMultiple {
			t += "\npick targets, press the button again to fire"
		}
	case debug:
		c := g.m.Cursor
		t = fmt.Sprintf("(%d, %d, 0):%s", c.X, c.Y, g.m.PosDebugInfo(c))
	}
	g.label("InfoLabel").SetText(t)
}

func (g *Battle) hitBreakdown(a *UnitAction, t *Unit) string {
	w := a.weapon()
	h, ok := hitChance(a.Unit, a.Unit.Cell, t, a.Skill.IsMelee)
	if !ok {
		return t.String()
	}
	lo, hi := 0, 0
	for _, d := range w.Rules.Damage {
		lo, hi = lo+slices.Min(d), hi+slices.Max(d)
	}
	return fmt.Sprintf("%s  HP %d/%d\nHIT %d%% (%d %+dwpn -%ddef -%drng)\nDMG %d-%d vs ARM %d",
		t.Name, t.Health(), t.Rules.Stats["Health"], h.Total, h.Acc, h.Weapon, h.Defense, h.Range, lo, hi, t.Armor())
}

// ---- frame update ----

func (g *Battle) Update() error {
	for _, r := range g.roots {
		if r.Name == "Canvas" {
			layoutCanvas(r, screenW, screenH)
		}
	}
	g.tick()
	for i := 0; i < 60*60 && g.hiddenOnly(); i++ { // fast-forward what the player can't see, without drawing
		silent = true
		now += 1.0 / 60
		g.tick()
	}
	silent = false
	g.fpsCount++
	if now > g.fpsNext {
		g.status.SetText("")
		if debug {
			g.status.SetText(fmt.Sprintf("%d FPS", int(float64(g.fpsCount)/0.5)))
		}
		g.fpsCount, g.fpsNext = 0, now+0.5
	}
	updateUI(g.roots, g.call)
	g.handleInput()
	return nil
}

// tick advances everything that is not driven by input (AI, animations, movement, timers).
func (g *Battle) tick() {
	dt := 1.0 / 60
	g.cam.update(dt)
	for i := 0; i < len(g.schedules); i++ {
		if s := g.schedules[i]; now >= s.at {
			g.schedules = append(g.schedules[:i], g.schedules[i+1:]...)
			i--
			s.fn()
		}
	}
	g.updateProjectiles(dt)
	g.updateAnims()
	for layer := range g.m.hl {
		hs := g.m.hl[layer][:0]
		for _, h := range g.m.hl[layer] {
			if h.expires == 0 || now < h.expires {
				hs = append(hs, h)
			}
		}
		g.m.hl[layer] = hs
	}
	for _, u := range append([]*Unit{}, g.units...) {
		u.update()
	}
	if !g.over && g.state != StateWaitForHotSeatPlayer && !g.busy() {
		if ai := g.CurrentPlayer().AI; ai != nil && now-g.lastAI > 0.8 {
			g.ToDefaultState()
			if !ai.Step() {
				g.EndPhase()
			}
			g.lastAI = now
		}
	}
}

func (g *Battle) handleInput() {
	mx, my := cursorPos()
	wx, wy := g.cam.toWorld(float64(mx), float64(my))
	cell := P{int(math.Floor(wx)), int(math.Floor(wy))}
	onMap := g.m.Contains(cell)
	if onMap {
		g.m.Cursor = cell
	}
	g.handleMapInput(cell, onMap, mx, my)
	g.drag.update(&g.cam, float64(mx), float64(my))
}

// selectNext cycles to the next own unit that still has AP, in unit order, and centres the camera on it.
func (g *Battle) selectNext() {
	start := slices.Index(g.units, g.selected)
	for i := 1; i <= len(g.units); i++ {
		u := g.units[(start+i+len(g.units))%len(g.units)]
		if u.Player == g.PoV && u.AP() > 0 && !u.Moving {
			g.ToDefaultState()
			g.UnitSelected(u.Cell)
			x, y := u.Cell.Center()
			g.cam.smoothTo(x, y)
			return
		}
	}
}

func (g *Battle) handleMapInput(cell P, onMap bool, mx, my int) {
	released := pointerJustReleased() && !g.drag.Active
	if g.over {
		if released {
			g.leave()
		}
		return
	}
	if inpututil.IsKeyJustPressed(ebiten.KeyEscape) {
		if g.leaving {
			g.leave()
			return
		}
		if !g.cancel() {
			g.leaving = true
			g.setCenter("LEAVE BATTLE?\nesc again to confirm")
		}
		return
	}
	if inpututil.IsKeyJustPressed(ebiten.KeyTab) && g.CurrentPlayer() == g.PoV {
		g.selectNext()
		return
	}
	if g.leaving && (released || backJustPressed()) {
		g.leaving = false
		g.setCenter("")
		return
	}
	if len(g.players) <= 1 || g.state == StateWaitForHotSeatPlayer || g.CurrentPlayer().AI != nil {
		return
	}
	if pointerOver(g.roots, float64(mx), float64(my)) {
		return
	}
	if backJustPressed() {
		g.cancel()
	}
	if onMap {
		if released {
			g.inputReleasedOnMap(cell)
		}
		if cell != g.lastCursor {
			if g.selected != nil && g.state == StateMoveTargetSelection && g.selected.Reachable()[cell] {
				g.previewMovement(g.selected, cell)
			}
			g.UpdateLabel()
		}
		g.lastCursor = cell
	} else if pointerJustPressed() {
		g.DeselectAll()
	}
}

// cancel steps back one level: targeting -> unit selected, locked-in move -> move preview, else deselect.
// Returns false if nothing was selected. (The original always deselected.)
func (g *Battle) cancel() bool {
	u := g.selected
	switch {
	case u == nil:
		return false
	case g.state == StateAttackTargetSelection:
		u.spriteOff = u.origOff
		g.ToDefaultState()
		g.UnitSelected(u.Cell)
	case g.state == StateMovePreConfirmation:
		g.m.ClearLines()
		g.state = StateMoveTargetSelection
		g.previewMovement(u, g.preConfAt)
		for _, o := range g.units {
			o.ShowHealth = o.IsVisible
		}
	default:
		g.DeselectAll()
	}
	playSound(sndDeny, 1)
	g.UpdateLabel()
	return true
}

func (g *Battle) updateProjectiles(dt float64) {
	keep := g.projectiles[:0]
	for _, p := range g.projectiles {
		dist := math.Hypot(p.tx-p.x, p.ty-p.y)
		step := math.Min(p.speed*dt, dist)
		if dist > 0 {
			p.x += (p.tx - p.x) / dist * step
			p.y += (p.ty - p.y) / dist * step
		}
		if dist <= 0.1 {
			playSound(sndHit, 0.5)
			if p.onArrive != nil {
				p.onArrive()
			}
			continue
		}
		keep = append(keep, p)
	}
	g.projectiles = keep
}

func (g *Battle) updateAnims() {
	var keep []*anim
	for _, a := range g.anims {
		if int((now-a.start)/a.delay) >= len(a.frames) {
			if a.done != nil {
				a.done()
			}
			continue
		}
		keep = append(keep, a)
	}
	g.anims = keep
}

// ---- drawing ----

func (g *Battle) Draw(screen *ebiten.Image) {
	screen.Fill(camBackground)
	g.cam.W, g.cam.H = screenW, screenH
	drawWorld(screen, &g.cam, g.m, g)
	drawUI(screen, g.roots)
	g.drawCostPips(screen)
	g.drawTooltip(screen)
}

// drawCostPips puts one small yellow pip per AP of cost in the lower left of each action button.
func (g *Battle) drawCostPips(dst *ebiten.Image) {
	u := g.selected
	if u == nil {
		return
	}
	pips := func(n *Node, skill string) {
		if !n.Active {
			return
		}
		x, y, _, h := n.screenRect()
		c := g.newAction(skill, u).cost()
		s := math.Max(math.Floor(h/14), 2) // pip size, whole pixels
		gap := s
		x = math.Floor(x + 2.5*s) // lower left, clear of the frame
		y = math.Floor(y + h - 3.5*s)
		clr := rgba(1, 0.92, 0.2, 1)
		if n.Disabled {
			clr = rgba(0.45, 0.45, 0.45, 1)
		}
		for i := 0; i < c; i++ {
			px := float32(x + float64(i)*(s+gap))
			vector.FillRect(dst, px-1, float32(y)-1, float32(s)+2, float32(s)+2, rgba(0, 0, 0, 1), false) // outline
			vector.FillRect(dst, px, float32(y), float32(s), float32(s), clr, false)
		}
	}
	pips(g.label("RangedAttackButton"), "RangedAttack")
	pips(g.label("MeleeAttackButton"), "MeleeAttack")
	pips(g.label("OpenDoorButton"), "OpenDoor")
	for i, b := range g.skillBtns {
		if i < len(u.Skills) {
			pips(b, u.Skills[i])
		}
	}
}

// drawTooltip explains the action button under the cursor.
func (g *Battle) drawTooltip(dst *ebiten.Image) {
	cx, cy := cursorPos()
	mx, my := float64(cx), float64(cy)
	u := g.selected
	var tip string
	var over *Node
	for _, name := range []string{"EndTurnButton", "RangedAttackButton", "MeleeAttackButton", "OpenDoorButton"} {
		if n := g.label(name); n.Active && n.contains(mx, my) {
			over = n
			switch {
			case name == "EndTurnButton":
				tip = "END TURN\nYour units get their action points back next turn."
			case name == "OpenDoorButton":
				tip = g.skillTip("OpenDoor", u)
			case u != nil && name == "RangedAttackButton":
				tip = g.skillTip("RangedAttack", u)
			case u != nil:
				tip = g.skillTip("MeleeAttack", u)
			}
		}
	}
	for i, b := range g.skillBtns {
		if b.Active && b.contains(mx, my) && u != nil && i < len(u.Skills) {
			over, tip = b, g.skillTip(u.Skills[i], u)
		}
	}
	if tip == "" {
		return
	}
	k := math.Min(screenW/1280, screenH/720)
	face := &text.GoTextFace{Source: arial, Size: pixelSize(16 * k)}
	w, pad := 520*k, 12*k
	lines := 0
	for _, l := range strings.Split(tip, "\n") { // wrapped line count, roughly as drawString wraps
		lines += max(int(math.Ceil(text.Advance(l, face)/(w-2*pad-face.Size))), 1)
	}
	m := face.Metrics()
	h := float64(lines)*(m.HLineGap+m.HAscent+m.HDescent)*1.2 + 2*pad
	bx, by, _, _ := over.screenRect()
	x, y := math.Min(bx, screenW-w-4), math.Max(by-h-8*k, 4)
	vector.FillRect(dst, float32(x), float32(y), float32(w), float32(h), rgba(0, 0, 0, 0.85), false)
	vector.StrokeRect(dst, float32(x), float32(y), float32(w), float32(h), float32(2*k), rgba(0.31, 0.98, 0.98, 1), false)
	drawString(dst, tip, "Arial", 16, x+pad, y+pad, w-2*pad, h-2*pad, 0, [4]float64{1, 1, 1, 1}, k, 0, 1, 1.2)
}

// skillTip: name, what it does, AP cost, and for attacks the weapon's damage and shots.
// skillBlocked says why u can't use skill right now (besides AP), or "" if it can.
func (g *Battle) skillBlocked(u *Unit, skill string) string {
	s := mod.skills[skill]
	if s.MaxActive == 0 {
		return ""
	}
	for _, e := range s.Effects {
		if e.Name != "SpawnUnit" {
			continue
		}
		n := 0
		for _, o := range g.units {
			if o.Player == u.Player && o.Rules.Type == e.StringOne {
				n++
			}
		}
		if n >= s.MaxActive {
			return fmt.Sprintf("Your team can only have %d %s at a time.", s.MaxActive, mod.Tr(e.StringOne))
		}
	}
	return ""
}

func (g *Battle) skillTip(skill string, u *Unit) string {
	a := g.newAction(skill, u)
	name := strings.ReplaceAll(a.Skill.DisplayName, "\n", " ")
	if name == "" {
		name = map[string]string{"MeleeAttack": "Melee attack", "RangedAttack": "Ranged attack", "OpenDoor": "Open door"}[skill]
	}
	t := strings.ToUpper(name) + fmt.Sprintf("  (%d AP)\n", a.cost()) + a.Skill.Description
	if w := a.weapon(); w != nil && a.Skill.IsOffensiveAction {
		lo, hi := 0, 0
		for _, d := range w.Rules.Damage {
			lo, hi = lo+slices.Min(d), hi+slices.Max(d)
		}
		dmg := fmt.Sprintf("%d-%d", lo, hi)
		if lo == hi {
			dmg = fmt.Sprint(lo)
		}
		t += fmt.Sprintf("\n%s: %s damage", w, dmg)
		if a.rollsToHit() {
			t += ", rolls to hit"
		} else {
			t += ", always hits"
		}
	}
	if r := a.targetRules(); r != "" {
		t += "\n" + r
	}
	if why := g.skillBlocked(u, skill); why != "" {
		t += "\n" + why
	} else if a.tm.Type != TargetSelf && !g.hasTargets(a) {
		t += "\nNO TARGETS NOW: " + g.noTargetReason(a)
	}
	return t
}

var unitLabel *Node

// drawWorld renders in the original sorting-layer order: Floors, Units, FogOfWar, then the world canvas.
func drawWorld(screen *ebiten.Image, c *Camera, m *Map, g *Battle) {
	at := func(img *ebiten.Image, p P, ppu float64, clr color.Color) {
		x, y := p.Center()
		c.drawWorld(screen, img, x, y, ppu, false, 0, clr)
	}
	// Floors layer
	for _, p := range m.AllPositions() {
		switch f := m.floor[m.i(p)]; {
		case f >= 10:
			at(spr.SpawnFloors[f-10], p, 24, nil)
		case f >= 0:
			at(spr.Floors[f], p, 24, nil)
		}
	}
	for _, p := range m.AllPositions() {
		if set := m.wall[m.i(p)]; set >= 0 {
			ws, s := spr.WallSets[set], m.wallSpr[m.i(p)]
			img := ws.Sprites[s%100]
			switch s / 100 {
			case 1:
				img = ws.Damaged[s%100]
			case 2:
				img = ws.Destroyed[s%100]
			case 3:
				img = ws.NorthAlt[s%100]
			}
			at(img, p, 24, nil)
		}
		if d := m.door[m.i(p)]; d >= 0 {
			at(spr.Doors[d], p, 24, nil)
		}
	}
	if g != nil {
		for _, b := range g.bloods {
			at(b.img, b.p, 32, nil)
		}
	}
	for layer := range m.hl {
		for _, h := range m.hl[layer] {
			at(spr.Blank, h.p, 32, h.clr)
		}
	}
	if g != nil {
	}
	for _, l := range m.lines {
		ax, ay := l.a.Center()
		bx, by := l.b.Center()
		c.line(screen, ax, ay, bx, by, 0.1, l.clr)
	}
	// Units layer
	if g != nil {
		for _, a := range g.anims {
			if a.layer == layerUnits {
				a.draw(screen, c)
			}
		}
		for _, p := range g.projectiles {
			scale := 1.0
			if p.arcing && p.dist0 > 0 {
				done := 1 - math.Hypot(p.tx-p.x, p.ty-p.y)/p.dist0
				scale = 1 + (0.5-math.Abs(0.5-done))*4
			}
			c.drawWorld(screen, spr.Bullets[p.sprite], p.x, p.y, 32/scale, false, -math.Atan2(p.ty-p.y, p.tx-p.x), nil)
		}
		for _, u := range g.units {
			if u.IsVisible {
				x, y := u.Cell.Center()
				c.drawWorld(screen, u.row[u.spriteOff+u.frame], x, y, 32, u.flipX, 0, nil)
			}
		}
		for _, u := range g.units {
			if !u.ShowHealth {
				continue
			}
			x, y := u.Cell.Center()
			w := 0.1
			if now < u.healthPulse {
				w = 0.5
			}
			pct := float64(u.Health()) / float64(u.Rules.Stats["Health"])
			c.line(screen, x-0.5, y-0.48, x-0.5+pct, y-0.48, w, rgba(0.047, 0.93, 1, 1))
			if u.Player == g.PoV {
				for i := 0; i < u.AP(); i++ {
					px := x - 0.45 + float64(i)*0.15
					c.line(screen, px, y+0.45, px+0.1, y+0.45, 0.08, rgba(1, 0.92, 0.016, 1))
				}
			}
		}
	}
	// FogOfWar layer
	fog := rgba(0, 0, 0, 0.6156863)
	for _, p := range m.AllPositions() {
		switch {
		case !m.fogVis[m.i(p)]:
		case g != nil && !g.over && g.state == StateWaitForHotSeatPlayer && g.mode == ModeSkirmish: // handover: hide the map
			c.fillCell(screen, p, camBackground)
		default:
			c.fillCell(screen, p, fog)
		}
	}
	cyan := rgba(0.12549022, 0.98039216, 0.9472578, 1)
	if g != nil {
		for _, a := range g.anims {
			if a.layer == layerFog {
				a.draw(screen, c)
			}
		}
		for _, p := range m.markers {
			at(spr.SelectRect, p, 32, cyan)
		}
		if u := g.marker; u != nil {
			clr := u.markerColor()
			if g.markerTgt {
				clr = rgba(1, 0.64, 0, 1)
			}
			at(spr.SelectRect, u.Cell, 32, clr)
		}
		at(spr.SelectRect, m.Cursor, 32, cyan)
	}
	if unitLabel == nil {
		unitLabel = clonePrefab("UnitLabel")
	}
	// WorldCanvas: UnitLabel prefab texts (scale 0.05 world units per canvas unit)
	lbl := unitLabel
	worldText := func(s string, x, y float64, alpha float64) {
		sx, sy := c.toScreen(x, y)
		// whole font pixels, following the tile size (1x-3x), so zooming changes it in clean steps
		k := math.Min(math.Max(math.Floor(c.scale()/28), 1), 3)
		w, h := 100*k, 12*k
		sy -= c.scale()*0.5 - h/2 // top edge of the tile
		clr := lbl.Text.Color
		clr[3] *= alpha
		drawString(screen, s, lbl.Text.Font, lbl.Text.Size, sx-w/2, sy-h/2, w, h, lbl.Text.Align, clr, k, lbl.Text.HOverflow, lbl.Text.VOverflow, lbl.Text.LineSpacing)
	}
	for _, l := range m.labels {
		x, y := l.p.Center()
		worldText(l.text, x, y, 1)
	}
	if g != nil {
		for _, u := range g.units {
			if u.labelOn {
				x, y := u.Cell.Center()
				worldText(u.label, x, y+u.labelY, math.Max(u.labelAlpha, 0))
			}
		}
	}
}
