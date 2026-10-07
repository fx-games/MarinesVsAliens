package main

import (
	"fmt"
	"image/color"
	"math"
	"math/rand"
	"strings"
)

// ---- TargetMode.cs ----

type affected struct {
	target P
	tiles  []P
}

func toPs(l [][]int) []P {
	var ps []P
	for _, v := range l {
		ps = append(ps, P{v[0], v[1]})
	}
	return ps
}

func (g *Battle) affectedTiles(tm *RuleTargeting, src P, targets []P) []affected {
	var res []affected
	if tm.Type == TargetSelf {
		return []affected{{targets[0], []P{src}}}
	}
	if rel := toPs(tm.AffectedTiles); len(rel) > 0 {
		for _, t := range targets {
			var tiles []P
			for _, r := range rel {
				tiles = append(tiles, t.Add(r))
			}
			res = append(res, affected{t, tiles})
		}
		return res
	}
	switch {
	case tm.ActiveAffectedPreset == AffectedStraightLine:
		for _, t := range targets {
			res = append(res, affected{t, append(g.straightLineTo(src, t), t)})
		}
	case tm.ActiveAffectedPreset == AffectedExplosion && tm.AffectedRange > 0:
		r := tm.AffectedRange
		for _, t := range targets {
			var tiles []P
			for i := t.X - r; i <= t.X+r; i++ {
				for j := t.Y - r; j <= t.Y+r; j++ {
					if math.Floor(t.Sub(P{i, j}).Mag()) <= float64(r) {
						tiles = append(tiles, P{i, j})
					}
				}
			}
			res = append(res, affected{t, tiles})
		}
	}
	return res
}

func (g *Battle) straightLineTo(a, b P) []P {
	d := P{sign(b.X - a.X), sign(b.Y - a.Y)}
	var res []P
	for prev, next := a, a.Add(d); next != b; prev, next = next, next.Add(d) {
		if !g.m.IsNeighborVisible(prev, next) {
			break
		}
		res = append(res, next)
	}
	return res
}

func sign(x int) int {
	switch {
	case x > 0:
		return 1
	case x < 0:
		return -1
	}
	return 0
}

// hasTargets: the action has something worth aiming at (an offensive action needs more than friendlies).
func (g *Battle) hasTargets(a *UnitAction) bool {
	for _, p := range g.validTargetPositions(&a.tm, a.Unit) {
		if !a.Skill.IsOffensiveAction || !g.m.IsFriendlyUnitOn(a.Unit, p) {
			return true
		}
	}
	return false
}

func (g *Battle) noTargetReason(a *UnitAction) string {
	switch a.tm.AllowedTargetPreset {
	case PresetCardinalMelee:
		return "No enemy right next to this unit."
	case PresetRangedAttack:
		return "No visible enemy in range with a clear line of fire."
	}
	return "Nothing to target from here."
}

// targetRules describes, for the tooltip, what the action can target and how range affects it.
func (a *UnitAction) targetRules() string {
	tm, w := &a.tm, a.weapon()
	var kinds []string
	for _, k := range []struct {
		bit  TargetTypes
		name string
	}{{TTEnemies, "enemies"}, {TTFriendlies, "own units"}, {TTFloor, "empty floor"}, {TTWall, "walls"}} {
		if tm.AllowedTargetTypes&k.bit != 0 {
			kinds = append(kinds, k.name)
		}
	}
	var t []string
	if tm.Type != TargetSelf && len(kinds) > 0 {
		t = append(t, "Targets: "+strings.Join(kinds, ", ")+".")
	}
	switch tm.AllowedTargetPreset {
	case PresetCardinalMelee:
		t = append(t, "Reach: the 4 tiles right next to the unit, not diagonal.")
	case PresetAllVisible:
		t = append(t, "Reach: any tile this unit can see.")
	case PresetAllReachable:
		t = append(t, "Reach: any tile this unit can walk to.")
	case PresetRangedAttack:
		if w == nil {
			break
		}
		r := w.Rules
		reach := "Reach: anything in line of sight"
		if r.StraightLineOnly {
			reach = "Reach: straight lines and diagonals only, stopped by walls and units"
		}
		if r.EffectiveRange < 40 {
			reach += fmt.Sprintf(", up to %d tiles", r.EffectiveRange)
		}
		if !mod.Options.AllowRangedWeaponsRangeOne {
			reach += "; not the tiles right next to the unit"
		}
		t = append(t, reach+".")
		if a.rollsToHit() {
			acc := fmt.Sprintf("Accuracy: best up to %d tiles, then -%d%% per tile", r.MaxRange, r.MaxDropOff)
			if r.MinRange > 1 {
				acc += fmt.Sprintf("; -%d%% per tile closer than %d", r.MinDropOff, r.MinRange)
			}
			t = append(t, acc+".")
		}
	}
	switch {
	case tm.ActiveAffectedPreset == AffectedExplosion && tm.AffectedRange > 0:
		t = append(t, fmt.Sprintf("Blast: hits everyone within %d tile of the target, walls too.", tm.AffectedRange))
	case tm.ActiveAffectedPreset == AffectedStraightLine:
		t = append(t, "Hits every unit along the line of fire.")
	}
	if tm.Type == TargetMultiple {
		split := ""
		if w != nil && w.Rules.DamageDistribution {
			split = ", damage is split between them"
		}
		t = append(t, "Pick several targets"+split+", then press the button again.")
	}
	return strings.Join(t, "\n")
}

