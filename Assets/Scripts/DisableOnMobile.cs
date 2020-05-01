using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DisableOnMobile : MonoBehaviour
{
    private void Awake()
    {
#if UNITY_IOS || UNITY_ANDROID
        gameObject.SetActive(false);
#endif
    }

    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
