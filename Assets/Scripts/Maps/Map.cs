using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Default;
using DefaultNamespace;
using QuickGraph;
using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEngine.UI;
using static Map;
using static Mod;
using Color = UnityEngine.Color;
using Random = UnityEngine.Random;

public abstract class GeneratorBase
{
    protected Map map;

    public void SetMap(Map map)
    {
        this.map = map;
    }
    public abstract IEnumerator Generate();
    public abstract void Step();
}
public class Map : MonoBehaviour, IGridMap, IPlayerMap
{
    public enum Direction
    {
        North,
        East,
        South,
        West,
        NorthEast,
        SouthEast,
        NorthWest,
        SouthWest,
        None       
    }

    public int Width { get; set; }
    public int Height { get; set; }

    public Direction[] Cardinal => new[] {Direction.East, Direction.West, Direction.North, Direction.South};
    public Direction[] Diagonal => new[] {Direction.NorthEast, Direction.NorthWest, Direction.SouthWest, Direction.SouthEast};
    public Direction[] Directions => new[] {Direction.East, Direction.West, Direction.North, Direction.South, Direction.NorthEast, Direction.NorthWest, Direction.SouthWest, Direction.SouthEast};


    public GameObject selectionRectPrefab;
    private GameObject selectionRect;
    private GameObject unitSelectedMarker; 
    public Tile[] floorTiles;
    public Tile[] walls;  // ORDER: NESW
    public Tile[] doors;
    public Tile fogTile;

    protected Dictionary<string, Tilemap> layerMap = new Dictionary<string, Tilemap>();
    protected Unit[][] unitMap;
    protected MapInfo[][] calcMap;
    protected bool[][] fogMap;
    protected int[][] damageMap;
    
    private Camera gameCamera;
    public GameObject blanktilePrefab;
    public Tile glowtile;

    private List<GameObject>[] allHighlights = new List<GameObject>[4]{ new List<GameObject>(), new List<GameObject>(), new List<GameObject>(), new List<GameObject>()};
    public Vector3Int CurrentCursorPosition { get; private set; }
    private HashSet<Vector3Int> visibleTiles = new HashSet<Vector3Int>();
    private Mod mod;
    private GeneratorBase mapGenerator;
    private Vector3Int lastCursorPosition;
    public GameObject cellLabelPrefab;
    private HashSet<Text> cellLabels = new HashSet<Text>();
    private Transform worldCanvas;
    private List<GameObject> allTileMarker = new List<GameObject>();
    private List<ParticleSystem> activeParticleSystems = new List<ParticleSystem>();
    private List<ParticleSystem.Particle[]> systemParticles = new List<ParticleSystem.Particle[]>();
    private Dictionary<Vector3Int, Color> particleTiles = new Dictionary<Vector3Int, Color>();

    public Tile[] spawnFloorMarkerTiles;
    private CellParticleSystem cellParticleSystem;
    private Dictionary<Color, Tile> glowTiles = new Dictionary<Color, Tile>();
    private List<LineRenderer> activeLines = new List<LineRenderer>();
    public GameObject linePrefab;
    private Raycaster raycaster;

    // Start is called before the first frame update
    void Awake()
    {
        fogTile.color = Color.black;
        worldCanvas = FindObjectsOfType<Canvas>().First(c => c.name == "WorldCanvas").transform;
        selectionRect = Instantiate(selectionRectPrefab);
        unitSelectedMarker = Instantiate(selectionRectPrefab);
        var spriteRenderer = unitSelectedMarker.GetComponent<SpriteRenderer>();
        spriteRenderer.color = Color.blue;
        spriteRenderer.enabled = false;
        
        mod = FindObjectOfType<Mod>();
        var mapLayers = GetComponentsInChildren<Tilemap>();
        foreach (Tilemap layer in mapLayers)
        {
            layerMap[layer.name] = layer;
        }

        var firstLayerSize = layerMap.First().Value.size;
        Width = firstLayerSize.x;
        Height = firstLayerSize.y;
        unitMap = new Unit[Width][];
        calcMap = new MapInfo[Width][];
        fogMap = new bool[Width][];
        damageMap = new int[Width][];
        for (int i = 0; i < Width; i++)
        {
            unitMap[i] = new Unit[Height];
            calcMap[i] = new MapInfo[Height];
            fogMap[i] = new bool[Height];
            damageMap[i] = new int[Height];
        }
        gameCamera = Camera.main;
        if (gameCamera == null)
        {
            throw new Exception("Game needs camera.");
        }
        cellParticleSystem = new CellParticleSystem(this);
        raycaster = new Raycaster(this);

        ((WallTile) walls[0]).Map = this;
        ((WallTile) walls[1]).Map = this;
    }

