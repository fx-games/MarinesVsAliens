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
                BuffedUnit.DisableActions();
            }
        }

        public override void OnNextTurn()
        {
            BuffedUnit.DisableActions();
        }
    }
}