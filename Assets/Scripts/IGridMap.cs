using System.Collections.Generic;
using System.Drawing;

namespace GridPathFinder.Map
{
    public interface IGridMap
    {
        int Width { get; }
        int Height { get; }
        List<Point> GetNeighbors(Unit currentUnit, Point cellPosition);
    }
}
