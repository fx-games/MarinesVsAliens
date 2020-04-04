using System.Collections.Generic;
using System.Linq;
using DefaultNamespace;
using UnityEngine;

namespace AI
{
    internal class DestroyBehaviour : SubsumptionBehavior
    {
        private IPlayerMap map;
        private IGame game;

        public DestroyBehaviour(IGame game, IPlayerMap map)
        {
            this.map = map;
            this.game = game;
        }

        public override void Init()
        {
        
        }

        public override bool CheckIfFired(MapTileInfo[,] mapKnowledge, List<Unit> allUnits, List<Unit> visibleEnemies)
        {
            return visibleEnemies.Count > 0;
        }

        public override PlayerCommand Fire(MapTileInfo[,] mapKnowledge, List<Unit> allUnits, List<Unit> visibleEnemies)
        {
            // no one can attack
            if (visibleEnemies.Count == 0 || !allUnits.Exists(unitInList => unitInList.CanAttack)) return new PlayerCommand(CommandType.EndTurn);

            var firstEnemy = visibleEnemies.First();
            
            var unitsCanAttackDirectly = allUnits.Where(u => u.CanAttack 
                                                             && (u.HasMeleeWeapon && map.AreNeighbors(u.Cell, firstEnemy.Cell) ||
                                                             u.HasRangedWeapon && map.IsLineOfFireBetween(u.Cell, firstEnemy.Cell))
                                                             ).ToList();

            if (unitsCanAttackDirectly.Any())
            {
                var attackingUnit = unitsCanAttackDirectly.First();
                
                return new PlayerCommand(CommandType.Attack)
                {
                    PlayerUnit = attackingUnit,
                    Targets = new List<Vector3Int>(){firstEnemy.Cell}
                };
            }
            else if (allUnits.Exists(u => u.CanAttack && u.MovesLeft > 0))
            {
                var movableUnit = allUnits.First(u => u.CanAttack && u.MovesLeft > 0);
                var reachableMapPositions = game.GetReachableMapPositions(movableUnit).ToList();
                var firingPositions = movableUnit.HasRangedWeapon 
                    ? reachableMapPositions.Where(p => map.IsLineOfFireBetween(p, firstEnemy.Cell)).ToList()
                    : reachableMapPositions.Where(p => map.AreNeighbors(p, firstEnemy.Cell)).ToList();
                if (firingPositions.Any())
                {
                    return new PlayerCommand(CommandType.Movement)
                    {
                        PlayerUnit = movableUnit,
                        Targets = new List<Vector3Int>(){firingPositions.First()}
                    };
                }
            }
            
            return new PlayerCommand(CommandType.EndTurn);
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