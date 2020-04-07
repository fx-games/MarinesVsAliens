using System.Collections;
using System.Collections.Generic;
using DefaultNamespace;
using UnityEngine;
using UnityEngine.SceneManagement;

public class MainMenu : MonoBehaviour
{
    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }


    public void PlayHotSeat()
    {
        GameController.ChosenMode = GameController.GameMode.HotSeat;
        SceneManager.LoadScene("BattleGameScene");
    }

    public void PlayAgainstAI()
    {
        GameController.ChosenMode = GameController.GameMode.AgainstAI;
        SceneManager.LoadScene("BattleGameScene");
    }

    public void WatchAIPlay()
    {
        GameController.ChosenMode = GameController.GameMode.WatchAI;
        SceneManager.LoadScene("BattleGameScene");
    }
}
