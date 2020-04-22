using UnityEngine;

namespace Buffs
{
    public class Shredder : Buff
    {
        public override string DisplayName => "Shredder";

        public override void OnHit(Unit otherUnit, Weapon weapon)
        {
            Debug.Log("Shredder buff OnHit Proc: " + otherUnit);
            otherUnit.ChangeCurrentStat("Armor", -1);
        }
    }
}