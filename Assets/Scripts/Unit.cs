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

    public event UnitEvent FinishedMoving;
    // Start is called before the first frame update
    void Awake()
    {
        gameMap = FindObjectOfType<Map>();
        mod = FindObjectOfType<Mod>();
        spriteRenderer = GetComponent<SpriteRenderer>();
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
                transform.eulerAngles = new Vector3(transform.eulerAngles.x, transform.eulerAngles.y, 0f);
                break;
            case Direction.NorthWest:
                transform.eulerAngles = new Vector3(transform.eulerAngles.x, transform.eulerAngles.y, 45f);
                break;
            case Direction.West:
                transform.eulerAngles = new Vector3(transform.eulerAngles.x, transform.eulerAngles.y, 90f);
                break;
            case Direction.SouthWest:
                transform.eulerAngles = new Vector3(transform.eulerAngles.x, transform.eulerAngles.y, 135f);
                break;
            case Direction.South:
                transform.eulerAngles = new Vector3(transform.eulerAngles.x, transform.eulerAngles.y, 180f);
                break;
            case Direction.SouthEast:
                transform.eulerAngles = new Vector3(transform.eulerAngles.x, transform.eulerAngles.y, 225f);
                break;
            case Direction.East:
                transform.eulerAngles = new Vector3(transform.eulerAngles.x, transform.eulerAngles.y, 270f);
                break;
            case Direction.NorthEast:
                transform.eulerAngles = new Vector3(transform.eulerAngles.x, transform.eulerAngles.y, 315f);
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
    }


    public void AnimateMovement(Vector3Int mapPos)
    {
        var cellToWorld = gameMap.CellToWorld(mapPos);
        transform.position = cellToWorld;
        gameMap.UpdateFOV(mapPos, currentVisionRange);
    }

    public void AttackedWith(int attackValue, bool isMelee)
    {
        var defense = currentArmor;
        if (isMelee)
        {
            defense = MeleeWeapon.RollDamage();
        }
        //Debug.Log("Defense: " + defense);
        int damage = attackValue - defense;
        currentHealth -= damage;
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
                MeleeWeapon = new Weapon(weaponRules);
            }
            else
            {
                RangedWeapon = new Weapon(weaponRules);
            }
        }

        unitRules = mod.GetUnitRule(unitType);
        name = unitType;
        currentArmor = unitRules.Armor;
        currentHealth = unitRules.Health;
        movesLeft = unitRules.Movement;
    }

    public void HasAttacked()
    {
        if (movesLeft != unitRules.Movement)
        {
            movesLeft = 0;    // can not move before AND after firing
        }
        canAttack = false;
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
}

public class Weapon
{
    private RuleWeapon weaponRules;

    public Weapon(RuleWeapon rules)
    {
        weaponRules = rules;
    }

    public bool HasAreaOfEffect
    {
        get { return weaponRules.Radius > 0; }
    }

    public RuleWeapon Rules => weaponRules;

    public int RollDamage()
    {
        return Random.Range(weaponRules.Damage[0], weaponRules.Damage[1]);
    }

    public override string ToString()
    {
        return weaponRules.Type;
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
}
