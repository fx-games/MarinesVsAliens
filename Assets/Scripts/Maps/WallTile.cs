using System.Collections;
using System.Collections.Generic;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.Tilemaps;

public class WallTile : Tile
{
    public Sprite[] m_Sprites;
    public Sprite[] m_DamagedSprites;
    public Sprite[] m_DestroyedSprites;
    public Sprite[] NorthWallAlternatives;
    public int NorthWallAlternativeChance;
    public Map Map { get; set; }

    public override void RefreshTile(Vector3Int location, ITilemap tilemap)
    {
        for (int yd = -1; yd <= 1; yd++)
        for (int xd = -1; xd <= 1; xd++)
        {
            Vector3Int position = new Vector3Int(location.x + xd, location.y + yd, location.z);
            if (HasWallTile(tilemap, position))
            {
                tilemap.RefreshTile(position);
            }
        }
    }
    
    public override void GetTileData(Vector3Int location, ITilemap tilemap, ref TileData tileData)
    {
        if (!Map.Contains(location.x, location.y)) return;
        var mask = GetNeighborMask(location, tilemap);
        int index = GetIndex((byte)mask);
        
        if (index >= 0 && index < m_Sprites.Length)
        {    
            tileData.color = Color.white;
            tileData.flags = TileFlags.LockTransform;
            tileData.colliderType = ColliderType.Grid;
            if (IsDestroyed(location))
            {
                tileData.sprite = m_DestroyedSprites[Random.Range(0, m_DestroyedSprites.Length)];
            }
            else if (IsDamaged(location) && (index == 2 || index == 5))
            {
                var damagedIndex = GetDamagedIndex(location, index, tilemap);
                if (damagedIndex < 0 || damagedIndex >= m_DamagedSprites.Length)
                {
                    tileData.sprite = m_Sprites[index];
                }
                else
                {
                    tileData.sprite = m_DamagedSprites[damagedIndex];
                }
            }
            else if (index == 2 && Random.Range(0,100) < NorthWallAlternativeChance) // northwall
            {
                tileData.sprite = NorthWallAlternatives[Random.Range(0, NorthWallAlternatives.Length)];
            }
            else
            {
                tileData.sprite = m_Sprites[index];
            }
        }
        else
        {
            Debug.LogWarning("Not enough sprites in WallTile instance");
        }
    }

    private bool IsDestroyed(Vector3Int location)
    {
        return Map.GetDamageFromTile(location.x, location.y) > 50;
    }
    
    private bool IsDamaged(Vector3Int location)
    {
        return Map.GetDamageFromTile(location.x, location.y) > 10;
    }

    private int GetNeighborMask(Vector3Int location, ITilemap tilemap)
    {
        int mask = HasWallTile(tilemap, location + new Vector3Int(0, 1, 0)) ? 1 : 0;
        mask += HasWallTile(tilemap, location + new Vector3Int(1, 0, 0)) ? 2 : 0;
        mask += HasWallTile(tilemap, location + new Vector3Int(0, -1, 0)) ? 4 : 0;
        mask += HasWallTile(tilemap, location + new Vector3Int(-1, 0, 0)) ? 8 : 0;
        return mask;
    }

    private int GetNeighborMask(Vector3Int location, Tilemap tilemap)
    {
        int mask = HasWallTile(tilemap, location + new Vector3Int(0, 1, 0)) ? 1 : 0;
        mask += HasWallTile(tilemap, location + new Vector3Int(1, 0, 0)) ? 2 : 0;
        mask += HasWallTile(tilemap, location + new Vector3Int(0, -1, 0)) ? 4 : 0;
        mask += HasWallTile(tilemap, location + new Vector3Int(-1, 0, 0)) ? 8 : 0;
        return mask;
    }
    private int GetDamagedIndex(Vector3Int location, int index, ITilemap tilemap)
    {
        if (index == 2) // horizontal
        {
            var isLeftWallDestroyed = HasWallDamagedSprite(tilemap,     location + Vector3Int.left);
            var isRightWallDestroyed = HasWallDamagedSprite(tilemap,     location + Vector3Int.right);
            // 0 = both sides intact
            // 1 = left side intact
            // 2 = both sides destroyed
            // 3 = right side intact
            if (isLeftWallDestroyed && isRightWallDestroyed)
            {
                return 2;
            }

            if (!isLeftWallDestroyed && !isRightWallDestroyed)
            {
                return 0;
            }

            if (isLeftWallDestroyed && !isRightWallDestroyed)
            {
                return 3;
            }
            
            if (!isLeftWallDestroyed && isRightWallDestroyed)
            {
                return 1;
            }
            
        }
        else if (index == 5) // vertical
        {
            var isUpWallDestroyed = HasWallDamagedSprite(tilemap,     location + Vector3Int.up);
            var isDownWallDestroyed = HasWallDamagedSprite(tilemap,     location + Vector3Int.down);
            if (isUpWallDestroyed && isDownWallDestroyed)
            {
                return 6;
            }

            if (!isUpWallDestroyed && !isDownWallDestroyed)
            {
                return 4;
            }

            if (isUpWallDestroyed && !isDownWallDestroyed)
            {
                return 7;
            }
            
            if (!isUpWallDestroyed && isDownWallDestroyed)
            {
                return 5;
            }
        }

        return -1;
    }

    public bool HasWallDamagedSprite(ITilemap tilemap, Vector3Int location)
    {
        var mask = GetNeighborMask(location, tilemap);
        int index = GetIndex((byte)mask);
        
        return (index == 2 || index == 5) && IsDamaged(location);
    }
    
    public bool HasWallDamagedSprite(Tilemap tilemap, Vector3Int location)
    {
        var mask = GetNeighborMask(location, tilemap);
        int index = GetIndex((byte)mask);
        
        return (index == 2 || index == 5) && IsDamaged(location);
    }

    // This determines if the Tile at the position is the same RoadTile.
    private bool HasWallTile(ITilemap tilemap, Vector3Int position)
    {
        return tilemap.GetTile(position) == this;
    }
    private bool HasWallTile(Tilemap tilemap, Vector3Int position)
    {
        return tilemap.GetTile(position) == this;
    }
    // The following determines which sprite to use based on the number of adjacent RoadTiles
    private int GetIndex(byte mask)
    {
        switch (mask)
        {
            case 0: return 0; // single
            case 2: return 1; // to the right
            case 10: return 2; // left and right
            case 8: return 3; // to the left
            case 4: return 4; // below
            case 5: return 5; // below and above
            case 1: return 6; // above
            case 6: return 7; // down and right
            case 12: return 8; // down and left
            case 3: return 9; // up and right
            case 9: return 10; // up and left
            case 15: return 11; // 4 way
            case 14: return 12; // all except up
            case 13: return 13; // all except right
            case 7: return 14; // all except left
            case 11: return 15; // all except down
        }
        return -1;
    }    

#if UNITY_EDITOR
// The following is a helper that adds a menu item to create a RoadTile Asset
    [MenuItem("Assets/Create/WallTile")]
    public static void CreateWallTile()
    {
        string path = EditorUtility.SaveFilePanelInProject("Save Wall Tile", "New Wall Tile", "Asset", "Save Wall Tile", "Assets");
        if (path == "")
            return;
    AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<WallTile>(), path);
    }
#endif
}
