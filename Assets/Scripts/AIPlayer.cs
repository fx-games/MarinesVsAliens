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
    private SubsumptionBehavior behaviourForThisPhase;

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
        
        scheduler.AddBehavior(new DestroyBehaviour(game, map) {Level = 0});
        scheduler.AddBehavior(new ExplorationBehaviour(game, map) {Level = 10});
        
    }

    // called when a phase for this player begins
    // needs to end turn when done
    public void NextPhase()
    {
        GatherInformation(); // should update mapKnowledge and visibleEnemies
        behaviourForThisPhase = scheduler.Run(mapKnowledge, allUnits, visibleEnemies);
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
        if (allUnits.Count == 0)
        {
            game.EndPhase(this);
        }
        var playerCommand = behaviourForThisPhase.Fire(mapKnowledge, allUnits, visibleEnemies);
        
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
}