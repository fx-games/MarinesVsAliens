using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using WorldEffects;

public class Projectile : MonoBehaviour
{
    public Vector3 target { get; set; }
    public float speed { get; set; } = 8;
    public Sprite[] Sprites;
    public int SelectedSprite { get; set; }
    public BattleGame Game { get; set; }
    private AudioSource localAudioSource;
    public AudioClip[] AudioClips;
    private SpriteRenderer spriteRenderer;
    private bool targetHit = false;
    public float VolumeSFX { get; set; } = 0.5f;

    public event VoidEvent OnArrivedAtTarget;
    private void Start()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        spriteRenderer.sprite = Sprites[SelectedSprite];
        localAudioSource = GetComponent<AudioSource>();
        localAudioSource.volume = VolumeSFX;
        localAudioSource.PlayOneShot(AudioClips[GetLaunchClip(SelectedSprite)]);
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
            transform.position = Vector3.MoveTowards(transform.position, target, Time.deltaTime * speed);
        }

        if ((transform.position - target).magnitude <= 0.1f && !targetHit)
        {
            HitTarget();
        }
    }

    private void HitTarget()
    {
        OnArrivedAtTarget?.Invoke(this);
        
        targetHit = true;
        spriteRenderer.enabled = false;

        localAudioSource.PlayOneShot(AudioClips[GetHitClip(SelectedSprite)]);

        StartCoroutine(WaitForSound());
    }

    public IEnumerator WaitForSound()
    {
        yield return new WaitUntil(() => localAudioSource.isPlaying == false);
        // or yield return new WaitWhile(() => audiosource.isPlaying == true);
        Destroy(gameObject);
    }

}
