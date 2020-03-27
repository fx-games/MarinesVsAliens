using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using GridPathFinder;
using Unity.Mathematics;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using YamlDotNet.RepresentationModel;
using static Mod;
using static Unit;
using Color = UnityEngine.Color;
using Random = UnityEngine.Random;

public class Game : MonoBehaviour
{
    public enum GameState
    {
        Default,
        MoveTargetSelection,
        AttackTargetSelection
    }

    public GameObject unitPrefab;
    public GameObject explosionPrefab;
    private List<Unit> allUnits = new List<Unit>();
    private Map gameMap;
    public Unit selectedUnit;
    private Camera gameCamera;
    public Text topLeftLabel;
    private Pathfinder pathfinder;
    private GameState gameState = GameState.Default;
    private HashSet<Vector3Int> validTargets;
    private Mod mod;


    void Start()
    {
        gameMap = FindObjectOfType<Map>();
        mod = GetComponent<Mod>();
        gameCamera = Camera.main;
        if (gameCamera == null)
        {
            throw new Exception("Game needs camera.");
        }
        pathfinder = new Pathfinder(gameMap, 1.0f, 1.2f);
        SpawnUnits();
    }

    
    // Update is called once per frame
    void Update()
    {
        if (!EventSystem.current.IsPointerOverGameObject())
        {
            Vector3 pos = gameCamera.ScreenToWorldPoint(Input.mousePosition);
            var mapPos = gameMap.WorldToCell(pos);
            if (gameMap.Contains(mapPos.x, mapPos.y))
            {
                if (Input.GetMouseButtonUp(0))
                {
                    if (gameMap.Contains(mapPos.x, mapPos.y))
                        OnMapClicked(mapPos);
                }
                else if (Input.GetMouseButtonUp(1))
                {
                    if (gameMap.Contains(mapPos.x, mapPos.y))
                        OnMapRightClicked(mapPos);
                }
                else if (selectedUnit != null && gameState == GameState.MoveTargetSelection)
                {
                    PreviewMovement(selectedUnit, mapPos);
                }
            }
        }
    }

    private void PreviewMovement(Unit unit, Vector3Int targetPos)
    {
        gameMap.RemoveAllHighlights(1);
        var path = pathfinder.GetShortestPath(new Point(targetPos.x, targetPos.y)).ConvertAll(p => new Vector3Int(p.X, p.Y, 0));
        if (path.Count > 0)
        {
            foreach (var pathPos in path)
            {
                gameMap.HighlightTile(pathPos, new Color(0,0,0.7f,0.6f), 1);
            }
        }
    }

    private void OnMapClicked(Vector3Int mapPos)
    {
        if (gameState == GameState.AttackTargetSelection)
        {
            OnAttackTargetSelected(mapPos);
        }
        else if (gameMap.IsUnitOn(mapPos))
        {
            selectedUnit = gameMap.GetUnitAt(mapPos);
            ShowReachableTiles(mapPos, selectedUnit.mobility);
            gameState = GameState.MoveTargetSelection;
        }
        else
        {
            gameState = GameState.Default;
            selectedUnit = null;
            gameMap.RemoveAllHighlights();
            gameMap.RemoveAllHighlights(1);
        }
        UpdateLabel();
    }

    private void OnMapRightClicked(Vector3Int mapPos)
    {
        if (gameState == GameState.AttackTargetSelection)
        {
            gameMap.RemoveAllHighlights();
            gameMap.RemoveAllHighlights(1);

            gameState = GameState.Default;
        }
        else if (!gameMap.IsUnitOn(mapPos) && selectedUnit != null)
        {
            var path = pathfinder.GetShortestPath(new Point(mapPos.x, mapPos.y)).ConvertAll(p => new Vector3Int(p.X, p.Y, 0));
            if (path.Count > 0)
            {
                gameMap.RemoveAllHighlights();
                gameMap.RemoveAllHighlights(1);
                selectedUnit.BeginMoving(path);
                gameState = GameState.Default;
            }
        }
    }

    private void OnAttackTargetSelected(Vector3Int mapPos)
    {
        if (validTargets.Contains(mapPos))
        {
            CreateExplosion(mapPos);
            if (gameMap.IsUnitOn(mapPos))
            {
                var hitUnit = gameMap.GetUnitAt(mapPos);
                ResolveAttack(selectedUnit, hitUnit);
            }
        }
    }

    private void ResolveAttack(Unit attacker, Unit target)
    {
        int attack = Random.Range(0, 4);
        int armor = 2;
        int damage = 0;
        Debug.Log("Attack: " + attack);
        if (attack > armor)
        {
            damage = attack - armor;
        }

        if (damage > 0)
        {
            target.InflictDamage(damage);
            Debug.Log("Damage: " + damage);
            if (target.health <= 0)
            {
                target.UnitDeath();
                allUnits.Remove(target);
            }
        }
    }

    private void CreateExplosion(Vector3Int mapPos)
    {
        Instantiate(explosionPrefab, gameMap.CellToWorld(mapPos), quaternion.identity);
    }

