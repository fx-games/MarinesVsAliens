using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Buffs;
using DefaultNamespace;
using DefaultNamespace.Units.UnitActions;
using GridPathFinder;
using UnityEngine;
using UnityEngine.Experimental.Rendering.Universal;
using UnityEngine.UI;
using static Map;
using static Mod;
using static Mod.RuleFaction;
using Random = UnityEngine.Random;


public class Unit : MonoBehaviour, IPlayerUnit
{
    public enum UnitState { Idle, Moving }

    public GameObject flashAnimationPrefab;
    private Dictionary<string, UnitStat> stats = new Dictionary<string, UnitStat>();
    private List<Buff> activeBuffs = new List<Buff>(); 
    public Weapon RangedWeapon { get; private set; }
    public Weapon MeleeWeapon { get; private set; }
    public GameObject bloodSplatter;
    public int ActionPoints
    {
        get => GetCurrentStat("ActionPoints") / 100;
        private set => SetCurrentStat("ActionPoints", value * 100);
    }
    
    public int CurrentVisionRange => GetCurrentStat("VisionRange");

    public int CurrentHealth
    {
        get => GetCurrentStat("Health");
        private set => SetCurrentStat("Health", value);
    }

    public int CurrentArmor => GetCurrentStat("Armor");
    public Player ControllingPlayer { get; set; }    
    public Vector3Int Cell => _map.WorldToCell(transform.position);

    private float lastMoveTime;
    private double moveDelay = 0.3f;
    private Mod mod;
    private RuleUnit unitRules;

    private Map _map;
    private int visualPathIndex;
    private List<Vector3Int> currentPath;
    private SpriteRenderer spriteRenderer;
    private SpriteRenderer markerRenderer;
    private Text unitLabel;
    private Color labelColor;
    private Camera gameCamera;
    public UnitState State { get; private set; } = UnitState.Idle;
    public bool HasRangedWeapon => RangedWeapon != null;
    public bool HasMeleeWeapon => MeleeWeapon != null;
    public string WeaponDescription { get
    {
        string melee = "";
        string ranged = "";
        if (HasMeleeWeapon)
        {
            melee = MeleeWeapon.ToString();
        }

        if (HasRangedWeapon)
        {
            ranged = RangedWeapon.ToString();
        }

        if (HasMeleeWeapon && HasRangedWeapon)
        {
            return melee + "/" + ranged;
        }

        return melee + ranged;
    } }

    public bool CanAct => ActionPoints > 0;
    public RuleUnit Rules => unitRules;
    public List<Vector3Int> OccupiedTiles { get; set; }
    public HashSet<IPlayerUnit> VisibleEnemies => _visibleEnemies;
    public HashSet<Vector3Int> FieldOfView => _fieldOfView;
    public List<Vector3Int> ReachablePositions => _reachablePositions.Value.Where(p => !_map.IsUnitOn(p)).ToList();
    public HashSet<Vector3Int> ValidRangedTargets => _validTargets.Value;

    private Pathfinder pathfinder;
    private Lazy<List<Vector3Int>> _reachablePositions;
    private Lazy<HashSet<Vector3Int>> _validTargets;
    private HashSet<IPlayerUnit> _visibleEnemies = new HashSet<IPlayerUnit>();
    private HashSet<Vector3Int> _fieldOfView;
    private LineRenderer healthBar;
    private int currentMapPathIndex = -1;
    private Light2D lightSource;
    private bool animatingLabel;
    private UnitAnimator animator;
    private int originalSpriteOffset;
    private int cardinalStepCost = 100;
    private int diagonalStepCost = 140;
    private SimpleAnimation flashAnimation;
    public event UnitEvent FinishedMoving;
    public event UnitEvent StepCompleted;
    // Start is called before the first frame update
    private void Awake()
    {
        flashAnimation = Instantiate(flashAnimationPrefab, transform, false).GetComponent<SimpleAnimation>();
        flashAnimation.PlayOnStart = false;
        flashAnimation.transform.localPosition = Vector3.zero;
        flashAnimation.OnCompleted += FlashAnimationOnOnCompleted;
        healthBar = GetComponentInChildren<LineRenderer>();
        gameCamera = Camera.main;
        _map = FindObjectOfType<Map>();
        mod = FindObjectOfType<Mod>();
        spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        lightSource = GetComponentInChildren<Light2D>(includeInactive: true);
        animator = GetComponentInChildren<UnitAnimator>();
        pathfinder = new Pathfinder(_map, cardinalStepCost, diagonalStepCost);
        Procs = new ProcReceiver(activeBuffs);                
    }

