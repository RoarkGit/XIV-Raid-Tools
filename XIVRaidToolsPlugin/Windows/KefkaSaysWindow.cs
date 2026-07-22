using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace XIVRaidToolsPlugin.Windows;

public sealed class KefkaSaysWindow : Window
{
    private const float ButtonGap = 5f;

    // Gap between the status column's three GroupCards (Gaze/Floor AOE/
    // Thunder & Blizzard) — tuned in-game via a temporary debug +/- control
    // to make the status column's total height match the input column's.
    private const float StatusCardGap = 7f;

    private readonly SessionClient _session;
    private readonly GameIcons _gameIcons;
    private string _roomInput = "";

    // Assigned by Plugin right after both windows are constructed (a
    // constructor parameter would need each to already exist for the
    // other's). PullHistoryWindow reads CurrentPos/CurrentSize below to
    // anchor itself just right of this window (see its PreDraw).
    public PullHistoryWindow? HistoryWindow { private get; set; }

    // Window.Position/Size are only the *requested* placement (unset here,
    // since this window floats free/draggable), so the actual on-screen
    // rect has to come from ImGui itself, captured while this window is the
    // current one (i.e. inside Draw()).
    public Vector2 CurrentPos { get; private set; }
    public Vector2 CurrentSize { get; private set; }

    // Fixed at 1 — the webapp uses fixed pixel sizes, so we don't scale with
    // the window. Kept (rather than deleting Sc/ScaledText) so the layout
    // dimensions stay written as design pixels in one place.
    private readonly float _uiScale = 1f;
    private float Sc(float px) => px * _uiScale;