    public void DrawLine(Vector3Int start, Vector3Int end, Color color)
    {
        var line = Instantiate(linePrefab, CellToWorld(start), Quaternion.identity).GetComponent<LineRenderer>();
        line.SetPositions(new[]
        {
            CellToWorld(start),
            CellToWorld(end)
        });
        line.startColor = color;
        line.endColor = color;
        activeLines.Add(line);
    }

    public void RemoveAllLines()
    {
        activeLines.ForEach(l => Destroy(l.gameObject));
        activeLines.Clear();
    }
    public void FlashEffect(Vector3Int mapPos, Color color)
    {
        var selectionFlash = new Gradient(){
                colorKeys = new[]
                {
                    new GradientColorKey(color, 0.0f),
                    new GradientColorKey(color, 1.0f),
                },
                alphaKeys = new[]
                {
                    new GradientAlphaKey(0.2f, 0.0f), 
                    new GradientAlphaKey(1.0f, 1.0f), 
                }};
        
        var selectionSlow = new Gradient(){
            colorKeys = new[]
            {
                new GradientColorKey(color, 0.0f),
                new GradientColorKey(color, 1.0f),
            },
            alphaKeys = new[]
            {
                new GradientAlphaKey(0.0f, 0.0f),
                new GradientAlphaKey(0.7f, 0.0f),
                new GradientAlphaKey(1.0f, 1.0f), 
            }};
        
        //CreateParticleEffect(CurrentCursorPosition);
        var mouseInWorld = CellToWorld(mapPos);
        
        cellParticleSystem.AddParticle(new WorldParticleBase(this)
        {
            Lifetime = 0.5f,
            Gradient = selectionSlow,
            Position = mouseInWorld
        });
        
        cellParticleSystem.AddParticle(new LineParticle(this)
        {
            Lifetime = 20,
            Speed = 35,
            Gradient = selectionFlash,
            Direction = Vector3.down,
            Length = 5,
            EndLength = 1,
            Position = mouseInWorld + Vector3.down * 10,
            EndPosition = mouseInWorld,
        });
        cellParticleSystem.AddParticle(new LineParticle(this)
        {
            Lifetime = 20,
            Speed = 35,
            Gradient = selectionFlash,
            Direction = Vector3.up,
            Length = 5,
            EndLength = 1,
            Position = mouseInWorld + Vector3.up * 10,
            EndPosition = mouseInWorld,
        });
        cellParticleSystem.AddParticle(new LineParticle(this)
        {
            Lifetime = 20,
            Speed = 35,
            Gradient = selectionFlash,
            Direction = Vector3.left,
            Length = 5,
            EndLength = 1,
            Position = mouseInWorld + Vector3.left * 10,
            EndPosition = mouseInWorld,
        });
        cellParticleSystem.AddParticle(new LineParticle(this)
        {
            Lifetime = 20,
            Speed = 35,
            Gradient = selectionFlash,
            Direction = Vector3.right,
            Length = 5,
            EndLength = 1,
            Position = mouseInWorld + Vector3.right * 10,
            EndPosition = mouseInWorld,
        });
    }
    
    
    public void DebugAction()
    {
        
    }
    
    

    public void DebugActionTwo()
    {
        ((CavernGenerator) mapGenerator).MakeCaverns(1);
    }

    public void FromRules(RuleMap mapRules)
    {
        if (mapRules.Generator == 1)
        {
            mapGenerator = new FromScript(mod, mapRules);
        }
        else if (mapRules.Generator == 2)
        {
            mapGenerator = new DungeonGeneratorBase(25,25);
        }
        else if (mapRules.Generator == 3)
        {
            mapGenerator = new CavernGenerator(25, 25);
        }

        mapGenerator.SetMap(this);
        mapGenerator.Generate();        
    }

    public virtual void Init(int width, int height)
    {
        Width = width;
        Height = height;
        foreach (var kvp in layerMap)
        {
            var layer = kvp.Value;
            layer.ClearAllTiles();
            layer.size = new Vector3Int(width,height,1);
        }

        unitMap = new Unit[Width][];
        calcMap = new MapInfo[Width][];
        fogMap = new bool[Width][];
        damageMap = new int[Width][];
        for (int i = 0; i < Width; i++)
        {
            unitMap[i] = new Unit[Height];
            calcMap[i] = new MapInfo[Height];
            fogMap[i] = new bool[Height];
            damageMap[i] = new int[Height];
        }
        
        //FillLayer("FogOfWar");
    }   
    private void Start()
    {
        
    }
    
    
    // Update is called once per frame
    void Update()
    {
        // debug

        if (Input.GetKeyDown(KeyCode.F))
        {
            DebugAction();
        }
        
        // update mouse
        Vector3 pos = gameCamera.ScreenToWorldPoint(Input.mousePosition);
        var mapPos = WorldToCell(pos);
        if (Contains(mapPos.x, mapPos.y) && mapPos != lastCursorPosition)
        {
            CurrentCursorPosition = mapPos;
            selectionRect.transform.position = CellToWorld(mapPos);
            lastCursorPosition = mapPos;
        }
        
        // update & synch particlesystems
        SynchParticles();
    }

