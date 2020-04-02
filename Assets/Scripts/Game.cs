using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using DefaultNamespace;
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
    public GameObject[] premadeMapBlocks;

    public GameObject unitPrefab;
    public GameObject explosionPrefab;
    private List<Unit> allUnits = new List<Unit>();
    private Map _map;
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
    private Vector3Int lastCursorLocation;
    private int currentMapIndex;

    private void Awake()
    {
        mod = GetComponent<Mod>();
        gameCamera = Camera.main;
        if (gameCamera == null)
        {
            throw new Exception("Game needs camera.");
        }
    }

    void Start()
    {
        cameraController = gameCamera.GetComponent<CameraControls>();
        factions = mod.GetFactions();
        currentMapIndex = (0);
        LoadMap();
    }

    void LoadMap()
    {
        if (_map != null)
        {
            Destroy(_map);
        }
        var mapRules = mod.GetMap(currentMapIndex);
        if (mapRules.Generator == 0)
        {
            var premadeMap = premadeMapBlocks.First(map => map.name == mapRules.Premade);
            _map = Instantiate(premadeMap).GetComponent<Map>();
        }
        else
        {
            _map = Instantiate(gridMapPrefab).GetComponent<Map>();
            _map.FromRules(mapRules);
        }
        
        Invoke("AfterMapLoad", 0.1f);
    }

    public void AfterMapLoad()
    {
        SpawnUnits();
        
        pathfinder = new Pathfinder(_map, 1.0f, 1.2f);
        
        phaseFaction = -1;
        PrepareNextTurn();
    }
    
    // Update is called once per frame
    void Update()
    {
        if (!EventSystem.current.IsPointerOverGameObject())
        {
            Vector3 pos = gameCamera.ScreenToWorldPoint(Input.mousePosition);
            var cursorLocation = _map.WorldToCell(pos);
            if (_map.Contains(cursorLocation.x, cursorLocation.y))
            {
                if (Input.GetMouseButtonDown(0))
                {
                    if (_map.Contains(cursorLocation.x, cursorLocation.y))
                    {
                        InputPressedOnMap(cursorLocation);
                    }
                    else
                    {
                        DeselectAll();
                    }
                }
                else if (Input.GetMouseButtonUp(0))
                {
                    if (_map.Contains(cursorLocation.x, cursorLocation.y))
                    {
                        InputReleasedOnMap(cursorLocation);
                    }
                }
                else if (Input.GetMouseButtonDown(1))
                {
                    if (_map.Contains(cursorLocation.x, cursorLocation.y))
                    {
                        MapRightClicked(cursorLocation);
                    }
                    else
                    {
                        DeselectAll();
                    }
                }
                else if (selectedUnit != null && gameState == GameState.MoveTargetSelection && cursorLocation != lastCursorLocation)
                {
                    PreviewMovement(selectedUnit, cursorLocation);
                }
                lastCursorLocation = cursorLocation;
            }
        }
    }

    public void DebugButtonPressed()
    {
        _map.DebugAction();
    }
    
    public void DebugButtonTwoPressed()
    {
        _map.DebugActionTwo();
    }

    private void DeselectAll()
    {
        selectedUnit = null;
        _map.HideUnitMarker();
        ToDefaultState();
    }

    private void PreviewMovement(Unit unit, Vector3Int targetPos)
    {
        _map.RemoveAllHighlights(1);
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
                _map.HighlightTile(tile, new Color(0.4f,0.3f,0.7f,0.6f), 1);
            }
        }
    }

    private void InputReleasedOnMap(Vector3Int mapPos)
    {
        if (cameraController.ActiveTouchCameraMovement)
            return;
        
        if (selectedUnit != null && gameState == GameState.AttackTargetSelection)
        {
            TryAttackWithUnit(selectedUnit, mapPos);
        }
        else if (_map.IsUnitOn(mapPos) && _map.GetUnitAt(mapPos).Faction == CurrentFaction)
        {
            ToDefaultState();
            UnitSelected(mapPos);
        }
        else if (gameState == GameState.MoveTargetSelection 
                 && !_map.IsUnitOn(mapPos) 
                 && selectedUnit != null)
        {
            gameState = GameState.MovementSelectedPreConfirmation;
            _map.SetHighlightColor(new Color(0,0,0.7f,0.6f), 1);
        }
        else if (selectedUnit != null
                && gameState == GameState.MovementSelectedPreConfirmation)
        {
            TryMoveWithUnit(selectedUnit, mapPos);
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
        selectedUnit = (Unit)_map.GetUnitAt(mapPos);
        selectedUnit.MarkAsSelected();
        if (selectedUnit.MovesLeft > 0)
        {
            ShowReachableTiles(selectedUnit, selectedUnit.MovesLeft);
            gameState = GameState.MoveTargetSelection;
        }
        UpdateLabel();
    }


    private void ToDefaultState()
    {
        _map.RemoveAllHighlights();
        _map.RemoveAllHighlights(1);
        gameState = GameState.Default;
    }

    private bool TryMoveWithUnit(IPlayerUnit playerUnit, Vector3Int mapPos)
    {
        var unit = (Unit) playerUnit;
        if (unit == null || !_map.CanBePlaced(unit, mapPos, false))
            return false;
        
        var path = pathfinder.GetShortestPath(new Point(mapPos.x, mapPos.y)).ConvertAll(p => new Vector3Int(p.X, p.Y, 0));
        
        if (path.Count <= 0) return false;
        
        _map.RemoveAllHighlights();
        _map.RemoveAllHighlights(1);
        unit.BeginMoving(path);
        gameState = GameState.Default;
        return true;
    }

    private bool TryAttackWithUnit(IPlayerUnit playerUnit, Vector3Int targetedPos)
    {
        var unit = (Unit) playerUnit;
        if (unit == null || !validTargets.Contains(targetedPos)) return false;
        var unitPos = unit.Cell;
        var isMelee = _map.AreNeighbors(unitPos,targetedPos);
        var weapon = isMelee ? unit.MeleeWeapon : unit.RangedWeapon;
        var areaOfEffect = _map.AreaOfEffect(unitPos, weapon, targetedPos);
        int[] attackerDice = weapon.RollDamage();
        foreach (var aoePos in areaOfEffect)
        {
            if (!_map.AoEAllowed(unit, weapon, targetedPos, aoePos)) continue;
            
            CreateExplosion(aoePos);
            
            if (!_map.IsUnitOn(aoePos)) continue;
            
            var hitUnit = _map.GetUnitAt(aoePos);
            ResolveAttack(attackerDice, unitPos,unit, targetedPos, aoePos, (Unit)hitUnit);
        }

        unit.HasAttacked();
        ToDefaultState();
        return true;
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
            if (target.CurrentHealth <= 0)
            {
                UnitDies(target);
            }
            if (attacker.CurrentHealth <= 0)
            {
                UnitDies(attacker);
            }
        }
    }

    private void UnitDies(Unit unit)
    {
        unit.FinishedMoving -= UnitFinishedMoving;
        unit.UnitDeath();
        _map.RemoveUnit(unit);
        allUnits.Remove(unit);
    }

    private bool IsMelee(Unit attacker, Unit target)
    {
        var attackpos = _map.WorldToCell(attacker.transform.position);
        var targetpos = _map.WorldToCell(target.transform.position);
        return _map.AreNeighbors(attackpos, targetpos);
    }

    private void CreateExplosion(Vector3Int mapPos)
    {
        Instantiate(explosionPrefab, _map.CellToWorld(mapPos), Quaternion.identity);
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
        foreach (var direction in _map.Cardinal)
        {
            var neighborPos = mapPos + direction.ToVector();
            if (_map.IsDoorAt(neighborPos))
            {
                _map.RemoveDoor(unit, neighborPos);
                
                _map.UpdateFOV(unit.Cell, unit.CurrentVisionRange);
                UpdateVisibleUnits();

                break;
            }
        }
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
        if (selectedUnit != null && selectedUnit.State != UnitState.Moving && selectedUnit.CanAttack)
        {
            _map.RemoveAllHighlights(0);
            _map.RemoveAllHighlights(1);
            PreviewAttack(selectedUnit, _map.WorldToCell(selectedUnit.transform.position), 10);
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
                validTargets.UnionWith(_map.GetStraightLine(unitPos, Map.Direction.North, maxDistance));
                validTargets.UnionWith(_map.GetStraightLine(unitPos, Map.Direction.South, maxDistance));
                validTargets.UnionWith(_map.GetStraightLine(unitPos, Map.Direction.East, maxDistance));
                validTargets.UnionWith(_map.GetStraightLine(unitPos, Map.Direction.West, maxDistance));
                validTargets.UnionWith(_map.GetStraightLine(unitPos, Map.Direction.NorthEast, maxDistance));
                validTargets.UnionWith(_map.GetStraightLine(unitPos, Map.Direction.NorthWest, maxDistance));
                validTargets.UnionWith(_map.GetStraightLine(unitPos, Map.Direction.SouthEast, maxDistance));
                validTargets.UnionWith(_map.GetStraightLine(unitPos, Map.Direction.SouthWest, maxDistance));   
            }
            else
            {
                validTargets = _map.GetFOV(unitPos, maxDistance, true);
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
            _map.HighlightTile(tile, new Color(0.6f, 0f, 0, 0.5f));
        }
    }

    private void AddToValidMeleeTargets(Unit unit, Vector3Int unitPos, Vector3Int pos)
    {
        if (_map.Contains(pos.x, pos.y) && _map.IsUnitOn(pos))
        {
            validTargets.Add(pos);
        }
    }

    private void ShowReachableTiles(Unit unit, int maxDistance)
    {
        var startPos = _map.WorldToCell(unit.transform.position);
        _map.RemoveAllHighlights();
        pathfinder.MaxCost = maxDistance + 1f;
        pathfinder.CurrentUnit = unit;
        
        pathfinder.Dijkstra(new HashSet<Point>{new Point(startPos.x, startPos.y)}, new HashSet<Point>());
        //pathfinder.PrintDistMap();
        
        var nodeList = pathfinder.ReachableList.ToList().ConvertAll(point => new Vector3Int(point.X, point.Y, 0));
        HashSet<Vector3Int> reachable = new HashSet<Vector3Int>();
        foreach (var reachablePos in nodeList)
        {
            if (!_map.CanBePlaced(unit, reachablePos)) continue;    // make sure to only show positions where we could end movement
            
            foreach (var offset in unit.OccupiedTiles)
            {
                reachable.Add(reachablePos + offset); // also highlight tiles for big units
            }
        }
        foreach (var pos in reachable)
        {
            if (!_map.IsUnitOn(pos)) // never highlight tiles with units on it
            {
                _map.HighlightTile(pos, new Color(0, 0.8f, 0, 0.2f));
            }
        }
    }

    private void UnitFinishedMoving(object sender, UnitEventArgs args)
    {
        //var unit = args.unit;
        GlobalFOVUpdate();
        
        if (selectedUnit == args.unit && selectedUnit.MovesLeft > 0)
        {
            ShowReachableTiles(selectedUnit, selectedUnit.MovesLeft);
            gameState = GameState.MoveTargetSelection;
        }
    }

    private void GlobalFOVUpdate()
    {
        _map.ResetFog();
        foreach (var unit in allUnits)
        {
            unit.SetVisible(false);
            if (unit.Faction == CurrentFaction)
            {
                unit.SetVisible(true);
                _map.UpdateFOV(_map.WorldToCell(unit.transform.position), unit.CurrentVisionRange);
            }
        }
        foreach (var unit in allUnits)
        {
            if (unit.Faction != CurrentFaction && !_map.IsInFog(unit.Cell))
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
            SetLabel(_map.CurrentCursorPosition.ToString());
        }
    }

    private void SpawnUnits()
    {
        var mapRules = mod.GetMap(currentMapIndex);
        List<List<Vector3Int>> spawnPointsByFaction = null;
        if (mapRules.Generator == 0) // premade map, load spawnpoints from map
        {
            spawnPointsByFaction =_map.GetSpawnPoints();
        }
        foreach (var ruleFaction in factions)
        {
            var spawnRule = mod.GetSpawnRule(ruleFaction.Type);
            if (spawnRule.RandomPlacement)
            {
                RandomPlacement(ruleFaction);
            }
            else
            {
                List<Vector3Int> spawnPositions;
                if (mapRules.Generator == 0 && spawnPointsByFaction != null)
                {
                    var factionIndex = factions.IndexOf(ruleFaction);
                    spawnPositions = spawnPointsByFaction[factionIndex];
                }
                else
                {
                    spawnPositions = spawnRule.Locations.ConvertAll(elem => new Vector3Int(elem[0], elem[1], 0));
                }
                
                int spawnIndex = 0;
                foreach (var loadOut in ruleFaction.LoadOut)
                {
                    for (int i = 0; i < loadOut.Count; i++)
                    {
                        if (spawnIndex >= spawnPositions.Count)
                            break;
                        var spawnPosition = spawnPositions[spawnIndex];
                        string unitType = loadOut.Unit;
                        SpawnUnit(spawnPosition, unitType, loadOut, ruleFaction);
                        Debug.Log("Unit " + unitType + " spawned at " + spawnPosition);
                        spawnIndex++;
                    }
                }
            }
        }
        
    }
    
    private bool SpawnUnit(Vector3Int spawnPosition, string unitType, RuleLoadout loadout, RuleFaction ruleFaction)
    {
        var unit = Instantiate(unitPrefab, _map.CellToWorld(spawnPosition), Quaternion.identity)
            .GetComponent<Unit>();
        unit.LoadRules(unitType, ruleFaction, loadout);
        if (_map.AddUnit(unit, spawnPosition))
        {
            var spriteRenderer = unit.GetComponentInChildren<SpriteRenderer>();
            //spriteRenderer.color = ColorFromArray(ruleFaction.Color);
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
        
        var newPos = _map.WorldToCell(unit.transform.position);

        _map.IsStandingOnDoor(unit, true);
        
        _map.UpdateFOV(newPos, unit.CurrentVisionRange);
        UpdateVisibleUnits();
    }

    private void UpdateVisibleUnits()
    {
        foreach (var unit in allUnits)
        {
            var unitPos = _map.WorldToCell(unit.transform.position);
            var shouldBeVisible = unit.Faction == CurrentFaction
                                  || !_map.IsInFog(unitPos);
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
                Vector3Int randomPosition = _map.RandomPosition;
                int tryCounter = 0;
                while (!SpawnUnit(randomPosition, loadOut.Unit, loadOut, ruleFaction) && tryCounter < 5)
                {
                    // wait for free spawn pos
                    randomPosition = _map.RandomPosition;
                    tryCounter++;
                }
            }
        }
    }
}
