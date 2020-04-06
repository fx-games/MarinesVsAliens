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

                PlayerCommand moveUnit = new PlayerCommand(CommandType.Movement)
                {
                    PlayerUnit = unit,
                    Targets = new List<Vector3Int>() {moveTarget}
                };
                callback(moveUnit);
                yield break;
            }
            else // move to attack
            {
                var reachableMapPositions = unit.ReachablePositions;

                if (reachableMapPositions.Count == 0)
                {
                    // cannot move anywhere
                    Debug.LogWarning(unit + " CANNOT MOVE anywhere.");
                    callback(new PlayerCommand(CommandType.Noop));
                    yield break;
                }
                var visibleToUnit = visibleEnemies.Where(enemy => map.IsLineOfSightBetween(enemy.Cell, unit.Cell));

                Unit targetEnemy;
                if (visibleToUnit.Any())
                {
                    targetEnemy = visibleToUnit.First();
                }
                else
                {
                    visibleEnemies.Sort((one,two) => (one.Cell - unit.Cell).magnitude.CompareTo((two.Cell - unit.Cell).magnitude));
                    targetEnemy = visibleEnemies.First();
                }

                yield return GetScoreMap(unit, reachableMapPositions, targetEnemy, scoreMap =>
                {
                    var scoreList = scoreMap.ToList();
                    scoreList.Sort((one, two) => one.Value.CompareTo(two.Value));

                    var bestChoice = scoreList.Last().Key;
                
                    PlayerCommand moveUnit = new PlayerCommand(CommandType.Movement)
                    {
                        PlayerUnit = unit,
                        Targets = new List<Vector3Int>() {bestChoice}
                    };
                    callback(moveUnit);
                });

                
                yield break;
            }
            
        }

        private IEnumerator GetScoreMap(Unit unit, List<Vector3Int> reachableMapPositions, Unit targetEnemy, Action<Dictionary<Vector3Int, int>> callback)
        {
            var scoreMap = new Dictionary<Vector3Int, int>();
            foreach (var reachableMapPosition in reachableMapPositions)
            {
                int posScore = AssignScore(unit, reachableMapPosition, targetEnemy);
                scoreMap[reachableMapPosition] = posScore;
                yield return null;
            }
            callback(scoreMap);
            yield break;
        }

        private int AssignScore(Unit unit, Vector3Int reachableMapPosition, Unit targetEnemy)
        {
            int positionScore = 0;

            int visibleEnemies = unit.VisibleEnemiesFrom(reachableMapPosition).Count;
            
            var isAttackPos = false;
            
            if (unit.HasRangedWeapon)
            {
                isAttackPos = map.IsLineOfFireBetween(reachableMapPosition, targetEnemy.Cell);
            }
            else if (unit.HasMeleeWeapon)
            {
                isAttackPos = map.AreNeighbors(reachableMapPosition, targetEnemy.Cell);
            }
            
            if (isAttackPos)
                positionScore += 100;

            if (visibleEnemies == 1)
            {
                positionScore += 100;
            }
            else if (visibleEnemies > 1)
            {
                positionScore += 10 - (visibleEnemies * 2);
            }
            else if (visibleEnemies == 0 && !isAttackPos)
            {
                var cellsNextToEnemy = map.GetAllNeighborsForUnit(unit, targetEnemy.Cell);
                var freeCells = cellsNextToEnemy.Where(p => map.CanBePlaced(unit, p, false, false)).ToList();
                if (freeCells.Any())
                {
                    var targetPos = freeCells.First();
                    var shortestPath = unit.GetShortestPath(reachableMapPosition, targetPos);
                    if (shortestPath.Count > 0)
                    {
                        positionScore += 20 - shortestPath.Count;
                    }
                }
            }
            
            //map.SetTextAt(reachableMapPosition, visibleEnemies + (isAttackPos ? "F" : ""));
            var dbgInfo = positionScore == 0 ? "_" : positionScore.ToString();
            if (Options.Debug) map.SetTextAt(reachableMapPosition, dbgInfo);            
            return positionScore;
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