    private void FlashAnimationOnOnCompleted(object sender)
    {
        _map.HighlightTile(Cell, Color.cyan, 0, 2);
    }

    private void Start()
    {
        originalSpriteOffset = animator.SpriteOffset;
        UpdateReachablePositions();
    }

    public bool IsHealthBarVisible
    {
        get => healthBar.enabled;
        set => healthBar.enabled = value;
    }
    
    public void ChangeSprite(int spriteOffset)
    {
        animator.SetSpriteOffset(spriteOffset);
    }
    public void ResetSprite()
    {
        Debug.Log("Trying to reset sprite of " + this);
        animator.SetSpriteOffset(originalSpriteOffset);
    }
    // Update is called once per frame
    private void Update()
    {
        if (State == UnitState.Moving && (Time.time - lastMoveTime) > moveDelay)
        {
            NextMove();
        }

        if (animatingLabel)
        {
            unitLabel.transform.position += Vector3.up * Time.deltaTime;
            labelColor.a -= 0.005f;
            unitLabel.color = labelColor;
            if (labelColor.a < 0)
            {
                animatingLabel = false;
                RemoveLabel();
            }
        }
    }

    private void OnDestroy()
    {
        if (unitLabel != null)
        {
            Destroy(unitLabel);
        }
    }

    public void SetLabelText(string text, bool animate = false)
    {
        labelColor = Color.white;
        unitLabel.enabled = true;
        unitLabel.transform.position = transform.position;
        unitLabel.text = text;
        unitLabel.color = labelColor;
        animatingLabel = animate;
    }

    public void RemoveLabel()
    {
        unitLabel.enabled = false;
    }

    public void TurnTowards(Vector3Int position)
    {
        if (position.x > Cell.x)
        {
            SetDirection(Direction.East);
        }
        else
        {
            SetDirection(Direction.West);
        }
    }
    
    private void SetDirection(Direction direction)
    {
        switch (direction)
        {
            case Direction.East:
            case Direction.NorthEast:
            case Direction.SouthEast:
            case Direction.North:
                spriteRenderer.flipX = true; // look right
                break;
            case Direction.South:
            case Direction.West:
            case Direction.SouthWest:
            case Direction.NorthWest:
                spriteRenderer.flipX = false; // look left
                break;
        }
    }    
    private void NextMove()
    {
        if (visualPathIndex + 1 < currentPath.Count)
        {
            Vector3Int currVisPos = currentPath[visualPathIndex];
            Vector3Int nextVisPos = currentPath[visualPathIndex + 1];
            Vector3Int currMapPos = currVisPos;

            if (currentMapPathIndex > -1 && currentMapPathIndex < currentPath.Count)
            {
                currMapPos = currentPath[currentMapPathIndex];
            }
                
            if (_map.IsFriendlyUnitOn(this, nextVisPos)) // we could walk through friendly units
            {
                // store the index were we could not go on.. but only once
                if (currentMapPathIndex == -1)
                {
                    currentMapPathIndex = visualPathIndex;
                }
            }
            else // normal walkable tile
            {
                // WE MOVE THE UNIT ON THE MAP HERE
                if (!_map.MoveUnit(this, currMapPos, nextVisPos))
                {
                    // should only trigger on mutual surprise later on
                    EndMovement();
                    return;
                }
                else // we were able to move on, reset the map index, we are in sync again
                {
                    currentMapPathIndex = -1;
                }
            }
            
            State = UnitState.Moving;
            lastMoveTime = Time.time;

            // handle movement with a scale of *10
            SetCurrentStat("ActionPoints", GetCurrentStat("ActionPoints") - StepCost(currVisPos, nextVisPos));
            
            // WE VISUALLY MOVE THE UNIT HERE
            RepositionUnitInWorld(nextVisPos);
            UpdateDirection(currVisPos, nextVisPos);
            OnStepCompleted(currVisPos);
            visualPathIndex++;
        }
        else
        {
            EndMovement();
        }
    }

