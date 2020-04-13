using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DefaultNamespace
{
    public class WorldParticleBase
    {
        protected Map map;

        public WorldParticleBase(Map map)
        {
            this.map = map;
        }

        public Vector3 Position { get; set; }
        public Vector3 Velocity { get; set; }

        public float Lifetime { get; set; }

        private float timeAlive = 0;

        public virtual float CompletionPercent => timeAlive / Lifetime;
        public Color SolidColor { get; set; }
        public Gradient Gradient { get; set; }

        public Color Color => Gradient?.Evaluate(CompletionPercent) ?? SolidColor;

        public virtual bool IsDead => timeAlive >= Lifetime;

        public virtual void Update()
        {
            Position += Velocity * Time.deltaTime;
            timeAlive += Time.deltaTime;
        }
    }

    public class StartEndParticle : WorldParticleBase
    {
        private Vector3 _endPosition;
        private Vector3 directionVector;
        private float startingDistance;
        public float Speed { private get; set; }
        public override float CompletionPercent =>  (startingDistance - (_endPosition - Position).magnitude) / startingDistance;

        // we know: start and endpoint and lifetime
        // calculate velocity
        public Vector3 EndPosition
        {
            set
            {
                _endPosition = value;
                directionVector = (_endPosition - Position);
                startingDistance = directionVector.magnitude;
                directionVector.Normalize();
                Velocity = directionVector * Speed;
            }
        }

        public override bool IsDead => map.AreWorldPositionsOnSameTile(Position, _endPosition);

        public StartEndParticle(Map map) : base(map)
        {
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

        public CircleParticle(Map map) : base(map)
        {
        }
    }
    
    public class LineParticle : StartEndParticle
    {
        private float _length;
        private float startLength;

        public float Length
        {
            get => _length;
            set => _length = startLength = value;
        }

        public float EndLength { get; set; }

        public Vector3 Direction { get; set; }

        public override void Update()
        {
            base.Update();
            _length = Mathf.Lerp(startLength, EndLength, CompletionPercent);
        }

        public LineParticle(Map map) : base(map)
        {
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
                
                if (particle.IsDead)
                {
                    Render(particle);
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
            else if (particle is LineParticle)
            {
                var lineParticle = (LineParticle) particle;
                RenderLine(particle.Position, lineParticle.Direction, lineParticle.Length, lineParticle.Color);
            }
            else
            {
                TileColoring.TryAdd(map.WorldToCell(particle.Position), particle.Color);
            }
        }
        
        private void RenderLine(Vector3 position, Vector3 direction, float length, Color color)
        {
            int sx, sy, xnext, ynext, dx, dy;
            float dist;
            var start = map.WorldToCell(position);
            int x0 = start.x;
            int y0 = start.y;
            var endInWorld = position + (direction.normalized * length);
            var end = map.WorldToCell(endInWorld);
            int x1 = end.x;
            int y1 = end.y;
            
            dx = x1 - x0;
            dy = y1 - y0;
    
            //determine which quadrant to we're calculating: we climb in these two directions
            sx = (x0 < x1) ? 1 : -1;
            sy = (y0 < y1) ? 1 : -1;

            int prevX;
            int prevY;
            xnext = prevX = x0;
            ynext = prevY = y0;
        
            //calculate length of line to cast (distance from start to final tile)
            dist = Mathf.Sqrt(dx * dx + dy * dy);
    
            while (xnext != x1 || ynext != y1) //essentially casting a ray of length radius: (radius^3)
            {
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
                
                TileColoring.TryAdd(new Vector3Int(prevX, prevY,0), color);
            }
            
            TileColoring.TryAdd(new Vector3Int(xnext, ynext,0), color);
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