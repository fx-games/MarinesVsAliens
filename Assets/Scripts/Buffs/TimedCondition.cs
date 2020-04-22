namespace Buffs
{
    public class TimedCondition : BuffEndCondition
    {
        private readonly int _turnsToLive;

        public TimedCondition(int turnsToLive)
        {
            _turnsToLive = turnsToLive;
        }

        public override bool IsFulfilled()
        {
            return _turnsToLive == TurnsAlive;
        }
    }
}