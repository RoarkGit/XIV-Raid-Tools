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

// Speaks the exact same protocol as kefka-says/app.js's connectWS/syncState:
// {type:'create'} / {type:'join',room} -> {type:'created'|'joined',room}
// {type:'state', state:{...}} broadcast to the rest of the room
// SYNC_KEYS below must stay in lockstep with app.js's SYNC_KEYS array.
//
// Unlike the webapp (which doesn't retry a dropped connection — you just
// reload the page), this reconnects with backoff, since "reload the tab" has
// no in-game equivalent and a raid-night WS blip shouldn't lose the room.
public sealed class SessionClient : IDisposable
{
    // Same production URL as kefka-says/app.js's WS_URL. Overridable per-user
    // via Configuration.RelayUrlOverride (see Windows/ConfigWindow.cs).
    public const string DefaultWsUrl = "wss://xiv-raid-tools-production.up.railway.app";

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
    private string WsUrl => string.IsNullOrWhiteSpace(_config.RelayUrlOverride) ? DefaultWsUrl : _config.RelayUrlOverride.Trim();

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

    public string? RoomId { get; private set; }
    public SessionStatus Status { get; private set; } = SessionStatus.Idle;
    public bool Connected => _ws?.State == WebSocketState.Open;
    public MechState State { get; } = new();

    public event Action? StateChanged;

    public SessionClient(IPluginLog log, Configuration config)
    {
        _log = log;
        _config = config;
    }

    public Task CreateAsync() => OpenAsync(new JsonObject { ["type"] = "create" }, desiredRoom: null);

    public Task JoinAsync(string room)
    {
        room = room.ToUpperInvariant();
        return OpenAsync(new JsonObject { ["type"] = "join", ["room"] = room }, desiredRoom: room);
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
        await ConnectOnceAsync(new JsonObject { ["type"] = "join", ["room"] = room });
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
            case "state" when msg["state"]?.AsObject() is { } state:
                _applyingRemote = true;
                ApplyRemote(state);
                _applyingRemote = false;
                break;
            case "error":
                _log.Warning($"Kefka Says session error: {msg["msg"]}");
                Leave();
                break;
        }
        StateChanged?.Invoke();
    }

    // Call after any local mutation — mirrors app.js's render() -> syncState().
    public void PushState()
    {
        if (_applyingRemote || !Connected) return;
        _ = SendAsync(new JsonObject { ["type"] = "state", ["state"] = BuildSharedState() });
    }

    // Reset needs one thing a plain PushState() can't do: tell every OTHER
    // client to clear its own G1Pos/G2Pos/G1Accel/G2Accel too (see
    // MechState.ClearLocalDebuffs's comment for why those aren't normal
    // synced fields). Piggybacks a "clearDebuffs" flag on the same state
    // message rather than a new WS message type — the relay server just
    // forwards the `state` object opaquely, so this needs no server change,
    // only ApplyRemote recognizing the flag on the receiving end.
    public void PushReset()
    {
        if (_applyingRemote || !Connected) return;
        var payload = BuildSharedState();
        payload["clearDebuffs"] = true;
        _ = SendAsync(new JsonObject { ["type"] = "state", ["state"] = payload });
    }

    private JsonObject BuildSharedState() => new()
    {
        ["g1rf"] = RfToStr(State.G1Rf),
        ["g2rf"] = RfToStr(State.G2Rf),
        ["it1type"] = TypeToStr(State.It1Type),
        ["it1rf"] = RfToStr(State.It1Rf),
        ["it2rf"] = RfToStr(State.It2Rf),
        ["thunderRF"] = RfToStr(State.ThunderRf),
        ["blizzardRF"] = RfToStr(State.BlizzardRf),
        ["enforceOrder"] = State.EnforceOrder,
    };

    private void ApplyRemote(JsonObject state)
    {
        // Must check ContainsKey, not `state["k"] is {}` — JsonObject's indexer
        // returns null both for an absent key AND a key explicitly set to JSON
        // null, so an `is {}` check can't tell "not sent" apart from "cleared".
        // app.js's applyShared avoids this with `if (k in state)`; we need the
        // same distinction or Reset()/unsetting a field never reaches peers.
        if (state.ContainsKey("g1rf")) State.G1Rf = StrToRf(state["g1rf"]?.GetValue<string>());
        if (state.ContainsKey("g2rf")) State.G2Rf = StrToRf(state["g2rf"]?.GetValue<string>());
        if (state.ContainsKey("it1type")) State.It1Type = StrToType(state["it1type"]?.GetValue<string>());
        if (state.ContainsKey("it1rf")) State.It1Rf = StrToRf(state["it1rf"]?.GetValue<string>());
        if (state.ContainsKey("it2rf")) State.It2Rf = StrToRf(state["it2rf"]?.GetValue<string>());
        if (state.ContainsKey("thunderRF")) State.ThunderRf = StrToRf(state["thunderRF"]?.GetValue<string>());
        if (state.ContainsKey("blizzardRF")) State.BlizzardRf = StrToRf(state["blizzardRF"]?.GetValue<string>());
        if (state.ContainsKey("enforceOrder")) State.EnforceOrder = state["enforceOrder"]?.GetValue<bool>() ?? false;

        // See PushReset's comment — clears THIS client's own local-only
        // debuff fields in response to another client's Reset, never
        // applies someone else's specific selection to us.
        if (state.ContainsKey("clearDebuffs")) State.ClearLocalDebuffs();
    }

    private static string? RfToStr(RF v) => v switch { RF.Real => "real", RF.Fake => "fake", _ => null };
    private static RF StrToRf(string? s) => s switch { "real" => RF.Real, "fake" => RF.Fake, _ => RF.None };
    private static string? TypeToStr(FloorType v) => v switch { FloorType.Inferno => "inferno", FloorType.Tsunami => "tsunami", _ => null };
    private static FloorType StrToType(string? s) => s switch { "inferno" => FloorType.Inferno, "tsunami" => FloorType.Tsunami, _ => FloorType.None };

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
        CancelCurrentConnection();
        RoomId = null;
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
