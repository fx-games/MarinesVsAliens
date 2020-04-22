namespace Buffs
{
    public class AttackCondition : BuffEndCondition
    {
        private readonly int _attacksToLive;

        public AttackCondition(int attacksToLive)
        {
            _attacksToLive = attacksToLive;
        }

        public override bool IsFulfilled()
        {
            return _attacksToLive == AttacksAlive;
        }
    }
}