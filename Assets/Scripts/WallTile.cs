using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

public class WallTile : Tile
{
    public Sprite[] m_Sprites;
    public Sprite m_Preview;
    public override void RefreshTile(Vector3Int location, ITilemap tilemap)
    {
        for (int yd = -1; yd <= 1; yd++)
        for (int xd = -1; xd <= 1; xd++)
        {
            Vector3Int position = new Vector3Int(location.x + xd, location.y + yd, location.z);
            if (HasWallTile(tilemap, position))
                tilemap.RefreshTile(position);
        }
    }
    
    public override void GetTileData(Vector3Int location, ITilemap tilemap, ref TileData tileData)
    {
        int mask = HasWallTile(tilemap, location + new Vector3Int(0, 1, 0)) ? 1 : 0;
           mask += HasWallTile(tilemap, location + new Vector3Int(1, 0, 0)) ? 2 : 0;
          mask += HasWallTile(tilemap, location + new Vector3Int(0, -1, 0)) ? 4 : 0;
          mask += HasWallTile(tilemap, location + new Vector3Int(-1, 0, 0)) ? 8 : 0;
        int index = GetIndex((byte)mask);
        if (index >= 0 && index < m_Sprites.Length)
        {
            tileData.sprite = m_Sprites[index];
            tileData.color = Color.white;
            tileData.flags = TileFlags.LockTransform;
            tileData.colliderType = ColliderType.Grid;
        }
        else
        {
        Debug.LogWarning("Not enough sprites in WallTile instance");
}
    }
    // This determines if the Tile at the position is the same RoadTile.
    private bool HasWallTile(ITilemap tilemap, Vector3Int position)
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
