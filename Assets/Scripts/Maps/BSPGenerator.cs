using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DefaultNamespace;
using DelaunayVoronoi;
using QuickGraph;
using QuickGraph.Algorithms;
using QuickGraph.Algorithms.Observers;
using QuickGraph.Algorithms.Search;
using QuickGraph.Algorithms.ShortestPath;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Random = UnityEngine.Random;

public class BSPRoomNode
{
    public BSPRoomNode(int x, int y, int width, int height)
    {
        Bounds = new RectInt(x,y,width,height);
    }

    public RectInt Bounds { get; set; }
    public BSPRoomNode Parent { get; set; }
    public Tuple<BSPRoomNode, BSPRoomNode> Children { get; private set; }
    public Vector3Int RoomCenter => new Vector3Int((int) Bounds.center.x,(int) Bounds.center.y,0);

    public void PartitionNode()
    {
        var bounds = Bounds;
        if (bounds.width > bounds.height)
        {
            // horizontal
            var splitAtX = Random.Range(bounds.xMin + 2, bounds.xMax - 2);
            var leftWidth = splitAtX - bounds.xMin;
            var rightWidth = bounds.width - leftWidth - 1;

            var leftRect = new BSPRoomNode(bounds.xMin, bounds.yMin, leftWidth, bounds.height);            
            var rightRect = new BSPRoomNode(splitAtX + 1, bounds.yMin, rightWidth, bounds.height);
            leftRect.Parent = this;
            rightRect.Parent = this;
            Children = new Tuple<BSPRoomNode, BSPRoomNode>(leftRect, rightRect);
        }
        else
        {
            // vertical
            var splitAtY = Random.Range(bounds.yMin + 2, bounds.yMax - 2);
            var lowerHeight = splitAtY - bounds.yMin;
            var upperHeight = bounds.height - lowerHeight - 1;
            var lowerRect = new BSPRoomNode(bounds.xMin, bounds.yMin, bounds.width, lowerHeight);
            var upperRect = new BSPRoomNode(bounds.xMin, splitAtY + 1, bounds.width, upperHeight);
            lowerRect.Parent = this;
            upperRect.Parent = this;
            Children = new Tuple<BSPRoomNode, BSPRoomNode>(lowerRect, upperRect);
        }
    }
}
public class BspGeneratorBase : GeneratorBase
{
    private readonly int _width;
    private readonly int _height;
    private List<BSPRoomNode> leafs = new List<BSPRoomNode>();
    private HashSet<BSPRoomNode> connectedRooms = new HashSet<BSPRoomNode>();
    private bool nextStep = false;
    private BSPRoomNode rootNode;
    private IEnumerable<STaggedEquatableEdge<Vector3Int, Corridor>> mst;
    private UndirectedGraph<Vector3Int, STaggedEquatableEdge<Vector3Int, Corridor>> mstGraph;
    private Dictionary<Vector3Int, Vector3Int> predecessors;
    private List<Vector3Int> mstLeafs;
    private int tileSet = 0;
    public BspGeneratorBase(int width, int height)
    {
        _width = width;
        _height = height;
    }

    public override IEnumerator Generate()
    {        
        leafs.Clear();
        
        map.Init(_width, _height);            
        // fill all with walls, we carve in the following steps
        map.FillLayer("Floors", tileSet);
        map.FillLayer("Walls", tileSet);
        
        rootNode = new BSPRoomNode(1,1, _width-2, _height-2);
        
        RecursivePartitioning(rootNode);
        
        AddRooms();
        
        map.RemoveAllHighlights();
        connectedRooms.Clear();
        
        mst = CreateMinimumSpanningTree(leafs);
        mstGraph = mst.ToUndirectedGraph<Vector3Int, STaggedEquatableEdge<Vector3Int, Corridor>>();

        //yield return ConnectRoomsRecursive(rootNode);
        ConnectRoomsFromMST();

        var mapHasNotEnoughSpawnZones = ChooseRegions();

        if (!mapHasNotEnoughSpawnZones)
        {
            yield return Generate();
            yield break;
        }

        PlaceSkirmishSpawnPoints();
        
        yield break;
    }

    private void PlaceSkirmishSpawnPoints()
    {
        HashSet<Vector3Int> spawnZones = new HashSet<Vector3Int>();
        while (spawnZones.Count < GameController.SkirmishPlayerCount)
        {
            var randomZone = mstLeafs.RandomElement();
            spawnZones.Add(randomZone);
        }

        int faction = 0;
        foreach (var spawnPos in spawnZones)
        {
            var roomNode = RoomFromPosition(spawnPos);
            PlaceSpawnPointsInRoom(roomNode, faction);
            faction++;
        }
    }

