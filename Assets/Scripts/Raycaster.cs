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
        
        private IGridMap map;

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
                    return false;
                }
                
                if (UnitsAreBlocking && !(xnext == x0 && ynext == y0) && map.IsOtherUnitOn(ObservingUnit, xnext, ynext))
                {
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
                //tag_memorised(xnext, ynext); // make a note of the wall
                return false;
            }
            return true;
        }
    }
}