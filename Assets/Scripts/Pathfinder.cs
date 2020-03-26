using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using C5;
using GridPathFinder.Map;
using UnityEngine;

namespace GridPathFinder
{

    public class DijkstraNode : IComparable<DijkstraNode>
    {
        public DijkstraNode(Point cell)
        {
            GridCoordinate = cell;
            TentativeDistance = float.PositiveInfinity;
        }

        public Point GridCoordinate { get; set; }
        public float TentativeDistance { get; set; }
        public float EstimatedCostPath { get; set; }
        public DijkstraNode Parent { get; set; }
        public IPriorityQueueHandle<DijkstraNode> PriorityQueueNode { get; set; }
        
        public int CompareTo(DijkstraNode other)
        {
            return EstimatedCostPath.CompareTo(other.EstimatedCostPath);
        }
    }

    public class Pathfinder
    {
        private readonly IGridMap mMap;

        private readonly DijkstraNode[,] mNodeMap;
        
        private readonly float mCardinalCost;
        private readonly float mDiagonalCost;
        private IntervalHeap<DijkstraNode> mOpenList;
        private int mMapWidth;
        private int mMapHeight;

        public System.Collections.Generic.HashSet<Point> ClosedList { get; private set; }
        private System.Collections.Generic.HashSet<Point> mStartNodes;
        private System.Collections.Generic.HashSet<Point> mGoalNodes;
        public System.Collections.Generic.HashSet<Point> ReachableList { get; private set; }

        public float MaxCost { get; set; }

        public float CardinalCost
        {
            get { return mCardinalCost; }
        }

        public Pathfinder(IGridMap map, float cardinalCost, float diagonalCost)
        {
            mMap = map;
            mCardinalCost = cardinalCost;
            mDiagonalCost = diagonalCost;
            mMapWidth = map.Width;
            mMapHeight = map.Height;
            
            MaxCost = -1;
            mNodeMap = new DijkstraNode[mMapHeight, mMapWidth];

            mOpenList = new IntervalHeap<DijkstraNode>();
            
            ClosedList = new System.Collections.Generic.HashSet<Point>();
        }

        public void Dijkstra(System.Collections.Generic.HashSet<Point> startNodes, System.Collections.Generic.HashSet<Point> goalNodes)
        {
            ReachableList = new System.Collections.Generic.HashSet<Point>();
            mStartNodes = startNodes;
            mGoalNodes = goalNodes;
            InitDijkstra();
            while (!EndCondition())
            {
                //PrintOpenList();
                DijkstraNode smallestDistCell = mOpenList.DeleteMin();
                if (MaxCost - mDiagonalCost > 0 && smallestDistCell.TentativeDistance > MaxCost - mDiagonalCost)
                    continue;

                //Console.WriteLine("Expanding Node: " + smallestDistCell.GridCoordinate + " (" + smallestDistCell.TentativeDistance + ")");
                List<Point> neighbors = mMap.GetNeighbors(smallestDistCell.GridCoordinate);
                foreach (Point neighbor in neighbors)
                {
                    if (!ClosedList.Contains(neighbor))
                    {
                        float newTentativeDistance = mNodeMap[smallestDistCell.GridCoordinate.Y, smallestDistCell.GridCoordinate.X].TentativeDistance + GetArcCost(smallestDistCell.GridCoordinate, neighbor);

                        float neighborToGoalEstimate = GetHeuristic(neighbor);

                        float estimatedCost = newTentativeDistance + neighborToGoalEstimate;

                        if (estimatedCost < mNodeMap[neighbor.Y, neighbor.X].TentativeDistance)
                        {

                            //Console.WriteLine("Updating Dist of Node: " + neighbor);// + " re-adding to OpenList");
                            mNodeMap[neighbor.Y, neighbor.X].TentativeDistance = newTentativeDistance;
                            mNodeMap[neighbor.Y, neighbor.X].EstimatedCostPath = estimatedCost;
                            mNodeMap[neighbor.Y, neighbor.X].Parent = smallestDistCell;
                            mOpenList.Replace(mNodeMap[neighbor.Y, neighbor.X].PriorityQueueNode, mNodeMap[neighbor.Y, neighbor.X]);
                            ReachableList.Add(neighbor);
                        }
                    }
                }
                ClosedList.Add(smallestDistCell.GridCoordinate);
                //PrintDistMap();
                //Console.ReadLine();
            }
        }

        private float GetHeuristic(Point currentNode)
        {
            if (mGoalNodes.Contains(currentNode)) return 0f;
            if (mGoalNodes.Count == 0) return 0f;

            float minDist = float.PositiveInfinity;
            foreach (Point goalNode in mGoalNodes)
            {
                float dist = GetEuclideanDistance(currentNode, goalNode);
                if (dist < minDist)
                    minDist = dist;
            }
            return minDist;
        }

