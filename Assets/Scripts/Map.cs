using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Xml;
using GridPathFinder.Map;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEngine.UI;
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

    public const int MapWidth = 16;
    public const int MapHeight = 16;
    public GameObject selectionRect;
    public Tile floorTile;
    public Tile northWall;
    public Tile eastWall;
    public Tile southWall;
    public Tile westWall;
    private Dictionary<string, Tilemap> layerMap = new Dictionary<string, Tilemap>();
    private Unit[,] unitMap = new Unit[MapWidth,MapHeight];
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
            layer.size = new Vector3Int(Width,Height,1);
            layerMap[layer.name] = layer;
        }
        
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

    private void CreateBlankSprite()
    {
        Texture2D blankTex = new Texture2D(32, 32) {wrapMode = TextureWrapMode.Repeat};
        var pixels = blankTex.GetPixels();
        for(var i = 0; i < pixels.Length; ++i)
        {
            pixels[i] = new Color(1,1,1,0.5f);
        }
        blankTex.SetPixels(pixels);
        blankTex.Apply();
        var blankSprite = Sprite.Create(blankTex, new Rect(0, 0, 32, 32), new Vector2(0.5f, 0.5f), 32f);
        
        blanktilePrefab = new GameObject();
        SpriteRenderer spriteRenderer = blanktilePrefab.AddComponent<SpriteRenderer>();
        spriteRenderer.sortingLayerName = "Units";
        spriteRenderer.sprite = blankSprite;
        
        PrefabUtility.SaveAsPrefabAsset(blanktilePrefab, "Assets/blanktile.prefab");
    }

    private void SetupWalls()
    {
        AddWall(new Vector3Int(2,2,0), Direction.East);
        AddWall(new Vector3Int(1,2,0), Direction.West);
        AddWall(new Vector3Int(2,3,0), Direction.East);
        AddWall(new Vector3Int(1,3,0), Direction.West);
        AddWall(new Vector3Int(2,4,0), Direction.East);
        AddWall(new Vector3Int(1,4,0), Direction.West);
        AddWall(new Vector3Int(2,5,0), Direction.East);
        AddWall(new Vector3Int(1,5,0), Direction.West);
        AddWall(new Vector3Int(2,5,0), Direction.North);
        AddWall(new Vector3Int(1,5,0), Direction.North);
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
        var hilite = Instantiate(blanktilePrefab, CellToWorld(mapPos), quaternion.identity);
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
        int i, j;
        for (i = -radius; i <= radius; i++)
            for (j = -radius; j <= radius; j++)
                if (i * i + j * j < radius * radius)
                    HasLOS(position.x, position.y, position.x + i, position.y + j, true);     
    }
    
    public HashSet<Vector3Int> GetFOV(Vector3Int position, int radius)
    {
        visibleTiles.Clear();
        UpdateFOV(position, radius);
        return visibleTiles;
    }

    private bool HasLOS(int x0, int y0, int x1, int y1, bool removeFog = false)
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
        dist = math.sqrt(dx * dx + dy * dy);

        while (xnext != x1 || ynext != y1) //essentially casting a ray of length radius: (radius^3)
        {
            if (!IsNeighborVisible(new Vector3Int(prevX, prevY, 0), new Vector3Int(xnext, ynext, 0)))
            {
                //tag_memorised(xnext, ynext); // make a note of the wall
                return false;
            }

            prevX = xnext;
            prevY = ynext;
            // Line-to-point distance formula < 0.5 
            if(math.abs(dy * (xnext - x0 + sx) - dx * (ynext - y0)) / dist < 0.5f)
                xnext += sx;
            else if(math.abs(dy * (xnext - x0) - dx * (ynext - y0 + sy)) / dist < 0.5f)
                ynext += sy;
            else
            {
                xnext += sx;
                ynext += sy;
            }
        }

        var litTile = new Vector3Int(prevX, prevY, 0);
        visibleTiles.Add(litTile);
        if (removeFog)
            RemoveFog(litTile);
        return true;
    }

    public void ResetFog()
    {
        layerMap["FogOfWar"].BoxFill(new Vector3Int(1,1,0), floorTile, 0,0,Width,Height);
    }
    
    private void RemoveFog(Vector3Int position)
    {
        layerMap["FogOfWar"].SetTile(position, null);
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
    bool IsWallBetween(Vector3Int tileOne, Vector3Int tileTwo)
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

    public int Width
    {
        get { return MapWidth; }
    }
    public int Height
    {
        get { return MapHeight; }
    }

    public Vector3Int RandomPosition => new Vector3Int(Random.Range(0, Width -1), Random.Range(0, Height - 1), 0);

    public bool Contains(int x, int y)
    {
        return (x >= 0 && y >= 0) && (x < Width && y < Height);
    }
    public List<Point> GetNeighbors(Point position)
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
            if (IsNeighborVisible(mapPos, neighbor) && Contains(neighbor.x, neighbor.y) && !IsUnitOn(neighbor))
            {
                reachableNeighbors.Add(neighbor);
            }
        }

        return reachableNeighbors.ConvertAll(elem => new Point(elem.x, elem.y));
    }

}
