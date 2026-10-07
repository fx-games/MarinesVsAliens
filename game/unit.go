package main

import (
	"fmt"
	"image/color"
	"math"

	"github.com/hajimehoshi/ebiten/v2"
)

type Player struct {
	Name    string
	Index   int
	Faction *RuleFaction
	AI      Controller
	vis     []bool // cached union of the team's field of view, nil = stale
}

func NewPlayer(i int) *Player { return &Player{Name: fmt.Sprint("Player ", i+1), Index: i} }

func (p *Player) String() string { return p.Name + " - " + mod.Tr(p.Faction.Type) }

// ---- stats & buffs ----

type Stat struct{ Max, Cur int }

func NewStat(v int) *Stat { return &Stat{v, v} }

// Set clamps into [0,Max] and returns the rest that did not fit.
func (s *Stat) Set(v int) int {
	s.Cur = min(max(v, 0), s.Max)
	return v - s.Cur
}

func (s *Stat) Change(d int) int {
	old := s.Cur
	s.Set(s.Cur + d)
	return s.Cur - old
}

const (
	BuffStat  = "Buff"
	BuffStun  = "Stun"
	BuffShred = "Shredder"
)

type Buff struct {
	Kind         string
	Unit         *Unit
	Mods         map[string]*Stat
	Timer        int // turns to live, 0 = none
	Attacks      int // attacks to live, 0 = none
	TurnsAlive   int
	AttacksAlive int
}

func (b *Buff) ended() bool {
	return b.Timer > 0 && b.TurnsAlive == b.Timer || b.Attacks > 0 && b.AttacksAlive == b.Attacks
}

func NewBuff(r *RuleBuff) *Buff {
	b := &Buff{Kind: r.Name, Mods: map[string]*Stat{}}
	if r.Timer > 0 {
		b.Timer = r.Timer
	} else if r.EndCondition == 1 {
		b.Attacks = 1
	}
	for _, m := range r.StatModifiers {
		b.Mods[m.Name] = NewStat(m.Value)
	}
	return b
}

// ---- weapons ----

type Weapon struct{ Rules *RuleWeapon }

func (w *Weapon) String() string { return mod.Tr(w.Rules.Type) }

func rollDamage(r *RuleWeapon) int {
	sum := 0
	for _, d := range r.Damage {
		switch {
		case len(d) == 1:
			sum += d[0]
		case len(d) == 2:
			sum += d[0] + dice.Intn(d[1]-d[0]+1)
		case len(d) > 2:
			sum += d[dice.Intn(len(d))]
		}
	}
	return sum
}

// ---- unit ----

type Unit struct {
	g          *Battle
	Rules      *RuleUnit
	Name       string
	Player     *Player
	Cell       P
	Occupied   []P
	stats      map[string]*Stat
	buffs      []*Buff
	Ranged     *Weapon
	Melee      *Weapon
	Skills     []string
	card, diag int
	pf         *Pathfinder
	pfDirty    bool
	targets    map[P]bool
	FOV        map[P]bool
	Visible    map[*Unit]bool

	// movement
	Moving   bool
	path     []P
	visIdx   int
	mapIdx   int
	lastMove float64

	// presentation
	IsVisible   bool
	ShowHealth  bool
	healthPulse float64
	flipX       bool
	row         []*ebiten.Image
	spriteOff   int
	origOff     int
	frame       int
	animDelay   float64
	animStart   float64
	lastAnim    float64
	label       string
	labelOn     bool
	labelAnim   bool
	labelY      float64
	labelAlpha  float64
}

func (u *Unit) GetStat(name string) int {
	v := u.stats[name].Cur
	for _, b := range u.buffs {
		if m, ok := b.Mods[name]; ok {
			v += m.Cur
		}
	}
	return v
}

func (u *Unit) SetStat(name string, v int) {
	rest := u.stats[name].Set(v)
	if rest > 0 {
		for _, b := range u.buffs {
			if m, ok := b.Mods[name]; ok {
				if rest = m.Set(rest); rest <= 0 {
					break
				}
			}
		}
	}
}

func (u *Unit) ChangeStat(name string, d int) {
	rest := d - u.stats[name].Change(d)
	if rest > 0 {
		for _, b := range u.buffs {
			if m, ok := b.Mods[name]; ok {
				rest -= m.Change(rest)
			}
			if rest <= 0 {
				break
			}
		}
	}
}

func (u *Unit) AP() int      { return u.GetStat("ActionPoints") / 100 }
func (u *Unit) setAP(v int)  { u.SetStat("ActionPoints", v*100) }
func (u *Unit) Health() int  { return u.GetStat("Health") }
func (u *Unit) Armor() int   { return u.GetStat("Armor") }
func (u *Unit) CanAct() bool { return u.AP() > 0 }

