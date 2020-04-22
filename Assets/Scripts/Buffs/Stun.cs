using UnityEngine;

namespace Buffs
{
    public class Stun : Buff
    {
        public override string DisplayName => "Stunned";

        public override void OnApply(Buff buff)
        {
            if (buff == this)
            {
                BuffedUnit.DisableAttack();
                BuffedUnit.DisableMovement();
            }
        }

        public override void OnNextTurn()
        {
            BuffedUnit.DisableAttack();
            BuffedUnit.DisableMovement();
        }
    }
}