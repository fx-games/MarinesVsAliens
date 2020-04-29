public class Player
{
    public Player(int index)
    {
        Index = index;
        index = index + 1;
        Name = "Player " + index;
    }

    public string Name { get; set; }
    public int Index { get; private set; }
    
    public Mod.RuleFaction Faction { get; set; }

    public override string ToString()
    {
        return Name + " - " + Faction;
    }
}