// whyNotTarget explains why isValidTarget rejects p.
func (g *Battle) whyNotTarget(tm *RuleTargeting, u *Unit, p P) string {
	m := g.m
	switch {
	case m.InFog(p):
		return "You can't see that tile."
	case tm.AllowedTargetTypes&TTWall == 0 && m.IsWallAt(p):
		return "Can't target walls."
	case tm.AllowedTargetTypes&TTFloor == 0 && m.IsEmptyFloorTile(p):
		return "Needs a unit as target."
	case tm.AllowedTargetTypes&TTFriendlies == 0 && m.IsFriendlyUnitOn(u, p):
		return "Can't target your own units."
	case tm.AllowedTargetTypes&TTEnemies == 0 && m.IsOtherFactionOn(u.Player, p):
		return "Can't target enemies."
	}
	switch tm.AllowedTargetPreset {
	case PresetRangedAttack:
		if u.Ranged == nil {
			return "No ranged weapon."
		}
		d, rr := p.Sub(u.Cell), u.Ranged.Rules.EffectiveRange+1
		switch {
		case p == u.Cell:
			return "Can't target yourself."
		case !mod.Options.AllowRangedWeaponsRangeOne && AreCardinalNeighbors(u.Cell, p), !mod.Options.AllowDiagonalAdjacentAttacks && AreDiagonalNeighbors(u.Cell, p):
			return "Too close for a ranged weapon, use melee."
		case d.X*d.X+d.Y*d.Y >= rr*rr:
			return fmt.Sprintf("Out of range (%d tiles).", u.Ranged.Rules.EffectiveRange)
		case u.Ranged.Rules.StraightLineOnly:
			return "Only fires in straight lines and diagonals."
		}
		return "No clear line of fire."
	case PresetAllVisible:
		return "Not in this unit's view."
	case PresetAllReachable:
		return "This unit can't reach it."
	case PresetCardinalMelee:
		return "Must be right next to the unit (no diagonals)."
	}
	return "Not a valid target for this action."
}

func (g *Battle) isValidTarget(tm *RuleTargeting, u *Unit, p P) bool {
	m := g.m
	if tm.AllowedTargetTypes&TTWall == 0 && m.IsWallAt(p) ||
		tm.AllowedTargetTypes&TTFloor == 0 && m.IsEmptyFloorTile(p) ||
		tm.AllowedTargetTypes&TTFriendlies == 0 && m.IsFriendlyUnitOn(u, p) ||
		tm.AllowedTargetTypes&TTEnemies == 0 && m.IsOtherFactionOn(u.Player, p) {
		return false
	}
	switch tm.AllowedTargetPreset {
	case PresetRangedAttack:
		return u.ValidRangedTargets()[p]
	case PresetAllReachable:
		return u.Reachable()[p]
	case PresetAllVisible:
		return u.FOV[p]
	case PresetCardinalMelee:
		return AreCardinalNeighbors(u.Cell, p)
	}
	for _, a := range toPs(tm.AllowedTargets) {
		if a == p {
			return true
		}
	}
	return false
}

// validTargetPositions lists the tiles to highlight; only those isValidTarget accepts, so every red tile fires.
func (g *Battle) validTargetPositions(tm *RuleTargeting, u *Unit) []P {
	var ps []P
	for _, p := range g.targetCandidates(tm, u) {
		if g.m.Contains(p) && g.isValidTarget(tm, u, p) {
			ps = append(ps, p)
		}
	}
	return ps
}

