using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace XIVRaidToolsPlugin.Windows;

// A popout docked to the right of KefkaSaysWindow rather than a dropdown
// popup — a popup closes the instant focus moves back to the main window
// (e.g. to click something while comparing an old pull), which defeats the
// point of a reference list. A real Window has no such auto-close and gets
// its own scrollbar for free once PullHistory grows past the window height.
public sealed class PullHistoryWindow : Window
{
    private readonly SessionClient<MechState> _session;
    private readonly KefkaSaysWindow _anchor;

    public PullHistoryWindow(SessionClient<MechState> session, KefkaSaysWindow anchor) : base("Pull History##XrtHistory")
    {
        _session = session;
        _anchor = anchor;
        Size = new Vector2(340, 420);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    // Snaps to the main window's current right edge every frame — anchor's
    // own Draw() (which runs first, see Plugin's AddWindow order) refreshes
    // CurrentPos/CurrentSize each frame, so this always reads this frame's
    // position, not last frame's (no drag lag).
    public override void PreDraw()
    {
        Theme.PushWindowChrome();

        Position = new Vector2(_anchor.CurrentPos.X + _anchor.CurrentSize.X + 8f, _anchor.CurrentPos.Y);
        PositionCondition = ImGuiCond.Always;

        // Closing the main window makes this popout meaningless to keep
        // open (and its anchor position stale) — follow it shut.
        if (!_anchor.IsOpen) IsOpen = false;
    }

    public override void PostDraw() => Theme.PopWindowChrome();

    public override void Draw()
    {
        var s = _session.State;

        // Right-aligned, same technique as the main window's Reset/History
        // buttons (CalcTextSize + GetContentRegionAvail).
        const string clearLabel = "Clear";
        var clearW = ImGui.CalcTextSize(clearLabel).X + ImGui.GetStyle().FramePadding.X * 2;
        ImGui.SameLine(ImGui.GetContentRegionAvail().X - clearW + ImGui.GetCursorPosX());
        ImGui.BeginDisabled(s.PullHistory.Count == 0);
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.Fake);
        var clear = ImGui.Button(clearLabel);
        ImGui.PopStyleColor();
        ImGui.EndDisabled();
        if (clear) s.ClearHistory();
        ImGui.Separator();

        if (s.PullHistory.Count == 0)
        {
            ImGui.TextColored(Theme.TextDim, "No saved pulls yet.");
            return;
        }

        ImGui.BeginChild("HistoryScroll", Vector2.Zero, false);
        for (var i = 0; i < s.PullHistory.Count; i++)
        {
            var snap = s.PullHistory[i];
            ImGui.PushID(i);
            ImGui.TextColored(Theme.TextDim, snap.Timestamp.ToString("HH:mm:ss"));
            ImGui.PushTextWrapPos(ImGui.GetContentRegionAvail().X + ImGui.GetCursorPosX());
            ImGui.TextUnformatted(snap.Describe());
            ImGui.PopTextWrapPos();
            if (ImGui.Button("Restore"))
            {
                s.RestoreSnapshot(snap);
                _session.PushState();
            }
            if (i < s.PullHistory.Count - 1) ImGui.Separator();
            ImGui.PopID();
        }
        ImGui.EndChild();
    }
}
