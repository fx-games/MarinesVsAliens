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
        int ResolveAttack(int[] attackValues, Vector3Int unitPos, Unit attackingUnit, Vector3Int targetedPos, Vector3Int aoePos,
            Unit hitUnit);
    }

    public abstract class CombatBase
    {
        protected bool IsMelee(Unit attacker, Unit target)
        {
            return attacker.IsNextTo(target);
        }
    }

    public class DiceBasedCombat : CombatBase, ICombatMechanics
    {
        public string AttackPreviewInformationFor(Unit attackingUnit, Unit targetUnit)
        {
            if (IsMelee(attackingUnit, targetUnit))
            {
                int maxMeleeDef = targetUnit.MeleeWeapon.Rules.Damage.Sum(die => die.Max());
                int minMeleeDef = targetUnit.MeleeWeapon.Rules.Damage.Sum(die => die.Min());
            
                int maxMeleeAtt = attackingUnit.MeleeWeapon.Rules.Damage.Sum(die => die.Max());
                int minMeleeAtt = attackingUnit.MeleeWeapon.Rules.Damage.Sum(die => die.Min());

                return minMeleeAtt + "-" + maxMeleeAtt + ":" + minMeleeDef + "-" + maxMeleeDef;
            }
            else
            {
                int maxRangedAtt = attackingUnit.RangedWeapon.Rules.Damage.Sum(die => die.Max());
                int minRangedAtt = attackingUnit.RangedWeapon.Rules.Damage.Sum(die => die.Min());
            
                int armor = targetUnit.CurrentArmor;

                return minRangedAtt + "-" + maxRangedAtt + ":" + armor;
            }
        }

        public int[] AttackValues(Unit attackingUnit, Vector3Int targetedPos, bool isMelee)
        {
            return isMelee ? RollDamage(attackingUnit.MeleeWeapon.Rules) : RollDamage(attackingUnit.RangedWeapon.Rules);
        }
        
        public int ResolveAttack(int[] attackerDice, Vector3Int attackerPos, Unit attackingUnit, Vector3Int targetPos,
            Vector3Int aoePos, Unit target)
        {
            int damageLeft = -1;
            
            var isMelee = IsMelee(attackingUnit, target);
            
            if (isMelee && !attackingUnit.HasMeleeWeapon)
            {
                return damageLeft; // No melee weapon for melee attack
            }

            if (!isMelee && !attackingUnit.HasRangedWeapon)
                return damageLeft; // No ranged weapon
        
            var isAoEPosition = targetPos != aoePos;
        
            var weapon = attackingUnit.RangedWeapon;
            if (isMelee)
            {
                weapon = attackingUnit.MeleeWeapon;
            }

            var isExplosion = weapon.Rules.Radius > 0;

            int attackerScore = attackerDice.Sum();
            int damage = -1;
            
            if (isExplosion && isAoEPosition)
            {
                attackerScore = attackerDice.Max();
            }
            //Debug.Log("Attack: " + attackerScore);
            int defenderScore = -1;
            if (attackerScore > 0)
            {
                defenderScore = GetDefense(target, isMelee);
                //Debug.Log("Defense: " + defenderScore);
                LastMeleeDefense = defenderScore;
                //target.AttackedWith(attack, isMelee);
                if (defenderScore >= attackerScore)
                {
                    // no damage to target
                    damageLeft = 0;
                    if (isMelee && defenderScore > attackerScore)
                    {
                        // damage to attacker
                        int returnedMeleeDamage = defenderScore - attackerScore;
                        attackingUnit.DamageReceived(target, target.MeleeWeapon, returnedMeleeDamage);
                    }
                }
                else
                {
                    // damage to target
                    damage = attackerScore - defenderScore;
                    damageLeft = target.DamageReceived(attackingUnit, weapon, damage);
                }
            }
            
            Debug.Log("ATT:" + attackingUnit + "(" + attackerScore + ") -> " + target + "("+defenderScore+") => Dmg: " + damage);
            
            return damageLeft;
        }

        private int GetDefense(Unit target, bool isMelee)
        {
            if (isMelee)
            {
                return target.HasMeleeWeapon ? RollDamage(target.MeleeWeapon.Rules).Sum() : 0;
            }

            return target.CurrentArmor;
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
        public int LastMeleeDefense { get; private set; }
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

        public int ResolveAttack(int[] attackValues, Vector3Int unitPos, Unit attackingUnit, Vector3Int targetedPos, Vector3Int aoePos, Unit hitUnit)
        {
            int randomAttackValue = attackValues[0];
            var isAoEPosition = targetedPos != aoePos;
            var isMelee = attackingUnit.IsNextTo(hitUnit);

            var weapon = attackingUnit.RangedWeapon;
            if (isMelee)
            {
                weapon = attackingUnit.MeleeWeapon;
            }
            var isExplosion = weapon.Rules.Radius > 0;
            
            int chanceToHit = CalculateChanceToHit(attackingUnit, hitUnit, isMelee);
            if (randomAttackValue < chanceToHit || (isExplosion && isAoEPosition))
            {
                // HIT
                var damage = RollDamage(weapon.Rules)[0];
                
                damage -= hitUnit.CurrentArmor;
                if (damage > 0)
                {
                    return hitUnit.DamageReceived(attackingUnit, weapon, damage);
                }
            }
            else
            {
                // MISS
            }

            return -1;
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
        private int CalculateChanceToHit(Unit attackingUnit, Unit targetUnit, bool isMelee)
        {
            float baseAccuracy = attackingUnit.Rules.Accuracy;
            var weapon = isMelee ? attackingUnit.MeleeWeapon : attackingUnit.RangedWeapon;

            if (weapon == null) return -1;
            
            float weaponModifier = weapon.Rules.AccuracyModifier;

            float modifiedAccuracy = baseAccuracy + weaponModifier;
            
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