    private void SynchParticles()
    {
        cellParticleSystem.Update();
        // reset the affected tiles
        foreach (var particleTile in cellParticleSystem.TilesAffectedInLastFrame)
        {
            if (Contains(particleTile.x, particleTile.y))
            {
                layerMap["Effects"].SetTile(particleTile, null);
            }
        }
        
        foreach (var kvp in cellParticleSystem.TileColoring)
        {
            var mapPos = kvp.Key;
            if (Contains(mapPos.x, mapPos.y))
            {
                layerMap["Effects"].SetTile(mapPos, GlowtileByColor(kvp.Value));
            }
        }
    }

    private TileBase GlowtileByColor(Color color)
    {
        if (!glowTiles.ContainsKey(color))
        {
            Tile colorTile = ScriptableObject.CreateInstance<Tile>();
            colorTile.sprite = glowtile.sprite;
            colorTile.color = color;

            glowTiles[color] = colorTile;
        }
        
        return glowTiles[color];
    }


    public bool IsDoorAt(Vector3Int pos)
    {
        return calcMap[pos.x][pos.y] == MapInfo.Door;
    }

    public bool IsWallAt(int x, int y)
    {
        return calcMap[x][y] == MapInfo.Wall;
    }

    public bool IsWallAt(Vector3Int pos)
    {
        return IsWallAt(pos.x, pos.y);
    }
    public bool IsEmptyFloorTile(Vector3Int mapPos)
    {
        return IsFloorAt(mapPos) && !IsUnitOn(mapPos);
    }
    public bool IsFloorAt(int x, int y)
    {
        return calcMap[x][y] == MapInfo.Floor;
    }
    private bool IsFloorAt(Vector3Int pos)
    {
        return calcMap[pos.x][pos.y] == MapInfo.Floor;
    }
    public void RemoveDoor(Vector3Int pos)
    {
        layerMap["Doors"].SetTile(pos, null);
        calcMap[pos.x][pos.y] = MapInfo.Floor;
    }
    private bool IsDoorAt(int x, int y)
    {
        return calcMap[x][y] == MapInfo.Door;
    }
    public void CreateDoor(int x, int y, int tileIndex = 0)
    {
        var pos = new Vector3Int(x,y,0);
        layerMap["Doors"].SetTile(pos, doors[tileIndex]);
        calcMap[pos.x][pos.y] = MapInfo.Door;
        RemoveWallAt(pos); // no walls at door tiles
    }
    
    public void CreateDoor(Vector3Int pos, int tileIndex = 0)
    {
        CreateDoor(pos.x, pos.y, tileIndex);
    }

    // returns true if wall is destroyed and fov update needed
    public bool AddDamageToTile(Vector3Int mapPos, int damage)
    {
        Debug.Log(damage + " damage to " + mapPos);
        damageMap[mapPos.x][mapPos.y] += damage;
        if (layerMap["Walls"].HasTile(mapPos))
        {
            //layerMap["Walls"].RefreshTile(mapPos);
            layerMap["Walls"].RefreshAllTiles();
            if (IsWallAt(mapPos))
            {
                var wallTile = layerMap["Walls"].GetTile<WallTile>(mapPos);
                var hasWallDamagedSprite = wallTile.HasWallDamagedSprite(layerMap["Walls"], mapPos);
                if (hasWallDamagedSprite)
                {
                    calcMap[mapPos.x][mapPos.y] = MapInfo.Floor;
                    FieldOfViewHasChanged = true;
                    return true;
                }
            }
        }

        return false;
    }

    public bool FieldOfViewHasChanged { get; set; }

    public int GetDamageFromTile(int x, int y)
    {
        return damageMap[x][y];
    }
    public void RemoveWallAt(Vector3Int pos)
    {
        calcMap[pos.x][pos.y] = MapInfo.Floor;
        layerMap["Walls"].SetTile(pos, null);
    }

    
    public virtual void ResetFog()
    {
        layerMap["FogOfWar"].ClearAllTiles();
        layerMap["FogOfWar"].FloodFill(new Vector3Int(Width-1,Height-1,0), fogTile);
        FillMapArray(fogMap, true);
    }
    public virtual void RemoveAllFog()
    {
        layerMap["FogOfWar"].ClearAllTiles();
        FillMapArray(fogMap, false);
    }
    protected virtual void RemoveFogMechanically(int x, int y)
    {
        fogMap[x][y] = false;
    }
    
