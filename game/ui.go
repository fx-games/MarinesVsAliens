package main

import (
	"bytes"
	_ "embed"
	"encoding/json"
	"github.com/hajimehoshi/ebiten/v2/colorm"
	"image"
	"image/color"
	"math"
	"strings"

	"github.com/hajimehoshi/ebiten/v2"
	"github.com/hajimehoshi/ebiten/v2/inpututil"
	"github.com/hajimehoshi/ebiten/v2/text/v2"
	"github.com/hajimehoshi/ebiten/v2/vector"
)

// A small runtime for the Unity uGUI canvases, exported from the scenes into ui.json.

//go:embed ui.json
var uiJSON []byte

type uiSprite struct {
	File    string
	X, Y, W int
	H       int
	Border  [4]float64 // left, bottom, right, top in pixels
	PPU     float64
	Builtin int
}

type uiCall struct {
	Method string
	Int    int
	Bool   bool
}

type Node struct {
	Name         string
	Active       bool
	AMin, AMax   [2]float64
	Pos, Size    [2]float64
	Pivot, Scale [2]float64
	Image        *struct {
		Sprite         *uiSprite
		Color          [4]float64
		Type           int
		PreserveAspect bool
		Enabled        bool
	}
	Text *struct {
		Text                 string
		Color                [4]float64
		Font                 string
		Size                 float64
		Align                int
		HOverflow, VOverflow int
		LineSpacing          float64
		Enabled              bool
	}
	Button *struct {
		Normal, Highlighted, Pressed [4]float64
		Multiplier                   float64
		Transition                   int
		OnClick                      []uiCall
		PressedSprite                *uiSprite // sprite swap (transition 2)
	}
	Input    *struct{ OnEndEdit []uiCall }
	Dropdown *struct {
		Options        []string
		OnValueChanged []uiCall
	}
	Scaler *struct {
		Mode        int
		Ref         [2]float64
		Match       float64
		ScaleFactor float64
	}
	Children []*Node

	// runtime
	OnClick  func()
	value    int     // dropdown selection
	downTill float64 // keeps the pressed look briefly after a click
	Disabled bool    // grayed out, ignores clicks
	focused  bool    // input field
	hot      xform
	rect     [4]float64 // local rect x, y, w, h
	parent   *Node
}

type uiFont struct {
	Texture     string
	LineSpacing float64
	Chars       map[int][9]float64 // uv x,y,w,h (top-left px), vert x,y,w,h, advance
	img         *ebiten.Image
}

var ui struct {
	Scenes  map[string]json.RawMessage
	Prefabs map[string]json.RawMessage
	Fonts   map[string]*uiFont
}

var arial *text.GoTextFaceSource // Silkscreen, used wherever the scenes ask for Arial

func loadUI() {
	if err := json.Unmarshal(uiJSON, &ui); err != nil {
		panic(err)
	}
	// stands in for Unity's built-in Arial: Silkscreen (OFL), embedded so every platform looks the same
	ttf, err := readAsset("fonts/Silkscreen-Regular.ttf")
	if err != nil {
		panic(err)
	}
	arial, _ = text.NewGoTextFaceSource(bytes.NewReader(ttf))
}

// loadScene returns a fresh copy of a scene's UI roots.
func loadScene(name string) []*Node {
	var n []*Node
	if err := json.Unmarshal(ui.Scenes[name], &n); err != nil {
		panic(err)
	}
	return n
}

func clonePrefab(name string) *Node {
	var n *Node
	if err := json.Unmarshal(ui.Prefabs[name], &n); err != nil {
		panic(err)
	}
	return n
}

func (n *Node) Find(name string) *Node {
	for _, c := range n.Children {
		if c.Name == name {
			return c
		}
		if f := c.Find(name); f != nil {
			return f
		}
	}
	return nil
}

func findIn(roots []*Node, name string) *Node {
	for _, r := range roots {
		if r.Name == name {
			return r
		}
		if f := r.Find(name); f != nil {
			return f
		}
	}
	return nil
}

func (n *Node) SetText(s string) { n.Text.Text = s }

// xform maps local canvas coordinates (y up) to screen: X = tx + sx*x, Y = ty - sy*y.
type xform struct{ tx, ty, sx, sy float64 }

