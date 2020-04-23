public class UnitStat
{
    public UnitStat(string name, int maxValue)
    {
        Name = name;
        CurrentValue = maxValue;
        MaxValue = maxValue;
    }

    public string Name { get; }
    public int MaxValue { get; private set; }
    public int CurrentValue { get; private set; }

    public int Change(int change)
    {
        int oldValue = CurrentValue;
        Set(CurrentValue + change);
        var absorbed = CurrentValue - oldValue;
        return absorbed;
    }

    public int Set(int value)
    {
        CurrentValue = value;
        
        if (CurrentValue <= 0)
        {
            CurrentValue = 0;
        }
        else if (CurrentValue > MaxValue)
        {
            CurrentValue = MaxValue;
        }

        var rest = value - CurrentValue;
        return rest;
    }
}