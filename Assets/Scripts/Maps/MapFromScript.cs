using System.Collections;
using UnityEngine;

namespace DefaultNamespace
{
    public class FromScript : GeneratorBase
    {
        private Mod.RuleMap mapRules;
        private Mod mod;

        public FromScript(Mod mod, Mod.RuleMap mapRules)
        {
            this.mod = mod;
            this.mapRules = mapRules;
        }

        public override IEnumerator Generate()
        {
            map.Init(mapRules.Size[0], mapRules.Size[1]);
            
            foreach (var mapScriptCommand in mapRules.MapScript)
            {
                if (mapScriptCommand.Command.ToLower().Equals("placeblock"))
                {
                    foreach (var blockPlacementPosition in mapScriptCommand.Rects)
                    {
                        var blockName = mapScriptCommand.Blocks[Random.Range(0, mapScriptCommand.Blocks.Count)];
                        PlaceMapBlock(blockPlacementPosition, blockName);
                    } 
                }
            }
            yield break;
        }

        public override void Step()
        {
            throw new System.NotImplementedException();
        }

        private void PlaceMapBlock(int[] offset, string blockName)
        {
            var block = mod.GetMapBlock(blockName);
            foreach (var room in block.Rooms)
            {
                CreateRoom(room[0] + offset[0], room[1] + offset[1], room[2] + offset[0], room[3] + offset[1]);
            }
            foreach (var wall in block.HorizontalWalls)
            {
                CreateWalls(wall[0] + offset[0], wall[1] + offset[1], wall[2] + offset[0], wall[3] + offset[1]);
            }
            foreach (var wall in block.VerticalWalls)
            {
                CreateWalls(wall[0] + offset[0], wall[1] + offset[1], wall[2] + offset[0], wall[3] + offset[1]);
            }
            foreach (var door in block.HorizontalDoors)
            {
                map.CreateDoor(door[0] + offset[0], door[1] + offset[1]);
            }
            foreach (var door in block.VerticalDoors)
            {
                map.CreateDoor(door[0] + offset[0], door[1] + offset[1]);
            }
        }
        
        private void CreateRoom(int x0, int y0, int x1, int y1)
        {
            CreateWalls(x0, y0 - 1, x1, y0); // southern walls        
            CreateWalls(x0, y1, x1, y1 + 1); // northern walls
            CreateWalls(x0 - 1, y0, x0, y1); // western walls
            CreateWalls(x1, y0, x1 + 1, y1); // eastern walls
        }
        
        protected virtual void CreateWalls(int x0, int y0, int x1, int y1)
        {
            for (int xCount = x0; xCount <= x1; xCount++)
            {
                for (int yCount = y0; yCount <= y1; yCount++)
                {
                    int x = xCount;
                    int y = yCount;
                
                    if (!map.Contains(x,y))
                        continue;
                    var currentPos = new Vector3Int(x,y,0);

                    map.AddWall(currentPos);
                }
            }
        }
    }
}