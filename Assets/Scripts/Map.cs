using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using DefaultNamespace;
using GridPathFinder.Map;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.Tilemaps;
using static Map;
using static Mod;
using Color = UnityEngine.Color;
using Random = UnityEngine.Random;


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

    public Direction[] Cardinal => new[] {Direction.East, Direction.West, Direction.North, Direction.South};
    
    public enum Axis
    {
        Horizontal,
        Vertical
    }
    public GameObject selectionRectPrefab;
    private GameObject selectionRect;
    private GameObject unitSelectedMarker; 
    public Tile[] floorTiles;
    public Tile[] walls;  // ORDER: NESW
    public Tile[] doors;

    protected Dictionary<string, Tilemap> layerMap = new Dictionary<string, Tilemap>();
    protected Unit[,] unitMap;
    private Camera gameCamera;
    public GameObject blanktilePrefab;
    private List<GameObject>[] allHighlights = new List<GameObject>[4]{ new List<GameObject>(), new List<GameObject>(), new List<GameObject>(), new List<GameObject>()};
    public Vector3Int CurrentCursorPosition { get; private set; }
    private HashSet<Vector3Int> visibleTiles = new HashSet<Vector3Int>();
    private Mod mod;
    private int genCurrentRegion = -1;
    private Dictionary<Vector3Int, int> genRegions = new Dictionary<Vector3Int, int>();
    private int extraConnectorChance = 5;
    private List<RectInt> genRooms;

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
        foreach (var layer in mapLayers)
        {
            layerMap[layer.name] = layer;
        }

        gameCamera = Camera.main;
        if (gameCamera == null)
        {
            throw new Exception("Game needs camera.");
        }
    }

    private void Generator(int width, int height)
    {
        if (width % 2 == 0 || height % 2 == 0)
        {
            throw new ArgumentException("Size must be odd.");
        }
        InitializeMap(width, height);
        
        // fill all with walls, we carve in the following steps
        layerMap["Walls"].BoxFill(new Vector3Int(1, 1, 0), walls[0], 0, 0, Width, Height);
        
        int roomTries = 200;
        genRooms = new List<RectInt>();
        
        for (int i = 0; i < roomTries; i++)
        {
            RectInt room = TryCreateRandomRoom();
            if (room.size != Vector2Int.zero)
            {
                genRooms.Add(room);
            }
        }
        
        for (var y = 1; y < height; y += 2) {
            for (var x = 1; x < width; x += 2) {
                if (!IsWallAt(x,y)) continue;
                GrowMaze(new Vector3Int(x, y, 0));
            }
        }

        ConnectRegions();
        RemoveDeadEnds();
        
        foreach (var room in genRooms)
        {
            DecorateRoom(room);
        }
    }

    private void DecorateRoom(RectInt room)
    {
        
    }

    private void RemoveDeadEnds()
    {
        var done = false;

        while (!done)
        {
            done = true;
            foreach (var pos in RectToList(1, 1, Width - 1, Height - 1))
            {
                if (IsWallAt(pos)) continue;

                var exits = 0;

                foreach (var neighborDirection in Cardinal)
                {
                    if (!IsWallAt(GetNeighbour(pos, neighborDirection))) exits++;
                }
                
                if (exits != 1) continue;

                done = false;
                
                AddWall(pos);
                if (IsDoorAt(pos))
                {
                    RemoveDoor(pos);
                }
            }
        }
    }

    private void RemoveDoor(Vector3Int pos)
    {
        layerMap["Doors"].SetTile(pos, null);
    }

    private void ConnectRegions()
    {
        Dictionary<Vector3Int, HashSet<int>> connectorRegions = new Dictionary<Vector3Int, HashSet<int>>();
        foreach (var pos in RectToList(1,1,Width - 1, Height - 1))
        {
            if (!IsWallAt(pos)) continue;
            
            HashSet<int> regions = new HashSet<int>();

            foreach (var neighborDirection in Cardinal)
            {
                var lookupPos = pos + neighborDirection.ToVector();
                if (genRegions.ContainsKey(lookupPos))
                {
                    int region = genRegions[lookupPos];
                    if (region != -1) regions.Add(region);
                }
            }
            
            if (regions.Count < 2) continue;

            connectorRegions[pos] = regions;
        }

        List<Vector3Int> connectors = new List<Vector3Int>(connectorRegions.Keys);

        var merged = new int[genCurrentRegion+1];
        var openRegions = new HashSet<int>();

        for (int i = 0; i <= genCurrentRegion; i++)
        {
            merged[i] = i;
            openRegions.Add(i);
        }

        while (openRegions.Count > 1)
        {
            var randomIndex = Random.Range(0, connectors.Count);
            Vector3Int connector = connectors.ElementAt(randomIndex);
            AddJunction(connector);
            var regions = connectorRegions[connector].Select(elem => merged[elem]);
            var dest = regions.First();
            var sources = regions.Skip(1).ToList();
            
            for (var i = 0; i <= genCurrentRegion; i++) {
                if (sources.Contains(merged[i])) {
                    merged[i] = dest;
                }
            }

            openRegions.RemoveWhere(item => sources.Contains(item));
            connectors.RemoveAll(pos =>
            {
                if (AreNeighbors(connector, pos, true)) return true;
                
                if ((connector - pos).magnitude < 2) return true;

                var conRegions = new HashSet<int>(connectorRegions[pos].Select(region => merged[region]));
                if (conRegions.Count > 1) return false;

                if (Random.Range(1, extraConnectorChance + 1) == 1)
                {
                    //AddJunction(pos);
                }

                return true;
            });
        }
    }

    private void AddJunction(Vector3Int connector)
    {
        layerMap["Floors"].SetTile(connector, floorTiles[0]);
        layerMap["Walls"].SetTile(connector, null);
        CreateDoor(connector.x, connector.y);
    }

    private void GrowMaze(Vector3Int start)
    {
        List<Vector3Int> cells = new List<Vector3Int>();
        Direction lastDirection = Direction.North;

        genCurrentRegion++;
        
        CarveMaze(start);
        
        cells.Add(start);

        while (cells.Count > 0)
        {
            var cell = cells.Last();
            var unmadeCells = new List<Direction>();
            
            foreach (var neighborDirection in Cardinal)
            {
                
                if (CanCarve(cell, neighborDirection))
                {
                    unmadeCells.Add(neighborDirection);
                }
            }

            if (unmadeCells.Count > 0)
            {
                int windingPercent = 23;
                Direction direction;
                if (unmadeCells.Contains(lastDirection) && Random.Range(0, 100) > windingPercent)
                {
                    direction = lastDirection;
                }
                else
                {
                    direction = unmadeCells[Random.Range(0, unmadeCells.Count)];
                }

                var firstCarvePos = GetNeighbour(cell, direction);
                var secondCarvePos = GetNeighbour(firstCarvePos, direction);

                RemoveWallAt(firstCarvePos);
                RemoveWallAt(secondCarvePos);
                
                cells.Add(secondCarvePos);

                lastDirection = direction;
            }
            else
            {
                cells.RemoveAt(cells.Count - 1);

                lastDirection = Direction.None;
            }
        }
    }

    private bool CanCarve(Vector3Int cell, Direction direction)
    {
        var endPos = cell + direction.ToVector() * 3;
        if (!Contains(endPos.x, endPos.y)) return false;

        return IsWallAt(cell + direction.ToVector() * 2);
    }

    private void CarveMaze(Vector3Int pos)
    {
        layerMap["Floors"].SetTile(pos, floorTiles[0]);
        RemoveWallAt(pos);
    }

    private RectInt TryCreateRandomRoom() // by removing walls, do not create rooms where we already carved
    {
        RectInt room = new RectInt(0,0,0,0);
        var lowerLeft = RandomPosition;
        
        if (lowerLeft.x % 2 == 0) lowerLeft.x = Random.value > 0.5 ? lowerLeft.x + 1 : lowerLeft.x - 1;
        if (lowerLeft.y % 2 == 0) lowerLeft.y = Random.value > 0.5 ? lowerLeft.y + 1 : lowerLeft.y - 1;

        
        var width = Random.Range(3, 5);
        var height = Random.Range(3, 5);

        height = height % 2 == 0 ? height + 1 : height;
        width = width % 2 == 0 ? width + 1 : width;

        int x = Random.Range(0, (int) (Math.Truncate((Width - width) / 2d))) * 2 + 1;
        int y = Random.Range(0, (int) (Math.Truncate((Height - height) / 2d))) * 2 + 1;
        
        room.x = lowerLeft.x;
        room.y = lowerLeft.y;
        room.SetMinMax(new Vector2Int(lowerLeft.x,lowerLeft.y), new Vector2Int(lowerLeft.x + width, lowerLeft.y + height));
        
        if (room.xMin <= 0 || room.xMax >= Width - 1 || room.yMin <= 0 || room.yMax >= Height - 1)
            return new RectInt(0,0,0,0);
        
        foreach (var existingRoom in genRooms)
        {
            if (existingRoom.Overlaps(room))
            {
                return new RectInt(0,0,0,0);
            }
        }
        
        genCurrentRegion++;
        
        CarveOut(room);

        return room;
    }

    private void CarveOut(RectInt room)
    {
        foreach (var pos in room.allPositionsWithin)
        {
            var roomTile = new Vector3Int(pos.x, pos.y, 0);
            RemoveWallAt(roomTile);   
        }
    }

    private void RemoveWallAt(Vector3Int pos)
    {
        layerMap["Walls"].SetTile(pos, null);
        TileData tiledata = new TileData();
        var tile = (FloorTile) layerMap["Floors"].GetTile(pos);
        tile.tileColor = IntToColor(genCurrentRegion);
        genRegions[pos] = genCurrentRegion;
    }

    private Color IntToColor(int regionIndex)
    {
        return ColorsAsList().ElementAt(regionIndex);
    }

    protected virtual void InitializeMap(int width, int height)
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
        layerMap["Floors"].BoxFill(new Vector3Int(1, 1, 0), floorTiles[0], 0, 0, Width, Height);
        layerMap["FogOfWar"].BoxFill(new Vector3Int(1, 1, 0), floorTiles[0], 0, 0, Width, Height);
    }   
    private void Start()
    {
        
    }

    public void FromRules(RuleMap mapRules)
    {
        Generator(25,25);
        //InitializeMap(mapRules.Size[0], mapRules.Size[1]);
        /*
        foreach (var mapScriptCommand in mapRules.MapScript)
        {
            if (mapScriptCommand.Command.ToLower().Equals("placeblock"))
            {
                foreach (var blockPlacementPosition in mapScriptCommand.Rects)
                {
                    var blockName = mapScriptCommand.Blocks[Random.Range(0, mapScriptCommand.Blocks.Count)];
                    PlaceMapBlock(blockPlacementPosition, blockName);
                } 
            }
        }
        */
        
    }

    private void PlaceMapBlock(int[] offset, string blockName)
    {
        var block = mod.GetMapBlock(blockName);
        foreach (var room in block.Rooms)
        {
            CreateRoom(room[0] + offset[0], room[1] + offset[1], room[2] + offset[0], room[3] + offset[1]);
        }
        foreach (var wall in block.HorizontalWalls)
        {
            CreateWalls(wall[0] + offset[0], wall[1] + offset[1], wall[2] + offset[0], wall[3] + offset[1]);
        }
        foreach (var wall in block.VerticalWalls)
        {
            CreateWalls(wall[0] + offset[0], wall[1] + offset[1], wall[2] + offset[0], wall[3] + offset[1]);
        }
        foreach (var door in block.HorizontalDoors)
        {
            CreateDoor(door[0] + offset[0], door[1] + offset[1]);
        }
        foreach (var door in block.VerticalDoors)
        {
            CreateDoor(door[0] + offset[0], door[1] + offset[1]);
        }
    }

    private void CreateDoor(int x, int y)
    {
        layerMap["Doors"].SetTile(new Vector3Int(x,y,0), doors[0]);
    }

    private void CreateRoom(int x0, int y0, int x1, int y1)
    {
        CreateWalls(x0, y0 - 1, x1, y0); // southern walls        
        CreateWalls(x0, y1, x1, y1 + 1); // northern walls
        CreateWalls(x0 - 1, y0, x0, y1); // western walls
        CreateWalls(x1, y0, x1 + 1, y1); // eastern walls
    }

    protected virtual void CreateWalls(int x0, int y0, int x1, int y1)
    {
        for (int xCount = x0; xCount <= x1; xCount++)
        {
            for (int yCount = y0; yCount <= y1; yCount++)
            {
                int x = xCount;
                int y = yCount;
                
                if (!Contains(x,y))
                    continue;
                var currentPos = new Vector3Int(x,y,0);

                AddWall(currentPos);
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

    public virtual void ResetFog()
    {
        layerMap["FogOfWar"].ClearAllTiles();
        layerMap["FogOfWar"].FloodFill(new Vector3Int(Width-1,Height-1,0), floorTiles[0]);
    }
    
    protected virtual void RemoveFog(Vector3Int position)
    {
        layerMap["FogOfWar"].SetTile(position, null);
    }

    public virtual bool IsInFog(Vector3Int position)
    {
        return layerMap["FogOfWar"].HasTile(position);
    }

    public bool IsNeighborReachable(IPlayerUnit unit, Vector3Int oldPos, Vector3Int newPos)
    {
        // handle diagonal cases
        if (oldPos.x != newPos.x && oldPos.y != newPos.y)
        {
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
        
        return CanBePlaced(unit, newPos, false);
    }

    public bool IsNeighborVisible(Vector3Int oldPos, Vector3Int newPos)
    {
        if (!Contains(oldPos.x, oldPos.y) || !Contains(newPos.x, newPos.y))
        {
            return false;
        }
        if (IsWallAt(oldPos) || IsWallAt(newPos))
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

    protected virtual void AddWall(Vector3Int pos, int wallIndex = 0)
    {
        if (Contains(pos.x, pos.y)) layerMap["Walls"].SetTile(pos, walls[wallIndex]);
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
        }
        return Vector3Int.zero;
    }

    public Vector3 CellToWorld(Vector3Int position)
    {
        return layerMap["Floors"].GetCellCenterWorld(position);
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

    private bool IsDoorAt(Vector3Int offsetPos)
    {
        return layerMap["Doors"].HasTile(offsetPos);
    }

    private bool IsWallAt(int x, int y)
    {
        return IsWallAt(new Vector3Int(x,y,0));
    }

    private bool IsWallAt(Vector3Int pos)
    {
        return layerMap["Walls"].HasTile(pos);
    }

    private bool IsFloorAt(int x, int y)
    {
        return layerMap["Floors"].HasTile(new Vector3Int(x,y,0));
    }

    public int Width { get; set; }

    public int Height { get; set; }

    public Vector3Int RandomPosition => new Vector3Int(Random.Range(0, Width), Random.Range(0, Height), 0);

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
            if (CanBePlaced(currentUnit, neighbor) && IsNeighborReachable(currentUnit, mapPos, neighbor))
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
        if (currentUnit.Rules.CanMoveDiagonally)
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
    
    private static List<Vector3Int> GetAllNeighbors(Vector3Int mapPos)
    {
        List<Vector3Int> neighbors = new List<Vector3Int>()
        {
            mapPos + Vector3Int.up,
            mapPos + Vector3Int.right,
            mapPos + Vector3Int.down,
            mapPos + Vector3Int.left,
        };
        neighbors.AddRange(new[]
        {
            mapPos + Vector3Int.up + Vector3Int.right,
            mapPos + Vector3Int.up + Vector3Int.left,
            mapPos + Vector3Int.down + Vector3Int.right,
            mapPos + Vector3Int.down + Vector3Int.left
        });

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

    public float GetEuclideanDistance(Vector3Int pointOne, Vector3Int pointTwo)
    {
        return (float) Math.Sqrt(Math.Pow((pointOne.x - pointTwo.x), 2) + Math.Pow(pointOne.y - pointTwo.y, 2));
    }

    public bool IsOtherFactionOn(IPlayerUnit unit, Vector3Int pos)
    {
        return IsOtherFactionOn((Unit) unit, pos);
    }

    public bool CanBePlaced(IPlayerUnit unit, Vector3Int mapPos, bool doorsAreBlocking = true)
    {
        return CanBePlaced((Unit) unit, mapPos, doorsAreBlocking);
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
        if (!IsUnitOn(pos))
        {
            return false;
        }

        var otherUnit = GetUnitAt(pos);
        return unit.Faction != otherUnit.Faction;
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

    public bool CanBePlaced(Unit unit, Vector3Int mapPos, bool doorsAreBlocking = true)
    {
        foreach (var offset in unit.OccupiedTiles)
        {
            var offsetPos = mapPos + offset;
            if (!Contains(offsetPos.x, offsetPos.y) 
                || IsOtherUnitOn(unit, offsetPos)
                || IsWallAt(offsetPos.x,offsetPos.y)
                || IsDoorAt(offsetPos))
            {
                return false;
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
