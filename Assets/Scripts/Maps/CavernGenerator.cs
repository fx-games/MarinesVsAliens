using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static Map;
using Random = UnityEngine.Random;

namespace DefaultNamespace
{
    //http://www.roguebasin.com/index.php?title=Cellular_Automata_Method_for_Generating_Random_Cave-Like_Levels
    public class CavernGenerator : IMapGenerator
    {
        private Map map;
        public int PercentWalls { get; set; } = 40;
        private List<RectInt> genRooms = new List<RectInt>();

        public CavernGenerator(int height, int width)
        {
            Height = height;
            Width = width;
        }

        public int Height { get; set; }
        public int Width { get; set; }
        
        public void Generate(Map map)
        {
            this.map = map;
            Generator(Width, Height);
        }

        private void Generator(int width, int height)
        {
            if (width % 2 == 0 || height % 2 == 0)
            {
                throw new ArgumentException("Size must be odd.");
            }
            map.Init(width, height);

            RandomFillMap();
            //MakeCaverns();
        }

        public void MakeCaverns(int stage)
        {
            if (stage == 0)
            {
                foreach (var pos in RectToList(0, 0, map.Width, map.Height))
                {
                    PlaceWallLogic(pos);
                }
            }
            else
            {
                foreach (var pos in RectToList(0, 0, map.Width, map.Height))
                {
                    PlaceWallLogic(pos, false);
                }
            }
            
            
        }

        private void PlaceWallLogic(Vector3Int pos, bool extended = true)
        {
            int numWallsOne = GetAdjacentWallCount(pos, 1);
            int numWallsTwo = GetAdjacentWallCount(pos, 2);
            //Debug.Log(numWalls);
            if (extended)
            {
                if (numWallsOne >= 5 || numWallsTwo <= 3)
                {
                    map.AddWall(pos);
                    return;
                }
            }
            else
            {
                if (numWallsOne >= 5)
                {
                    map.AddWall(pos);
                    return;
                }
            }
            
            map.RemoveWallAt(pos);
        }

        private int GetAdjacentWallCount(Vector3Int pos, int radius)
        {
            int walls = 0;
            foreach (var rectPos in RectToList(pos.x - radius, pos.y - radius, 2*radius, 2*radius))
            {
                if (map.IsWallAt(rectPos) || !map.Contains(rectPos.x, rectPos.y))
                    walls++;
            }

            return walls;
        }

        private void RandomFillMap()
        {
            var mapMiddle = map.Height / 2;
            foreach (var pos in RectToList(0,0, map.Width, map.Height))
            {
                if (pos.x == 0 || pos.y == 0 || pos.x == map.Width - 1 || pos.y == map.Height - 1)
                {
                    map.AddWall(pos);
                }
                else
                {
                    var random = Random.Range(0, 99);
                    if (pos.x != mapMiddle && random < PercentWalls)
                    {
                        map.AddWall(pos);
                    }
                    else
                    {
                        map.RemoveWallAt(pos);
                    }
                }
            }
        }
    }

   
}