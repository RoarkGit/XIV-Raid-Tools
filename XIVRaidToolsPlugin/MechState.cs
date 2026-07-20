using System;
using System.Collections.Generic;

namespace XIVRaidToolsPlugin;

// Mirrors the `S` object and calc*/floorAoe helpers in kefka-says/app.js.
// Field names intentionally match the JS SYNC_KEYS so SessionClient's
// (de)serialization stays a flat 1:1 mapping.
public enum RF { None, Real, Fake }
public enum Pos { None, Water, Lightning }
public enum FloorType { None, Inferno, Tsunami }

// A snapshot of one pull's mechanic state, taken right before Reset() clears
// it — lets a misclick be undone, or an earlier pull's calls be reviewed.
// Local-only (like G1Pos/G2Pos/G1Accel/G2Accel — see ClearLocalDebuffs),
// not synced: each client's own perspective, including its own personal
// debuff pick, not something to push onto teammates.
public sealed record PullSnapshot(
    DateTime Timestamp,
    RF G1Rf, Pos G1Pos, bool G1Accel,
    RF G2Rf, Pos G2Pos, bool G2Accel,
    FloorType It1Type, RF It1Rf, RF It2Rf,
    RF ThunderRf, RF BlizzardRf)
{
    // One-line summary for the pull history popup — only mentions fields
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

public sealed class MechState
{
    private const int MaxPullHistory = 20;

    // Most recent first. Only ever grows on a non-empty Reset() (see
    // IsEmpty) — spamming Reset on an already-cleared state shouldn't fill
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

    // setPos(gco, v) — Grand Cross Omega pairs the two targets: exactly one
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

    // togAccel(gco) — at most one player has Accel Bomb at a time; taking it
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

    // Whether every mechanic-wide and personal field is at its default —
    // EnforceOrder deliberately excluded, it's a standing preference, not
    // per-pull state. Used to skip saving a PullHistory entry for a reset
    // that had nothing to lose (e.g. a second accidental click on Reset).
    public bool IsEmpty =>
        G1Rf == RF.None && G2Rf == RF.None && It1Rf == RF.None && It2Rf == RF.None &&
        ThunderRf == RF.None && BlizzardRf == RF.None && It1Type == FloorType.None &&
        G1Pos == Pos.None && G2Pos == Pos.None && !G1Accel && !G2Accel;

    // reset() — clears everything except enforceOrder; accel fields go back
    // to false, everything else back to None. Snapshots the pre-reset state
    // into PullHistory first so it can be restored later (see PullSnapshot).
    public void Reset()
    {
        if (!IsEmpty)
        {
            PullHistory.Insert(0, new PullSnapshot(
                DateTime.Now, G1Rf, G1Pos, G1Accel, G2Rf, G2Pos, G2Accel,
                It1Type, It1Rf, It2Rf, ThunderRf, BlizzardRf));
            if (PullHistory.Count > MaxPullHistory)
                PullHistory.RemoveRange(MaxPullHistory, PullHistory.Count - MaxPullHistory);
        }

        G1Rf = G2Rf = It1Rf = It2Rf = ThunderRf = BlizzardRf = RF.None;
        ClearLocalDebuffs();
        It1Type = FloorType.None;
    }

    // Repopulates every field from a past snapshot — the inverse of the copy
    // Reset() takes. Callers still need to PushState() afterward to sync the
    // shared fields to the room, same as any other mutation.
    public void RestoreSnapshot(PullSnapshot snap)
    {
        G1Rf = snap.G1Rf; G1Pos = snap.G1Pos; G1Accel = snap.G1Accel;
        G2Rf = snap.G2Rf; G2Pos = snap.G2Pos; G2Accel = snap.G2Accel;
        It1Type = snap.It1Type; It1Rf = snap.It1Rf; It2Rf = snap.It2Rf;
        ThunderRf = snap.ThunderRf; BlizzardRf = snap.BlizzardRf;
    }

    // G1Pos/G2Pos/G1Accel/G2Accel are deliberately NOT part of SessionClient's
    // synced fields (see its SYNC_KEYS comment) — each player's own Grand
    // Cross Omega debuff assignment is personal, not a shared raid-wide fact
    // like g1rf/g2rf, so one client's selection must never overwrite
    // another's. But that means a normal state sync from another client's
    // Reset() never touched these fields either, leaving a half-reset: their
    // Real/Fake cleared, but Water/Lightning/Accel Bomb still showing
    // selected. SessionClient.PushReset() calls this on every receiving
    // client specifically for that reason — it's still each client clearing
    // its own local fields, never one client's values pushed onto another.
    public void ClearLocalDebuffs()
    {
        G1Pos = G2Pos = Pos.None;
        G1Accel = G2Accel = false;
    }
}
