using System.Collections.Generic;
using DefaultNamespace;
using UnityEngine;

namespace AI
{
    /// <summary>
    /// The basic SubsumptionScheduler.
    /// </summary>
    public class SubsumptionScheduler
    {
        /// <summary>
        /// The list of behaviors this Scheduler manages.
        /// </summary>
        protected readonly List<SubsumptionBehavior> mBehaviors;

        private IGame game;
        private IPlayerMap map;
        private Mod mod;
        private Mod.RuleFaction faction;


        public SubsumptionScheduler(IGame game, IPlayerMap map, Mod mod, Mod.RuleFaction faction)
        {
            Verbose = true;
            mBehaviors = new List<SubsumptionBehavior>();
            this.game = game;
            this.map = map;
            this.mod = mod;
            this.faction = faction;
        }

        /// <summary>
        /// If set to true, the Scheduler will output verbose debug messages on every frame.
        /// </summary>
        public bool Verbose { get; set; }

        /// <summary>
        /// Call this add a behavior to this scheduler.
        /// </summary>
        /// <param name="behavior">The behavior to add.</param>
        public void AddBehavior(SubsumptionBehavior behavior)
        {
            mBehaviors.Add(behavior);
            behavior.Init();
        }

        /// <summary>
        /// Runs the scheduler. Will execute a behavior if possible.
        /// </summary>
        /// <returns>The behavior which was executed.</returns>
        public virtual SubsumptionBehavior Run(Unit unit, MapTileInfo[,] mapKnowledge,
            List<Unit> visibleEnemies)
        {
            if (mBehaviors.Count == 0) return null;
            List<SubsumptionBehavior> firingBehaviors = new List<SubsumptionBehavior>();
            foreach (SubsumptionBehavior behavior in mBehaviors)
            {
                if (behavior.CheckIfFired(unit, mapKnowledge, visibleEnemies))
                    firingBehaviors.Add(behavior);
            }
            if (firingBehaviors.Count == 0) return null;
            firingBehaviors.Sort(BehaviorComparison);
            if (Verbose)
            {
                string dout = BehaviorListToString(firingBehaviors);
                Debug.Log(dout);
            }
            SubsumptionBehavior behaviorToFire = firingBehaviors[0];
            if (Verbose) Debug.Log(behaviorToFire);
            return behaviorToFire;
        }

        private static string BehaviorListToString(IEnumerable<SubsumptionBehavior> behaviors)
        {
            string output = "";
            foreach (SubsumptionBehavior b in behaviors)
                output += b.Name + ", ";
            return output;
        }

        /// <summary>
        /// Used for sorting. Compares to behaviors based on their priority level.
        /// </summary>
        /// <param name="one">The first behavior to compare.</param>
        /// <param name="two">The second behavior to compare.</param>
        /// <returns>-1 if the first has a smaller priority, 1 if the second has the smaller priority and 0 if they are equal.</returns>
        protected static int BehaviorComparison(SubsumptionBehavior one, SubsumptionBehavior two)
        {
            return one.Level.CompareTo(two.Level);
        }
    }
}