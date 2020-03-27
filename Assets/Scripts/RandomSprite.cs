using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class RandomSprite : MonoBehaviour
{
    public Sprite[] frames;
    private int frameCount;
    private SpriteRenderer spriteRenderer;

    // Start is called before the first frame update
    void Start()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        frameCount = frames.Length;
        spriteRenderer.sprite = frames[Random.Range(0, frameCount-1)];
    }

}