func (g *Battle) targetCandidates(tm *RuleTargeting, u *Unit) []P {
	set := func(m map[P]bool) []P {
		var ps []P
		for p := range m {
			ps = append(ps, p)
		}
		return ps
	}
	switch tm.AllowedTargetPreset {
	case PresetRangedAttack:
		return set(u.ValidRangedTargets())
	case PresetAllReachable:
		return set(u.Reachable())
	case PresetAllVisible:
		return set(u.FOV)
	case PresetCardinalMelee:
		var ps []P
		for _, d := range Cardinal {
			ps = append(ps, u.Cell.Add(d))
		}
		return ps
	}
	return toPs(tm.AllowedTargets)
}

// ---- UnitAction.cs ----

type UnitAction struct {
	g       *Battle
	Skill   *RuleSkill
	Unit    *Unit
	tm      RuleTargeting // the skill's targeting, adjusted for the weapon used
	targets []P
}

var knownBuffs = map[string]bool{BuffStat: true, BuffStun: true, BuffShred: true}

func (g *Battle) newAction(skill string, u *Unit) *UnitAction {
	a := &UnitAction{g: g, Skill: mod.skills[skill], Unit: u}
	a.tm = a.Skill.TargetMode
	// weapon rules the original declared but never used
	if w := a.weapon(); w != nil && a.Skill.IsOffensiveAction && !a.Skill.IsMelee && a.tm.ActiveAffectedPreset == AffectedNone {
		r := w.Rules
		if r.MultipleTargets {
			a.tm.Type = TargetMultiple
		}
		switch {
		case r.DamageLineOfFire:
			a.tm.AffectedTiles, a.tm.ActiveAffectedPreset = nil, AffectedStraightLine
		case r.Radius > 0:
			a.tm.AffectedTiles, a.tm.ActiveAffectedPreset, a.tm.AffectedRange = nil, AffectedExplosion, r.Radius
		}
	}
	return a
}

// cost is the AP the skill takes: apCost from the rules, else the weapon's cost for attacks, else options.skillAPCost.
func (a *UnitAction) cost() int {
	switch {
	case a.Skill.APCost != nil:
		return *a.Skill.APCost
	case a.Skill.IsOffensiveAction && a.weapon() != nil:
		return a.weapon().Rules.UseCost
	}
	return mod.Options.SkillAPCost
}

// preview is the hit chance shown over a target before the player commits.
func (a *UnitAction) preview(t *Unit) string {
	if !a.Skill.IsOffensiveAction {
		return ""
	}
	if !a.rollsToHit() {
		return "100%"
	}
	c := chanceToHit(a.Unit, t, a.Skill.IsMelee)
	if c < 0 {
		return ""
	}
	return fmt.Sprint(c, "%")
}

// rollsToHit: explosions (Explosion effect) always hit, attacks roll.
func (a *UnitAction) rollsToHit() bool {
	for _, e := range a.Skill.Effects {
		if e.Name == "Explosion" {
			return false
		}
	}
	return true
}

func (a *UnitAction) weapon() *Weapon {
	if a.Skill.IsMelee && a.Unit.Melee != nil {
		return a.Unit.Melee
	}
	return a.Unit.Ranged
}

func (a *UnitAction) StartTargeting() {
	g, u, tm := a.g, a.Unit, &a.tm
	if u.AP() < a.cost() || g.skillBlocked(u, a.Skill.Name) != "" {
		return
	}
	if tm.Type == TargetSelf {
		a.executeOn([]P{u.Cell}, []affected{{u.Cell, []P{u.Cell}}})
		return
	}
	if u.Moving {
		return
	}
	g.m.ClearHighlights(0)
	g.m.ClearHighlights(1)
	valid := g.validTargetPositions(tm, u)
	g.highlightTiles(valid, rgba(0.6, 0, 0, 0.5), 0)
	if g.CurrentPlayer() == g.PoV {
		for _, p := range valid {
			if g.m.IsOtherFactionOn(u.Player, p) {
				t := g.m.UnitAt(p)
				t.SetLabel(a.preview(t), false)
				t.ShowHealth = true
			}
		}
	}
	if len(valid) == 0 {
		g.toast("NO TARGETS\n" + g.noTargetReason(a))
		return
	}
	if a.Skill.ChangeSpriteTo != -1 {
		u.spriteOff = a.Skill.ChangeSpriteTo
	} else {
		u.spriteOff = u.origOff
	}
	g.state = StateAttackTargetSelection
	a.targets = nil
}