    private void PlaceSpawnPointsInRoom(BSPRoomNode roomNode, int factionIndex, int count = 4)
    {
        int placed = 0;
        foreach (var pos in roomNode.Bounds.allPositionsWithin)
        {
            if (map.IsFloorAt(pos.x, pos.y) && placed < count)
            {
                map.CreateSpawnPoint(pos.ToVector3Int(), factionIndex);
                placed++;
            }
        }
    }

    private bool ChooseRegions()
    {
        map.RemoveAllHighlights();
        map.RemoveAllCellLabels();
        
        var green = new Color(0f, 0.8f, 0, 0.5f);
        var red = new Color(0.8f, 0.2f, 0, 0.5f);
        var yellow = new Color(0.8f, 0.8f, 0, 0.5f);
        
        mstLeafs = mstGraph.Vertices.Where(v => mstGraph.AdjacentDegree(v) == 1).ToList();

        if (mstLeafs.Count < GameController.SkirmishPlayerCount) return false;
        
        // choose a leaf of the mst at random
        var randomLeaf = mstLeafs.RandomElement();
        
        // calculate distance to all other vertices
        var distances = new Dictionary<Vector3Int, int>();
        predecessors = new Dictionary<Vector3Int, Vector3Int>();
        CalcDistanceTable(0, randomLeaf, distances, predecessors);
        var distanceList = distances.ToList();
        distanceList.Sort((one, two) => one.Value.CompareTo(two.Value));
        
        foreach (var keyValuePair in predecessors)
        {
            map.DrawLine(keyValuePair.Key, keyValuePair.Value, yellow);
        }
        
        distanceList.ForEach(kvp => map.SetTextAt(kvp.Key, kvp.Value.ToString()));
        // choose by distance
        var start = randomLeaf;
        var keyVertex = distanceList.Skip(distanceList.Count / 2).First().Key;
        var end = distanceList.Last().Key;

        var doorEdge = FindDoorTo(end);
        map.CreateDoor(doorEdge.Tag.DoorPosition, 1);

        var startRoom = RoomFromPosition(start);
        var keyRoom = RoomFromPosition(keyVertex);
        var endRoom = RoomFromPosition(end);
        
        ColorRoom(startRoom, green);
        ColorRoom(keyRoom, yellow);
        ColorRoom(endRoom, red);

        return true;
    }

    private STaggedEquatableEdge<Vector3Int, Corridor> FindDoorTo(Vector3Int currentVertex)
    {
        var predecessor = predecessors[currentVertex];
        STaggedEquatableEdge<Vector3Int, Corridor> edge;
        if (mstGraph.TryGetEdge(predecessor, currentVertex, out edge))
        {
            if (edge.Tag.HasDoor) return edge;
            return FindDoorTo(predecessor);
        }

        return null;
    }

    private void CalcDistanceTable(int currentDistance, Vector3Int vertex, Dictionary<Vector3Int, int> distances, Dictionary<Vector3Int, Vector3Int> predecessors)
    {
        if (distances.ContainsKey(vertex)) return;
        distances[vertex] = currentDistance;
        foreach (var edge in mstGraph.AdjacentEdges(vertex))
        {
            var otherVertex = edge.GetOtherVertex(vertex);
            if (!predecessors.ContainsKey(otherVertex)) predecessors[otherVertex] = vertex;
            CalcDistanceTable(currentDistance + 1, otherVertex, distances, predecessors);
        }
    }

    private void ColorRoom(BSPRoomNode room, Color color)
    {        
        foreach (var roomPos in room.Bounds.allPositionsWithin)
        {
            map.HighlightTile(roomPos.ToVector3Int(), color);
        }
    }

    private void ConnectRoomsFromMST()
    {
        map.RemoveAllLines();
        foreach (var edge in mst)
        {
            //DrawEdge(edge);
            var firstRoom = RoomFromPosition(edge.Source);
            var secondRoom = RoomFromPosition(edge.Target);
            if (firstRoom != null && secondRoom != null)
            {
                var corridor = ConnectTwoRooms(firstRoom, secondRoom);
                edge.Tag = corridor;
            }
        }
    }

    private BSPRoomNode RoomFromPosition(Vector3Int pos)
    {
        return leafs.Find(r => r.Bounds.Contains(new Vector2Int(pos.x, pos.y)));
    }

