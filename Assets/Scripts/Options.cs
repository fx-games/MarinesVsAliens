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

    }

    public interface ICombatMechanics
    {
        event UnitEvent UnitKilled;
        string AttackPreviewInformationFor(Unit unit, Unit attackingUnit);
        int[] AttackValues(bool isMelee, Unit unit, Vector3Int targetedPos);
        int ResolveAttack(int[] attackValues, Vector3Int unitPos, Unit unit, Vector3Int targetedPos, Vector3Int aoePos,
            Unit hitUnit);
    }

    public abstract class CombatBase
    {
        public event UnitEvent UnitKilled;

        public void OnUnitKilled(Unit unit)
        {
            if (UnitKilled != null)
            {
                UnitKilled(this, new UnitEventArgs(){unit = unit});
            }
        }

        protected bool IsMelee(Unit attacker, Unit target)
        {
            return attacker.IsNextTo(target);
        }
    }

    public class DiceBasedCombat : CombatBase, ICombatMechanics
    {
        public string AttackPreviewInformationFor(Unit unit, Unit attackingUnit)
        {
            if (IsMelee(attackingUnit, unit))
            {
                int maxMeleeDef = unit.MeleeWeapon.Rules.Damage.Sum(die => die.Max());
                int minMeleeDef = unit.MeleeWeapon.Rules.Damage.Sum(die => die.Min());
            
                int maxMeleeAtt = attackingUnit.MeleeWeapon.Rules.Damage.Sum(die => die.Max());
                int minMeleeAtt = attackingUnit.MeleeWeapon.Rules.Damage.Sum(die => die.Min());

                return minMeleeAtt + "-" + maxMeleeAtt + ":" + minMeleeDef + "-" + maxMeleeDef;
            }
            else
            {
                int maxRangedAtt = attackingUnit.RangedWeapon.Rules.Damage.Sum(die => die.Max());
                int minRangedAtt = attackingUnit.RangedWeapon.Rules.Damage.Sum(die => die.Min());
            
                int armor = unit.CurrentArmor;

                return minRangedAtt + "-" + maxRangedAtt + ":" + armor;
            }
        }

        public int[] AttackValues(bool isMelee, Unit unit, Vector3Int targetedPos)
        {
            return isMelee ? unit.MeleeWeapon.RollDamage() : unit.RangedWeapon.RollDamage();
        }
        
        public int ResolveAttack(int[] attackerDice, Vector3Int attackerPos, Unit attacker, Vector3Int targetPos,
            Vector3Int aoePos, Unit target)
        {
            int damageLeft = -1;
            var isMelee = IsMelee(attacker, target);
            
            if (isMelee && !attacker.HasMeleeWeapon)
            {
                return damageLeft; // No melee weapon for melee attack
            }

            if (!isMelee && !attacker.HasRangedWeapon)
                return damageLeft; // No ranged weapon
        
            var isAoEPosition = targetPos != aoePos;
        
            var weapon = attacker.RangedWeapon;
            if (isMelee)
            {
                weapon = attacker.MeleeWeapon;
            }

            var isExplosion = weapon.Rules.Radius > 0;

            int attackerScore = attackerDice.Sum();
            
            if (isExplosion && isAoEPosition)
            {
                attackerScore = attackerDice.Max();
            }
            Debug.Log("Attack: " + attackerScore);

            if (attackerScore > 0)
            {
                int defenderScore = target.GetDefense(isMelee);
                Debug.Log("Defense: " + defenderScore);
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
                        attacker.DamageReceived(target, target.MeleeWeapon, returnedMeleeDamage);
                    }
                }
                else
                {
                    // damage to target
                    int damage = attackerScore - defenderScore;
                    damageLeft = target.DamageReceived(attacker, weapon, damage);
                }
                if (target.CurrentHealth <= 0)
                {
                    OnUnitKilled(target);
                }
                if (attacker.CurrentHealth <= 0)
                {
                    OnUnitKilled(attacker);
                }
            }

            return damageLeft;
        }

        public int LastMeleeDefense { get; private set; }
    }

    public class PercentageBasedCombat : CombatBase, ICombatMechanics
    {

        public string AttackPreviewInformationFor(Unit unit, Unit attackingUnit)
        {
            int chanceToHit = CalculateChanceToHit(attackingUnit, unit);
            return chanceToHit.ToString();
        }

        public int[] AttackValues(bool isMelee, Unit unit, Vector3Int targetedPos)
        {
            throw new NotImplementedException();
        }

        public int ResolveAttack(int[] attackValues, Vector3Int unitPos, Unit unit, Vector3Int targetedPos,
            Vector3Int aoePos,
            Unit hitUnit)
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