package main

import (
	"bytes"
	"embed"
	"image"
	"image/color"
	_ "image/png"
	"io"
	"log"
	"math"
	"strings"

	"github.com/hajimehoshi/ebiten/v2"
	"github.com/hajimehoshi/ebiten/v2/audio"
	"github.com/hajimehoshi/ebiten/v2/audio/mp3"
	"github.com/hajimehoshi/ebiten/v2/audio/wav"
	"github.com/hajimehoshi/ebiten/v2/vector"
)

//go:embed assets/sprites assets/fonts assets/sounds assets/music assets/percent.rul
var assetFS embed.FS

func readAsset(path string) ([]byte, error) { return assetFS.ReadFile("assets/" + path) }

func loadImage(path string) *ebiten.Image {
	f, err := assetFS.Open("assets/" + path)
	if err != nil {
		log.Fatal(err)
	}
	defer f.Close()
	img, _, err := image.Decode(f)
	if err != nil {
		log.Fatal(path, ": ", err)
	}
	return ebiten.NewImageFromImage(img)
}

func sub(img *ebiten.Image, x, y, w, h int) *ebiten.Image {
	return img.SubImage(image.Rect(x, y, x+w, y+h)).(*ebiten.Image)
}

// Sprite rects below were taken from the Unity .meta slicing (converted to top-left origin).

// WallSet is a WallTile asset: 16 neighbour-mask sprites plus damage variants.
type WallSet struct {
	Sprites, Damaged, Destroyed, NorthAlt []*ebiten.Image
	NorthChance                           int
}

type Sprites struct {
	Floors                     []*ebiten.Image // station, rock, decorated station
	WallSets                   []WallSet       // station, rock
	Doors, SpawnFloors         []*ebiten.Image
	FactionRows                [][]*ebiten.Image // idle frames per faction prefab
	Explosion, MeleeHit, Blood []*ebiten.Image
	Flash, Bullets             []*ebiten.Image
	SelectRect, Blank          *ebiten.Image
}

var spr Sprites

// factionRowY is the creature sheet row of each faction prefab: Marine, Bird, Legion, Traveller, Drone.
var factionRowY = []int{0, 528, 312, 216, 936}

func loadSprites() {
	w := loadImage("sprites/world.png")
	t := func(x, y int) *ebiten.Image { return sub(w, x, y, 24, 24) }
	row := func(x0, y, n int) []*ebiten.Image {
		var r []*ebiten.Image
		for i := 0; i < n; i++ {
			r = append(r, t(x0+24*i, y))
		}
		return r
	}
	spr.Floors = []*ebiten.Image{t(0, 0), t(0, 840), t(24, 0)}
	destroyed := row(720, 72, 3)
	spr.WallSets = []WallSet{
		{Sprites: row(144, 0, 16), Damaged: row(432, 72, 8), Destroyed: destroyed, NorthChance: 5,
			NorthAlt: []*ebiten.Image{t(336, 24), t(360, 24), t(672, 24), t(336, 72), t(360, 72), t(408, 72)}},
		{Sprites: append(append(row(0, 912, 12), t(48, 888)), row(312, 912, 3)...), Destroyed: destroyed},
	}
	spr.Doors = []*ebiten.Image{t(336, 48), t(360, 48)}
	spr.SpawnFloors = []*ebiten.Image{t(72, 24), t(48, 24), t(24, 24), t(0, 24)}

	c := loadImage("sprites/creatures.png")
	for _, y := range factionRowY {
		var r []*ebiten.Image
		for i := 0; i < 32; i++ {
			r = append(r, sub(c, 24*i, y, 24, 24))
		}
		spr.FactionRows = append(spr.FactionRows, r)
	}

	e := loadImage("sprites/effects.png")
	fx := func(i int) *ebiten.Image {
		if i < 9 {
			return sub(e, 32*i, 0, 32, 32)
		}
		return sub(e, 32*(i-9), 32, 32, 32)
	}
	for i := 0; i <= 5; i++ {
		spr.Explosion = append(spr.Explosion, fx(i))
	}
	spr.MeleeHit = []*ebiten.Image{fx(6), fx(7), fx(8)}
	spr.Blood = []*ebiten.Image{fx(11), fx(12), fx(13), fx(14)}
	spr.SelectRect, spr.Blank = fx(9), fx(10)
	for i := 0; i < 7; i++ {
		spr.Flash = append(spr.Flash, sub(e, 40*i, 476, 36, 36))
	}

	b := loadImage("sprites/bullets.png")
	spr.Bullets = []*ebiten.Image{sub(b, 21, 14, 10, 6), sub(b, 21, 32, 10, 6), sub(b, 21, 50, 10, 6),
		sub(b, 21, 68, 10, 6), sub(b, 280, 215, 13, 10), sub(b, 211, 30, 11, 8)}
}

// ---- audio ----

const (
	sndSelect  = "sounds/select.wav"
	sndDeny    = "sounds/deny.mp3"
	sndConfirm = "sounds/confirm.wav"
	sndButton  = "sounds/button.wav"
	sndIceball = "sounds/iceball.wav"
	sndRocket  = "sounds/rlaunch.wav"
	sndHit     = "sounds/hit.wav"
)

var sndSwords = []string{"sounds/sword1.mp3", "sounds/sword2.mp3", "sounds/sword3.mp3"}

