using System;
using System.Collections.Generic;
using System.Linq;
using AI;
using DefaultNamespace;
using UnityEngine;

public enum MapTileInfo
{
    Unknown,
    Walkable,
    Blocked,
    Dangerous,
    Enemy,
    MissionGoal
}

public class AIPlayer
{
    private IGame game;
    private IPlayerMap map;
    private Mod mod;
    public Mod.RuleFaction Faction { get; }
    private SubsumptionScheduler scheduler;
    private List<Unit> allUnits;
    private MapTileInfo[,] mapKnowledge;
    private List<Unit> visibleEnemies = new List<Unit>();
    private int nextUnit;
    private HashSet<Unit> noopUnits = new HashSet<Unit>();

    public AIPlayer(IGame game, Mod mod, IPlayerMap map, Mod.RuleFaction faction)
    {
        this.game = game;
        this.map = map;
        this.mod = mod;
        Faction = faction;

        mapKnowledge = new MapTileInfo[map.Width,map.Height];
        Array.Clear(mapKnowledge, 0, mapKnowledge.Length);
        allUnits = this.game.GetAllUnits(this);
        
        scheduler = new SubsumptionScheduler(game, map, mod, faction);
        
        scheduler.AddBehavior(new AttackBehaviour(game, map) {Name = "Attack", Level = 0});
        scheduler.AddBehavior(new MovementBehaviour(game, map) {Name = "Move", Level = 10});
        scheduler.AddBehavior(new NoopBehaviour(game, map) {Name = "Noop", Level = 100});
        
    }

    // called when a phase for this player begins
    // needs to end turn when done
    public void NextPhase()
    {
        noopUnits.Clear();
        GatherInformation(); // should update mapKnowledge and visibleEnemies
    }

    private void GatherInformation()
    {
        allUnits = game.GetAllUnits(this);
        visibleEnemies = game.GetVisibleEnemies(this);
        UpdateMapKnowledge();
    }

    private void UpdateMapKnowledge()
    {
        foreach (var mapPos in map.AllPositions)
        {
            var alreadySeen = mapKnowledge[mapPos.x, mapPos.y] != MapTileInfo.Unknown;
            var notVisible = map.IsInFog(mapPos);
            if (alreadySeen || notVisible) continue;
            if (map.IsWallAt(mapPos))
            {
                mapKnowledge[mapPos.x, mapPos.y] = MapTileInfo.Blocked;
            }
            else
            {
                mapKnowledge[mapPos.x, mapPos.y] = MapTileInfo.Walkable;
            }
        }
    }

    public void ExecuteNextCommand()
    {   
        map.RemoveAllCellLabels();
        // high-level plan
        
        // assess the situation
        // choose best unit which can still act
        // create command for that unit
        // endCondition: No unit can act anymore OR No action would improve the situation
        
        var availableUnits = allUnits.Where(u => u.CanAct && !noopUnits.Contains(u)).ToList();

        if (availableUnits.Count == 0)
        {
            game.EndPhase(this);
            return;
        }

        var firstAvailableUnit = availableUnits.First();
        
        var behaviourForThisPhase = scheduler.Run(firstAvailableUnit, mapKnowledge, visibleEnemies);
        var playerCommand = behaviourForThisPhase.Fire(firstAvailableUnit, mapKnowledge, visibleEnemies);
        
        Debug.Log(playerCommand.ToString());
        
        if (playerCommand.Type == CommandType.Movement)
        {
            ExecuteMovement(playerCommand.PlayerUnit, playerCommand.Targets.First());
            GatherInformation();
        }
        if (playerCommand.Type == CommandType.Attack)
        {
            ExecuteAttack(playerCommand.PlayerUnit, playerCommand.Targets);
            GatherInformation();
        }
        else if (playerCommand.Type == CommandType.Noop)
        {
            noopUnits.Add(firstAvailableUnit);
        }
        else if (playerCommand.Type == CommandType.EndTurn)
        {
            game.EndPhase(this);
        }
    }

    private void ExecuteAttack(Unit unit, List<Vector3Int> targets)
    {
        game.AttackWithUnit(unit, targets);
    }

    private void ExecuteMovement(Unit unit, Vector3Int pos)
    {
        game.MoveUnit(unit, pos);
    }
}

public interface IGame
{
    List<Unit> GetAllUnits(AIPlayer aiPlayer);
    List<Unit> GetVisibleEnemies(AIPlayer aiPlayer);
    Vector3Int GetBestMoveTowards(Unit unit, Vector3Int first);
    void EndPhase(AIPlayer aiPlayer);
    void MoveUnit(Unit unit, Vector3Int targetPos);
    HashSet<Vector3Int> GetReachableMapPositions(Unit unit);
    void AttackWithUnit(Unit unit, List<Vector3Int> targets);
    List<Unit> GetVisibleEnemies(Unit unit);
    List<Unit> GetVisibleEnemies(Vector3Int mapPos, Mod.RuleFaction playerFaction);
    List<Vector3Int> GetShortestPath(Unit unit, Vector3Int goalPosition, bool movesLeftIsMaxDistance = false);
    List<Vector3Int> GetShortestPath(Unit unit, Vector3Int startPosition, Vector3Int goalPosition);
}

public enum CommandType
{
    Movement,
    Attack,
    EndTurn,
    Noop
}
public class PlayerCommand
{
    public PlayerCommand(CommandType type)
    {
        this.Type = type;
    }

    public CommandType Type { get; set; }
    public Unit PlayerUnit { get; set; }
    public List<Vector3Int> Targets { get; set; }

    public override string ToString()
    {
        string commandType = Enum.GetName(typeof(CommandType), Type);
        string unit = PlayerUnit != null ? PlayerUnit.ToString() : "";
        string target = Targets != null && Targets.Count > 0 ? Targets.First().ToString() : "";

        return commandType + "(" + unit + " -> " + target + ")";
    }
}