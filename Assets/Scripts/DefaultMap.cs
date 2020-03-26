using System;
using System.Collections.Generic;
using System.Drawing;

namespace GridPathFinder.Map
{
    public class DefaultMap : IGridMap
    {
        private readonly int mWidth;
        private readonly int mHeight;
        private bool[,] mMap;
        private List<Point> mRelativeNeighbors;

        public DefaultMap(int width, int height)
        {
            mWidth = width;
            mHeight = height;
            mMap = new bool[height,width];
            for (int y = 0; y < mHeight; y++)
            {
                for (int x = 0; x < mWidth; x++)
                {
                    mMap[y, x] = true;
                }
            }
            mRelativeNeighbors = new List<Point>() { new Point(1, 0), new Point(-1, 0), new Point(0, 1), new Point(0, -1), new Point(1, -1), new Point(-1, -1), new Point(-1, 1), new Point(1, 1) };
        }

        public List<Point> GetNeighbors(Point smallestDistCell)
        {
            List<Point> result = new List<Point>();
            foreach (Point relativeNeighbor in mRelativeNeighbors)
            {
                Point absoluteNeighbor = new Point(smallestDistCell.X + relativeNeighbor.X, smallestDistCell.Y + relativeNeighbor.Y);
                if (Contains(absoluteNeighbor.X, absoluteNeighbor.Y) && IsWalkable(absoluteNeighbor.X, absoluteNeighbor.Y))
                    result.Add(absoluteNeighbor);
            }
            return result;
        }
        public void CreateWall(int x, int y)
        {
            mMap[y, x] = false;
        }

        public bool IsWalkable(int x, int y)
        {
            return mMap[y, x];
        }

        public int Width
        {
            get { return mWidth; }
        }

        public int Height
        {
            get { return mHeight; }
        }

        public bool Contains(int x, int y)
        {
            return x >= 0 && x < mWidth && y >= 0 && y < mHeight;
        }
    }
}
