package main

import (
	"fmt"
	"math/rand"
	"testing"
)

// recorder sits between an AI and the battle and writes down what it does as online commands.
type recorder struct {
	*Battle
	log *[]cmd
}

func (r recorder) Move(u *Unit, to P) bool {
	c := cmd{Unit: u.Cell, Move: &to}
	ok := r.Battle.Move(u, to)
	if ok {
		*r.log = append(*r.log, c)
	}
	return ok
}

func (r recorder) UseSkill(u *Unit, skill string, p P) bool {
	c := cmd{Unit: u.Cell, Skill: skill, Targets: []P{p}}
	ok := r.Battle.UseSkill(u, skill, p)
	if ok {
		*r.log = append(*r.log, c)
	}
	return ok
}

type endRecorder struct {
	Controller
	log *[]cmd
}

func (e endRecorder) Step() bool {
	more := e.Controller.Step()
	if !more {
		*e.log = append(*e.log, cmd{End: true})
	}
	return more
}

// TestLockstep: an AI match recorded as commands and replayed by two remote seats with the same seed
// must end in the same state. This is what keeps two online clients in sync.
func TestLockstep(t *testing.T) {
	headless(t)
	run := func(seed int64, drive func(g *Battle)) (string, *Battle) {
		seedGen(seed)
		dice = rand.New(rand.NewSource(seed))
		g := NewBattle(GenerateBSP(21, 21, 2), ModeOnline, []int{0, 2}, func(g *Battle, i int, p *Player) {
			p.AI = &remote{g: g, p: p} // placeholder so PoV logic sees no human
		})
		drive(g)
		g.CurrentPlayer().AI.StartPhase()
		for i := 0; i < 60*60*20 && !g.over; i++ {
			now += 1.0 / 60
			rand.Float64() // cosmetic randomness differs between clients
			g.tick()
		}
		s := fmt.Sprint("turn ", g.Turn, " phase ", g.phase)
		for _, u := range g.units {
			s += fmt.Sprintf(" %s@%v hp%d ap%d", u.Rules.Type, u.Cell, u.Health(), u.AP())
		}
		return s, g
	}
	for seed := int64(1); seed <= 3; seed++ {
		logs := [2][]cmd{}
		rec, g := run(seed, func(g *Battle) {
			for i, p := range g.players {
				p.AI = endRecorder{NewTacticalAI(recorder{g, &logs[i]}, p), &logs[i]}
			}
		})
		if !g.over {
			t.Logf("seed %d: match not finished, comparing the state after the recorded turns", seed)
		}
		rep, _ := run(seed, func(g *Battle) {
			for i, p := range g.players {
				p.AI = &remote{g: g, p: p, queue: append([]cmd{}, logs[i]...)}
			}
		})
		if rec != rep {
			t.Fatalf("seed %d: replay diverged\nrecorded: %s\nreplayed: %s", seed, rec, rep)
		}
		t.Logf("seed %d: %d+%d commands, in sync: %s", seed, len(logs[0]), len(logs[1]), rec[:min(len(rec), 80)])
	}
}