func (t xform) apply(x, y float64) (float64, float64) { return t.tx + t.sx*x, t.ty - t.sy*y }

func canvasScale(n *Node, w, h float64) float64 {
	s := n.Scaler
	if s == nil || s.Mode != 1 {
		if s != nil && s.ScaleFactor > 0 {
			return s.ScaleFactor
		}
		return 1
	}
	lw, lh := math.Log2(w/s.Ref[0]), math.Log2(h/s.Ref[1])
	return math.Pow(2, lw+(lh-lw)*s.Match)
}

// layoutCanvas lays out a screen-space overlay canvas.
func layoutCanvas(root *Node, w, h float64) {
	s := canvasScale(root, w, h)
	root.hot = xform{0, h, s, s}
	root.rect = [4]float64{0, 0, w / s, h / s}
	for _, c := range root.Children {
		layout(c, root.rect, root.hot)
	}
}

func layout(n *Node, pr [4]float64, pt xform) {
	var size, ref [2]float64
	for i := 0; i < 2; i++ {
		size[i] = pr[2+i]*(n.AMax[i]-n.AMin[i]) + n.Size[i]
		ref[i] = pr[i] + pr[2+i]*(n.AMin[i]+(n.AMax[i]-n.AMin[i])*n.Pivot[i]) + n.Pos[i]
	}
	tx, ty := pt.apply(ref[0], ref[1])
	n.hot = xform{tx, ty, pt.sx * n.Scale[0], pt.sy * n.Scale[1]}
	n.rect = [4]float64{-n.Pivot[0] * size[0], -n.Pivot[1] * size[1], size[0], size[1]}
	for _, c := range n.Children {
		c.parent = n
		layout(c, n.rect, n.hot)
	}
}

// screenRect returns the node rect in screen pixels (x, y top-left, w, h).
func (n *Node) screenRect() (float64, float64, float64, float64) {
	x0, y0 := n.hot.apply(n.rect[0], n.rect[1]+n.rect[3])
	return x0, y0, n.rect[2] * n.hot.sx, n.rect[3] * n.hot.sy
}

func (n *Node) contains(mx, my float64) bool {
	x, y, w, h := n.screenRect()
	return mx >= x && mx < x+w && my >= y && my < y+h
}

// ---- input ----

var (
	uiPressed  *Node
	uiDropdown *Node // open dropdown
)

// pointerOver mirrors EventSystem.IsPointerOverGameObject: any active graphic under the cursor.
func pointerOver(roots []*Node, mx, my float64) bool {
	var hit bool
	walk(roots, func(n *Node) {
		if (n.Image != nil && n.Image.Enabled || n.Button != nil || n.Input != nil || n.Dropdown != nil) && n.contains(mx, my) { // text labels never block the map
			hit = true
		}
	})
	return hit || uiDropdown != nil
}

func walk(roots []*Node, fn func(*Node)) {
	for _, r := range roots {
		if !r.Active {
			continue
		}
		fn(r)
		walk(r.Children, fn)
	}
}

// updateUI handles clicks for buttons, input fields and dropdowns; call handles persistent calls.
func updateUI(roots []*Node, call func(uiCall, *Node)) {
	x, y := cursorPos()
	mx, my := float64(x), float64(y)
	if uiDropdown != nil && pointerJustReleased() {
		d := uiDropdown
		uiDropdown = nil
		for i := range d.Dropdown.Options {
			if ix, iy, iw, ih := dropdownItem(d, i); mx >= ix && mx < ix+iw && my >= iy && my < iy+ih {
				d.value = i
				d.Find("Label").SetText(d.Dropdown.Options[i])
				for _, c := range d.Dropdown.OnValueChanged {
					c.Int = i
					call(c, d)
				}
			}
		}
		return
	}
	var top *Node
	walk(roots, func(n *Node) {
		if (n.Button != nil || n.Input != nil || n.Dropdown != nil) && n.contains(mx, my) {
			top = n
		}
	})
	if pointerJustPressed() {
		uiPressed = top
		walk(roots, func(n *Node) {
			if n.Input != nil && n.focused && n != top {
				n.focused = false
				for _, c := range n.Input.OnEndEdit {
					call(c, n)
				}
			}
		})
	}
	if pointerJustReleased() {
		if top != nil && top == uiPressed {
			switch {
			case top.Button != nil && top.Disabled:
			case top.Button != nil:
				top.downTill = now + 0.12
				if top.OnClick != nil {
					top.OnClick()
				}
				for _, c := range top.Button.OnClick {
					call(c, top)
				}
			case top.Input != nil:
				top.focused = true
			case top.Dropdown != nil:
				uiDropdown = top
			}
		}
		uiPressed = nil
	}
	walk(roots, func(n *Node) {
		if n.Input == nil || !n.focused {
			return
		}
		t := n.Find("Text")
		for _, r := range ebiten.AppendInputChars(nil) {
			t.Text.Text += string(r)
		}
		if inpututil.IsKeyJustPressed(ebiten.KeyBackspace) && len(t.Text.Text) > 0 {
			t.Text.Text = t.Text.Text[:len(t.Text.Text)-1]
		}
		if inpututil.IsKeyJustPressed(ebiten.KeyEnter) {
			n.focused = false
			for _, c := range n.Input.OnEndEdit {
				call(c, n)
			}
		}
	})
}

