package main

import (
	"encoding/json"
	"os"
	"path/filepath"

	"github.com/hajimehoshi/ebiten/v2"
)

// settings survive restarts in the user config dir. Errors are ignored: the web build has no
// file system and simply starts with the defaults.
type settings struct {
	Volume     float64
	MusicOn    bool
	Fullscreen bool
}

func settingsPath() string {
	dir, err := os.UserConfigDir()
	if err != nil {
		return ""
	}
	return filepath.Join(dir, "marinesvsaliens", "settings.json")
}

func loadSettings() {
	b, err := os.ReadFile(settingsPath())
	if err != nil {
		return
	}
	s := settings{Volume: volume, MusicOn: !musicMuted}
	if json.Unmarshal(b, &s) == nil {
		volume, musicMuted = s.Volume, !s.MusicOn
		ebiten.SetFullscreen(s.Fullscreen)
	}
}

func saveSettings(fullscreen bool) {
	p := settingsPath()
	if p == "" {
		return
	}
	b, _ := json.Marshal(settings{volume, !musicMuted, fullscreen})
	if os.MkdirAll(filepath.Dir(p), 0o755) == nil {
		os.WriteFile(p, b, 0o644)
	}
}
