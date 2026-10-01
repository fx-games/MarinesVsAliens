package main

import (
	"fmt"
	"slices"

	"gopkg.in/yaml.v3"
)

// Rule structs mirror Mod.cs; the .rul files are read unchanged from Assets/StreamingAssets.

type Rules struct {
	Options      RuleOptions     `yaml:"options"`
	Factions     []*RuleFaction  `yaml:"factions"`
	Units        []*RuleUnit     `yaml:"units"`
	Weapons      []*RuleWeapon   `yaml:"weapons"`
	MapBlocks    []*RuleMapBlock `yaml:"mapBlocks"`
	Maps         []*RuleMap      `yaml:"maps"`
	Skills       []*RuleSkill    `yaml:"skills"`
	Translations []struct {
		Lang    string            `yaml:"lang"`
		Strings map[string]string `yaml:"strings"`
	} `yaml:"translations"`
}

// RuleOptions are the game-wide rules (the original's Options.cs plus values that used to be hardcoded).
type RuleOptions struct {
	AllowDiagonalMovement        bool       `yaml:"allowDiagonalMovement"`
	AllowDiagonalAdjacentAttacks bool       `yaml:"allowDiagonalAdjacentAttacks"`
	AllowRangedWeaponsRangeOne   bool       `yaml:"allowRangedWeaponsRangeOne"`
	DiagonalMoveCost             float64    `yaml:"diagonalMoveCost"` // times the cost of a straight step
	MinimumDamage                int        `yaml:"minimumDamage"`    // a hit deals at least this much through armour
	WallHealth                   int        `yaml:"wallHealth"`       // damage a wall piece takes before it breaks
	SkillAPCost                  int        `yaml:"skillAPCost"`      // skills without apCost that aren't attacks
	WeaponDefaults               RuleWeapon `yaml:"weaponDefaults"`   // for fields a weapon leaves out
}

type RuleMapBlock struct {
	Type            string  `yaml:"type"`
	Size            []int   `yaml:"size"`
	Rooms           [][]int `yaml:"rooms"`
	HorizontalWalls [][]int `yaml:"horizontalWalls"`
	VerticalWalls   [][]int `yaml:"verticalWalls"`
	HorizontalDoors [][]int `yaml:"horizontalDoors"`
	VerticalDoors   [][]int `yaml:"verticalDoors"`
}

// Map generators: 0 premade, 1 map script, 2 dungeon, 3 cavern (Map.FromRules).
const (
	GenPremade = iota
	GenScript
	GenDungeon
	GenCavern
)

type RuleMap struct {
	Type      string    `yaml:"type"`
	Premade   string    `yaml:"premade"`
	Generator Generator `yaml:"generator"`
	Size      []int     `yaml:"size"`
	MapScript []struct {
		Command string   `yaml:"command"`
		Blocks  []string `yaml:"blocks"`
		Rects   [][]int  `yaml:"rects"`
	} `yaml:"mapScript"`
}

type RuleFaction struct {
	Type          string         `yaml:"type"`
	Color         []int          `yaml:"color"`
	FactionPrefab int            `yaml:"factionPrefab"`
	LoadOut       []*RuleLoadout `yaml:"loadOut"`
}

type RuleLoadout struct {
	Unit       string   `yaml:"unit"`
	Count      int      `yaml:"count"`
	Weapons    []string `yaml:"weapons"`
	DoNotSpawn bool     `yaml:"doNotSpawn"`
}

func (l *RuleLoadout) UnmarshalYAML(n *yaml.Node) error {
	type raw RuleLoadout
	r := raw{Count: 1}
	if err := n.Decode(&r); err != nil {
		return err
	}
	*l = RuleLoadout(r)
	return nil
}

type RuleUnit struct {
	Type              string         `yaml:"type"`
	Stats             map[string]int `yaml:"stats"`
	OccupiedTiles     [][]int        `yaml:"occupiedTiles"`
	CanMoveDiagonally bool           `yaml:"canMoveDiagonally"`
	FactionPrefab     int            `yaml:"factionPrefab"`
	SpriteOffset      int            `yaml:"spriteOffset"`
	UnitSkills        []string       `yaml:"unitSkills"`
}

func (u *RuleUnit) UnmarshalYAML(n *yaml.Node) error {
	type raw RuleUnit
	r := raw{OccupiedTiles: [][]int{{0, 0}}, CanMoveDiagonally: true, FactionPrefab: -1}
	if err := n.Decode(&r); err != nil {
		return err
	}
	*u = RuleUnit(r)
	return nil
}