func (u *Unit) String() string {
	name := u.Name
	for _, b := range u.buffs {
		name += " +" + b.Kind
	}
	return fmt.Sprintf("%s\nHP %d/%d  AP %d/%d  ARM %d  ACC %d", name, u.Health(), u.Rules.Stats["Health"],
		u.AP(), u.Rules.Stats["ActionPoints"], u.Armor(), u.GetStat("Accuracy"))
}

func (u *Unit) WeaponDescription() string {
	s := ""
	if u.Melee != nil {
		s = u.Melee.String()
	}
	if u.Ranged != nil {
		if s != "" {
			s += "/"
		}
		s += u.Ranged.String()
	}
	return s
}

func (u *Unit) LoadRules(unitType string, pl *Player, lo *RuleLoadout) {
	u.Player = pl
	for _, wt := range lo.Weapons {
		wr := mod.weapons[wt]
		if wr.IsMelee {
			u.Melee = &Weapon{wr}
		} else {
			u.Ranged = &Weapon{wr}
		}
	}
	u.Rules = mod.units[unitType]
	u.Name = mod.Tr(unitType)
	u.Skills = u.Rules.UnitSkills
	u.stats = map[string]*Stat{}
	for k, v := range u.Rules.Stats {
		if k == "ActionPoints" {
			v *= 100
		}
		u.stats[k] = NewStat(v)
	}
	u.card = u.stats["ActionPoints"].Max / u.stats["Movement"].Max
	u.diag = int(float64(u.card) * mod.Options.DiagonalMoveCost)
	u.pf = &Pathfinder{m: u.g.m, card: float64(u.card), diag: float64(u.diag)}
	u.pfDirty = true
	for _, o := range u.Rules.OccupiedTiles {
		u.Occupied = append(u.Occupied, P{o[0], o[1]})
	}
}

// ---- movement (Unit.Update / NextMove) ----

func (u *Unit) Move(path []P) {
	if u.Moving {
		return
	}
	u.visIdx, u.mapIdx, u.path, u.Moving = 0, -1, path, true
}

func (u *Unit) update() {
	if u.Moving && now-u.lastMove > 0.3 {
		u.nextMove()
	}
	if u.labelAnim {
		u.labelY += 1.0 / 60
		u.labelAlpha -= 0.005
		if u.labelAlpha < 0 {
			u.labelAnim, u.labelOn = false, false
		}
	}
	if now > u.animStart && now-u.lastAnim > u.animDelay {
		u.frame = (u.frame + 1) % 2
		u.lastAnim = now
	}
}

func (u *Unit) nextMove() {
	if u.visIdx+1 >= len(u.path) {
		u.EndMovement()
		return
	}
	cur, next := u.path[u.visIdx], u.path[u.visIdx+1]
	curMap := cur
	if u.mapIdx > -1 && u.mapIdx < len(u.path) {
		curMap = u.path[u.mapIdx]
	}
	m := u.g.m
	if m.IsFriendlyUnitOn(u, next) { // walk through friendly units
		if u.mapIdx == -1 {
			u.mapIdx = u.visIdx
		}
	} else if !m.MoveUnit(u, curMap, next) {
		u.EndMovement()
		return
	} else {
		u.mapIdx = -1
	}
	u.lastMove = now
	cost := u.card
	if cur.X != next.X && cur.Y != next.Y {
		cost = u.diag
	}
	u.SetStat("ActionPoints", u.GetStat("ActionPoints")-cost)
	u.Cell = next
	u.faceTowards(next.X - cur.X)
	u.g.unitStepCompleted(u)
	u.visIdx++
}

func (u *Unit) EndMovement() {
	if !u.Moving {
		return
	}
	if u.mapIdx > -1 && u.mapIdx < len(u.path) {
		u.Cell = u.path[u.mapIdx] // stopped while passing a friendly unit: step back to where the map has us
	}
	u.Moving, u.visIdx, u.path = false, 0, nil
	u.g.unitFinishedMoving(u)
}

// faceTowards mirrors SetDirection: east and north look right, everything else left.
func (u *Unit) faceTowards(dx int) { u.flipX = dx > 0 }

func (u *Unit) TurnTowards(p P) { u.flipX = p.X > u.Cell.X }

// ---- reachability & targets ----

func (u *Unit) ensurePaths() {
	if !u.pfDirty {
		return
	}
	u.pf.MaxReachableCost = float64(u.GetStat("ActionPoints"))
	u.pf.Run(u, u.Cell)
	if u.AP() == 0 {
		u.pf.Reachable = map[P]bool{}
	}
	u.pfDirty = false
}