    private IEnumerable<STaggedEquatableEdge<Vector3Int, Corridor>> CreateMinimumSpanningTree(List<BSPRoomNode> rooms)
    {
        var delaunayTriangulator = new DelaunayTriangulator();
        var points = rooms.Select(r => new Point(r.Bounds.center.x, r.Bounds.center.y));
        var triangles = delaunayTriangulator.BowyerWatson(points);

        UndirectedGraph<Vector3Int, STaggedEquatableEdge<Vector3Int, Corridor>> g =
            new UndirectedGraph<Vector3Int, STaggedEquatableEdge<Vector3Int, Corridor>>(allowParallelEdges: false);
        Func<STaggedEquatableEdge<Vector3Int, Corridor>, double> edgeWeights = edge => (edge.Source - edge.Target).magnitude;

        foreach (var triangle in triangles)
        {
            var start = new Vector3Int((int) triangle.Vertices[0].X, (int) triangle.Vertices[0].Y, 0);
            var mid = new Vector3Int((int) triangle.Vertices[1].X, (int) triangle.Vertices[1].Y, 0);
            var end = new Vector3Int((int) triangle.Vertices[2].X, (int) triangle.Vertices[2].Y, 0);

            var graphEdgeOne = new STaggedEquatableEdge<Vector3Int, Corridor>(start, mid, null);
            var graphEdgeTwo = new STaggedEquatableEdge<Vector3Int, Corridor>(mid, end, null);
            var graphEdgeThree = new STaggedEquatableEdge<Vector3Int, Corridor>(end, start, null);
            g.AddVerticesAndEdge(graphEdgeOne);
            g.AddVerticesAndEdge(graphEdgeTwo);
            g.AddVerticesAndEdge(graphEdgeThree);
        }

        return g.MinimumSpanningTreePrim(edgeWeights);
    }

    private void DrawTriangle(Triangle triangle)
    {
        var color = new Color(0f,0.8f,0,0.5f);
        var one = triangle.Vertices[0].ToVector3Int();
        var two = triangle.Vertices[1].ToVector3Int();
        var three = triangle.Vertices[2].ToVector3Int();
        map.DrawLine(one, two, color);
        map.DrawLine(two, three, color);
        map.DrawLine(three, one, color);
    }

    private void DrawEdge(STaggedEquatableEdge<Vector3Int, Corridor> edge)
    {
        map.DrawLine(edge.Source, edge.Target, Color.green);
    }

    private void DrawEdge(Edge edge)
    {
        var start = new Vector3Int((int) edge.Point1.X, (int) edge.Point1.Y, 0);
        var end = new Vector3Int((int) edge.Point2.X, (int) edge.Point2.Y, 0);
        map.DrawLine(start, end, Color.green);
    }

    public override void Step()
    {
        nextStep = true;
    }

    private IEnumerator ConnectRoomsRecursive(BSPRoomNode currentNode)
    {
        if (currentNode.Children?.Item1 != null && currentNode.Children?.Item2 != null)
        {
            nextStep = false;
            yield return ConnectTwoRooms(currentNode.Children.Item1, currentNode.Children.Item2);            
            yield return ConnectRoomsRecursive(currentNode.Children.Item1);
            yield return ConnectRoomsRecursive(currentNode.Children.Item2);
        }        
    }

    
    private Corridor ConnectTwoRooms(BSPRoomNode firstSibling, BSPRoomNode secondSibling)
    {
        Corridor corr = new Corridor()
        {
            ConnectedRooms = new Tuple<BSPRoomNode, BSPRoomNode>(firstSibling, secondSibling)
        };
        nextStep = false;
        Vector3Int corridorStart = Vector3Int.zero;
        Vector3Int corridorEnd = Vector3Int.zero;
        
        var one = firstSibling.Bounds;
        var two = secondSibling.Bounds;
        var needsHorizontal = false;
        var needsVertical = false;
        if (one.xMax < two.xMin) // one is left of two
        {
            needsHorizontal = true;
        }
        else if (one.xMin > two.xMax) // two is left of one
        {
            needsHorizontal = true;
        }
        
        if (one.yMax < two.yMin) // one is below of two
        {
            needsVertical = true;
        }
        else if (one.yMin > two.yMax) // two is below of one
        {
            needsVertical = true;
        }

        if (needsHorizontal && !needsVertical)
        {
            // chose y coordinate
            var minY = Mathf.Max(one.yMin, two.yMin);
            var maxY = Mathf.Min(one.yMax, two.yMax);
            var corrY = Random.Range(minY, maxY);
            var oneX = one.center.x;
            var twoX = two.center.x;
            corridorStart.x = (int) oneX;
            corridorStart.y = corrY;
            corridorEnd.x = (int) twoX;
            corridorEnd.y = corrY;
        }
        
        if (!needsHorizontal && needsVertical)
        {
            // chose x coordinate
            var minX = Mathf.Max(one.xMin, two.xMin);
            var maxX = Mathf.Min(one.xMax, two.xMax);
            var corrX = Random.Range(minX, maxX);
            var oneY = one.center.y;
            var twoY = two.center.y;
            corridorStart.x = corrX;
            corridorStart.y = (int) oneY;
            corridorEnd.x = corrX;
            corridorEnd.y = (int) twoY;
        }
        map.RemoveAllHighlights();
        var redColor = new Color(0.5f,0.2f,0,0.5f);
        foreach (var pos in firstSibling.Bounds.allPositionsWithin)
        {
            map.HighlightTile(new Vector3Int(pos.x, pos.y, 0), redColor);
        }
        foreach (var pos in secondSibling.Bounds.allPositionsWithin)
        {
            map.HighlightTile(new Vector3Int(pos.x, pos.y, 0), redColor);
        }
        var color = new Color(0f,0.8f,0,0.5f);
        map.ForTileInLine(corridorStart, corridorEnd, pos =>
        {            
            map.HighlightTile(pos, color);
        });
        //yield return new WaitUntil(() => nextStep);
        bool doorPlaced = false;
        map.ForTileInLine(corridorStart, corridorEnd, pos =>
        {            
            if (map.IsWallAt(pos))
            {
                corr.Tiles.Add(pos);
                map.RemoveWallAt(pos);
                if (!doorPlaced)
                {
                    doorPlaced = CanPlaceDoor(pos);
                    if (doorPlaced)
                    {
                        corr.HasDoor = true;
                        corr.DoorPosition = pos;
                        map.CreateDoor(pos.x, pos.y);
                    }
                }
            }
        });
        return corr;
    }

