using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace XIVRaidToolsPlugin;

// Mirrors the `S` object and calc*/floorAoe helpers in kefka-says/app.js.
// Field names intentionally match the JS SYNC_KEYS so Serialize/ApplyRemote
// below stay a flat 1:1 mapping.
public enum RF { None, Real, Fake }
public enum Pos { None, Water, Lightning }
public enum FloorType { None, Inferno, Tsunami }

// A snapshot of one pull's mechanic state, taken right before Reset() clears
// it - lets a misclick be undone, or an earlier pull's calls be reviewed.
// Local-only (like G1Pos/G2Pos/G1Accel/G2Accel - see ClearLocalDebuffs),
// not synced: each client's own perspective, including its own personal
// debuff pick, not something to push onto teammates.
public sealed record PullSnapshot(
    DateTime Timestamp,
    RF G1Rf, Pos G1Pos, bool G1Accel,
    RF G2Rf, Pos G2Pos, bool G2Accel,
    FloorType It1Type, RF It1Rf, RF It2Rf,
    RF ThunderRf, RF BlizzardRf)
{
    // One-line summary for the pull history popup - only mentions fields
    // that were actually set, so an early-pull-wipe snapshot (say, only GCO1
    // called before a reset) doesn't read as a wall of "None"s.
    public string Describe()
    {
        var parts = new List<string>();

        void AddGco(int n, RF rf, Pos pos, bool accel)
        {
            if (rf == RF.None && pos == Pos.None && !accel) return;
            var bits = new List<string>();
            if (rf != RF.None) bits.Add(rf.ToString());
            if (pos != Pos.None) bits.Add(pos.ToString());
            if (accel) bits.Add("Accel");
            parts.Add($"GCO{n}: {string.Join(" ", bits)}");
        }

        AddGco(1, G1Rf, G1Pos, G1Accel);
        AddGco(2, G2Rf, G2Pos, G2Accel);
        if (It1Type != FloorType.None || It1Rf != RF.None)
            parts.Add($"Floor: {It1Type} {It1Rf}".TrimEnd());
        if (ThunderRf != RF.None) parts.Add($"Thunder: {ThunderRf}");
        if (BlizzardRf != RF.None) parts.Add($"Blizzard: {BlizzardRf}");

        return parts.Count > 0 ? string.Join(" | ", parts) : "(empty)";
    }
}

public sealed class MechState : ISyncedState
{
    private const int MaxPullHistory = 20;

    // Most recent first. Only ever grows on a non-empty Reset() (see
    // IsEmpty) - spamming Reset on an already-cleared state shouldn't fill
    // this with blank entries.
    public List<PullSnapshot> PullHistory { get; } = new();

    public void ClearHistory() => PullHistory.Clear();
    public RF G1Rf, G2Rf, It1Rf, It2Rf, ThunderRf, BlizzardRf;
    public Pos G1Pos, G2Pos;
    public bool G1Accel, G2Accel;
    public FloorType It1Type;
    public bool EnforceOrder;

    // it2type() in app.js
    public FloorType It2Type => It1Type switch
    {
        FloorType.Inferno => FloorType.Tsunami,
        FloorType.Tsunami => FloorType.Inferno,
        _ => FloorType.None,
    };

    // calcSpread()
    public bool? Spread()
    {
        static bool? Chk(Pos pos, RF rf)
        {
            if (pos == Pos.None || rf == RF.None) return null;
            if (pos == Pos.Water) return rf == RF.Fake;
            if (pos == Pos.Lightning) return rf == RF.Real;
            return null;
        }

        var s1 = Chk(G1Pos, G1Rf);
        var s2 = Chk(G2Pos, G2Rf);
        if (s1 == true || s2 == true) return true;
        if (s1 == false || s2 == false) return false;
        return null;
    }

    // calcAccel()
    public string? Accel()
    {
        var rf = G1Accel ? G1Rf : RF.None;
        if (rf == RF.None) rf = G2Accel ? G2Rf : RF.None;
        return rf == RF.None ? null : rf == RF.Real ? "still" : "move";
    }

