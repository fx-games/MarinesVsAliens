using System;
using System.Collections.Generic;
using C5;
using Default;
using UnityEngine;

namespace GridPathFinder
{

    public class DijkstraNode : IComparable<DijkstraNode>
    {
        public DijkstraNode(Vector3Int cell)
        {
            GridCoordinate = cell;
            TentativeDistance = float.PositiveInfinity;
        }
        public Vector3Int GridCoordinate { get; set; }
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
        
        public float MaxReachableCost { get; set; } = -1f;

        private readonly float mCardinalCost;
        private readonly float mDiagonalCost;
        private IntervalHeap<DijkstraNode> mOpenList;
        private int mMapWidth;
        private int mMapHeight;

        public System.Collections.Generic.HashSet<Vector3Int> ClosedList { get; private set; }
        private System.Collections.Generic.HashSet<Vector3Int> mStartNodes;
        private System.Collections.Generic.HashSet<Vector3Int> mGoalNodes;
        public System.Collections.Generic.HashSet<Vector3Int> ReachableList { get; private set; } = new System.Collections.Generic.HashSet<Vector3Int>();

        public float MaxCost { get; set; }
        public Unit CurrentUnit { get; set; }
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
            
            ClosedList = new System.Collections.Generic.HashSet<Vector3Int>();
        }

        public void Dijkstra(System.Collections.Generic.HashSet<Vector3Int> startNodes, System.Collections.Generic.HashSet<Vector3Int> goalNodes)
        {
            ReachableList.Clear();
            mStartNodes = startNodes;
            mGoalNodes = goalNodes;
            List<Vector3Int> neighbors = new List<Vector3Int>();
            InitDijkstra();
            while (!EndCondition())
            {
                //PrintOpenList();
                DijkstraNode smallestDistCell = mOpenList.DeleteMin();
                if (MaxCost - mDiagonalCost > 0 && smallestDistCell.TentativeDistance > MaxCost - mDiagonalCost)
                    continue;

                //Console.WriteLine("Expanding Node: " + smallestDistCell.GridCoordinate + " (" + smallestDistCell.TentativeDistance + ")");
                neighbors.Clear();
                mMap.GetNeighbors(neighbors, CurrentUnit, smallestDistCell.GridCoordinate);
                foreach (Vector3Int neighbor in neighbors)
                {
                    if (!ClosedList.Contains(neighbor))
                    {
                        float newTentativeDistance = 
                            mNodeMap[smallestDistCell.GridCoordinate.y, smallestDistCell.GridCoordinate.x].TentativeDistance 
                            + GetArcCost(smallestDistCell.GridCoordinate, neighbor);

                        float neighborToGoalEstimate = GetHeuristic(neighbor);

                        float estimatedCost = newTentativeDistance + neighborToGoalEstimate;

                        if (estimatedCost < mNodeMap[neighbor.y, neighbor.x].TentativeDistance)
                        {

                            //Console.WriteLine("Updating Dist of Node: " + neighbor);// + " re-adding to OpenList");
                            mNodeMap[neighbor.y, neighbor.x].TentativeDistance = newTentativeDistance;
                            mNodeMap[neighbor.y, neighbor.x].EstimatedCostPath = estimatedCost;
                            mNodeMap[neighbor.y, neighbor.x].Parent = smallestDistCell;
                            mOpenList.Replace(mNodeMap[neighbor.y, neighbor.x].PriorityQueueNode, mNodeMap[neighbor.y, neighbor.x]);
                            if (MaxReachableCost > 0 && newTentativeDistance < MaxReachableCost)
                            {
                                ReachableList.Add(neighbor);
                            }
                        }
                    }
                }
                ClosedList.Add(smallestDistCell.GridCoordinate);
                //PrintDistMap();
                //Console.ReadLine();
            }
        }

        private float GetHeuristic(Vector3Int currentNode)
        {
            if (mGoalNodes.Contains(currentNode)) return 0f;
            if (mGoalNodes.Count == 0) return 0f;

            float minDist = float.PositiveInfinity;
            foreach (Vector3Int goalNode in mGoalNodes)
            {
                float dist = (currentNode - goalNode).magnitude;
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
                    Vector3Int pos = new Vector3Int(x,y,0);
                    if (mStartNodes.Contains(pos))
                        output += "*";
                    else if (mGoalNodes.Contains(pos))
                        output +=("!");
                    else if (mNodeMap[y, x] == null)
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

        public float GetShortestPathDistance(Vector3Int goalNode)
        {
            return mNodeMap[goalNode.y, goalNode.x].TentativeDistance;
        }
        private float GetShortestPathDistanceTest(Vector3Int goalNode)
        {
            float dist = 0f;
            List<Vector3Int> shortestPath = GetShortestPath(goalNode);
            for (int index = 1; index < shortestPath.Count; index++)
            {
                Vector3Int firstVector3Int = shortestPath[index - 1];
                Vector3Int secondVector3Int = shortestPath[index];
                dist += GetArcCost(firstVector3Int, secondVector3Int);
            }
            return dist;
        }
        public List<Vector3Int> GetShortestPath(Vector3Int goalNode)
        {
            List<Vector3Int> path = new List<Vector3Int>();
            DijkstraNode currentCell = mNodeMap[goalNode.y, goalNode.x];
            
            if (currentCell.Parent == null) return path;    // goal node is unreachable, so return an empty path

            while (currentCell != null)
            {
                path.Add(currentCell.GridCoordinate);
                currentCell = mNodeMap[currentCell.GridCoordinate.y, currentCell.GridCoordinate.x].Parent;
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
            foreach (Vector3Int goalNode in mGoalNodes)
            {
                if (!ClosedList.Contains(goalNode))
                    return false;
            }
            return true;
        }

        private float GetArcCost(Vector3Int Vector3IntOne, Vector3Int Vector3IntTwo)
        {
            int diff = Math.Abs(Vector3IntOne.x - Vector3IntTwo.x) + Math.Abs(Vector3IntOne.y - Vector3IntTwo.y);
            if (diff == 1) return mCardinalCost;
            if (diff == 2) return mDiagonalCost;
            return float.PositiveInfinity;
        }

        private void InitDijkstra()
        {
            mOpenList = new IntervalHeap<DijkstraNode>();

            ClosedList = new System.Collections.Generic.HashSet<Vector3Int>();

            for (int y = 0; y < mMapHeight; y++)
            {
                for (int x = 0; x < mMapWidth; x++)
                {
                    Vector3Int currentCell = new Vector3Int(x, y, 0);
                    //if (mMap.Contains(currentCell.x, currentCell.y) && mMap.IsWalkable(currentCell.x, currentCell.y))
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