    public void AttackButtonClicked()
    {
        if (selectedUnit != null && selectedUnit.State != UnitState.Moving)
        {
            gameMap.RemoveAllHighlights(0);
            gameMap.RemoveAllHighlights(1);
            ShowRangedAttack(gameMap.WorldToCell(selectedUnit.transform.position), 20);
            gameState = GameState.AttackTargetSelection;
        }
    }

    private void ShowRangedAttack(Vector3Int mapPos, int maxDistance)
    {
        gameMap.RemoveAllHighlights();
        validTargets = gameMap.GetFOV(mapPos, maxDistance);
        
        validTargets.Remove(mapPos);
        validTargets.Remove(mapPos + Vector3Int.up + Vector3Int.right);
        validTargets.Remove(mapPos + Vector3Int.up + Vector3Int.left);
        validTargets.Remove(mapPos + Vector3Int.down + Vector3Int.right);
        validTargets.Remove(mapPos + Vector3Int.down + Vector3Int.left);
        
        foreach (var tile in validTargets)
        {
            gameMap.HighlightTile(tile, new Color(0.6f, 0f, 0, 0.5f));
        }
    }

    private void ShowReachableTiles(Vector3Int mapPos, int maxDistance)
    {
        gameMap.RemoveAllHighlights();
        pathfinder.MaxCost = maxDistance;
        pathfinder.Dijkstra(new HashSet<Point>(){new Point(mapPos.x, mapPos.y)}, new HashSet<Point>());
        //pathfinder.PrintDistMap();
        var nodeList = pathfinder.ReachableList.ToList().ConvertAll(point => new Vector3Int(point.X, point.Y, 0));
        foreach (var reachablePos in nodeList)
        {
            gameMap.HighlightTile(reachablePos, new Color(0, 0.8f, 0, 0.5f));
        }
    }

    private void UnitFinishedMoving(object sender, UnitEventArgs args)
    {
        //var unit = args.unit;
        GlobalFOVUpdate();
    }

    private void GlobalFOVUpdate()
    {
        gameMap.ResetFog();
        foreach (var unit in allUnits)
        {
            gameMap.UpdateFOV(gameMap.WorldToCell(unit.transform.position), unit.visionRange);
        }
    }


    public void SetLabel(string text)
    {
        topLeftLabel.text = text;
    }

    private void UpdateLabel()
    {
        if (selectedUnit != null)
        {
            SetLabel(gameMap.CurrentCursorPosition + " - Sel.: " + selectedUnit.name);
        }
        else
        {
            SetLabel(gameMap.CurrentCursorPosition.ToString());
        }
    }

    private void SpawnUnits()
    {
        var factions = mod.GetFactions();

        foreach (var ruleFaction in factions)
        {
            var spawnRule = mod.GetSpawnRule(ruleFaction.Type);
            if (spawnRule.RandomPlacement)
            {
                RandomPlacement(ruleFaction);
            }
            else
            {
                List<Vector3Int> spawnPositions = spawnRule.Locations.ConvertAll(elem => new Vector3Int(elem[0], elem[1], 0));
                int spawnIndex = 0;
                foreach (var loadOut in ruleFaction.LoadOut)
                {
                    for (int i = 0; i < loadOut.Count; i++)
                    {
                        var spawnPosition = spawnPositions[spawnIndex];
                        string unitType = loadOut.Unit;
                        SpawnUnit(spawnPosition, unitType, ruleFaction);
                        spawnIndex++;
                    }
                }
            }
        }
        
    }

    private bool SpawnUnit(Vector3Int spawnPosition, string unitType, RuleFaction ruleFaction)
    {
        var unit = Instantiate(unitPrefab, gameMap.CellToWorld(spawnPosition), Quaternion.identity)
            .GetComponent<Unit>();
        if (gameMap.AddUnit(unit, spawnPosition))
        {
            unit.GetComponent<SpriteRenderer>().color = ColorFromArray(ruleFaction.Color);
            unit.LoadRules(unitType);
            unit.FinishedMoving += UnitFinishedMoving;
            allUnits.Add(unit);
            gameMap.UpdateFOV(spawnPosition, unit.visionRange);
            return true;
        }

        Destroy(unit.gameObject);
        Debug.LogWarning("Could not spawn unit at: " + spawnPosition);
        return false;
    }

    private Color ColorFromArray(int[] colorDefinition)
    {
        return new Color(((float)colorDefinition[0])/255f,((float)colorDefinition[1])/255f,((float)colorDefinition[2])/255f);
    }

    private void RandomPlacement(RuleFaction ruleFaction)
    {
        foreach (var loadOut in ruleFaction.LoadOut)
        {
            for (int i = 0; i < loadOut.Count; i++)
            {
                Vector3Int randomPosition = gameMap.RandomPosition;
                while (!SpawnUnit(randomPosition, loadOut.Unit, ruleFaction))
                {
                    // wait for free spawn pos
                }
            }
        }
    }
}
