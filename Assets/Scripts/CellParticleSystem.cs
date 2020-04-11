using System.Collections.Generic;
using System.Linq;
using System.Transactions;
using UnityEngine;

namespace DefaultNamespace
{
    public class WorldParticleBase
    {
        public Vector3 Position { get; set; }
        public Vector3 Velocity { get; set; }
        public int Lifetime { get; set; }
        public Color Color { get; set; }

        public virtual void Update()
        {
            Position += Velocity;
            Lifetime -= 1;
        }
    }
    
    public class CircleParticle : WorldParticleBase
    {
        public float Radius { get; set; }
        public float DeltaRadius { get; set; }
        public override void Update()
        {
            base.Update();
            Radius += DeltaRadius;
        }
    }
    
    public class CellParticleSystem
    {
        public Dictionary<Vector3Int, Color> TileColoring { get; } = new Dictionary<Vector3Int, Color>();
        public List<Vector3Int> TilesAffectedInLastFrame { get; private set; } = new List<Vector3Int>();
        private List<WorldParticleBase> activeParticles = new List<WorldParticleBase>();
        private Map map;

        public CellParticleSystem(Map map)
        {
            this.map = map;
        }

        public void AddParticle(WorldParticleBase particle)
        {
            activeParticles.Add(particle);
        }
        
        public void Update()
        {
            TilesAffectedInLastFrame = TileColoring.Keys.ToList();
            TileColoring.Clear();

            for (var index = activeParticles.Count - 1; index >= 0; index--)
            {
                var particle = activeParticles[index];
                
                if (particle.Lifetime <= 0)
                {
                    activeParticles.RemoveAt(index);
                }
                else
                {
                    Render(particle);
                }
                
                particle.Update();
            }
            
            
        }

        private void Render(WorldParticleBase particle)
        {
            if (particle is CircleParticle)
            {
                var circleParticle = (CircleParticle) particle;                    
                var mapPos = map.WorldToCell(particle.Position);
                RenderCircle2(mapPos.x, mapPos.y, Mathf.RoundToInt(circleParticle.Radius), circleParticle.Color);
            }
        }

        private void RenderCircle2(int centerX, int centerY, int radius, Color color)
        {
            int d = (5 - radius * 4) / 4;
            int x = 0;
            int y = radius;
 
            do
            {
                // ensure index is in range before setting (depends on your image implementation)
                // in this case we check if the pixel location is within the bounds of the image before setting the pixel
                TileColoring.TryAdd(new Vector3Int(centerX + x, centerY + y, 0), color);
                TileColoring.TryAdd(new Vector3Int(centerX + x, centerY - y, 0), color);
                TileColoring.TryAdd(new Vector3Int(centerX - x, centerY + y, 0), color);
                TileColoring.TryAdd(new Vector3Int(centerX - x, centerY - y, 0), color);
                
                TileColoring.TryAdd(new Vector3Int(centerX + y, centerY + x, 0), color);
                TileColoring.TryAdd(new Vector3Int(centerX + y, centerY - x, 0), color);
                TileColoring.TryAdd(new Vector3Int(centerX - y, centerY + x, 0), color);
                TileColoring.TryAdd(new Vector3Int(centerX - y, centerY - x, 0), color);
                
                if (d < 0)
                {
                    d += 2 * x + 1;
                }
                else
                {
                    d += 2 * (x - y) + 1;
                    y--;
                }
                x++;
            } while (x <= y);
        }
        private void RenderCircle(int xCenter, int yCenter, int r, Color color) 
        { 
            int x = r, y = 0; 
	
            // Printing the initial point on the 
            // axes after translation 
            TileColoring.TryAdd(new Vector3Int(x + xCenter,y + yCenter,0), color); 
	
            // When radius is zero only a single 
            // point will be printed 
            if (r > 0)
            {
                TileColoring.TryAdd(new Vector3Int(x + xCenter, -y + yCenter, 0), color);
                TileColoring.TryAdd(new Vector3Int(y + xCenter, x + yCenter, 0), color);
                TileColoring.TryAdd(new Vector3Int(-y + xCenter, x + yCenter, 0), color);
            } 
	
            // Initialising the value of P 
            int p = 1 - r; 
            while (x > y) 
            { 
			
                y++; 
		
                // Mid-point is inside or on the perimeter 
                if (p <= 0) 
                    p = p + 2 * y + 1; 
		
                // Mid-point is outside the perimeter 
                else
                { 
                    x--; 
                    p = p + 2 * y - 2 * x + 1; 
                } 
		
                // All the perimeter points have already 
                // been printed 
                if (x < y) 
                    break; 
		
                // Printing the generated point and its 
                // reflection in the other octants after 
                // translation 
                TileColoring.TryAdd(new Vector3Int(x + xCenter, y + yCenter, 0), color);
                TileColoring.TryAdd(new Vector3Int(-x + xCenter, y + yCenter, 0), color);
                TileColoring.TryAdd(new Vector3Int(x + xCenter, -y + yCenter, 0), color);
                TileColoring.TryAdd(new Vector3Int(-x + xCenter, -y + yCenter, 0), color);
		
                // If the generated point is on the 
                // line x = y then the perimeter points 
                // have already been printed 
                if (x != y) 
                { 
                    TileColoring.TryAdd(new Vector3Int(y + xCenter, x + yCenter, 0), color);
                    TileColoring.TryAdd(new Vector3Int(-y + xCenter, x + yCenter, 0), color);
                    TileColoring.TryAdd(new Vector3Int(y + xCenter, -x + yCenter, 0), color);
                    TileColoring.TryAdd(new Vector3Int(-y + xCenter, -x + yCenter, 0), color);
                }	
            }
        } 
    }

    public static class DictionaryExtension
    {
        public static void TryAdd<T1, T2>(this Dictionary<T1, T2> dict, T1 key, T2 value)
        {
            if (dict.ContainsKey(key)) return;
            dict.Add(key, value);
        }
    }
}