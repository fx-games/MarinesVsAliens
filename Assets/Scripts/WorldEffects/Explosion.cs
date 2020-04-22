using System.Linq;
using DefaultNamespace;

namespace WorldEffects
{
    public class Explosion : WorldEffect
    {
        public override void Execute()
        {
            foreach (var targetedPos in Targets)
            {
                var areaOfEffect = AffectedTiles;
        
                foreach (var aoePos in areaOfEffect)
                {
                    // the following call will add damage to map tiles automatically
                    if (!_map.ExplosionReaches(targetedPos, aoePos, DamageRoll.Sum())) continue;
        
                    _game.CreateExplosionOnTile(aoePos);
                
                    if (!_map.IsUnitOn(aoePos)) continue;
        
                    var hitUnit = (Unit)_map.GetUnitAt(aoePos);
            
                    hitUnit.TurnTowards(ActingUnit.Cell);
                
                    Options.CombatMechanics.ResolveAttack(ChanceToHitRoll, ActingUnit.Cell,ActingUnit, targetedPos, aoePos, hitUnit);            
                }
            }
            
            
            if (_game.CurrentFaction == _game.PoVFaction)
            {
                ActingUnit.SetLabelText(ChanceToHitRoll.Sum().ToString());
            }
            
            ActingUnit.AllowNoMoreActions();
        }
    }
}