const sampleRate = 44100

var (
	audioCtx   = audio.NewContext(sampleRate)
	soundCache = map[string][]byte{}
	volume     = 1.0 // master volume, -/= keys
	music      *audio.Player
	musicPath  string
	musicMuted = true // M key; off by default
)

// playMusic switches the background track; loop repeats it forever.
func playMusic(path string, loop bool) {
	if path == musicPath && music != nil && music.IsPlaying() {
		return
	}
	if music != nil {
		music.Close()
		music = nil
	}
	musicPath = path
	pcm, ok := soundCache[path]
	if !ok {
		pcm = decodeSound(path)
		soundCache[path] = pcm
	}
	if pcm == nil {
		return
	}
	var err error
	if loop {
		music, err = audioCtx.NewPlayer(audio.NewInfiniteLoop(bytes.NewReader(pcm), int64(len(pcm))))
	} else {
		music = audioCtx.NewPlayerFromBytes(pcm)
	}
	if err != nil {
		log.Println(err)
		return
	}
	updateMusicVolume()
	music.Play()
}

func updateMusicVolume() {
	if music != nil {
		v := volume * 0.4
		if musicMuted {
			v = 0
		}
		music.SetVolume(v)
	}
}

var silent bool // set while fast-forwarding hidden moves

func playSound(path string, vol float64) {
	if silent {
		return
	}
	pcm, ok := soundCache[path]
	if !ok {
		pcm = decodeSound(path)
		soundCache[path] = pcm
	}
	if pcm == nil {
		return
	}
	p := audioCtx.NewPlayerFromBytes(pcm)
	p.SetVolume(volume * vol)
	p.Play()
}

func decodeSound(path string) []byte {
	b, err := readAsset(path)
	if err != nil {
		log.Println(err)
		return nil
	}
	var s io.Reader
	if strings.HasSuffix(path, ".mp3") {
		s, err = mp3.DecodeWithSampleRate(sampleRate, bytes.NewReader(b))
	} else {
		s, err = wav.DecodeWithSampleRate(sampleRate, bytes.NewReader(b))
	}
	if err == nil {
		b, err = io.ReadAll(s)
	}
	if err != nil {
		log.Println(path, err)
		return nil
	}
	return b
}

// ---- drawing helpers ----

type Camera struct {
	X, Y, Size   float64 // Size = Unity orthographic size (half view height in cells)
	W, H         float64
	lerpFrom     [2]float64
	lerpTo       [2]float64
	lerpT, lerpD float64
}

func (c *Camera) scale() float64 { return c.H / (2 * c.Size) }

func (c *Camera) toScreen(wx, wy float64) (float64, float64) {
	s := c.scale()
	return (wx-c.X)*s + c.W/2, c.H/2 - (wy-c.Y)*s
}

func (c *Camera) toWorld(sx, sy float64) (float64, float64) {
	s := c.scale()
	return (sx-c.W/2)/s + c.X, (c.H/2-sy)/s + c.Y
}

// drawWorld draws img centered at world pos, sized so that ppu image pixels = 1 cell.
func (c *Camera) drawWorld(dst, img *ebiten.Image, wx, wy, ppu float64, flipX bool, rot float64, clr color.Color) {
	b := img.Bounds()
	op := &ebiten.DrawImageOptions{}
	op.GeoM.Translate(-float64(b.Dx())/2, -float64(b.Dy())/2)
	if flipX {
		op.GeoM.Scale(-1, 1)
	}
	op.GeoM.Rotate(rot)
	s := c.scale() / ppu
	op.GeoM.Scale(s, s)
	op.GeoM.Translate(c.toScreen(wx, wy))
	if clr != nil {
		op.ColorScale.ScaleWithColor(clr)
	}
	dst.DrawImage(img, op)
}

func (c *Camera) fillCell(dst *ebiten.Image, p P, clr color.Color) {
	x, y := c.toScreen(float64(p.X), float64(p.Y+1))
	s := c.scale()
	vector.FillRect(dst, float32(x), float32(y), float32(s), float32(s), clr, false)
}

func (c *Camera) line(dst *ebiten.Image, x0, y0, x1, y1, width float64, clr color.Color) {
	ax, ay := c.toScreen(x0, y0)
	bx, by := c.toScreen(x1, y1)
	vector.StrokeLine(dst, float32(ax), float32(ay), float32(bx), float32(by), float32(width*c.scale()), clr, true)
}

func (c *Camera) smoothTo(x, y float64) {
	c.lerpFrom = [2]float64{c.X, c.Y}
	c.lerpTo = [2]float64{x, y}
	c.lerpT, c.lerpD = 0, 0.3
}

func (c *Camera) update(dt float64) {
	if c.lerpD == 0 {
		return
	}
	c.lerpT += dt
	t := math.Min(c.lerpT/c.lerpD, 1)
	c.X = c.lerpFrom[0] + (c.lerpTo[0]-c.lerpFrom[0])*t
	c.Y = c.lerpFrom[1] + (c.lerpTo[1]-c.lerpFrom[1])*t
	if t >= 1 {
		c.lerpD = 0
	}
}

func rgba(r, g, b, a float64) color.Color {
	return color.NRGBA{uint8(r * 255), uint8(g * 255), uint8(b * 255), uint8(a * 255)}
}
