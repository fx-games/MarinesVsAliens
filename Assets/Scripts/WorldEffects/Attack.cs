using System.Linq;
using DefaultNamespace;

namespace WorldEffects
{
    public class Attack : WorldEffect
    {
        public override void Execute()
        {
            if (_game.CurrentFaction == _game.PoVFaction)
            {
                ActingUnit.SetLabelText(ChanceToHitRoll.Sum().ToString());
            }
            
            foreach (var targetAffectedPair in AffectedTiles)
            {
                var target = targetAffectedPair.Key;
                var areaOfEffect = targetAffectedPair.Value;
        
                foreach (var aoePos in areaOfEffect)
                {
                    if (!_map.IsUnitOn(aoePos)) continue;
        
                    var hitUnit = (Unit)_map.GetUnitAt(aoePos);
            
                    hitUnit.TurnTowards(ActingUnit.Cell);

                    Options.CombatMechanics.ResolveAttack(ActiveWeapon, DamageRoll, ChanceToHitRoll, ActingUnit, hitUnit);            
                }
            }
            
            ActingUnit.Procs.OnHasAttacked();
            ActingUnit.AllowNoMoreActions();
            
            OnCompleted();
        }
    }
}