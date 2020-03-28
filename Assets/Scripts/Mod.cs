using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using UnityEngine;
using YamlDotNet.RepresentationModel;
using YamlDotNet.Samples;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

public class Mod : MonoBehaviour
{
    public class Rules
    {
        public List<RuleFaction> Factions { get; set; }
        public List<RuleUnit> Units { get; set; }
        public List<RuleWeapon> Weapons { get; set; }
        public List<RuleMapBlock> MapBlocks { get; set; }
        public List<RuleMap> Maps { get; set; }
        public List<RuleSpawn> Spawns { get; set; }

    }
    
    public class RuleMapBlock
    {
        public string Type { get; set; }
        public int[] Size { get; set; } = {12,12};
        public List<int[]> Rooms { get; set; } = new List<int[]>();
        public List<int[]> HorizontalWalls { get; set; } = new List<int[]>();
        public List<int[]> VerticalWalls { get; set; } = new List<int[]>();
        public List<int[]> HorizontalDoors { get; set; } = new List<int[]>();
        public List<int[]> VerticalDoors { get; set; } = new List<int[]>();
    }
    
    public class RuleMap
    {
        public string Type { get; set; }
        public int[] Size { get; set; } = {12,12};
        public List<MapScriptCommand> MapScript { get; set; }
    }
    
    public class MapScriptCommand
    {
        public string Command { get; set; }
        public List<string> Blocks { get; set; } = new List<string>();
        public List<int[]> Rects { get; set; } = new List<int[]>();
    }
    
    public class RuleSpawn
    {
        public string Faction { get; set; }
        public List<int[]> Locations { get; set; }
        [DefaultValue(false)] public bool RandomPlacement { get; set; } = false;
    }

    public class RuleFaction
    {
        public string Type { get; set; }
        public int[] Color { get; set; }

        public List<RuleLoadout> LoadOut { get; set; }

        public class RuleLoadout
        {
            public string Unit { get; set; }
            [DefaultValue(1)] public int Count { get; set; } = 1;
            public List<string> Weapons { get; set; }
        }
    }
    public class RuleUnit
    {
        public string Type { get; set; }
        [DefaultValue(6)] public int Movement { get; set; } = 6;
        [DefaultValue(1)] public int Health { get; set; } = 1;
        [DefaultValue(0)] public int Armor { get; set; } = 0;
    }
    
    public class RuleWeapon
    {
        public string Type { get; set; }
        public int[] Damage { get; set; }
        [DefaultValue(0)] public int Radius { get; set; } = 0;
        [DefaultValue(false)] public bool IsMelee { get; set; } = false;
        [DefaultValue(false)] public bool StraightLineOnly { get; set; } = false;
        [DefaultValue(false)] public bool DamageLineOfFire { get; set; } = false;
    }
    private Dictionary<string, RuleFaction> factionRules = new Dictionary<string, RuleFaction>();
    private Dictionary<string, RuleUnit> unitRules = new Dictionary<string, RuleUnit>();
    private Dictionary<string, RuleWeapon> weaponRules = new Dictionary<string, RuleWeapon>();
    private Dictionary<string, RuleSpawn> spawnRules = new Dictionary<string, RuleSpawn>();
    private Dictionary<string, RuleMap> mapRules = new Dictionary<string, RuleMap>();
    private Dictionary<string, RuleMapBlock> mapBlockRules = new Dictionary<string, RuleMapBlock>();

    // Start is called before the first frame update
    void Awake()
    {
        ReadRules();
    }

    string ReadTextFile(string filePath)
    {
        StreamReader streamReader = new StreamReader(filePath);
        string output = streamReader.ReadToEnd();
        streamReader.Close();
        return output;
    }
    private void ReadRules()
    {
        var unitsFile = Path.Combine(Application.streamingAssetsPath, "core.rul");
        var input = new StringReader(ReadTextFile(unitsFile));
        //var yaml = new YamlStream();
        //yaml.Load(input);
        var deserializerBuilder = new DeserializerBuilder().IgnoreUnmatchedProperties().WithNamingConvention(new CamelCaseNamingConvention());
        var deserializer = deserializerBuilder.Build();
        var rules = deserializer.Deserialize<Rules>(input);
        
        //Debug.Log("Rules: " + rules.Factions.Count + "/" + rules.Units.Count + "/" + rules.Weapons.Count + "/" + rules.Spawns.Count);
        foreach (var ruleFaction in rules.Factions)
        {
            factionRules[ruleFaction.Type] = ruleFaction;
        }
        foreach (var ruleUnit in rules.Units)
        {
            unitRules[ruleUnit.Type] = ruleUnit;
        }
        foreach (var ruleWeapon in rules.Weapons)
        {
            weaponRules[ruleWeapon.Type] = ruleWeapon;
        }
        
        foreach (var ruleSpawn in rules.Spawns)
        {
            spawnRules[ruleSpawn.Faction] = ruleSpawn;
        }
        
        
        foreach (var ruleMap in rules.Maps)
        {
            mapRules[ruleMap.Type] = ruleMap;
        }
        
        
        foreach (var ruleMapBlock in rules.MapBlocks)
        {
            mapBlockRules[ruleMapBlock.Type] = ruleMapBlock;
        }
    }

    public RuleUnit GetUnitRule(string type)
    {
        return unitRules[type];
    }
    
    public RuleFaction GetFactionRule(string type)
    {
        return factionRules[type];
    }
    
    public RuleWeapon GetWeaponRule(string type)
    {
        return weaponRules[type];
    }

    public RuleSpawn GetSpawnRule(string faction)
    {
        return spawnRules[faction];
    }

    public List<RuleFaction> GetFactions()
    {
        return factionRules.Values.ToList();
    }
    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public RuleMap GetMap(string mapName)
    {
        return mapRules[mapName];
    }
    
    public RuleMapBlock GetMapBlock(string mapBlockName)
    {
        return mapBlockRules[mapBlockName];
    }

    public RuleMap GetMap(int index)
    {
        return mapRules.Values.ElementAt(index);
    }
}
