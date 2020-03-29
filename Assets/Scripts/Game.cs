using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using GridPathFinder;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using static Mod;
using static Mod.RuleFaction;
using static Unit;
using Color = UnityEngine.Color;

public class Game : MonoBehaviour
{
    public enum GameState
    {
        Default,
        MoveTargetSelection,
        AttackTargetSelection,
        MovementSelectedPreConfirmation
    }

    public GameObject gridMapPrefab;
    public GameObject unitPrefab;
    public GameObject explosionPrefab;
    private List<Unit> allUnits = new List<Unit>();
    private Map gameMap;
    public Unit selectedUnit;
    private Camera gameCamera;
    public Text topLeftLabel;
    private Pathfinder pathfinder;
    private GameState gameState = GameState.Default;
    private HashSet<Vector3Int> validTargets = new HashSet<Vector3Int>();
    private Mod mod;
    private List<RuleFaction> factions;
    private int phaseFaction;
    private CameraControls cameraController;

    private void Awake()
    {
        var gridMap = Instantiate(gridMapPrefab);
    }

    void Start()
    {
        gameMap = FindObjectOfType<Map>();
        mod = GetComponent<Mod>();
        gameCamera = Camera.main;
        cameraController = gameCamera.GetComponent<CameraControls>();
        if (gameCamera == null)
        {
            throw new Exception("Game needs camera.");
        }
        
        factions = mod.GetFactions();
        var firstMap = mod.GetMap(0);
        gameMap.FromRules(firstMap);
        SpawnUnits();
        
        pathfinder = new Pathfinder(gameMap, 1.0f, 1.2f);
        
        phaseFaction = -1;
        PrepareNextTurn();
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
                if (Input.GetMouseButtonDown(0))
                {
                    if (gameMap.Contains(mapPos.x, mapPos.y))
                    {
                        InputPressedOnMap(mapPos);
                    }
                    else
                    {
                        DeselectAll();
                    }
                }
                else if (Input.GetMouseButtonUp(0))
                {
                    if (gameMap.Contains(mapPos.x, mapPos.y))
                    {
                        InputReleasedOnMap(mapPos);
                    }
                }
                else if (Input.GetMouseButtonDown(1))
                {
                    if (gameMap.Contains(mapPos.x, mapPos.y))
                    {
                        MapRightClicked(mapPos);
                    }
                    else
                    {
                        DeselectAll();
                    }
                }
                else if (selectedUnit != null && gameState == GameState.MoveTargetSelection)
                {
                    PreviewMovement(selectedUnit, mapPos);
                }
            }
        }
    }

    private void DeselectAll()
    {
        selectedUnit = null;
        gameMap.HideUnitMarker();
        ToDefaultState();
    }

    private void PreviewMovement(Unit unit, Vector3Int targetPos)
    {
        gameMap.RemoveAllHighlights(1);
        var path = pathfinder.GetShortestPath(new Point(targetPos.x, targetPos.y)).ConvertAll(p => new Vector3Int(p.X, p.Y, 0));
        HashSet<Vector3Int> previewTiles = new HashSet<Vector3Int>();
            
        if (path.Count > 0)
        {
            foreach (var pathPos in path)
            {
                foreach (var offset in unit.OccupiedTiles)
                {
                    previewTiles.Add(pathPos + offset);
                }
            }

            foreach (var tile in previewTiles)
            {
                gameMap.HighlightTile(tile, new Color(0.4f,0.3f,0.7f,0.6f), 1);
            }
        }
    }

    private void InputReleasedOnMap(Vector3Int mapPos)
    {
        if (cameraController.ActiveTouchCameraMovement)
            return;
        
        if (gameState == GameState.AttackTargetSelection)
        {
            AttackTargetSelected(mapPos);
        }
        else if (gameMap.IsUnitOn(mapPos) && gameMap.GetUnitAt(mapPos).Faction == CurrentFaction)
        {
            ToDefaultState();
            UnitSelected(mapPos);
        }
        else if (gameState == GameState.MoveTargetSelection 
                 && !gameMap.IsUnitOn(mapPos) 
                 && selectedUnit != null)
        {
            gameState = GameState.MovementSelectedPreConfirmation;
            gameMap.SetHighlightColor(new Color(0,0,0.7f,0.6f), 1);
        }
        else if (gameState == GameState.MovementSelectedPreConfirmation 
                && !gameMap.IsUnitOn(mapPos) 
                && selectedUnit != null)
        {
            ConfirmMovement(mapPos);
        }
        else
        {
            DeselectAll();
        }
        UpdateLabel();
    }

    private void InputPressedOnMap(Vector3Int mapPos)
    {
        
    }
    
    private void MapRightClicked(Vector3Int mapPos)
    {
        if (gameState == GameState.AttackTargetSelection)
        {
            ToDefaultState();
        }
    }

    private void UnitSelected(Vector3Int mapPos)
    {
        selectedUnit = gameMap.GetUnitAt(mapPos);
        selectedUnit.MarkAsSelected();
        if (selectedUnit.movesLeft > 0)
        {
            ShowReachableTiles(selectedUnit, selectedUnit.movesLeft);
            gameState = GameState.MoveTargetSelection;
        }
        UpdateLabel();
    }


    private void ToDefaultState()
    {
        gameMap.RemoveAllHighlights();
        gameMap.RemoveAllHighlights(1);
        gameState = GameState.Default;
    }

    private void ConfirmMovement(Vector3Int mapPos)
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

    private void AttackTargetSelected(Vector3Int targetedPos)
    {
        if (selectedUnit != null && validTargets.Contains(targetedPos))
        {
            var unitPos = gameMap.WorldToCell(selectedUnit.transform.position);
            var isMelee = gameMap.AreNeighbors(unitPos,targetedPos);
            var weapon = isMelee ? selectedUnit.MeleeWeapon : selectedUnit.RangedWeapon;
            var areaOfEffect = gameMap.AreaOfEffect(unitPos, weapon, targetedPos);
            int[] attackerDice = weapon.RollDamage();
            foreach (var aoePos in areaOfEffect)
            {
                if (gameMap.AoEAllowed(selectedUnit, weapon, targetedPos, aoePos))
                {
                    CreateExplosion(aoePos);
                    if (gameMap.IsUnitOn(aoePos))
                    {
                        var hitUnit = gameMap.GetUnitAt(aoePos);
                        ResolveAttack(attackerDice, unitPos,selectedUnit, targetedPos, aoePos, hitUnit);
                    }
                }
            }

            selectedUnit.HasAttacked();
            ToDefaultState();
        }
    }

    private void ResolveAttack(int[] attackerDice, Vector3Int attackerPos, Unit attacker, Vector3Int targetPos, Vector3Int aoePos, Unit target)
    {
        var isMelee = IsMelee(attacker, target);
        var isAoEPosition = targetPos != aoePos;
        
        var weapon = attacker.RangedWeapon;
        if (isMelee)
        {
            weapon = attacker.MeleeWeapon;
        }

        var isExplosion = weapon.Rules.Radius > 0;

        int attackerScore = attackerDice.Sum();

        if (isExplosion && isAoEPosition)
        {
            attackerScore = attackerDice.Max();
        }
        //Debug.Log("Attack: " + attack);

        if (attackerScore > 0)
        {
            int defenderScore = target.GetDefense(isMelee);
            //target.AttackedWith(attack, isMelee);
            if (defenderScore >= attackerScore)
            {
                // no damage to target
                if (isMelee && defenderScore > attackerScore)
                {
                    // damage to attacker
                    int returnedMeleeDamage = defenderScore - attackerScore;
                    attacker.DamageReceived(target, attacker.MeleeWeapon, returnedMeleeDamage);
                }
            }
            else
            {
                // damage to target
                int damage = attackerScore - defenderScore;
                target.DamageReceived(attacker, weapon, damage);
            }
            if (target.currentHealth <= 0)
            {
                UnitDies(target);
            }
            if (attacker.currentHealth <= 0)
            {
                UnitDies(attacker);
            }
        }
    }

    private void UnitDies(Unit unit)
    {
        unit.FinishedMoving -= UnitFinishedMoving;
        unit.UnitDeath();
        gameMap.RemoveUnit(unit);
        allUnits.Remove(unit);
    }

    private bool IsMelee(Unit attacker, Unit target)
    {
        var attackpos = gameMap.WorldToCell(attacker.transform.position);
        var targetpos = gameMap.WorldToCell(target.transform.position);
        return gameMap.AreNeighbors(attackpos, targetpos);
    }

    private void CreateExplosion(Vector3Int mapPos)
    {
        Instantiate(explosionPrefab, gameMap.CellToWorld(mapPos), Quaternion.identity);
    }

    public void OpenDoorButtonClicked()
    {
        if (selectedUnit != null)
        {
            OpenDoor(selectedUnit, selectedUnit.Cell);
        }
    }

    private void OpenDoor(Unit unit, Vector3Int mapPos)
    {
        gameMap.RemoveDoor(unit, mapPos);
        gameMap.UpdateFOV(unit.Cell, unit.currentVisionRange);
        UpdateVisibleUnits();
    }

    public void EndTurnButtonClicked()
    {
        PrepareNextTurn();
    }

    private void PrepareNextTurn()
    {
        ToDefaultState();
        phaseFaction++;
        if (phaseFaction > factions.Count - 1)
        {
            phaseFaction = 0;
        }

        bool firstUnitFound = false;
        foreach (var unit in allUnits)
        {
            if (unit.Faction == CurrentFaction)
            {
                unit.PrepareNewTurn();
                if (!firstUnitFound)
                {
                    firstUnitFound = true;
                    UnitSelected(unit.Cell);
                    CenterCameraOn(unit);
                }
            }
        }

        GlobalFOVUpdate();
    }

    private void CenterCameraOn(Unit unit)
    {
        var unitPos = unit.transform.position;
        gameCamera.GetComponent<CameraControls>().SmoothTransition(unitPos);
    }

    public void AttackButtonClicked()
    {
        if (selectedUnit != null && selectedUnit.State != UnitState.Moving && selectedUnit.canAttack)
        {
            gameMap.RemoveAllHighlights(0);
            gameMap.RemoveAllHighlights(1);
            PreviewAttack(selectedUnit, gameMap.WorldToCell(selectedUnit.transform.position), 10);
            gameState = GameState.AttackTargetSelection;
        }
    }

    private void PreviewAttack(Unit unit, Vector3Int unitPos, int maxDistance)
    {
        validTargets.Clear();
        if (unit.HasRangedWeapon)
        {
            var weaponRules = unit.RangedWeapon.Rules;
            if (weaponRules.StraightLineOnly)
            {
                validTargets.UnionWith(gameMap.GetStraightLine(unitPos, Map.Direction.North, maxDistance));
                validTargets.UnionWith(gameMap.GetStraightLine(unitPos, Map.Direction.South, maxDistance));
                validTargets.UnionWith(gameMap.GetStraightLine(unitPos, Map.Direction.East, maxDistance));
                validTargets.UnionWith(gameMap.GetStraightLine(unitPos, Map.Direction.West, maxDistance));
                validTargets.UnionWith(gameMap.GetStraightLine(unitPos, Map.Direction.NorthEast, maxDistance));
                validTargets.UnionWith(gameMap.GetStraightLine(unitPos, Map.Direction.NorthWest, maxDistance));
                validTargets.UnionWith(gameMap.GetStraightLine(unitPos, Map.Direction.SouthEast, maxDistance));
                validTargets.UnionWith(gameMap.GetStraightLine(unitPos, Map.Direction.SouthWest, maxDistance));   
            }
            else
            {
                validTargets = gameMap.GetFOV(unitPos, maxDistance, true);
            }
            
            validTargets.Remove(unitPos);
        }
        
        if (!unit.HasMeleeWeapon) // can only attack these tiles with melee
        {
            validTargets.Remove(unitPos + Vector3Int.right);
            validTargets.Remove(unitPos + Vector3Int.up);
            validTargets.Remove(unitPos + Vector3Int.down);
            validTargets.Remove(unitPos + Vector3Int.left);
        }
        else
        {
            AddToValidMeleeTargets(unit,unitPos,unitPos + Vector3Int.right);
            AddToValidMeleeTargets(unit,unitPos,unitPos + Vector3Int.up);
            AddToValidMeleeTargets(unit,unitPos,unitPos + Vector3Int.down);
            AddToValidMeleeTargets(unit,unitPos,unitPos + Vector3Int.left);
        }
        validTargets.Remove(unitPos + Vector3Int.up + Vector3Int.right);
        validTargets.Remove(unitPos + Vector3Int.down + Vector3Int.left);
        validTargets.Remove(unitPos + Vector3Int.down + Vector3Int.right);
        validTargets.Remove(unitPos + Vector3Int.up + Vector3Int.left);
        foreach (var tile in validTargets)
        {
            gameMap.HighlightTile(tile, new Color(0.6f, 0f, 0, 0.5f));
        }
    }

    private void AddToValidMeleeTargets(Unit unit, Vector3Int unitPos, Vector3Int pos)
    {
        if (gameMap.Contains(pos.x, pos.y) && !gameMap.IsWallBetween(unitPos, pos) && gameMap.IsUnitOn(pos))
        {
            validTargets.Add(pos);
        }
    }

    private void ShowReachableTiles(Unit unit, int maxDistance)
    {
        var startPos = gameMap.WorldToCell(unit.transform.position);
        gameMap.RemoveAllHighlights();
        pathfinder.MaxCost = maxDistance + 1f;
        pathfinder.CurrentUnit = unit;
        
        pathfinder.Dijkstra(new HashSet<Point>{new Point(startPos.x, startPos.y)}, new HashSet<Point>());
        //pathfinder.PrintDistMap();
        
        var nodeList = pathfinder.ReachableList.ToList().ConvertAll(point => new Vector3Int(point.X, point.Y, 0));
        
        foreach (var reachablePos in nodeList)
        {
            if (!gameMap.IsUnitOn(reachablePos))
            {
                gameMap.HighlightTile(reachablePos, new Color(0, 0.8f, 0, 0.2f));
            }
        }
    }

    private void UnitFinishedMoving(object sender, UnitEventArgs args)
    {
        //var unit = args.unit;
        GlobalFOVUpdate();
        
        if (selectedUnit == args.unit && selectedUnit.movesLeft > 0)
        {
            ShowReachableTiles(selectedUnit, selectedUnit.movesLeft);
            gameState = GameState.MoveTargetSelection;
        }
    }

    private void GlobalFOVUpdate()
    {
        gameMap.ResetFog();
        foreach (var unit in allUnits)
        {
            unit.SetVisible(false);
            if (unit.Faction == CurrentFaction)
            {
                unit.SetVisible(true);
                gameMap.UpdateFOV(gameMap.WorldToCell(unit.transform.position), unit.currentVisionRange);
            }
        }
        foreach (var unit in allUnits)
        {
            if (unit.Faction != CurrentFaction && !gameMap.IsInFog(unit.Cell))
            {
                unit.SetVisible(true);
            }
        }
    }

    private RuleFaction CurrentFaction => factions[phaseFaction];


    public void SetLabel(string text)
    {
        topLeftLabel.text = text;
    }

    private void UpdateLabel()
    {
        if (selectedUnit != null)
        {
            SetLabel(selectedUnit.name + "\n" + selectedUnit.WeaponDescription);
        }
        else
        {
            SetLabel(gameMap.CurrentCursorPosition.ToString());
        }
    }

    private void SpawnUnits()
    {
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
                        SpawnUnit(spawnPosition, unitType, loadOut, ruleFaction);
                        spawnIndex++;
                    }
                }
            }
        }
        
    }

    private bool SpawnUnit(Vector3Int spawnPosition, string unitType, RuleLoadout loadout, RuleFaction ruleFaction)
    {
        var unit = Instantiate(unitPrefab, gameMap.CellToWorld(spawnPosition), Quaternion.identity)
            .GetComponent<Unit>();
        unit.LoadRules(unitType, ruleFaction, loadout);
        if (gameMap.AddUnit(unit, spawnPosition))
        {
            var spriteRenderer = unit.GetComponentInChildren<SpriteRenderer>();
            spriteRenderer.color = ColorFromArray(ruleFaction.Color);
            spriteRenderer.enabled = false; // spawn every unit invisible
            
            unit.FinishedMoving += UnitFinishedMoving;
            unit.StepCompleted += UnitStepCompleted;
            allUnits.Add(unit);
            
            //gameMap.UpdateFOV(spawnPosition, unit.visionRange);
            return true;
        }

        Destroy(unit.gameObject);
        Debug.LogWarning("Could not spawn unit at: " + spawnPosition);
        return false;
    }

    private void UnitStepCompleted(object sender, UnitEventArgs args)
    {
        var unit = args.unit;
        var oldPos = args.previousPosition;
        
        var newPos = gameMap.WorldToCell(unit.transform.position);

        if (gameMap.IsDoorBetween(oldPos, newPos))
        {
            gameMap.RemoveDoorBetween(unit, oldPos, newPos);
        }
        if (unit.OccupiedTiles.Count > 1)
        {
            gameMap.IsStandingOnDoor(unit, true);
        }
        
        gameMap.UpdateFOV(newPos, unit.currentVisionRange);
        UpdateVisibleUnits();
    }

    private void UpdateVisibleUnits()
    {
        foreach (var unit in allUnits)
        {
            var unitPos = gameMap.WorldToCell(unit.transform.position);
            var shouldBeVisible = unit.Faction == CurrentFaction
                                  || !gameMap.IsInFog(unitPos);
            unit.SetVisible(shouldBeVisible);
        }
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
                int tryCounter = 0;
                while (!SpawnUnit(randomPosition, loadOut.Unit, loadOut, ruleFaction) && tryCounter < 5)
                {
                    // wait for free spawn pos
                    randomPosition = gameMap.RandomPosition;
                    tryCounter++;
                }
            }
        }
    }
}
