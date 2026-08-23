using System.Security.Claims;
using Crawlers.Server.Contracts;
using Crawlers.Server.Hubs;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;

namespace Crawlers.Tests.TestSupport;

/// <summary>
/// Minimal stand-ins for the SignalR plumbing a <see cref="GameHub"/> needs.
/// The repo has no mocking library, so hub tests hand-roll the four pieces
/// the hub touches: the caller context (connection id + Items bag), the
/// group manager, the caller-clients bag, and the IHubContext the
/// SessionBroadcaster sends through.
/// </summary>
internal sealed class FakeHubCallerContext : HubCallerContext
{
    private readonly Dictionary<object, object?> _items = new();
    private readonly IFeatureCollection _features = new FeatureCollection();
    private readonly CancellationTokenSource _cts = new();

    public FakeHubCallerContext(string connectionId) => ConnectionId = connectionId;

    public override string ConnectionId { get; }
    public override string? UserIdentifier => null;
    public override ClaimsPrincipal? User => null;
    public override IDictionary<object, object?> Items => _items;
    public override IFeatureCollection Features => _features;
    public override CancellationToken ConnectionAborted => _cts.Token;
    public override void Abort() => _cts.Cancel();
}

internal sealed class FakeGroupManager : IGroupManager
{
    public List<(string ConnectionId, string Group)> Added { get; } = new();
    public List<(string ConnectionId, string Group)> Removed { get; } = new();

    public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken ct = default)
    {
        Added.Add((connectionId, groupName));
        return Task.CompletedTask;
    }

    public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken ct = default)
    {
        Removed.Add((connectionId, groupName));
        return Task.CompletedTask;
    }
}

/// <summary>Records every snapshot pushed at a given connection.</summary>
internal sealed class RecordingGameClient : IGameClient
{
    public List<GameStateSnapshotDto> Snapshots { get; } = new();

    public Task ReceiveSnapshot(GameStateSnapshotDto snapshot)
    {
        Snapshots.Add(snapshot);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Hands out one <see cref="RecordingGameClient"/> per connection id so a
/// test can assert which connection actually received a session's snapshots.
/// </summary>
internal sealed class RecordingGameClients : IHubCallerClients<IGameClient>
{
    private readonly Dictionary<string, RecordingGameClient> _byConnection = new(StringComparer.Ordinal);
    private readonly RecordingGameClient _sink = new();

    public RecordingGameClient For(string connectionId)
    {
        if (!_byConnection.TryGetValue(connectionId, out var c))
        {
            c = new RecordingGameClient();
            _byConnection[connectionId] = c;
        }
        return c;
    }

    public IGameClient Client(string connectionId) => For(connectionId);

    public IGameClient All => _sink;
    public IGameClient Caller => _sink;
    public IGameClient Others => _sink;
    public IGameClient AllExcept(IReadOnlyList<string> excludedConnectionIds) => _sink;
    public IGameClient Clients(IReadOnlyList<string> connectionIds) => _sink;
    public IGameClient Group(string groupName) => _sink;
    public IGameClient Groups(IReadOnlyList<string> groupNames) => _sink;
    public IGameClient GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => _sink;
    public IGameClient OthersInGroup(string groupName) => _sink;
    public IGameClient User(string userId) => _sink;
    public IGameClient Users(IReadOnlyList<string> userIds) => _sink;
}

internal sealed class FakeGameHubContext : IHubContext<GameHub, IGameClient>
{
    public RecordingGameClients Recording { get; } = new();
    public FakeGroupManager GroupManager { get; } = new();

    IHubClients<IGameClient> IHubContext<GameHub, IGameClient>.Clients => Recording;
    IGroupManager IHubContext<GameHub, IGameClient>.Groups => GroupManager;
}
