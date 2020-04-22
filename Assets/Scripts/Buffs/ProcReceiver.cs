using System.Collections.Generic;

namespace Buffs
{
    public class ProcReceiver
    {
        private List<Buff> buffs;

        public ProcReceiver(List<Buff> activeBuffs)
        {
            buffs = activeBuffs;
        }

        private void CheckForBuffEnd()
        {
            buffs.RemoveAll(b => b.EndCondition.IsFulfilled());
        }
        public void OnHasAttacked()
        {
            buffs.ForEach(b => b.EndCondition.AttacksAlive++);
            buffs.ForEach(b => b.OnHasAttacked());
            CheckForBuffEnd();
        }
        public void OnHit(Unit hitUnit, Weapon weapon)
        {
            buffs.ForEach(b => b.OnHit(hitUnit, weapon));
            CheckForBuffEnd();
        }

        public void OnBeingHit(Unit attackingUnit, Weapon weapon)
        {
            buffs.ForEach(b => b.OnBeingHit(attackingUnit, weapon));
            CheckForBuffEnd();
        }

        public void OnApply(Buff buff)
        {
            buffs.ForEach(b => b.OnApply(buff));
            CheckForBuffEnd();
        }

        public void OnNextTurn()
        {
            buffs.ForEach(b => b.EndCondition.TurnsAlive++);
            CheckForBuffEnd();
            buffs.ForEach(b => b.OnNextTurn());            
        }
        public void OnMiss(Unit hitUnit, Weapon weapon)
        {
            buffs.ForEach(b => b.OnMiss(hitUnit, weapon));
            CheckForBuffEnd();
        }
    }
}