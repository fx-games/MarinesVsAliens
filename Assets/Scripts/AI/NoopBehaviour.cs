using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DefaultNamespace;
using UnityEngine;

namespace AI
{
    internal class NoopBehaviour : SubsumptionBehavior
    {
        private IPlayerMap map;
        private IGame game;
        
        public NoopBehaviour(IGame game, IPlayerMap map)
        {
            this.map = map;
            this.game = game;
        }

        public override void Init()
        {
        
        }

        public override bool CheckIfFired(Unit unit, MapTileInfo[,] mapKnowledge, List<Unit> visibleEnemies)
        {
            return true;
        }

        public override IEnumerator Fire(Unit unit, MapTileInfo[,] mapKnowledge, List<Unit> visibleEnemies, Action<PlayerCommand> callback)
        {
            callback(new PlayerCommand(CommandType.Noop));
            yield return null;
        }


    }
}