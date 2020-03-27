using System.Collections.Generic;
using System.Drawing;

namespace GridPathFinder.Map
{
    public interface IGridMap
    {
        bool IsWalkable(int x, int y);
        int Width { get; }
        int Height { get; }
        bool Contains(int x, int y);
        List<Point> GetNeighbors(Unit currentUnit, Point smallestDistCell);
    }
}
