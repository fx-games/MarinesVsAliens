using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class PopUp : MonoBehaviour
{
    public Text outlineText;
    public Text fillText;
    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        if (Input.GetMouseButtonUp(0) || Input.GetMouseButtonUp(1))
        {
            gameObject.SetActive(false);
        }
    }

    private void SetText(string text)
    {
        outlineText.text = text;
        fillText.text = text;
    }
}
