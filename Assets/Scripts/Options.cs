using System;
using System.Linq;
using UnityEngine;
using Random = UnityEngine.Random;

namespace DefaultNamespace
{
    public enum CombatMode { Dice, Percentage }
    public static class Options
    {
        public static CombatMode CombatMode { get; set; } = CombatMode.Percentage;
        public static bool AllowDiagonalMovement { get; set; } = true;
        public static bool AllowDiagonalAdjacentAttacks { get; set; } = false;

        public static ICombatMechanics CombatMechanics { get; set; } = new PercentageBasedCombat();

        public static bool Debug { get; set; } = false;
    }

    public interface ICombatMechanics
    {
        string AttackPreviewInformationFor(Unit attackingUnit, Unit targetUnit);
        int[] AttackValues(Unit attackingUnit, Vector3Int targetedPos, bool isMelee);
        int ResolveAttack(Weapon activeWeapon, int[] damageRoll, int[] attackValues, Unit attackingUnit, Unit hitUnit);
        int ResolveExplosion(Weapon activeWeapon, int[] damageRoll, Unit attackingUnit, Unit hitUnit);
        int ResolveDamageToTile(Weapon weapon);
        int[] RollChanceToHit(Unit actingUnit);
        int[] RollDamage(Unit actingUnit, bool isMelee);
    }

    public abstract class CombatBase
    {
        protected bool IsMelee(Unit attacker, Unit target)
        {
            return attacker.IsNextTo(target);
        }
    }

    public class PercentageBasedCombat : CombatBase, ICombatMechanics
    {
        public string AttackPreviewInformationFor(Unit attackingUnit, Unit targetUnit)
        {
            int chanceToHit = CalculateChanceToHit(attackingUnit, targetUnit, attackingUnit.IsNextTo(targetUnit));
            return chanceToHit == -1 ? "" : chanceToHit.ToString();
        }

        public int[] AttackValues(Unit attackingUnit, Vector3Int targetedPos, bool isMelee)
        {
            return new[] { Random.Range(0,100)}; // 0-99
        }
        public int ResolveExplosion(Weapon activeWeapon, int[] damageRoll, Unit attackingUnit, Unit hitUnit)
        {
            // HIT
            Debug.Log("Combat Mechanics registered an explosion on " + hitUnit);
                
            var damage = damageRoll.Sum();

            attackingUnit.Procs.OnHit(hitUnit, activeWeapon);
            hitUnit.Procs.OnBeingHit(attackingUnit, activeWeapon);
            damage -= hitUnit.CurrentArmor;
            if (damage > 0)
            {
                hitUnit.SetLabelText(damage.ToString(), animate: true);
                return hitUnit.DamageReceived(attackingUnit, activeWeapon, damage);
            }
            else
            {
                hitUnit.SetLabelText("abs", animate: true);
            }

            return -1;
        }
        public int ResolveAttack(Weapon activeWeapon,  int[] damageRoll, int[] attackValues, Unit attackingUnit, Unit hitUnit)
        {
            int randomAttackValue = attackValues.Sum();
            
            int chanceToHit = CalculateChanceToHit(attackingUnit, hitUnit, attackingUnit.MeleeWeapon == activeWeapon);
            if (randomAttackValue < chanceToHit)
            {
                // HIT
                Debug.Log("Combat Mechanics registered an attack hit on " + hitUnit);
                
                var damage = damageRoll.Sum();

                attackingUnit.Procs.OnHit(hitUnit, activeWeapon);
                hitUnit.Procs.OnBeingHit(attackingUnit, activeWeapon);
                damage -= hitUnit.CurrentArmor;
                if (damage > 0)
                {
                    hitUnit.SetLabelText(damage.ToString(), animate: true);
                    return hitUnit.DamageReceived(attackingUnit, activeWeapon, damage);
                }
                else
                {
                    hitUnit.SetLabelText("abs", animate: true);
                }
            }
            else
            {
                // MISS
                attackingUnit.Procs.OnMiss(hitUnit, activeWeapon);
                hitUnit.SetLabelText("mis", animate: true);
            }
            
            return -1;
        }

        public int ResolveDamageToTile(Weapon weapon)
        {
            return RollDamage(weapon.Rules).Sum();
        }

        public int[] RollChanceToHit(Unit actingUnit)
        {
            return new []{Random.Range(0, 100)}; // 100 values, 0-99
        }

        public int[] RollDamage(Unit actingUnit, bool melee)
        {
            return RollDamage(melee ? actingUnit.MeleeWeapon.Rules : actingUnit.RangedWeapon.Rules);
        }

        private int[] RollDamage(Mod.RuleWeapon weaponRules)
        {
            int[] results = new int[weaponRules.Damage.Count];
            int diceCount = 0;
            foreach (var damageRoll in weaponRules.Damage)
            {
                if (damageRoll.Length == 1)
                {
                    results[diceCount] = damageRoll[0];
                }
                else if (damageRoll.Length == 2)
                {
                    results[diceCount] = Random.Range(damageRoll[0], damageRoll[1] + 1);
                }
                else if (damageRoll.Length > 2)
                {
                    results[diceCount] = damageRoll[Random.Range(0, damageRoll.Length)];
                }

                diceCount++;
            }

            return results;
        }


        // should return an integer between 0 and 100
        // TODO: Add buffs and stuff for accuracy
        private int CalculateChanceToHit(Unit attackingUnit, Unit targetUnit, bool isMelee)
        {
            float baseAccuracy = attackingUnit.GetCurrentStat("Accuracy");
            
            var weapon = isMelee ? attackingUnit.MeleeWeapon : attackingUnit.RangedWeapon;

            if (weapon == null) return -1;
            
            float weaponModifier = weapon.Rules.AccuracyModifier;

            float targetUnitDefense = targetUnit.GetCurrentStat("Defense");

            float modifiedAccuracy = baseAccuracy + weaponModifier - targetUnitDefense;
            
            float distance = (attackingUnit.Cell - targetUnit.Cell).magnitude;

            float delta;
            float penalty = 0;
            if (distance < weapon.Rules.MinRange)
            {
                delta = weapon.Rules.MinRange - distance;
                penalty = delta * weapon.Rules.MinDropOff;
            }
            if (distance > weapon.Rules.MaxRange)
            {
                delta = distance - weapon.Rules.MaxRange;
                penalty = delta * weapon.Rules.MaxDropOff;
            }

            if (distance > weapon.Rules.EffectiveRange)
            {
                penalty = modifiedAccuracy;
            }

            float distanceModifiedAccuracy = modifiedAccuracy - penalty;

            if (distanceModifiedAccuracy < 0.0f)
            {
                distanceModifiedAccuracy = 0.0f;
            }
            else if (distanceModifiedAccuracy > 100.0f)
            {
                distanceModifiedAccuracy = 100.0f;
            }
            
            return (int)distanceModifiedAccuracy;
        }

    }
}