using System.Collections.Generic;
using System.Drawing;
using UnityEngine;

namespace GridPathFinder.Map
{
    public interface IGridMap
    {
        int Width { get; }
        int Height { get; }
        void GetNeighbors(List<Vector3Int> neighbors, Unit currentUnit, Vector3Int cellPosition);
    }
}
