using System;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;

namespace XIVRaidToolsPlugin;

public enum SessionStatus { Idle, Connecting, Active, Reconnecting }

// Non-generic home for members that don't depend on TState — referencing a
// generic class's static members from outside always needs a type argument
// (SessionClient<MechState>.Foo), which is awkward for something like a
// config UI's URL hint that has nothing to do with any tool's state shape.
public static class SessionDefaults
{
    // Same production URL as kefka-says/session.js's WS_URL. Overridable
    // per-user via Configuration.RelayUrlOverride (see Windows/ConfigWindow.cs).
    public const string DefaultWsUrl = "wss://xiv-raid-tools-production.up.railway.app";
}

// Speaks the exact same protocol as kefka-says/session.js's connectWS/syncState:
// {type:'create'} / {type:'join',room} -> {type:'created'|'joined',room}
// {type:'state', state:{...}} broadcast to the rest of the room
// TState.Serialize()'s keys must stay in lockstep with the matching tool's
// own SYNC_KEYS on the webapp side (app.js for Kefka Says).
//
// Generic over TState (an ISyncedState) so this connection/room/reconnect
// machinery is reusable by a future second Dalamud tool with its own state
// shape — this class itself has zero knowledge of Kefka Says' mechanic
// fields, all of that lives in MechState now (see ISyncedState.cs).
//
// Unlike the webapp (which doesn't retry a dropped connection — you just
// reload the page), this reconnects with backoff, since "reload the tab" has
// no in-game equivalent and a raid-night WS blip shouldn't lose the room.
public sealed class SessionClient<TState> : IDisposable where TState : ISyncedState
{
    private static readonly TimeSpan[] ReconnectDelays =
    {
        TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(20),
    };

    private readonly IPluginLog _log;
    private readonly Configuration _config;
    private readonly CancellationTokenSource _lifecycle = new();

    // Read fresh on every connect attempt (not captured once) so a relay URL
    // change in ConfigWindow takes effect on the next create/join/reconnect
    // without needing a plugin restart.
    private string WsUrl => string.IsNullOrWhiteSpace(_config.RelayUrlOverride) ? SessionDefaults.DefaultWsUrl : _config.RelayUrlOverride.Trim();

    private ClientWebSocket? _ws;
    private CancellationTokenSource? _connCts;
    private bool _applyingRemote;

    // The room we intend to stay connected to. Null means "no session
    // wanted" (idle, or user hit Leave) — ScheduleReconnect no-ops on null.
    // Set to the *actual* room id once a create/join response arrives, so a
    // later drop always reconnects via `join`, even for a session that
    // started with `create`.
    private string? _desiredRoom;
    private int _reconnectAttempt;

    // Remembered for the lifetime of the session (not just the initial
    // send) so a dropped-connection reconnect's `join` still carries it —
    // see ReconnectAfterDelayAsync. Null/empty means "no password set".
    private string? _password;

    public string? RoomId { get; private set; }
    public SessionStatus Status { get; private set; } = SessionStatus.Idle;
    public bool Connected => _ws?.State == WebSocketState.Open;
    public TState State { get; }
    public bool HasPassword => _password is not null;

    // Server broadcasts this whenever room membership changes (join/leave) —
    // see server/index.js's broadcastCount. Starts at 1 (just yourself)
    // rather than 0 so there's never a flash of "0 connected" before the
    // first broadcast arrives.
    public int ConnectedCount { get; private set; } = 1;

    public event Action? StateChanged;

    public SessionClient(IPluginLog log, Configuration config, TState state)
    {
        _log = log;
        _config = config;
        State = state;
    }

    // roomCode lets the caller claim a specific code (if free) instead of
    // a server-assigned random one — same optional-request/server-validates
    // pattern as password, see server/index.js's create handler.
    public Task CreateAsync(string? password = null, string? roomCode = null)
    {
        _password = string.IsNullOrEmpty(password) ? null : password;
        var msg = new JsonObject { ["type"] = "create", ["client"] = "plugin" };
        if (_password is not null) msg["password"] = _password;
        if (!string.IsNullOrEmpty(roomCode)) msg["room"] = roomCode.ToUpperInvariant();
        return OpenAsync(msg, desiredRoom: null);
    }

    public Task JoinAsync(string room, string? password = null)
    {
        room = room.ToUpperInvariant();
        _password = string.IsNullOrEmpty(password) ? null : password;
        var msg = new JsonObject { ["type"] = "join", ["room"] = room, ["client"] = "plugin" };
        if (_password is not null) msg["password"] = _password;
        return OpenAsync(msg, desiredRoom: room);
    }

    private async Task OpenAsync(JsonObject initial, string? desiredRoom)
    {
        CancelCurrentConnection();
        _desiredRoom = desiredRoom;
        _reconnectAttempt = 0;
        SetStatus(SessionStatus.Connecting);
        await ConnectOnceAsync(initial);
    }