    // floorAoe(type, rf)
    public static string? FloorAoe(FloorType type, RF rf)
    {
        if (type == FloorType.None || rf == RF.None) return null;
        return type == FloorType.Inferno
            ? (rf == RF.Real ? "circle" : "donut")
            : (rf == RF.Real ? "donut" : "circle");
    }

    // setPos(gco, v) - Grand Cross Omega pairs the two targets: exactly one
    // gets a positional debuff (water/lightning), the other gets Accel Bomb.
    // Assigning a position to one player forces the other into accel and
    // clears their position.
    public void SetPos(int gco, Pos v)
    {
        if (gco == 1)
        {
            G1Pos = G1Pos == v ? Pos.None : v;
            if (G1Pos != Pos.None) { G1Accel = false; G2Accel = true; G2Pos = Pos.None; }
        }
        else
        {
            G2Pos = G2Pos == v ? Pos.None : v;
            if (G2Pos != Pos.None) { G2Accel = false; G1Accel = true; G1Pos = Pos.None; }
        }
    }

    // togAccel(gco) - at most one player has Accel Bomb at a time; taking it
    // clears your own position debuff.
    public void ToggleAccel(int gco)
    {
        if (gco == 1)
        {
            G1Accel = !G1Accel;
            if (G1Accel) { G1Pos = Pos.None; G2Accel = false; }
        }
        else
        {
            G2Accel = !G2Accel;
            if (G2Accel) { G2Pos = Pos.None; G1Accel = false; }
        }
    }

    // Whether every mechanic-wide and personal field is at its default.
    // EnforceOrder deliberately excluded, it's a standing preference, not
    // per-pull state. Used to skip saving a PullHistory entry for a reset
    // that had nothing to lose (e.g. a second accidental click on Reset).
    public bool IsEmpty =>
        G1Rf == RF.None && G2Rf == RF.None && It1Rf == RF.None && It2Rf == RF.None &&
        ThunderRf == RF.None && BlizzardRf == RF.None && It1Type == FloorType.None &&
        G1Pos == Pos.None && G2Pos == Pos.None && !G1Accel && !G2Accel;

    // reset() - clears everything except enforceOrder; accel fields go back
    // to false, everything else back to None. Snapshots the pre-reset state
    // into PullHistory first so it can be restored later (see PullSnapshot).
    // Returns that snapshot (or null if there was nothing to save) so the
    // caller can also push it to the rest of the room via SerializeSnapshot
    // (see KefkaSaysWindow's Reset handling) - otherwise only the client
    // that actually hit Reset would ever get a PullHistory entry for this
    // pull.
    public PullSnapshot? Reset()
    {
        PullSnapshot? snapshot = null;
        if (!IsEmpty)
        {
            snapshot = new PullSnapshot(
                DateTime.Now, G1Rf, G1Pos, G1Accel, G2Rf, G2Pos, G2Accel,
                It1Type, It1Rf, It2Rf, ThunderRf, BlizzardRf);
            AddRemoteHistorySnapshot(snapshot);
        }

        G1Rf = G2Rf = It1Rf = It2Rf = ThunderRf = BlizzardRf = RF.None;
        ClearLocalDebuffs();
        It1Type = FloorType.None;
        return snapshot;
    }

    // Records a PullHistory entry for a Reset() that happened on ANOTHER
    // client - see ApplyRemote below. Same insert-at-front-and-trim as
    // Reset() takes for its own snapshot above, just without touching this
    // client's live mechanic fields.
    public void AddRemoteHistorySnapshot(PullSnapshot snap)
    {
        PullHistory.Insert(0, snap);
        if (PullHistory.Count > MaxPullHistory)
            PullHistory.RemoveRange(MaxPullHistory, PullHistory.Count - MaxPullHistory);
    }

    // Repopulates every field from a past snapshot - the inverse of the copy
    // Reset() takes. Callers still need to PushState() afterward to sync the
    // shared fields to the room, same as any other mutation.
    public void RestoreSnapshot(PullSnapshot snap)
    {
        G1Rf = snap.G1Rf; G1Pos = snap.G1Pos; G1Accel = snap.G1Accel;
        G2Rf = snap.G2Rf; G2Pos = snap.G2Pos; G2Accel = snap.G2Accel;
        It1Type = snap.It1Type; It1Rf = snap.It1Rf; It2Rf = snap.It2Rf;
        ThunderRf = snap.ThunderRf; BlizzardRf = snap.BlizzardRf;
    }