// ---- drawing ----

var imageCache = map[string]*ebiten.Image{}

func spriteImage(s *uiSprite) *ebiten.Image {
	img, ok := imageCache[s.File]
	if !ok {
		img = loadImage(s.File)
		imageCache[s.File] = img
	}
	return img.SubImage(image.Rect(s.X, s.Y, s.X+s.W, s.Y+s.H)).(*ebiten.Image)
}

func mulColor(a, b [4]float64) [4]float64 {
	return [4]float64{a[0] * b[0], a[1] * b[1], a[2] * b[2], a[3] * b[3]}
}

func toColor(c [4]float64) color.Color {
	cl := func(v float64) float64 { return math.Min(math.Max(v, 0), 1) }
	return rgba(cl(c[0]), cl(c[1]), cl(c[2]), cl(c[3]))
}

func drawUI(dst *ebiten.Image, roots []*Node) {
	x, y := cursorPos()
	walk(roots, func(n *Node) { drawNode(dst, n, float64(x), float64(y)) })
	walk(roots, func(n *Node) { // InputField caret: 1 unit wide, blink rate 0.85
		if n.Input == nil || !n.focused || math.Mod(now*0.85, 1) >= 0.5 {
			return
		}
		t := n.Find("Text")
		x, y, _, _ := t.screenRect()
		face := &text.GoTextFace{Source: arial, Size: pixelSize(t.Text.Size * t.hot.sx)}
		cx := x + text.Advance(t.Text.Text, face)
		h := face.Metrics().HAscent + face.Metrics().HDescent
		vector.FillRect(dst, float32(cx), float32(y), float32(t.hot.sx), float32(h), toColor([4]float64{0.19607843, 0.19607843, 0.19607843, 1}), false)
	})
	if d := uiDropdown; d != nil {
		tmpl, lbl := d.Find("Template"), d.Find("Item Label")
		lx, ly, lw, lh := dropdownList(d)
		drawSprite(dst, tmpl.Image.Sprite, lx, ly, lw, lh, 1, tmpl.Image.Type, tmpl.Image.Color, d.hot.sx)
		k := d.hot.sx
		for i, o := range d.Dropdown.Options {
			ix, iy, iw, ih := dropdownItem(d, i)
			if i == d.value {
				drawCheckmark(dst, ix+10*k, iy+ih/2, 20*k)
			}
			drawString(dst, o, lbl.Text.Font, lbl.Text.Size, ix+20*k, iy+2*k, iw-30*k, ih-3*k, lbl.Text.Align, lbl.Text.Color, k, lbl.Text.HOverflow, lbl.Text.VOverflow, lbl.Text.LineSpacing)
		}
	}
}

// dropdownList mirrors Dropdown.Show: the template shrinks to 20 per item + 8 padding, hangs 2 units
// below the dropdown, and flips above it when it would leave the screen.
func dropdownList(d *Node) (float64, float64, float64, float64) {
	x, y, w, h := d.screenRect()
	k := d.hot.sx
	lh := (20*float64(len(d.Dropdown.Options)) + 8) * k
	top := y + h - 2*k
	if top+lh > screenH {
		top = y + 2*k - lh
	}
	return x, top, w, lh
}