    private async Task ConnectOnceAsync(JsonObject initial)
    {
        var ws = new ClientWebSocket();
        var cts = CancellationTokenSource.CreateLinkedTokenSource(_lifecycle.Token);
        try
        {
            await ws.ConnectAsync(new Uri(WsUrl), cts.Token);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.Warning($"Kefka Says connect failed: {ex.Message}");
            cts.Dispose();
            ScheduleReconnect();
            return;
        }

        _ws = ws;
        _connCts = cts;
        _ = ReceiveLoopAsync(ws, cts.Token);
        await SendAsync(initial);
    }

    private async Task ReceiveLoopAsync(ClientWebSocket ws, CancellationToken ct)
    {
        var buffer = new byte[8192];
        try
        {
            while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                using var stream = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await ws.ReceiveAsync(buffer, ct);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        ScheduleReconnect();
                        return;
                    }
                    stream.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                HandleMessage(Encoding.UTF8.GetString(stream.ToArray()));
            }
        }
        catch (OperationCanceledException)
        {
            // Manual Leave()/Dispose() — not a drop, don't reconnect.
        }
        catch (Exception ex)
        {
            _log.Warning($"Kefka Says WS dropped: {ex.Message}");
            ScheduleReconnect();
        }
    }

    private void ScheduleReconnect()
    {
        if (_desiredRoom is not { } room || _lifecycle.IsCancellationRequested) return;
        SetStatus(SessionStatus.Reconnecting);
        var delay = ReconnectDelays[Math.Min(_reconnectAttempt, ReconnectDelays.Length - 1)];
        _reconnectAttempt++;
        _ = ReconnectAfterDelayAsync(delay, room);
    }

    private async Task ReconnectAfterDelayAsync(TimeSpan delay, string room)
    {
        try
        {
            await Task.Delay(delay, _lifecycle.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (_desiredRoom != room) return; // superseded by a manual Leave/Join/Create meanwhile
        var msg = new JsonObject { ["type"] = "join", ["room"] = room, ["client"] = "plugin" };
        if (_password is not null) msg["password"] = _password;
        await ConnectOnceAsync(msg);
    }

    private void HandleMessage(string json)
    {
        if (JsonNode.Parse(json)?.AsObject() is not { } msg) return;

        switch (msg["type"]?.GetValue<string>())
        {
            case "created" or "joined":
                RoomId = msg["room"]?.GetValue<string>();
                _desiredRoom = RoomId;
                _reconnectAttempt = 0;
                SetStatus(SessionStatus.Active);
                break;
            case "count" when msg["count"] is { } count:
                ConnectedCount = count.GetValue<int>();
                break;
            // "readyCheck" is deliberately unhandled — the game already has
            // its own ready check, so this plugin doesn't surface the
            // webapp/relay's anonymous-count version at all, even if
            // another room member (on the webapp) starts one.
            case "state" when msg["state"]?.AsObject() is { } state:
                _applyingRemote = true;
                State.ApplyRemote(state);
                _applyingRemote = false;
                break;
            case "error":
                _log.Warning($"Kefka Says session error: {msg["msg"]}");
                Leave();
                break;
        }
        StateChanged?.Invoke();
    }

    // Call after any local mutation — mirrors session.js's syncState().
    // configureExtra lets a caller piggyback one-shot keys onto this specific
    // push (Kefka's Reset() attaches clearDebuffs/historySnapshot this way —
    // see KefkaSaysWindow's Reset handling) on top of State.Serialize()'s
    // normal synced fields, without SessionClient needing to know what those
    // keys mean — TState.ApplyRemote is what interprets them on the
    // receiving end.
    public void PushState(Action<JsonObject>? configureExtra = null)
    {
        if (_applyingRemote || !Connected) return;
        var payload = State.Serialize();
        configureExtra?.Invoke(payload);
        _ = SendAsync(new JsonObject { ["type"] = "state", ["state"] = payload });
    }

    private async Task SendAsync(JsonObject obj)
    {
        if (_ws is not { State: WebSocketState.Open }) return;
        var bytes = Encoding.UTF8.GetBytes(obj.ToJsonString());
        await _ws.SendAsync(bytes, WebSocketMessageType.Text, true, _connCts?.Token ?? CancellationToken.None);
    }

    private void SetStatus(SessionStatus status)
    {
        Status = status;
        StateChanged?.Invoke();
    }

    // User-initiated disconnect: clears the reconnect target so any
    // in-flight backoff (or a not-yet-acked connect attempt) gives up.
    public void Leave()
    {
        _desiredRoom = null;
        _password = null;
        CancelCurrentConnection();
        RoomId = null;
        ConnectedCount = 1;
        SetStatus(SessionStatus.Idle);
    }

    private void CancelCurrentConnection()
    {
        _connCts?.Cancel();
        _ws?.Abort();
        _ws = null;
    }

    public void Dispose()
    {
        _desiredRoom = null;
        _lifecycle.Cancel();
        CancelCurrentConnection();
    }
}