    private int StepCost(Vector3Int curPos, Vector3Int nextPos)
    {
        // 4 AP => 400 AP in Datastructure
        // movement = 10 -> 10 tiles in gerader linie
        // 10 schritte = 400 ap -> 1 schritt = MaxAP/MaxMovement (400/10=40)
        
        
        if (curPos.x == nextPos.x || curPos.y == nextPos.y) return cardinalStepCost;
        return (int) diagonalStepCost;
    }

    public void EndMovement()
    {
        State = UnitState.Idle;
        visualPathIndex = 0;
        currentPath = null;

        OnFinishedMoving();
    }

    private void UpdateHealthBar()
    {
        IsHealthBarVisible = true;
        var healthInPercent = CurrentHealth / (float)Rules.Stats["Health"];
        var leftPoint = healthBar.GetPosition(0);
        healthBar.SetPosition(1, new Vector3(leftPoint.x + healthInPercent, leftPoint.y, leftPoint.z));
        healthBar.widthMultiplier = 0.5f;
        Invoke(nameof(ScaleHealthbarBack), 0.5f);
    }

    private void ScaleHealthbarBack()
    {
        healthBar.widthMultiplier = 0.1f;
    }
    private void UpdateDirection(Vector3Int lastPos, Vector3Int nextPos)
    {
        if (lastPos.y == nextPos.y)
        {
            if (lastPos.x < nextPos.x)
            {
                // we are oving right
                SetDirection(Direction.East);
            }
            else
            {
                SetDirection(Direction.West);
            }
        }
        else if (lastPos.x == nextPos.x)
        {
            if (lastPos.y < nextPos.y)
            {
                // we are oving up
                SetDirection(Direction.North);
            }
            else
            {
                SetDirection(Direction.South);
            }
        }
        else if (lastPos.x < nextPos.x)
        {
            if (lastPos.y < nextPos.y)
            {
                // we are moving topright
                SetDirection(Direction.NorthEast);
            }
            else
            {
                SetDirection(Direction.SouthEast);
            }
        }
        else
        {
            if (lastPos.y < nextPos.y)
            {
                // we are moving topleft
                SetDirection(Direction.NorthWest);
            }
            else
            {
                SetDirection(Direction.SouthWest);
            }
        }
    }

    private void OnFinishedMoving()
    {
        FinishedMoving?.Invoke(this, new UnitEventArgs{unit = this});
    }
    
    private void OnStepCompleted(Vector3Int oldPos)
    {
        StepCompleted?.Invoke(this, new UnitEventArgs{unit = this, previousPosition = oldPos});
    }

    public void Move(List<Vector3Int> path)
    {
        if (State == UnitState.Moving) return;

        visualPathIndex = 0;
        currentPath = path;
        State = UnitState.Moving;

        UpdateMarker();
    }

    private void RepositionUnitInWorld(Vector3Int mapPos)
    {
        var cellToWorld = _map.CellToWorld(mapPos);
        transform.position = cellToWorld;
    }

    public void UnitDeath()
    {
        Debug.Log("Death of " + name);
        Deselect();
        Instantiate(bloodSplatter, transform.position, Quaternion.identity);
        Destroy(gameObject);
    }

