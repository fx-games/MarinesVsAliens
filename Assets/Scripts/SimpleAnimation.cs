using System.Collections;
using System.Collections.Generic;
using UnityEditor.U2D;
using UnityEngine;

public class SimpleAnimation : MonoBehaviour
{
    public Sprite[] frames;
    public bool DestroyOnFinish = true;
    private int currentFrame = 0;
    private int frameCount;
    public int delayFrames = 4;
    private SpriteRenderer spriteRenderer;
    private int lastAnimFrame = 0;

    // Start is called before the first frame update
    void Start()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        frameCount = frames.Length;
        spriteRenderer.sprite = frames[currentFrame];
    }

    // Update is called once per frame
    void Update()
    {
        if (Time.frameCount - lastAnimFrame > delayFrames)
        {
            currentFrame++;
            if (currentFrame < frameCount)
            {
                spriteRenderer.sprite = frames[currentFrame];
                lastAnimFrame = Time.frameCount;
            }
            else if (DestroyOnFinish)
            {
                Destroy(gameObject);
            }
        }
    }
}
