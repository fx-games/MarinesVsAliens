using System;
using System.Collections.Generic;
using System.Linq;
using DefaultNamespace;
using GridPathFinder;
using UnityEngine;
using UnityEngine.UI;
using static Map;
using static Mod;
using static Mod.RuleFaction;
using Random = UnityEngine.Random;


public class Unit : MonoBehaviour, IPlayerUnit
{
    public enum UnitState { Idle, Moving }

    public Weapon RangedWeapon { get; private set; }
    public Weapon MeleeWeapon { get; private set; }
    public GameObject bloodSplatter;
    public int MovesLeft { get; private set; } = 4;
    public int CurrentVisionRange { get; } = 20;
    public int CurrentHealth { get; private set; } = 1;
    public int CurrentArmor { get; set; } = 2;
    public bool CanAttack { get; private set; } = true;
    public RuleFaction Faction { get; set; }
    
    public Vector3Int Cell => _map.WorldToCell(transform.position);

    private float lastMoveTime;
    private double moveDelay = 0.3f;
    private Mod mod;
    private RuleUnit unitRules;

    private Map _map;
    private int currentPathIndex;
    private List<Vector3Int> currentPath;
    private SpriteRenderer spriteRenderer;
    private SpriteRenderer markerRenderer;
    private Text unitLabel;
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

    public bool CanAct => CanAttack || MovesLeft > 0;
    public RuleUnit Rules => unitRules;
    public List<Vector3Int> OccupiedTiles { get; set; }
    public List<IPlayerUnit> VisibleEnemies { get; set; } = new List<IPlayerUnit>();
    public HashSet<Vector3Int> FieldOfView { get; set; }

    public List<Vector3Int> ReachablePositions
    {
        get => _reachablePositions.Value.Where(p => !_map.IsUnitOn(p)).ToList();
    }

    private Pathfinder pathfinder;
    private Lazy<List<Vector3Int>> _reachablePositions;
    private Lazy<HashSet<Vector3Int>> _validTargets;
    public event UnitEvent FinishedMoving;
    public event UnitEvent StepCompleted;
    // Start is called before the first frame update
    void Awake()
    {
        gameCamera = Camera.main;
        _map = FindObjectOfType<Map>();
        mod = FindObjectOfType<Mod>();
        spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        pathfinder = new Pathfinder(_map, 1.0f, 1.41f);
        _reachablePositions = new Lazy<List<Vector3Int>>(CreateMovementMap);
    }

    
    // Update is called once per frame
    void Update()
    {
        if (State == UnitState.Moving && (Time.time - lastMoveTime) > moveDelay)
        {
            NextMove();
        }
    }

    private void OnDestroy()
    {
        //Debug.Log("OnDestroy of " + name);
        if (unitLabel != null)
        {
            Destroy(unitLabel);
        }
    }

