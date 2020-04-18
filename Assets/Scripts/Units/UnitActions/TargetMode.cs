using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DefaultNamespace.Units.UnitActions
{
    
    public enum TargetModeType
    {
        Self,
        Single,
        Multiple,
    }
    [Flags]
    public enum TargetType
    {
        None = 0,
        Floor = 1,
        Wall = 2,
        Enemies = 4,
        Friendlies = 8
    }

    public enum AffectedPreset
    {
        NoPreset,
        Explosion,
        StraightLine
    }
    
    public enum TargetPreset
    {
        NoPreset,
        CardinalMelee,
        RangedAttack,
        AllVisibleTiles,
        AllReachableTiles
    }
    public class TargetMode
    {
        private BattleGame _game;
        private Map _map;

        public TargetMode(TargetModeType type)
        {
            Type = type;
        }

        public TargetModeType Type { get; set; }
        public TargetType AllowedTargetTypes { get; set; }
        public TargetPreset AllowedTargetPreset { get; set; }
        public AffectedPreset ActiveAffectedPreset { get; set; }
        public List<Vector3Int> AllowedTargets { get; set; } = new List<Vector3Int>();
        public List<Vector3Int> AffectedTiles { get; set; }
        public int TargetRange { get; set; }
        public int TargetLimit { get; set; }
        public int AffectedRange { get; set; } // radius for explosions
        public int AffectedLimit { get; set; }

        public List<Vector3Int> GetAffectedTiles(Vector3Int sourcePos, HashSet<Vector3Int> targets)
        {
            if (Type == TargetModeType.Self) return new List<Vector3Int>{sourcePos};
            
            HashSet<Vector3Int> positions = new HashSet<Vector3Int>();
            
            if (AffectedTiles.Count > 0) // custom tiles have been set            
            {
                foreach (var target in targets)
                {
                    positions.UnionWith(AffectedTiles.Select(rel => target + rel));
                }       
                return positions.ToList();
            }

            if (ActiveAffectedPreset == AffectedPreset.StraightLine)
            {
                foreach (var target in targets)
                {
                    positions.UnionWith(GetStraightLine(sourcePos, target));
                    positions.Remove(sourcePos);
                }
            }
            
            if (ActiveAffectedPreset == AffectedPreset.Explosion)
            {
                var radius = AffectedRange;
                if (radius > 0)
                {
                    foreach (var mapPos in targets)
                    {
                        for (int i = mapPos.x - radius; i <= mapPos.x + radius; i++)
                        {
                            for (int j = mapPos.y - radius; j <= mapPos.y + radius; j++)
                            {
                                var nextPos = new Vector3Int(i, j, 0);
                                var euclideanDistance = (mapPos - nextPos).magnitude;
                                if (Mathf.Floor(euclideanDistance) <= radius)
                                {
                                    positions.Add(nextPos);
                                }
                            }
                        }
                    }
                    return positions.ToList();
                }                
            }
            
            return positions.ToList();
        }
        
        public HashSet<Vector3Int> GetStraightLine(Vector3Int posOne, Vector3Int posTwo)
        {
            HashSet<Vector3Int> linePositions = new HashSet<Vector3Int>();
                
            Vector3Int rayDirVector = posOne.x < posTwo.x ? Vector3Int.right : posOne.x > posTwo.x ? Vector3Int.left : Vector3Int.zero;
            rayDirVector += posOne.y < posTwo.y ? Vector3Int.up : posOne.y > posTwo.y ? Vector3Int.down : Vector3Int.zero;
            var prevPos = posOne;
            var nextPos = prevPos + rayDirVector;
            while (nextPos != posTwo)
            {
                if (!_map.IsNeighborVisible(prevPos.x, prevPos.y, nextPos.x, nextPos.y))
                {
                    return linePositions;
                }

                linePositions.Add(nextPos);
                prevPos = nextPos;
                nextPos = prevPos + rayDirVector;
            }

            return linePositions;
        }

        public bool IsValidTarget(Unit actingUnit, Vector3Int targetedPos)
        {
            bool validTypeTargeted = true;

            bool validTargetPosition = true;
            
            if ((AllowedTargetTypes & TargetType.Wall) == 0 && _map.IsWallAt(targetedPos)) // flag is NOT set
            {
                validTypeTargeted = false;
            }
            
            if ((AllowedTargetTypes & TargetType.Floor) == 0 && _map.IsEmptyFloorTile(targetedPos))
            {
                validTypeTargeted = false;
            }
            
            if ((AllowedTargetTypes & TargetType.Friendlies) == 0 && _map.IsFriendlyUnitOn(actingUnit, targetedPos))
            {
                validTypeTargeted = false;
            }
            
            if ((AllowedTargetTypes & TargetType.Enemies) == 0 && _map.IsOtherFactionOn(actingUnit, targetedPos))
            {
                validTypeTargeted = false;
            }
            
            switch (AllowedTargetPreset)
            {
                case TargetPreset.RangedAttack:
                    validTargetPosition = actingUnit.ValidRangedTargets.Contains(targetedPos);
                    break;
                case TargetPreset.AllReachableTiles:
                    validTargetPosition = actingUnit.ReachablePositions.Contains(targetedPos);
                    break;
                case TargetPreset.AllVisibleTiles:
                    validTargetPosition = actingUnit.FieldOfView.Contains(targetedPos);
                    break;
                case TargetPreset.CardinalMelee:
                    validTargetPosition = _map.AreNeighbors(actingUnit.Cell, targetedPos);
                    break;
                default:
                    validTargetPosition = AllowedTargets.Contains(targetedPos);
                    break;
            }

            return validTypeTargeted && validTargetPosition;
        }

        public IEnumerable<Vector3Int> GetValidTargetPositions(Unit actingUnit)
        {
            switch (AllowedTargetPreset)
            {
                case TargetPreset.RangedAttack:
                    return actingUnit.ValidRangedTargets;
                case TargetPreset.AllReachableTiles:
                    return actingUnit.ReachablePositions;
                case TargetPreset.AllVisibleTiles:
                    return actingUnit.FieldOfView;
                case TargetPreset.CardinalMelee:
                    return _map.Cardinal.Select(d => d.ToVector() + actingUnit.Cell);
                default:
                    return AllowedTargets;
            }
        }

        protected internal void Initialize(BattleGame game, Map map)
        {
            _game = game;
            _map = map;
        }
    }
}