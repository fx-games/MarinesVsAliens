using System.Collections.Generic;

namespace Buffs
{
    public class Buff
    {
        public Unit BuffedUnit { get; set; }
        public Dictionary<string, UnitStat> Modifiers { get; set; } = new Dictionary<string, UnitStat>();
        public BuffEndCondition EndCondition { get; set; } = new UnlimitedCondition();
        public virtual string DisplayName => "StatBuff";

        public virtual void OnHasAttacked(){}
        public virtual void OnHit(Unit hitUnit, Weapon weapon){}
        public virtual void OnMiss(Unit hitUnit, Weapon weapon){}

        public virtual void OnBeingHit(Unit attackingUnit, Weapon weapon){}
        public virtual void OnApply(Buff buff) {}

        public virtual void OnNextTurn() {}
        
        public int OnCurrentStatChange(string statName, int change)
        {
            if (Modifiers.ContainsKey(statName))
            {
                int absorbed = Modifiers[statName].Change(change);
                var rest = change - absorbed;
                return rest;
            }

            return change;
        }

        public void SetMaxStatModifier(string statName, int change)
        {
            Modifiers.Add(statName, new UnitStat(statName, change));
        }

    }

    public abstract class BuffEndCondition
    {
        public abstract bool IsFulfilled();
        public int TurnsAlive { get; set; }
        public int AttacksAlive { get; set; }
    }
}