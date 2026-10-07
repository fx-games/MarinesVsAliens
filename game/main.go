package main

import (
	"flag"
	"log"
	"math"
	"math/rand"
	"runtime"
	"slices"
	"strconv"

	"github.com/hajimehoshi/ebiten/v2"
	"github.com/hajimehoshi/ebiten/v2/inpututil"
	"github.com/hajimehoshi/ebiten/v2/vector"
)

var (
	mod   *Mod
	debug bool    // Options.Debug: map generator overlays, coordinates, FPS
	now   float64 // seconds since start, advanced once per tick
	scene interface {
		Update() error
		Draw(*ebiten.Image)
	}
	screenW, screenH = 1280.0, 720.0
	camBackground    = rgba(0.18867922, 0.18867922, 0.18867922, 1)
)

// GameController.cs
var (
	skirmishFactions = []int{0, 1, 2, 3}
	skirmishPlayers  = 2
	skirmishAI       = []bool{false, true, true, true} // NameButton toggles human / computer
)

type game struct{}

func (game) Update() error {
	now += 1.0 / 60
	updatePointer()
	switch {
	case inpututil.IsKeyJustPressed(ebiten.KeyF11):
		fs := !ebiten.IsFullscreen()
		ebiten.SetFullscreen(fs)
		saveSettings(fs)
	case inpututil.IsKeyJustPressed(ebiten.KeyM):
		musicMuted = !musicMuted
		updateMusicVolume()
		saveSettings(ebiten.IsFullscreen())
	case inpututil.IsKeyJustPressed(ebiten.KeyMinus):
		volume = math.Max(volume-0.1, 0)
		updateMusicVolume()
		saveSettings(ebiten.IsFullscreen())
	case inpututil.IsKeyJustPressed(ebiten.KeyEqual):
		volume = math.Min(volume+0.1, 1)
		updateMusicVolume()
		saveSettings(ebiten.IsFullscreen())
	case inpututil.IsKeyJustPressed(ebiten.KeyF1):
		showHelp = !showHelp
	}
	return scene.Update()
}

func (game) Draw(s *ebiten.Image) {
	scene.Draw(s)
	if showHelp {
		drawHelp(s)
	}
}

var showHelp bool

const helpText = `CONTROLS
Left click - select a unit, pick a move target, click it again to move.
Right click or Esc - step back: cancel targeting, undo a move preview, deselect.
Esc with nothing selected - leave the battle.
Buttons - end turn, ranged or melee attack, open door, unit skills.
Multi-target weapons - click the targets, then press the attack button again.
WASD, drag, mouse wheel - move and zoom the camera.
F1 help, F11 fullscreen, M music on/off, - and = volume.

HOW TO PLAY
Tab cycles through your units that still have AP.
Each unit has action points (yellow pips). Moving and attacking cost AP.
Green tiles leave enough AP to attack afterwards, yellow ones don't.
Hover an enemy while targeting to see the hit chance and damage.
Damage is reduced by armour. Eliminate every other faction to win.`

func drawHelp(s *ebiten.Image) {
	w, h := screenW*0.8, screenH*0.7
	x, y := (screenW-w)/2, (screenH-h)/2
	vector.FillRect(s, float32(x), float32(y), float32(w), float32(h), rgba(0, 0, 0, 0.85), false)
	k := math.Min(screenW/1280, screenH/720)
	drawString(s, helpText, "Arial", 18, x+30*k, y+30*k, w-60*k, h-60*k, 0, [4]float64{1, 1, 1, 1}, k, 1, 1, 1.1)
}
func (game) Layout(w, h int) (int, int) {
	screenW, screenH = float64(w), float64(h)
	return w, h
}

func main() {
	flag.BoolVar(&debug, "debug", false, "show map generator and pathfinding debug output")
	flag.Parse()
	var err error
	if mod, err = LoadMod("percent.rul"); err != nil {
		log.Fatal(err)
	}
	loadSettings()
	loadSprites()
	loadUI()
	scene = newMainMenu()
	ebiten.SetWindowSize(1280, 720)
	ebiten.SetWindowTitle("Marines vs Aliens")
	ebiten.SetWindowResizingMode(ebiten.WindowResizingModeEnabled)
	if err := ebiten.RunGame(game{}); err != nil && err != ebiten.Termination {
		log.Fatal(err)
	}
}

