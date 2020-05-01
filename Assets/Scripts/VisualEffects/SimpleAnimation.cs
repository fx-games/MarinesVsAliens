using UnityEngine;
using UnityEngine.Serialization;
using WorldEffects;

public class SimpleAnimation : MonoBehaviour
{
    public Sprite[] frames;
    public bool DestroyOnFinish = true;
    public bool PlayOnStart = true;   
    private int currentFrame = 0;
    private int frameCount;
    public float delay = 0.1f;
    private SpriteRenderer spriteRenderer;
    private float lastAnimTime = 0;
    private SimpleAudio simpleAudio;
    private bool animationFinished = false;
    private bool audioFinished = false;
    private bool completed = false;
    private bool isPlaying = false;
    public event VoidEvent OnCompleted;
    // Start is called before the first frame update
    void Start()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        simpleAudio = GetComponent<SimpleAudio>();
        frameCount = frames.Length;
        if (simpleAudio != null)
        {
            simpleAudio.OnCompleted += AudioCompleted;
        }
        else // no audio
        {
            audioFinished = true;
        }

        if (PlayOnStart)
        {
            Play();
        }
    }

    public void Play()
    {
        currentFrame = 0;        
        spriteRenderer.sprite = frames[currentFrame];
        isPlaying = true;
        animationFinished = false;
        completed = false;
    }

    private void AudioCompleted(object sender)
    {
        audioFinished = true;
    }

    // Update is called once per frame
    void Update()
    {
        if (!animationFinished && isPlaying && Time.time - lastAnimTime > delay)
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
            isPlaying = false;
            OnCompleted?.Invoke(this);
            if (DestroyOnFinish)
            {
                Destroy(gameObject);
            }
            else
            {
                spriteRenderer.sprite = null;
            }
        }
    }
}
