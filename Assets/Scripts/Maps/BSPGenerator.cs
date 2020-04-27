using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DelaunayVoronoi;
using QuickGraph;
using QuickGraph.Algorithms;
using UnityEditor;
using UnityEngine;
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
        map.FillLayer("Walls");
        
        rootNode = new BSPRoomNode(1,1, _width-2, _height-2);
        
        RecursivePartitioning(rootNode);
        
        AddRooms();
        
        map.RemoveAllHighlights();
        connectedRooms.Clear();
        
        //yield return ConnectRoomsRecursive(rootNode);
        yield return ConnectRoomsAlternative(rootNode);
    }

    private IEnumerator ConnectRoomsAlternative(BSPRoomNode currentNode)
    {
        var delaunayTriangulator = new DelaunayTriangulator();
        var points = leafs.Select(r => new Point(r.Bounds.center.x, r.Bounds.center.y));
        var triangles = delaunayTriangulator.BowyerWatson(points);
        
        UndirectedGraph<Vector3Int, Edge<Vector3Int>> g = new UndirectedGraph<Vector3Int, Edge<Vector3Int>>(allowParallelEdges: false);
        Func<Edge<Vector3Int>, double> edgeWeights = edge => (edge.Source - edge.Target).magnitude;

        foreach (var triangle in triangles)
        {
            var start = new Vector3Int((int) triangle.Vertices[0].X, (int) triangle.Vertices[0].Y, 0);
            var mid = new Vector3Int((int) triangle.Vertices[1].X, (int) triangle.Vertices[1].Y, 0);
            var end = new Vector3Int((int) triangle.Vertices[2].X, (int) triangle.Vertices[2].Y, 0);
            
            var graphEdgeOne = new Edge<Vector3Int>(start, mid); 
            var graphEdgeTwo = new Edge<Vector3Int>(mid, end); 
            var graphEdgeThree = new Edge<Vector3Int>(end, start); 
            g.AddVerticesAndEdge(graphEdgeOne);
            g.AddVerticesAndEdge(graphEdgeTwo);
            g.AddVerticesAndEdge(graphEdgeThree);
        }

        IEnumerable<Edge<Vector3Int>> mst = g.MinimumSpanningTreePrim(edgeWeights);
        
        map.RemoveAllLines();
        foreach (var edge in mst)
        {
            DrawEdge(edge);
            var firstRoom = leafs.Find(r => r.Bounds.Contains(new Vector2Int(edge.Source.x, edge.Source.y)));
            var secondRoom = leafs.Find(r => r.Bounds.Contains(new Vector2Int(edge.Target.x, edge.Target.y)));
            if (firstRoom != null && secondRoom != null)
            {
                yield return ConnectTwoRooms(firstRoom, secondRoom);
            }
        }
        yield break;
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

    private void DrawEdge(Edge<Vector3Int> edge)
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

    private IEnumerator ConnectTwoRooms(BSPRoomNode firstSibling, BSPRoomNode secondSibling)
    {
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
        map.ForTileInLine(firstSibling.RoomCenter, secondSibling.RoomCenter, pos =>
        {            
            map.HighlightTile(pos, color);
        });
        //yield return new WaitUntil(() => nextStep);
        map.ForTileInLine(firstSibling.RoomCenter, secondSibling.RoomCenter, pos =>
        {            
            map.RemoveWallAt(pos);
        });
        yield return null;
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

public static class PointExtension
{
    public static Vector3Int ToVector3Int(this Point point)
    {
        return new Vector3Int((int) point.X, (int) point.Y, 0);
    }
}