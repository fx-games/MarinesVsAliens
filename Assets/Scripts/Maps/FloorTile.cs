
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

public class FloorTile : Tile
{
    public Sprite[] m_Sprites;

    public override void GetTileData(Vector3Int location, ITilemap tilemap, ref TileData tileData)
    {
        tileData.sprite = m_Sprites[0];
        tileData.flags = TileFlags.None;
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
