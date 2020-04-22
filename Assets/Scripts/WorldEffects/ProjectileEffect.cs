using System.Linq;
using DefaultNamespace;
using UnityEngine;

namespace WorldEffects
{
    public class ProjectileEffect : WorldEffect
    {
        public int SelectedSprite { get; set; }
        public int Count { get; set; } = 3;
        public float Speed { get; set; }
        public bool WithDeviation { get; set; } = true;

        public override void Execute()
        {
            SelectedSprite = IntegerOne;
            Count = IntegerTwo;
            Speed = FloatOne;
            float timer = 0.0f;
            foreach (var targetedPos in Targets)
            {
                for (int i = 0; i < Count; i++)
                {
                    _game.Schedule(() => { FireProjectileAt(targetedPos); }, timer);
                    timer += Random.Range(0.001f, 0.201f);
                }
            }
        }


        private void FireProjectileAt(Vector3Int targetedPos)
        {
            var targetPos = _map.CellToWorld(targetedPos);
            if (WithDeviation)
            {
                targetPos += new Vector3(Random.Range(-0.5f, 0.5f), Random.Range(-0.5f, 0.5f), 0);
            }

            _game.CreateProjectile(_map.CellToWorld(SourcePosition), targetPos, Speed, SelectedSprite);
        }

        
    }
}