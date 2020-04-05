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

        public override PlayerCommand Fire(Unit unit, MapTileInfo[,] mapKnowledge, List<Unit> visibleEnemies)
        {
            return new PlayerCommand(CommandType.Noop);
        }


    }
}