    public void LoadRules(string unitType, Player player, RuleLoadout loadout)
    {
        ControllingPlayer = player;
        foreach (var weaponType in loadout.Weapons)
        {
            var weaponRules = mod.GetWeaponRule(weaponType);
            if (weaponRules.IsMelee)
            {
                MeleeWeapon = new Weapon(mod, weaponRules);
            }
            else
            {
                RangedWeapon = new Weapon(mod, weaponRules);
            }
        }

        unitRules = mod.GetUnitRule(unitType);
        name = mod.Tr(unitType);

        AvailableSkills = unitRules.UnitSkills;
        
        foreach (var kvp in unitRules.Stats)
        {
            var statName = kvp.Key;
            var statValue = kvp.Value;

            if (statName == "ActionPoints") statValue *= 100; // scale action points
            
            stats[statName] = new UnitStat(statName, statValue);
        }
        
        cardinalStepCost = stats["ActionPoints"].MaxValue / stats["Movement"].MaxValue;
        diagonalStepCost = (int) (cardinalStepCost * 1.4f);
        
        pathfinder = new Pathfinder(_map, cardinalStepCost, diagonalStepCost);
        
        OccupiedTiles = unitRules.OccupiedTiles.ConvertAll(elem => new Vector3Int(elem[0], elem[1], 0));

        OffsetSprite();
    }

    private void OffsetSprite()
    {
        if (OccupiedTiles.Count == 1)
            return;
        
        int maxX = OccupiedTiles.Max(offset => offset.x);
        int minX = OccupiedTiles.Min(offset => offset.x);
            
        int maxY = OccupiedTiles.Max(offset => offset.y);
        int minY = OccupiedTiles.Min(offset => offset.y);

        float centerX = (minX + maxX) * 0.5f;
        float centerY = (minY + maxY) * 0.5f;
        
        spriteRenderer.transform.localPosition = new Vector3(centerX, centerY, 0f);
    }

    public void AllowNoMoreActions()
    {
        ActionPoints = 0;
        UpdateMarker();
    }

    public void PrepareNewPhase()
    {
        ActionPoints = unitRules.Stats["ActionPoints"];
        UpdateValidAttackTargets();
        UpdateReachablePositions();

        Procs.OnNextTurn();
    }

    public void ApplyBuff(Buff buff)
    {
        Debug.Log(buff.DisplayName + " applied to " + this);
        buff.BuffedUnit = this;
        activeBuffs.Add(buff);
        Procs.OnApply(buff);
    }

    public bool IsVisible
    {
        get => spriteRenderer.enabled;
        set
        {
            spriteRenderer.enabled = value;
            //lightSource.enabled = value;
        }
    }

    public List<string> AvailableSkills { get; set; }
    public ProcReceiver Procs { get; set; }

    public IEnumerable<Vector3Int> ReachablePositionsWithAttack
    {
        get
        {
            var apNeededForAttack = MinApCostsForAttack();
            var apLeftToMove = ActionPoints - apNeededForAttack;
            var scaledApToMove = apLeftToMove * 100;
            return ReachablePositions.Where(p => pathfinder.GetPathCost(p) <= scaledApToMove);
        }
    }
    
    public IEnumerable<Vector3Int> ReachablePositionsWithoutAttack
    {
        get
        {
            var apNeededForAttack = MinApCostsForAttack();
            var apLeftToMove = ActionPoints - apNeededForAttack;
            var scaledApToMove = apLeftToMove * 100;
            return ReachablePositions.Where(p => pathfinder.GetPathCost(p) > scaledApToMove);
        }
    }

    public void MarkAsSelected(bool asTarget = false)
    {
        var unitMarker = _map.GetUnitMarker();        
        markerRenderer = unitMarker.GetComponent<SpriteRenderer>();
        markerRenderer.enabled = true;
        SetMarkerSize(unitMarker);
        UpdateMarker(asTarget);
        flashAnimation.Play();        
    }

