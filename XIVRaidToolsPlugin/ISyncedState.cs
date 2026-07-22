using System.Text.Json.Nodes;

namespace XIVRaidToolsPlugin;

// Implemented by a tool's own state object (MechState today) to plug into
// SessionClient<TState>'s generic room-sync machinery, mirroring the
// getSharedState/onStateReceived split kefka-says/session.js uses on the
// webapp side - SessionClient knows how to connect/create/join/reconnect,
// but has zero knowledge of any tool's actual mechanic fields.
public interface ISyncedState
{
    // This tool's own synced fields (the "SYNC_KEYS" subset) - called fresh
    // on every push, since it reads current values off the implementer.
    JsonObject Serialize();

    // Applies an incoming state payload to local state: this tool's own
    // synced fields, plus whatever extra one-shot keys it chooses to
    // recognize (Kefka's clearDebuffs/historySnapshot, say - see
    // SessionClient<TState>.PushState's configureExtra parameter for how
    // those get attached on the sending side).
    void ApplyRemote(JsonObject state);
}
