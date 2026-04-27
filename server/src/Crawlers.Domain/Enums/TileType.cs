namespace Crawlers.Domain.Enums;

public enum TileType
{
    Floor = 0,
    Wall = 1,
    Door = 2,        // closed door — blocks LOS, opens on player bump
    StairsUp = 3,
    StairsDown = 4,
    OpenDoor = 5,    // does not block LOS or movement
    LockedDoor = 6   // blocks LOS and movement; opened by an external trigger (e.g. boss death)
}
