using System;
using System.Collections.Generic;

namespace DefaultNamespace.Units.UnitActions
{
    public static class UnitActionFactory
    {
        private static Dictionary<string, List<Lazy<WorldEffect>>> effectMap = new Dictionary<string, List<Lazy<WorldEffect>>>()
        {
            {"OpenDoor", new List<Lazy<WorldEffect>>()
            {
                new Lazy<WorldEffect>(WorldEffectFactory.CreateWorldEffect<OpenDoorEffect>)
            }}
        };
        public static BattleGame Game { get; set; }
        public static Map Map { get; set; }

        public static T CreateUnitAction<T>(string identifier, Unit unit) where T : UnitAction, new()
        {
            T action = new T
            {
                ActingUnit = unit,
                Turn = Game.CurrentTurn,
                ActingFaction = Game.CurrentFaction,
                Identifier = identifier,
                EffectsOnTheWorld = effectMap[identifier]
            };
            return action;
        }
    }
}