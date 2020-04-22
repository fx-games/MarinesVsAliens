using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Projectile : MonoBehaviour
{
    public Vector3 target { get; set; }
    public float speed { get; set; } = 8;
    public Sprite[] Sprites;
    public int SelectedSprite { get; set; } = 4;
    public BattleGame Game { get; set; }

    private void Start()
    {
        GetComponent<SpriteRenderer>().sprite = Sprites[SelectedSprite];
    }

    private void Update()
    {
        // Moves the object to target position
        transform.position = Vector3.MoveTowards(transform.position, target, Time.deltaTime * speed);

        if ((transform.position - target).magnitude <= 0.1f)
        {
            Destroy(gameObject);
            Game.CreateExplosionOnTile(transform.position);
        }
    }
}