func dropdownItem(d *Node, i int) (float64, float64, float64, float64) {
	x, top, w, _ := dropdownList(d)
	k := d.hot.sx
	return x, top + (4+20*float64(i))*k, w, 20 * k
}

func drawCheckmark(dst *ebiten.Image, cx, cy, size float64) {
	var p vector.Path
	p.MoveTo(float32(cx-size*0.3), float32(cy))
	p.LineTo(float32(cx-size*0.08), float32(cy+size*0.22))
	p.LineTo(float32(cx+size*0.3), float32(cy-size*0.25))
	sop := &vector.StrokeOptions{Width: float32(size * 0.12)}
	dop := &vector.DrawPathOptions{}
	dop.ColorScale.ScaleWithColor(toColor([4]float64{0.19607843, 0.19607843, 0.19607843, 1}))
	vector.StrokePath(dst, &p, sop, dop)
}

// isDown: the button is held under the cursor, or was just clicked.
func (n *Node) isDown(mx, my float64) bool {
	return n != nil && n.Button != nil && !n.Disabled && (uiPressed == n && pointerDown() && n.contains(mx, my) || now < n.downTill)
}

func drawNode(dst *ebiten.Image, n *Node, mx, my float64) {
	x, y, w, h := n.screenRect()
	swap := func(b *Node) bool { return b != nil && b.Button != nil && b.Button.PressedSprite != nil }
	// pressed: swap to the pressed sprite; buttons without one sink and darken instead
	down := n.isDown(mx, my) && !swap(n) || n.parent.isDown(mx, my) && !swap(n.parent)
	if down {
		y += math.Max(math.Floor(n.hot.sy), 1) * 1.5
	}
	if im := n.Image; im != nil && im.Enabled {
		clr, sp := im.Color, im.Sprite
		if swap(n) && n.isDown(mx, my) {
			sp = n.Button.PressedSprite
		}
		if down {
			clr = mulColor(clr, [4]float64{0.7, 0.7, 0.7, 1})
		}
		if b := n.Button; b != nil && b.Transition == 1 {
			tint := b.Normal
			if n.contains(mx, my) {
				tint = b.Highlighted
				if uiPressed == n && pointerDown() {
					tint = b.Pressed
				}
			}
			for i := range tint {
				tint[i] *= b.Multiplier
			}
			clr = mulColor(clr, tint)
		}
		uiGray = n.grayed()
		drawSprite(dst, sp, x, y, w, h, n.hot.sx, im.Type, clr, n.hot.sx)
		uiGray = false
	}
	if t := n.Text; t != nil && t.Enabled {
		if p := n.parent; p != nil && (p.Button != nil || p.Input != nil || p.Dropdown != nil) && p.contains(x+w/2, y+h/2) {
			// keep labels inside the control's frame (the scenes' text boxes often stick out)
			px, py, pw, ph := p.screenRect()
			pad := 0.0
			if p.Image != nil && p.Image.Type == 1 { // stay clear of a sliced frame's border
				pad = math.Min(pw, ph) * 0.08
			}
			x, y, w, h = intersect(x, y, w, h, px+pad, py+pad, pw-2*pad, ph-2*pad)
		}
		tc := t.Color
		if n.grayed() {
			l := (tc[0]*0.3 + tc[1]*0.59 + tc[2]*0.11) * 0.45
			tc = [4]float64{l, l, l, tc[3]}
		}
		drawString(dst, t.Text, t.Font, t.Size, x, y, w, h, t.Align, tc, n.hot.sx, t.HOverflow, t.VOverflow, t.LineSpacing)
	}
}

var uiGray bool // drawImg renders desaturated and dimmed (disabled buttons)

func drawImg(dst, img *ebiten.Image, op *ebiten.DrawImageOptions) {
	if !uiGray {
		dst.DrawImage(img, op)
		return
	}
	var cm colorm.ColorM
	cs := op.ColorScale
	cm.Scale(float64(cs.R()), float64(cs.G()), float64(cs.B()), float64(cs.A())) // keep the tint's brightness, then drop the hue
	cm.ChangeHSV(0, 0, 0.5)
	cop := &colorm.DrawImageOptions{GeoM: op.GeoM}
	colorm.DrawImage(dst, img, cm, cop)
}

