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
using UnityEngine.Video;
using static Map;
using static Mod;
using Color = UnityEngine.Color;
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
    protected Unit[,] unitMap;
    private Camera gameCamera;
    public GameObject blanktilePrefab;
    private List<GameObject>[] allHighlights = new List<GameObject>[4]{ new List<GameObject>(), new List<GameObject>(), new List<GameObject>(), new List<GameObject>()};
    public Vector3Int CurrentCursorPosition { get; private set; }
    private HashSet<Vector3Int> visibleTiles = new HashSet<Vector3Int>();
    private Mod mod;
    private IMapGenerator generator;
    private Vector3Int lastCursorPosition;
    private bool spectatorMode = false;

    // Start is called before the first frame update
    void Awake()
    {
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
        unitMap = new Unit[Width, Height];

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

        Width = width;
        Height = height;
        unitMap = new Unit[Width, Height];
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
    
    public void RemoveDoor(Unit unit, Vector3Int mapPos)
    {
        foreach (var offset in unit.OccupiedTiles)
        {
            var offsetPos = mapPos + offset;
            
            if (IsDoorAt(offsetPos))
            {
                // door is above
                layerMap["Doors"].SetTile(offsetPos, null);
            }
        }
    }

    public bool IsDoorAt(Vector3Int offsetPos)
    {
        return layerMap["Doors"].HasTile(offsetPos);
    }

    public bool IsWallAt(int x, int y)
    {
        return IsWallAt(new Vector3Int(x,y,0));
    }

    public bool IsWallAt(Vector3Int pos)
    {
        return layerMap["Walls"].HasTile(pos);
    }

    public bool IsFloorAt(int x, int y)
    {
        return IsFloorAt(new Vector3Int(x, y, 0));
    }
    private bool IsFloorAt(Vector3Int pos)
    {
        return layerMap["Floors"].HasTile(pos);
    }
    public void RemoveDoor(Vector3Int pos)
    {
        layerMap["Doors"].SetTile(pos, null);
    }

    public void CreateDoor(int x, int y)
    {
        var pos = new Vector3Int(x,y,0);
        layerMap["Doors"].SetTile(pos, doors[0]);
        RemoveWallAt(pos); // no walls at door tiles
    }


    public void RemoveWallAt(Vector3Int pos)
    {
        layerMap["Walls"].SetTile(pos, null);
    }

    public IPlayerUnit GetUnitAt(Vector3Int mapPos)
    {
        return unitMap[mapPos.x, mapPos.y];
    }

    public bool IsUnitOn(Vector3Int mapPos)
    {
        return unitMap[mapPos.x, mapPos.y] != null;
    }


    public virtual void ResetFog()
    {
        layerMap["FogOfWar"].ClearAllTiles();
        layerMap["FogOfWar"].FloodFill(new Vector3Int(Width-1,Height-1,0), floorTiles[0]);
    }
    public virtual void RemoveAllFog()
    {
        layerMap["FogOfWar"].ClearAllTiles();
    }
    protected virtual void RemoveFog(Vector3Int position)
    {
        layerMap["FogOfWar"].SetTile(position, null);
    }

    public virtual bool IsInFog(Vector3Int position)
    {
        return layerMap["FogOfWar"].HasTile(position);
    }

    public virtual void AddWall(Vector3Int pos, int wallIndex = 0)
    {
        if (Contains(pos.x, pos.y)) layerMap["Walls"].SetTile(pos, walls[wallIndex]);
    }

    public void FillLayer(string layerName)
    {
        switch (layerName)
        {
            case "Walls":
                layerMap["Walls"].BoxFill(new Vector3Int(1, 1, 0), walls[0], 0, 0, Width, Height);
                break;
            case "Floors":
                layerMap["Floors"].BoxFill(new Vector3Int(1, 1, 0), floorTiles[0], 0, 0, Width, Height);
                break;
            case "FogOfWar":
                layerMap["FogOfWar"].BoxFill(new Vector3Int(1, 1, 0), floorTiles[0], 0, 0, Width, Height);
                break;
        }
        
    }

    public void SetFloor(Vector3Int pos)
    {
        layerMap["Floors"].SetTile(pos, floorTiles[0]);
    }
    public virtual Vector3Int GetNeighbour(Vector3Int position, Direction direction)
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
    public bool AddUnit(Unit unit, Vector3Int mapPos)
    {
        if (!CanBePlaced(unit, mapPos)) return false;
        
        foreach (var offset in unit.OccupiedTiles)
        {
            var offsetPos = mapPos + offset;
            unitMap[offsetPos.x, offsetPos.y] = unit;
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
                    unitMap[offsetPos.x, offsetPos.y] = null;
                }
            }
        }
    }

    public bool MoveUnit(Unit unit, Vector3Int oldMapPos, Vector3Int newMapPos)
    {
        if (AddUnit(unit, newMapPos))
        {
            RemoveUnit(oldMapPos);
            return true;
        }

        return false;
    }


    public Vector3Int RandomPosition => new Vector3Int(Random.Range(0, Width), Random.Range(0, Height), 0);
    public List<Vector3Int> AllPositions => RectToList(0, 0, Width, Height).ToList();

    public void EnableSpectatorMode()
    {
        spectatorMode = true;
        //layerMap["FogOfWar"].GetComponent<TilemapRenderer>().enabled = false;
    }

    public virtual bool Contains(int x, int y)
    {
        return (x >= 0 && y >= 0) && (x < Width && y < Height);
    }
    public List<Point> GetNeighbors(Unit currentUnit, Point position)
    {
        Vector3Int mapPos = new Vector3Int(position.X, position.Y, 0);
        List<Vector3Int> reachableNeighbors = new List<Vector3Int>();
        
        var neighbors = GetAllNeighborsForUnit(currentUnit, mapPos);
        foreach (var neighbor in neighbors)
        {
            if (IsNeighborReachable(currentUnit, mapPos, neighbor))
            {
                reachableNeighbors.Add(neighbor);
            }
        }

        return reachableNeighbors.ConvertAll(elem => new Point(elem.x, elem.y));
    }

    private static List<Vector3Int> GetAllNeighborsForUnit(Unit currentUnit, Vector3Int mapPos)
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
        if (includeDiagonals)
        {
            var areNeighbors = Mathf.Abs(posOne.y - posTwo.y) == 1 && Mathf.Abs(posOne.x - posTwo.x) == 1;
            if (areNeighbors)
                return true;
        }
        return posOne.x == posTwo.x && Mathf.Abs(posOne.y - posTwo.y) == 1
               || posOne.y == posTwo.y && Mathf.Abs(posOne.x - posTwo.x) == 1;
    }

    public void RemoveUnit(Unit unit)
    {
        var pos = WorldToCell(unit.transform.position);
        if (unitMap[pos.x, pos.y] == unit)
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
            affectedTiles.RemoveWhere(pos => AreNeighbors(unitPos, pos, true));
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

    public bool CanBePlaced(Unit unit, Vector3Int mapPos, bool doorsAreBlocking = true,
        bool friendlyUnitsAreBlocking = true)
    {
        foreach (var offset in unit.OccupiedTiles)
        {
            var offsetPos = mapPos + offset;
            if (!Contains(offsetPos.x, offsetPos.y) 
                || IsWallAt(offsetPos.x,offsetPos.y))
            {
                return false;
            }

            if (friendlyUnitsAreBlocking)
            {
                if (IsOtherUnitOn(unit, offsetPos))
                    return false;
            }
            else if (IsOtherFactionOn(unit, offsetPos))
            {
                return false;
            }
            
            if (doorsAreBlocking && IsDoorAt(offsetPos))
                return false;
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

    public bool IsNeighborVisible(Vector3Int oldPos, Vector3Int newPos)
    {
        if (!Contains(oldPos.x, oldPos.y) || !Contains(newPos.x, newPos.y))
        {
            return false;
        }
        if (IsWallAt(oldPos) || IsWallAt(newPos) ||  IsDoorAt(oldPos) || IsDoorAt(newPos))
        {
            return false;
        }

        if (oldPos.x != newPos.x && oldPos.y != newPos.y)
        {
            var dx = oldPos.x - newPos.x;
            var dy = oldPos.y - newPos.y;
            // one is to the top right of two
            var vertTile = new Vector3Int(newPos.x + dx, newPos.y, 0);
            var horiTile = new Vector3Int(newPos.x, newPos.y + dy, 0);

            if (IsWallAt(vertTile) || IsWallAt(horiTile)) // no diagonal looking around and through walls
            {
                return false;
            }
        }
        return true;
    }


    public void UpdateFOV(Vector3Int position, int radius)
    {
        //Debug.Log("UpdateFoV: " + position);
        radius++;
        int i, j;
        for (i = -radius; i <= radius; i++)
            for (j = -radius; j <= radius; j++)
                if (i * i + j * j < radius * radius)
                    LineCast(position.x, position.y, position.x + i, position.y + j, true);
    }
    public HashSet<Vector3Int> GetStraightLine(Vector3Int unitPos, Direction rayDirection, int maxDistance)
    {
        HashSet<Vector3Int> linePositions = new HashSet<Vector3Int>();
        var prevPos = unitPos;
        Vector3Int rayDirVector = rayDirection.ToVector();
        for (int i = 0; i < maxDistance; i++)
        {
            var nextPos = prevPos + rayDirVector;
            if (!IsNeighborVisible(prevPos, nextPos))
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
            if (!IsNeighborVisible(prevPos, nextPos))
            {
                return linePositions;
            }

            linePositions.Add(nextPos);
            prevPos = nextPos;
            nextPos = prevPos + rayDirVector;
        }

        return linePositions;
    }

    public HashSet<Vector3Int> GetFOV(Vector3Int position, int radius, bool unitsAreBlocking = false)
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
                    if (LineCast(position.x, position.y, position.x + i, position.y + j, true, unitsAreBlocking))
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
        return LineCast(source.x, source.y, target.x, target.y, false, true);
    }
    
    public bool IsLineOfSightBetween(Vector3Int source, Vector3Int target)
    {
        return LineCast(source.x, source.y, target.x, target.y);
    }
    
    private bool LineCast(int x0, int y0, int x1, int y1, bool removeFog = false, bool unitsAreBlocking = false)
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
            var previousTile = new Vector3Int(prevX, prevY, 0);
            var nextTile = new Vector3Int(xnext, ynext, 0);
            if (!IsNeighborVisible(previousTile, nextTile))
            {
                //tag_memorised(xnext, ynext); // make a note of the wall
                return false;
            }

            if (unitsAreBlocking && !(xnext == x0 && ynext == y0) && IsUnitOn(nextTile))
            {
                return false;
            }

            prevX = xnext;
            prevY = ynext;
            // Line-to-point distance formula < 0.5 
            // decide on the direction for the next step; horiz, verti, diag
            if(Mathf.Abs(dy * (xnext - x0 + sx) - dx * (ynext - y0)) / dist < 0.5f)
                xnext += sx;
            else if(Mathf.Abs(dy * (xnext - x0) - dx * (ynext - y0 + sy)) / dist < 0.5f)
                ynext += sy;
            else
            {
                xnext += sx;
                ynext += sy;
            }
        }
        if (!IsNeighborVisible(new Vector3Int(prevX, prevY, 0), new Vector3Int(xnext, ynext, 0)))
        {
            //tag_memorised(xnext, ynext); // make a note of the wall
            return false;
        }
        
        var litTile = new Vector3Int(xnext, ynext, 0);
        if (removeFog)
            RemoveFog(litTile);
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
        var fogHere = IsInFog(pos) ? "F" : "";
        return wallHere + doorHere + unitHere + floorHere + fogHere;
    }
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
