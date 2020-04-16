namespace DefaultNamespace.Units.UnitActions
{
    public abstract class WorldEffect
    {
        protected BattleGame _game;
        protected Map _map;
        public string Identifier { get; set; }
        public Unit Source { get; set; }
        protected WorldEffect(){}
        protected internal void Initialize(BattleGame game, Map map)
        {
            _game = game;
            _map = map;
        }

        public abstract void Execute();
    }
}