    public void Deselect()
    {
        if (markerRenderer != null)
        {
            markerRenderer.transform.SetParent(null);
            markerRenderer.enabled = false;
            markerRenderer = null;
        }
    }

    private void SetMarkerSize(GameObject unitMarker)
    {
        if (OccupiedTiles.Count == 1)
        {
            unitMarker.transform.position = transform.position;
            unitMarker.transform.SetParent(transform);
            unitMarker.transform.localScale = new Vector3(1, 1, 1);
            return;
        }
        
        int maxX = OccupiedTiles.Max(offset => offset.x);
        int minX = OccupiedTiles.Min(offset => offset.x);
            
        int maxY = OccupiedTiles.Max(offset => offset.y);
        int minY = OccupiedTiles.Min(offset => offset.y);

        int sizeX = (maxX+1) - minX;
        int sizeY = (maxY+1) - minY;

        unitMarker.transform.localScale = new Vector3(sizeX, sizeY, 1);
        
        unitMarker.transform.position = spriteRenderer.transform.position;
        unitMarker.transform.SetParent(transform);
    }

    private void UpdateMarker(bool asTarget = false)
    {
        if (markerRenderer != null && markerRenderer.transform.IsChildOf(transform))
        {
            if (asTarget)
            {
                markerRenderer.color = new Color(1.0f, 0.64f, 0.0f);
            }
            else if (ActionPoints == Rules.Stats["ActionPoints"])
            {
                markerRenderer.color = Color.green;
            }
            else if (ActionPoints < Rules.Stats["ActionPoints"] / 2)
            {
                markerRenderer.color = Color.yellow;
            }
            else if (ActionPoints < Rules.Stats["ActionPoints"] / 4)
            {
                markerRenderer.color = Color.red;
            }
            else if (ActionPoints < 1)
            {
                markerRenderer.color = Color.gray;
            }
        }
    }

    public int DamageReceived(Unit source, Weapon weaponUsed, int damage)
    {
        
        var oldHealth = CurrentHealth;
        CurrentHealth -= damage;
        Debug.Log(name + " received " + damage + " damage from " + source + ", HP left: " + CurrentHealth);
        UpdateHealthBar();
        
        if (damage > oldHealth)
        {
            var damageLeft = damage - oldHealth;
            //Debug.Log("Damage left: " + damageLeft);
            return damageLeft;
        }

        return 0;
    }

    public void SetLabel(Text text)
    {
        unitLabel = text;
    }

    public bool IsNextTo(Unit target)
    {
        foreach (var targetOccupiedTile in target.OccupiedTiles)
        {
            foreach (var occupiedTile in OccupiedTiles)
            {
                if (_map.AreNeighbors(Cell + occupiedTile, target.Cell + targetOccupiedTile))
                {
                    return true;
                }
            }
        }

        return false;
    }

    public bool CanAttackUnit(IPlayerUnit otherUnit)
    {
        return CanAttack()
               && (HasMeleeWeapon && _map.AreNeighbors(Cell, otherUnit.Cell) ||
                   HasRangedWeapon && ValidRangedTargets.Contains(otherUnit.Cell));
    }

    public bool CanAttack()
    {
        var minApCosts = MinApCostsForAttack();
        return ActionPoints >= minApCosts;
    }

    private int MinApCostsForAttack()
    {
        var meleeCosts = HasMeleeWeapon ? MeleeWeapon.Rules.UseCost : int.MaxValue;
        var rangedCosts = HasRangedWeapon ? RangedWeapon.Rules.UseCost : int.MaxValue;

        var minApCosts = Mathf.Min(meleeCosts, rangedCosts);
        return minApCosts;
    }

    public bool CanAffordRangedAttack()
    {
        var rangedCosts = HasRangedWeapon ? RangedWeapon.Rules.UseCost : int.MaxValue;
        return ActionPoints >= rangedCosts; 
    }