func (u *Unit) UpdateReachable() { u.pfDirty = true }
func (u *Unit) UpdateTargets()   { u.targets = nil }
func (u *Unit) Path(goal P) []P  { u.ensurePaths(); return u.pf.Path(goal) }

// pathCost is the AP (x100) needed to walk to p, +Inf if unreachable.
func (u *Unit) pathCost(p P) float64 { u.ensurePaths(); return u.pf.Cost(p) }

func (u *Unit) Reachable() map[P]bool {
	u.ensurePaths()
	res := map[P]bool{}
	for p := range u.pf.Reachable {
		if !u.g.m.IsUnitOn(p) {
			res[p] = true
		}
	}
	return res
}

func (u *Unit) reachableSplit(withAttack bool) []P {
	scaled := float64((u.AP() - u.minAttackCost()) * 100)
	var res []P
	for p := range u.Reachable() {
		if (u.pf.Cost(p) <= scaled) == withAttack {
			res = append(res, p)
		}
	}
	return res
}

func (u *Unit) minAttackCost() int {
	c := math.MaxInt32
	if u.Melee != nil {
		c = min(c, u.Melee.Rules.UseCost)
	}
	if u.Ranged != nil {
		c = min(c, u.Ranged.Rules.UseCost)
	}
	return c
}

func (u *Unit) CanAttack() bool       { return u.AP() >= u.minAttackCost() }
func (u *Unit) CanAffordRanged() bool { return u.Ranged != nil && u.AP() >= u.Ranged.Rules.UseCost }
func (u *Unit) CanAffordMelee() bool  { return u.Melee != nil && u.AP() >= u.Melee.Rules.UseCost }

func (u *Unit) ValidRangedTargets() map[P]bool {
	if u.targets == nil {
		u.targets = u.RangedTargetsAt(u.Cell)
	}
	return u.targets
}

// canShootFrom tells whether u could shoot at t when standing at from (RangedTargetsAt for a single tile).
func (u *Unit) canShootFrom(from, t P) bool {
	if u.Ranged == nil || from == t ||
		!mod.Options.AllowRangedWeaponsRangeOne && AreCardinalNeighbors(from, t) ||
		!mod.Options.AllowDiagonalAdjacentAttacks && AreDiagonalNeighbors(from, t) {
		return false
	}
	r := u.Ranged.Rules
	if r.StraightLineOnly {
		return u.RangedTargetsAt(from)[t]
	}
	d, rr := t.Sub(from), r.EffectiveRange+1
	if d.X*d.X+d.Y*d.Y >= rr*rr {
		return false
	}
	ok, _ := u.g.m.lineCast(from, t, u, true)
	return ok
}

func (u *Unit) hasBuff(kind string) bool {
	for _, b := range u.buffs {
		if b.Kind == kind {
			return true
		}
	}
	return false
}

func (u *Unit) CanAttackUnit(o *Unit) bool {
	return u.CanAttack() && (u.Melee != nil && AreCardinalNeighbors(u.Cell, o.Cell) ||
		u.Ranged != nil && u.ValidRangedTargets()[o.Cell])
}

func (u *Unit) RangedTargetsAt(pos P) map[P]bool {
	res := map[P]bool{}
	if u.Ranged == nil {
		return res
	}
	r := u.Ranged.Rules
	if r.StraightLineOnly {
		for _, d := range append(append([]P{}, Cardinal...), Diagonal...) {
			for p := range u.g.m.StraightLine(pos, d, r.EffectiveRange) {
				res[p] = true
			}
		}
	} else {
		res = u.g.m.FOV(u, pos, r.EffectiveRange, false, false, true)
	}
	delete(res, pos)
	if !mod.Options.AllowRangedWeaponsRangeOne {
		for _, d := range Cardinal {
			delete(res, pos.Add(d))
		}
	}
	if !mod.Options.AllowDiagonalAdjacentAttacks {
		for _, d := range Diagonal {
			delete(res, pos.Add(d))
		}
	}
	return res
}

// UpdateFoV returns enemies that just became visible.
func (u *Unit) UpdateFoV(clearVisual, clearMech bool) []*Unit {
	prev := u.Visible
	u.FOV = u.g.m.FOV(u, u.Cell, u.GetStat("VisionRange"), clearVisual, clearMech, false)
	u.Visible = map[*Unit]bool{}
	var appeared []*Unit
	for p := range u.FOV {
		if u.g.m.IsOtherFactionOn(u.Player, p) {
			o := u.g.m.UnitAt(p)
			u.Visible[o] = true
			if !prev[o] {
				appeared = append(appeared, o)
			}
		}
	}
	return appeared
}