    protected virtual void RemoveFogVisually(int x, int y)
    {
        layerMap["FogOfWar"].SetTile(new Vector3Int(x,y,0), null);
    }

    public virtual bool IsInFogMechanically(Vector3Int position)
    {
        return fogMap[position.x][position.y];
    }
    
    public virtual bool IsInFogVisually(Vector3Int position)
    {
        return layerMap["FogOfWar"].HasTile(position);
    }

    public virtual void AddWall(Vector3Int pos, int wallIndex = 0)
    {
        if (Contains(pos.x, pos.y))
        {
            calcMap[pos.x][pos.y] = MapInfo.Wall;
            layerMap["Walls"].SetTile(pos, walls[wallIndex]);
        }
    }

    public void FillLayer(string layerName, int tileIndex = 0)
    {
        switch (layerName)
        {
            case "Walls":
                layerMap["Walls"].BoxFill(new Vector3Int(1, 1, 0), walls[tileIndex], 0, 0, Width, Height);
                FillMapArray(calcMap, MapInfo.Wall);
                break;
            case "Floors":
                layerMap["Floors"].BoxFill(new Vector3Int(1, 1, 0), floorTiles[tileIndex], 0, 0, Width, Height);
                FillMapArray(calcMap, MapInfo.Floor);
                break;
            case "FogOfWar":
                layerMap["FogOfWar"].BoxFill(new Vector3Int(1, 1, 0), fogTile, 0, 0, Width, Height);
                FillMapArray(fogMap, true);
                break;
        }        
    }

    public void ForTileInLine(Vector3Int start, Vector3Int end, Action<Vector3Int> posCallback)
    {
        raycaster.Bresenham(start, end, posCallback);
    }
    private void FillMapArray<T>(T[][] array, T value)
    {
        for (int x = 0; x < Width; x++)
        {
            for (int y = 0; y < Height; y++)
            {
                array[x][y] = value;
            }
        }
    }
    

    public void SetFloor(Vector3Int pos)
    {
        layerMap["Floors"].SetTile(pos, floorTiles[0]);
        calcMap[pos.x][pos.y] = MapInfo.Floor;
    }
    public virtual Vector3Int GetNeighborInDirection(Vector3Int position, Direction direction)
    {
        switch (direction)
        {
            case Direction.North:
                return position + Vector3Int.up;
            case Direction.East:
                return position + Vector3Int.right;
            case Direction.South:
                return position + Vector3Int.down;
            case Direction.West:
                return position + Vector3Int.left;
            case Direction.NorthEast:
                return position + Vector3Int.up + Vector3Int.right;
            case Direction.SouthWest:
                return position + Vector3Int.left + Vector3Int.down;
            case Direction.SouthEast:
                return position + Vector3Int.down + Vector3Int.right;
            case Direction.NorthWest:
                return position + Vector3Int.left + Vector3Int.up;
        }
        return Vector3Int.zero;
    }

    public Vector3 CellToWorld(Vector3Int position)
    {
        return layerMap["Floors"].GetCellCenterWorld(position);
    }

    private Color IntToColor(int regionIndex)
    {
        return ColorsAsList().ElementAt(regionIndex);
    }

    public Vector3Int WorldToCell(Vector3 pos)
    {
        return layerMap["Floors"].WorldToCell(pos);
    }
    
    private void SetTileColor(Vector3Int pos, Color color)
    {
        // Flag the tile, indicating that it can change colour.
        // By default it's set to "Lock Colour".
        layerMap["Floors"].SetTileFlags(pos, TileFlags.None);
        // Set the colour.
        layerMap["Floors"].SetColor(pos, color);
    }
    public void SetHighlightColor(Color color, int layer = 0)
    {
        foreach (var highlight in allHighlights[layer])
        {
            highlight.GetComponent<SpriteRenderer>().color = color;
        }
    }

    public void HighlightTile(Vector3Int mapPos, Color color, int layer = 0)
    {
        var hilite = Instantiate(blanktilePrefab, CellToWorld(mapPos), Quaternion.identity);
        hilite.GetComponent<SpriteRenderer>().color = color;
        allHighlights[layer].Add(hilite);
    }
    public void RemoveAllHighlights(int layer = 0)
    {
        foreach (var highlight in allHighlights[layer])
        {
            Destroy(highlight);
        }
        allHighlights[layer].Clear();
    }
    
    
    public IPlayerUnit GetUnitAt(Vector3Int mapPos)
    {
        return unitMap[mapPos.x][mapPos.y];
    }
    
    public IPlayerUnit GetUnitAt(int x, int y)
    {
        return unitMap[x][y];
    }

