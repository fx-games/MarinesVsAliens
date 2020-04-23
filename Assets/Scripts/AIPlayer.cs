using System;
using System.Collections;
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
public enum AIState { Thinking, Idle}

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
    public AIState State { get; private set; }

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
        State = AIState.Thinking;
        allUnits = game.GetAllUnits(this);
        visibleEnemies = game.GetVisibleEnemies(this);
        UpdateMapKnowledge();
        State = AIState.Idle;
    }

    private void UpdateMapKnowledge()
    {
        foreach (var mapPos in map.AllPositions)
        {
            var alreadySeen = mapKnowledge[mapPos.x, mapPos.y] != MapTileInfo.Unknown;
            var notVisible = map.IsInFogMechanically(mapPos);
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

    public IEnumerator ExecuteNextCommand()
    {
        State = AIState.Thinking;
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
            State = AIState.Idle;
            yield break;
        }

        var firstAvailableUnit = availableUnits.First();
        
        var behaviourForThisPhase = scheduler.Run(firstAvailableUnit, mapKnowledge, visibleEnemies);

        yield return behaviourForThisPhase.Fire(firstAvailableUnit, mapKnowledge, visibleEnemies, returnValue =>
            {
                ThinkingDone(firstAvailableUnit, returnValue);
            });
    }

    private void ThinkingDone(Unit unit, PlayerCommand playerCommand)
    {
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
            noopUnits.Add(unit);
        }
        else if (playerCommand.Type == CommandType.EndTurn)
        {
            game.EndPhase(this);
        }
        
        State = AIState.Idle;
    }


    private void ExecuteAttack(Unit unit, List<Vector3Int> targets)
    {
        game.AttackWithUnit(unit, targets);
    }

    private void ExecuteMovement(Unit unit, Vector3Int pos)
    {
        game.MoveUnit(unit, pos);
        if (!unit.CanAttack())
        {
            noopUnits.Add(unit); // only move once
        }
    }
}

public interface IGame
{
    List<Unit> GetAllUnits(AIPlayer aiPlayer);
    List<Unit> GetVisibleEnemies(AIPlayer aiPlayer);
    Vector3Int GetBestMoveTowards(Unit unit, Vector3Int first);
    void EndPhase(AIPlayer aiPlayer);
    void MoveUnit(Unit unit, Vector3Int targetPos);
    void AttackWithUnit(Unit unit, List<Vector3Int> targets);
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