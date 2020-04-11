using System;
using System.Linq;
using UnityEngine;

namespace DefaultNamespace
{
    public enum CombatMode { Dice, Percentage }
    public static class Options
    {
        public static CombatMode CombatMode { get; set; } = CombatMode.Percentage;
        public static bool AllowDiagonalMovement { get; set; } = true;
        public static bool AllowDiagonalAdjacentAttacks { get; set; } = false;

        public static ICombatMechanics CombatMechanics { get; set; } = new DiceBasedCombat();

        public static bool Debug { get; set; } = false;
    }

    public interface ICombatMechanics
    {
        string AttackPreviewInformationFor(Unit attackingUnit, Unit targetUnit);
        int[] AttackValues(bool isMelee, Unit attackingUnit, Vector3Int targetedPos);
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

        public int[] AttackValues(bool isMelee, Unit attackingUnit, Vector3Int targetedPos)
        {
            return isMelee ? attackingUnit.MeleeWeapon.RollDamage() : attackingUnit.RangedWeapon.RollDamage();
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
                defenderScore = target.GetDefense(isMelee);
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

        public int LastMeleeDefense { get; private set; }
    }

    public class PercentageBasedCombat : CombatBase, ICombatMechanics
    {

        public string AttackPreviewInformationFor(Unit attackingUnit, Unit targetUnit)
        {
            int chanceToHit = CalculateChanceToHit(attackingUnit, targetUnit);
            return chanceToHit.ToString();
        }

        public int[] AttackValues(bool isMelee, Unit attackingUnit, Vector3Int targetedPos)
        {
            throw new NotImplementedException();
        }

        public int ResolveAttack(int[] attackValues, Vector3Int unitPos, Unit attackingUnit, Vector3Int targetedPos, Vector3Int aoePos, Unit hitUnit)
        {
            throw new NotImplementedException();
        }

        private int CalculateChanceToHit(Unit attackingUnit, Unit targetUnit)
        {
            int baseAccuracy = 70;
            float distance = (attackingUnit.Cell - targetUnit.Cell).magnitude;

            int accuracy = (int) Math.Max(baseAccuracy - (distance * 7.5f), 0);

            return accuracy;
        }

    }
}