func (u *Unit) IsNextTo(t *Unit) bool {
	for _, a := range t.Occupied {
		for _, b := range u.Occupied {
			if AreCardinalNeighbors(u.Cell.Add(b), t.Cell.Add(a)) {
				return true
			}
		}
	}
	return false
}

// ---- buffs & procs (ProcReceiver.cs) ----

func (u *Unit) ApplyBuff(b *Buff) {
	b.Unit = u
	u.buffs = append(u.buffs, b)
	if b.Kind == BuffStun {
		u.DisableActions()
	}
	u.checkBuffEnd()
}

func (u *Unit) checkBuffEnd() {
	var keep []*Buff
	for _, b := range u.buffs {
		if !b.ended() {
			keep = append(keep, b)
		}
	}
	u.buffs = keep
}

func (u *Unit) procNextTurn() {
	for _, b := range u.buffs {
		b.TurnsAlive++
	}
	u.checkBuffEnd()
	for _, b := range u.buffs {
		if b.Kind == BuffStun {
			u.DisableActions()
		}
	}
}

func (u *Unit) procHasAttacked() {
	for _, b := range u.buffs {
		b.AttacksAlive++
	}
	u.checkBuffEnd()
}

func (u *Unit) procHit(target *Unit) {
	for _, b := range u.buffs {
		if b.Kind == BuffShred {
			target.ChangeStat("Armor", -1)
		}
	}
	u.checkBuffEnd()
}

// ---- turn handling ----

func (u *Unit) PrepareNewPhase() {
	u.setAP(u.Rules.Stats["ActionPoints"])
	u.UpdateTargets()
	u.UpdateReachable()
	u.procNextTurn()
}

func (u *Unit) DisableActions() {
	u.setAP(0)
	u.UpdateReachable()
}

func (u *Unit) DeductActionPoints(c int) {
	u.setAP(u.AP() - c)
	u.UpdateReachable()
}

func (u *Unit) DamageReceived(dmg int) {
	u.SetStat("Health", u.Health()-dmg)
	u.ShowHealth = true
	u.healthPulse = now + 0.5
}

func (u *Unit) SetLabel(s string, animate bool) {
	u.label, u.labelOn, u.labelAnim, u.labelY, u.labelAlpha = s, true, animate, 0, 1
}

func (u *Unit) markerColor() color.Color {
	max, ap := u.Rules.Stats["ActionPoints"], u.AP()
	switch {
	case ap == max:
		return color.RGBA{0, 255, 0, 255}
	case ap < 1:
		return color.RGBA{128, 128, 128, 255}
	case ap < max/4:
		return color.RGBA{255, 0, 0, 255}
	case ap < max/2:
		return color.RGBA{255, 235, 4, 255}
	}
	return color.RGBA{0, 0, 255, 255}
}

// ---- PercentageBasedCombat ----

// hitCalc is CalculateChanceToHit split into its parts, for the targeting breakdown.
type hitCalc struct{ Acc, Weapon, Defense, Range, Total int }

func hitChance(a *Unit, from P, t *Unit, melee bool) (hitCalc, bool) {
	w := a.Ranged
	if melee {
		w = a.Melee
	}
	if w == nil {
		return hitCalc{}, false
	}
	h := hitCalc{Acc: a.GetStat("Accuracy"), Weapon: w.Rules.AccuracyModifier, Defense: t.GetStat("Defense")}
	acc := float64(h.Acc + h.Weapon - h.Defense)
	dist := from.Sub(t.Cell).Mag()
	pen := 0.0
	if dist < float64(w.Rules.MinRange) {
		pen = (float64(w.Rules.MinRange) - dist) * float64(w.Rules.MinDropOff)
	}
	if dist > float64(w.Rules.MaxRange) {
		pen = (dist - float64(w.Rules.MaxRange)) * float64(w.Rules.MaxDropOff)
	}
	if dist > float64(w.Rules.EffectiveRange) {
		pen = acc
	}
	h.Range = int(pen)
	h.Total = int(math.Min(math.Max(acc-pen, 0), 100))
	return h, true
}

func chanceToHit(a, t *Unit, melee bool) int {
	h, ok := hitChance(a, a.Cell, t, melee)
	if !ok {
		return -1
	}
	return h.Total
}

func applyDamage(a, t *Unit, dmg int) {
	a.procHit(t)
	dmg = max(dmg-t.Armor(), mod.Options.MinimumDamage) // a hit always hurts
	t.SetLabel(fmt.Sprint(dmg), true)
	t.DamageReceived(dmg)
}

func resolveAttack(w *Weapon, dmg, roll int, a, t *Unit) {
	if roll < chanceToHit(a, t, a.Melee == w) {
		applyDamage(a, t, dmg)
	} else {
		t.SetLabel("mis", true)
	}
}
