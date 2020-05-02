using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using WorldEffects;

public class Projectile : MonoBehaviour
{
    public Vector3 target { get; set; }
    public float speed { get; set; } = 8;
    
    public bool Arcing { get; set; }
    public Sprite[] Sprites;
    public int SelectedSprite { get; set; }
    public BattleGame Game { get; set; }
    private AudioSource localAudioSource;
    public AudioClip[] AudioClips;
    private SpriteRenderer spriteRenderer;
    private bool targetHit = false;
    private float initialDistanceToTarget;
    public float VolumeSFX { get; set; } = 0.5f;

    public event VoidEvent OnArrivedAtTarget;

    protected void Start()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        spriteRenderer.sprite = Sprites[SelectedSprite];
        localAudioSource = GetComponent<AudioSource>();
        localAudioSource.volume = VolumeSFX;
        initialDistanceToTarget = (transform.position - target).magnitude;
        if (AudioClips.Length > 0)
        {
            var audioClip = AudioClips[GetLaunchClip(SelectedSprite)];
            localAudioSource.PlayOneShot(audioClip);
        }
    }

    
    private int GetLaunchClip(int selectedSprite)
    {
        if (selectedSprite == 4) return 1;
        return 0;
    }

    private int GetHitClip(int selectedSprite)
    {
        return 2;
    }
    private void Update()
    {
        // Moves the object to target position
        if (!targetHit)
        {
            var currentPosition = transform.position;
            var distanceToTarget = (currentPosition - target).magnitude;
            if (Arcing)
            {
                var completedPercent = 1.0f - (distanceToTarget / initialDistanceToTarget);
                var scaleFactor =
                    1.0f + ((0.5f - Mathf.Abs(0.5f - completedPercent)) *
                            4); // from 2.0 (right in the middle) to 1.0 (at the start or the end)            
                transform.localScale = Vector3.one * scaleFactor;
            }
            
            currentPosition = Vector3.MoveTowards(currentPosition, target, Time.deltaTime * speed);
            transform.position = currentPosition;

            if (distanceToTarget <= 0.1f)
            {
                HitTarget();
            }
        }
    }


    private void HitTarget()
    {
        OnArrivedAtTarget?.Invoke(this);
        
        targetHit = true;
        spriteRenderer.enabled = false;

        if (AudioClips.Length > 0)
        {
            localAudioSource.PlayOneShot(AudioClips[GetHitClip(SelectedSprite)]);
            StartCoroutine(WaitForSound());
        }
        else
        {
            Destroy(gameObject);
        }
    }

    public IEnumerator WaitForSound()
    {
        yield return new WaitUntil(() => localAudioSource.isPlaying == false);
        // or yield return new WaitWhile(() => audiosource.isPlaying == true);
        Destroy(gameObject);
    }

}
