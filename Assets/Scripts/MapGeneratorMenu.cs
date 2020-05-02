using System;
using System.Collections;
using System.Collections.Generic;
using DefaultNamespace;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Random = UnityEngine.Random;

public class MapGeneratorMenu : MonoBehaviour
{
    public InputField seedField;
    public GameObject mapPrefab;
    private Map currentMap;
    private GeneratorBase _generatorBase;

    public int Width = 21;

    public int Height = 21;

    private int currentSeed = -1;

    // Start is called before the first frame update
    void Start()
    {        
        currentMap = Instantiate(mapPrefab).GetComponent<Map>();
        DontDestroyOnLoad(currentMap.gameObject);        
        AlgorithmChanged(0);
        NewSeed();
        RunToCompletionButtonPressed(false);
    }

    private void NewSeed()
    {
        var randomSeed = Random.Range(Int32.MinValue, Int32.MaxValue);
        currentSeed = randomSeed;
        seedField.text = randomSeed.ToString();
        Random.InitState(randomSeed);
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public void AlgorithmChanged(int newValue)
    {
        // Re-Create the algorithm and map instance
        Debug.Log("Algo: " + newValue);
        switch (newValue)
        {
            case 0:
                _generatorBase = new BspGeneratorBase(Width, Height);
                break;
            case 1:
                _generatorBase = new CavernGenerator(Width, Height);
                break;
        }
                
        _generatorBase.SetMap(currentMap);
    }

    public void StepButtonPressed()
    {
        _generatorBase.Step();
    }

    public void RunToCompletionButtonPressed(bool newSeed = true)
    {
        if (newSeed) NewSeed();
        StartCoroutine(_generatorBase.Generate());
    }

    public void SeedChanged(string newSeed)
    {
        int seedAsInt;
        if (int.TryParse(newSeed, out seedAsInt))
        {
            Debug.Log("Seed: " + seedAsInt);
            currentSeed = seedAsInt;
            Random.InitState(seedAsInt);
            RunToCompletionButtonPressed(false);
        }
    }

    public void AcceptMap()
    {
        currentMap.RemoveAllLines();
        currentMap.RemoveAllHighlights();
        currentMap.RemoveAllCellLabels();
        currentMap.RemoveAllTileMarker();
        GameController.CurrentMapSeed = currentSeed;
        Debug.Log("Chosen Map Seed: " + currentSeed);
        SceneManager.LoadScene("BattleGameScene");
    }
}