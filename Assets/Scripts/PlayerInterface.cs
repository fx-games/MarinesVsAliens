using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DefaultNamespace
{
    public interface PlayerInterface
    {
        Mod.RuleFaction Faction { get; }
        IPlayerMap PlayerMap { get; }
        List<IPlayerUnit> AllPlayerUnits { get; }
        bool TryMoveWithUnit(IPlayerUnit playerUnit, Vector3Int mapPos);
        bool TryAttackWithUnit(IPlayerUnit playerUnit, Vector3Int mapPos);
        
    }

    public interface IPlayerMap
    {
        IPlayerUnit GetUnitAt(Vector3Int mapPos);
        bool IsUnitOn(Vector3Int mapPos);
        bool IsNeighborReachable(IPlayerUnit unit, Vector3Int tileOne, Vector3Int tileTwo);
        bool IsNeighborVisible(Vector3Int oldPos, Vector3Int newPos);
        Vector3Int GetNeighbour(Vector3Int position, Map.Direction direction);
        int Width { get; }
        int Height { get; }
        Vector3Int RandomPosition { get; }
        List<Vector3Int> AllPositions { get; }
        bool Contains(int x, int y);
        bool AreNeighbors(Vector3Int posOne, Vector3Int posTwo, bool includeDiagonals = false);
        List<Vector3Int> AreaOfEffect(Vector3Int unitPos, Weapon weapon, Vector3Int mapPos);
        bool IsOtherFactionOn(IPlayerUnit unit, Vector3Int pos);
        bool CanBePlaced(IPlayerUnit unit, Vector3Int mapPos, bool doorsAreBlocking = true, bool friendlyUnitsAreBlocking = true);
        bool IsOtherUnitOn(IPlayerUnit unit, Vector3Int mapPos);
        bool IsInFog(Vector3Int mapPos);
        bool IsWallAt(Vector3Int mapPos);
        bool IsOtherFactionOn(Mod.RuleFaction faction, Vector3Int mapPos);
        bool IsLineOfFireBetween(Vector3Int source, Vector3Int target);
        bool IsLineOfSightBetween(Vector3Int source, Vector3Int target);
        string GetPosDebugInfo(Vector3Int pos);
    }

    public interface IPlayerUnit
    {
        Weapon RangedWeapon { get; }
        Weapon MeleeWeapon { get; }
        int MovesLeft { get; }
        int CurrentVisionRange { get; }
        int CurrentHealth { get; }
        bool CanAttack { get; }
        Mod.RuleFaction Faction { get; set; }
        Vector3Int Cell { get; }
        bool HasRangedWeapon { get; }
        bool HasMeleeWeapon { get; }
        Mod.RuleUnit Rules { get; }
        List<Vector3Int> OccupiedTiles { get; set; }
    }
    
}