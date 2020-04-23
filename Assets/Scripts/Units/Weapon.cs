using DefaultNamespace;

public class Weapon
{
    private Mod.RuleWeapon weaponRules;
    private Mod mod;

    public Weapon(Mod mod, Mod.RuleWeapon rules)
    {
        this.mod = mod;
        weaponRules = rules;
    }

    public bool HasAreaOfEffect => weaponRules.Radius > 0;

    public Mod.RuleWeapon Rules => weaponRules;

    public override string ToString()
    {
        return mod.Tr(weaponRules.Type);
    }

    public int DamageToTile()
    {
        return Options.CombatMechanics.ResolveDamageToTile(this);
    }

    public int AoEDamageToTile()
    {
        return DamageToTile();
    }
}