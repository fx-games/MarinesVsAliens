using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using DefaultNamespace;
using GridPathFinder;
using UnityEngine;
using UnityEngine.Assertions.Comparers;
using UnityEngine.EventSystems;
using UnityEngine.Serialization;
using UnityEngine.UI;
using static Mod;
using static Mod.RuleFaction;
using static Unit;
using Color = UnityEngine.Color;

public class Game : MonoBehaviour, IGame
{
    public enum GameState
    {
        Default,
        MoveTargetSelection,
        AttackTargetSelection,
        MovementSelectedPreConfirmation,
        WaitForHotSeatPlayer
    }
    public GameObject unitLabelPrefab;
    public Transform canvasTransform;
    public GameObject gridMapPrefab;
    public GameObject[] premadeMapBlocks;

    public GameObject[] unitPrefabs;
    public GameObject explosionPrefab;
    private List<Unit> allUnits = new List<Unit>();
    private HashSet<RuleFaction> eliminatedFactions = new HashSet<RuleFaction>();
    private Map _map;
    public Unit selectedUnit;
    private Camera gameCamera;
    public Text topLeftLabel;
    public Text centerOfScreenLabel;
    private Pathfinder pathfinder;
    private GameState gameState = GameState.Default;
    private HashSet<Vector3Int> validTargets = new HashSet<Vector3Int>();
    private Mod mod;
    private List<RuleFaction> factions;
    private int phaseFaction;
    private CameraControls cameraController;
    private Vector3Int lastCursorLocation;
    private int currentMapIndex;
    private int currentTurn = 0;
    private HashSet<Vector3Int> multipleTargets = new HashSet<Vector3Int>();
    private Dictionary<RuleFaction, AIPlayer> aiPlayers = new Dictionary<RuleFaction, AIPlayer>();
    private double lastAiCommandTime;
    private double aiCommandDelay = 0.5f;
    private bool spectatorMode = false;

    private void Awake()
    {
        centerOfScreenLabel.enabled = false;
        mod = GetComponent<Mod>();
        gameCamera = Camera.main;
        if (gameCamera == null)
        {
            throw new Exception("Game needs camera.");
        }

        Options.CombatMechanics.UnitKilled += (sender, args) => KillUnit(args.unit);
    }

    void Start()
    {
        cameraController = gameCamera.GetComponent<CameraControls>();
        factions = mod.GetFactions();
        currentMapIndex = (0);
        LoadMap();
    }

    void EnableSpectatorMode()
    {
        spectatorMode = true;
        _map.EnableSpectatorMode();
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
        
        NewTurn();
        PrepareNextPhase();

        CreateAIPlayers();
        
        //DEBUG
        EnableSpectatorMode();
    }

    private void CreateAIPlayers()
    {
        foreach (var faction in factions)
        {
            var playerAi = new AIPlayer(this, mod, _map, faction);
            aiPlayers[faction] = playerAi;
        }
    }

