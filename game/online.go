package main

import (
	"bytes"
	"encoding/json"
	"fmt"
	"math/rand"
	"net/http"
	"sync"
	"time"
)

// dice rolls everything that decides a fight (hits and damage). Online, both clients seed it alike and
// replay the same commands in the same order, so they stay in sync; cosmetic randomness uses math/rand.
var dice = rand.New(rand.NewSource(time.Now().UnixNano()))

var onlineURL = "https://ruzzoli.de/mva-battles/api/games"

// cmd is one player action sent to the opponent. Units are addressed by cell (one unit per cell).
type cmd struct {
	Move    *P     `json:"move,omitempty"` // destination
	Skill   string `json:"skill,omitempty"`
	Unit    P      `json:"unit"`
	Targets []P    `json:"targets,omitempty"`
	End     bool   `json:"end,omitempty"`
	Quit    bool   `json:"quit,omitempty"`
}

type startMsg struct {
	Seed     int64    `json:"seed"`
	Factions []int    `json:"factions"`
	Names    []string `json:"names"`
}

type netMsg struct {
	Seat int `json:"seat"`
	Data struct {
		cmd
		Start *startMsg `json:"start"`
	} `json:"data"`
}

// link is the connection to one online game. A goroutine long-polls the relay; the game loop
// drains what arrived with take.
type link struct {
	id, token string
	seat      int
	mu        sync.Mutex
	in        []netMsg
	err       error
	closed    bool
	out       chan cmd
}

type lobbyGame struct {
	ID      string `json:"id"`
	Name    string `json:"name"`
	Faction int    `json:"faction"`
}

func postJSON(url string, body, out any) error {
	b, _ := json.Marshal(body)
	resp, err := http.Post(url, "application/json", bytes.NewReader(b))
	if err != nil {
		return err
	}
	defer resp.Body.Close()
	if resp.StatusCode != 200 {
		return fmt.Errorf("server: %s", resp.Status)
	}
	return json.NewDecoder(resp.Body).Decode(out)
}

func listGames() ([]lobbyGame, error) {
	resp, err := http.Get(onlineURL)
	if err != nil {
		return nil, err
	}
	defer resp.Body.Close()
	var gs []lobbyGame
	return gs, json.NewDecoder(resp.Body).Decode(&gs)
}

func hostGame(name string, faction int) (*link, error) {
	var r struct{ ID, Token string }
	if err := postJSON(onlineURL, map[string]any{"name": name, "faction": faction}, &r); err != nil {
		return nil, err
	}
	l := &link{id: r.ID, token: r.Token, seat: 0, out: make(chan cmd, 256)}
	go l.poll()
	go l.sender()
	return l, nil
}

func joinGame(id, name string, faction int) (*link, error) {
	var r struct{ Token string }
	if err := postJSON(onlineURL+"/"+id+"/join", map[string]any{"name": name, "faction": faction}, &r); err != nil {
		return nil, err
	}
	l := &link{id: id, token: r.Token, seat: 1, out: make(chan cmd, 256)}
	go l.poll()
	go l.sender()
	return l, nil
}

func (l *link) poll() {
	from, fails := 0, 0
	for {
		l.mu.Lock()
		if l.closed {
			l.mu.Unlock()
			return
		}
		l.mu.Unlock()
		resp, err := http.Get(fmt.Sprintf("%s/%s/recv?from=%d&token=%s", onlineURL, l.id, from, l.token))
		var ms []netMsg
		if err == nil {
			if resp.StatusCode != 200 {
				err = fmt.Errorf("server: %s", resp.Status)
			} else {
				err = json.NewDecoder(resp.Body).Decode(&ms)
			}
			resp.Body.Close()
		}
		l.mu.Lock()
		if err != nil {
			if fails++; fails > 5 {
				l.err = err
				l.mu.Unlock()
				return
			}
			l.mu.Unlock()
			time.Sleep(2 * time.Second)
			continue
		}
		fails = 0
		l.in = append(l.in, ms...)
		from += len(ms)
		l.mu.Unlock()
	}
}

// take returns the messages that arrived since the last call.
func (l *link) take() ([]netMsg, error) {
	l.mu.Lock()
	defer l.mu.Unlock()
	in := l.in
	l.in = nil
	return in, l.err
}

