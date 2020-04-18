using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using WorldEffects;

namespace DefaultNamespace.Units.UnitActions
{
    public class Skill
    {
        public string Name { get; set; }
        public List<Lazy<WorldEffect>> Effects { get; set; } = new List<Lazy<WorldEffect>>();
        public Lazy<TargetMode> TargetMode { get; set; }
    }
    public static class GameFactory
    {
        //MethodInfo test = typeof(GameFactory).GetMethod("CreateWorldEffect").MakeGenericMethod("OpenDoorEffect");
        private static Dictionary<string, Skill> skillMap = new Dictionary<string, Skill>()
        {
            {"OpenDoor", new Skill()
            {
                Name = "OpenDoor",
                TargetMode = new Lazy<TargetMode>(() => new TargetMode(TargetModeType.Self)),
                Effects = new List<Lazy<WorldEffect>>()
                {
                    new Lazy<WorldEffect>(CreateWorldEffect<OpenDoorEffect>)
                }
            }},
            {"MeleeAttack", new Skill()
            {
                Name = "MeleeAttack",
                TargetMode = new Lazy<TargetMode>(() => new TargetMode(TargetModeType.Single)
                {
                    AffectedTiles = new List<Vector3Int>(){Vector3Int.zero},
                    AllowedTargetTypes = TargetType.Enemies | TargetType.Friendlies,
                    AllowedTargetPreset = TargetPreset.CardinalMelee
                }),
                Effects = new List<Lazy<WorldEffect>>()
                {
                    new Lazy<WorldEffect>(CreateWorldEffect<Attack>)
                }
            }},
            {"RangedAttack", new Skill()
            {
                Name = "RangedAttack",
                TargetMode = new Lazy<TargetMode>(() => new TargetMode(TargetModeType.Single)
                {
                    AffectedTiles = new List<Vector3Int>(){Vector3Int.zero},
                    AllowedTargetTypes = TargetType.Enemies | TargetType.Friendlies,
                    AllowedTargetPreset = TargetPreset.RangedAttack
                }),
                Effects = new List<Lazy<WorldEffect>>()
                {
                    new Lazy<WorldEffect>(CreateWorldEffect<Attack>)
                }
            }},
            {"RocketLauncher", new Skill()
            {
                Name = "RocketLauncher",
                TargetMode = new Lazy<TargetMode>(() => new TargetMode(TargetModeType.Single)
                {
                    ActiveAffectedPreset = AffectedPreset.Explosion,
                    AllowedTargetTypes = TargetType.Floor | TargetType.Enemies | TargetType.Friendlies,
                    AllowedTargetPreset = TargetPreset.RangedAttack,
                    AffectedRange = 1 // radius
                }),
                Effects = new List<Lazy<WorldEffect>>()
                {
                    new Lazy<WorldEffect>(CreateWorldEffect<Explosion>)
                }
            }},
        };

        private static BattleGame Game { get; set; }
        private static Map Map { get; set; }

        public static T CreateUnitAction<T>(string identifier, Unit unit) where T : UnitAction, new()
        {
            var targetMode = skillMap[identifier].TargetMode.Value;
            targetMode.Initialize(Game, Map);
            T action = new T
            {
                ActingUnit = unit,
                Turn = Game.CurrentTurn,
                ActingFaction = Game.CurrentFaction,
                Identifier = identifier,
                EffectsOnTheWorld = skillMap[identifier].Effects,
                TargetMode = targetMode
            };
            action.Initialize(Game, Map);
            return action;
        }
        
        public static T CreateWorldEffect<T>() where T : WorldEffect, new()
        {
            T effect = new T
            {
                Identifier = nameof(T)
            };
            effect.Initialize(Game, Map);
            return effect;
        }

        public static void Initialize(BattleGame battleGame, Map map, Mod mod)
        {
            Game = battleGame;
            Map = map;
            LoadSkillDefinitions(mod);
        }

        private static void LoadSkillDefinitions(Mod mod)
        {
            skillMap.Clear();
            foreach (var skillDef in mod.GetSkills())
            {
                skillMap.Add(skillDef.Name, new Skill()
                {
                    Name = skillDef.Name,
                    TargetMode = new Lazy<TargetMode>(() => new TargetMode(skillDef.TargetMode.Type)
                    {
                        AllowedTargetTypes = skillDef.TargetMode.AllowedTargetTypes,
                        AllowedTargetPreset = skillDef.TargetMode.AllowedTargetPreset,
                        ActiveAffectedPreset = skillDef.TargetMode.ActiveAffectedPreset,
                        AllowedTargets = skillDef.TargetMode.AllowedTargets.Select(i => new Vector3Int(i[0], i[1], 0)).ToList(),
                        AffectedTiles = skillDef.TargetMode.AffectedTiles.Select(i => new Vector3Int(i[0], i[1], 0)).ToList(),
                        AffectedRange = skillDef.TargetMode.AffectedRange,
                    }),
                    Effects = skillDef.Effects.Where(e =>
                    {
                        var effectType = Type.GetType("WorldEffects." + e);
                        return effectType != null && effectType.IsSubclassOf(typeof(WorldEffect));
                    }).Select(effectName =>
                    {
                        return new Lazy<WorldEffect>(() =>
                        {
                            MethodInfo createMethod = typeof(GameFactory).GetMethod("CreateWorldEffect")
                                ?.MakeGenericMethod(Type.GetType("WorldEffects." + effectName));
                            return createMethod.Invoke(null, null) as WorldEffect;
                        });
                    }).ToList()
                });
            }
        }
    }
}