type RuleSkill struct {
	Name              string        `yaml:"name"`
	Effects           []*RuleEffect `yaml:"effects"`
	Buffs             []*RuleBuff   `yaml:"buffs"`
	TargetMode        RuleTargeting `yaml:"targetMode"`
	DisplayName       string        `yaml:"displayName"`
	Description       string        `yaml:"description"` // button tooltip
	IsMelee           bool          `yaml:"isMelee"`
	ChangeSpriteTo    int           `yaml:"changeSpriteTo"`
	IsOffensiveAction bool          `yaml:"isOffensiveAction"`
	APCost            *int          `yaml:"apCost"`    // default: the weapon's useCost for attacks, options.skillAPCost otherwise
	MaxActive         int           `yaml:"maxActive"` // SpawnUnit skills: how many of the spawned unit a team may have (0 = no limit)
}

func (s *RuleSkill) UnmarshalYAML(n *yaml.Node) error {
	type raw RuleSkill
	r := raw{ChangeSpriteTo: -1}
	if err := n.Decode(&r); err != nil {
		return err
	}
	*s = RuleSkill(r)
	return nil
}

type RuleEffect struct {
	Name       string  `yaml:"name"`
	StringOne  string  `yaml:"stringOne"`
	IntegerOne int     `yaml:"integerOne"`
	IntegerTwo int     `yaml:"integerTwo"`
	FloatOne   float64 `yaml:"floatOne"`
	Chained    bool    `yaml:"chained"`
	BoolOne    bool    `yaml:"boolOne"`
}

type RuleBuff struct {
	Name          string `yaml:"name"`
	Timer         int    `yaml:"timer"`
	StatModifiers []struct {
		Name  string `yaml:"name"`
		Value int    `yaml:"value"`
	} `yaml:"statModifiers"`
	EndCondition EndCondition `yaml:"endCondition"`
}

// TargetModeType
const (
	TargetSelf = iota
	TargetSingle
	TargetMultiple
)

// TargetType flags
const (
	TTFloor = 1 << iota
	TTWall
	TTEnemies
	TTFriendlies
)

// TargetPreset
const (
	PresetNone = iota
	PresetCardinalMelee
	PresetRangedAttack
	PresetAllVisible
	PresetAllReachable
)

// AffectedPreset
const (
	AffectedNone = iota
	AffectedExplosion
	AffectedStraightLine
)

type RuleTargeting struct {
	Type                 TargetModeType `yaml:"type"`
	AllowedTargetTypes   TargetTypes    `yaml:"allowedTargetTypes"`
	AllowedTargetPreset  TargetPreset   `yaml:"allowedTargetPreset"`
	ActiveAffectedPreset AffectedPreset `yaml:"activeAffectedPreset"`
	AllowedTargets       [][]int        `yaml:"allowedTargets"`
	AffectedTiles        [][]int        `yaml:"affectedTiles"`
	AffectedRange        int            `yaml:"affectedRange"`
}

type RuleWeapon struct {
	UseCost            int     `yaml:"useCost"`
	Type               string  `yaml:"type"`
	Damage             [][]int `yaml:"damage"`
	MinRange           int     `yaml:"minRange"`
	MaxRange           int     `yaml:"maxRange"`
	MinDropOff         int     `yaml:"minDropOff"`
	MaxDropOff         int     `yaml:"maxDropOff"`
	EffectiveRange     int     `yaml:"effectiveRange"`
	AccuracyModifier   int     `yaml:"accuracyModifier"`
	Radius             int     `yaml:"radius"`
	IsMelee            bool    `yaml:"isMelee"`
	StraightLineOnly   bool    `yaml:"straightLineOnly"`
	MultipleTargets    bool    `yaml:"multipleTargets"`
	DamageDistribution bool    `yaml:"damageDistribution"`
	DamageLineOfFire   bool    `yaml:"damageLineOfFire"`
	// projectile animation: sprite in spr.Bullets, shots per attack, speed in tiles/s, lobbed
	Projectile struct {
		Sprite, Count int
		Speed         float64
		Arc           bool
	} `yaml:"projectile"`
}

func (w *RuleWeapon) UnmarshalYAML(n *yaml.Node) error {
	type raw RuleWeapon
	r := raw(weaponDefaults)
	if err := n.Decode(&r); err != nil {
		return err
	}
	*w = RuleWeapon(r)
	return nil
}

