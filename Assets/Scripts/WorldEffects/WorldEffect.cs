using System.Collections.Generic;
using UnityEngine;

namespace WorldEffects
{
    public abstract class WorldEffect
    {
        protected BattleGame _game;
        protected Map _map;
        protected Mod _mod;
        public string Identifier { get; set; }
        public Unit ActingUnit { get; set; }
        public Vector3Int SourcePosition { get; set; }
        public List<Vector3Int> Targets { get; set; }
        public List<Vector3Int> AffectedTiles { get; set; }
        public string StringOne { get; set; }
        public float FloatOne { get; set; }
        public int IntegerOne { get; set; }
        public int IntegerTwo { get; set; }
        public int[] DamageRoll { get; set; }
        public int[] ChanceToHitRoll { get; set; }
        protected internal void Initialize(BattleGame game, Map map, Mod mod)
        {
            _game = game;
            _map = map;
            _mod = mod;
        }

        public abstract void Execute();
    }
}