using Crawlers.Server.Contracts;
using Crawlers.Server.Lobbies;

namespace Crawlers.Tests.Contracts;

public class LobbyMapperTests
{
    [Fact]
    public void ToDto_maps_username_through_to_lobby_member_dto()
    {
        var mgr = new LobbyManager(new Random(7));
        var host = Guid.NewGuid();
        var state = mgr.CreateLobby(host, "Brac", "conn-host");
        var joiner = Guid.NewGuid();
        mgr.JoinByCode(state.Room.Code, joiner, "Companion", "conn-joiner");

        var dto = LobbyMapper.ToDto(state);

        Assert.Equal(2, dto.Members.Count);
        Assert.Equal("Brac", dto.Members[0].Username);
        Assert.True(dto.Members[0].IsHost);
        Assert.Equal("Companion", dto.Members[1].Username);
        Assert.False(dto.Members[1].IsHost);
    }
}
