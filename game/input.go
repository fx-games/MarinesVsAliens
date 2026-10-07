package main

import (
	"math"

	"github.com/hajimehoshi/ebiten/v2"
	"github.com/hajimehoshi/ebiten/v2/inpututil"
)

// The pointer folds mouse and touch into one: one finger is the left button, a second finger
// tapping is the right button (step back), and pinching zooms like the wheel.
var ptr struct {
	x, y          int
	down, wasDown bool
	touch         bool // last input was a touch; the cursor stays where the finger lifted
	back          bool
	cancel        bool // a second finger joined: this press is not a click
	pinch, zoom   float64
}

// updatePointer runs once per frame before anything reads the pointer.
func updatePointer() {
	ptr.wasDown, ptr.back, ptr.zoom = ptr.down, false, 0
	ids := ebiten.AppendTouchIDs(nil)
	switch {
	case len(ids) > 0:
		ptr.touch = true
		ptr.x, ptr.y = ebiten.TouchPosition(ids[0])
		ptr.down = true
		if !ptr.wasDown {
			ptr.cancel = false
		}
		if len(ids) >= 2 {
			ptr.cancel = true
			x2, y2 := ebiten.TouchPosition(ids[1])
			d := math.Hypot(float64(x2-ptr.x), float64(y2-ptr.y))
			if len(inpututil.AppendJustPressedTouchIDs(nil)) > 0 {
				ptr.back = true
			} else if ptr.pinch > 0 {
				ptr.zoom = (d - ptr.pinch) / 40
			}
			ptr.pinch = d
		} else {
			ptr.pinch = 0
		}
	case ptr.touch:
		ptr.down, ptr.pinch = false, 0
		ptr.touch = !ebiten.IsMouseButtonPressed(ebiten.MouseButtonLeft)
	}
	if !ptr.touch {
		ptr.x, ptr.y = ebiten.CursorPosition()
		ptr.down = ebiten.IsMouseButtonPressed(ebiten.MouseButtonLeft)
	}
}

func cursorPos() (int, int)     { return ptr.x, ptr.y }
func pointerDown() bool         { return ptr.down }
func pointerJustPressed() bool  { return ptr.down && !ptr.wasDown }
func pointerJustReleased() bool { return !ptr.down && ptr.wasDown }
func backJustPressed() bool {
	return ptr.back || inpututil.IsMouseButtonJustPressed(ebiten.MouseButtonRight)
}
func wheelY() float64 { _, w := ebiten.Wheel(); return w + ptr.zoom }
