using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace XIVRaidToolsPlugin;

// Started as a 1:1 port of the :root custom properties in kefka-says/
// index.html, then brightened across the board - the webapp's palette is
// tuned for a browser tab you look at deliberately; an in-game overlay is
// glanced at over a busy 3D scene mid-fight, so it needs more contrast/
// legibility than "matches the website exactly" was giving it.
public static class Theme
{
    public static readonly Vector4 Text = Rgb(0xe8, 0xe8, 0xf7);
    public static readonly Vector4 TextDim = Rgb(0x90, 0x90, 0xb8);
    public static readonly Vector4 TextLabel = Rgb(0x9c, 0x9c, 0xcc);
    public static readonly Vector4 BtnBg = Rgb(0x2e, 0x2e, 0x52);
    public static readonly Vector4 BtnHover = Rgb(0x3c, 0x3c, 0x68);
    public static readonly Vector4 Border = Rgb(0x40, 0x40, 0x68);
    // Solid (alpha-1) stand-in for "dim vector icon placeholder" that used
    // to be Text at ~0.2 alpha. Several vector icons (Stack's circle over
    // its arrow tips, Droplet/Flame's circle over a triangle) draw
    // overlapping primitives - tinting the whole icon at alpha<1 makes each
    // overlapping pixel blend twice, a visible seam where shapes cross. A
    // real game-icon texture has no internal overlap (one flat image), so it
    // can safely use alpha for dimming; vector icons can't and need a solid
    // pre-dimmed color instead. ~22% of Text blended over CardBg.
    public static readonly Vector4 Placeholder = Rgb(0x48, 0x48, 0x66);
    public static readonly Vector4 CardBg = Rgb(0x1c, 0x1c, 0x38); // .scard background

    // index.html's :root --bg/--surface, used unbrightened here (unlike the
    // rest of this palette) - a large flat fill reads fine at the webapp's
    // own darkness, it's small foreground details that needed the boost.
    public static readonly Vector4 WindowBg = Rgb(0x0a, 0x0a, 0x18); // --bg (body background)
    public static readonly Vector4 TitleBgActive = Rgb(0x13, 0x13, 0x2a); // --surface, focused title bar
    // Unfocused title bar matches WindowBg so it blends in rather than
    // drawing attention, standard convention for a focused/unfocused pair.
    public static readonly Vector4 TitleBg = WindowBg;

    public static readonly Vector4 Real = Rgb(0x6e, 0xd6, 0x90);
    public static readonly Vector4 Fake = Rgb(0xe0, 0x6e, 0x6e);
    public static readonly Vector4 Water = Rgb(0x5c, 0xc0, 0xf2);
    public static readonly Vector4 Lightning = Rgb(0xf0, 0xe0, 0x5c);
    public static readonly Vector4 Accel = Rgb(0xf2, 0xa0, 0x4d);
    public static readonly Vector4 Inferno = Rgb(0xff, 0x85, 0x60);
    public static readonly Vector4 Tsunami = Rgb(0x5c, 0x9d, 0xff);
    public static readonly Vector4 Spread = Rgb(0xf2, 0xb0, 0x5c);
    public static readonly Vector4 Stack = Rgb(0x6c, 0xad, 0xf2);
    public static readonly Vector4 Still = Rgb(0xf2, 0xaf, 0x4d);
    public static readonly Vector4 Move = Rgb(0x5c, 0xd0, 0xd0);
    public static readonly Vector4 Out = Rgb(0xe0, 0x80, 0xd0);
    public static readonly Vector4 In = Rgb(0xff, 0xa0, 0xdd);
    public static readonly Vector4 Thunder = Rgb(0xf0, 0xe0, 0x5c);
    public static readonly Vector4 Blizzard = Rgb(0xa0, 0xd9, 0xff);

    private static readonly Vector4 SelRealBg = Rgb(0x1e, 0x3a, 0x2a);
    private static readonly Vector4 SelFakeBg = Rgb(0x3a, 0x1a, 0x1a);
    private static readonly Vector4 SelWaterBg = Rgb(0x16, 0x2a, 0x3e);
    private static readonly Vector4 SelLightBg = Rgb(0x30, 0x2c, 0x16);
    private static readonly Vector4 SelAccelBg = Rgb(0x30, 0x22, 0x10);
    private static readonly Vector4 SelInfBg = Rgb(0x32, 0x16, 0x10);
    private static readonly Vector4 SelTsuBg = Rgb(0x10, 0x1e, 0x30);
    private static readonly Vector4 SelThrBg = Rgb(0x2e, 0x2a, 0x12);
    private static readonly Vector4 SelBlzBg = Rgb(0x10, 0x1e, 0x32);

    // Mirrors the .btn.on-<tag> CSS rules: (foreground, tinted background).
    // Tags with no CSS background rule of their own (spread/stack/still/
    // move/in/out) are card-only accents - never used as a button state.
    public static (Vector4 Fg, Vector4 Bg) ButtonAccent(AccentTag tag) => tag switch
    {
        AccentTag.Real => (Real, SelRealBg),
        AccentTag.Fake => (Fake, SelFakeBg),
        AccentTag.Water => (Water, SelWaterBg),
        AccentTag.Lightning => (Lightning, SelLightBg),
        AccentTag.Accel => (Accel, SelAccelBg),
        AccentTag.Inferno => (Inferno, SelInfBg),
        AccentTag.Tsunami => (Tsunami, SelTsuBg),
        AccentTag.Thunder => (Thunder, SelThrBg),
        AccentTag.Blizzard => (Blizzard, SelBlzBg),
        _ => (TextDim, BtnBg),
    };

    public static Vector4 CardColor(AccentTag tag) => tag switch
    {
        AccentTag.Spread => Spread,
        AccentTag.Stack => Stack,
        AccentTag.Still => Still,
        AccentTag.Move => Move,
        AccentTag.In => In,
        AccentTag.Out => Out,
        AccentTag.Inferno => Inferno,
        AccentTag.Tsunami => Tsunami,
        AccentTag.Thunder => Thunder,
        AccentTag.Blizzard => Blizzard,
        _ => TextDim,
    };

    public static uint U32(Vector4 color, float alpha = 1f) =>
        ImGui.ColorConvertFloat4ToU32(color with { W = color.W * alpha });

    // Window chrome (background fill + title bar) is drawn by ImGui.Begin()
    // itself, before a window's own Draw() runs - so unlike the
    // Button/Text/Border colors each window pushes inside Draw(), these have
    // to be pushed in PreDraw() (before Begin) to take effect, and popped in
    // PostDraw(). Call both from every window for a consistent look.
    public static void PushWindowChrome()
    {
        ImGui.PushStyleColor(ImGuiCol.WindowBg, WindowBg);
        ImGui.PushStyleColor(ImGuiCol.TitleBg, TitleBg);
        ImGui.PushStyleColor(ImGuiCol.TitleBgActive, TitleBgActive);
        ImGui.PushStyleColor(ImGuiCol.TitleBgCollapsed, TitleBg);
        ImGui.PushStyleColor(ImGuiCol.Text, Text);
    }

    public static void PopWindowChrome() => ImGui.PopStyleColor(5);

    private static Vector4 Rgb(byte r, byte g, byte b) => new(r / 255f, g / 255f, b / 255f, 1f);
}

// Matches the CSS class suffixes on .btn.on-* and .c-* in index.html.
public enum AccentTag
{
    None, Real, Fake, Water, Lightning, Accel, Inferno, Tsunami, Thunder, Blizzard,
    Spread, Stack, Still, Move, In, Out,
}
