using System;
using Default;
using UnityEngine;

namespace DefaultNamespace
{
    public class Raycaster
    {
        public Raycaster(IGridMap map)
        {
            this.map = map;
        }
        public bool UnitsAreBlocking { get; set; }
        public Unit ObservingUnit { get; set; }
        public Vector3Int FirstObstaclePosition { get; set; }

        private IGridMap map;

        public void Bresenham(Vector3Int start, Vector3Int end, Action<Vector3Int> positionCallback)
        {
            var dx = Mathf.Abs(end.x - start.x);
            var sx = start.x < end.x ? 1 : -1;            
            var dy = -Mathf.Abs(end.y - start.y);
            var sy = start.y < end.y ? 1 : -1;
            var x0 = start.x;
            var y0 = start.y;

            var err = dx + dy;

            while (true)
            {
                positionCallback(new Vector3Int(x0, y0, 0));
                if (x0 == end.x && y0 == end.y) break;
                var e2 = 2 * err;
                if (e2 >= dy)
                {
                    err += dy;
                    x0 += sx;
                }

                if (e2 <= dx)
                {
                    err += dx;
                    y0 += sy;
                }
            }
        }
        public bool LineCast(int x0, int y0, int x1, int y1)
        {
            int sx, sy, xnext, ynext, dx, dy, prevX, prevY;
            float dist;
    
            dx = x1 - x0;
            dy = y1 - y0;
    
            //determine which quadrant to we're calculating: we climb in these two directions
            sx = (x0 < x1) ? 1 : -1;
            sy = (y0 < y1) ? 1 : -1;
    
            xnext = prevX = x0;
            ynext = prevY = y0;
        
            //calculate length of line to cast (distance from start to final tile)
            dist = Mathf.Sqrt(dx * dx + dy * dy);
    
            while (xnext != x1 || ynext != y1) //essentially casting a ray of length radius: (radius^3)
            {
                if (!map.IsNeighborVisible(prevX, prevY, xnext, ynext))
                {
                    //tag_memorised(xnext, ynext); // make a note of the wall
                    FirstObstaclePosition = new Vector3Int(xnext, ynext, 0);
                    return false;
                }
                
                if (UnitsAreBlocking && !(xnext == x0 && ynext == y0) && map.IsOtherUnitOn(ObservingUnit, xnext, ynext))
                {
                    FirstObstaclePosition = new Vector3Int(xnext, ynext, 0);
                    return false;
                }
                
                prevX = xnext;
                prevY = ynext;
                // Line-to-point distance formula < 0.5 
                // decide on the direction for the next step; horiz, verti, diag
                if (Mathf.Abs(dy * (xnext - x0 + sx) - dx * (ynext - y0)) / dist < 0.5f)
                {
                    xnext += sx;
                }
                else if (Mathf.Abs(dy * (xnext - x0) - dx * (ynext - y0 + sy)) / dist < 0.5f)
                {
                    ynext += sy;
                }
                else
                {
                    xnext += sx;
                    ynext += sy;
                }
            }
            if (!map.IsNeighborVisible(prevX, prevY,xnext, ynext))
            {
                FirstObstaclePosition = new Vector3Int(xnext, ynext, 0);
                return false;
            }
            return true;
        }
    }
}