    private bool CanPlaceDoor(Vector3Int pos)
    {
        return true;
    }

    private void AddRooms()
    {
        foreach (var roomNode in leafs)
        {
            CarveOut(roomNode.Bounds);
        }
    }
    
    private void CarveOut(RectInt room)
    {
        foreach (var pos in room.allPositionsWithin)
        {
            var roomTile = new Vector3Int(pos.x, pos.y, 0);
            map.RemoveWallAt(roomTile);
        }
    }
    private void RecursivePartitioning(BSPRoomNode node)
    {
        node.PartitionNode();
        
        //yield return Highlight(node);
        
        //yield return new WaitForSeconds(.5f);
        
        if (NodeIsTooBig(node.Children.Item1))
        {
            RecursivePartitioning(node.Children.Item1);
        }
        else
        {
            leafs.Add(node.Children.Item1);
        }

        if (NodeIsTooBig(node.Children.Item2))
        {
            RecursivePartitioning(node.Children.Item2);
        }
        else
        {
            leafs.Add(node.Children.Item2);
        }
    }

    private IEnumerator Highlight(BSPRoomNode node)
    {
        map.RemoveAllHighlights();
        foreach (var pos in node.Bounds.allPositionsWithin)
        {
            var roomTile = new Vector3Int(pos.x, pos.y, 0);
            map.HighlightTile(roomTile, Color.green);
        }
        
        yield return new WaitForSeconds(.5f);
        
        foreach (var pos in node.Children.Item1.Bounds.allPositionsWithin)
        {
            var roomTile = new Vector3Int(pos.x, pos.y, 0);
            map.HighlightTile(roomTile, Color.blue);
        }

        foreach (var pos in node.Children.Item2.Bounds.allPositionsWithin)
        {
            var roomTile = new Vector3Int(pos.x, pos.y, 0);
            map.HighlightTile(roomTile, Color.red);
        }
    }
    

    private bool NodeIsTooBig(BSPRoomNode node)
    {
        return node.Bounds.width * node.Bounds.height > 25;
    }

}

public class Corridor
{
    public HashSet<Vector3Int> Tiles { get; set; } = new HashSet<Vector3Int>();
    public bool HasDoor { get; set; }
    public Vector3Int DoorPosition { get; set; }
    public Tuple<BSPRoomNode, BSPRoomNode> ConnectedRooms { get; set; }
}
public static class PointExtension
{
    public static Vector3Int ToVector3Int(this Point point)
    {
        return new Vector3Int((int) point.X, (int) point.Y, 0);
    }
}

public static class Vector2IntExtension
{
    public static Vector3Int ToVector3Int(this Vector2Int point)
    {
        return new Vector3Int((int) point.x, (int) point.y, 0);
    }
}

public static class IEnumerableExtension
{
    public static T RandomElement<T>(this IEnumerable<T> list)
    {
        var enumerable = list as T[] ?? list.ToArray();
        return enumerable.ElementAt(Random.Range(0, enumerable.Length));
    }
}

public static class ComparableExtension
{
    public static int CompareTo(this Vector3Int leftHand, Vector3Int other)
    {
        return leftHand.x.CompareTo(other.x) + leftHand.y.CompareTo(other.y);
    }
    
    public static int Compare<T>(this System.Collections.Generic.IComparer<T> comparer, Vector3Int leftHand, Vector3Int other)
    {
        if (leftHand == other)
        {
            return 0;
        }
        // cannot return zero now
        if (leftHand.x == other.x)
        {
            return leftHand.y.CompareTo(other.y);
        }
        
        return leftHand.x.CompareTo(other.x);
    }
}