func (n *Node) grayed() bool { return n.Disabled || n.parent != nil && n.parent.Disabled }

func drawSprite(dst *ebiten.Image, s *uiSprite, x, y, w, h, scale float64, typ int, clr [4]float64, canvasScale float64) {
	if s == nil || s.Builtin != 0 {
		if s != nil && s.Builtin == 10915 { // dropdown arrow
			cx, cy := x+w/2, y+h/2
			var p vector.Path
			p.MoveTo(float32(cx-w/4), float32(cy-h/8))
			p.LineTo(float32(cx+w/4), float32(cy-h/8))
			p.LineTo(float32(cx), float32(cy+h/6))
			p.Close()
			op := &vector.FillOptions{}
			dop := &vector.DrawPathOptions{}
			dop.ColorScale.ScaleWithColor(toColor(mulColor(clr, [4]float64{0.2, 0.2, 0.2, 1})))
			vector.FillPath(dst, &p, op, dop)
			return
		}
		vector.FillRect(dst, float32(x), float32(y), float32(w), float32(h), toColor(clr), false)
		return
	}
	img := spriteImage(s)
	cs := &ebiten.ColorScale{}
	cs.ScaleWithColor(toColor(clr))
	if typ != 1 {
		op := &ebiten.DrawImageOptions{}
		op.GeoM.Scale(w/float64(s.W), h/float64(s.H))
		op.GeoM.Translate(x, y)
		op.ColorScale = *cs
		drawImg(dst, img, op)
		return
	}
	// sliced: Unity scaled borders by 100/PPU, which blew the corner screws up to ~80px; draw them
	// at a whole pixel scale instead so the frame stays crisp and the label has room
	k := math.Max(math.Floor(canvasScale), 1)
	l, b, r, t := s.Border[0]*k, s.Border[1]*k, s.Border[2]*k, s.Border[3]*k
	if l+r > w {
		f := w / (l + r)
		l, r = l*f, r*f
	}
	if t+b > h {
		f := h / (t + b)
		t, b = t*f, b*f
	}
	sx := []float64{0, s.Border[0], float64(s.W) - s.Border[2], float64(s.W)}
	sy := []float64{0, s.Border[3], float64(s.H) - s.Border[1], float64(s.H)}
	dx := []float64{x, x + l, x + w - r, x + w}
	dy := []float64{y, y + t, y + h - b, y + h}
	for i := 0; i < 3; i++ {
		for j := 0; j < 3; j++ {
			sw, sh := sx[i+1]-sx[i], sy[j+1]-sy[j]
			dw, dh := dx[i+1]-dx[i], dy[j+1]-dy[j]
			if sw <= 0 || sh <= 0 || dw <= 0 || dh <= 0 {
				continue
			}
			part := img.SubImage(image.Rect(s.X+int(sx[i]), s.Y+int(sy[j]), s.X+int(sx[i+1]), s.Y+int(sy[j+1]))).(*ebiten.Image)
			op := &ebiten.DrawImageOptions{}
			op.GeoM.Scale(dw/sw, dh/sh)
			op.GeoM.Translate(dx[i], dy[j])
			op.ColorScale = *cs
			drawImg(dst, part, op)
		}
	}
}

func intersect(x, y, w, h, x2, y2, w2, h2 float64) (float64, float64, float64, float64) {
	l, t := math.Max(x, x2), math.Max(y, y2)
	r, b := math.Min(x+w, x2+w2), math.Min(y+h, y2+h2)
	return l, t, math.Max(r-l, 0), math.Max(b-t, 0)
}

