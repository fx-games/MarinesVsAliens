/// <summary>
/// The base class for all SubsumptionBehaviors. Derive from this when you implement a behavior.
/// </summary>
public abstract class SubsumptionBehavior
{
    /// <summary>
    /// Optional..
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// The priority Level, lower value means higher priority.
    /// </summary>
    public int Level { get; set; }

    /// <summary>
    /// Called once when the behavior gets initialized.
    /// </summary>
    public abstract void Init();

    /// <summary>
    /// Called to check if this behavior should fire for this agent.
    /// </summary>
    /// <param name="agent">The affected agent.</param>
    /// <returns>True if this behavior should fire, false otherwise.</returns>
    public abstract bool CheckIfFired(Unit agent);

    /// <summary>
    /// Called when this behavior fires for the given agent.
    /// </summary>
    /// <param name="agent">The affected agent.</param>
    public abstract void Fire(Unit agent);
}