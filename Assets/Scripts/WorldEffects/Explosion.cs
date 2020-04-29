using System.Linq;
using DefaultNamespace;
using UnityEngine;

namespace WorldEffects
{
    public class Explosion : WorldEffect
    {
        public override void Execute()
        {
            if (_game.CurrentPlayer == _game.PoVPlayer)
            {
                ActingUnit.SetLabelText(ChanceToHitRoll.Sum().ToString());
            }

            foreach (var targetAffectedPair in AffectedTiles)
            {
                var target = targetAffectedPair.Key;
                var areaOfEffect = targetAffectedPair.Value;
                 
                foreach (var aoePos in areaOfEffect)
                {
                    // the following call will add damage to map tiles automatically
                    if (!_map.ExplosionReaches(target, aoePos, DamageRoll.Sum())) continue;
        
                    if (!_map.IsUnitOn(aoePos)) continue;
        
                    var hitUnit = (Unit)_map.GetUnitAt(aoePos);
            
                    hitUnit.TurnTowards(ActingUnit.Cell);
                
                    Debug.Log("Explosion hit " + hitUnit);
                    
                    Options.CombatMechanics.ResolveExplosion(ActiveWeapon, DamageRoll, ActingUnit, hitUnit);            
                }
            }
            
            OnCompleted();
        }
    }
}