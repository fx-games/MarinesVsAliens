using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Serialization;
using static Map;
using static Mod;
using static Mod.RuleFaction;

public class Unit : MonoBehaviour
{
    public enum UnitState { Idle, Moving }

    public Weapon RangedWeapon { get; set; }
    public Weapon MeleeWeapon { get; set; }
    public GameObject bloodSplatter;
    public int movesLeft = 4;
    public int currentVisionRange = 2;
    public int currentHealth = 1;
    public int currentArmor = 2;
    public bool canAttack = true;
    public RuleFaction Faction { get; set; }
    
    public Vector3Int Cell => gameMap.WorldToCell(transform.position);

    private float lastMoveTime;
    public double moveDelay = 0.5f;
    private Mod mod;
    private RuleUnit unitRules;

    private Map gameMap;
    private int currentPathIndex;
    private List<Vector3Int> currentPath;
    private SpriteRenderer spriteRenderer;
    private SpriteRenderer markerRenderer;
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

    public RuleUnit Rules => unitRules;
    public List<Vector3Int> OccupiedTiles { get; set; }

    public event UnitEvent FinishedMoving;
    public event UnitEvent StepCompleted;
    // Start is called before the first frame update
    void Awake()
    {
        gameMap = FindObjectOfType<Map>();
        mod = FindObjectOfType<Mod>();
        spriteRenderer = GetComponentInChildren<SpriteRenderer>();
    }

    // Update is called once per frame
    void Update()
    {
        if (State == UnitState.Moving && (Time.time - lastMoveTime) > moveDelay)
        {
            NextMove();
        }
    }

    private void SetDirection(Direction direction)
    {
        switch (direction)
        {
            case Direction.North:
                spriteRenderer.transform.eulerAngles = new Vector3(spriteRenderer.transform.eulerAngles.x, spriteRenderer.transform.eulerAngles.y, 0f);
                break;
            case Direction.NorthWest:
                spriteRenderer.transform.eulerAngles = new Vector3(spriteRenderer.transform.eulerAngles.x, spriteRenderer.transform.eulerAngles.y, 45f);
                break;
            case Direction.West:
                spriteRenderer.transform.eulerAngles = new Vector3(spriteRenderer.transform.eulerAngles.x, spriteRenderer.transform.eulerAngles.y, 90f);
                break;
            case Direction.SouthWest:
                spriteRenderer.transform.eulerAngles = new Vector3(spriteRenderer.transform.eulerAngles.x, spriteRenderer.transform.eulerAngles.y, 135f);
                break;
            case Direction.South:
                spriteRenderer.transform.eulerAngles = new Vector3(spriteRenderer.transform.eulerAngles.x, spriteRenderer.transform.eulerAngles.y, 180f);
                break;
            case Direction.SouthEast:
                spriteRenderer.transform.eulerAngles = new Vector3(spriteRenderer.transform.eulerAngles.x, spriteRenderer.transform.eulerAngles.y, 225f);
                break;
            case Direction.East:
                spriteRenderer.transform.eulerAngles = new Vector3(spriteRenderer.transform.eulerAngles.x, spriteRenderer.transform.eulerAngles.y, 270f);
                break;
            case Direction.NorthEast:
                spriteRenderer.transform.eulerAngles = new Vector3(spriteRenderer.transform.eulerAngles.x, spriteRenderer.transform.eulerAngles.y, 315f);
                break;
        }
    }    
    private void NextMove()
    {   
        if (currentPathIndex < currentPath.Count)
        {
            var lastPos = gameMap.WorldToCell(transform.position);
            var nextPos = currentPath[currentPathIndex];
            State = UnitState.Moving;
            lastMoveTime = Time.time;
            AnimateMovement(nextPos);
            UpdateDirection(lastPos, nextPos);
            currentPathIndex++;
        }
        else
        {
            State = UnitState.Idle;
            currentPathIndex = -1;
            currentPath = null;
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
        var oldMapPos = gameMap.WorldToCell(transform.position);
        path.Remove(oldMapPos);
        currentPathIndex = 0;
        currentPath = path;
        State = UnitState.Moving;
        int steps = path.Count;
        //Debug.Log("Moving: " + steps);
        movesLeft -= steps;
        // TELEPORT INSTANTLY TO THE END OF THE PATH ON THE MAP - BECAUSE OF PATHFINDING OF OTHER UNITS
        gameMap.MoveUnit(this, oldMapPos, path.Last());
        UpdateMarker();
    }


    public void AnimateMovement(Vector3Int mapPos)
    {
        var oldPos = gameMap.WorldToCell(transform.position);
        RepositionUnitInWorld(mapPos);
        OnStepCompleted(oldPos);
    }

    private void RepositionUnitInWorld(Vector3Int mapPos)
    {
        var cellToWorld = gameMap.CellToWorld(mapPos);
        transform.position = cellToWorld;
    }

    public void UnitDeath()
    {
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
        currentArmor = unitRules.Armor;
        currentHealth = unitRules.Health;
        movesLeft = unitRules.Movement;

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
        if (movesLeft != unitRules.Movement)
        {
            movesLeft = 0;    // can not move before AND after firing
        }
        canAttack = false;
        UpdateMarker();
    }

    public void PrepareNewTurn()
    {
        movesLeft = unitRules.Movement;
        canAttack = true;
    }

    public void SetVisible(bool isVisible)
    {
        spriteRenderer.enabled = isVisible;
    }
    
    
    public void MarkAsSelected()
    {
        var unitMarker = gameMap.GetUnitMarker();
        
        markerRenderer = unitMarker.GetComponent<SpriteRenderer>();
        markerRenderer.enabled = true;
        SetMarkerSize(unitMarker);
        UpdateMarker();
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
            if (movesLeft > 0 && canAttack)
            {
                markerRenderer.color = Color.green;
            }
            else if (movesLeft <= 0 && canAttack)
            {
                markerRenderer.color = Color.red;
            }
            else if (!canAttack && movesLeft > 0)
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
            return MeleeWeapon.RollDamage().Sum();
        }
        else
        {
            return currentArmor;
        }
    }

    public void DamageReceived(Unit source, Weapon weaponUsed, int damage)
    {
        currentHealth -= damage;
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
