using System.Collections.Generic;

namespace AI
{
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

        public abstract bool CheckIfFired(MapTileInfo[,] mapKnowledge, List<Unit> allUnits, List<Unit> visibleEnemies);

        public abstract PlayerCommand Fire(MapTileInfo[,] mapKnowledge, List<Unit> allUnits, List<Unit> visibleEnemies);
    }
}