    public bool IsUnitOn(Vector3Int mapPos)
    {
        return unitMap[mapPos.x][mapPos.y] != null;
    }

    public bool IsUnitOn(int x, int y)
    {
        return unitMap[x][y] != null;
    }
    
    public bool IsFriendlyUnitOn(Unit unit, Vector3Int mapPos)
    {
        if (!IsUnitOn(mapPos)) return false;
        var unitAtPos = GetUnitAt(mapPos);
        return unitAtPos.Faction == unit.Faction;
    }
    public bool AddUnit(Unit unit, Vector3Int mapPos, bool doorsAreBlocking = true)
    {
        if (!CanBePlaced(unit, mapPos, doorsAreBlocking)) return false;
        
        foreach (var offset in unit.OccupiedTiles)
        {
            var offsetPos = mapPos + offset;
            unitMap[offsetPos.x][offsetPos.y] = unit;
        }

        return true;
    }
    
    public void RemoveUnit(Vector3Int mapPos)
    {
        if (IsUnitOn(mapPos))
        {
            var unit = GetUnitAt(mapPos);
            foreach (var offset in unit.OccupiedTiles)
            {
                var offsetPos = mapPos + offset;
                if (IsUnitOn(offsetPos))
                {
                    unitMap[offsetPos.x][offsetPos.y] = null;
                }
            }
        }
    }

    public bool MoveUnit(Unit unit, Vector3Int oldMapPos, Vector3Int newMapPos)
    {
        if (oldMapPos == newMapPos) return false;

        if (!AddUnit(unit, newMapPos, doorsAreBlocking: false)) return false;
        
        RemoveUnit(oldMapPos);
        
        return true;
    }


    public Vector3Int RandomPosition => new Vector3Int(Random.Range(0, Width), Random.Range(0, Height), 0);
    public List<Vector3Int> AllPositions => RectToList(0, 0, Width, Height).ToList();

    public virtual bool Contains(int x, int y)
    {
        return (x >= 0 && y >= 0) && (x < Width && y < Height);
    }
    public void GetNeighbors(List<Vector3Int> reachableNeighbors, Unit currentUnit, Vector3Int mapPos)
    {
        var neighbors = GetAllNeighborsForUnit(currentUnit, mapPos);
        foreach (var neighbor in neighbors)
        {
            if (IsNeighborReachable(currentUnit, mapPos, neighbor))
            {
                reachableNeighbors.Add(neighbor);
            }
        }
    }

    public List<Vector3Int> GetAllNeighborsForUnit(Unit currentUnit, Vector3Int mapPos)
    {
        List<Vector3Int> neighbors = new List<Vector3Int>()
        {
            mapPos + Vector3Int.up,
            mapPos + Vector3Int.right,
            mapPos + Vector3Int.down,
            mapPos + Vector3Int.left,
        };
        if (currentUnit.Rules.CanMoveDiagonally && Options.AllowDiagonalMovement)
        {
            neighbors.AddRange(new[]
            {
                mapPos + Vector3Int.up + Vector3Int.right,
                mapPos + Vector3Int.up + Vector3Int.left,
                mapPos + Vector3Int.down + Vector3Int.right,
                mapPos + Vector3Int.down + Vector3Int.left
            });
        }

        return neighbors;
    }
    
    public bool AreNeighbors(Vector3Int posOne, Vector3Int posTwo, bool includeDiagonals = false)
    {
        if (includeDiagonals && AreDiagonalNeighbors(posOne, posTwo))
        {
            return true;
        }

        var neighbors = AreCardinalNeighbors(posOne, posTwo);
        return neighbors;
    }

    public static bool AreDiagonalNeighbors(Vector3Int posOne, Vector3Int posTwo)
    {
        return Mathf.Abs(posOne.y - posTwo.y) == 1 && Mathf.Abs(posOne.x - posTwo.x) == 1;
    }

    public static bool AreCardinalNeighbors(Vector3Int posOne, Vector3Int posTwo)
    {
        return posOne.x == posTwo.x && Mathf.Abs(posOne.y - posTwo.y) == 1
               || posOne.y == posTwo.y && Mathf.Abs(posOne.x - posTwo.x) == 1;
    }

    public void RemoveUnit(Unit unit)
    {
        var pos = WorldToCell(unit.transform.position);
        if (unitMap[pos.x][pos.y] == unit)
        {
            RemoveUnit(pos);
        }
    }

    public bool IsOtherFactionOn(IPlayerUnit unit, Vector3Int pos)
    {
        return IsOtherFactionOn((Unit) unit, pos);
    }