type Mod struct {
	Rules
	units   map[string]*RuleUnit
	weapons map[string]*RuleWeapon
	skills  map[string]*RuleSkill
	strings map[string]string
}

var weaponDefaults RuleWeapon // options.weaponDefaults, applied while decoding the weapons

func LoadMod(path string) (*Mod, error) {
	b, err := readAsset(path)
	if err != nil {
		return nil, err
	}
	var opts struct {
		Options RuleOptions `yaml:"options"`
	}
	weaponDefaults = RuleWeapon{}
	if err := yaml.Unmarshal(b, &opts); err != nil { // first, so the weapons get their defaults
		return nil, fmt.Errorf("%s: %w", path, err)
	}
	weaponDefaults = opts.Options.WeaponDefaults
	m := &Mod{units: map[string]*RuleUnit{}, weapons: map[string]*RuleWeapon{}, skills: map[string]*RuleSkill{}, strings: map[string]string{}}
	if err := yaml.Unmarshal(b, &m.Rules); err != nil {
		return nil, fmt.Errorf("%s: %w", path, err)
	}
	for _, u := range m.Units {
		m.units[u.Type] = u
	}
	for _, w := range m.Weapons {
		m.weapons[w.Type] = w
	}
	for _, s := range m.Skills {
		m.skills[s.Name] = s
	}
	for _, t := range m.Translations {
		if t.Lang == "en-US" {
			m.strings = t.Strings
		}
	}
	return m, nil
}

func (m *Mod) Tr(key string) string {
	if s, ok := m.strings[key]; ok {
		return s
	}
	return key
}

func (m *Mod) MapBlock(name string) *RuleMapBlock {
	for _, b := range m.MapBlocks {
		if b.Type == name {
			return b
		}
	}
	panic("unknown map block " + name)
}

func (m *Mod) Loadout(f *RuleFaction, unitType string) *RuleLoadout {
	for _, l := range f.LoadOut {
		if l.Unit == unitType {
			return l
		}
	}
	panic("UnitType " + unitType + " has no loadout.")
}

// ---- enums: written by name in the rules file ----

type (
	TargetModeType int
	TargetTypes    int // flags, a list of names in the rules
	TargetPreset   int
	AffectedPreset int
	EndCondition   int
	Generator      int
)

var enumNames = map[string][]string{
	"TargetModeType": {"self", "single", "multiple"},
	"TargetTypes":    {"floor", "wall", "enemies", "friendlies"},
	"TargetPreset":   {"none", "cardinalMelee", "rangedAttack", "visibleTiles", "reachableTiles"},
	"AffectedPreset": {"none", "explosion", "straightLine"},
	"EndCondition":   {"none", "onFirstAttack"},
	"Generator":      {"premade", "mapScript", "dungeon", "cavern"},
}

func decodeEnum(n *yaml.Node, kind string) (int, error) {
	i := slices.Index(enumNames[kind], n.Value)
	if n.Kind != yaml.ScalarNode || i < 0 {
		return 0, fmt.Errorf("line %d: %q is not a %s, use one of %v", n.Line, n.Value, kind, enumNames[kind])
	}
	return i, nil
}

func (e *TargetModeType) UnmarshalYAML(n *yaml.Node) error {
	v, err := decodeEnum(n, "TargetModeType")
	*e = TargetModeType(v)
	return err
}

func (e *TargetPreset) UnmarshalYAML(n *yaml.Node) error {
	v, err := decodeEnum(n, "TargetPreset")
	*e = TargetPreset(v)
	return err
}

func (e *AffectedPreset) UnmarshalYAML(n *yaml.Node) error {
	v, err := decodeEnum(n, "AffectedPreset")
	*e = AffectedPreset(v)
	return err
}

func (e *EndCondition) UnmarshalYAML(n *yaml.Node) error {
	v, err := decodeEnum(n, "EndCondition")
	*e = EndCondition(v)
	return err
}

func (e *Generator) UnmarshalYAML(n *yaml.Node) error {
	v, err := decodeEnum(n, "Generator")
	*e = Generator(v)
	return err
}

func (e *TargetTypes) UnmarshalYAML(n *yaml.Node) error {
	*e = 0
	for _, c := range n.Content {
		v, err := decodeEnum(c, "TargetTypes")
		if err != nil {
			return err
		}
		*e |= 1 << v
	}
	if n.Kind != yaml.SequenceNode {
		return fmt.Errorf("line %d: allowedTargetTypes is a list, e.g. [enemies, friendlies]", n.Line)
	}
	return nil
}
