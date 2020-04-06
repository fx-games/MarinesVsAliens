using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DefaultNamespace;
using UnityEngine;

namespace AI
{
    internal class AttackBehaviour : SubsumptionBehavior
    {
        private IPlayerMap map;
        private IGame game;
        
        public AttackBehaviour(IGame game, IPlayerMap map)
        {
            this.map = map;
            this.game = game;
        }

        public override void Init()
        {
        
        }

        public override bool CheckIfFired(Unit unit, MapTileInfo[,] mapKnowledge, List<Unit> visibleEnemies)
        {
            if (visibleEnemies.Count == 0 || !unit.CanAttack) return false;
            return visibleEnemies.Any(unit.CanAttackUnit);
        }

        public override IEnumerator Fire(Unit unit, MapTileInfo[,] mapKnowledge, List<Unit> visibleEnemies, Action<PlayerCommand> callback)
        {
            var attackTarget = visibleEnemies.First(unit.CanAttackUnit).Cell;
            if (Options.Debug) map.SetTextAt(attackTarget, "ATT");
            callback(new PlayerCommand(CommandType.Attack)
            {
                PlayerUnit = unit,
                Targets = new List<Vector3Int> {attackTarget}
            });
            yield return null;
        }


    }
}