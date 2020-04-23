using UnityEngine;
using UnityEngine.Serialization;
using WorldEffects;

public class SimpleAnimation : MonoBehaviour
{
    public Sprite[] frames;
    public bool DestroyOnFinish = true;
    private int currentFrame = 0;
    private int frameCount;
    private float delay = 0.1f;
    private SpriteRenderer spriteRenderer;
    private float lastAnimTime = 0;
    private SimpleAudio simpleAudio;
    private bool animationFinished = false;
    private bool audioFinished = false;
    private bool completed = false;

    public event VoidEvent OnCompleted;
    // Start is called before the first frame update
    void Start()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        simpleAudio = GetComponent<SimpleAudio>();
        if (simpleAudio != null)
        {
            simpleAudio.OnCompleted += AudioCompleted;
        }
        else // no audio
        {
            audioFinished = true;
        }
        frameCount = frames.Length;
        spriteRenderer.sprite = frames[currentFrame];
    }

    private void AudioCompleted(object sender)
    {
        audioFinished = true;
    }

    // Update is called once per frame
    void Update()
    {
        if (!animationFinished && Time.time - lastAnimTime > delay)
        {
            currentFrame++;
            if (currentFrame < frameCount)
            {
                spriteRenderer.sprite = frames[currentFrame];
                lastAnimTime = Time.time;
            }
            else
            {
                animationFinished = true;
            }
        }

        if (audioFinished && animationFinished && !completed)
        {
            completed = true;
            OnCompleted?.Invoke(this);
            if (DestroyOnFinish)
            {
                Destroy(gameObject);
            }
        }
    }
}