    public bool CanBePlaced(IPlayerUnit unit, Vector3Int mapPos, bool doorsAreBlocking = true,
        bool friendlyUnitsAreBlocking = true)
    {
        return CanBePlaced((Unit) unit, mapPos, doorsAreBlocking, friendlyUnitsAreBlocking);
    }

    public bool IsOtherUnitOn(IPlayerUnit unit, Vector3Int mapPos)
    {
        return IsOtherUnitOn((Unit) unit, mapPos);
    }
    public bool ExplosionReaches(Vector3Int sourcePos, Vector3Int affectedPos, int damageToTile)
    {
        raycaster.ObservingUnit = null;
        raycaster.UnitsAreBlocking = false;

        var hasLos = raycaster.LineCast(sourcePos.x, sourcePos.y, affectedPos.x, affectedPos.y);

        AddDamageToTile(hasLos ? affectedPos : raycaster.FirstObstaclePosition, damageToTile);

        return hasLos;
    }

    public bool IsOtherFactionOn(Unit unit, Vector3Int pos)
    {
        return IsOtherFactionOn(unit.Faction, pos);
    }

    public bool IsOtherFactionOn(RuleFaction faction, Vector3Int mapPosition)
    {
        if (!IsUnitOn(mapPosition))
        {
            return false;
        }

        var otherUnit = GetUnitAt(mapPosition);
        return faction != otherUnit.Faction;
    }


    public GameObject GetUnitMarker()
    {
        return unitSelectedMarker;
    }
    
    public void HideUnitMarker()
    {
        unitSelectedMarker.GetComponent<SpriteRenderer>().enabled = false;
    }
    
    public void MarkTile(Vector3Int mapPos)
    {
        var marker = Instantiate(selectionRectPrefab, CellToWorld(mapPos), Quaternion.identity);
        allTileMarker.Add(marker);
    }

    public void RemoveAllTileMarker()
    {
        allTileMarker.ForEach(Destroy);
        allTileMarker.Clear();
    }

    public bool IsStandingOnDoor(Unit unit, bool openDoor = false)
    {
        bool thereIsADoor = false;
        foreach (var offsetOne in unit.OccupiedTiles)
        {
            var tileOne = unit.Cell + offsetOne;
            if (IsDoorAt(tileOne))
            {
                if (openDoor)
                {
                    RemoveDoor(tileOne);
                }

                thereIsADoor = true;
            }
        }
        return thereIsADoor;
    }

    public bool CanBePlaced(Unit unit, Vector3Int mapPos, bool doorsAreBlocking = true, bool friendlyUnitsAreBlocking = true)
    {
        foreach (var offset in unit.OccupiedTiles)
        {
            var offsetPos = mapPos + offset;
            if (!Contains(offsetPos.x, offsetPos.y))
            {
                return false;
            }

            if (IsWallAt(offsetPos.x, offsetPos.y))
            {
                return false;
            }
            
            if (doorsAreBlocking && IsDoorAt(offsetPos))
            {
                return false;
            }

            if (IsUnitOn(offsetPos))
            {
                var unitAtPosition = GetUnitAt(offsetPos);
                var isFriendlyUnit = unitAtPosition.Faction == unit.Faction;
                if (friendlyUnitsAreBlocking) return false;
                if (!isFriendlyUnit) return false;
            }
        }
        return true;
    }
    
    public bool IsOtherUnitOn(Unit unit, int x, int y)
    {
        if (!IsUnitOn(x,y))
        {
            return false;
        }

        return GetUnitAt(x,y) != unit;
    }
    public bool IsOtherUnitOn(Unit unit, Vector3Int mapPos)
    {
        if (!IsUnitOn(mapPos))
        {
            return false;
        }

        return GetUnitAt(mapPos) != unit;
    }
    
    public static IEnumerable<Vector3Int> RectToList(int x, int y, int width, int height) {
        for (int xIndex = 0; xIndex < width; xIndex++)
        {
            for (int yIndex = 0; yIndex < height; yIndex++)
            {
                yield return new Vector3Int(x + xIndex, y + yIndex, 0);
            }
        }
    }
    
    public static IEnumerable<Color> ColorsAsList() {
        for (int zIndex = 0; zIndex < 255; zIndex += 10)
        {
            for (int xIndex = 0; xIndex < 255; xIndex += 10)
            {
                for (int yIndex = 0; yIndex < 255; yIndex += 10)
                {
                    yield return new Color(xIndex/255f,yIndex/255f, zIndex/255f);
                }
            }
        }
    }
    
    
    public bool IsNeighborReachable(IPlayerUnit unit, Vector3Int oldPos, Vector3Int newPos)
    {
        // handle diagonal cases
        if (!Contains(newPos.x, newPos.y))
        {
            return false;
        }
        if (oldPos.x != newPos.x && oldPos.y != newPos.y)
        {
            if (!Options.AllowDiagonalMovement) return false;
            
            var dx = oldPos.x - newPos.x;
            var dy = oldPos.y - newPos.y;
            // one is to the top right of two
            var vertTile = new Vector3Int(newPos.x + dx, newPos.y, 0);
            var horiTile = new Vector3Int(newPos.x, newPos.y + dy, 0);

            if (IsWallAt(vertTile) || IsWallAt(horiTile)) // no diagonal walking around and through walls
            {
                return false;
            }
        }
        
        return CanBePlaced(unit, newPos, false, false);
    }

