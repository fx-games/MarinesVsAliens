using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using QuickGraph;
using QuickGraph.Algorithms.ShortestPath;
using UnityEngine;
using static Map;
using Random = UnityEngine.Random;

namespace DefaultNamespace
{
    
    public class DungeonGeneratorBase : GeneratorBase
    {
        private int genCurrentRegion = -1;
        private Dictionary<Vector3Int, int> genRegions = new Dictionary<Vector3Int, int>();
        private int extraConnectorChance = 5;
        private List<RectInt> genRooms;

        public DungeonGeneratorBase(int height, int width)
        {
            Height = height;
            Width = width;
        }

        public int Height { get; set; }
        public int Width { get; set; }
        
        public override IEnumerator Generate()
        {
            Generator(Width, Height);
            yield break;
        }

        public override void Step()
        {
            throw new NotImplementedException();
        }

        private void Test()
        {
            var g = new AdjacencyGraph<Vector3Int, TaggedEdge<Vector3Int, string>>();
            Vector3Int zero = Vector3Int.zero;
            Vector3Int rightOfZero = Vector3Int.right;
            g.AddVertex(zero);
            g.AddVertex(rightOfZero);
            
            var edge = new TaggedEdge<Vector3Int, string>(zero, rightOfZero, "NeighborEdge");
            g.AddEdge(edge);

            var weights = new Func<TaggedEdge<Vector3Int, string>, double>(e => e.Source.x == e.Target.x || e.Source.y == e.Target.y
                ? 10
                : 14);
            
            var fw = new FloydWarshallAllShortestPathAlgorithm<Vector3Int, TaggedEdge<Vector3Int, string>>(g, weights);
            fw.Compute();

        }

        private void Generator(int width, int height)
        {
            if (width % 2 == 0 || height % 2 == 0)
            {
                throw new ArgumentException("Size must be odd.");
            }
            map.Init(width, height);
            
            // fill all with walls, we carve in the following steps
            map.FillLayer("Walls");
            
            int roomTries = 2000;
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
                    if (!map.IsWallAt(x,y)) continue;
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
                foreach (var pos in RectToList(1, 1, map.Width - 1, map.Height - 1))
                {
                    if (map.IsWallAt(pos)) continue;
    
                    var exits = 0;
    
                    foreach (var neighborDirection in map.Cardinal)
                    {
                        if (!map.IsWallAt(map.GetNeighborInDirection(pos, neighborDirection))) exits++;
                    }
                    
                    if (exits != 1) continue;
    
                    done = false;
                    
                    map.AddWall(pos);
                    if (map.IsDoorAt(pos))
                    {
                        map.RemoveDoor(pos);
                    }
                }
            }
        }
    
        private void ConnectRegions()
        {
            Dictionary<Vector3Int, HashSet<int>> connectorRegions = new Dictionary<Vector3Int, HashSet<int>>();
            foreach (var pos in RectToList(1,1,map.Width - 1, map.Height - 1))
            {
                if (!map.IsWallAt(pos)) continue;
                
                HashSet<int> regions = new HashSet<int>();
    
                foreach (var neighborDirection in map.Cardinal)
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
                    if (map.AreNeighbors(connector, pos, true)) return true;
                    
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
            map.SetFloor(connector);
            //layerMap["Floors"].SetTile(connector, floorTiles[0]);
            map.RemoveWallAt(connector);
            //layerMap["Walls"].SetTile(connector, null);
            map.CreateDoor(connector.x, connector.y);
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
                
                foreach (var neighborDirection in map.Cardinal)
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
    
                    var firstCarvePos = map.GetNeighborInDirection(cell, direction);
                    var secondCarvePos = map.GetNeighborInDirection(firstCarvePos, direction);
    
                    map.RemoveWallAt(firstCarvePos);
                    genRegions[firstCarvePos] = genCurrentRegion;
                    map.RemoveWallAt(secondCarvePos);
                    genRegions[secondCarvePos] = genCurrentRegion;
                    
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
            if (!map.Contains(endPos.x, endPos.y)) return false;
    
            return map.IsWallAt(cell + direction.ToVector() * 2);
        }
    
        private void CarveMaze(Vector3Int pos)
        {
            //layerMap["Floors"].SetTile(pos, floorTiles[0]);
            map.SetFloor(pos);
            map.RemoveWallAt(pos);
            genRegions[pos] = genCurrentRegion;
        }
    
        private RectInt TryCreateRandomRoom() // by removing walls, do not create rooms where we already carved
        {
            RectInt room = new RectInt(0,0,0,0);
            var lowerLeft = map.RandomPosition;
                    
            var width = Random.Range(3, 5);
            var height = Random.Range(3, 5);

            if (width % 2 == 0) width += 1;
            if (height % 2 == 0) height += 1;

            lowerLeft.x = lowerLeft.x % 2 == 0 ? lowerLeft.x + 1 : lowerLeft.x;
            lowerLeft.y = lowerLeft.y % 2 == 0 ? lowerLeft.y + 1 : lowerLeft.y;
            
            room.SetMinMax(new Vector2Int(lowerLeft.x,lowerLeft.y), new Vector2Int(lowerLeft.x + width, lowerLeft.y + height));
            
            if (room.xMin <= 0 || room.xMax >= map.Width - 1 || room.yMin <= 0 || room.yMax >= map.Height - 1)
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
                map.RemoveWallAt(roomTile);   
                genRegions[roomTile] = genCurrentRegion;
            }
        }

    }

   
}