    public KefkaSaysWindow(SessionClient session, GameIcons gameIcons) : base("Kefka Says##KefkaSaysMain")
    {
        _session = session;
        _gameIcons = gameIcons;
        // Plain resizable window with a generous initial size (remembered
        // after the user's first manual resize via FirstUseEver). The two
        // columns matching each other doesn't need the window itself to be
        // fixed-size — TableSetColumnIndex measures both columns' true
        // height within the same frame regardless of window size, so
        // resizing doesn't reintroduce the scale/cache feedback loop from
        // earlier attempts. AlwaysAutoResize was considered, but it fights
        // the table's stretchy (fill-available-width) columns — content that
        // sizes itself to "available width" has no natural width for
        // auto-resize to converge on, risking a collapsed or unstable size.
        // Smaller default now that padding/gaps/icons are tightened —
        // in-game HUD real estate is precious, this was too big before.
        Size = new Vector2(680, 560);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void PreDraw() => Theme.PushWindowChrome();
    public override void PostDraw() => Theme.PopWindowChrome();

    public override void Draw()
    {
        CurrentPos = ImGui.GetWindowPos();
        CurrentSize = ImGui.GetWindowSize();

        // The webapp does NOT scale with window size: every font, icon and
        // column is a fixed pixel size, capped by `.page { max-width: 980px;
        // margin: 0 auto }` and centered, with a scrollbar/stack when the
        // viewport is smaller. So we do the same — fixed sizes (Sc() is now a
        // no-op at scale 1), content capped and centered, ImGui's own
        // scrollbar handling small windows. Trying to auto-scale everything
        // to the window created a scale↔content-height feedback loop that
        // vibrated; a fixed layout has no such loop.

        // Window-wide defaults matching .btn's base state and body text color
        // in index.html; per-selected-button accents are pushed on top of
        // these by AccentButton, same layering as the CSS's .btn.on-* rules
        // overriding .btn's base background/text/border. FramePadding wider
        // than ImGui's default to match .btn-group .btn's 13px/8px padding
        // (chunkier than a stock ImGui button).
        ImGui.PushStyleColor(ImGuiCol.Button, Theme.BtnBg);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Theme.BtnHover);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, Theme.BtnHover);
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextDim);
        ImGui.PushStyleColor(ImGuiCol.Border, Theme.Border);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(Sc(6), Sc(6)));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(Sc(6), Sc(4)));
        ImGui.SetWindowFontScale(_uiScale); // base text scale; ScaledText composes on top
        try
        {
            DrawTopBar();
            ImGui.Separator();

            // Mirrors .main-card's two flex columns: .col-inputs beside
            // .col-status. The webapp's .col-inputs is a fixed 410px because
            // it holds full-width text buttons; ours holds compact icon
            // buttons with no row labels at all now. Sized to fit whichever
            // is wider: the "Debuffs" row's 3 buttons, or the Thunder/
            // Blizzard panes side by side (each needs room for its own
            // "THUNDER"/"BLIZZARD" header plus 2 buttons plus the pane's own
            // padding) — Thunder & Blizzard turned out to need more room
            // than 3 plain buttons did once each got its own bordered pane.
            var contentW = ImGui.GetContentRegionAvail().X;
            var btnSize = ImGui.GetFrameHeight() + Sc(18f);
            var debuffsRowW = 3 * btnSize + 2 * Sc(ButtonGap) + Sc(12f);

            ImGui.SetWindowFontScale(_uiScale * LabelScale);
            var paneLabelW = MathF.Max(ImGui.CalcTextSize("THUNDER").X, ImGui.CalcTextSize("BLIZZARD").X);
            ImGui.SetWindowFontScale(_uiScale);
            var paneButtonsW = 2 * btnSize + Sc(ButtonGap);
            var paneW = MathF.Max(paneLabelW, paneButtonsW) + 2 * Sc(CardPadX);
            var thunderBlizzardW = 2 * paneW + Sc(16f);

            var inputsW = MathF.Max(debuffsRowW, thunderBlizzardW);

            if (ImGui.BeginTable("main-layout", 2, ImGuiTableFlags.BordersInnerV, new Vector2(contentW, 0f)))
            {
                ImGui.TableSetupColumn("inputs", ImGuiTableColumnFlags.WidthFixed, inputsW);
                ImGui.TableSetupColumn("status", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableNextRow();

                // .main-card align-items:stretch — both columns the same
                // height, computed with ZERO cross-frame guessing: draw the
                // status column FIRST (out of visual order, via
                // TableSetColumnIndex) so its true height is known within
                // THIS frame, then draw input and pad whichever one is
                // shorter against the other's now-known height. The previous
                // approach cached last frame's height to decide this frame's
                // padding — a one-frame lag that was a plausible source of
                // the mismatches we kept seeing. This has no lag: both
                // heights are real numbers from the current frame before any
                // padding decision is made.
                ImGui.TableSetColumnIndex(1);
                var sy0 = ImGui.GetCursorPosY();
                DrawStatusColumn();
                var statusH = ImGui.GetCursorPosY() - sy0;
                // Recorded explicitly rather than trusting TableSetColumnIndex
                // to restore column 1's cursor to wherever DrawStatusColumn
                // left it when we jump back below — a persistent few-pixel
                // mismatch survived several rounds of size tuning, which
                // pointed at this assumption rather than the sizes themselves.
                var statusEndPos = ImGui.GetCursorScreenPos();

                ImGui.TableSetColumnIndex(0);
                var iy0 = ImGui.GetCursorPosY();
                DrawInputsColumn();
                var inputH = ImGui.GetCursorPosY() - iy0;

                if (inputH < statusH)
                    ImGui.Dummy(new Vector2(0, statusH - inputH));
                else if (statusH < inputH)
                {
                    ImGui.TableSetColumnIndex(1);
                    ImGui.SetCursorScreenPos(statusEndPos);
                    ImGui.Dummy(new Vector2(0, inputH - statusH));
                }

                ImGui.EndTable();
            }
        }
        finally
        {
            ImGui.SetWindowFontScale(1f);
            ImGui.PopStyleVar(2);
            ImGui.PopStyleColor(5);
        }
    }

    // Left column. Section order matches the HTML exactly: GCO#1, Floor
    // AOE#1, GCO#2, Floor AOE#2, Thunder & Blizzard — app.js interleaves
    // these rather than grouping both GCOs together. Drawn at natural height;
    // the caller (Draw) appends a trailing spacer if this column comes up
    // shorter than the status column, so both end at the same Y.
    private void DrawInputsColumn()
    {
        DrawGrandCross(1);
        ImGui.Separator();
        DrawFloorAoeSection(1);
        ImGui.Separator();
        DrawGrandCross(2);
        ImGui.Separator();
        DrawFloorAoeSection(2);
        ImGui.Separator();
        DrawThunderBlizzard();
    }

    // Session controls, the enforce-order checkbox, and Reset all share one
    // top bar now instead of two stacked rows — the session bar's own
    // controls (Create/Join or Room/Leave) are short, so there was no need
    // for "Enforce mechanic order" to have a whole row to itself below them.
    private void DrawTopBar()
    {
        var s = _session.State;
        switch (_session.Status)
        {
            case SessionStatus.Active:
                ImGui.AlignTextToFramePadding();
                ImGui.TextColored(Theme.Text, $"Room: {_session.RoomId}");
                ImGui.SameLine();
                ImGui.TextColored(Theme.TextDim, $"· {_session.ConnectedCount} connected");
                ImGui.SameLine();
                if (ImGui.Button("Leave")) _session.Leave();
                break;

            case SessionStatus.Connecting:
                ImGui.AlignTextToFramePadding();
                ImGui.TextColored(Theme.TextDim, "Connecting...");
                ImGui.SameLine();
                if (ImGui.Button("Cancel")) _session.Leave();
                break;

            case SessionStatus.Reconnecting:
                ImGui.AlignTextToFramePadding();
                ImGui.TextColored(Theme.TextDim, $"Reconnecting to {_session.RoomId}...");
                ImGui.SameLine();
                if (ImGui.Button("Give up")) _session.Leave();
                break;

            case SessionStatus.Idle:
            default:
                if (ImGui.Button("Create session")) _ = _session.CreateAsync();
                ImGui.SameLine();
                ImGui.SetNextItemWidth(60);
                // EnterReturnsTrue mirrors the webapp's room-input onkeydown
                // handler (index.html) — Enter joins the same as clicking Join.
                var enterPressed = ImGui.InputTextWithHint("##room", "ABCD", ref _roomInput, 4,
                    ImGuiInputTextFlags.CharsUppercase | ImGuiInputTextFlags.EnterReturnsTrue);
                ImGui.SameLine();
                var joinClicked = ImGui.Button("Join");
                if ((enterPressed || joinClicked) && _roomInput.Length == 4) _ = _session.JoinAsync(_roomInput);
                break;
        }

        ImGui.SameLine();
        var enforceOrder = s.EnforceOrder;
        if (ImGui.Checkbox("Enforce order", ref enforceOrder))
        {
            s.EnforceOrder = enforceOrder;
            _session.PushState();
        }

        // .btn-reset sits top-right of .page-header in the webapp; right-align
        // it (and History, right next to it) here rather than tack them onto
        // this row's natural flow.
        var resetLabel = "Reset";
        var resetW = ImGui.CalcTextSize(resetLabel).X + ImGui.GetStyle().FramePadding.X * 2;
        var historyLabel = $"History ({s.PullHistory.Count})";
        var historyW = ImGui.CalcTextSize(historyLabel).X + ImGui.GetStyle().FramePadding.X * 2;
        var gap = ImGui.GetStyle().ItemSpacing.X;

        ImGui.SameLine(ImGui.GetContentRegionAvail().X - historyW - gap - resetW + ImGui.GetCursorPosX());
        ImGui.BeginDisabled(s.PullHistory.Count == 0);
        if (ImGui.Button(historyLabel) && HistoryWindow is { } hw) hw.IsOpen = !hw.IsOpen;
        ImGui.EndDisabled();

        ImGui.SameLine();
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.Fake);
        var reset = ImGui.Button(resetLabel);
        ImGui.PopStyleColor();
        if (reset)
        {
            s.Reset();
            // PushReset, not PushState — Reset needs every other client to
            // also clear its own local-only debuff selections, which a plain
            // state sync never touches (see MechState.ClearLocalDebuffs).
            _session.PushReset();
        }
    }

    // Position/accel mutations go through MechState.SetPos/ToggleAccel so the
    // cross-player exclusion rules (see MechState.cs) apply the same way
    // they do in app.js's setPos()/togAccel(). Row disabling below mirrors
    // app.js's g1Order/g1PosOrder (enforceOrder) exactly — only GCO#2 is
    // ever gated, GCO#1 never is.
    private void DrawGrandCross(int gco)
    {
        var s = _session.State;
        var rf = gco == 1 ? s.G1Rf : s.G2Rf;
        var pos = gco == 1 ? s.G1Pos : s.G2Pos;
        var accel = gco == 1 ? s.G1Accel : s.G2Accel;

        bool rfDisabled = false, posDisabled = false, accelDisabled = false, g1PosOrder = false;
        if (gco == 2)
        {
            var g1Done = s.G1Pos != Pos.None || s.G1Accel;
            var g1Order = s.EnforceOrder && s.G1Rf == RF.None;
            g1PosOrder = s.EnforceOrder && !g1Done;
            rfDisabled = g1Order;
            posDisabled = s.G2Accel || g1PosOrder;
            accelDisabled = s.G2Pos != Pos.None || s.G1Accel || g1PosOrder;
        }

        SectionLabel($"Grand Cross Omega #{gco}");

        CastRow($"gco{gco}cast", rf, AccentTag.Real, rfDisabled, "Set Grand Cross Omega #1 Cast first",
            v => SetRf(gco, v));

        // Water/Lightning/Accel Bomb aren't a plain 2-way toggle (SetPos and
        // ToggleAccel carry the cross-player exclusion rules), so DebuffsRow
        // calls straight into the model method per button instead of a
        // click-to-clear closure. Water/Lightning and Accel Bomb also have
        // distinct Disabled conditions (mutual exclusion with a sibling
        // button vs. the enforceOrder gate), not one shared flag.
        DebuffsRow(gco, pos, accel, posDisabled, accelDisabled, g1PosOrder,
            "Set Grand Cross Omega #1 Debuffs first");

        void SetRf(int g, RF v)
        {
            var cur = g == 1 ? s.G1Rf : s.G2Rf;
            var next = cur == v ? RF.None : v;
            if (g == 1) s.G1Rf = next; else s.G2Rf = next;
            _session.PushState();
        }
    }

    private void DrawFloorAoeSection(int n)
    {
        var s = _session.State;
        SectionLabel($"Floor AOE #{n}");

        // Real/Fake first, Type second — swapped from app.js's original
        // Type-then-Cast order (index.html was updated to match, since this
        // was a deliberate ordering choice, not just a plugin-side quirk).
        if (n == 1)
        {
            CastRow("floor1cast", s.It1Rf, AccentTag.Real, false, null, SetIt1Rf);
            TypeRow("floor1type", s.It1Type, false, SetType);
        }
        else
        {
            var it1Order = s.EnforceOrder && s.It2Type == FloorType.None;
            CastRow("floor2cast", s.It2Rf, AccentTag.Real, it1Order, "Set Floor AOE #1 Type first", SetIt2Rf);

            // Floor AOE #2's Type row is permanently disabled in app.js too
            // (<button disabled> in the HTML) — it only ever displays the
            // derived It2Type, never an independent choice.
            TypeRow("floor2type", s.It2Type, true, null);
        }

        void SetType(FloorType v) { s.It1Type = s.It1Type == v ? FloorType.None : v; _session.PushState(); }
        void SetIt1Rf(RF v) { s.It1Rf = s.It1Rf == v ? RF.None : v; _session.PushState(); }
        void SetIt2Rf(RF v) { s.It2Rf = s.It2Rf == v ? RF.None : v; _session.PushState(); }
    }

    // Two small bordered panes side by side (reusing the status column's
    // GroupCard for the box/header — nothing about it is status-column
    // specific) instead of one "Thunder & Blizzard" section with "Thunder"/
    // "Blizzard" row labels: each pane's own header already says which
    // element it is, so a per-row label would just repeat it. Needs an
    // actual nested table (not just SameLine) so GroupCard's and CastRow's
    // internal GetContentRegionAvail() calls see the half-width column
    // instead of the whole row.
    private void DrawThunderBlizzard()
    {
        var s = _session.State;
        if (ImGui.BeginTable("thunderblizzard", 2))
        {
            ImGui.TableSetupColumn("thr", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("blz", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableNextRow();

            // Real is tagged with the element's own color (thunder/blizzard),
            // Fake is always tagged red — matches app.js's
            // btnCls('thr',...,'thunder') vs btnCls('thf',...,'fake') asymmetry.
            ImGui.TableNextColumn();
            GroupCard("Thunder", () => CastRow("thunder", s.ThunderRf, AccentTag.Thunder, false, null,
                v => { s.ThunderRf = s.ThunderRf == v ? RF.None : v; _session.PushState(); }));

            ImGui.TableNextColumn();
            GroupCard("Blizzard", () => CastRow("blizzard", s.BlizzardRf, AccentTag.Blizzard, false, null,
                v => { s.BlizzardRf = s.BlizzardRf == v ? RF.None : v; _session.PushState(); }));

            ImGui.EndTable();
        }
    }

    // ImGui has one loaded font size; these approximate index.html's relative
    // scale (11px labels / 13px body / 16-17px values) as multipliers off
    // whatever the base font size actually is, via SetWindowFontScale.
    private const float LabelScale = 11f / 13f;
    private const float ScardValScale = 17f / 13f;
    private const float TbValScale = 16f / 13f;

    // Composes the relative text scale on top of the global _uiScale, and
    // restores to _uiScale (the base for this frame) rather than 1f.
    private void ScaledText(string text, float scale, Vector4 color)
    {
        ImGui.SetWindowFontScale(_uiScale * scale);
        ImGui.TextColored(color, text);
        ImGui.SetWindowFontScale(_uiScale);
    }

    // .section-label: 11px bold uppercase, --text-label color.
    private void SectionLabel(string text) => ScaledText(text.ToUpperInvariant(), LabelScale, Theme.TextLabel);

    // Width to give each of n icon buttons in a row so they stretch to fill
    // whatever's left after the row label (flex:1-equivalent), instead of a
    // fixed small square that leaves the rest of the row's width empty —
    // this was the actual leftover "sparse space" after the input column
    // itself got sized correctly.
    private float RowButtonWidth(int n, float gap) => (ImGui.GetContentRegionAvail().X - gap * (n - 1)) / n;

    // Compact icon-only button — the space-saving swap for what used to be
    // full-width "Real"/"Fake"/"Water"/"Lightning"/"Accel Bomb" text buttons.
    // width/height are independent so a row can stretch button WIDTH to fill
    // the row while the icon itself (sized off height) doesn't get stretched
    // into an odd elongated shape. Tinted per Theme.ButtonAccent when
    // selected like AccentButton, with an optional identifying tooltip
    // (skipped while disabled — the row-level ordering tooltip takes over)
    // that also previews the icon at full size, since a ~20px button icon
    // for a debuff most players have never seen at that size is hard to
    // recognize on its own.
    private bool IconAccentButton(string id, Icon icon, bool selected, AccentTag tag, bool disabled, float width, float height, string? tooltip = null, bool isRealIcon = false)
    {
        var (fg, bg) = selected ? Theme.ButtonAccent(tag) : (Theme.TextDim, Theme.BtnBg);
        ImGui.PushStyleColor(ImGuiCol.Button, bg);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, selected ? bg : Theme.BtnHover);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, bg);
        ImGui.BeginDisabled(disabled);
        var clicked = ImGui.Button($"##{id}", new Vector2(width, height));
        var min = ImGui.GetItemRectMin();
        ImGui.EndDisabled();
        ImGui.PopStyleColor(3);

        // Real game-icon textures (a single flat image, no internal overlap
        // possible) use alpha for the dim/bright distinction — see
        // GameIcons.Draw. Vector Icons.* can't: several of them draw
        // overlapping primitives (Stack's circle over its arrow tips,
        // Droplet/Flame's circle over a triangle), and tinting the whole
        // icon at alpha<1 makes each overlapping pixel blend twice — a
        // visible seam where the shapes cross. So vector icons stay fully
        // opaque and get dim vs. bright entirely from fg's color choice
        // (TextDim vs. the accent) instead.
        var iconColor = isRealIcon ? Theme.U32(fg, selected ? 1f : 0.65f) : Theme.U32(fg);

        // Sized off the SMALLER of width/height (not height alone): a row's
        // buttons often end up wider than tall (RowButtonWidth stretches
        // width to fill the row), and an icon sized only off height then
        // reads as a small square floating in the middle of a much wider
        // button instead of filling it. Target 90% of height (buttons are
        // usually wider than tall here), capped by width so it's still safe
        // if a button ever ends up narrower than it is tall.
        var iconSize = MathF.Min(width, height * 0.9f);
        icon(ImGui.GetWindowDrawList(), new Vector2(min.X + (width - iconSize) / 2f, min.Y + (height - iconSize) / 2f),
            iconSize, iconColor);

        if (!disabled && tooltip is not null && ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            const float previewSize = 64f;
            // The tooltip's width is whatever its widest line ends up being
            // — usually the identifying text ("Acceleration Bomb"), which is
            // wider than the 64px icon. Center both the icon and the text
            // against that shared width instead of leaving the icon flush
            // left of a wider text line below it.
            var textWidth = ImGui.CalcTextSize(tooltip).X;
            var contentWidth = MathF.Max(previewSize, textWidth);
            var startX = ImGui.GetCursorPosX();

            ImGui.SetCursorPosX(startX + MathF.Max(0, (contentWidth - previewSize) / 2f));
            var previewOrigin = ImGui.GetCursorScreenPos();
            // Preview always renders at full brightness/its natural accent
            // color regardless of the button's current selected state —
            // it's a reference image, not a live reflection of selection.
            var previewColor = Theme.U32(Theme.ButtonAccent(tag).Fg, 1f);
            icon(ImGui.GetWindowDrawList(), previewOrigin, previewSize, previewColor);
            ImGui.Dummy(new Vector2(previewSize, previewSize));

            ImGui.SetCursorPosX(startX + MathF.Max(0, (contentWidth - textWidth) / 2f));
            ImGui.TextUnformatted(tooltip);
            ImGui.EndTooltip();
        }
        return clicked;
    }

    // Real/Fake as check/X icon buttons, no row label at all — GCO and Floor
    // AOE's section header ("Grand Cross Omega #1"/"Floor AOE #1") and
    // Thunder/Blizzard's own pane header (see DrawThunderBlizzard) already
    // give enough context that a "Cast" label added nothing but width
    // pressure. Same click-to-clear semantics as app.js's set(): onSet
    // receives the raw clicked target (Real or Fake) and decides the
    // toggle-off itself, matching SetIt1Rf/SetIt2Rf/SetRf's convention.
    private void CastRow(string id, RF current, AccentTag realTag, bool disabled, string? tooltip, Action<RF> onSet)
    {
        var height = ImGui.GetFrameHeight();
        var gap = Sc(ButtonGap);
        var width = RowButtonWidth(2, gap);

        ImGui.BeginGroup();
        if (IconAccentButton($"{id}real", Icons.Check, current == RF.Real, realTag, disabled, width, height, "Real"))
            onSet(RF.Real);
        ImGui.SameLine(0, gap);
        if (IconAccentButton($"{id}fake", Icons.Cross, current == RF.Fake, AccentTag.Fake, disabled, width, height, "Fake"))
            onSet(RF.Fake);
        ImGui.EndGroup();
        if (disabled && tooltip is not null && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip(tooltip);
    }

    // "Debuffs" row as Compressed Water / Forked Lightning / Acceleration
    // Bomb icon buttons — no row label, same reasoning as CastRow's. The
    // actual debuff names live in hover tooltips instead. Water/Lightning
    // share posDisabled, Accel Bomb has its own accelDisabled (mutual
    // exclusion with a sibling button vs. the enforceOrder gate — see
    // MechState.cs); showTooltipWhen/tooltip is the enforceOrder-only
    // row-level message.
    private void DebuffsRow(int gco, Pos pos, bool accel, bool posDisabled, bool accelDisabled, bool showTooltipWhen, string tooltip)
    {
        var s = _session.State;
        var height = ImGui.GetFrameHeight() + Sc(18f);
        var gap = Sc(ButtonGap);
        var width = RowButtonWidth(3, gap);

        // No PushState() here — g1pos/g2pos/g1accel/g2accel are never in
        // BuildSharedState's payload (see its comment), so a push after only
        // one of these changing would just resend the room's already-synced
        // fields unchanged. The local UI updates regardless, since ImGui
        // redraws from MechState directly every frame.
        ImGui.BeginGroup();
        if (IconAccentButton($"g{gco}water", _gameIcons.Water ?? Icons.Droplet, pos == Pos.Water, AccentTag.Water, posDisabled, width, height, "Compressed Water", isRealIcon: _gameIcons.Water is not null))
            s.SetPos(gco, Pos.Water);
        ImGui.SameLine(0, gap);
        if (IconAccentButton($"g{gco}lightning", _gameIcons.Lightning ?? Icons.Thunder, pos == Pos.Lightning, AccentTag.Lightning, posDisabled, width, height, "Forked Lightning", isRealIcon: _gameIcons.Lightning is not null))
            s.SetPos(gco, Pos.Lightning);
        ImGui.SameLine(0, gap);
        if (IconAccentButton($"g{gco}accel", _gameIcons.AccelBomb ?? Icons.Bomb, accel, AccentTag.Accel, accelDisabled, width, height, "Acceleration Bomb", isRealIcon: _gameIcons.AccelBomb is not null))
            s.ToggleAccel(gco);
        ImGui.EndGroup();
        if (showTooltipWhen && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip(tooltip);
    }

    // Floor AOE's "Type" row as Flame/Wave icon buttons instead of
    // "Inferno"/"Tsunami" text. onSet is null for Floor AOE #2's row, which
    // is always disabled/display-only (see DrawFloorAoeSection) — the click
    // handler doesn't matter there since IconAccentButton's own disabled
    // state already blocks the click, this just keeps the caller from having
    // to pass a no-op lambda.
    private void TypeRow(string id, FloorType current, bool disabled, Action<FloorType>? onSet)
    {
        var height = ImGui.GetFrameHeight() + Sc(18f);
        var gap = Sc(ButtonGap);
        var width = RowButtonWidth(2, gap);

        ImGui.BeginGroup();
        if (IconAccentButton($"{id}inferno", _gameIcons.Inferno ?? Icons.Flame, current == FloorType.Inferno, AccentTag.Inferno, disabled, width, height, "Inferno (Entropy)", isRealIcon: _gameIcons.Inferno is not null))
            onSet?.Invoke(FloorType.Inferno);
        ImGui.SameLine(0, gap);
        if (IconAccentButton($"{id}tsunami", _gameIcons.Tsunami ?? Icons.Wave, current == FloorType.Tsunami, AccentTag.Tsunami, disabled, width, height, "Tsunami (Dynamic Fluid)", isRealIcon: _gameIcons.Tsunami is not null))
            onSet?.Invoke(FloorType.Tsunami);
        ImGui.EndGroup();
    }

    // Mirrors .col-status: a "My Status" section label, the Position/Accel
    // Bomb pair side by side (.scards-top's 2-col grid), then the Gaze /
    // Floor AOE / Thunder & Blizzard groups, each a labeled block of
    // labeled sub-rows (.tb-row) — same grouping as the HTML, not a flat
    // list of cards.
    private const float TbNameWidth = 48f;
    private const float CardRounding = 6f;
    // GroupCard's interior left/right inset — named for the status column
    // originally, but also used by DrawThunderBlizzard's panes now.
    private const float CardPadX = 8f;

    // Right column: the "My Status" label + top pair as one block, then the
    // three group cards. Drawn at natural height; see DrawInputsColumn.
    private void DrawStatusColumn()
    {
        var s = _session.State;

        SectionLabel("My Status");
        ImGui.Spacing();

        // .scards-top is a grid-template-columns: 1fr 1fr — both cards get an
        // equal fixed-width cell. Drawing them as actual bordered .scard
        // boxes (rather than bare centered content floating in the column) is
        // what makes the centering read as intentional instead of two icons
        // drifting to opposite edges of a wide empty column.
        var cardGap = Sc(8f);
        var cardWidth = (ImGui.GetContentRegionAvail().X - cardGap) / 2f;

        var spread = s.Spread();
        Card(cardWidth, "Position", spread == false ? Icons.Stack : Icons.Spread, spread is not null,
            spread == false ? AccentTag.Stack : AccentTag.Spread, spread == true ? "SPREAD" : "STACK");
        ImGui.SameLine(0, cardGap);
        var accel = s.Accel();
        Card(cardWidth, "Accel Bomb", accel == "move" ? Icons.Move : Icons.Still, accel is not null,
            accel == "move" ? AccentTag.Move : AccentTag.Still, accel == "still" ? "STOP" : "MOVE");
        SetExactGap(StatusCardGap);

        GroupCard("Gaze", () =>
        {
            GazeRow("1st GCO", s.G1Rf);
            GazeRow("2nd GCO", s.G2Rf);
        });

        // GroupCard's own trailing Dummy already advances the cursor by
        // style.ItemSpacing.Y automatically (ImGui adds that after every
        // item, including Dummy) — a plain extra Dummy(StatusCardGap) here
        // would stack ON TOP of that. SetExactGap cancels the automatic
        // spacing out first, so StatusCardGap is the WHOLE gap between boxes.
        SetExactGap(StatusCardGap);

        GroupCard("Floor AOE", () =>
        {
            var infernoRf = s.It1Type == FloorType.Inferno ? s.It1Rf : s.It2Type == FloorType.Inferno ? s.It2Rf : RF.None;
            FloorRow("Inferno", FloorType.Inferno, infernoRf, AccentTag.Inferno);
            var tsunamiRf = s.It1Type == FloorType.Tsunami ? s.It1Rf : s.It2Type == FloorType.Tsunami ? s.It2Rf : RF.None;
            FloorRow("Tsunami", FloorType.Tsunami, tsunamiRf, AccentTag.Tsunami);
        });

        SetExactGap(StatusCardGap);

        GroupCard("Thunder & Blizzard", () =>
        {
            ElementRow("Thunder", s.ThunderRf, Icons.Thunder, Icons.ThunderFake, AccentTag.Thunder);
            ElementRow("Blizzard", s.BlizzardRf, Icons.Blizzard, Icons.BlizzardFake, AccentTag.Blizzard);
        });
    }

    private void GazeRow(string n, RF rf) => TbRow(n,
        rf == RF.Real ? Icons.GazeOut : Icons.GazeIn, rf != RF.None,
        rf == RF.Real ? AccentTag.Out : AccentTag.In,
        rf == RF.Real ? "OUT" : rf == RF.Fake ? "IN" : "");

    private void FloorRow(string n, FloorType type, RF rf, AccentTag tag)
    {
        var shape = MechState.FloorAoe(type, rf);
        TbRow(n, shape == "donut" ? Icons.Donut : Icons.Circle, rf != RF.None, tag,
            rf == RF.Real ? "REAL" : rf == RF.Fake ? "FAKE" : "",
            shape switch { "circle" => "Stack → get out", "donut" => "Stack → stay in", _ => null },
            reserveSub: true);
    }

    // real/fake icons differ (thunderFake/blizzardFake add the dim + red
    // strike-through per icons.js) — passing one Icon for both states, as an
    // earlier pass here did, silently dropped that distinction.
    private void ElementRow(string n, RF rf, Icon real, Icon fake, AccentTag tag) => TbRow(n,
        rf == RF.Fake ? fake : real, rf != RF.None, tag,
        rf == RF.Real ? "REAL" : rf == RF.Fake ? "FAKE" : "");

    // A bordered .scard box (bg #0f0f24 + border, rounded) drawn around a
    // section label and its rows. Height is unknown until the rows are laid
    // out, so the content is drawn into draw-list channel 1 first, then the
    // background rect is painted behind it in channel 0 and merged — the
    // standard ImGui trick for "box that fits its content".
    // Overrides the automatic ItemSpacing.Y that ImGui adds after the
    // previous item so the gap before the next item is exactly Sc(gap) —
    // no hidden baseline getting added on top. See the call sites in
    // DrawStatusColumn for why this matters over a plain Dummy.
    private void SetExactGap(float gap)
    {
        var cur = ImGui.GetCursorScreenPos();
        var autoSpacing = ImGui.GetStyle().ItemSpacing.Y;
        ImGui.SetCursorScreenPos(new Vector2(cur.X, cur.Y - autoSpacing + Sc(gap)));
    }

    private void GroupCard(string label, Action drawRows)
    {
        var padTop = Sc(8f);
        var padBot = Sc(8f);
        var padX = Sc(CardPadX);
        var dl = ImGui.GetWindowDrawList();
        var p0 = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;

        dl.ChannelsSplit(2);
        dl.ChannelsSetCurrent(1); // content on top

        // A fixed-width single-column table (not Indent) for the interior:
        // Indent only insets the LEFT side, so content (e.g. CastRow's
        // buttons, sized off GetContentRegionAvail()) stretched flush to the
        // box's right edge with no matching right-side margin, touching or
        // slightly overflowing the border. Starting the table's cursor
        // already padX right of p0, with a column exactly (width - 2*padX)
        // wide, gives an equal inset on both sides — same table-for-width-
        // constraint technique already used for the main 2-column layout and
        // the Thunder/Blizzard pane split.
        ImGui.SetCursorScreenPos(new Vector2(p0.X + padX, p0.Y + padTop));
        if (ImGui.BeginTable($"gc_{label}", 1))
        {
            ImGui.TableSetupColumn("c", ImGuiTableColumnFlags.WidthFixed, width - 2 * padX);
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            SectionLabel(label);
            ImGui.Spacing();
            drawRows();
            ImGui.EndTable();
        }
        var contentBottom = ImGui.GetCursorScreenPos().Y;

        var p1 = new Vector2(p0.X + width, contentBottom + padBot);
        dl.ChannelsSetCurrent(0); // background behind
        dl.AddRectFilled(p0, p1, Theme.U32(Theme.CardBg), Sc(CardRounding));
        dl.AddRect(p0, p1, Theme.U32(Theme.Border), Sc(CardRounding));
        dl.ChannelsMerge();

        // No trailing ImGui.Spacing() here — ImGui already inserts one
        // ItemSpacing.Y after the Dummy automatically before the next
        // GroupCard; an explicit Spacing() on top of that was doubling the
        // gap between the Gaze/Floor AOE/Thunder & Blizzard boxes.
        ImGui.SetCursorScreenPos(p0);
        ImGui.Dummy(new Vector2(width, p1.Y - p0.Y));
    }

    // A .scard box for the top pair: label / icon / value stacked and
    // horizontally centered. Height is fixed (known content), so the box is
    // drawn first and content placed on top — no channel split needed.
    private void Card(float width, string label, Icon icon, bool active, AccentTag tag, string value)
    {
        var iconSize = Sc(40f);
        float padTop = Sc(10f), padBot = Sc(10f), gap = Sc(7f);
        var displayValue = active ? value : "-";

        var dl = ImGui.GetWindowDrawList();
        var lineH = ImGui.GetTextLineHeight();
        var labelH = lineH * LabelScale;
        var valueH = lineH * ScardValScale;
        var cardH = padTop + labelH + gap + iconSize + gap + valueH + padBot;

        var p0 = ImGui.GetCursorScreenPos();
        var p1 = new Vector2(p0.X + width, p0.Y + cardH);
        dl.AddRectFilled(p0, p1, Theme.U32(Theme.CardBg), Sc(CardRounding));
        dl.AddRect(p0, p1, Theme.U32(Theme.Border), Sc(CardRounding));

        var cx = p0.X + width / 2f;
        var y = p0.Y + padTop;
        CenteredText(label, LabelScale, Theme.TextLabel, cx, y);
        y += labelH + gap;
        var iconColor = active ? Theme.U32(Theme.CardColor(tag)) : Theme.U32(Theme.Placeholder);
        icon(dl, new Vector2(cx - iconSize / 2f, y), iconSize, iconColor);
        y += iconSize + gap;
        CenteredText(displayValue, ScardValScale, active ? Theme.CardColor(tag) : Theme.TextDim, cx, y);

        ImGui.SetCursorScreenPos(p0);
        ImGui.Dummy(new Vector2(width, cardH));
    }

    // .tb-icon ~34px, .tb-n right-aligned in 56px slot, .tb-val 16px, .tb-sub
    // body-size — vertically centered against the icon (.tb-row's
    // align-items:center) AND the whole [name|icon|value] block horizontally
    // centered within the card interior (.tb-row's justify-content:center).
    // Fully screen-positioned so it lays out correctly regardless of the
    // enclosing GroupCard's indent.
    // reserveSub reserves the sub-text line's width/height whether or not
    // `sub` is actually populated THIS frame — Floor AOE's hint ("Stack →
    // get out"/"stay in") only appears once both a type and real/fake are
    // set, so sizing off `sub is null` made the Floor AOE box visibly grow
    // the instant that hint appeared. Rows that can never have a sub line
    // (Gaze, Thunder & Blizzard) don't set this and stay their normal size.
    private void TbRow(string n, Icon icon, bool active, AccentTag tag, string value, string? sub = null, bool reserveSub = false)
    {
        var hasSubSlot = reserveSub || sub is not null;
        var iconSize = Sc(29f);
        var gap = Sc(8f);
        var nameSlot = Sc(TbNameWidth);
        // .tb-val is 48px, .tb-valcol (sub present) is 120px — a fixed value
        // column keeps every row in a group sharing one left edge so they
        // read as a column even while the block as a whole is centered.
        var valueColWidth = Sc(hasSubSlot ? 110f : 44f);
        var block = nameSlot + gap + iconSize + gap + valueColWidth;

        var dl = ImGui.GetWindowDrawList();
        var rowStart = ImGui.GetCursorScreenPos();
        var lineH = ImGui.GetTextLineHeight();

        var nameH = lineH * LabelScale;
        var valueH = lineH * TbValScale;
        var subH = hasSubSlot ? lineH : 0f;
        var subGap = hasSubSlot ? Sc(2f) : 0f;
        var blockH = valueH + subGap + subH;
        var rowH = MathF.Max(iconSize, blockH);
        var iconColor = active ? Theme.U32(Theme.CardColor(tag)) : Theme.U32(Theme.Placeholder);

        // interior width = card right edge (≈ window right, minus the pad) to
        // the indented rowStart.X; center the fixed-width block within it.
        var interiorWidth = ImGui.GetContentRegionAvail().X - Sc(CardPadX);
        var blockX = rowStart.X + MathF.Max(0, (interiorWidth - block) / 2f);

        // name — right-aligned in its slot, vertically centered
        ImGui.SetWindowFontScale(_uiScale * LabelScale);
        var nWidth = ImGui.CalcTextSize(n).X;
        ImGui.SetWindowFontScale(_uiScale);
        ImGui.SetCursorScreenPos(new Vector2(blockX + MathF.Max(0, nameSlot - nWidth), rowStart.Y + (rowH - nameH) / 2f));
        ScaledText(n, LabelScale, Theme.TextDim);

        // icon — vertically centered
        var iconX = blockX + nameSlot + gap;
        icon(dl, new Vector2(iconX, rowStart.Y + (rowH - iconSize) / 2f), iconSize, iconColor);

        // value (+ optional sub) — vertically centered block
        var textX = iconX + iconSize + gap;
        var blockY = rowStart.Y + (rowH - blockH) / 2f;
        ImGui.SetCursorScreenPos(new Vector2(textX, blockY));
        ScaledText(value, TbValScale, active ? Theme.CardColor(tag) : Theme.TextDim);
        if (sub is not null)
        {
            ImGui.SetCursorScreenPos(new Vector2(textX, blockY + valueH + subGap));
            ImGui.TextColored(Theme.TextDim, sub);
        }

        // No trailing ImGui.Spacing() here either — same reasoning as
        // GroupCard's: it was doubling the gap between "1st GCO"/"2nd GCO"
        // and similar row pairs.
        ImGui.SetCursorScreenPos(rowStart);
        ImGui.Dummy(new Vector2(ImGui.GetContentRegionAvail().X, rowH));
    }

    // Draws text horizontally centered on centerX at the given screen Y, at
    // the given font scale — used for the stacked .scard content.
    private void CenteredText(string text, float scale, Vector4 color, float centerX, float screenY)
    {
        ImGui.SetWindowFontScale(_uiScale * scale);
        var w = ImGui.CalcTextSize(text).X;
        ImGui.SetCursorScreenPos(new Vector2(centerX - w / 2f, screenY));
        ImGui.TextColored(color, text);
        ImGui.SetWindowFontScale(_uiScale);
    }

}
