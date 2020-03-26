using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static Map;

public class Unit : MonoBehaviour
{
    enum UnitState { Idle, Moving }
    public int mobility = 4;
    public int visionRange = 20;
    private Map gameMap;
    private int currentPathIndex;
    private List<Vector3Int> currentPath;
    private UnitState state = UnitState.Idle;
    private float lastMoveTime;
    public double moveDelay = 0.5f;
    

    public event UnitEvent FinishedMoving;
    // Start is called before the first frame update
    void Start()
    {
        gameMap = FindObjectOfType<Map>();
    }

    // Update is called once per frame
    void Update()
    {
        if (state == UnitState.Moving && (Time.time - lastMoveTime) > moveDelay)
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
            state = UnitState.Moving;
            lastMoveTime = Time.time;
            AnimateMovement(nextPos);
            UpdateDirection(lastPos, nextPos);
        }
        else
        {
            state = UnitState.Idle;
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
        state = UnitState.Moving;
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