func (a *UnitAction) TargetSelected(p P) bool {
	if !a.g.isValidTarget(&a.tm, a.Unit, p) {
		return false
	}
	for _, t := range a.targets {
		if t == p {
			return false
		}
	}
	a.g.m.MarkTile(p)
	a.targets = append(a.targets, p)
	a.Unit.TurnTowards(p)
	return true
}

func (a *UnitAction) ConfirmTargets() {
	a.g.ToDefaultState()
	a.executeOn(a.targets, a.g.affectedTiles(&a.tm, a.Unit.Cell, a.targets))
	a.targets = nil
}

type effect struct {
	r       *RuleEffect
	action  *UnitAction
	src     P
	targets []P
	aff     []affected
	dmg     int
	roll    int
	weapon  *Weapon
	onDone  []func()
}

func (a *UnitAction) executeOn(targets []P, aff []affected) {
	u := a.Unit
	a.g.sendCmd(u, cmd{Skill: a.Skill.Name, Targets: targets})
	roll := dice.Intn(100)
	dmg := 0
	if a.Skill.IsOffensiveAction {
		if a.Skill.IsMelee {
			dmg = rollDamage(u.Melee.Rules)
		} else {
			dmg = rollDamage(u.Ranged.Rules)
		}
	}
	weapon := a.weapon()
	if weapon != nil && weapon.Rules.DamageDistribution && len(targets) > 1 {
		dmg = (dmg + len(targets) - 1) / len(targets) // split over the targets, rounded up
	}
	// Effects run back to front: a chained effect is executed when its predecessor completes,
	// and the last chained effect in the list triggers AfterAction.
	afterActionPending := false
	var chained *effect
	for i := len(a.Skill.Effects) - 1; i >= 0; i-- {
		r := a.Skill.Effects[i]
		if !knownEffects[r.Name] {
			continue
		}
		e := &effect{r: r, action: a, src: u.Cell, targets: targets, aff: aff, dmg: dmg, roll: roll, weapon: weapon}
		if chained != nil {
			next := chained
			e.onDone = append(e.onDone, next.execute)
			chained = nil
		}
		if r.Chained {
			chained = e
			if !afterActionPending {
				afterActionPending = true
				e.onDone = append(e.onDone, a.afterAction)
			}
		} else {
			e.execute()
		}
	}
	for _, rb := range a.Skill.Buffs {
		if !knownBuffs[rb.Name] {
			continue
		}
		for _, af := range aff {
			for _, p := range af.tiles {
				if t := a.g.m.UnitAt(p); t != nil {
					t.ApplyBuff(NewBuff(rb))
				}
			}
		}
	}
	if !afterActionPending {
		a.afterAction()
	}
}

func (a *UnitAction) afterAction() {
	g, u := a.g, a.Unit
	u.spriteOff = u.origOff
	if a.Skill.IsOffensiveAction {
		u.procHasAttacked()
	}
	u.DeductActionPoints(a.cost())
	g.CheckForDeadUnits()
	a.targets = nil
	g.m.ClearMarkers()
	g.UpdateUIButtons()
	if g.m.FOVDirty {
		g.GlobalFOVUpdate()
	}
}

// ---- WorldEffects ----

var knownEffects = map[string]bool{"FlashEffect": true, "Attack": true, "Explosion": true, "ExplosionEffect": true, "MeleeHitEffect": true,
	"OpenDoorEffect": true, "ProjectileEffect": true, "SpawnUnit": true, "SpriteEffect": true}

func (e *effect) done() {
	for _, f := range e.onDone {
		f()
	}
}

