using System;
using System.Linq;
using DefaultNamespace;
using UnityEngine;

namespace WorldEffects
{  
    public class SpawnUnit : WorldEffect
    {
        public override void Execute()
        {
            var unitType = StringOne;
            Debug.Log("SpawnUnit("+unitType+") executing.");

            foreach (var affectedTile in AffectedTiles)
            {
                var unitLoadout = _mod.GetLoadout(ActingUnit.Faction, unitType);
                _game.SpawnUnit(affectedTile, unitType, unitLoadout, ActingUnit.Faction);
            }
            
            ActingUnit.AllowNoMoreActions();
        }
    }
}