    public void SetLabelText(string text)
    {
        unitLabel.enabled = true;
        unitLabel.transform.position = transform.position;
        unitLabel.text = text;
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
        if (currentPathIndex < currentPath.Count)
        {
            Vector3Int lastPos = _map.WorldToCell(transform.position);
            Vector3Int nextPos = currentPath[currentPathIndex];
            State = UnitState.Moving;
            lastMoveTime = Time.time;
            RepositionUnitInWorld(nextPos);
            UpdateDirection(lastPos, nextPos);
            OnStepCompleted(lastPos);
            currentPathIndex++;
        }
        else
        {
            State = UnitState.Idle;
            currentPathIndex = -1;
            currentPath = null;

            _reachablePositions = new Lazy<List<Vector3Int>>(CreateMovementMap);
            UpdateValidAttackTargets();
            OnFinishedMoving();
        }
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

    public void BeginMoving(List<Vector3Int> path)
    {
        if (State == UnitState.Moving) return;
        var oldMapPos = _map.WorldToCell(transform.position);
        path.Remove(oldMapPos);
        currentPathIndex = 0;
        currentPath = path;
        State = UnitState.Moving;
        int steps = path.Count;
        //Debug.Log("Moving: " + steps);
        MovesLeft -= steps;
        // TELEPORT INSTANTLY TO THE END OF THE PATH ON THE MAP - BECAUSE OF PATHFINDING OF OTHER UNITS
        _map.MoveUnit(this, oldMapPos, path.Last());
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

    public void LoadRules(string unitType, RuleFaction faction, RuleLoadout loadout)
    {
        Faction = faction;
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
        CurrentArmor = unitRules.Armor;
        CurrentHealth = unitRules.Health;
        MovesLeft = unitRules.Movement;

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

    public void HasAttacked()
    {
        if (MovesLeft != unitRules.Movement)
        {
            MovesLeft = 0;    // can not move before AND after firing
        }
        CanAttack = false;
        UpdateMarker();
    }

    public void PrepareNewPhase()
    {
        MovesLeft = unitRules.Movement;
        CanAttack = true;
        UpdateValidAttackTargets();
        _reachablePositions = new Lazy<List<Vector3Int>>(CreateMovementMap);
    }

    public HashSet<Vector3Int> ValidTargets
    {
        get => _validTargets.Value;
    }

    public void SetVisible(bool isVisible)
    {
        spriteRenderer.enabled = isVisible;
    }
    
    public void MarkAsSelected()
    {
        var unitMarker = _map.GetUnitMarker();
        
        markerRenderer = unitMarker.GetComponent<SpriteRenderer>();
        markerRenderer.enabled = true;
        SetMarkerSize(unitMarker);
        UpdateMarker();
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

    public void UpdateMarker()
    {
        if (markerRenderer != null && markerRenderer.transform.IsChildOf(transform))
        {
            if (MovesLeft > 0 && CanAttack)
            {
                markerRenderer.color = Color.green;
            }
            else if (MovesLeft <= 0 && CanAttack)
            {
                markerRenderer.color = Color.red;
            }
            else if (!CanAttack && MovesLeft > 0)
            {
                markerRenderer.color = Color.yellow;
            }
            else
            {
                markerRenderer.color = Color.gray;
            }
        }
    }

    public int GetDefense(bool isMelee)
    {
        if (isMelee)
        {
            return HasMeleeWeapon ? MeleeWeapon.RollDamage().Sum() : 0;
        }

        return CurrentArmor;
    }

    public int DamageReceived(Unit source, Weapon weaponUsed, int damage)
    {
        //Debug.Log(name + " received " + damage + " damage");
        var oldHealth = CurrentHealth;
        CurrentHealth -= damage;
        
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
        return CanAttack
               && (HasMeleeWeapon && _map.AreNeighbors(Cell, otherUnit.Cell) ||
                   HasRangedWeapon && ValidTargets.Contains(otherUnit.Cell));
    }

    public HashSet<Vector3Int> GetValidAttackTargetsAt(Vector3Int mapPos)
    {
        int maxDistance = 20;
        HashSet<Vector3Int> validTargets = new HashSet<Vector3Int>();
        if (HasRangedWeapon)
        {
            var weaponRules = RangedWeapon.Rules;
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
                validTargets = _map.GetFOV(mapPos, maxDistance, false);
            }
            
            validTargets.Remove(mapPos);
        }
        
        if (!HasMeleeWeapon) // can only attack these tiles with melee
        {
            validTargets.Remove(mapPos + Vector3Int.right);
            validTargets.Remove(mapPos + Vector3Int.up);
            validTargets.Remove(mapPos + Vector3Int.down);
            validTargets.Remove(mapPos + Vector3Int.left);
        }
        else
        {
            AddToValidTargets(ref validTargets,mapPos + Vector3Int.right);
            AddToValidTargets(ref validTargets,mapPos + Vector3Int.up);
            AddToValidTargets(ref validTargets,mapPos + Vector3Int.down);
            AddToValidTargets(ref validTargets,mapPos + Vector3Int.left);
        }

        if (!Options.AllowDiagonalAdjacentAttacks)
        {
            validTargets.Remove(mapPos + Vector3Int.up + Vector3Int.right);
            validTargets.Remove(mapPos + Vector3Int.down + Vector3Int.left);
            validTargets.Remove(mapPos + Vector3Int.down + Vector3Int.right);
            validTargets.Remove(mapPos + Vector3Int.up + Vector3Int.left);
        }

        return validTargets;
    }
    
    private void UpdateValidAttackTargets()
    {
        _validTargets = new Lazy<HashSet<Vector3Int>>(() => GetValidAttackTargetsAt(Cell));
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
        return name + " (M:" + MovesLeft + "/H:"+ CurrentHealth+")@"+Cell;
    }
    

    private List<Vector3Int> CreateMovementMap()
    {
        pathfinder.MaxReachableCost = MovesLeft;
        pathfinder.MaxCost =  -1;
        pathfinder.CurrentUnit = this;
        pathfinder.Dijkstra(new HashSet<Vector3Int>(){Cell}, new HashSet<Vector3Int>());
        return pathfinder.ReachableList.ToList();
    }
    
    public List<Vector3Int> GetShortestPath(Vector3Int startPosition, Vector3Int goalPosition)
    {
//        Debug.Log("GetShortestPath(" + startPosition + ", " + goalPosition + ")");
        //_map.SetTextAt(startPosition, "BGN");
        //_map.SetTextAt(goalPosition, "END");
        var shortestPath = pathfinder.GetShortestPath(goalPosition);
        if (Options.Debug) shortestPath.ForEach(p => _map.SetTextAt(p, "."));
        return shortestPath;
    }
    
    public List<Vector3Int> GetShortestPath(Vector3Int goalPosition)
    {
        return GetShortestPath(Cell, goalPosition);
    }

    public List<IPlayerUnit> VisibleEnemiesFrom(Vector3Int reachableMapPosition)
    {
        var fov = _map.GetFOV(reachableMapPosition, CurrentVisionRange, false);
        return fov.Where(pos => _map.IsOtherFactionOn(Faction, pos)).Select(pos => _map.GetUnitAt(pos)).ToList();
    }

    public void UpdateFoV(bool removeFog)
    {
        var fov = _map.GetFOV(Cell, CurrentVisionRange, removeFog);
        FieldOfView = fov;
        VisibleEnemies = FieldOfView.Where(pos => _map.IsOtherFactionOn(Faction, pos)).Select(pos => _map.GetUnitAt(pos)).ToList();
    }
}

public class Weapon
{
    private RuleWeapon weaponRules;
    private Mod mod;

    public Weapon(Mod mod, RuleWeapon rules)
    {
        this.mod = mod;
        weaponRules = rules;
    }

    public bool HasAreaOfEffect
    {
        get { return weaponRules.Radius > 0; }
    }

    public RuleWeapon Rules => weaponRules;

    public int[] RollDamage()
    {
        int[] results = new int[weaponRules.Damage.Count];
        int diceCount = 0;
        foreach (var damageRoll in weaponRules.Damage)
        {
            if (damageRoll.Length == 1)
            {
                results[diceCount] = damageRoll[0];
            }
            else if (damageRoll.Length == 2)
            {
                results[diceCount] = Random.Range(damageRoll[0], damageRoll[1] + 1);
            }
            else if (damageRoll.Length > 2)
            {
                results[diceCount] = damageRoll[Random.Range(0, damageRoll.Length)];
            }

            diceCount++;
        }

        return results;
    }
    public override string ToString()
    {
        return mod.Tr(weaponRules.Type);
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
