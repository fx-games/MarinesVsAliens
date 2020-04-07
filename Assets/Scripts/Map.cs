using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Security.Cryptography;
using DefaultNamespace;
using GridPathFinder.Map;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.Tilemaps;
using UnityEngine.UI;
using UnityEngine.Video;
using static Map;
using static Mod;
using Color = UnityEngine.Color;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

public interface IMapGenerator
{
    void Generate(Map map);
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
    public Tile[] spawnFloorMarkerTiles;

    protected Dictionary<string, Tilemap> layerMap = new Dictionary<string, Tilemap>();
    protected Unit[][] unitMap;
    protected MapInfo[][] calcMap;
    protected bool[][] fogMap;
    
    
    private Camera gameCamera;
    public GameObject blanktilePrefab;
    private List<GameObject>[] allHighlights = new List<GameObject>[4]{ new List<GameObject>(), new List<GameObject>(), new List<GameObject>(), new List<GameObject>()};
    public Vector3Int CurrentCursorPosition { get; private set; }
    private HashSet<Vector3Int> visibleTiles = new HashSet<Vector3Int>();
    private Mod mod;
    private IMapGenerator generator;
    private Vector3Int lastCursorPosition;
    public GameObject cellLabelPrefab;
    private HashSet<Text> cellLabels = new HashSet<Text>();
    private Transform worldCanvas;

    // Start is called before the first frame update
    void Awake()
    {
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
        for (int i = 0; i < Width; i++)
        {
            unitMap[i] = new Unit[Height];
            calcMap[i] = new MapInfo[Height];
            fogMap[i] = new bool[Height];
        }
        gameCamera = Camera.main;
        if (gameCamera == null)
        {
            throw new Exception("Game needs camera.");
        }
    }
    
    
    public void DebugAction()
    {
        ((CavernGenerator) generator).MakeCaverns(0);
    }
    
    public void DebugActionTwo()
    {
        ((CavernGenerator) generator).MakeCaverns(1);
    }

    public void FromRules(RuleMap mapRules)
    {
        if (mapRules.Generator == 1)
        {
            generator = new MapFromScript(mod, mapRules);
        }
        else if (mapRules.Generator == 2)
        {
            generator = new DungeonGenerator(25,25);
        }
        else if (mapRules.Generator == 3)
        {
            generator = new CavernGenerator(25,25);
        }
        generator.Generate(this);        
    }

    public virtual void Init(int width, int height)
    {
        foreach (var kvp in layerMap)
        {
            var layer = kvp.Value;
            layer.ClearAllTiles();
            layer.size = new Vector3Int(width,height,1);
        }

        unitMap = new Unit[Width][];
        calcMap = new MapInfo[Width][];
        fogMap = new bool[Width][];
        for (int i = 0; i < Width; i++)
        {
            unitMap[i] = new Unit[Height];
            calcMap[i] = new MapInfo[Height];
            fogMap[i] = new bool[Height];
        }
        FillLayer("Floors");
        //FillLayer("FogOfWar");
    }   
    private void Start()
    {
        
    }
    
    
    // Update is called once per frame
    void Update()
    {
        Vector3 pos = gameCamera.ScreenToWorldPoint(Input.mousePosition);
        var mapPos = WorldToCell(pos);
        if (Contains(mapPos.x, mapPos.y) && mapPos != lastCursorPosition)
        {
            CurrentCursorPosition = mapPos;
            selectionRect.transform.position = CellToWorld(mapPos);
            lastCursorPosition = mapPos;
        }
    }
    
    public void RemoveDoor(Unit unit, Vector3Int doorPos)
    {
        Debug.Log("RemoveDoor() FIRED: " + unit + " / " + doorPos);
        if (IsDoorAt(doorPos))
        {
            // door is above
            layerMap["Doors"].SetTile(doorPos, null);
            calcMap[doorPos.x][doorPos.y] = MapInfo.Floor;
        }
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
        return calcMap[pos.x][pos.y] == MapInfo.Wall;
    }