        private void PrintOpenList()
        {
            Debug.Log("OpenList: ");
            foreach (DijkstraNode dijkstraNode in mOpenList)
            {
                Debug.Log(dijkstraNode.GridCoordinate + ": " + dijkstraNode.TentativeDistance + ", ");
            }
            Debug.Log("\n");
        }

        public void PrintDistMap()
        {
            string output = "";
            for (int y = 0; y < mMapHeight; y++)
            {
                for (int x = 0; x < mMapWidth; x++)
                {
                    Point point = new Point(x,y);
                    if (mStartNodes.Contains(point))
                        output += "*";
                    else if (mGoalNodes.Contains(point))
                        output +=("!");
                    else if (mNodeMap[y, x] == null || !mMap.IsWalkable(x, y))
                        output +=("#");
                    else if (float.IsPositiveInfinity(mNodeMap[y, x].TentativeDistance))
                        output +=(".");
                    else if ((int) mNodeMap[y, x].TentativeDistance >= 10)
                        output +=("+");
                    else
                        output +=((int) mNodeMap[y, x].TentativeDistance);
                }
                output += "\n";
            }
            output += "\n";
            
            Debug.Log(output);
        }

        public float GetShortestPathDistance(Point goalNode)
        {
            return mNodeMap[goalNode.Y, goalNode.X].TentativeDistance;
        }
        private float GetShortestPathDistanceTest(Point goalNode)
        {
            float dist = 0f;
            List<Point> shortestPath = GetShortestPath(goalNode);
            for (int index = 1; index < shortestPath.Count; index++)
            {
                Point firstPoint = shortestPath[index - 1];
                Point secondPoint = shortestPath[index];
                dist += GetArcCost(firstPoint, secondPoint);
            }
            return dist;
        }
        public List<Point> GetShortestPath(Point goalNode)
        {
            List<Point> path = new List<Point>();
            DijkstraNode currentCell = mNodeMap[goalNode.Y, goalNode.X];
            
            if (currentCell.Parent == null) return path;    // goal node is unreachable, so return an empty path

            while (currentCell != null)
            {
                path.Add(currentCell.GridCoordinate);
                currentCell = mNodeMap[currentCell.GridCoordinate.Y, currentCell.GridCoordinate.X].Parent;
            }
            path.Reverse();
            return path;
        }

        private bool EndCondition()
        {
            bool settledAll = GetSettledAll();
            return settledAll || mOpenList.Count == 0;
        }

        private bool GetSettledAll()
        {
            if (mGoalNodes.Count == 0) return false;
            foreach (Point goalNode in mGoalNodes)
            {
                if (!ClosedList.Contains(goalNode))
                    return false;
            }
            return true;
        }

        private float GetArcCost(Point pointOne, Point pointTwo)
        {
            int diff = Math.Abs(pointOne.X - pointTwo.X) + Math.Abs(pointOne.Y - pointTwo.Y);
            if (diff == 1) return mCardinalCost;
            if (diff == 2) return mDiagonalCost;
            return float.PositiveInfinity;
        }

        private float GetEuclideanDistance(Point pointOne, Point pointTwo)
        {
            return (float) Math.Sqrt(Math.Pow((pointOne.X - pointTwo.X), 2) + Math.Pow(pointOne.Y - pointTwo.Y, 2));
        }

        

        private void InitDijkstra()
        {
            mOpenList = new IntervalHeap<DijkstraNode>();

            ClosedList = new System.Collections.Generic.HashSet<Point>();

            for (int y = 0; y < mMapHeight; y++)
            {
                for (int x = 0; x < mMapWidth; x++)
                {
                    Point currentCell = new Point(x, y);
                    //if (mMap.Contains(currentCell.X, currentCell.Y) && mMap.IsWalkable(currentCell.X, currentCell.Y))
                    {
                        mNodeMap[y, x] = new DijkstraNode(currentCell);
                        mNodeMap[y, x].Parent = null;
                        if (mStartNodes.Contains(currentCell))
                        {
                            mNodeMap[y, x].TentativeDistance = 0;
                            mNodeMap[y, x].EstimatedCostPath = 0;
                        }
                        else
                        {
                            mNodeMap[y, x].TentativeDistance = float.PositiveInfinity;
                            mNodeMap[y, x].EstimatedCostPath = float.PositiveInfinity;
                        }
                        IPriorityQueueHandle<DijkstraNode> priorityQueueNode = null;
                        mOpenList.Add(ref priorityQueueNode, mNodeMap[y, x]);
                        mNodeMap[y, x].PriorityQueueNode = priorityQueueNode;
                    }
                }
            }
        }
    }
}
