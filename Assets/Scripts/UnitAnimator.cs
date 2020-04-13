using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class UnitAnimator : MonoBehaviour
{
    public Sprite[] frames;
    private int currentFrame = 0;
    private int frameCount;
    private float delay = 0.5f;
    private SpriteRenderer spriteRenderer;
    private float lastAnimTime = 0;
    private bool hasStarted = false;

    // Start is called before the first frame update
    void Start()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        frameCount = frames.Length;
        spriteRenderer.sprite = frames[currentFrame];
        delay += Random.Range(-0.1f, 0.1f);
        Invoke("StartAnimate", Random.value * 2);
    }

    void StartAnimate()
    {
        hasStarted = true;
    }

    // Update is called once per frame
    void Update()
    {
        if (!hasStarted) return;
        if (Time.time - lastAnimTime > delay)
        {
            currentFrame++;
            if (currentFrame < frameCount)
            {
                spriteRenderer.sprite = frames[currentFrame];
                lastAnimTime = Time.time;
            }
            else
            {
                currentFrame = 0;
                spriteRenderer.sprite = frames[currentFrame];
                lastAnimTime = Time.time;
            }
        }
    }
}
