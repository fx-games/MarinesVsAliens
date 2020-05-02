using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using DefaultNamespace.Units.UnitActions;
using UnityEngine;
using UnityEngine.Networking;
using YamlDotNet.RepresentationModel;
using YamlDotNet.Samples;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

public class Mod : MonoBehaviour
{
    public enum EndConditionPreset
    {
        None,
        OnFirstAttack
    }
    public class Rules
    {
        public List<RuleFaction> Factions { get; set; }
        public List<RuleUnit> Units { get; set; }
        public List<RuleWeapon> Weapons { get; set; }
        public List<RuleMapBlock> MapBlocks { get; set; }
        public List<RuleMap> Maps { get; set; }
        public List<RuleSpawn> Spawns { get; set; }        
        public List<RuleSkill> Skills { get; set; }
        public List<Translation> Translations { get; set; }        
    }
    
    public class Translation
    {
        public string Lang { get; set; }
        public Dictionary<string, string> Strings { get; set; }
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
        public string Premade { get; set; }
        public int[] Size { get; set; } = {12,12};
        public int Generator { get; set; } = 0;
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
        public Mod Mod { get; set; }

        public string Type { get; set; }
        public int[] Color { get; set; }
        public int FactionPrefab { get; set; } = 0;
        public List<RuleLoadout> LoadOut { get; set; }

        public class RuleLoadout
        {
            public string Unit { get; set; }
            [DefaultValue(1)] public int Count { get; set; } = 1;
            public List<string> Weapons { get; set; }
            public bool DoNotSpawn { get; set; }
        }

        public override string ToString()
        {
            return Mod.Tr(Type);
        }
    }
    
    public class RuleUnit
    {
        public string Type { get; set; }
        //public List<NameIntValuePair> Stats { get; set; } = new List<NameIntValuePair>();
        public Dictionary<string, int> Stats { get; set; }
        public List<int[]> OccupiedTiles { get; set; } = new List<int[]> {new []{0,0}};
        public bool CanMoveDiagonally { get; set; } = true;
        public int FactionPrefab { get; set; } = -1;
        public int SpriteOffset { get; set; } = 0;
        public List<string> UnitSkills { get; set; } = new List<string>();
    }
    
    public class RuleSkill
    {
        public string Name { get; set; }
        public List<RuleEffect> Effects { get; set; } = new List<RuleEffect>();
        public List<RuleBuff> Buffs { get; set; } = new List<RuleBuff>();
        public RuleTargeting TargetMode { get; set; }
        public string DisplayName { get; set; }
        public bool IsMelee { get; set; }
        public int ChangeSpriteTo { get; set; } = -1;
        public bool IsOffensiveAction { get; set; }
    }
    public class RuleEffect
    {
        public string Name { get; set; }
        public string StringOne { get; set; }
        public int IntegerOne { get; set; }
        public int IntegerTwo { get; set; }
        public float FloatOne { get; set; }
        public bool Chained { get; set; }
        public bool BoolOne { get; set; }
    }
    public class RuleBuff
    {
        public string Name { get; set; }
        public int Timer { get; set; }

        public List<NameIntValuePair> StatModifiers { get; set; } = new List<NameIntValuePair>();
        public EndConditionPreset EndCondition { get; set; }
    }

    public class NameIntValuePair
    {
        public string Name { get; set; }
        public int Value { get; set; }
    }

    public class RuleTargeting
    {
        public TargetModeType Type { get; set; }
        public TargetType AllowedTargetTypes { get; set; }
        public TargetPreset AllowedTargetPreset { get; set; }
        public AffectedPreset ActiveAffectedPreset { get; set; }
        public List<int[]> AllowedTargets { get; set; } = new List<int[]>();
        public List<int[]> AffectedTiles { get; set; } = new List<int[]>();
        public int AffectedRange { get; set; }
    }