    public bool CanAffordMeleeAttack()
    {
        var meleeCosts = HasMeleeWeapon ? MeleeWeapon.Rules.UseCost : int.MaxValue;
        return ActionPoints >= meleeCosts;
    }
    public HashSet<Vector3Int> GetValidRangedAttackTargetsAt(Vector3Int mapPos)
    {
        HashSet<Vector3Int> validTargets = new HashSet<Vector3Int>();
        if (HasRangedWeapon)
        {
            var weaponRules = RangedWeapon.Rules;            
            var maxDistance = weaponRules.EffectiveRange;
            if (weaponRules.StraightLineOnly)
            {
                validTargets.UnionWith(_map.GetStraightLine(mapPos, Direction.North, maxDistance));
                validTargets.UnionWith(_map.GetStraightLine(mapPos, Direction.South, maxDistance));
                validTargets.UnionWith(_map.GetStraightLine(mapPos, Direction.East, maxDistance));
                validTargets.UnionWith(_map.GetStraightLine(mapPos, Direction.West, maxDistance));
                validTargets.UnionWith(_map.GetStraightLine(mapPos, Direction.NorthEast, maxDistance));
                validTargets.UnionWith(_map.GetStraightLine(mapPos, Direction.NorthWest, maxDistance));
                validTargets.UnionWith(_map.GetStraightLine(mapPos, Direction.SouthEast, maxDistance));
                validTargets.UnionWith(_map.GetStraightLine(mapPos, Direction.SouthWest, maxDistance));   
            }
            else
            {
                validTargets = _map.GetFOV(this, mapPos, maxDistance, false, false, true);
            }
            
            validTargets.Remove(mapPos);

            if (!Options.AllowRangedWeaponsRangeOne)
            {
                foreach (var direction in _map.Cardinal)
                {
                    validTargets.Remove(mapPos + direction.ToVector());
                }
            }
            if (!Options.AllowDiagonalAdjacentAttacks)
            {
                foreach (var direction in _map.Diagonal)
                {
                    validTargets.Remove(mapPos + direction.ToVector());
                }
            }
        }
        
        return validTargets;
    }
    
    public HashSet<IPlayerUnit> UpdateFoV(bool removeFogVisually, bool removeFogMechanically)
    {
        var previouslyVisibleEnemies = _visibleEnemies;
        _fieldOfView = CreateFieldOfView(removeFogVisually, removeFogMechanically);
        _visibleEnemies = new HashSet<IPlayerUnit>(FieldOfView.Where(pos => _map.IsOtherFactionOn(ControllingPlayer, pos)).Select(pos => _map.GetUnitAt(pos)));

        var becameVisible = new HashSet<IPlayerUnit>(_visibleEnemies);
        becameVisible.ExceptWith(previouslyVisibleEnemies);
        return becameVisible;
    }

    private HashSet<Vector3Int> CreateFieldOfView(bool removeFogVisually, bool removeFogMechanically)
    {
        return _map.GetFOV(this, Cell, CurrentVisionRange, removeFogVisually, removeFogMechanically);
    }

    public void UpdateReachablePositions()
    {
        _reachablePositions = new Lazy<List<Vector3Int>>(CreateMovementMap);
    }
    public void UpdateValidAttackTargets()
    {
        _validTargets = new Lazy<HashSet<Vector3Int>>(() => GetValidRangedAttackTargetsAt(Cell));
    }
    private void AddToValidTargets(ref HashSet<Vector3Int> validTargets, Vector3Int pos)
    {
        if (_map.Contains(pos.x, pos.y) && !_map.IsWallAt(pos))
        {
            validTargets.Add(pos);
        }
    }
    
    public override string ToString()
    {
        return name + "\nM:" + ActionPoints + "/H:" + CurrentHealth + "/A:" + CurrentArmor + "/ACC:" + GetCurrentStat("Accuracy") + "@" + Cell;
    }
    

