using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DefaultNamespace;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class MainMenu : MonoBehaviour
{
    public GameObject mainMenu;
    public GameObject chooseFactionMenu;
    public GameObject skirmishMenu;

    public GameObject[] skirmishPlayerSlots;
    public GameObject[] skirmishPlayerFactions;
    public Sprite[] factionSprites;
    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public void Button1()
    {
        //GameController.ChosenMode
        GameController.ChosenMode = GameController.GameMode.Campaign;
        mainMenu.SetActive(false);
        chooseFactionMenu.SetActive(true);
    }

    public void Button2()
    {        
        GameController.ChosenMode = GameController.GameMode.Skirmish;
        mainMenu.SetActive(false);
        skirmishMenu.SetActive(true);
    }
    public void Button3()
    {
        Application.Quit();
    }

    public void CampaignSelection(int factionIndex)
    {
        GameController.CampaignFaction = factionIndex;
        LoadBattleGame();
    }
    
    public void ToggleSkirmishFaction(int playerIndex)
    {
        var currentFaction = GameController.SkirmishFactions[playerIndex];
        currentFaction = (currentFaction + 1) % 4;
        GameController.SkirmishFactions[playerIndex] = currentFaction;
        skirmishPlayerFactions[playerIndex].GetComponent<Image>().sprite = factionSprites[currentFaction];
    }

    public void AddSkirmishPlayerSlot()
    {
        foreach (var playerSlot in skirmishPlayerSlots)
        {
            if (!playerSlot.activeInHierarchy)
            {
                playerSlot.SetActive(true);
                return;
            }
        }
    }
    
    public void RemoveSkirmishPlayerSlot()
    {
        for (var index = skirmishPlayerSlots.Length - 1; index >= 2; index--)
        {
            var playerSlot = skirmishPlayerSlots[index];
            if (playerSlot.activeInHierarchy)
            {
                playerSlot.SetActive(false);
                return;
            }
        }
    }

    public void LoadMapGenerator()
    {
        SceneManager.LoadScene("MapGenerator");
        GameController.SkirmishPlayerCount = skirmishPlayerSlots.Count(slot => slot.activeInHierarchy);
    }
    
    public void LoadBattleGame()
    {        
        SceneManager.LoadScene("BattleGameScene");
    }
}
