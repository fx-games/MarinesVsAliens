using System;
using System.Collections.Generic;
using UnityEngine;

namespace DefaultNamespace.Units.UnitActions
{
    public class UnitAction
    {
        public int Turn { get; set; }
        public Mod.RuleFaction ActingFaction { get; set; }
        public Unit ActingUnit { get; set; }
        public string Identifier { get; set; }
        public List<Lazy<WorldEffect>> EffectsOnTheWorld { get; set; } = new List<Lazy<WorldEffect>>();
    }

    public class TargetedUnitAction : UnitAction
    {
        public Vector3Int Target { get; set; }
    }
    
    public class MultiTargetedUnitAction : UnitAction
    {
        public List<Vector3Int> Targets { get; set; }
    }
}