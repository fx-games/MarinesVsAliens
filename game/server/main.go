// mva-battles: lobby and message relay for online Marines vs Aliens. Both clients run the same
// deterministic battle; the server only orders their commands and picks the shared seed.
//
//	GET  api/games                    open games
//	POST api/games       {name,faction}    -> {id,token}   (host, seat 0)
//	POST api/games/ID/join {name,faction}  -> {token}      (guest, seat 1); appends the start message
//	POST api/games/ID/send?token=T  json   appends {seat, data}
//	GET  api/games/ID/recv?from=N           messages from index N, long-polls up to 25 s
//
// Everything else under -base redirects to the web game (-play).
package main

import (
	"crypto/rand"
	"encoding/hex"
	"encoding/json"
	"flag"
	"io"
	"log"
	mrand "math/rand"
	"net/http"
	"strconv"
	"strings"
	"sync"
	"time"
)

type msg struct {
	Seat int             `json:"seat"`
	Data json.RawMessage `json:"data"`
}

type game struct {
	ID      string `json:"id"`
	Name    string `json:"name"`
	Faction int    `json:"faction"`
	tokens  [2]string
	msgs    []msg
	wake    chan struct{} // closed and replaced on every new message
	touched time.Time
}

var (
	mu    sync.Mutex
	games = map[string]*game{}
)

func token() string {
	b := make([]byte, 12)
	rand.Read(b)
	return hex.EncodeToString(b)
}

func (g *game) push(seat int, data json.RawMessage) {
	g.msgs = append(g.msgs, msg{seat, data})
	g.touched = time.Now()
	close(g.wake)
	g.wake = make(chan struct{})
}

type seatReq struct {
	Name    string `json:"name"`
	Faction int    `json:"faction"`
}

func readJSON(r *http.Request, v any) error {
	return json.NewDecoder(io.LimitReader(r.Body, 64<<10)).Decode(v)
}

func writeJSON(w http.ResponseWriter, v any) {
	w.Header().Set("Content-Type", "application/json")
	w.Header().Set("Cache-Control", "no-store")
	json.NewEncoder(w).Encode(v)
}

func api(w http.ResponseWriter, r *http.Request, path string) {
	parts := strings.Split(strings.Trim(path, "/"), "/")
	mu.Lock()
	defer mu.Unlock()
	switch {
	case len(parts) == 1 && r.Method == "GET":
		open := []*game{}
		for _, g := range games {
			if g.tokens[1] == "" {
				open = append(open, g)
			}
		}
		writeJSON(w, open)
	case len(parts) == 1 && r.Method == "POST":
		var q seatReq
		if readJSON(r, &q) != nil || len(games) > 500 {
			http.Error(w, "bad request", 400)
			return
		}
		g := &game{ID: token()[:8], Name: trim(q.Name), Faction: q.Faction & 3, wake: make(chan struct{}), touched: time.Now()}
		g.tokens[0] = token()
		games[g.ID] = g
		writeJSON(w, map[string]string{"id": g.ID, "token": g.tokens[0]})
	case len(parts) == 3:
		g := games[parts[1]]
		if g == nil {
			http.Error(w, "no such game", 404)
			return
		}
		switch parts[2] {
		case "join":
			var q seatReq
			if r.Method != "POST" || readJSON(r, &q) != nil || g.tokens[1] != "" {
				http.Error(w, "game is full", 409)
				return
			}
			g.tokens[1] = token()
			start, _ := json.Marshal(map[string]any{"start": map[string]any{
				"seed": mrand.Int31(), "factions": []int{g.Faction, q.Faction & 3}, "names": []string{g.Name, trim(q.Name)}}})
			g.push(-1, start)
			writeJSON(w, map[string]string{"token": g.tokens[1]})
		case "send":
			seat := -1
			for i, t := range g.tokens {
				if t != "" && t == r.URL.Query().Get("token") {
					seat = i
				}
			}
			var data json.RawMessage
			if r.Method != "POST" || seat < 0 || readJSON(r, &data) != nil || len(g.msgs) > 20000 {
				http.Error(w, "forbidden", 403)
				return
			}
			g.push(seat, data)
			writeJSON(w, len(g.msgs))
		case "recv":
			from, _ := strconv.Atoi(r.URL.Query().Get("from"))
			deadline := time.After(25 * time.Second)
			for from >= len(g.msgs) {
				wake := g.wake
				mu.Unlock()
				timeout := false
				select {
				case <-wake:
				case <-deadline:
					timeout = true
				case <-r.Context().Done():
				}
				mu.Lock()
				if r.Context().Err() != nil {
					return
				}
				if timeout {
					writeJSON(w, []msg{})
					return
				}
			}
			writeJSON(w, g.msgs[max(from, 0):])
		default:
			http.NotFound(w, r)
		}
	default:
		http.NotFound(w, r)
	}
}

func trim(s string) string {
	s = strings.TrimSpace(s)
	if len(s) > 20 {
		s = s[:20]
	}
	if s == "" {
		s = "Player"
	}
	return s
}

func main() {
	addr := flag.String("addr", "127.0.0.1:8378", "listen address")
	base := flag.String("base", "/mva-battles", "URL prefix")
	play := flag.String("play", "/games/marines-vs-aliens/play/", "where the web game is")
	flag.Parse()
	go func() { // drop games idle for 2 h
		for range time.Tick(time.Minute) {
			mu.Lock()
			for id, g := range games {
				if time.Since(g.touched) > 2*time.Hour {
					delete(games, id)
				}
			}
			mu.Unlock()
		}
	}()
	http.HandleFunc(*base+"/", func(w http.ResponseWriter, r *http.Request) {
		if p, ok := strings.CutPrefix(r.URL.Path, *base+"/api/"); ok {
			api(w, r, p)
			return
		}
		http.Redirect(w, r, *play, http.StatusFound) // the game itself lives with the other web games
	})
	log.Println("listening on", *addr)
	log.Fatal(http.ListenAndServe(*addr, nil))
}