    private List<Vector3Int> CreateMovementMap()
    {
        pathfinder.MaxReachableCost = GetCurrentStat("ActionPoints") + cardinalStepCost;
        pathfinder.MaxCost =  -1;
        pathfinder.CurrentUnit = this;
        pathfinder.Dijkstra(new HashSet<Vector3Int>(){Cell}, new HashSet<Vector3Int>());
        return ActionPoints == 0 ? new List<Vector3Int>() : pathfinder.ReachableList.ToList();
    }

    public void LabelMovementMap()
    {
        _map.RemoveAllCellLabels();
        foreach (var reachablePosition in ReachablePositions)
        {
            var dist = pathfinder.GetPathCost(reachablePosition);
            _map.AddTextLabelAt(reachablePosition, dist.ToString(CultureInfo.InvariantCulture));
        }
    }
    
    public List<Vector3Int> GetShortestPath(Vector3Int startPosition, Vector3Int goalPosition)
    {
//        Debug.Log("GetShortestPath(" + startPosition + ", " + goalPosition + ")");
        //_map.SetTextAt(startPosition, "BGN");
        //_map.SetTextAt(goalPosition, "END");
        var shortestPath = pathfinder.GetShortestPath(goalPosition);
        if (Options.Debug) shortestPath.ForEach(p => _map.AddTextLabelAt(p, "."));
        return shortestPath;
    }
    
    public List<Vector3Int> GetShortestPath(Vector3Int goalPosition)
    {
        return GetShortestPath(Cell, goalPosition);
    }
    
    public List<IPlayerUnit> VisibleEnemiesFrom(Vector3Int reachableMapPosition)
    {
        var fov = _map.GetFOV(this, reachableMapPosition, CurrentVisionRange, false, false);
        return fov.Where(pos => _map.IsOtherFactionOn(ControllingPlayer, pos)).Select(pos => _map.GetUnitAt(pos)).ToList();
    }

    public int GetCurrentStat(string statName)
    {
        int statCurrentValue = stats[statName].CurrentValue;
        
        foreach (var buff in activeBuffs)
        {
            if (buff.Modifiers.ContainsKey(statName))
            {
                statCurrentValue += buff.Modifiers[statName].CurrentValue;
            }
        }

        return statCurrentValue;
    }
    
    public void SetCurrentStat(string statName, int value)
    {
        int rest = stats[statName].Set(value);

        if (rest > 0)
        {
            foreach (var buff in activeBuffs)
            {
                if (!buff.Modifiers.ContainsKey(statName)) continue;
                
                rest = buff.Modifiers[statName].Set(rest);
                if (rest <= 0)
                {
                    break;
                }
            }
        }
    }
    
    public int GetMaxStat(string statName)
    {
        int statCurrentValue = stats[statName].MaxValue;
        
        foreach (var buff in activeBuffs)
        {
            if (buff.Modifiers.ContainsKey(statName))
            {
                statCurrentValue += buff.Modifiers[statName].MaxValue;
            }
        }

        return statCurrentValue;
    }

    public void ChangeCurrentStat(string statName, int change)
    {
        var absorbed = stats[statName].Change(change);
        var rest = change - absorbed;
        if (rest > 0)
        {
            foreach (var buff in activeBuffs)
            {
                rest = buff.OnCurrentStatChange(statName, rest);
                if (rest <= 0)
                {
                    break;
                }
            }
        }
    }

    public void DisableActions()
    {
        ActionPoints = 0;
        UpdateReachablePositions();
        UpdateMarker();
    }

    public void DeductActionPoints(int actionCost)
    {
        ActionPoints -= actionCost;
        UpdateReachablePositions();
        UpdateMarker();
    }
}


public delegate void UnitEvent(object sender, UnitEventArgs args);

public class UnitEventArgs
{
    public Unit unit
    {
        get;
        set;
    }
    
    public Vector3Int previousPosition
    {
        get;
        set;
    }
}
