using System;
using System.Collections;
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

        public abstract bool CheckIfFired(Unit unit, MapTileInfo[,] mapKnowledge, List<Unit> visibleEnemies);

        public abstract IEnumerator Fire(Unit unit, MapTileInfo[,] mapKnowledge, List<Unit> visibleEnemies, Action<PlayerCommand> callback);

        public override string ToString()
        {
            return Name;
        }
    }
}