// sender posts queued commands one at a time, so the relay gets them in order.
func (l *link) sender() {
	for c := range l.out {
		var n int
		for try := 0; ; try++ {
			err := postJSON(fmt.Sprintf("%s/%s/send?token=%s", onlineURL, l.id, l.token), c, &n)
			if err == nil {
				break
			}
			if try == 5 {
				l.mu.Lock()
				l.err = err
				l.mu.Unlock()
				return
			}
			time.Sleep(time.Second)
		}
	}
}

func (l *link) close() {
	l.mu.Lock()
	l.closed = true
	l.mu.Unlock()
	close(l.out)
}

// remote plays the opponent's seat by replaying its commands. It is a Controller like the AI, so the
// battle treats the opponent's turn like a computer turn (hidden movement, no input).
type remote struct {
	g     *Battle
	p     *Player
	queue []cmd
}

func (r *remote) StartPhase() {}

// Step applies one queued command; with nothing queued it waits (true) unless the turn ended.
func (r *remote) Step() bool {
	if len(r.queue) == 0 {
		return true
	}
	c := r.queue[0]
	r.queue = r.queue[1:]
	g := r.g
	u := g.m.UnitAt(c.Unit)
	switch {
	case c.End:
		return false
	case u == nil || u.Player != r.p:
		g.desync(fmt.Sprintf("no unit of %s at %v", r.p.Name, c.Unit))
	case c.Move != nil:
		if !g.Move(u, *c.Move) {
			g.desync(fmt.Sprintf("move %v -> %v failed", c.Unit, *c.Move))
		}
	default:
		g.UnitSelected(u.Cell)
		a := g.newAction(c.Skill, u)
		g.action = a
		if a.tm.Type == TargetSelf {
			a.StartTargeting()
			break
		}
		a.StartTargeting()
		for _, t := range c.Targets {
			if g.state != StateAttackTargetSelection || !a.TargetSelected(t) {
				g.desync(fmt.Sprintf("%s at %v failed", c.Skill, t))
				return true
			}
		}
		a.ConfirmTargets()
	}
	return true
}

// idle: nothing to replay right now (keeps the hidden fast-forward from spinning while we wait).
func (r *remote) idle() bool { return len(r.queue) == 0 }

func (g *Battle) desync(why string) {
	g.toast("OUT OF SYNC\n" + why)
}

// pumpNet feeds the opponent's commands into its controller and reports a lost connection.
func (g *Battle) pumpNet() {
	ms, err := g.net.take()
	for _, m := range ms {
		if m.Seat == g.net.seat || m.Data.Start != nil {
			continue
		}
		if m.Data.Quit {
			g.opponentLeft("OPPONENT LEFT")
			continue
		}
		for _, p := range g.players {
			if r, ok := p.AI.(*remote); ok {
				r.queue = append(r.queue, m.Data.cmd)
			}
		}
	}
	if err != nil && !g.over {
		g.opponentLeft("CONNECTION LOST")
	}
}

// opponentLeft ends an online battle early: the remaining player wins.
func (g *Battle) opponentLeft(why string) {
	if g.over {
		return
	}
	g.over, g.won = true, true
	g.m.RemoveAllFog()
	for _, u := range g.units {
		u.IsVisible = true
	}
	g.selected, g.marker = nil, nil
	g.ToDefaultState()
	g.UpdateUIButtons()
	g.setCenter(why + "\nclick to continue")
}

// sendCmd tells the opponent about a local player's action.
func (g *Battle) sendCmd(u *Unit, c cmd) {
	if g.net == nil || u.Player.AI != nil {
		return
	}
	c.Unit = u.Cell
	g.net.out <- c
}

// startOnline begins an online battle once the relay sent the start message.
func startOnline(l *link, s *startMsg) {
	seedGen(s.Seed)
	dice = rand.New(rand.NewSource(s.Seed))
	m := GenerateBSP(21, 21, 2)
	g := NewBattle(m, ModeOnline, s.Factions, func(g *Battle, i int, p *Player) {
		p.Name = s.Names[i]
		if i != l.seat {
			p.AI = &remote{g: g, p: p}
		}
	})
	g.net = l
	for _, u := range g.units { // the guest moves second: show its own squad, not the empty middle
		if u.Player == g.PoV {
			g.cam.X, g.cam.Y = u.Cell.Center()
			break
		}
	}
	scene = g
}
