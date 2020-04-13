using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DefaultNamespace;
using UnityEngine;

namespace AI
{
    internal class MovementBehaviour : SubsumptionBehavior
    {
        private IPlayerMap map;
        private IGame game;

        public MovementBehaviour(IGame game, IPlayerMap map)
        {
            this.map = map;
            this.game = game;
        }

        public override void Init()
        {
        
        }

        public override bool CheckIfFired(Unit unit, MapTileInfo[,] mapKnowledge, List<Unit> visibleEnemies)
        {
            if (unit.MovesLeft == 0) return false;
            return true;
        }

        public override IEnumerator Fire(Unit unit, MapTileInfo[,] mapKnowledge, List<Unit> visibleEnemies, Action<PlayerCommand> callback)
        {
            if (visibleEnemies.Count == 0)
            {
                Vector3Int target = FindExplorationTarget(mapKnowledge);
                if (Options.Debug) map.SetTextAt(target, "EX");
                // no moves or nothing left to explore
                if (target == Vector3Int.down + Vector3Int.left)
                {
                    callback(new PlayerCommand(CommandType.Noop));
                    yield break;
                }

                Vector3Int moveTarget = game.GetBestMoveTowards(unit, target);


                if (moveTarget == unit.Cell)
                {
                    Debug.Log("NO Target for " + unit + ": " + target);
                    Debug.Log("With best move towards being: " + moveTarget);
                    Debug.Log("PosInfo: " + map.GetPosDebugInfo(moveTarget));
                    callback(new PlayerCommand(CommandType.Noop));
                    yield break;
                    /*
                    var reachableMapPositions =
                        game.GetReachableMapPositions(unit).Where(pos => !map.IsUnitOn(pos)).ToList();
                    reachableMapPositions.Sort((posOne, posTwo) =>
                        (unit.Cell - posOne).magnitude.CompareTo((unit.Cell - posTwo).magnitude));
                    moveTarget = reachableMapPositions.Last();
                    Debug.LogWarning("Random movement to: " + moveTarget);
                    if (Options.Debug) map.SetTextAt(moveTarget, "RND");
                    */
                }
                Debug.Log("EXPLORATION TARGET");
                PlayerCommand moveUnit = new PlayerCommand(CommandType.Movement)
                {
                    PlayerUnit = unit,
                    Targets = new List<Vector3Int>() {moveTarget}
                };
                callback(moveUnit);
                yield break;
            }
            else // enemies are visible to some units, move to attack
            {
                var reachableMapPositions = unit.ReachablePositions;

                if (reachableMapPositions.Count == 0)
                {
                    // cannot move anywhere
                    Debug.LogWarning(unit + " CANNOT MOVE anywhere.");
                    callback(new PlayerCommand(CommandType.Noop));
                    yield break;
                }

                foreach (var mapPosition in reachableMapPositions)
                {
                    var validTargets = unit.GetValidAttackTargetsAt(mapPosition);
                    foreach (var visibleEnemy in visibleEnemies)
                    {
                        if (!validTargets.Contains(visibleEnemy.Cell)) continue;
                        Debug.Log("ATTACK POSITION FOUND");
                        PlayerCommand moveUnit = new PlayerCommand(CommandType.Movement)
                        {
                            PlayerUnit = unit,
                            Targets = new List<Vector3Int>() {mapPosition}
                        };
                        callback(moveUnit);
                        yield break;
                    }
                }
                // no attack position found

                // just move towards
                {
                    visibleEnemies.Sort((one,two) => (one.Cell - unit.Cell).magnitude.CompareTo((two.Cell - unit.Cell).magnitude));
                    var targetEnemy = visibleEnemies.First();
                    var cellsNextToEnemy = map.GetAllNeighborsForUnit(unit, targetEnemy.Cell);
                    var freeCells = cellsNextToEnemy.Where(p => map.CanBePlaced(unit, p, false, false)).ToList();
                    if (freeCells.Any())
                    {
                        var targetPos = freeCells.First();
                        Vector3Int moveTarget = game.GetBestMoveTowards(unit, targetPos);
                        if (moveTarget == unit.Cell)
                        {
                            callback(new PlayerCommand(CommandType.Noop));
                            yield break;
                        }
                        Debug.Log("BEST MOVE TOWARDS " + targetPos);
                        PlayerCommand moveUnit = new PlayerCommand(CommandType.Movement)
                        {
                            PlayerUnit = unit,
                            Targets = new List<Vector3Int>() {moveTarget}
                        };
                        callback(moveUnit);
                        yield break;
                    }
                }

                yield break;
            }
            
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