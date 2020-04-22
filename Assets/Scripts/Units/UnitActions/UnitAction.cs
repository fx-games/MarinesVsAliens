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
        public bool ReRollDamageForEachEffect { get; set; }

        public bool ReRollChanceToHitForEachEffect { get; set; }
        public int Turn { get; set; }
        public Mod.RuleFaction ActingFaction { get; set; }
        public Unit ActingUnit { get; set; }
        public string Identifier { get; set; }
        public List<Mod.RuleBuff> Buffs { get; set; } = new List<Mod.RuleBuff>();

        public List<Lazy<WorldEffect>> Effects { get; set; } = new List<Lazy<WorldEffect>>();
        public TargetMode TargetMode { get; set; }
        
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
                ExecuteOn(selfPos, selfPos);
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
        
        private void ExecuteOn(List<Vector3Int> targets, List<Vector3Int> affectedTiles)
        {
            int[] chanceToHit = Options.CombatMechanics.RollChanceToHit(ActingUnit);
            int[] damageRoll = Options.CombatMechanics.RollDamage(ActingUnit, IsMelee);
            
            foreach (var lazyEffect in Effects)
            {
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
                
                effect.Execute(); // EFFECTS EXECUTED HERE
            }

            foreach (var ruleBuff in Buffs)
            {
                var buffFromRules = GameFactory.CreateBuffFromRules(ruleBuff);
                foreach (var affectedTile in affectedTiles)
                {
                    if (!_map.IsUnitOn(affectedTile)) continue;
                    
                    var affectedUnit = _map.GetUnitAt(affectedTile);
                    
                    affectedUnit.ApplyBuff(buffFromRules); // BUFFS APPLIED HERE
                }
            }

            AfterAction();
        }

        public bool IsMelee { get; set; }

        private void AfterAction()
        {
            targetList.Clear();
            _map.RemoveAllTileMarker();
            _game.CheckForDeadUnits();
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