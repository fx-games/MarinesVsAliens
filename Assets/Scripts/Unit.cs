using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static Map;

public class Unit : MonoBehaviour
{
    public enum UnitState { Idle, Moving }

    public GameObject bloodSplatter;
    public int mobility = 4;
    public int visionRange = 20;
    public int health = 1;
    public int armor = 2;

    private Map gameMap;
    private int currentPathIndex;
    private List<Vector3Int> currentPath;
    public UnitState State { get; private set; } = UnitState.Idle;
    private float lastMoveTime;
    public double moveDelay = 0.5f;
    private Mod mod;
    
    public event UnitEvent FinishedMoving;
    // Start is called before the first frame update
    void Awake()
    {
        gameMap = FindObjectOfType<Map>();
        mod = FindObjectOfType<Mod>();
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
        currentPathIndex++;
        if (currentPathIndex < currentPath.Count)
        {
            var lastPos = gameMap.WorldToCell(transform.position);
            var nextPos = currentPath[currentPathIndex];
            State = UnitState.Moving;
            lastMoveTime = Time.time;
            AnimateMovement(nextPos);
            UpdateDirection(lastPos, nextPos);
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
        var oldMapPos = gameMap.WorldToCell(transform.position);
        currentPathIndex = 0;
        currentPath = path;
        State = UnitState.Moving;
        lastMoveTime = Time.time;
        // TELEPORT INSTANTLY TO THE END OF THE PATH ON THE MAP - BECAUSE OF PATHFINDING OF OTHER UNITS
        gameMap.MoveUnit(this, oldMapPos, path.Last());
    }


    public void AnimateMovement(Vector3Int mapPos)
    {
        var cellToWorld = gameMap.CellToWorld(mapPos);
        transform.position = cellToWorld;
        gameMap.UpdateFOV(mapPos, visionRange);
    }

    public void InflictDamage(int damage)
    {
        health -= damage;
    }

    public void UnitDeath()
    {
        Instantiate(bloodSplatter, transform.position, Quaternion.identity);
        Destroy(gameObject);
    }

    public void LoadRules(string unitType)
    {
        var rules = mod.GetUnitRule(unitType);
        mobility = rules.Movement;
        health = rules.Health;
        armor = rules.Armor;
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