func factionSprite(faction int) *uiSprite {
	return &uiSprite{File: "sprites/creatures.png", Y: factionRowY[faction], W: 24, H: 24}
}

// ---- MainMenu.cs ----

type mainMenu struct {
	roots []*Node
	quit  bool
}

func newMainMenu() *mainMenu {
	m := &mainMenu{roots: loadScene("MainMenuScene")}
	playMusic("music/menu.mp3", true)
	m.slotFaction(1).Image.Sprite = factionSprite(skirmishFactions[1])
	// the scene never wired these buttons up; CampaignSelection is what they were meant to call
	for i, name := range []string{"Marines", "Birds", "Legion", "Travellers"} {
		i := i
		findIn(m.roots, name).OnClick = func() { campaignSelection(i) }
	}
	for i := 0; i < 4; i++ {
		i := i
		b := m.slot(i).Find("NameButton")
		human := b.Find("Text").Text.Text
		show := func() {
			b.Find("Text").Text.Text = human
			if skirmishAI[i] {
				b.Find("Text").Text.Text = "Computer"
			}
		}
		b.OnClick = func() { skirmishAI[i] = !skirmishAI[i]; show() }
		show()
	}
	return m
}

func campaignSelection(faction int) { startMission(mod.Factions[faction], 0) }

// startMission loads campaign mission idx; the other factions are played by the AI.
func startMission(f *RuleFaction, idx int) {
	g := NewBattle(MapFromRules(mod.Maps[idx], len(mod.Factions)), ModeCampaign, []int{slices.Index(mod.Factions, f)})
	g.mission = idx
	g.setCenter(mod.Tr(mod.Maps[idx].Type) + "\n" + g.CurrentPlayer().String())
	g.schedule(3, func() { g.setCenter("") })
	scene = g
}

func (m *mainMenu) slot(i int) *Node {
	name := "PlayerSlot"
	if i > 0 {
		name += " (" + strconv.Itoa(i) + ")"
	}
	return findIn(m.roots, name)
}

func (m *mainMenu) slotFaction(i int) *Node { return m.slot(i).Find("FactionSprite") }

func (m *mainMenu) show(menu string) {
	for _, n := range []string{"MainMenu", "ChooseFactionMenu", "SkirmishMenu"} {
		findIn(m.roots, n).Active = n == menu
	}
}

func (m *mainMenu) call(c uiCall, _ *Node) {
	switch c.Method {
	case "Button1":
		m.show("ChooseFactionMenu")
	case "Button2":
		m.show("SkirmishMenu")
	case "Button3":
		m.quit = runtime.GOOS != "js" // quitting would leave a dead canvas in the browser
	case "BackToMainMenu":
		m.show("MainMenu")
	case "ToggleSkirmishFaction":
		skirmishFactions[c.Int] = (skirmishFactions[c.Int] + 1) % 4
		m.slotFaction(c.Int).Image.Sprite = factionSprite(skirmishFactions[c.Int])
	case "AddSkirmishPlayerSlot":
		for i := 0; i < 4; i++ {
			if !m.slot(i).Active {
				m.slotFaction(i).Image.Sprite = factionSprite(skirmishFactions[i])
				m.slot(i).Active = true
				return
			}
		}
	case "RemoveSkirmishPlayerSlot":
		for i := 3; i >= 2; i-- {
			if m.slot(i).Active {
				m.slot(i).Active = false
				return
			}
		}
	case "LoadMapGenerator":
		skirmishPlayers = 0
		for i := 0; i < 4; i++ {
			if m.slot(i).Active {
				skirmishPlayers++
			}
		}
		scene = newMapGenerator()
	}
}

func (m *mainMenu) Update() error {
	for _, r := range m.roots {
		layoutCanvas(r, screenW, screenH)
	}
	updateUI(m.roots, m.call)
	if m.quit {
		return ebiten.Termination
	}
	return nil
}

func (m *mainMenu) Draw(s *ebiten.Image) {
	s.Fill(camBackground)
	drawUI(s, m.roots)
}

// ---- MapGeneratorMenu.cs ----

type mapGenerator struct {
	roots []*Node
	m     *Map
	cam   Camera
	algo  int
	seed  int
	drag  camDrag
}