    public bool IsNeighborVisible(int xOld, int yOld, int xNew, int yNew)
    {
        if (!Contains(xOld, yOld) || !Contains(xNew, yNew))
        {
            return false;
        }
        if (IsWallAt(xOld, yOld) || IsWallAt(xNew, yNew) || IsDoorAt(xOld, yOld) || IsDoorAt(xNew, yNew))
        {
            return false;
        }

        if (xOld != xNew && yOld != yNew)
        {
            var dx = xOld - xNew;
            var dy = yOld - yNew;
            // one is to the top right of two
            if (IsWallAt(xNew + dx, yNew) || IsWallAt(xNew, yNew + dy)) // no diagonal looking around and through walls
            {
                return false;
            }
        }
        return true;
    }

    public HashSet<Vector3Int> GetFOV(Unit unit, Vector3Int position, int radius, bool removeFogVisually = true, bool removeFogMechanically = true, bool unitsAreBlocking = false)
    {
        raycaster.ObservingUnit = unit;
        raycaster.UnitsAreBlocking = unitsAreBlocking;
        var fieldOfView = new HashSet<Vector3Int>();
        radius++;
        int i, j;
        for (i = -radius; i <= radius; i++)
        {
            for (j = -radius; j <= radius; j++)
            {
                if (i * i + j * j < radius * radius)
                {
                    if (raycaster.LineCast(position.x, position.y, position.x + i, position.y + j))
                    {   
                        var checkedMapPos = new Vector3Int(position.x + i, position.y + j, 0);
                        if (removeFogVisually)
                        {
                            RemoveFogVisually(checkedMapPos.x, checkedMapPos.y);
                        }

                        if (removeFogMechanically)
                        {
                            RemoveFogMechanically(checkedMapPos.x, checkedMapPos.y);
                        }

                        fieldOfView.Add(checkedMapPos);
                    }
                }
            }
        }
        return fieldOfView;
    }

    public bool IsLineOfFireBetween(Unit attacker, Vector3Int source, Vector3Int target)
    {
        if (!Options.AllowDiagonalAdjacentAttacks && AreDiagonalNeighbors(source, target))
        {
            return false;
        }

        raycaster.ObservingUnit = attacker;
        raycaster.UnitsAreBlocking = true;
        return raycaster.LineCast(source.x, source.y, target.x, target.y);
    }
    
    public bool IsLineOfSightBetween(Vector3Int source, Vector3Int target)
    {
        raycaster.ObservingUnit = null;
        raycaster.UnitsAreBlocking = false;
        return raycaster.LineCast(source.x, source.y, target.x, target.y);
    }

    public List<List<Vector3Int>> GetSpawnPoints()
    {
        Dictionary<Vector3Int, int> spawnPoints = new Dictionary<Vector3Int, int>();
        foreach (var pos in RectToList(0,0,Width,Height))
        {
            if (!IsFloorAt(pos)) continue;
            var floorTile = layerMap["Floors"].GetTile<Tile>(pos);
            if (floorTile == null) continue;
            
            int faction = spawnFloorMarkerTiles.ToList().IndexOf(floorTile);
                
            if (faction > -1)
            {
                spawnPoints[pos] = faction;
            }
        }
        List<List<Vector3Int>> results = new List<List<Vector3Int>>();
        int maxFactionIndex = spawnPoints.Values.Max();
        for (int i = 0; i <= maxFactionIndex; i++)
        {
            var factionPoints = spawnPoints.Where(kvp => kvp.Value == i).Select(kvp => kvp.Key);
            results.Add(new List<Vector3Int>(factionPoints));
        }

        return results;
    }
    
    public HashSet<Vector3Int> GetStraightLine(Vector3Int unitPos, Direction rayDirection, int maxDistance)
    {
        HashSet<Vector3Int> linePositions = new HashSet<Vector3Int>();
        var prevPos = unitPos;
        Vector3Int rayDirVector = rayDirection.ToVector();
        for (int i = 0; i < maxDistance; i++)
        {
            var nextPos = prevPos + rayDirVector;
            if (!IsNeighborVisible(prevPos.x, prevPos.y, nextPos.x, nextPos.y))
            {
                return linePositions;
            }

            linePositions.Add(nextPos);
            prevPos = nextPos;
        }

        return linePositions;
    }

