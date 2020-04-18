using UnityEngine;

namespace WorldEffects
{
    public class OpenDoorEffect : WorldEffect
    {   
        public override void Execute()
        {
            Debug.Log("OpenDoor() FIRED: " + this);
            foreach (var affectedTile in AffectedTiles)
            {
                if (_map.IsDoorAt(affectedTile))
                {
                    _map.RemoveDoor(affectedTile);
                }
                else
                {
                    foreach (var direction in _map.Cardinal)
                    {
                        var neighborPos = affectedTile + direction.ToVector();
                    
                        if (!_map.IsDoorAt(neighborPos)) continue;
                    
                        _map.RemoveDoor(neighborPos);
    
                        break;
                    }
                }
            }
            
            _game.GlobalFOVUpdate();
            _game.UpdateVisibleUnits(ActingUnit);            
        }
    }
}