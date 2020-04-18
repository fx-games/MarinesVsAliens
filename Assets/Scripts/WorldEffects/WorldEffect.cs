using System.Collections.Generic;
using UnityEngine;

namespace WorldEffects
{
    public abstract class WorldEffect
    {
        protected BattleGame _game;
        protected Map _map;
        public string Identifier { get; set; }
        public Unit ActingUnit { get; set; }
        public Vector3Int SourcePosition { get; set; }
        public List<Vector3Int> Targets { get; set; }
        public List<Vector3Int> AffectedTiles { get; set; }

        public int[] DamageRoll { get; set; }
        public int[] ChanceToHitRoll { get; set; }
        protected internal void Initialize(BattleGame game, Map map)
        {
            _game = game;
            _map = map;
        }

        public abstract void Execute();
    }
}