using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using GridPathFinder.Map;
using UnityEngine;
using UnityEngine.Tilemaps;
using Color = UnityEngine.Color;
using Random = UnityEngine.Random;

public class Map : MonoBehaviour, IGridMap
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
    }

    public enum Axis
    {
        Horizontal,
        Vertical
    }
    public GameObject selectionRect;
    public Tile floorTile;
    public Tile northWall;
    public Tile eastWall;
    public Tile southWall;
    public Tile westWall;
    public Tile[] verticalDoors;
    public Tile[] horizontalDoors;

    private Dictionary<string, Tilemap> layerMap = new Dictionary<string, Tilemap>();
    private Unit[,] unitMap;
    private Camera gameCamera;
    public GameObject blanktilePrefab;
    private List<GameObject>[] allHighlights = new List<GameObject>[4]{ new List<GameObject>(), new List<GameObject>(), new List<GameObject>(), new List<GameObject>()};
    public Vector3Int CurrentCursorPosition { get; private set; }
    private HashSet<Vector3Int> visibleTiles = new HashSet<Vector3Int>();

    // Start is called before the first frame update
    void Awake()
    {
        //CreateBlankSprite();
        var mapLayers = GetComponentsInChildren<Tilemap>();
        foreach (var layer in mapLayers)
        {
            layer.ClearAllTiles();
            layer.size = new Vector3Int(12,12,1);
            layerMap[layer.name] = layer;
        }

        var size = mapLayers.First().size;
        Width = size.x;
        Height = size.y;
        
        unitMap = new Unit[Width, Height];

        gameCamera = Camera.main;
        if (gameCamera == null)
        {
            throw new Exception("Game needs camera.");
        }
        layerMap["Floors"].BoxFill(new Vector3Int(1,1,0), floorTile, 0,0,Width,Height);
        layerMap["FogOfWar"].BoxFill(new Vector3Int(1,1,0), floorTile, 0,0,Width,Height);
        SetupWalls();
    }

    private void Start()
    {
        
    }

    private void SetupWalls()
    {
        CreateRoom(0, 7, 4, 11);
        CreateDoor(1, 6, 2, 7, Axis.Horizontal);
        
        CreateRoom(2, 2, 6, 4);
        CreateDoor(4, 1, 5, 2, Axis.Horizontal);
        
        CreateRoom(7, 0, 11, 4);
        CreateDoor(9, 4, 10, 5, Axis.Horizontal);

        CreateWalls(6, 5, 7, 9, Axis.Vertical);
        CreateWalls(9, 8, 10, 9, Axis.Vertical);
        CreateWalls(9, 9, 9, 10, Axis.Horizontal);
        CreateWalls(10, 7, 11, 8, Axis.Horizontal);

        CreateDoor(7, 9, 8, 10, Axis.Horizontal);
    }

    private void CreateDoor(int x0, int y0, int x1, int y1, Axis axis)
    {
        Vector3Int lowerLeft = new Vector3Int(x0, y0, 0);
        Vector3Int upperLeft = new Vector3Int(x0, y1, 0);
            
        Vector3Int lowerRight= new Vector3Int(x1, y0, 0);
        Vector3Int upperRight = new Vector3Int(x1, y1, 0);
        if (axis == Axis.Horizontal)
        {
            layerMap["NorthWalls"].SetTile(lowerLeft, horizontalDoors[0]);
            layerMap["NorthWalls"].SetTile(lowerRight, horizontalDoors[1]);
            
            layerMap["SouthWalls"].SetTile(upperLeft, horizontalDoors[2]);
            layerMap["SouthWalls"].SetTile(upperRight, horizontalDoors[3]);
        }
        else if (axis == Axis.Horizontal)
        {
            layerMap["EastWalls"].SetTile(lowerLeft, horizontalDoors[0]);
            layerMap["WestWalls"].SetTile(lowerRight, horizontalDoors[1]);
            
            layerMap["EastWalls"].SetTile(upperLeft, horizontalDoors[2]);
            layerMap["WestWalls"].SetTile(upperRight, horizontalDoors[3]);
        }
    }

    private void CreateRoom(int x0, int y0, int x1, int y1)
    {
        CreateWalls(x0, y0 - 1, x1, y0, Axis.Horizontal); // southern walls        
        CreateWalls(x0, y1, x1, y1 + 1, Axis.Horizontal); // northern walls
        CreateWalls(x0 - 1, y0, x0, y1, Axis.Vertical); // western walls
        CreateWalls(x1, y0, x1 + 1, y1, Axis.Vertical); // eastern walls
    }
    
    private void CreateWalls(int x0, int y0, int x1, int y1, Axis axis)
    {
        for (int x = x0; x <= x1; x++)
        {
            for (int y = y0; y <= y1; y++)
            {
                if (!Contains(x,y))
                    continue;
                var currentPos = new Vector3Int(x,y,0);
                
                if (axis == Axis.Horizontal)
                {
                    if (y == y0) // lower row
                    {
                        AddWall(currentPos, Direction.North);
                    }
                    else if (y == y1) // upper row
                    {
                        AddWall(currentPos, Direction.South);
                    }
                }
                else if (axis == Axis.Vertical)
                {
                    if (x == x0) // left col
                    {
                        AddWall(currentPos, Direction.East);
                    }
                    else if (x == x1) // right col
                    {
                        AddWall(currentPos, Direction.West);
                    }
                }
            }
        }
    }

    // Update is called once per frame
    void Update()
    {
        Vector3 pos = gameCamera.ScreenToWorldPoint(Input.mousePosition);
        var mapPos = WorldToCell(pos);
        if (Contains(mapPos.x, mapPos.y))
        {
            CurrentCursorPosition = mapPos;
            selectionRect.transform.position = CellToWorld(mapPos);
        }
    }

    public Vector3Int WorldToCell(Vector3 pos)
    {
        return layerMap["Floors"].WorldToCell(pos);
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
    }

    public Unit GetUnitAt(Vector3Int mapPos)
    {
        return unitMap[mapPos.x, mapPos.y];
    }

    public bool IsUnitOn(Vector3Int mapPos)
    {
        return unitMap[mapPos.x, mapPos.y] != null;
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
        Vector3Int rayDirVector = ToVector(rayDirection);
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

    private Vector3Int ToVector(Direction direction)
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

    public void ResetFog()
    {
        layerMap["FogOfWar"].ClearAllTiles();
        layerMap["FogOfWar"].FloodFill(new Vector3Int(Width-1,Height-1,0), floorTile);
    }
    
    private void RemoveFog(Vector3Int position)
    {
        layerMap["FogOfWar"].SetTile(position, null);
    }

    public bool IsInFog(Vector3Int position)
    {
        return layerMap["FogOfWar"].HasTile(position);
    }
    bool IsNeighborVisible(Vector3Int tileOne, Vector3Int tileTwo)
    {
        if (!Contains(tileOne.x, tileOne.y) || !Contains(tileTwo.x, tileTwo.y))
        {
            return false;
        }
        if (tileOne.x == tileTwo.x || tileOne.y == tileTwo.y)
        {
            return !IsWallBetween(tileOne, tileTwo);
        }

        // handle diagonal cases

        var dx = tileOne.x - tileTwo.x;
        var dy = tileOne.y - tileTwo.y;
        // one is to the top right of two
        var vertTile = new Vector3Int(tileTwo.x + dx, tileTwo.y, 0);
        var horiTile = new Vector3Int(tileTwo.x, tileTwo.y + dy, 0);

        var oneToVertBlocked = IsWallBetween(tileOne, vertTile);
        var oneToHoriBlocked = IsWallBetween(tileOne, horiTile);

        if (oneToHoriBlocked && oneToVertBlocked)
            return false;

        var vertToTwoBlocked = IsWallBetween(vertTile, tileTwo);
        var horiToTwoBlocked = IsWallBetween(horiTile, tileTwo);

        if (oneToHoriBlocked && vertToTwoBlocked)
            return false;

        if (oneToVertBlocked && horiToTwoBlocked)
            return false;

        if (vertToTwoBlocked && horiToTwoBlocked)
            return false;

        return true;
    }

    public bool IsWallBetween(Vector3Int tileOne, Vector3Int tileTwo)
    {
        if (tileOne.Equals(tileTwo)) return false;
        if (tileOne.y == tileTwo.y)
        {
            if (tileOne.x - tileTwo.x == 1)
            {
                // one is to the right of two
                // two (east)  -> one (west)
                return layerMap["EastWalls"].HasTile(tileTwo) 
                       || layerMap["WestWalls"].HasTile(tileOne);
            }
            if (tileOne.x - tileTwo.x == -1)
            {
                return layerMap["EastWalls"].HasTile(tileOne) 
                       || layerMap["WestWalls"].HasTile(tileTwo);
            }
        }
        else if (tileOne.x == tileTwo.x)
        {
            if (tileOne.y - tileTwo.y == 1)
            {
                // one is on top of two
                // two (north)  -> one (south)
                return layerMap["NorthWalls"].HasTile(tileTwo)
                       || layerMap["SouthWalls"].HasTile(tileOne);
            }
            if (tileOne.y - tileTwo.y == -1)
            {
                return layerMap["NorthWalls"].HasTile(tileOne)
                       || layerMap["SouthWalls"].HasTile(tileTwo);
            }
        }
        return false;
    }

    void AddWall(Vector3Int tilePos, Direction direction)
    {
        AddWall(tilePos, GetNeighbour(tilePos, direction));
    }
    void AddWall(Vector3Int tileOne, Vector3Int tileTwo)
    {
        if (tileOne.y == tileTwo.y)
        {
            if (tileOne.x - tileTwo.x == 1)
            {
                // one is to the right of two
                // two (east)  -> one (west)
                layerMap["EastWalls"].SetTile(tileTwo, eastWall);
                layerMap["WestWalls"].SetTile(tileOne, westWall);
            }
            else if (tileOne.x - tileTwo.x == -1)
            {
                layerMap["EastWalls"].SetTile(tileOne, eastWall);
                layerMap["WestWalls"].SetTile(tileTwo, westWall);
            }
        }
        else if (tileOne.x == tileTwo.x)
        {
            if (tileOne.y - tileTwo.y == 1)
            {
                // one is on top of two
                // two (north)  -> one (south)
                layerMap["NorthWalls"].SetTile(tileTwo, northWall);
                layerMap["SouthWalls"].SetTile(tileOne, southWall);
            }
            else if (tileOne.y - tileTwo.y == -1)
            {
                layerMap["NorthWalls"].SetTile(tileOne, northWall);
                layerMap["SouthWalls"].SetTile(tileTwo, southWall);
            }
        }
    }

    Vector3Int GetNeighbour(Vector3Int position, Direction direction)
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
        }
        return Vector3Int.zero;
    }

    public Vector3 CellToWorld(Vector3Int position)
    {
        return layerMap["Floors"].GetCellCenterWorld(position);
    }

    public bool AddUnit(Unit unit, Vector3Int mapPos)
    {
        if (IsUnitOn(mapPos))
        {
            return false;
        }

        unitMap[mapPos.x, mapPos.y] = unit;
        RemoveFog(mapPos);
        return true;
    }
    
    public void RemoveUnit(Vector3Int mapPos)
    {
        if (IsUnitOn(mapPos))
        {
            unitMap[mapPos.x, mapPos.y] = null;
        }
    }

    public bool MoveUnit(Unit unit, Vector3Int oldMapPos, Vector3Int newMapPos)
    {
        if (!AddUnit(unit, newMapPos)) return false;
        RemoveUnit(oldMapPos);
        return true;
    }

    public bool IsWalkable(int x, int y)
    {
        return true;
    }

    public int Width { get; set; }

    public int Height { get; set; }

    public Vector3Int RandomPosition => new Vector3Int(Random.Range(0, Width -1), Random.Range(0, Height - 1), 0);

    public bool Contains(int x, int y)
    {
        return (x >= 0 && y >= 0) && (x < Width && y < Height);
    }
    public List<Point> GetNeighbors(Unit currentUnit, Point position)
    {
        Vector3Int mapPos = new Vector3Int(position.X, position.Y, 0);
        List<Vector3Int> reachableNeighbors = new List<Vector3Int>();
        List<Vector3Int> neighbors = new List<Vector3Int>()
        {
            mapPos + Vector3Int.up,
            mapPos + Vector3Int.right,
            mapPos + Vector3Int.down,
            mapPos + Vector3Int.left,
            mapPos + Vector3Int.up + Vector3Int.right,
            mapPos + Vector3Int.up + Vector3Int.left,
            mapPos + Vector3Int.down + Vector3Int.right,
            mapPos + Vector3Int.down + Vector3Int.left
        };
        foreach (var neighbor in neighbors)
        {
            if (IsNeighborVisible(mapPos, neighbor) && Contains(neighbor.x, neighbor.y) && !IsOtherFactionOn(currentUnit, neighbor))
            {
                reachableNeighbors.Add(neighbor);
            }
        }

        return reachableNeighbors.ConvertAll(elem => new Point(elem.x, elem.y));
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
                    var euclideanDistance = GetEuclideanDistance(mapPos, nextPos);
                    if (Mathf.Floor(euclideanDistance) <= radius)
                    {
                        affectedTiles.Add(nextPos);
                    }
                }
            }
        }

        return affectedTiles.ToList();
    }
    
    private float GetEuclideanDistance(Vector3Int pointOne, Vector3Int pointTwo)
    {
        return (float) Math.Sqrt(Math.Pow((pointOne.x - pointTwo.x), 2) + Math.Pow(pointOne.y - pointTwo.y, 2));
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
        if (!IsUnitOn(pos))
        {
            return false;
        }

        var otherUnit = GetUnitAt(pos);
        return unit.Faction != otherUnit.Faction;
    }

    
}