    public class RuleWeapon
    {
        public int UseCost { get; set; }
        public string Type { get; set; }
        public List<int[]> Damage { get; set; }
        public int MinRange { get; set; } = 1;
        public int MaxRange { get; set; } = 20;
        public int MinDropOff { get; set; } = 5;
        public int MaxDropOff { get; set; } = 5;
        public int EffectiveRange { get; set; } = 40;
        public int AccuracyModifier { get; set; } = 0;
        [DefaultValue(0)] public int Radius { get; set; } = 0;
        [DefaultValue(false)] public bool IsMelee { get; set; } = false;
        [DefaultValue(false)] public bool StraightLineOnly { get; set; } = false;
        [DefaultValue(false)] public bool DamageLineOfFire { get; set; } = false;
        [DefaultValue(false)] public bool DamageDistribution { get; set; } = false;
        public bool MultipleTargets { get; set; } = false;        
    }
    private Dictionary<string, RuleFaction> factionRules = new Dictionary<string, RuleFaction>();
    private Dictionary<string, RuleUnit> unitRules = new Dictionary<string, RuleUnit>();
    private Dictionary<string, RuleWeapon> weaponRules = new Dictionary<string, RuleWeapon>();
    private Dictionary<string, RuleMap> mapRules = new Dictionary<string, RuleMap>();
    private Dictionary<string, RuleMapBlock> mapBlockRules = new Dictionary<string, RuleMapBlock>();
    private Dictionary<string, Translation> translations = new Dictionary<string, Translation>();
    private Dictionary<string, RuleSkill> skillRules = new Dictionary<string, RuleSkill>();

    // Start is called before the first frame update
    void Awake()
    {
        ReadRules();
    }
#if UNITY_WEBGL
    IEnumerator LoadFromWeb(string fileUrl) 
    {
        UnityWebRequest www = UnityWebRequest.Get(fileUrl);
        yield return www.SendWebRequest();
 
        if(www.isNetworkError || www.isHttpError) 
        {
            Debug.Log(www.error);
        }
        else 
        {
            OnRuleFileLoaded(www.downloadHandler.text);
        }
    }
    void ReadTextFile(string fileUrl)
    {
        StartCoroutine(LoadFromWeb(fileUrl));
    }
#else
    void ReadTextFile(string filePath)
    {
        StreamReader streamReader = new StreamReader(filePath);
        string output = streamReader.ReadToEnd();
        streamReader.Close();
        OnRuleFileLoaded(output);
    }
#endif

    private void OnRuleFileLoaded(string ruleText)
    {
        var input = new StringReader(ruleText);
        //var yaml = new YamlStream();
        //yaml.Load(input);
        var deserializerBuilder = new DeserializerBuilder().IgnoreUnmatchedProperties().WithNamingConvention(new CamelCaseNamingConvention());
        var deserializer = deserializerBuilder.Build();
        var rules = deserializer.Deserialize<Rules>(input);
        
        //Debug.Log("Rules: " + rules.Factions.Count + "/" + rules.Units.Count + "/" + rules.Weapons.Count + "/" + rules.Spawns.Count);
        foreach (var ruleFaction in rules.Factions)
        {
            ruleFaction.Mod = this;
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
        foreach (var ruleSkill in rules.Skills)
        {
            skillRules[ruleSkill.Name] = ruleSkill;
        }
        foreach (var ruleMap in rules.Maps)
        {
            mapRules[ruleMap.Type] = ruleMap;
        }
        foreach (var ruleMapBlock in rules.MapBlocks)
        {
            mapBlockRules[ruleMapBlock.Type] = ruleMapBlock;
        }
        foreach (var translation in rules.Translations)
        {
            translations[translation.Lang] = translation;
        }
    }
    private void ReadRules()
    {
        var unitsFile = Path.Combine(Application.streamingAssetsPath, "percent.rul");
        ReadTextFile(unitsFile);
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

    public IEnumerable<RuleSkill> GetSkills()
    {
        return skillRules.Values;
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

    public string Tr(string textKey)
    {
        return translations["en-US"].Strings.ContainsKey(textKey)
            ? translations["en-US"].Strings[textKey]
            : textKey;
    }

    public RuleSkill GetSkill(string skillName)
    {
        return skillRules[skillName];
    }

    public RuleFaction.RuleLoadout GetLoadout(RuleFaction faction, string unitType)
    {
        foreach (var loadout in factionRules[faction.Type].LoadOut)
        {
            if (loadout.Unit == unitType)
            {
                return loadout;
            }
        }
        throw new KeyNotFoundException("UnitType "+unitType+" has no loadout.");
    } 
}
