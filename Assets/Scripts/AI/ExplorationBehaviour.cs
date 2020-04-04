using System.Collections.Generic;
using System.Linq;
using DefaultNamespace;
using UnityEngine;

namespace AI
{
    internal class ExplorationBehaviour : SubsumptionBehavior
    {
        private IPlayerMap map;
        private IGame game;

        public ExplorationBehaviour(IGame game, IPlayerMap map)
        {
            this.map = map;
            this.game = game;
        }

        public override void Init()
        {
        
        }

        public override bool CheckIfFired(MapTileInfo[,] mapKnowledge, List<Unit> allUnits, List<Unit> visibleEnemies)
        {
            return visibleEnemies.Count == 0;
        }

        public override PlayerCommand Fire(MapTileInfo[,] mapKnowledge, List<Unit> allUnits, List<Unit> visibleEnemies)
        {
            if (!allUnits.Exists(unitInList => unitInList.MovesLeft > 0)) return new PlayerCommand(CommandType.EndTurn);
            
            var unit = allUnits.First(unitInList => unitInList.MovesLeft > 0);
            
            Vector3Int target = FindExplorationTarget(mapKnowledge);
            // no moves or nothing left to explore
            if (target == Vector3Int.down + Vector3Int.left) return new PlayerCommand(CommandType.EndTurn);

            Vector3Int moveTarget = game.GetBestMoveTowards(unit, target);
            

            if (moveTarget == unit.Cell)
            {
                Debug.Log("NO Target for " + unit + " at " + unit.Cell + ": " + target);
                Debug.Log("With best move towards being: " + moveTarget);
                Debug.Log("PosInfo: " + map.GetPosDebugInfo(moveTarget));
                var reachableMapPositions = game.GetReachableMapPositions(unit);
                moveTarget = reachableMapPositions.ElementAt(Random.Range(0, reachableMapPositions.Count));                
                Debug.Log("Random movement to: " + moveTarget);
            }
                
            PlayerCommand moveUnit = new PlayerCommand(CommandType.Movement)
            {
                PlayerUnit = unit,
                Targets = new List<Vector3Int>(){moveTarget}
            };
            
            return moveUnit;
        }

        private Vector3Int FindExplorationTarget(MapTileInfo[,] mapKnowledge)
        {
            foreach (var mapPos in map.AllPositions)
            {
                if (map.IsWallAt(mapPos) || map.IsUnitOn(mapPos)) continue;
            
                if (mapKnowledge[mapPos.x, mapPos.y] != MapTileInfo.Unknown) continue;
            
                return mapPos;
            }

            return Vector3Int.left + Vector3Int.down;
        }
    }
}