    // G1Pos/G2Pos/G1Accel/G2Accel are deliberately NOT part of Serialize's
    // synced fields below - each player's own Grand Cross Omega debuff
    // assignment is personal, not a shared raid-wide fact like g1rf/g2rf, so
    // one client's selection must never overwrite another's. But that means
    // a normal state sync from another client's Reset() never touched these
    // fields either, leaving a half-reset: their Real/Fake cleared, but
    // Water/Lightning/Accel Bomb still showing selected. The Reset button's
    // handler attaches a "clearDebuffs" flag onto that same push specifically
    // for this reason (see KefkaSaysWindow's Reset handling and ApplyRemote
    // below) - it's still each client clearing its own local fields, never
    // one client's values pushed onto another.
    public void ClearLocalDebuffs()
    {
        G1Pos = G2Pos = Pos.None;
        G1Accel = G2Accel = false;
    }

    // ── ISyncedState ─────────────────────────────────────────────────────
    // Mechanic-wide fields only; player-specific debuffs (G1/G2 Pos/Accel)
    // stay local - mirrors app.js's SYNC_KEYS/sharedState() exactly.
    public JsonObject Serialize() => new()
    {
        ["g1rf"] = RfToStr(G1Rf),
        ["g2rf"] = RfToStr(G2Rf),
        ["it1type"] = TypeToStr(It1Type),
        ["it1rf"] = RfToStr(It1Rf),
        ["it2rf"] = RfToStr(It2Rf),
        ["thunderRF"] = RfToStr(ThunderRf),
        ["blizzardRF"] = RfToStr(BlizzardRf),
        ["enforceOrder"] = EnforceOrder,
    };

    // When a field was last set by an INCOMING remote update (see
    // ApplyRemote) - consulted by KefkaSaysWindow's click handlers before
    // toggling a synced field off, to stop a race where two people click
    // the same correct call at nearly the same instant: if A's click's
    // broadcast reaches B right before B's own (independent, already
    // in-flight) click on the same value registers, naive toggle-off logic
    // reads "already set to what I clicked" and clears it, undoing A's call
    // out from under them. Mirrors kefka-says/app.js's _remoteSetAt.
    private readonly Dictionary<string, DateTime> _remoteSetAt = new();
    private const int RaceWindowMs = 400;

    public bool WasJustSetRemotely(string field) =>
        _remoteSetAt.TryGetValue(field, out var at) && (DateTime.UtcNow - at).TotalMilliseconds < RaceWindowMs;

    public void ApplyRemote(JsonObject state)
    {
        // Must check ContainsKey, not `state["k"] is {}` - JsonObject's indexer
        // returns null both for an absent key AND a key explicitly set to JSON
        // null, so an `is {}` check can't tell "not sent" apart from "cleared".
        // app.js's applyShared avoids this with `if (k in state)`; we need the
        // same distinction or Reset()/unsetting a field never reaches peers.
        var now = DateTime.UtcNow;
        if (state.ContainsKey("g1rf")) { G1Rf = StrToRf(state["g1rf"]?.GetValue<string>()); _remoteSetAt["g1rf"] = now; }
        if (state.ContainsKey("g2rf")) { G2Rf = StrToRf(state["g2rf"]?.GetValue<string>()); _remoteSetAt["g2rf"] = now; }
        if (state.ContainsKey("it1type")) { It1Type = StrToType(state["it1type"]?.GetValue<string>()); _remoteSetAt["it1type"] = now; }
        if (state.ContainsKey("it1rf")) { It1Rf = StrToRf(state["it1rf"]?.GetValue<string>()); _remoteSetAt["it1rf"] = now; }
        if (state.ContainsKey("it2rf")) { It2Rf = StrToRf(state["it2rf"]?.GetValue<string>()); _remoteSetAt["it2rf"] = now; }
        if (state.ContainsKey("thunderRF")) { ThunderRf = StrToRf(state["thunderRF"]?.GetValue<string>()); _remoteSetAt["thunderRF"] = now; }
        if (state.ContainsKey("blizzardRF")) { BlizzardRf = StrToRf(state["blizzardRF"]?.GetValue<string>()); _remoteSetAt["blizzardRF"] = now; }
        if (state.ContainsKey("enforceOrder")) EnforceOrder = state["enforceOrder"]?.GetValue<bool>() ?? false;

        // See ClearLocalDebuffs's comment - clears THIS client's own
        // local-only debuff fields in response to another client's Reset,
        // never applies someone else's specific selection to us.
        if (state.ContainsKey("clearDebuffs")) ClearLocalDebuffs();

        // A PullHistory entry from whoever's Reset() this state message
        // came from - see SerializeSnapshot and KefkaSaysWindow's Reset
        // handling (the configureExtra callback that attaches it).
        if (state["historySnapshot"]?.AsObject() is { } snap)
            AddRemoteHistorySnapshot(DeserializeSnapshot(snap));
    }

