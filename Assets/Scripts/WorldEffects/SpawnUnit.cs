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

            foreach (var targetAffectedPair in AffectedTiles)
            {
                var target = targetAffectedPair.Key;
                var areaOfEffect = targetAffectedPair.Value;

                foreach (var affectedTile in areaOfEffect)
                {
                    var unitLoadout = _mod.GetLoadout(ActingUnit.ControllingPlayer.Faction, unitType);
                    _game.SpawnUnit(affectedTile, unitType, unitLoadout, ActingUnit.ControllingPlayer);
                }
            }
            
            OnCompleted();
        }
    }
}