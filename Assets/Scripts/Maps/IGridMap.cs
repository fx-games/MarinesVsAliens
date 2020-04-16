using System.Collections.Generic;
using System.Drawing;
using UnityEngine;

namespace Default
{
    public interface IGridMap
    {
        int Width { get; }
        int Height { get; }
        void GetNeighbors(List<Vector3Int> neighbors, Unit currentUnit, Vector3Int cellPosition);
        bool IsNeighborVisible(int prevX, int prevY, int nextX, int nextY);
        bool IsOtherUnitOn(Unit observingUnit, int x, int y);
    }
}