func newMapGenerator() *mapGenerator {
	g := &mapGenerator{roots: loadScene("MapGenerator"), cam: Camera{X: 8.767941, Y: 10.261607, Size: 11.2}}
	dd := findIn(g.roots, "Dropdown").Dropdown
	dd.Options = append(dd.Options, "Dungeon")
	g.newSeed()
	g.generate()
	return g
}

func (g *mapGenerator) newSeed() {
	g.seed = int(int32(rand.Uint32()))
	findIn(g.roots, "SeedInputField").Find("Text").SetText(strconv.Itoa(g.seed))
	rand.Seed(int64(g.seed))
}

func (g *mapGenerator) generate() {
	switch g.algo {
	case 0:
		g.m = GenerateBSP(21, 21, skirmishPlayers)
	case 1:
		g.m = withSpawns(skirmishPlayers, func() *Map { return GenerateCavern(21, 21) })
	case 2:
		g.m = withSpawns(skirmishPlayers, func() *Map { return GenerateDungeon(21, 21) })
	}
	g.m.refreshWalls()
}

func (g *mapGenerator) call(c uiCall, n *Node) {
	switch c.Method {
	case "RunToCompletionButtonPressed":
		if c.Bool {
			g.newSeed()
		}
		g.generate()
	case "SeedChanged":
		if v, err := strconv.ParseInt(n.Find("Text").Text.Text, 10, 32); err == nil {
			g.seed = int(v)
			rand.Seed(v)
			g.generate()
		}
	case "AlgorithmChanged":
		g.algo = c.Int
		g.generate()
	case "AcceptMap":
		if len(g.m.SpawnPoints()) < skirmishPlayers {
			return // the original crashed here when the cave generator placed no spawn points
		}
		for l := range g.m.hl {
			g.m.ClearHighlights(l)
		}
		g.m.ClearLines()
		g.m.labels = nil
		g.m.ClearMarkers()
		scene = NewBattle(g.m, ModeSkirmish, skirmishFactions[:skirmishPlayers])
	}
}

func (g *mapGenerator) Update() error {
	for _, r := range g.roots {
		layoutCanvas(r, screenW, screenH)
	}
	input := findIn(g.roots, "SeedInputField")
	input.Find("Placeholder").Text.Enabled = input.Find("Text").Text.Text == ""
	updateUI(g.roots, g.call)
	x, y := cursorPos()
	g.drag.update(&g.cam, float64(x), float64(y))
	return nil
}

func (g *mapGenerator) Draw(s *ebiten.Image) {
	s.Fill(rgba(0.19215687, 0.3019608, 0.4745098, 1))
	g.cam.W, g.cam.H = screenW, screenH
	drawWorld(s, &g.cam, g.m, nil)
	drawUI(s, g.roots)
}

// ---- CameraControls.cs ----

type camDrag struct {
	start   [2]float64
	screen  [2]float64 // cursor position at the press
	Active  bool
	pressed bool
}

func (d *camDrag) update(c *Camera, mx, my float64) {
	wx, wy := c.toWorld(mx, my)
	dx, dy := 0.0, 0.0
	if ebiten.IsKeyPressed(ebiten.KeyA) {
		dx = -1
	} else if ebiten.IsKeyPressed(ebiten.KeyD) {
		dx = 1
	}
	if ebiten.IsKeyPressed(ebiten.KeyW) {
		dy = 1
	} else if ebiten.IsKeyPressed(ebiten.KeyS) {
		dy = -1
	}
	if n := math.Hypot(dx, dy); n > 0 { // Lerp(pos, pos+dir, dt*8)
		c.X += dx / n * 8.0 / 60
		c.Y += dy / n * 8.0 / 60
	}
	wheel := wheelY()
	c.Size = math.Min(math.Max(c.Size-wheel*0.1, 1), 20)

	if pointerJustPressed() {
		d.start, d.screen, d.pressed = [2]float64{wx, wy}, [2]float64{mx, my}, true
	}
	// a drag starts once the cursor moves a few pixels; a long but still click stays a click
	if d.pressed && pointerDown() && (d.Active || math.Hypot(mx-d.screen[0], my-d.screen[1]) > 6) {
		c.X += d.start[0] - wx
		c.Y += d.start[1] - wy
		d.Active = true
	}
	if !pointerDown() {
		d.Active, d.pressed = false, false
	}
}