    public bool ContainsWorldPos(Vector3 pos)
    {
        var worldMin = transform.TransformPoint(layerMap["Floors"].localBounds.min);
        var worldMax = transform.TransformPoint(layerMap["Floors"].localBounds.max);
        if (pos.x >= worldMin.x && pos.x <= worldMax.x && pos.y >= worldMin.y && pos.y <= worldMax.y)
            return true;
        return false;
    }

    public string GetPosDebugInfo(Vector3Int pos)
    {
        var wallHere = IsWallAt(pos) ? "W" : "";
        var doorHere = IsDoorAt(pos) ? "D" : "";
        var unitHere = IsUnitOn(pos) ? "U" : "";
        var floorHere = IsFloorAt(pos) ? "" : "X";
        var fogHere = IsInFogMechanically(pos) ? "F" : "";
        return wallHere + doorHere + unitHere + floorHere + fogHere;
    }

    public void SetTextAt(Vector3Int mapPos, string text)
    {
        Text cellLabel = Instantiate(cellLabelPrefab, CellToWorld(mapPos), Quaternion.identity).GetComponent<Text>();
        cellLabel.transform.SetParent(worldCanvas);
        cellLabels.Add(cellLabel);
        cellLabel.text = text;
    }

    public void RemoveAllCellLabels()
    {
        foreach (var cellLabel in cellLabels)
        {
            Destroy(cellLabel);
        }
        cellLabels.Clear();
    }

    public void FromPrefab()
    {
        for (int x = 0; x < Width; x++)
        {
            for (int y = 0; y < Height; y++)
            {
                Vector3Int pos = new Vector3Int(x,y,0);
                if (layerMap["Doors"].HasTile(pos))
                {
                    calcMap[x][y] = MapInfo.Door;
                }
                else if (layerMap["Walls"].HasTile(pos))
                {
                    calcMap[x][y] = MapInfo.Wall;
                }
                else
                {
                    calcMap[x][y] = MapInfo.Floor;
                }
            }
        }
    }
    
	
	// Implementing Mid-Point Circle 
	// Drawing Algorithm 

    public bool AreWorldPositionsOnSameTile(Vector3 positionOne, Vector3 positionTwo)
    {
        if (!ContainsWorldPos(positionOne))
        {
            return !ContainsWorldPos(positionTwo);
        }

        if (!ContainsWorldPos(positionTwo))
        {
            return !ContainsWorldPos(positionOne);
        }

        // both inside
        var mapOne = WorldToCell(positionOne);
        var mapTwo = WorldToCell(positionTwo);

        return mapOne == mapTwo;
    }

   
}

public enum MapInfo
{
    Floor,
    Door,
    Wall
}


public static class DirectionExtension
{
    public static Vector3Int ToVector(this Direction direction)
    {
        switch (direction)
        {
            case Direction.North:
                return Vector3Int.up;
            case Direction.NorthWest:
                return Vector3Int.up + Vector3Int.left;
            case Direction.West:
                return Vector3Int.left;
            case Direction.SouthWest:
                return Vector3Int.down + Vector3Int.left;
            case Direction.South:
                return Vector3Int.down;
            case Direction.SouthEast:
                return Vector3Int.down + Vector3Int.right;
            case Direction.East:
                return Vector3Int.right;
            case Direction.NorthEast:
                return Vector3Int.up + Vector3Int.right;
        }
        return Vector3Int.zero;
    }

}

public static class Vector3IntExtension
{
    public static Direction ToDirection(this Vector3Int v)
    {
        if (v == Vector3Int.up) return Direction.North;
        if (v == Vector3Int.down) return Direction.South;
        if (v == Vector3Int.right) return Direction.East;
        if (v == Vector3Int.left) return Direction.West;
        
        if (v == Vector3Int.up + Vector3Int.right) return Direction.NorthEast;
        if (v == Vector3Int.down + Vector3Int.right) return Direction.SouthEast;
        if (v == Vector3Int.left + Vector3Int.up) return Direction.NorthWest;
        if (v == Vector3Int.left + Vector3Int.down) return Direction.SouthWest;
        
        return Direction.None;
    }
}

public static class Vector2Extension
{

    public static Vector2 Rotate(this Vector2 v, float degrees)
    {
        float sin = Mathf.Sin(degrees * Mathf.Deg2Rad);
        float cos = Mathf.Cos(degrees * Mathf.Deg2Rad);

        float tx = v.x;
        float ty = v.y;
        v.x = (cos * tx) - (sin * ty);
        v.y = (sin * tx) + (cos * ty);
        return v;
    }
}
