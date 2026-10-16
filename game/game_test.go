package main

import (
	"fmt"
	"io/fs"
	"path"
	"strings"
	"testing"

	"github.com/hajimehoshi/ebiten/v2"
)

// headless loads the rules and blank sprites so battles run without a window.
func headless(t *testing.T) {
	var err error
	if mod, err = LoadMod("percent.rul"); err != nil {
		t.Fatal(err)
	}
	blank := func(n int) []*ebiten.Image { return make([]*ebiten.Image, n) }
	spr = Sprites{FactionRows: [][]*ebiten.Image{blank(32), blank(32), blank(32), blank(32), blank(32)},
		Blood: blank(4), Explosion: blank(6), Hits: map[string][]*ebiten.Image{"slash": blank(3), "hammer": blank(3), "swirl": blank(3), "sparkle": blank(4)}, Flash: blank(7),
		WallSets: []WallSet{{Sprites: blank(16), Damaged: blank(8), Destroyed: blank(3), NorthAlt: blank(6), NorthChance: 5}, {Sprites: blank(16), Destroyed: blank(3)}}}
	loadUI()
}

// TestAIMatch plays whole matches with every player controlled by the AI, headless.
func TestAIMatch(t *testing.T) {
	headless(t)
	for seed := int64(1); seed <= 3; seed++ {
		seedGen(seed)
		m := GenerateBSP(21, 21, 3)
		if sp := m.SpawnPoints(); len(sp) != 3 {
			t.Fatalf("seed %d: want 3 spawn zones, got %d", seed, len(sp))
		}
		play(t, NewBattle(m, ModeSkirmish, []int{0, 1, 2}))
	}
	for i := range mod.Maps {
		play(t, NewBattle(MapFromRules(mod.Maps[i], 4), ModeCampaign, []int{0}))
	}
}

func play(t *testing.T, g *Battle) {
	start := len(g.units)
	for _, p := range g.players {
		p.AI = NewTacticalAI(g, p)
	}
	g.CurrentPlayer().AI.StartPhase() // the first phase may already be running
	for i := 0; i < 60*60*30 && !g.over; i++ {
		now += 1.0 / 60
		g.tick()
		if g.state == StateWaitForHotSeatPlayer {
			g.EndPhase()
		}
	}
	t.Logf("turns=%d units %d -> %d over=%v", g.Turn, start, len(g.units), g.over)
	if g.Turn < 3 {
		t.Errorf("match stalled: turns=%d units %d -> %d", g.Turn, start, len(g.units))
	}
}

// TestGenerators: every generator yields a map where all spawn zones share one connected region.
func TestGenerators(t *testing.T) {
	headless(t)
	GenerateFromScript(mod.Maps[1]) // must not panic; the block data has no usable layout yet
	for seed := int64(1); seed <= 20; seed++ {
		seedGen(seed)
		for name, m := range map[string]*Map{
			"cavern":   withSpawns(4, func() *Map { return GenerateCavern(21, 21) }),
			"dungeon":  withSpawns(4, func() *Map { return GenerateDungeon(21, 21) }),
			"mission2": MapFromRules(mod.Maps[1], 4),
		} {
			sp := m.SpawnPoints()
			if len(sp) < 4 {
				t.Fatalf("%s seed %d: %d spawn zones", name, seed, len(sp))
			}
			reach := map[P]bool{}
			m.flood(sp[0][:1], func(p P, _ int) { reach[p] = true })
			for f, zone := range sp {
				if len(zone) == 0 || !reach[zone[0]] {
					t.Fatalf("%s seed %d: spawn zone %d unreachable\n%s", name, seed, f, m.ascii())
				}
			}
			if seed == 1 {
				t.Logf("%s\n%s", name, m.ascii())
			}
		}
	}
}

func (m *Map) ascii() string {
	s := ""
	for y := m.H - 1; y >= 0; y-- {
		for x := 0; x < m.W; x++ {
			p := P{x, y}
			switch {
			case m.IsWallAt(p):
				s += "#"
			case m.IsDoorAt(p):
				s += "D"
			case m.floor[m.i(p)] >= 10:
				s += fmt.Sprint(m.floor[m.i(p)] - 10)
			default:
				s += "."
			}
		}
		s += "\n"
	}
	return s
}

// TestSounds: every embedded sound decodes (Ebiten's WAV decoder only reads 8/16-bit).
func TestSounds(t *testing.T) {
	fs.WalkDir(assetFS, "assets", func(p string, d fs.DirEntry, _ error) error {
		if ext := path.Ext(p); !d.IsDir() && (ext == ".wav" || ext == ".mp3") && decodeSound(strings.TrimPrefix(p, "assets/")) == nil {
			t.Error("cannot decode", p)
		}
		return nil
	})
}

// TestSkirmishVsAI: a human ends the turn, the computer plays under the human's fog and hands back.
func TestSkirmishVsAI(t *testing.T) {
	headless(t)
	seedGen(1)
	g := NewBattle(GenerateBSP(21, 21, 2), ModeSkirmish, []int{0, 1})
	human := g.players[0]
	if human.AI != nil || g.players[1].AI == nil {
		t.Fatal("want human vs computer")
	}
	for i := 0; i < 60*60*5 && g.state == StateWaitForHotSeatPlayer; i++ {
		now += 1.0 / 60
		g.tick()
	}
	for turn := g.Turn; g.Turn < turn+3 && !g.over; {
		if g.CurrentPlayer() == human {
			g.EndPhase()
		}
		now += 1.0 / 60
		g.tick()
		if g.PoV != human {
			t.Fatal("PoV switched to the computer")
		}
	}
}
