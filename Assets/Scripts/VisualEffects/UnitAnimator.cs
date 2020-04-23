using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum UnitSpriteOffset
{
    NoWeapon = 0,
    Rifle = 2,
    Shotgun = 4,
    Pistol = 6,
    Sword = 8,
    RocketLauncher = 10,
    FlameThrower = 12,
    HeavyRifle = 14,
    /* BELOW HERE IS EXTENDED, eg. Bird people don't have these sprites */
    PistolAndShield = 16,
    RedFlagAndPistol = 18,
    BlueFlagAndPistol = 20,
    BlueLaserSword = 22,
    RedLaserSword = 24,
    BlueGhost = 26,
    GrayGhost = 28,
    RedGhost = 30
}

public enum DroneSpriteOffset
{
    CupGrabber = 0,
    ShoddyCupGrabber = 2,
    SphereGrabber = 4,
    NeutralSpikes = 6,
    AggressiveSpikes = 8,
    SpheroidRedLens = 10,
    SpheroidYellowLens = 12,
    SmallCube = 14,
    LargeCube = 16
}
public class UnitAnimator : MonoBehaviour
{
    public Sprite[] idleFrames;
    private int currentFrame = 0;
    private int idleFrameCount = 2;
    private float delay = 0.5f;
    private SpriteRenderer spriteRenderer;
    private float lastAnimTime = 0;
    private bool idleAnimationHasStarted = false;
    private int spriteOffset = 0;
    public int SpriteOffset => spriteOffset;

    // Start is called before the first frame update
    void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        spriteRenderer.sprite = idleFrames[spriteOffset + currentFrame];
        delay += Random.Range(-0.1f, 0.1f);
        Invoke(nameof(StartIdleAnimation), Random.value * 2);
    }

    public void SetSpriteOffset(UnitSpriteOffset sprite)
    {
        SetSpriteOffset((int) sprite);
    }
    
    public void SetSpriteOffset(DroneSpriteOffset sprite)
    {
        SetSpriteOffset((int) sprite);
    }
    
    public void SetSpriteOffset(int sprite)
    {
        spriteOffset = sprite;
        spriteRenderer.sprite = idleFrames[spriteOffset + currentFrame];
    }

    void StartIdleAnimation()
    {
        idleAnimationHasStarted = true;
    }

    // Update is called once per frame
    void Update()
    {
        if (!idleAnimationHasStarted) return;
        if (Time.time - lastAnimTime > delay)
        {
            currentFrame++;
            if (currentFrame >= idleFrameCount)
            {
                currentFrame = 0;
            }

            spriteRenderer.sprite = idleFrames[spriteOffset + currentFrame];
            lastAnimTime = Time.time;
        }
    }
}
