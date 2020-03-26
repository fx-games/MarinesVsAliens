using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using GridPathFinder;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Color = UnityEngine.Color;

public class Game : MonoBehaviour
{
    
    public GameObject unitPrefab;
    private List<Unit> allUnits = new List<Unit>();
    private Map gameMap;
    public Unit selectedUnit;
    private Camera gameCamera;
    public Text topLeftLabel;
    private Pathfinder pathfinder;

    // Start is called before the first frame update
    void Awake()
    {
        
    }

    void Start()
    {
        gameMap = FindObjectOfType<Map>();
        gameCamera = Camera.main;
        if (gameCamera == null)
        {
            throw new Exception("Game needs camera.");
        }
        pathfinder = new Pathfinder(gameMap, 1.0f, 1.0f);
        SpawnUnits();
    }

    
    // Update is called once per frame
    void Update()
    {
        if (!EventSystem.current.IsPointerOverGameObject())
        {
            if (Input.GetMouseButtonUp(0))
            {
                Vector3 pos = gameCamera.ScreenToWorldPoint(Input.mousePosition);
                var mapPos = gameMap.WorldToCell(pos);
                if (gameMap.Contains(mapPos.x, mapPos.y))
                    OnMapClicked(mapPos);
            }
            else if (Input.GetMouseButtonUp(1))
            {
                Vector3 pos = gameCamera.ScreenToWorldPoint(Input.mousePosition);
                var mapPos = gameMap.WorldToCell(pos);
                if (gameMap.Contains(mapPos.x, mapPos.y))
                    OnMapRightClicked(mapPos);
            }
        }
    }
    private void OnMapClicked(Vector3Int mapPos)
    {
        if (gameMap.IsUnitOn(mapPos))
        {
            selectedUnit = gameMap.GetUnitAt(mapPos);
            ShowReachableTiles(mapPos, selectedUnit.mobility);
        }
        else
        {
            selectedUnit = null;
            gameMap.RemoveAllHighlights();
        }
        UpdateLabel();
    }

    public void AttackButtonClicked()
    {
        if (selectedUnit != null)
        {
            
            ShowAttackableTiles(gameMap.WorldToCell(selectedUnit.transform.position), 20);
        }
    }

    private void ShowAttackableTiles(Vector3Int mapPos, int maxDistance)
    {
        gameMap.RemoveAllHighlights();
        var visibleTiles = gameMap.GetFOV(mapPos, maxDistance);
        foreach (var tile in visibleTiles)
        {
            gameMap.HighlightTile(tile, new Color(0.6f, 0f, 0, 0.5f));
        }   
    }

    private void ShowReachableTiles(Vector3Int mapPos, int maxDistance)
    {
        gameMap.RemoveAllHighlights();
        pathfinder.MaxCost = maxDistance;
        pathfinder.Dijkstra(new HashSet<Point>(){new Point(mapPos.x, mapPos.y)}, new HashSet<Point>());
        pathfinder.PrintDistMap();
        var nodeList = pathfinder.ReachableList.ToList().ConvertAll(point => new Vector3Int(point.X, point.Y, 0));
        foreach (var reachablePos in nodeList)
        {
            gameMap.HighlightTile(reachablePos, new Color(0, 0.8f, 0, 0.5f));
        }
    }

    private void OnMapRightClicked(Vector3Int mapPos)
    {
        if (!gameMap.IsUnitOn(mapPos) && selectedUnit != null)
        {
            
            var path = pathfinder.GetShortestPath(new Point(mapPos.x, mapPos.y)).ConvertAll(p => new Vector3Int(p.X, p.Y, 0));
            if (path.Count > 0)
            {
                gameMap.RemoveAllHighlights();
                selectedUnit.BeginMoving(path);   
            }
        }
    }

    private void UnitFinishedMoving(object sender, UnitEventArgs args)
    {
        //var unit = args.unit;
        GlobalFOVUpdate();
    }

    private void GlobalFOVUpdate()
    {
        gameMap.ResetFog();
        foreach (var unit in allUnits)
        {
            gameMap.UpdateFOV(gameMap.WorldToCell(unit.transform.position), unit.visionRange);
        }
    }


    public void SetLabel(string text)
    {
        topLeftLabel.text = text;
    }

    private void UpdateLabel()
    {
        if (selectedUnit != null)
        {
            SetLabel(gameMap.CurrentCursorPosition + " - Sel.: " + selectedUnit.name);
        }
        else
        {
            SetLabel(gameMap.CurrentCursorPosition.ToString());
        }
    }

    private void SpawnUnits()
    {
        List<Vector3Int> spawnPositions = new List<Vector3Int> {new Vector3Int(0, 0, 0), new Vector3Int(0, 1, 0)};
        foreach (var spawnPosition in spawnPositions)
        {
            var unit = Instantiate(unitPrefab, gameMap.CellToWorld(spawnPosition), Quaternion.identity)
                .GetComponent<Unit>();
            if (gameMap.AddUnit(unit, spawnPosition))
            {
                unit.FinishedMoving += UnitFinishedMoving;
                allUnits.Add(unit);
                gameMap.UpdateFOV(spawnPosition, unit.visionRange);
            }
            else
            {
                Destroy(unit.gameObject);
                Debug.LogWarning("Could not spawn unit at: " + spawnPosition);
            }
        }
    }
}
