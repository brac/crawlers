using Crawlers.Domain.Enums;
using Crawlers.Server.Contracts;
using Crawlers.Server.Sessions;
using Xunit;

namespace Crawlers.Tests.Contracts;

public class SnapshotMapperTests
{
    [Fact]
    public void Tiles_are_flattened_in_row_major_order()
    {
        var mgr = new SessionManager();
        var state = mgr.CreateSession(seed: 1);
        var snap = SnapshotMapper.ToSnapshot(state);

        Assert.Equal(state.Floor.Width * state.Floor.Height, snap.Floor.Tiles.Length);
        for (int y = 0; y < state.Floor.Height; y++)
        {
            for (int x = 0; x < state.Floor.Width; x++)
            {
                int expected = (int)state.Floor.TileGrid[x, y].Type;
                int actual = snap.Floor.Tiles[y * state.Floor.Width + x];
                Assert.Equal(expected, actual);
            }
        }
    }

    [Fact]
    public void Snapshot_carries_session_player_and_floor_metadata()
    {
        var mgr = new SessionManager();
        var state = mgr.CreateSession(seed: 1);
        var snap = SnapshotMapper.ToSnapshot(state);

        Assert.Equal(state.Session.Id, snap.SessionId);
        Assert.Equal(GameMode.Exploration, snap.Mode);
        Assert.Equal(1, snap.FloorNumber);
        Assert.Equal(state.Player.Id, snap.Player.Id);
        Assert.Equal(state.Player.Position.X, snap.Player.X);
        Assert.Equal(state.Player.Position.Y, snap.Player.Y);
        Assert.Equal(state.Floor.Width, snap.Floor.Width);
        Assert.Equal(state.Floor.Height, snap.Floor.Height);
        Assert.Equal(state.Floor.Rooms.Count, snap.Floor.Rooms.Count);
    }
}