// drawString lays out Unity Text: align 0..8 = upper/middle/lower x left/center/right. Like Unity's
// Best Fit, the text shrinks (in whole font pixels, so pixel fonts stay crisp) until it fits the box,
// and nothing is drawn outside the box or the screen.
func drawString(dst *ebiten.Image, s, fontName string, size, x, y, w, h float64, align int, clr [4]float64, scale float64, hOverflow, vOverflow int, lineSpacing float64) {
	if s == "" || w <= 0 || h <= 0 {
		return
	}
	b := dst.Bounds()
	clip := image.Rect(int(math.Floor(x)), int(math.Floor(y)), int(math.Ceil(x+w)), int(math.Ceil(y+h))).Intersect(b)
	// fit into what is on screen, measured from the alignment anchor so centred text stays centred
	fitW, fitH := visibleSpan(x, w, float64(b.Min.X), float64(b.Max.X), align%3), visibleSpan(y, h, float64(b.Min.Y), float64(b.Max.Y), align/3)
	if clip.Empty() {
		return
	}
	dst = dst.SubImage(clip).(*ebiten.Image)
	f := ui.Fonts[fontName]
	if f != nil && f.img == nil {
		f.img = loadImage(f.Texture)
	}
	var face *text.GoTextFace
	var lineH float64
	var lines []string
	adv := func(line string) float64 {
		if f == nil {
			return text.Advance(line, face)
		}
		sum := 0.0
		for _, r := range line {
			if g, ok := f.Chars[int(r)]; ok {
				sum += g[8]
			}
		}
		return sum * scale
	}
	fits := func() bool {
		lines = nil
		for _, para := range strings.Split(s, "\n") {
			if hOverflow == 1 || adv(para) <= fitW {
				lines = append(lines, para)
				continue
			}
			cur := ""
			for _, word := range strings.Fields(para) {
				if cur != "" && adv(cur+" "+word) > fitW {
					lines = append(lines, cur)
					cur = word
				} else if cur == "" {
					cur = word
				} else {
					cur += " " + word
				}
			}
			lines = append(lines, cur)
		}
		widest := 0.0
		for _, l := range lines {
			widest = math.Max(widest, adv(l))
		}
		return widest <= fitW+0.5 && float64(len(lines))*lineH <= fitH+0.5
	}
	if f == nil {
		face = &text.GoTextFace{Source: arial, Size: pixelSize(size * scale)}
		for {
			m := face.Metrics()
			lineH = (m.HLineGap + m.HAscent + m.HDescent) * lineSpacing
			if fits() || face.Size <= 8 {
				break
			}
			face.Size -= 8
		}
	} else {
		scale = math.Max(math.Floor(scale), 1)
		for {
			lineH = f.LineSpacing * scale * lineSpacing
			if fits() || scale <= 1 {
				break
			}
			scale--
		}
	}
	blockH := float64(len(lines)) * lineH
	top := y
	switch align / 3 {
	case 1:
		top = y + (h-blockH)/2
	case 2:
		top = y + h - blockH
	}
	for i, line := range lines {
		lx := x
		switch align % 3 {
		case 1:
			lx = x + (w-adv(line))/2
		case 2:
			lx = x + w - adv(line)
		}
		ly := math.Round(top + float64(i)*lineH)
		if f == nil {
			op := &text.DrawOptions{}
			op.GeoM.Translate(math.Round(lx), ly)
			op.ColorScale.ScaleWithColor(toColor(clr))
			text.Draw(dst, line, face, op)
			continue
		}
		pen := math.Round(lx)
		for _, r := range line {
			g, ok := f.Chars[int(r)]
			if !ok {
				continue
			}
			if g[2] > 0 {
				part := f.img.SubImage(image.Rect(int(g[0]), int(g[1]), int(g[0]+g[2]), int(g[1]+g[3]))).(*ebiten.Image)
				op := &ebiten.DrawImageOptions{}
				op.GeoM.Scale(math.Abs(g[6])/g[2]*scale, math.Abs(g[7])/g[3]*scale)
				op.GeoM.Translate(pen+g[4]*scale, ly-g[5]*scale)
				op.ColorScale.ScaleWithColor(toColor(clr))
				dst.DrawImage(part, op)
			}
			pen += g[8] * scale
		}
	}
}

// pixelSize rounds to a multiple of Silkscreen's 8px design grid.
func pixelSize(px float64) float64 { return math.Max(math.Floor(px/8), 1) * 8 }

// visibleSpan is how much of [a, a+size] is usable between lo and hi for text anchored at the start (0),
// centre (1) or end (2).
func visibleSpan(a, size, lo, hi float64, anchor int) float64 {
	switch anchor {
	case 1:
		c := a + size/2
		return math.Max(2*math.Min(math.Min(c-lo, hi-c), size/2), 0)
	case 2:
		return math.Max(math.Min(a+size, hi)-math.Max(a, lo), 0)
	}
	return math.Max(math.Min(a+size, hi)-math.Max(a, lo), 0)
}
