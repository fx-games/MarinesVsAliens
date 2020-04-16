namespace DefaultNamespace.Units.UnitActions
{
    public static class WorldEffectFactory
    {
        public static BattleGame Game { get; set; }
        public static Map Map { get; set; }
        public static T CreateWorldEffect<T>() where T : WorldEffect, new()
        {
            T effect = new T
            {
                Identifier = nameof(T)
            };
            effect.Initialize(Game, Map);
            return effect;
        }
    }
}