using System;
using System.Collections.Generic;
using System.Linq;
using Buffs;
using UnityEngine;
using WorldEffects;
using static Unit.UnitState;

namespace DefaultNamespace.Units.UnitActions
{
    public class UnitAction
    {
        private Map _map;
        private BattleGame _game;
        
        private HashSet<Vector3Int> targetList = new HashSet<Vector3Int>();
        private List<WorldEffect> executedEffects = new List<WorldEffect>();
        public bool ReRollDamageForEachEffect { get; set; }

        public bool ReRollChanceToHitForEachEffect { get; set; }
        public int Turn { get; set; }
        public Mod.RuleFaction ActingFaction { get; set; }
        public Unit ActingUnit { get; set; }
        public string Identifier { get; set; }
        public List<Mod.RuleBuff> Buffs { get; set; } = new List<Mod.RuleBuff>();

        public List<Lazy<WorldEffect>> Effects { get; set; } = new List<Lazy<WorldEffect>>();
        public TargetMode TargetMode { get; set; }
        public int ChangeSpriteTo { get; set; }

        protected internal void Initialize(BattleGame game, Map map)
        {
            _game = game;
            _map = map;
        }
        public void StartTargeting()
        {
            if (TargetMode.Type == TargetModeType.Self)
            {
                // simple case, no special targeting needed:
                var selfPos = new List<Vector3Int>() {ActingUnit.Cell};
                var selfTarget = new Dictionary<Vector3Int, List<Vector3Int>>(){{ActingUnit.Cell, new List<Vector3Int>(){ActingUnit.Cell}}};
                ExecuteOn(selfPos, selfTarget);
                return;
            }

            if (ActingUnit.State != Moving && ActingUnit.CanAttack)
            {
                _map.RemoveAllHighlights(0);
                _map.RemoveAllHighlights(1);

                var validTargetPositions = TargetMode.GetValidTargetPositions(ActingUnit).ToList();
                _game.ShowValidTargets(validTargetPositions);
                
                var unitsInRange = validTargetPositions.Where(pos => _map.IsOtherFactionOn(ActingUnit, pos))
                    .Select(pos => _map.GetUnitAt(pos));
                if (_game.CurrentFaction == _game.PoVFaction)
                {
                    foreach (var unit in unitsInRange)
                    {
                        var targetUnit = (Unit) unit;
                        string info = Options.CombatMechanics.AttackPreviewInformationFor(ActingUnit, targetUnit);
                        targetUnit.SetLabelText(info);
                        targetUnit.IsHealthBarVisible = true;
                    }
                }

                if (validTargetPositions.Any())
                {
                    if (ChangeSpriteTo != -1)
                    {
                        ActingUnit.ChangeSprite(ChangeSpriteTo);
                    }
                    _game.ChangeStateTo(BattleGame.GameState.AttackTargetSelection);
                    targetList.Clear();
                }
                else
                {
                    Debug.Log("No Targets");
                }
            }
        }
        
        public bool TargetSelected(Vector3Int targetedPos)
        {
            if (!TargetMode.IsValidTarget(ActingUnit, targetedPos) || targetList.Contains(targetedPos))
                return false;

            _map.MarkTile(targetedPos);
            targetList.Add(targetedPos);

            ActingUnit.TurnTowards(targetedPos);
            return true;
        }

        public void ConfirmTargets()
        {
            _game.ToDefaultState();
            
            var affectedTiles = TargetMode.GetAffectedTiles(ActingUnit.Cell, targetList);
            
            ExecuteOn(targetList.ToList(), affectedTiles);
            
            targetList.Clear();
        }
        
        private void ExecuteOn(List<Vector3Int> targets, Dictionary<Vector3Int, List<Vector3Int>> affectedTiles)
        {
            int[] chanceToHit = Options.CombatMechanics.RollChanceToHit(ActingUnit);
            int[] damageRoll = IsOffensiveAction ? Options.CombatMechanics.RollDamage(ActingUnit, IsMelee) : new int[0];
            var effectChainCallsAfterAction = false;
            WorldEffect chainedEffect = null;
            for (var index = Effects.Count - 1; index >= 0; index--)
            {
                Lazy<WorldEffect> lazyEffect = Effects[index];
                var effect = lazyEffect.Value;

                effect.ActingUnit = ActingUnit;
                effect.SourcePosition = ActingUnit.Cell;
                effect.Targets = targets;
                effect.AffectedTiles = affectedTiles;

                effect.DamageRoll = ReRollDamageForEachEffect
                    ? Options.CombatMechanics.RollDamage(ActingUnit, IsMelee)
                    : damageRoll;
                effect.ChanceToHitRoll = ReRollChanceToHitForEachEffect
                    ? Options.CombatMechanics.RollChanceToHit(ActingUnit)
                    : chanceToHit;
                
                effect.ActiveWeapon = IsMelee && ActingUnit.HasMeleeWeapon
                    ? ActingUnit.MeleeWeapon
                    : ActingUnit.RangedWeapon;

                if (chainedEffect != null)
                {
                    var afterEffect = chainedEffect;
                    effect.OnEffectCompleted += sender => { afterEffect.Execute(); };                    
                    chainedEffect = null;
                }
                if (effect.Chained)
                {
                    chainedEffect = effect;
                    if (!effectChainCallsAfterAction)
                    {
                        effectChainCallsAfterAction = true;
                        effect.OnEffectCompleted += sender => { AfterAction(); };
                    }
                }
                else
                {
                    effect.Execute(); // EFFECTS EXECUTED HERE
                }

                executedEffects.Add(effect);
            }

            foreach (var ruleBuff in Buffs)
            {
                var buffFromRules = GameFactory.CreateBuffFromRules(ruleBuff);
                foreach (var affectedTileList in affectedTiles.Values)
                {
                    foreach (var affectedTile in affectedTileList)
                    {
                        if (!_map.IsUnitOn(affectedTile)) continue;
                    
                        var affectedUnit = _map.GetUnitAt(affectedTile);
                    
                        affectedUnit.ApplyBuff(buffFromRules); // BUFFS APPLIED HERE
                    }
                }
            }

            if (!effectChainCallsAfterAction)
            {
                AfterAction();
            }
        }

        public bool IsMelee { get; set; }
        public bool IsOffensiveAction { get; set; }

        private void AfterAction()
        {
            
            executedEffects.ForEach(e => { e.RemoveAllCompletionHandlers(); });
            ActingUnit.ResetSprite();
            if (IsOffensiveAction)
            {
                ActingUnit.Procs.OnHasAttacked();
                ActingUnit.AllowNoMoreActions();
            }
            
            _game.CheckForDeadUnits();
            targetList.Clear();
            _map.RemoveAllTileMarker();
            _game.UpdateUIButtons();
            
            if (!_map.FieldOfViewHasChanged) return;
            _game.GlobalFOVUpdate();
            _map.FieldOfViewHasChanged = false;
        }

        public bool IsValidTarget(Vector3Int mapPos)
        {
            return TargetMode.IsValidTarget(ActingUnit, mapPos);
        }
    }
}