    // Mirrors app.js's snapshotState()'s field set exactly (including the
    // personal g1pos/g2pos/g1accel/g2accel fields - a history entry is a
    // record of what actually happened that pull, not a live-synced field,
    // so it's fine for it to carry the resetting player's own debuff pick
    // the way Serialize() above never would).
    public static JsonObject SerializeSnapshot(PullSnapshot snap) => new()
    {
        ["timestamp"] = new DateTimeOffset(snap.Timestamp.ToUniversalTime()).ToUnixTimeMilliseconds(),
        ["g1rf"] = RfToStr(snap.G1Rf),
        ["g1pos"] = PosToStr(snap.G1Pos),
        ["g1accel"] = snap.G1Accel,
        ["it1type"] = TypeToStr(snap.It1Type),
        ["it1rf"] = RfToStr(snap.It1Rf),
        ["g2rf"] = RfToStr(snap.G2Rf),
        ["g2pos"] = PosToStr(snap.G2Pos),
        ["g2accel"] = snap.G2Accel,
        ["it2rf"] = RfToStr(snap.It2Rf),
        ["thunderRF"] = RfToStr(snap.ThunderRf),
        ["blizzardRF"] = RfToStr(snap.BlizzardRf),
    };

    private static PullSnapshot DeserializeSnapshot(JsonObject snap) => new(
        Timestamp: snap["timestamp"] is { } ts ? DateTimeOffset.FromUnixTimeMilliseconds(ts.GetValue<long>()).LocalDateTime : DateTime.Now,
        G1Rf: StrToRf(snap["g1rf"]?.GetValue<string>()), G1Pos: StrToPos(snap["g1pos"]?.GetValue<string>()), G1Accel: snap["g1accel"]?.GetValue<bool>() ?? false,
        G2Rf: StrToRf(snap["g2rf"]?.GetValue<string>()), G2Pos: StrToPos(snap["g2pos"]?.GetValue<string>()), G2Accel: snap["g2accel"]?.GetValue<bool>() ?? false,
        It1Type: StrToType(snap["it1type"]?.GetValue<string>()), It1Rf: StrToRf(snap["it1rf"]?.GetValue<string>()), It2Rf: StrToRf(snap["it2rf"]?.GetValue<string>()),
        ThunderRf: StrToRf(snap["thunderRF"]?.GetValue<string>()), BlizzardRf: StrToRf(snap["blizzardRF"]?.GetValue<string>()));

    private static string? RfToStr(RF v) => v switch { RF.Real => "real", RF.Fake => "fake", _ => null };
    private static RF StrToRf(string? s) => s switch { "real" => RF.Real, "fake" => RF.Fake, _ => RF.None };
    private static string? TypeToStr(FloorType v) => v switch { FloorType.Inferno => "inferno", FloorType.Tsunami => "tsunami", _ => null };
    private static FloorType StrToType(string? s) => s switch { "inferno" => FloorType.Inferno, "tsunami" => FloorType.Tsunami, _ => FloorType.None };
    // Only needed for PullSnapshot (de)serialization - Pos is never part of
    // Serialize/ApplyRemote's live-synced fields (see their comments).
    private static string? PosToStr(Pos v) => v switch { Pos.Water => "water", Pos.Lightning => "lightning", _ => null };
    private static Pos StrToPos(string? s) => s switch { "water" => Pos.Water, "lightning" => Pos.Lightning, _ => Pos.None };
}