func (e *effect) execute() {
	g, u := e.action.g, e.action.Unit
	switch e.r.Name {
	case "FlashEffect": // a coloured flash on the affected tiles (stun, aim, deploy)
		for _, af := range e.aff {
			for _, p := range af.tiles {
				f := g.addAnim(spr.Flash, p, 0.04, nil)
				f.layer, f.ppu, f.tint = layerUnits, 32.0/8, flashColors[e.r.StringOne]
			}
		}
		playSound(sndSelect, 0.6)
	case "SpriteEffect": // a named animation on the affected tiles (e.g. the drone deploy sparkle)
		for _, af := range e.aff {
			for _, p := range af.tiles {
				a := g.addAnim(spr.Hits[e.r.StringOne], p, 0.08, nil)
				a.layer, a.ppu = layerUnits, 20
			}
		}
		playSound(sndSelect, 0.6)
	case "Attack":
		for _, af := range e.aff {
			for _, p := range af.tiles {
				if t := g.m.UnitAt(p); t != nil {
					t.TurnTowards(u.Cell)
					resolveAttack(e.weapon, e.dmg, e.roll, u, t)
				}
			}
		}
	case "Explosion":
		walls := map[P]bool{}
		for _, af := range e.aff {
			for _, p := range af.tiles {
				if !g.m.ExplosionReaches(af.target, p, e.dmg, walls) {
					continue
				}
				if t := g.m.UnitAt(p); t != nil {
					t.TurnTowards(u.Cell)
					applyDamage(u, t, e.dmg)
				}
			}
		}
	case "ExplosionEffect":
		for _, af := range e.aff {
			for _, p := range af.tiles {
				if g.m.ExplosionReaches(af.target, p, 0, map[P]bool{}) {
					g.addAnim(spr.Explosion, p, 0.1, nil)
				}
			}
		}
	case "MeleeHitEffect":
		for _, af := range e.aff {
			for _, p := range af.tiles {
				hit := "slash"
				if w := e.weapon; w != nil && w.Rules.MeleeHit != "" {
					hit = w.Rules.MeleeHit
				}
				g.addAnim(spr.Hits[hit], p, 0.1, nil)
				playSound(sndSwords[rand.Intn(len(sndSwords))], 1)
			}
		}
	case "OpenDoorEffect":
		for _, af := range e.aff {
			for _, p := range af.tiles {
				if g.m.IsDoorAt(p) {
					g.m.RemoveDoor(p)
					continue
				}
				for _, d := range Cardinal {
					if g.m.IsDoorAt(p.Add(d)) {
						g.m.RemoveDoor(p.Add(d))
						break
					}
				}
			}
		}
		g.GlobalFOVUpdate()
		g.UpdateVisibleUnits(u)
	case "ProjectileEffect":
		count := e.r.IntegerTwo
		if w := e.weapon; w != nil && w.Rules.Projectile.Count > 0 && e.action.Skill.Name == "RangedAttack" {
			count = w.Rules.Projectile.Count
		}
		timer := 0.0
		for _, t := range e.targets {
			for i := 0; i < count; i++ {
				last := i == count-1
				t := t
				g.schedule(timer, func() {
					g.fireProjectile(e, t, last)
				})
				timer += 0.1 + rand.Float64()*0.201
			}
		}
		return // completes when the last projectile arrives
	case "SpawnUnit":
		for _, af := range e.aff {
			for _, p := range af.tiles {
				g.SpawnUnit(p, e.r.StringOne, mod.Loadout(u.Player.Faction, e.r.StringOne), u.Player)
			}
		}
	}
	e.done()
}

type projectile struct {
	x, y, tx, ty, speed, dist0 float64
	sprite                     int
	arcing                     bool
	onArrive                   func()
}

func (g *Battle) fireProjectile(e *effect, target P, last bool) {
	sx, sy := e.src.Center()
	tx, ty := target.Center()
	tx += rand.Float64() - 0.5
	ty += rand.Float64() - 0.5
	p := &projectile{x: sx, y: sy, tx: tx, ty: ty, speed: e.r.FloatOne, sprite: e.r.IntegerOne, arcing: e.r.BoolOne}
	if w := e.weapon; w != nil && w.Rules.Projectile.Count > 0 && e.action.Skill.Name == "RangedAttack" {
		pr := w.Rules.Projectile // each weapon looks different
		p.speed, p.sprite, p.arcing = pr.Speed, pr.Sprite, pr.Arc
	}
	p.dist0 = math.Hypot(tx-sx, ty-sy)
	if last {
		p.onArrive = e.done
	}
	launch := sndIceball
	if p.sprite == 4 {
		launch = sndRocket
	}
	playSound(launch, 0.5)
	g.projectiles = append(g.projectiles, p)
}

var flashColors = map[string]color.Color{
	"yellow": rgba(1, 0.92, 0.2, 1), "red": rgba(1, 0.25, 0.2, 1), "cyan": rgba(0.31, 0.98, 0.98, 1),
}