    // Update is called once per frame
    void Update()
    {
        if (gameState != GameState.WaitForHotSeatPlayer 
            && aiPlayers.ContainsKey(CurrentFaction) 
            && Time.time - lastAiCommandTime > aiCommandDelay)
        {
            aiPlayers[CurrentFaction].ExecuteNextCommand();
            lastAiCommandTime = Time.time;
            return;
        }
        
        if (Input.GetMouseButtonDown(1))
        {
            DeselectAll();
        }

        if (EventSystem.current.IsPointerOverGameObject()) return;

        Vector3 pos = gameCamera.ScreenToWorldPoint(Input.mousePosition);

        if (_map.ContainsWorldPos(pos))
        {
            var cursorLocation = _map.WorldToCell(pos);
            if (Input.GetMouseButtonUp(0))
            {
                InputReleasedOnMap(cursorLocation);
            }

            if (selectedUnit != null && gameState == GameState.MoveTargetSelection &&
                cursorLocation != lastCursorLocation)
            {
                //Debug.Log("PreviewMovement at " + cursorLocation);
                PreviewMovement(selectedUnit, cursorLocation);
            }

            //Debug.Log("lastCursorLocation at " + cursorLocation);
            lastCursorLocation = cursorLocation;
        }
        else
        {
            if (Input.GetMouseButtonDown(0))
            {
                DeselectAll();
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
        
        if (selectedUnit != null && gameState == GameState.AttackTargetSelection && validTargets.Contains(mapPos))
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

    private void TryAttackWithUnit(Unit unit, Vector3Int targetedPos, bool isMultipleTargetConfirmation = false)
    {
        if (unit == null) return;
        var unitPos = unit.Cell;
        var isMelee = _map.AreNeighbors(unitPos,targetedPos);
        var weapon = isMelee ? unit.MeleeWeapon : unit.RangedWeapon;

        if (weapon.Rules.MultipleTargets && !isMultipleTargetConfirmation)
        {
            multipleTargets.Add(targetedPos);
            return;
        }
        
        ToDefaultState();
        
        unit.TurnTowards(targetedPos);
        var multipleTargetPositions = new HashSet<Vector3Int>(new []{targetedPos});
        if (weapon.Rules.MultipleTargets && isMultipleTargetConfirmation)
        {
            multipleTargetPositions = multipleTargets;
        }

        bool firstPos = true;
        int attackScore = -1;
        int[] attackValues = new int[0];
        int damageLeft = -1;
        string scoreForLabel = "";
        foreach (var multiHitPosition in multipleTargetPositions)
        {
            if (firstPos || !weapon.Rules.DamageDistribution)
            {
                attackValues = Options.CombatMechanics.AttackValues(isMelee, unit, multiHitPosition);
                attackScore = attackValues.Sum();    // set on first pos for distributed and everytime else
                scoreForLabel = attackScore.ToString();
                firstPos = false;
            }

            if (weapon.Rules.DamageDistribution & damageLeft != -1)
            {
                if (damageLeft == 0)
                {
                    break;
                }
                else if (damageLeft > 0)
                {
                    attackValues = new[] {damageLeft};    // adjust dice left
                    attackScore = attackValues.Sum();
                }
            }

            var areaOfEffect = _map.AreaOfEffect(unitPos, weapon, multiHitPosition);
        
            foreach (var aoePos in areaOfEffect)
            {
                if (!_map.AoEAllowed(unit, weapon, multiHitPosition, aoePos)) continue;
        
                CreateExplosion(aoePos);
        
                if (!_map.IsUnitOn(aoePos)) continue;
        
                var hitUnit = (Unit)_map.GetUnitAt(aoePos);
            
                hitUnit.TurnTowards(unit.Cell);
                
                damageLeft = Options.CombatMechanics.ResolveAttack(attackValues, unitPos,unit, multiHitPosition, aoePos, hitUnit);
            
                if (attackScore > 0 && isMelee && Options.CombatMechanics is DiceBasedCombat)
                {
                    var defense = ((DiceBasedCombat) Options.CombatMechanics).LastMeleeDefense;
                    hitUnit.SetLabelText(defense.ToString());
                }
            }
        }
        

        unit.HasAttacked();
        
        unit.SetLabelText(scoreForLabel);
    }


    private void UnitSelected(Vector3Int mapPos)
    {
        if (_map.IsUnitOn(mapPos))
        {
            selectedUnit = (Unit) _map.GetUnitAt(mapPos);
            selectedUnit.MarkAsSelected();
            if (selectedUnit.MovesLeft > 0)
            {
                ShowReachableTiles(selectedUnit, selectedUnit.MovesLeft);
                gameState = GameState.MoveTargetSelection;
            }

            UpdateLabel();
        }
    }


    private void ToDefaultState()
    {
        _map.RemoveAllHighlights();
        _map.RemoveAllHighlights(1);
        allUnits.ForEach(unit => unit.RemoveLabel());
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
    
    private void KillUnit(Unit unit)
    {
        unit.FinishedMoving -= UnitFinishedMoving;
        unit.UnitDeath();
        _map.RemoveUnit(unit);
        allUnits.Remove(unit);
    }

    private void CreateExplosion(Vector3Int mapPos)
    {
        Instantiate(explosionPrefab, _map.CellToWorld(mapPos), Quaternion.identity);
    }

    public void OpenDoorButtonClicked()
    {
        if (aiPlayers.ContainsKey(CurrentFaction)) return;
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
                UpdateVisibleUnits(unit);

                break;
            }
        }
    }

    public void EndTurnButtonClicked()
    {
        if (gameState == GameState.WaitForHotSeatPlayer)
        {
            StartNextPhase();
        }
        else
        {
            if (!IsMissionOver())
            {
                PrepareNextPhase();
            }
        }
    }

    private void StartWaitingForHeatSeatPlayer(RuleFaction faction)
    {
        
        selectedUnit = null;
        _map.HideUnitMarker();
        allUnits.ForEach(unit => { unit.SetVisible(false); unit.Deselect(); });

        if (spectatorMode)
        {
            gameState = GameState.Default;
            StartNextPhase();
            return;
        }
        gameState = GameState.WaitForHotSeatPlayer;
        centerOfScreenLabel.enabled = true;
        centerOfScreenLabel.text = "NEXT PLAYER\n" + faction;        
        _map.ResetFog();
        UpdateLabel();
        CenterCameraOn(new Vector3Int(_map.Width / 2, _map.Height / 2, 0));
    }

    

    private bool IsMissionOver()
    {
        Dictionary<RuleFaction, int> unitCounts = new Dictionary<RuleFaction, int>();
        foreach (var faction in factions)
        {
            unitCounts[faction] = 0;
        }
        foreach (var unit in allUnits)
        {
            unitCounts[unit.Faction]++;
        }

        foreach (var kvp in unitCounts)
        {
            var unitCount = kvp.Value;
            if (unitCount != 0) continue;
            
            var faction = kvp.Key;
            factions.Remove(faction);
            eliminatedFactions.Add(faction);
            Debug.Log(faction.Type + " has been eliminated.");
        }

        if (factions.Count <= 1)
        {
            //only one or zero faction left
            MissionDebriefing();
            return true;
        }

        return false;
    }

    private void MissionDebriefing()
    {
        _map.RemoveAllFog();
        Debug.Log("Mission is over!");
    }

    private void PrepareNextPhase()
    {
        ToDefaultState();
        phaseFaction++;
        if (phaseFaction > factions.Count - 1)
        {
            phaseFaction = 0;
            NewTurn();
        }
        
        StartWaitingForHeatSeatPlayer(CurrentFaction);
    }

    private void StartNextPhase()
    {
        gameState = GameState.Default;
        centerOfScreenLabel.enabled = false;
            
        bool firstUnitFound = false;
        foreach (var unit in allUnits)
        {
            if (unit.Faction != CurrentFaction) continue;
            unit.PrepareNewPhase();
            
            if (firstUnitFound || spectatorMode) continue;
            firstUnitFound = true;            
            UnitSelected(unit.Cell);
            CenterCameraOn(unit);
        }

        GlobalFOVUpdate();

        if (aiPlayers.ContainsKey(CurrentFaction))
        {
            aiPlayers[CurrentFaction].NextPhase();
        }
    }

    private void NewTurn()
    {
        currentTurn++;
        Debug.Log("Turn :" + currentTurn);
    }

    private void CenterCameraOn(Unit unit)
    {
        var unitPos = unit.transform.position;
        gameCamera.GetComponent<CameraControls>().SmoothTransition(unitPos);
    }
    private void CenterCameraOn(Vector3Int pos)
    {
        gameCamera.GetComponent<CameraControls>().SmoothTransition(_map.CellToWorld(pos));
    }

    public void AttackButtonClicked()
    {
        if (selectedUnit == null) return;
        
        if (aiPlayers.ContainsKey(CurrentFaction)) return;

        if (gameState == GameState.AttackTargetSelection && multipleTargets.Count > 0)
        {
            if (selectedUnit.HasMeleeWeapon)
            {
                if (selectedUnit.MeleeWeapon.Rules.DamageDistribution)
                {
                    TryAttackWithUnit(selectedUnit, multipleTargets.First(), true);
                }
            }
            if (selectedUnit.HasRangedWeapon)
            {
                if (selectedUnit.RangedWeapon.Rules.DamageDistribution)
                {
                    TryAttackWithUnit(selectedUnit, multipleTargets.First(), true);
                }
            }
        }
        if (selectedUnit.State != UnitState.Moving && selectedUnit.CanAttack)
        {
            _map.RemoveAllHighlights(0);
            _map.RemoveAllHighlights(1);
            PreviewAttack(selectedUnit, _map.WorldToCell(selectedUnit.transform.position), 10);
            var unitsInRange = validTargets.Where(pos => _map.IsOtherFactionOn(selectedUnit, pos))
                .Select(pos => _map.GetUnitAt(pos));
            foreach (var unit in unitsInRange)
            {
                string info = Options.CombatMechanics.AttackPreviewInformationFor((Unit) unit, selectedUnit);
                ((Unit)unit).SetLabelText(info);
            }

            if (validTargets.Count > 0)
            {
                gameState = GameState.AttackTargetSelection;
                multipleTargets.Clear();
            }
            else
            {
                Debug.Log("No Targets");
            }
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
            AddToValidTargets(unit,unitPos,unitPos + Vector3Int.right);
            AddToValidTargets(unit,unitPos,unitPos + Vector3Int.up);
            AddToValidTargets(unit,unitPos,unitPos + Vector3Int.down);
            AddToValidTargets(unit,unitPos,unitPos + Vector3Int.left);
        }

        if (!Options.AllowDiagonalAdjacentAttacks)
        {
            validTargets.Remove(unitPos + Vector3Int.up + Vector3Int.right);
            validTargets.Remove(unitPos + Vector3Int.down + Vector3Int.left);
            validTargets.Remove(unitPos + Vector3Int.down + Vector3Int.right);
            validTargets.Remove(unitPos + Vector3Int.up + Vector3Int.left);
        }

        foreach (var tile in validTargets)
        {
            _map.HighlightTile(tile, new Color(0.6f, 0f, 0, 0.5f));
        }
    }

    private void AddToValidTargets(Unit unit, Vector3Int unitPos, Vector3Int pos)
    {
        if (_map.Contains(pos.x, pos.y) && !_map.IsWallAt(pos))
        {
            validTargets.Add(pos);
        }
    }

    private void ShowReachableTiles(Unit unit, int maxDistance)
    {
        _map.RemoveAllHighlights();
        var reachableMapPositions = GetReachableMapPositions(unit);
        
        foreach (var pos in reachableMapPositions)
        {
            if (!_map.IsUnitOn(pos)) // never highlight tiles with units on it
            {
                _map.HighlightTile(pos, new Color(0, 0.8f, 0, 0.2f));
            }
        }
    }

    public HashSet<Vector3Int> GetReachableMapPositions(Unit unit)
    {
        var startPos = unit.Cell;
        pathfinder.MaxCost = unit.MovesLeft + 1f;
        pathfinder.CurrentUnit = unit;

        pathfinder.Dijkstra(new HashSet<Point> {new Point(startPos.x, startPos.y)}, new HashSet<Point>());
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
        return reachable;
    }

    public void AttackWithUnit(Unit unit, List<Vector3Int> targets)
    {
        foreach (var target in targets)
        {
            TryAttackWithUnit(unit, target);
        }
        
        if (targets.Count > 1)
        {
            TryAttackWithUnit(unit, targets.First(), true);
        }
    }

    private void UnitFinishedMoving(object sender, UnitEventArgs args)
    {
        if (gameState == GameState.WaitForHotSeatPlayer) return;
        
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
                var fov = _map.GetFOV(_map.WorldToCell(unit.transform.position), unit.CurrentVisionRange);
                unit.FieldOfView = fov;
                unit.VisibleEnemies = new HashSet<IPlayerUnit>(unit.FieldOfView.Where(pos => _map.IsOtherFactionOn(CurrentFaction, pos)).Select(pos => _map.GetUnitAt(pos)));
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
        if (gameState == GameState.WaitForHotSeatPlayer)
        {
            SetLabel("");
            return;
        }
        if (selectedUnit != null)
        {
            SetLabel(selectedUnit.name + " - HP: " + selectedUnit.CurrentHealth + "\n" + selectedUnit.WeaponDescription);
        }
        else if (_map.IsOtherFactionOn(CurrentFaction, _map.CurrentCursorPosition))
        {
            var enemyUnit = (Unit)_map.GetUnitAt(_map.CurrentCursorPosition);
            SetLabel(enemyUnit.name + " - HP: " + enemyUnit.CurrentHealth + "\n" + enemyUnit.WeaponDescription);
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
        var unitPrefab = FindPrefab(unitType, loadout, ruleFaction);
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
            
            var unitLabel = Instantiate(unitLabelPrefab).GetComponent<Text>();
            unitLabel.transform.SetParent(canvasTransform);
            unitLabel.enabled = false;
            unit.SetLabel(unitLabel);
            
            allUnits.Add(unit);
            
            //gameMap.UpdateFOV(spawnPosition, unit.visionRange);
            return true;
        }

        Destroy(unit.gameObject);
        Debug.LogWarning("Could not spawn unit at: " + spawnPosition);
        return false;
    }

    private GameObject FindPrefab(string unitType, RuleLoadout loadout, RuleFaction ruleFaction)
    {
        var unitSprite = mod.GetUnitRule(unitType).UnitSprite;
        var ruleWeapons = loadout.Weapons.Select(weaponName => mod.GetWeaponRule(weaponName));
        var prefabName = "";
        var weaponRules = ruleWeapons as RuleWeapon[] ?? ruleWeapons.ToArray();
        if (weaponRules.Any(rule => rule.WeaponSprite.Length > 0))
        {
            var weaponRuleWithSprite = weaponRules.First(rule => rule.WeaponSprite.Length > 0);
        
            if (weaponRuleWithSprite != null)
            {
                prefabName = ruleFaction.SpritePrefix + weaponRuleWithSprite.WeaponSprite;
            }
        }

        if (unitSprite.Length > 0)
        {
            prefabName = ruleFaction.SpritePrefix + unitSprite;
        }

        if (prefabName == "")
        {
            Debug.Log("NO PREFAB FOR " + unitType);
        }
        Debug.Log("Trying to load prefab " + prefabName);
        var unitPrefab = unitPrefabs.First(go => go.name == prefabName);
        return unitPrefab;
    }

    private void UnitStepCompleted(object sender, UnitEventArgs args)
    {
        var unit = args.unit;
        var oldPos = args.previousPosition;
        
        var newPos = _map.WorldToCell(unit.transform.position);

        _map.IsStandingOnDoor(unit, true);

        if (gameState == GameState.WaitForHotSeatPlayer) return;
        
        _map.UpdateFOV(newPos, unit.CurrentVisionRange);
        
        UpdateVisibleUnits(unit);
    }

    private void UpdateVisibleUnits(Unit sourceUnit)
    {
        foreach (var unit in allUnits)
        {
            var unitPos = _map.WorldToCell(unit.transform.position);
            var shouldBeVisible = unit.Faction == CurrentFaction
                                  || !_map.IsInFog(unitPos);
            if (shouldBeVisible && unit.Faction != CurrentFaction)
            {
                unit.TurnTowards(sourceUnit.Cell);
            }
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

    public Vector3Int GetBestMoveTowards(Unit unit, Vector3Int goalPosition)
    {
        var shortestPath = GetShortestPath(unit, goalPosition, false);
        if (shortestPath.Count == 0)
        {
            Debug.LogError("No path for " + unit + " at " + unit.Cell + " to " + goalPosition);
            Debug.LogError(_map.GetPosDebugInfo(goalPosition));
            return unit.Cell;
        }
        Debug.Log(PathToString(shortestPath));
        
        var bestMoveTowards = shortestPath.Last();
        if (shortestPath.Count > unit.MovesLeft + 1)
        {
            bestMoveTowards = shortestPath.Skip(unit.MovesLeft).First();
        }
        
        if (!_map.IsUnitOn(bestMoveTowards)) return bestMoveTowards;
        
        var reachable = GetReachableMapPositions(unit).ToList();

        if (reachable.Count == 0)
        {
            Debug.LogError("No reachable pos for " + unit + " at " + unit.Cell + " to " + goalPosition);
            Debug.LogError(_map.GetPosDebugInfo(goalPosition));
            return unit.Cell;
        }
        reachable.Sort((posOne, posTwo) =>  (posOne - goalPosition).magnitude.CompareTo((posTwo - goalPosition).magnitude));

        bestMoveTowards = reachable.First();
        // problem left: There could be a unit on any position on the path
        // especially on bestMoveTowards..
        
        return bestMoveTowards;
    }

    private string PathToString(List<Vector3Int> shortestPath)
    {
        string output = "";

        foreach (var pos in shortestPath)
        {
            output += pos.ToString();
        }

        return output;
    }

    public void EndPhase(AIPlayer aiPlayer)
    {
        if (aiPlayer.Faction == CurrentFaction)
        {
            EndTurnButtonClicked();
        }
    }

    public void MoveUnit(Unit unit, Vector3Int targetPos)
    {
        UnitSelected(unit.Cell);
        TryMoveWithUnit(unit, targetPos);
    }

    private List<Vector3Int> GetShortestPath(Unit unit, Vector3Int goalPosition, bool useMovesLeftAsMaxDistance = true)
    {
        pathfinder.MaxCost = useMovesLeftAsMaxDistance ? unit.MovesLeft + 1f : -1;
        pathfinder.CurrentUnit = unit;
        pathfinder.Dijkstra(new HashSet<Point>(new[] {unit.Cell.ToPoint()}), new HashSet<Point>() {goalPosition.ToPoint()});
        return pathfinder.GetShortestPath(goalPosition.ToPoint()).Select(point => new Vector3Int(point.X, point.Y, 0)).ToList();
    }

    public List<Unit> GetAllUnits(AIPlayer aiPlayer)
    {
        var factionOfPlayer = aiPlayer.Faction;
        return allUnits.Where(unit => unit.Faction == factionOfPlayer).ToList();
    }

    public List<Unit> GetVisibleEnemies(AIPlayer aiPlayer)
    {
        return allUnits.Where(unit => unit.Faction != aiPlayer.Faction && !_map.IsInFog(unit.Cell)).ToList();
    }
}