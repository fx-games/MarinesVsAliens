using UnityEngine;

namespace DefaultNamespace.Units.UnitActions
{
    public class OpenDoorEffect : WorldEffect
    {   
        public override void Execute()
        {
            Debug.Log("OpenDoor() FIRED: " + Source);
            foreach (var direction in _map.Cardinal)
            {
                var neighborPos = Source.Cell + direction.ToVector();
                
                if (!_map.IsDoorAt(neighborPos)) continue;
                
                _map.RemoveDoor(Source, neighborPos);

                Source.UpdateFoV(Source.Faction == _game.PoVFaction, Source.Faction == _game.PoVFaction);
                
                _game.UpdateVisibleUnits(Source);

                break;
            }
        }

    }
}