using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Buffs;
using UnityEngine;
using UnityEngine.Experimental.Rendering.Universal;
using WorldEffects;

namespace DefaultNamespace.Units.UnitActions
{
    public class Skill
    {
        public string Name { get; set; }
        public List<Lazy<WorldEffect>> Effects { get; set; } = new List<Lazy<WorldEffect>>();
        public Lazy<TargetMode> TargetMode { get; set; }
        public bool IsMelee { get; set; }
        public List<Mod.RuleBuff> Buffs { get; set; }
        public int ChangeSpriteTo { get; set; }
        public bool IsOffensiveAction { get; set; }
    }
    public static class GameFactory
    {
        private static readonly Dictionary<string, Skill> skillMap = new Dictionary<string, Skill>();

        private static BattleGame Game { get; set; }
        private static Map Map { get; set; }

        public static Buff CreateBuffFromRules(Mod.RuleBuff buffRules)
        {
            BuffEndCondition condition;
            if (buffRules.Timer > 0)
            {
                condition = new TimedCondition(buffRules.Timer);
            }
            else if (buffRules.EndCondition != Mod.EndConditionPreset.None)
            {
                condition = EndConditionFromPreset(buffRules.EndCondition);
            }
            else
            {
                condition = new UnlimitedCondition();
            }
            
            MethodInfo createMethod = typeof(GameFactory).GetMethod("CreateBuff")
                ?.MakeGenericMethod(Type.GetType("Buffs." + buffRules.Name));
            var buffFromRules = createMethod.Invoke(null, new object[]{buffRules.Name, condition}) as Buff;
            
            foreach (var rulesStatModifier in buffRules.StatModifiers)
            {
                buffFromRules?.SetMaxStatModifier(rulesStatModifier.Name, rulesStatModifier.Value);
            }
            
            return buffFromRules;
        }

        private static BuffEndCondition EndConditionFromPreset(Mod.EndConditionPreset buffRulesEndCondition)
        {
            switch (buffRulesEndCondition)
            {
                case Mod.EndConditionPreset.OnFirstAttack:
                    return new AttackCondition(1);
            }
            return new UnlimitedCondition();
        }

        public static T CreateBuff<T>(string name, BuffEndCondition condition) where T : Buff, new()
        {
            T buff = new T()
            {
                EndCondition = condition
            };
            return buff;
        }
        
        public static T CreateUnitAction<T>(string identifier, Unit unit) where T : UnitAction, new()
        {
            var targetMode = skillMap[identifier].TargetMode.Value;
            targetMode.Initialize(Game, Map);
            T action = new T
            {
                ActingUnit = unit,
                Turn = Game.CurrentTurn,
                ActingPlayer = Game.CurrentPlayer,
                Identifier = identifier,
                Effects = skillMap[identifier].Effects,
                Buffs = skillMap[identifier].Buffs,
                TargetMode = targetMode,
                IsMelee = skillMap[identifier].IsMelee,
                IsOffensiveAction = skillMap[identifier].IsOffensiveAction,
                ChangeSpriteTo = skillMap[identifier].ChangeSpriteTo
            };
            action.Initialize(Game, Map);
            return action;
        }
        // Called via reflection by LoadSkillDefinitions
        public static T CreateWorldEffect<T>(Mod.RuleEffect effectRules) where T : WorldEffect, new()
        {
            T effect = new T
            {
                Identifier = nameof(T),
                StringOne = effectRules.StringOne,
                FloatOne = effectRules.FloatOne,
                IntegerOne = effectRules.IntegerOne,
                IntegerTwo = effectRules.IntegerTwo,
                Chained = effectRules.Chained,
            };
            effect.Initialize(Game, Map, Mod);
            return effect;
        }

        public static Mod Mod { get; set; }

        public static void Initialize(BattleGame battleGame, Map map, Mod mod)
        {
            Game = battleGame;
            Map = map;
            Mod = mod;
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
                    IsMelee = skillDef.IsMelee,
                    IsOffensiveAction = skillDef.IsOffensiveAction,
                    ChangeSpriteTo = skillDef.ChangeSpriteTo,
                    TargetMode = new Lazy<TargetMode>(() => new TargetMode(skillDef.TargetMode.Type)
                    {
                        AllowedTargetTypes = skillDef.TargetMode.AllowedTargetTypes,
                        AllowedTargetPreset = skillDef.TargetMode.AllowedTargetPreset,
                        ActiveAffectedPreset = skillDef.TargetMode.ActiveAffectedPreset,
                        AllowedTargets = skillDef.TargetMode.AllowedTargets.Select(i => new Vector3Int(i[0], i[1], 0)).ToList(),
                        AffectedTiles = skillDef.TargetMode.AffectedTiles.Select(i => new Vector3Int(i[0], i[1], 0)).ToList(),
                        AffectedRange = skillDef.TargetMode.AffectedRange,
                    }),
                    Buffs = skillDef.Buffs.Where(b =>
                    {
                        var buffType = Type.GetType("Buffs." + b.Name);
                        return buffType != null && (buffType.IsSubclassOf(typeof(Buff)) || buffType == typeof(Buff));
                    }).ToList(),
                    Effects = skillDef.Effects.Where(e =>
                    {
                        var effectType = Type.GetType("WorldEffects." + e.Name);
                        return effectType != null && effectType.IsSubclassOf(typeof(WorldEffect));
                    }).Select(effectRules =>
                    {
                        return new Lazy<WorldEffect>(() =>
                        {
                            MethodInfo createMethod = typeof(GameFactory).GetMethod("CreateWorldEffect")
                                ?.MakeGenericMethod(Type.GetType("WorldEffects." + effectRules.Name));
                            var worldEffect = createMethod.Invoke(null, new object[]{effectRules}) as WorldEffect;
                            return worldEffect;
                        });
                    }).ToList()
                });
            }
        }
    }
}