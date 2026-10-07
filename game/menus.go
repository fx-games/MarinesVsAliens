package main

import (
	"fmt"
	"math"
	"sync"

	"github.com/hajimehoshi/ebiten/v2"
)

// The Online and Settings menus are built from clones of the main menu's own buttons, so they look
// like the rest of the Unity UI.

func (m *mainMenu) cloneButton(label string, y float64, onClick func()) *Node {
	n := findIn(loadScene("MainMenuScene"), "PlayButton1") // a fresh copy
	n.Name, n.Pos[1], n.Size[1] = label, y, 22
	n.Button.OnClick, n.OnClick = nil, onClick
	n.Find("Text").SetText(label)
	return n
}

// addMenu adds an inactive full-screen container next to MainMenu.
func (m *mainMenu) addMenu(name string) *Node {
	c := &Node{Name: name, AMax: [2]float64{1, 1}, Scale: [2]float64{1, 1}}
	m.roots[0].Children = append(m.roots[0].Children, c) // the Canvas
	return c
}

func (m *mainMenu) menus() []string {
	return []string{"MainMenu", "ChooseFactionMenu", "SkirmishMenu", "OnlineMenu", "SettingsMenu"}
}

// extendMainMenu squeezes Online and Settings in between the original three buttons.
func (m *mainMenu) extendMainMenu() {
	main := findIn(m.roots, "MainMenu")
	ys := []float64{44, 18, -8, -34, -60}
	for i, name := range []string{"PlayButton1", "PlayButton2", "PlayButton3"} {
		b := findIn(m.roots, name)
		b.Size[1] = 22
		b.Pos[1] = ys[[]int{0, 1, 4}[i]]
	}
	main.Children = append(main.Children,
		m.cloneButton("ONLINE", ys[2], func() { m.openOnline() }),
		m.cloneButton("SETTINGS", ys[3], func() { m.show("SettingsMenu"); m.refreshSettings() }))
	m.buildSettings()
	m.buildOnline()
}

// ---- settings ----

func (m *mainMenu) buildSettings() {
	s := m.addMenu("SettingsMenu")
	var music, vol, full *Node
	refresh := func() {
		music.Find("Text").SetText("MUSIC: " + map[bool]string{true: "OFF", false: "ON"}[musicMuted])
		vol.Find("Text").SetText(fmt.Sprintf("VOLUME: %d%%", int(math.Round(volume*100))))
		full.Find("Text").SetText("FULLSCREEN: " + map[bool]string{true: "ON", false: "OFF"}[ebiten.IsFullscreen()])
	}
	music = m.cloneButton("", 44, func() {
		musicMuted = !musicMuted
		updateMusicVolume()
		saveSettings(ebiten.IsFullscreen())
		refresh()
	})
	vol = m.cloneButton("", 18, func() {
		volume = math.Mod(math.Round(volume*5)+1, 6) / 5 // 0, 20 ... 100 %
		updateMusicVolume()
		playSound(sndButton, 1)
		saveSettings(ebiten.IsFullscreen())
		refresh()
	})
	full = m.cloneButton("", -8, func() {
		fs := !ebiten.IsFullscreen()
		ebiten.SetFullscreen(fs)
		saveSettings(fs)
		full.Find("Text").SetText("FULLSCREEN: " + map[bool]string{true: "ON", false: "OFF"}[fs])
	})
	back := m.cloneButton("BACK", -60, func() { m.show("MainMenu") })
	s.Children = []*Node{music, vol, full, back}
	m.refreshSettings = refresh
	refresh()
}

// ---- online lobby ----

type lobby struct {
	mu     sync.Mutex
	status string
	games  []lobbyGame
	link   *link
	busy   bool
	shown  string // state the buttons were built for
}

func (m *mainMenu) buildOnline() {
	o := m.addMenu("OnlineMenu")
	m.lobby = &lobby{}
	m.onlineMenu = o
}

func (m *mainMenu) openOnline() {
	m.show("OnlineMenu")
	m.refreshGames()
}

// lobbyName is how this player shows up in the lobby: the faction picked in the skirmish menu.
func lobbyName() string { return mod.Tr(mod.Factions[skirmishFactions[0]].Type) }

func (m *mainMenu) refreshGames() {
	l := m.lobby
	l.mu.Lock()
	if l.busy {
		l.mu.Unlock()
		return
	}
	l.busy, l.status = true, "Looking for games ..."
	l.mu.Unlock()
	go func() { // network calls must not block the game loop (and can't in the browser)
		gs, err := listGames()
		l.mu.Lock()
		defer l.mu.Unlock()
		l.busy, l.games, l.status = false, gs, ""
		if err != nil {
			l.status = "Server not reachable"
		} else if len(gs) == 0 {
			l.status = "No open games. Host one!"
		}
	}()
}

// connect hosts or joins in the background; the battle starts when the relay sends the start message.
func (m *mainMenu) connect(join string) {
	l := m.lobby
	l.mu.Lock()
	if l.busy || l.link != nil {
		l.mu.Unlock()
		return
	}
	l.busy, l.status = true, "Connecting ..."
	l.mu.Unlock()
	go func() {
		var ln *link
		var err error
		if join == "" {
			ln, err = hostGame(lobbyName(), skirmishFactions[0])
		} else {
			ln, err = joinGame(join, lobbyName(), skirmishFactions[0])
		}
		l.mu.Lock()
		defer l.mu.Unlock()
		l.busy, l.link = false, ln
		switch {
		case err != nil:
			l.status = "Could not connect: " + err.Error()
		case join == "":
			l.status = "Waiting for an opponent ..."
		default:
			l.status = "Starting ..."
		}
	}()
}

func (m *mainMenu) leaveLobby() {
	l := m.lobby
	l.mu.Lock()
	if l.link != nil {
		l.link.close()
		l.link = nil
	}
	l.mu.Unlock()
	m.show("MainMenu")
}

// updateOnline rebuilds the lobby buttons from the current state and starts the battle once matched.
func (m *mainMenu) updateOnline() {
	if !m.onlineMenu.Active {
		return
	}
	l := m.lobby
	l.mu.Lock()
	defer l.mu.Unlock()
	if l.link != nil {
		ms, err := l.link.take()
		for _, msg := range ms {
			if msg.Data.Start != nil {
				startOnline(l.link, msg.Data.Start)
				l.link = nil
				return
			}
		}
		if err != nil {
			l.status, l.link = "Connection lost", nil
		}
	}
	faction := mod.Tr(mod.Factions[skirmishFactions[0]].Type)
	state := fmt.Sprint(faction, l.status, l.link != nil, l.games)
	if state == l.shown {
		return // rebuilding every frame would lose the pressed button between press and release
	}
	l.shown = state
	kids := []*Node{m.cloneButton("PLAY AS: "+faction, 44, func() {
		if l.link == nil {
			skirmishFactions[0] = (skirmishFactions[0] + 1) % len(mod.Factions)
		}
	})}
	if l.link == nil {
		kids = append(kids, m.cloneButton("HOST GAME", 20, func() { m.connect("") }))
		for i, g := range l.games {
			if i == 2 {
				break
			}
			g := g
			kids = append(kids, m.cloneButton("JOIN "+g.Name, -4-float64(i)*24, func() { m.connect(g.ID) }))
		}
		kids = append(kids, m.cloneButton("REFRESH", -76, func() { m.refreshGames() }))
	}
	status := m.cloneButton(l.status, -52, nil)
	status.Image, status.Button = nil, nil // a plain label
	kids = append(kids, status, m.cloneButton("BACK", -100, func() { m.leaveLobby() }))
	m.onlineMenu.Children = kids
}
