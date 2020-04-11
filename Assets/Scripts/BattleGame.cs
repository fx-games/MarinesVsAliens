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

public class BattleGame : MonoBehaviour, IGame
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
    private GameState gameState = GameState.WaitForHotSeatPlayer;
    private Mod mod;
    private List<RuleFaction> factions;
    private int phaseFaction = 0;
    private CameraControls cameraController;
    private Vector3Int lastCursorLocation;
    private int currentMapIndex;
    private int currentTurn = 0;
    private HashSet<Vector3Int> multipleTargets = new HashSet<Vector3Int>();
    private Dictionary<RuleFaction, AIPlayer> aiPlayers = new Dictionary<RuleFaction, AIPlayer>();
    private double lastAiCommandTime;
    private double aiCommandDelay = 0.8f;
    private bool spectatorMode = false;
    private Coroutine currentAiCoroutine;
    private RuleFaction singlePlayerFaction;
    private Vector3Int movePreConfirmedAt;
    private List<GameObject> uiButtons = new List<GameObject>();
    public GameObject endTurnButton;
    public GameObject openDoorButton;
    public GameObject attackButton;

    private void Awake()
    {
        centerOfScreenLabel.enabled = false;
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
            _map.FromPrefab();
        }
        else
        {
            _map = Instantiate(gridMapPrefab).GetComponent<Map>();
            _map.FromRules(mapRules);
        }
        
        Invoke(nameof(AfterMapLoad), 0.1f);
    }

    public void AfterMapLoad()
    {
        if (GameController.ChosenMode == GameController.GameMode.AgainstAI)
        {
            singlePlayerFaction = CurrentFaction;
            Debug.Log("Singleplayer Faction: " + singlePlayerFaction);
        }
        if (GameController.ChosenMode != GameController.GameMode.HotSeat)
        {
            CreateAIPlayers();
        }

        if (GameController.ChosenMode == GameController.GameMode.WatchAI)
        {
            EnableSpectatorMode();
        }

        phaseFaction = -1; // start with player 0 on first phase
        
        SpawnUnits();
        
        NextTurn();
        
        PrepareNextPhase();
    }

    void EnableSpectatorMode()
    {
        spectatorMode = true;
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
        if (factions.Count <= 1) return;
        /* Automatic AI moves disabled */
        if (gameState != GameState.WaitForHotSeatPlayer 
            && (spectatorMode || (GameController.ChosenMode == GameController.GameMode.AgainstAI && CurrentFaction != singlePlayerFaction)))
        {
            Debug.Log("CurrentFaction (AI Move): " + CurrentFaction);
            if (aiPlayers[CurrentFaction].State == AIState.Idle && currentAiCoroutine != null)
            {
                StopCoroutine(currentAiCoroutine);
            }
            if (Time.time - lastAiCommandTime > aiCommandDelay)
            {
                ToDefaultState();

                currentAiCoroutine = StartCoroutine(aiPlayers[CurrentFaction].ExecuteNextCommand());
                lastAiCommandTime = Time.time;
            }

            return;
            
        }
#if UNITY_IOS || UNITY_ANDROID
        if (EventSystem.current.IsPointerOverGameObject(0)) return; // Touch devices
#else
        if (EventSystem.current.IsPointerOverGameObject()) return;           // Desktop & Web
#endif
        if (Input.GetMouseButtonDown(1)) // Right-Click on Desktop
        {
            DeselectAll();
        }

        Vector3 pos = gameCamera.ScreenToWorldPoint(Input.mousePosition);

        if (_map.ContainsWorldPos(pos))
        {
            var cursorLocation = _map.WorldToCell(pos);
            if (Input.GetMouseButtonUp(0))
            {
                InputReleasedOnMap(cursorLocation);
            }

            if (selectedUnit != null && gameState == GameState.MoveTargetSelection &&
                cursorLocation != lastCursorLocation && selectedUnit.ReachablePositions.Contains(cursorLocation))
            {
                PreviewMovement(selectedUnit, cursorLocation);
            }
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
        //aiPlayers[CurrentFaction].NextPhase();
        if (gameState != GameState.WaitForHotSeatPlayer)
        {
            ToDefaultState();
            currentAiCoroutine = StartCoroutine(aiPlayers[CurrentFaction].ExecuteNextCommand());
            lastAiCommandTime = Time.time;
        }
    }
    
    public void DebugButtonTwoPressed()
    {
        //
    }

    private void DeselectAll()
    {
        selectedUnit = null;
        _map.HideUnitMarker();
        _map.RemoveAllTileMarker();
        ToDefaultState();
        UpdateUIButtons();
        
    }

    private void ToDefaultState()
    {
        _map.RemoveAllHighlights();
        _map.RemoveAllHighlights(1);
        allUnits.ForEach(unit => unit.RemoveLabel());
        gameState = GameState.Default;
    }

    private void PreviewMovement(Unit unit, Vector3Int targetPos)
    {
        var path = unit.GetShortestPath(targetPos);
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
            HighlightTiles(previewTiles.ToList(), new Color(0.4f,0.3f,0.7f,0.6f), 1);
        }
    }


    private void InputReleasedOnMap(Vector3Int mapPos)
    {
        if (cameraController.ActiveTouchCameraMovement)
            return;
        
        if (selectedUnit != null && gameState == GameState.AttackTargetSelection && selectedUnit.ValidTargets.Contains(mapPos))
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
            movePreConfirmedAt = mapPos;
            gameState = GameState.MovementSelectedPreConfirmation;
            _map.SetHighlightColor(new Color(0,0,0.7f,0.6f), 1);
        }
        else if (selectedUnit != null
                && gameState == GameState.MovementSelectedPreConfirmation && movePreConfirmedAt == mapPos)
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

        if (!unit.ValidTargets.Contains(targetedPos))
            return;
        
        var unitPos = unit.Cell;
        var isMelee = _map.AreNeighbors(unitPos,targetedPos);
        var weapon = isMelee ? unit.MeleeWeapon : unit.RangedWeapon;

        if (weapon.Rules.MultipleTargets && !isMultipleTargetConfirmation)
        {
            _map.MarkTile(targetedPos);
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
            
                if (attackScore > 0 && isMelee && Options.CombatMechanics is DiceBasedCombat && CurrentFaction == PoVFaction)
                {
                    var defense = ((DiceBasedCombat) Options.CombatMechanics).LastMeleeDefense;
                    hitUnit.SetLabelText(defense.ToString());
                }
            }
        }
        

        unit.HasAttacked();

        if (CurrentFaction == PoVFaction)
        {
            unit.SetLabelText(scoreForLabel);
        }
        
        multipleTargets.Clear();

        _map.RemoveAllTileMarker();
        CheckForDeadUnits();
        UpdateUIButtons();
    }

    private void CheckForDeadUnits()
    {
        for (var index = allUnits.Count - 1; index >= 0; index--)
        {
            Unit unit = allUnits[index];
            if (unit.CurrentHealth <= 0)
            {
                KillUnit(unit);
            }
        }
        GlobalPathfindingUpdate();
    }


    private void UnitSelected(Vector3Int mapPos)
    {
        if (_map.IsUnitOn(mapPos))
        {
            selectedUnit = (Unit) _map.GetUnitAt(mapPos);
            Debug.Log("UnitSelected: " + selectedUnit);
            if (selectedUnit.MovesLeft > 0)
            {
                gameState = GameState.MoveTargetSelection;
            }
            HighlightTiles(selectedUnit.ReachablePositions, new Color(0, 0.8f, 0, 0.2f));
            
            if (CurrentFaction == PoVFaction)
            {
                selectedUnit.Mark();
                UpdateLabel();
            }

            UpdateUIButtons();
        }
    }

    private bool TryMoveWithUnit(IPlayerUnit playerUnit, Vector3Int mapPos)
    {
        var unit = (Unit) playerUnit;
        if (unit == null || !_map.CanBePlaced(unit, mapPos, false))
            return false;
        
        var path = unit.GetShortestPath(mapPos);
        
        if (path.Count <= 0 || path.Count - 1 > unit.MovesLeft) return false;
        
        _map.RemoveAllHighlights();
        _map.RemoveAllHighlights(1);
        unit.Move(path);
        gameState = GameState.Default;
        return true;
    }
    
    private void KillUnit(Unit unit)
    {
        foreach (var otherUnit in allUnits)
        {
            if (unit != otherUnit) otherUnit.VisibleEnemies.Remove(unit);
        }
        
        unit.FinishedMoving -= UnitFinishedMoving;
        unit.StepCompleted -= UnitStepCompleted;
        _map.RemoveUnit(unit);
        allUnits.Remove(unit);
        unit.UnitDeath();
    }

    private void CreateExplosion(Vector3Int mapPos)
    {
        Instantiate(explosionPrefab, _map.CellToWorld(mapPos), Quaternion.identity);
    }

    public void OpenDoorButtonClicked()
    {
        Debug.Log("OpenDoorButtonClicked() FIRED: " + CurrentFaction + " / " + PoVFaction);
        if (CurrentFaction != PoVFaction) return;
        Debug.Log("SelectedUnit: " + selectedUnit);
        if (selectedUnit != null)
        {
            OpenDoor(selectedUnit);
        }
    }

    private void OpenDoor(Unit unit)
    {
        Debug.Log("OpenDoor() FIRED: " + unit);
        foreach (var direction in _map.Cardinal)
        {
            var neighborPos = unit.Cell + direction.ToVector();
            if (_map.IsDoorAt(neighborPos))
            {
                _map.RemoveDoor(unit, neighborPos);

                unit.UpdateFoV(unit.Faction == PoVFaction, true);
                
                UpdateVisibleUnits(unit);

                break;
            }
        }
    }

    public void EndTurnButtonClicked()
    {
        if (CurrentFaction != PoVFaction) return;
        EndPhase();
    }

    private void EndPhase()
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
    
    public void EndPhase(AIPlayer aiPlayer)
    {
        if (aiPlayer.Faction == CurrentFaction)
        {
            EndPhase();
        }
    }

    private void StartWaitingForHeatSeatPlayer(RuleFaction faction)
    {
        selectedUnit = null;
        _map.HideUnitMarker();
        allUnits.ForEach(unit => { unit.SetVisible(false); unit.Deselect(); });

        if (spectatorMode || CurrentFaction != PoVFaction)
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
        allUnits.ForEach(u => u.SetVisible(true));
        Debug.Log("Mission is over!");
    }

    private void PrepareNextPhase()
    {
        ToDefaultState();
        phaseFaction++;
        if (phaseFaction > factions.Count - 1)
        {
            phaseFaction = 0;
            NextTurn();
        }
        
        PoVFaction = CurrentFaction;
        if (GameController.ChosenMode == GameController.GameMode.AgainstAI)
        {
            PoVFaction = singlePlayerFaction;
        }
        
        StartWaitingForHeatSeatPlayer(CurrentFaction);
    }

    public RuleFaction PoVFaction { get; set; }

    private void StartNextPhase()
    {
        gameState = GameState.Default;
        centerOfScreenLabel.enabled = false;

        if (CurrentFaction != PoVFaction)
        {
            centerOfScreenLabel.text = "HIDDEN MOVEMENT";
            centerOfScreenLabel.enabled = true;
        }
        
        bool firstUnitFound = false;
        foreach (var unit in allUnits)
        {
            if (unit.Faction != CurrentFaction) continue;
            unit.PrepareNewPhase();
            
            if (firstUnitFound || spectatorMode || (GameController.ChosenMode == GameController.GameMode.AgainstAI && CurrentFaction != singlePlayerFaction)) continue;
            firstUnitFound = true;            
            UnitSelected(unit.Cell);
            CenterCameraOn(unit);
        }

        GlobalFOVUpdate();
        
        if (aiPlayers.ContainsKey(CurrentFaction) && CurrentFaction != singlePlayerFaction)
        {
            aiPlayers[CurrentFaction].NextPhase();
        }
    }

    private void NextTurn()
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
        if (CurrentFaction != PoVFaction) return;
        
        //if (aiPlayers.ContainsKey(CurrentFaction) ) return;

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
            ShowValidTargets(selectedUnit);
            var unitsInRange = selectedUnit.ValidTargets.Where(pos => _map.IsOtherFactionOn(selectedUnit, pos))
                .Select(pos => _map.GetUnitAt(pos));
            if (CurrentFaction == PoVFaction)
            {
                foreach (var unit in unitsInRange)
                {
                    string info = Options.CombatMechanics.AttackPreviewInformationFor(selectedUnit, (Unit) unit);
                    ((Unit) unit).SetLabelText(info);
                }
            }

            if (selectedUnit.ValidTargets.Count > 0)
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

    private void ShowValidTargets(Unit unit)
    {
        HighlightTiles(unit.ValidTargets.ToList(), new Color(0.6f, 0f, 0, 0.5f));
    }


    private void HighlightTiles(List<Vector3Int> positions, Color color, int layer = 0)
    {
        _map.RemoveAllHighlights(layer);
        if (CurrentFaction == PoVFaction)
        {
            foreach (var pos in positions)
            {
                _map.HighlightTile(pos, color, layer);
            }
        }
    }

    public void AttackWithUnit(Unit unit, List<Vector3Int> targets)
    {
        UnitSelected(unit.Cell);
        
        AttackButtonClicked();
        
        foreach (var target in targets)
        {
            TryAttackWithUnit(unit, target);
        }

        var isMelee = targets.TrueForAll(pos => _map.AreNeighbors(unit.Cell, pos));
        if ((isMelee && unit.HasMeleeWeapon && unit.MeleeWeapon.Rules.DamageDistribution)
            ||(!isMelee && unit.HasRangedWeapon && unit.RangedWeapon.Rules.DamageDistribution))
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
            gameState = GameState.MoveTargetSelection;
            HighlightTiles(selectedUnit.ReachablePositions, new Color(0, 0.8f, 0, 0.2f));
        }
        
        UpdateUIButtons();
    }

    private void GlobalPathfindingUpdate()
    {
        allUnits.ForEach(u => u.UpdateReachablePositions());
    }
    private void GlobalFOVUpdate()
    {
        _map.ResetFog();
        
        foreach (var unit in allUnits)
        {
            unit.SetVisible(false);

            if (unit.Faction == PoVFaction)
            {
                unit.SetVisible(true);
            }
            unit.UpdateFoV(unit.Faction == PoVFaction, true);
        }
        foreach (var unit in allUnits)
        {
            if (unit.Faction != PoVFaction && !_map.IsInFogVisually(unit.Cell))
            {
                unit.SetVisible(true);
            }
        }
    }

    private void UpdateVisibleUnits(Unit sourceUnit)
    {
        foreach (var unit in allUnits)
        {
            var unitPos = _map.WorldToCell(unit.transform.position);
            var shouldBeVisible = unit.Faction == PoVFaction
                                  || !_map.IsInFogVisually(unitPos);
            if (shouldBeVisible && unit.Faction != CurrentFaction)
            {
                unit.TurnTowards(sourceUnit.Cell);
            }
            unit.SetVisible(shouldBeVisible);
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

        _map.IsStandingOnDoor(unit, true);

        if (gameState == GameState.WaitForHotSeatPlayer) return;
        
        unit.UpdateFoV(unit.Faction == PoVFaction, true);
        
        UpdateVisibleUnits(unit);
    }

    private void UpdateUIButtons()
    {
        uiButtons.Clear();

        var attackButtonShouldBeVisible = selectedUnit != null && selectedUnit.CanAttack;
        var openDoorButtonShouldBeVisible = selectedUnit != null && IsNextToDoor(selectedUnit);
        
        uiButtons.Add(endTurnButton);

        if (attackButtonShouldBeVisible)
        {
            uiButtons.Add(attackButton);
        }
        else
        {
            attackButton.SetActive(false);
        }

        if (openDoorButtonShouldBeVisible)
        {
            uiButtons.Add(openDoorButton);
        }
        else
        {
            openDoorButton.SetActive(false);
        }
        
        int xOffset = 29;
        int xDist = 45;
        
        foreach (var bu in uiButtons)
        {
            bu.SetActive(true);
            var rectTransform = bu.GetComponent<RectTransform>();
            var anchoredPosition = rectTransform.anchoredPosition;
            rectTransform.anchoredPosition = new Vector2(xOffset, anchoredPosition.y);
            xOffset += xDist;
        }
    }

    private bool IsNextToDoor(Unit unit)
    {
        foreach (var unitOccupiedTile in unit.OccupiedTiles)
        {
            var unitPos = unit.Cell + unitOccupiedTile;
            if (_map.Cardinal.Any(direction => _map.IsDoorAt(unitPos + direction.ToVector())))
            {
                return true;
            }
        }

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
                Vector3Int randomPosition = _map.RandomPosition;
                int tryCounter = 0;
                while (!SpawnUnit(randomPosition, loadOut.Unit, loadOut, ruleFaction) && tryCounter < 100)
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
        var reachable = unit.ReachablePositions;
        var shortestPath = unit.GetShortestPath(goalPosition);
        if (shortestPath.Count == 0)
        {
            Debug.LogWarning("No path for " + unit + " to " + goalPosition + " ("+_map.GetPosDebugInfo(goalPosition)+")");
            return unit.Cell;
        }
        if (Options.Debug) shortestPath.ForEach(p => _map.SetTextAt(p, "_"));
        
        if (reachable.Count == 0)
        {
            Debug.LogWarning("No reachable pos for " + unit + " to " + goalPosition + " (" + _map.GetPosDebugInfo(goalPosition) + ")");
            return unit.Cell;
        }
        if (Options.Debug) reachable.ToList().ForEach(p => _map.SetTextAt(p, "R"));
        shortestPath.Reverse();

        foreach (var pos in shortestPath)
        {
            if (reachable.Contains(pos))
            {
                if (Options.Debug) _map.SetTextAt(pos, "BST");
                return pos;
            }
        }
        Debug.LogWarning("No Best move towards found for " + unit + " and goal " + goalPosition);
        return unit.Cell;
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


    public void MoveUnit(Unit unit, Vector3Int targetPos)
    {
        UnitSelected(unit.Cell);
        TryMoveWithUnit(unit, targetPos);
    }

    public List<Unit> GetAllUnits(AIPlayer aiPlayer)
    {
        var factionOfPlayer = aiPlayer.Faction;
        return allUnits.Where(unit => unit.Faction == factionOfPlayer).ToList();
    }

    public List<Unit> GetVisibleEnemies(AIPlayer aiPlayer)
    {
        return allUnits.Where(unit => unit.Faction != aiPlayer.Faction && !_map.IsInFogMechanically(unit.Cell)).ToList();
    }
}