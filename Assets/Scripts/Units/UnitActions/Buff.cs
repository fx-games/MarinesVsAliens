using System.Collections.Generic;

namespace DefaultNamespace.Units.UnitActions
{
    public enum ProcType
    {
        OnAttack,
        OnHit,
        OnDamage,
        OnNewTurn,
        OnDeath,
        OnBeingAttacked,
        OnBeingDamaged,
        OnMove
    }
    public abstract class Buff
    {
        public List<UnitStat> Modifiers { get; set; }
        public abstract bool IsEffectOver();
        public abstract bool ApplyProcs(ProcType type);
    }
}