    public bool IsFloorAt(int x, int y)
    {
        return calcMap[x][y] == MapInfo.Floor;;
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
    public void CreateDoor(int x, int y)
    {
        var pos = new Vector3Int(x,y,0);
        layerMap["Doors"].SetTile(pos, doors[0]);
        calcMap[pos.x][pos.y] = MapInfo.Door;
        RemoveWallAt(pos); // no walls at door tiles
    }


    public void RemoveWallAt(Vector3Int pos)
    {
        calcMap[pos.x][pos.y] = MapInfo.Floor;
        layerMap["Walls"].SetTile(pos, null);
    }

    
    public virtual void ResetFog()
    {
        layerMap["FogOfWar"].ClearAllTiles();
        layerMap["FogOfWar"].FloodFill(new Vector3Int(Width-1,Height-1,0), floorTiles[0]);
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

    public void FillLayer(string layerName)
    {
        switch (layerName)
        {
            case "Walls":
                layerMap["Walls"].BoxFill(new Vector3Int(1, 1, 0), walls[0], 0, 0, Width, Height);
                FillMapArray(calcMap, MapInfo.Wall);
                break;
            case "Floors":
                layerMap["Floors"].BoxFill(new Vector3Int(1, 1, 0), floorTiles[0], 0, 0, Width, Height);
                FillMapArray(calcMap, MapInfo.Floor);
                break;
            case "FogOfWar":
                layerMap["FogOfWar"].BoxFill(new Vector3Int(1, 1, 0), floorTiles[0], 0, 0, Width, Height);
                FillMapArray(fogMap, true);
                break;
        }

        
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

    public bool IsUnitOn(Vector3Int mapPos)
    {
        return unitMap[mapPos.x][mapPos.y] != null;
    }

    public bool IsUnitOn(int x, int y)
    {
        return unitMap[x][y] != null;
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

    public List<Vector3Int> AreaOfEffect(Vector3Int unitPos, Weapon weapon, Vector3Int mapPos)
    {
        var rules = weapon.Rules;
        var radius = rules.Radius;
        HashSet<Vector3Int> affectedTiles = new HashSet<Vector3Int>(){mapPos};
        if (rules.DamageLineOfFire)
        {
            affectedTiles.UnionWith(GetStraightLine(unitPos, mapPos));
            affectedTiles.Remove(unitPos);
            //affectedTiles.RemoveWhere(pos => AreNeighbors(unitPos, pos, true));
        }
        
        if (radius > 0)
        {
            for (int i = mapPos.x - radius; i <= mapPos.x + radius; i++)
            {
                for (int j = mapPos.y - radius; j <= mapPos.y + radius; j++)
                {
                    var nextPos = new Vector3Int(i, j, 0);
                    var euclideanDistance = (mapPos - nextPos).magnitude;
                    if (Mathf.Floor(euclideanDistance) <= radius)
                    {
                        affectedTiles.Add(nextPos);
                    }
                }
            }
        }

        return affectedTiles.ToList();
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

    public bool AoEAllowed(Unit attacker, Weapon weapon, Vector3Int mapPos, Vector3Int affectedPos)
    {
        if (mapPos == affectedPos) return true;
        if (weapon.HasAreaOfEffect)
        {
            // affectedPos may be one of the aoe tiles
            var hasLos = LineCast(mapPos.x, mapPos.y, affectedPos.x, affectedPos.y);
            return hasLos;
        }
        else
        {
            // affectedPos is the tile the user clicked on
            // since weapon preview should already have handled this, we are done here
        }

        return true;
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
                    RemoveDoor(unit, tileOne);
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
    
    public HashSet<Vector3Int> GetStraightLine(Vector3Int posOne, Vector3Int posTwo)
    {
        HashSet<Vector3Int> linePositions = new HashSet<Vector3Int>();
                
        Vector3Int rayDirVector = posOne.x < posTwo.x ? Vector3Int.right : posOne.x > posTwo.x ? Vector3Int.left : Vector3Int.zero;
        rayDirVector += posOne.y < posTwo.y ? Vector3Int.up : posOne.y > posTwo.y ? Vector3Int.down : Vector3Int.zero;
        var prevPos = posOne;
        var nextPos = prevPos + rayDirVector;
        while (nextPos != posTwo)
        {
            if (!IsNeighborVisible(prevPos.x, prevPos.y, nextPos.x, nextPos.y))
            {
                return linePositions;
            }

            linePositions.Add(nextPos);
            prevPos = nextPos;
            nextPos = prevPos + rayDirVector;
        }

        return linePositions;
    }

    public HashSet<Vector3Int> GetFOV(Vector3Int position, int radius, bool removeFogVisually = true, bool removeFogMechanically = true, bool unitsAreBlocking = false)
    {
        var fieldOfView = new HashSet<Vector3Int>();
        radius++;
        int i, j;
        for (i = -radius; i <= radius; i++)
        {
            for (j = -radius; j <= radius; j++)
            {
                if (i * i + j * j < radius * radius)
                {
                    if (LineCast(position.x, position.y, position.x + i, position.y + j, removeFogVisually, removeFogMechanically, unitsAreBlocking))
                    {
                        fieldOfView.Add(new Vector3Int(position.x + i, position.y + j, 0));
                    }
                }
            }
        }
        return fieldOfView;
    }

    public bool IsLineOfFireBetween(Vector3Int source, Vector3Int target)
    {
        if (!Options.AllowDiagonalAdjacentAttacks && AreDiagonalNeighbors(source, target))
        {
            return false;
        }
        return LineCast(source.x, source.y, target.x, target.y, false, true);
    }
    
    public bool IsLineOfSightBetween(Vector3Int source, Vector3Int target)
    {
        return LineCast(source.x, source.y, target.x, target.y);
    }
    
    private bool LineCast(int x0, int y0, int x1, int y1, bool removeFogVisually = false, bool removeFogMechanically = false, bool unitsAreBlocking = false)
    {
        int sx, sy, xnext, ynext, dx, dy, prevX, prevY;
        float dist;

        dx = x1 - x0;
        dy = y1 - y0;

        //determine which quadrant to we're calculating: we climb in these two directions
        sx = (x0 < x1) ? 1 : -1;
        sy = (y0 < y1) ? 1 : -1;

        xnext = prevX = x0;
        ynext = prevY = y0;
    
        //calculate length of line to cast (distance from start to final tile)
        dist = Mathf.Sqrt(dx * dx + dy * dy);

        while (xnext != x1 || ynext != y1) //essentially casting a ray of length radius: (radius^3)
        {
            if (!IsNeighborVisible(prevX, prevY, xnext, ynext))
            {
                //tag_memorised(xnext, ynext); // make a note of the wall
                return false;
            }
            
            if (unitsAreBlocking && !(xnext == x0 && ynext == y0) && IsUnitOn(xnext, ynext))
            {
                return false;
            }
            
            prevX = xnext;
            prevY = ynext;
            // Line-to-point distance formula < 0.5 
            // decide on the direction for the next step; horiz, verti, diag
            if (Mathf.Abs(dy * (xnext - x0 + sx) - dx * (ynext - y0)) / dist < 0.5f)
            {
                xnext += sx;
            }
            else if (Mathf.Abs(dy * (xnext - x0) - dx * (ynext - y0 + sy)) / dist < 0.5f)
            {
                ynext += sy;
            }
            else
            {
                xnext += sx;
                ynext += sy;
            }
        }
        if (!IsNeighborVisible(prevX, prevY,xnext, ynext))
        {
            //tag_memorised(xnext, ynext); // make a note of the wall
            return false;
        }

        if (removeFogVisually)
        {
            RemoveFogVisually(xnext, ynext);
        }

        if (removeFogMechanically)
        {
            RemoveFogMechanically(xnext, ynext);
        }
        return true;
    }

    public List<List<Vector3Int>> GetSpawnPoints()
    {
        Dictionary<Vector3Int, int> spawnPoints = new Dictionary<Vector3Int, int>();
        foreach (var pos in RectToList(0,0,Width,Height))
        {
            if (!IsFloorAt(pos)) continue;
            int faction = spawnFloorMarkerTiles.ToList().IndexOf(layerMap["Floors"].GetTile<Tile>(pos));
                
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
    
    public static Point ToPoint(this Vector3Int v)
    {
        return new Point(v.x, v.y);
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
