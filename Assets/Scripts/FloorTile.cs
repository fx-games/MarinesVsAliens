
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

public class FloorTile : Tile
{
    public Sprite[] m_Sprites;
    public Sprite m_Preview;
    public Color tileColor;
    
    
    public override void GetTileData(Vector3Int location, ITilemap tilemap, ref TileData tileData)
    {
        tileData.sprite = m_Sprites[0];
        tileData.color = color;
        tileData.flags = TileFlags.LockAll;
        tileData.colliderType = ColliderType.Grid;
    }

#if UNITY_EDITOR
// The following is a helper that adds a menu item to create a RoadTile Asset
    [MenuItem("Assets/Create/FloorTile")]
    public static void CreateFloorTile()
    {
        string path = EditorUtility.SaveFilePanelInProject("Save Floor Tile", "New Floor Tile", "Asset", "Save Floor Tile", "Assets");
        
        if (path == "")
            return;
        AssetDatabase.CreateAsset(CreateInstance<FloorTile>(), path);
    }
#endif
}
