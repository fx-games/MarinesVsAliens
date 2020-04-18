using UnityEngine;
using UnityEngine.Serialization;

public class SimpleAnimation : MonoBehaviour
{
    public Sprite[] frames;
    public bool DestroyOnFinish = true;
    private int currentFrame = 0;
    private int frameCount;
    private float delay = 0.1f;
    private SpriteRenderer spriteRenderer;
    private float lastAnimTime = 0;

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
        if (Time.time - lastAnimTime > delay)
        {
            currentFrame++;
            if (currentFrame < frameCount)
            {
                spriteRenderer.sprite = frames[currentFrame];
                lastAnimTime = Time.time;
            }
            else if (DestroyOnFinish)
            {
                Destroy(gameObject);
            }
        }
    }
}
