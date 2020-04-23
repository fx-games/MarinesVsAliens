using System.Linq;
using DefaultNamespace;

namespace WorldEffects
{
    public class MeleeHitEffect : WorldEffect
    {
        public override void Execute()
        {
            foreach (var targetAffectedPair in AffectedTiles)
            {
                var target = targetAffectedPair.Key;
                var areaOfEffect = targetAffectedPair.Value;
                
                foreach (var aoePos in areaOfEffect)
                {
                    _game.CreateMeleeHitOnTile(aoePos);
                }
            }
            OnCompleted();
        }
    }
}