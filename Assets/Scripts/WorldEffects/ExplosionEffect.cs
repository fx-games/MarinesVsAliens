using System.Linq;
using DefaultNamespace;

namespace WorldEffects
{
    public class ExplosionEffect : WorldEffect
    {
        public override void Execute()
        {
            foreach (var targetAffectedPair in AffectedTiles)
            {
                var target = targetAffectedPair.Key;
                var areaOfEffect = targetAffectedPair.Value;
                
                foreach (var aoePos in areaOfEffect)
                {
                    // the following call will add damage to map tiles automatically
                    if (!_map.ExplosionReaches(target, aoePos, 0)) continue;
        
                    _game.CreateExplosionOnTile(aoePos);
                }
            }
            OnCompleted();
        }
    }
}