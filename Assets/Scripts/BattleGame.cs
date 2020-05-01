using System;
using System.Collections.Generic;
using System.Linq;
using Buffs;
using C5;
using DefaultNamespace;
using DefaultNamespace.Units.UnitActions;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using static Mod;
using static Mod.RuleFaction;
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

    public GameObject projectilePrefab;
    public GameObject unitLabelPrefab;
    public Transform worldCanvasTransform;
    public Transform uiCanvasTransform;
    public GameObject gridMapPrefab;
    public GameObject[] premadeMapBlocks;
    public AudioClip soundUnitSelection;
    public AudioClip soundUiDenied;
    public AudioClip soundAbortAll;
    public AudioClip soundUiConfirm;
    public AudioClip soundButtonPressed;

    public AudioSource globalAudioSource;
    public GameObject[] factionPrefabs;
    
    public GameObject explosionPrefab;
    public GameObject meleeHitPrefab;

    private List<Unit> allUnits = new List<Unit>();
    private C5.HashSet<Player> eliminatedPlayers = new C5.HashSet<Player>();
    private Map _map;
    private Unit selectedUnit;
    private Camera gameCamera;
    public Text topLeftLabel;
    public Text centerOfScreenLabel;
    private GameState gameState = GameState.WaitForHotSeatPlayer;
    private Mod mod;
    private List<Player> players;
    private int playerPhase = 0;
    private CameraControls cameraController;
    private Vector3Int lastCursorLocation;
    private int currentMapIndex;
    public int CurrentTurn { get; private set; } = 0;
    private double lastAiCommandTime;
    private double aiCommandDelay = 0.8f;
    private bool spectatorMode = false;
    private Coroutine currentAiCoroutine;
    private Player campaignPlayer;
    private Vector3Int movePreConfirmedAt;
    private List<GameObject> uiButtons = new List<GameObject>();
    public GameObject endTurnButton;
    public GameObject openDoorButton;
    public GameObject rangedAttackButton;
    public GameObject meleeAttackButton;
    private List<GameObject> skillButtons = new List<GameObject>();

    public GameObject skillButtonPrefab;
    private UnitAction unitAction;
    private Queue<Action> scheduledActions = new Queue<Action>();
    private List<RuleFaction> allFactions;

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
        allFactions = mod.GetFactions();
        players = new List<Player>();
        
        currentMapIndex = (0);
        LoadMap();
    }

    void LoadMap()
    {
        if (_map != null)
        {
            Destroy(_map.gameObject);
            _map = null;
        }
        
        // try to find a map in the scene (in skirmish with map generation)
        _map = FindObjectOfType<Map>();        

        if (_map == null)
        {
            // try loading from rules (usually in campaign)
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
        }
        else
        {
            _map.InitInScene(); // re-initialize the map
        }

        Invoke(nameof(AfterMapLoad), 0.1f);
    }

    public void PlaySoundEffect(UiSoundEffect effect)
    {
        switch (effect)
        {
            case UiSoundEffect.AbortOrBack:
                globalAudioSource.PlayOneShot(soundAbortAll);
                break;
            case UiSoundEffect.UnitSelection:
                globalAudioSource.PlayOneShot(soundUnitSelection);
                break;
            case UiSoundEffect.UiConfirm:
                globalAudioSource.PlayOneShot(soundUiConfirm);
                break;
            case UiSoundEffect.UiDeny:
                globalAudioSource.PlayOneShot(soundUiDenied);
                break;
            case UiSoundEffect.ButtonPressed:
                globalAudioSource.PlayOneShot(soundButtonPressed);
                break;
        }
    }
    private void AfterMapLoad()
    {
        GameFactory.Initialize(this, _map, mod);
        
        if (GameController.ChosenMode == GameController.GameMode.Campaign)
        {
            // add campaign player
            campaignPlayer = new Player(0)
            {
                Faction = allFactions[GameController.CampaignFaction]
            };
            players.Add(campaignPlayer);
            Debug.Log("Campaign Player: " + campaignPlayer);
            CreateAIPlayers();
        }
        else if (GameController.ChosenMode == GameController.GameMode.Skirmish)
        {
            // add skirmish players
            for (int i = 0; i < GameController.SkirmishPlayerCount; i++)
            {
                players.Add(new Player(i)
                {
                    Faction = allFactions[GameController.SkirmishFactions[i]]
                });
            }
        }
        
        

        if (GameController.ChosenMode == GameController.GameMode.WatchAI)
        {
            EnableSpectatorMode();
        }

        playerPhase = -1; // start with player 0 on first phase
        
        SpawnUnits();
        
        NextTurn();
        
        PrepareNextPhase();
    }

    private void EnableSpectatorMode()
    {
        spectatorMode = true;
    }
    private void CreateAIPlayers()
    {
        foreach (var faction in allFactions)
        {
            if (campaignPlayer.Faction == faction) continue;
            var playerAi = new AIPlayer(0, this, mod, _map, faction);
            players.Add(playerAi);
        }
    }

    // Update is called once per frame
    private void Update()
    {
        if (players.Count <= 1) return;
        if (gameState == GameState.WaitForHotSeatPlayer) return;
        
        /* Automatic AI moves */
        if (CurrentPlayer is AIPlayer)
        {
            var aiPlayer = CurrentPlayer as AIPlayer;
            Debug.Log("CurrentFaction (AI Move): " + CurrentPlayer);
            if (aiPlayer.State == AIState.Idle && currentAiCoroutine != null)
            {
                StopCoroutine(currentAiCoroutine);
            }
            if (Time.time - lastAiCommandTime > aiCommandDelay)
            {
                ToDefaultState();

                currentAiCoroutine = StartCoroutine(aiPlayer.ExecuteNextCommand());
                lastAiCommandTime = Time.time;
            }

            return;
            
        }

        if (IsPointerOverGameObject()) return;
        
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
        if (selectedUnit != null)
        {
            selectedUnit.ApplyBuff(new Stun
            {
                EndCondition = new TimedCondition(2)
            });
        }
        //aiPlayers[CurrentFaction].NextPhase();
        /*
        if (gameState != GameState.WaitForHotSeatPlayer)
        {
            ToDefaultState();
            currentAiCoroutine = StartCoroutine(aiPlayers[CurrentFaction].ExecuteNextCommand());
            lastAiCommandTime = Time.time;
        }
        */
    }
    
    public void DebugButtonTwoPressed()
    {
        //
    }

    private void DeselectAll()
    {
        if (selectedUnit != null)
        {
            selectedUnit.ResetSprite();
        }
        selectedUnit = null;        
        ToDefaultState();
        UpdateUIButtons();
        //PlaySoundEffect(UiSoundEffect.AbortOrBack);
    }

    public void ToDefaultState()
    {
        _map.RemoveAllLines();
        _map.RemoveAllHighlights();
        _map.RemoveAllHighlights(1);
        _map.RemoveAllTileMarker();        
        _map.HideUnitMarker();
        allUnits.ForEach(unit =>
        {
            unit.RemoveLabel();
            unit.IsHealthBarVisible = false;
        });
        gameState = GameState.Default;
    }

    private void PreviewMovement(Unit unit, Vector3Int targetPos)
    {
        var path = unit.GetShortestPath(targetPos);
        System.Collections.Generic.HashSet<Vector3Int> previewTiles = new System.Collections.Generic.HashSet<Vector3Int>();
            
        if (path.Count > 0)
        {
            foreach (var pathPos in path)
            {
                foreach (var offset in unit.OccupiedTiles)
                {
                    previewTiles.Add(pathPos + offset);
                }
            }
            HighlightTiles(previewTiles.ToList(), new Color(227/(float)255, 144/(float)255, 36/(float)255, 0.5f), 1);
        }
    }

    private void ShowAllHealthBars()
    {
        allUnits.ForEach(unit =>
        {
            unit.IsHealthBarVisible = unit.IsVisible;
        });
    }


    private void InputReleasedOnMap(Vector3Int mapPos)
    {
        if (cameraController.ActiveTouchCameraMovement)
            return;
        
        if (gameState != GameState.AttackTargetSelection && _map.IsUnitOn(mapPos))
        {
            ToDefaultState();
            UnitSelected(mapPos);
        }
        else if (selectedUnit != null)
        {
            switch (gameState)
            {
                case GameState.AttackTargetSelection when unitAction.IsValidTarget(mapPos):
                    if (unitAction.TargetSelected(mapPos))
                    {
                        if (unitAction.TargetMode.Type == TargetModeType.Single)
                        {
                            unitAction.ConfirmTargets();
                        }
                        else
                        {
                            PlaySoundEffect(UiSoundEffect.UiConfirm);
                        }
                    }
                    break;
                case GameState.MoveTargetSelection when selectedUnit.ReachablePositions.Contains(mapPos) && !_map.IsUnitOn(mapPos):
                    MovementPreviewLockedIn(selectedUnit, mapPos);
                    PlaySoundEffect(UiSoundEffect.UiConfirm);
                    break;
                case GameState.MovementSelectedPreConfirmation when movePreConfirmedAt == mapPos:
                    TryMoveWithUnit(selectedUnit, mapPos);
                    PlaySoundEffect(UiSoundEffect.UiConfirm);
                    break;
                case GameState.MovementSelectedPreConfirmation when movePreConfirmedAt != mapPos:
                    _map.RemoveAllLines();
                    gameState = GameState.MoveTargetSelection;
                    PreviewMovement(selectedUnit, mapPos);
                    PlaySoundEffect(UiSoundEffect.AbortOrBack);
                    break;
                default:
                    DeselectAll();
                    break;
            }
        }
        else
        {
            DeselectAll();
        }
        UpdateLabel();
    }

    private void MovementPreviewLockedIn(Unit unit, Vector3Int mapPos)
    {
        _map.RemoveAllLines();
        movePreConfirmedAt = mapPos;
        gameState = GameState.MovementSelectedPreConfirmation;
        _map.SetHighlightColor(new Color(0, 0, 0.7f, 0.6f), 1);
        foreach (var visibleEnemy in GetVisibleEnemies(CurrentPlayer))
        {
            if (_map.IsLineOfFireBetween(unit, mapPos, visibleEnemy.Cell))
            {
                ((Unit) visibleEnemy).IsHealthBarVisible = true;
                _map.DrawLine(mapPos, visibleEnemy.Cell, Color.red);
            }
        }
    }


    public void CheckForDeadUnits()
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
        var clickedUnit = _map.GetUnitAt(mapPos);
        
        if (clickedUnit.ControllingPlayer == CurrentPlayer)
        {
            selectedUnit = (Unit) clickedUnit;
            Debug.Log("UnitSelected: " + selectedUnit);
            if (selectedUnit.ActionPoints > 0)
            {
                gameState = GameState.MoveTargetSelection;
            }
            HighlightTiles(selectedUnit.ReachablePositions, new Color(0, 0.8f, 0, 0.2f));
            
            if (CurrentPlayer == PoVPlayer)
            {
                selectedUnit.MarkAsSelected();
                UpdateLabel();
            }

            UpdateUIButtons();
            
            PlaySoundEffect(UiSoundEffect.UnitSelection);
        }
        
        ShowAllHealthBars();
    }

    public Projectile CreateProjectile(Vector3 start, Vector3 end, float speed, int selectedSprite)
    {
        var moveDirection = (end - start);
//        Debug.Log("Spawning projectile effect (" + start +" -> " + end + ")");
        float angle = Mathf.Atan2(moveDirection.y, moveDirection.x) * Mathf.Rad2Deg;
        var go = Instantiate(projectilePrefab, start, Quaternion.AngleAxis(angle, Vector3.forward));
        Projectile projectile = go.GetComponent<Projectile>();
        projectile.Game = this;
        projectile.target = end;
        projectile.SelectedSprite = selectedSprite;
        projectile.speed = speed;
        return projectile;
    }

    private bool TryMoveWithUnit(IPlayerUnit playerUnit, Vector3Int mapPos)
    {
        var unit = (Unit) playerUnit;
        if (unit == null || !_map.CanBePlaced(unit, mapPos, false))
            return false;
        
        var path = unit.GetShortestPath(mapPos);
        
        if (path.Count <= 0 || unit.ActionPoints <= 0) return false;
        
        _map.RemoveAllHighlights();
        _map.RemoveAllHighlights(1);
        _map.RemoveAllLines();

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
    public void CreateMeleeHitOnTile(Vector3Int mapPos)
    {
        var go = Instantiate(meleeHitPrefab, _map.CellToWorld(mapPos), Quaternion.identity);
        //go.GetComponent<SimpleAnimation>().OnCompleted += sender => { action.Invoke(); };
    }
    public void CreateExplosionOnTile(Vector3Int mapPos)
    {
        Instantiate(explosionPrefab, _map.CellToWorld(mapPos), Quaternion.identity);
    }
    public void CreateExplosionOnTile(Vector3 worldPos)
    {
        CreateExplosionOnTile(_map.WorldToCell(worldPos));
    }
    public void OpenDoorButtonClicked()
    {
        PlaySoundEffect(UiSoundEffect.ButtonPressed);
        Debug.Log("OpenDoorButtonClicked() FIRED: " + CurrentPlayer + " / " + PoVPlayer);
        if (CurrentPlayer != PoVPlayer) return;
        Debug.Log("SelectedUnit: " + selectedUnit);        
        StartTargetingForAction("OpenDoor");        
    }

    private void StartTargetingForAction(string actionName)
    {
        if (selectedUnit != null)
        {
            unitAction = GameFactory.CreateUnitAction<UnitAction>(actionName, selectedUnit);
            unitAction.StartTargeting();
        }
    }

    public void EndTurnButtonClicked()
    {
        PlaySoundEffect(UiSoundEffect.ButtonPressed);
        if (CurrentPlayer != PoVPlayer) return;
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
    
    public void EndPhase(Player player)
    {
        if (player == CurrentPlayer)
        {
            EndPhase();
        }
    }

    private void StartWaitingForHeatSeatPlayer(Player player)
    {
        selectedUnit = null;
        _map.HideUnitMarker();
        allUnits.ForEach(unit => { unit.IsVisible = false; unit.Deselect(); });

        if (spectatorMode || CurrentPlayer != PoVPlayer)
        {
            gameState = GameState.Default;
            StartNextPhase();
            return;
        }
        gameState = GameState.WaitForHotSeatPlayer;
        centerOfScreenLabel.enabled = true;
        centerOfScreenLabel.text = "NEXT PLAYER\n" + player;        
        _map.ResetFog();
        UpdateLabel();
        CenterCameraOn(new Vector3Int(_map.Width / 2, _map.Height / 2, 0));
    }

    

    private bool IsMissionOver()
    {
        Dictionary<Player, int> unitCounts = new Dictionary<Player, int>();
        foreach (var player in players)
        {
            unitCounts[player] = 0;
        }
        foreach (var unit in allUnits)
        {
            unitCounts[unit.ControllingPlayer]++;
        }

        foreach (var kvp in unitCounts)
        {
            var unitCount = kvp.Value;
            if (unitCount != 0) continue;
            
            var player = kvp.Key;
            players.Remove(player);
            eliminatedPlayers.Add(player);
            Debug.Log(player + " has been eliminated.");
        }

        if (players.Count <= 1)
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
        allUnits.ForEach(u => u.IsVisible = true);
        Debug.Log("Mission is over!");
    }

    private void PrepareNextPhase()
    {
        ToDefaultState();
        playerPhase++;
        if (playerPhase > players.Count - 1)
        {
            playerPhase = 0;
            NextTurn();
        }
        
        PoVPlayer = CurrentPlayer;
        if (GameController.ChosenMode == GameController.GameMode.Campaign)
        {
            PoVPlayer = campaignPlayer;
        }
        
        StartWaitingForHeatSeatPlayer(CurrentPlayer);
    }

    public Player PoVPlayer { get; set; }

    private void StartNextPhase()
    {
        gameState = GameState.Default;
        centerOfScreenLabel.enabled = false;

        if (CurrentPlayer != PoVPlayer)
        {
            centerOfScreenLabel.text = "HIDDEN MOVEMENT";
            centerOfScreenLabel.enabled = true;
        }
        
        GlobalFOVUpdate();
        
        bool firstUnitFound = false;
        foreach (var unit in allUnits)
        {
            if (unit.ControllingPlayer != CurrentPlayer) continue;
            unit.PrepareNewPhase();
            
            if (firstUnitFound || spectatorMode || (GameController.ChosenMode == GameController.GameMode.Campaign && CurrentPlayer != campaignPlayer)) continue;
            firstUnitFound = true;            
            UnitSelected(unit.Cell);
            CenterCameraOn(unit);
        }

        (CurrentPlayer as AIPlayer)?.NextPhase();
    }

    private void NextTurn()
    {
        CurrentTurn++;
        Debug.Log("Turn :" + CurrentTurn);
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

    public void MeleeAttackButtonClicked()
    {
        PlaySoundEffect(UiSoundEffect.ButtonPressed);
        if (selectedUnit == null) return;
        if (CurrentPlayer != PoVPlayer) return;
                
        //if (aiPlayers.ContainsKey(CurrentFaction) ) return;

        if (gameState == GameState.AttackTargetSelection && unitAction.TargetMode.Type == TargetModeType.Multiple)
        {
            unitAction.ConfirmTargets();
        }
        else
        {
            StartTargetingForAction("MeleeAttack");
        }
    }

    public void RangedAttackButtonClicked()
    {
        PlaySoundEffect(UiSoundEffect.ButtonPressed);
        if (selectedUnit == null) return;
        if (CurrentPlayer != PoVPlayer) return;
                
        //if (aiPlayers.ContainsKey(CurrentFaction) ) return;

        if (gameState == GameState.AttackTargetSelection && unitAction.TargetMode.Type == TargetModeType.Multiple)
        {
            unitAction.ConfirmTargets();
        }
        else
        {
            StartTargetingForAction("RangedAttack");
        }
    }
    
    public void SkillButtonClicked(int skillIndex)
    {
        PlaySoundEffect(UiSoundEffect.ButtonPressed);
        if (selectedUnit == null) return;
        if (CurrentPlayer != PoVPlayer) return;
                
        //if (aiPlayers.ContainsKey(CurrentFaction) ) return;

        if (gameState == GameState.AttackTargetSelection && unitAction.TargetMode.Type == TargetModeType.Multiple)
        {
            unitAction.ConfirmTargets();
        }
        else
        {
            var skillName = selectedUnit.AvailableSkills[skillIndex];
            StartTargetingForAction(skillName);
        }
    }

    public void ShowValidTargets(IEnumerable<Vector3Int> targetPositions)
    {
        HighlightTiles(targetPositions, new Color(0.6f, 0f, 0, 0.5f));
    }

    private void HighlightTiles(IEnumerable<Vector3Int> positions, Color color, int layer = 0)
    {
        _map.RemoveAllHighlights(layer);
        if (CurrentPlayer == PoVPlayer)
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
        
        RangedAttackButtonClicked();
        
        foreach (var target in targets)
        {
            unitAction.TargetSelected(target);
        }

        unitAction.ConfirmTargets();
    }

    private void UnitFinishedMoving(object sender, UnitEventArgs args)
    {
        if (gameState == GameState.WaitForHotSeatPlayer) return;
        
        GlobalFOVUpdate();
        
        if (selectedUnit == args.unit && selectedUnit.ActionPoints > 0)
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

    public void GlobalFOVUpdate()
    {
        _map.ResetFog();
        
        foreach (var unit in allUnits)
        {
            unit.IsVisible = false;

            if (unit.ControllingPlayer == PoVPlayer)
            {
                unit.IsVisible = true;
            }
            unit.UpdateFoV(unit.ControllingPlayer == PoVPlayer, unit.ControllingPlayer == PoVPlayer);
        }
        foreach (var unit in allUnits)
        {
            if (unit.ControllingPlayer != PoVPlayer && !_map.IsInFogVisually(unit.Cell))
            {
                unit.IsVisible = true;
            }
        }
    }

    public void UpdateVisibleUnits(Unit sourceUnit)
    {
        foreach (var unit in allUnits)
        {
            var unitPos = _map.WorldToCell(unit.transform.position);
            var shouldBeVisible = unit.ControllingPlayer == PoVPlayer
                                  || !_map.IsInFogVisually(unitPos);
            
            if (shouldBeVisible && unit.ControllingPlayer != CurrentPlayer)
            {
                unit.TurnTowards(sourceUnit.Cell);
            }
            unit.IsVisible = shouldBeVisible;
        }
    }

    public Player CurrentPlayer => players[playerPhase];


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
            SetLabel(selectedUnit+"\n" + selectedUnit.WeaponDescription);
        }
        else if (_map.IsOtherFactionOn(CurrentPlayer, _map.CurrentCursorPosition))
        {
            var enemyUnit = (Unit)_map.GetUnitAt(_map.CurrentCursorPosition);
            SetLabel(enemyUnit + "\n" + enemyUnit.WeaponDescription);
        }
        else
        {
            SetLabel(_map.CurrentCursorPosition.ToString() +":"+ _map.GetPosDebugInfo(_map.CurrentCursorPosition));
        }
    }

    private void SpawnUnits()
    {
        var mapRules = mod.GetMap(currentMapIndex);
        var spawnPointsByPlayer = _map.GetSpawnPoints();
        var playerIndex = 0;
        Debug.Log("SpawnUnits has " + spawnPointsByPlayer.Count + " lists of spawnpoints.");
        Debug.Log("SpawnUnits tries to spawn units for " + players.Count + " players");

        foreach (var player in players)
        {
            var spawnPositions = spawnPointsByPlayer[playerIndex];
            playerIndex++;
            int spawnIndex = 0;
            bool outOfSpawn = false;
            foreach (var loadOut in player.Faction.LoadOut)
            {
                for (int i = 0; i < loadOut.Count; i++)
                {
                    if (loadOut.DoNotSpawn) continue;
                    if (spawnIndex >= spawnPositions.Count)
                    {
                        outOfSpawn = true;
                        break;
                    }
                    var spawnPosition = spawnPositions[spawnIndex];
                    string unitType = loadOut.Unit;
                    SpawnUnit(spawnPosition, unitType, loadOut, player);
                    Debug.Log("Unit " + unitType + " spawned at " + spawnPosition);
                    spawnIndex++;
                }

                if (outOfSpawn)
                {
                    break;
                    Debug.Log("Player " + player + " ran out of spawn points.");
                }
            }
        }
    }

    public bool SpawnUnit(Vector3Int spawnPosition, string unitType, RuleLoadout loadout, Player player)
    {
        var unitRules = mod.GetUnitRule(unitType);
        var unitPrefab = unitRules.FactionPrefab != -1 ? factionPrefabs[unitRules.FactionPrefab] : factionPrefabs[player.Faction.FactionPrefab];
        var go = Instantiate(unitPrefab, _map.CellToWorld(spawnPosition), Quaternion.identity);
        var unit = go.GetComponent<Unit>();
        unit.LoadRules(unitType, player, loadout);
        if (_map.AddUnit(unit, spawnPosition))
        {
            unit.FinishedMoving += UnitFinishedMoving;
            unit.StepCompleted += UnitStepCompleted;
            var unitAnimator = go.GetComponentInChildren<UnitAnimator>();
            unitAnimator.SetSpriteOffset(unitRules.SpriteOffset);
            
            var unitLabel = Instantiate(unitLabelPrefab).GetComponent<Text>();
            unitLabel.transform.SetParent(worldCanvasTransform);
            unitLabel.enabled = false;
            unit.SetLabel(unitLabel);
            
            allUnits.Add(unit);
            return true;
        }

        Destroy(unit.gameObject);
        Debug.LogWarning("Could not spawn unit at: " + spawnPosition);
        return false;
    }

    private void UnitStepCompleted(object sender, UnitEventArgs args)
    {
        var unit = args.unit;

        _map.IsStandingOnDoor(unit, true);

        if (gameState == GameState.WaitForHotSeatPlayer) return;
        
        var unitsBecameVisible = unit.UpdateFoV(unit.ControllingPlayer == PoVPlayer, unit.ControllingPlayer == PoVPlayer);
        
        UpdateVisibleUnits(unit);

        if (unitsBecameVisible.Count > 0)
        {
            if (_map.GetUnitAt(unit.Cell) == unit) // cannot end movement on the same tile as another unit
            {
                unit.EndMovement();
            }            
        }
    }

    public void UpdateUIButtons()
    {
        uiButtons.Clear();

        var unitIsSelected = selectedUnit != null;
        
        var rangedAttackButtonShouldBeVisible = unitIsSelected && selectedUnit.CanAffordRangedAttack() && selectedUnit.HasRangedWeapon;
        var meleeAttackButtonShouldBeVisible = unitIsSelected && selectedUnit.CanAffordMeleeAttack() && selectedUnit.HasMeleeWeapon;
        var openDoorButtonShouldBeVisible = unitIsSelected && IsNextToDoor(selectedUnit);
        var numberOfNeededSkillButtons = unitIsSelected ? selectedUnit.AvailableSkills.Count : 0;
        
        uiButtons.Add(endTurnButton);

        if (rangedAttackButtonShouldBeVisible)
        {
            uiButtons.Add(rangedAttackButton);
        }
        else
        {
            rangedAttackButton.SetActive(false);
        }
        
        if (meleeAttackButtonShouldBeVisible)
        {
            uiButtons.Add(meleeAttackButton);
        }
        else
        {
            meleeAttackButton.SetActive(false);
        }

        if (openDoorButtonShouldBeVisible)
        {
            uiButtons.Add(openDoorButton);
        }
        else
        {
            openDoorButton.SetActive(false);
        }

        int missingSkillButtons = numberOfNeededSkillButtons - skillButtons.Count;

        if (missingSkillButtons > 0)
        {
            for (int i = 0; i < missingSkillButtons; i++)
            {
                var go = Instantiate(skillButtonPrefab);                
                go.transform.SetParent(uiCanvasTransform);
                go.transform.localScale = Vector3.one;
                skillButtons.Add(go);
            }
        }

        if (unitIsSelected && selectedUnit.CanAct)
        {
            for (int i = 0; i < skillButtons.Count; i++)
            {
                var skillButton = skillButtons[i];
                if (i < numberOfNeededSkillButtons)
                {
                    var skillIndex = i;
                    var skillName = selectedUnit.AvailableSkills[i];
                    var ruleSkill = mod.GetSkill(skillName);
                    var buttonComponent = skillButton.GetComponent<Button>();

                    buttonComponent.onClick.RemoveAllListeners();
                    buttonComponent.onClick.AddListener(() => { SkillButtonClicked(skillIndex); });
                    skillButton.GetComponentInChildren<Text>().text = ruleSkill.DisplayName;

                    uiButtons.Add(skillButton);
                }
                else
                {
                    skillButton.SetActive(false);
                }
            }
        }
        else
        {
            skillButtons.ForEach(b => b.SetActive(false));
        }

        int xOffset = 29;
        int xDist = 45;
        
        foreach (var bu in uiButtons)
        {
            bu.SetActive(true);
            var rectTransform = bu.GetComponent<RectTransform>();
            rectTransform.anchoredPosition = new Vector2(xOffset, 24);
            xOffset += xDist;
        }
    }

    private bool IsNextToDoor(Unit unit)
    {
        foreach (var unitOccupiedTile in unit.OccupiedTiles)
        {
            var unitPos = unit.Cell + unitOccupiedTile;
            for (var index = 0; index < _map.Cardinal.Length; index++)
            {
                var direction = _map.Cardinal[index];
                var doorThere = _map.IsDoorAt(unitPos + direction.ToVector());
                if (doorThere) return true;
            }
        }

        return false;
    }

    private Color ColorFromArray(int[] colorDefinition)
    {
        return new Color(((float)colorDefinition[0])/255f,((float)colorDefinition[1])/255f,((float)colorDefinition[2])/255f);
    }

    private void RandomPlacement(Player player)
    {
        foreach (var loadOut in player.Faction.LoadOut)
        {
            if (loadOut.DoNotSpawn) continue;
            for (int i = 0; i < loadOut.Count; i++)
            {
                Vector3Int randomPosition = _map.RandomPosition;
                int tryCounter = 0;
                while (!SpawnUnit(randomPosition, loadOut.Unit, loadOut, player) && tryCounter < 100)
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

    public List<Unit> GetAllUnits(Player player)
    {
        return allUnits.Where(unit => unit.ControllingPlayer == player).ToList();
    }

    public List<Unit> GetVisibleEnemies(Player player)
    {
        return allUnits.Where(unit => unit.ControllingPlayer != player && !_map.IsInFogMechanically(unit.Cell)).ToList();
    }

    public void ChangeStateTo(GameState nextState)
    {
        gameState = nextState;
    }

    public void Schedule(Action action, float timer)
    {
        scheduledActions.Enqueue(action);
        Invoke(nameof(NextScheduledAction), timer);
    }

    public void NextScheduledAction()
    {
        var nextAction = scheduledActions.Dequeue();
        nextAction.Invoke();
    }

    private static bool IsPointerOverGameObject()
    {
#if UNITY_IOS || UNITY_ANDROID
        //check touch
        if (Input.touchCount > 0 && Input.touches[0].phase == TouchPhase.Began)
        {
            if (EventSystem.current.IsPointerOverGameObject(Input.touches[0].fingerId))
                return true;
        }
#else     
        //check mouse
        if (EventSystem.current.IsPointerOverGameObject())
            return true;
#endif
        return false;
    }
}

public enum UiSoundEffect
{
    UnitSelection,
    AbortOrBack,
    UiConfirm,
    UiDeny,
    ButtonPressed
}