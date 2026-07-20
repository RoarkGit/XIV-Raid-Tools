using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace XIVRaidToolsPlugin;

// Vector re-draws of kefka-says/icons.js's inline SVGs onto an ImGui draw
// list, at the same 44x44 viewBox the source SVGs use. No texture/PNG
// pipeline needed — everything here is lines, circles, and a couple of
// manually-triangulated fills, scaled to whatever box the caller draws into.
public delegate void Icon(ImDrawListPtr dl, Vector2 topLeft, float size, uint color);

public static class Icons
{
    private static Vector2 P(Vector2 origin, float s, float x, float y) => origin + new Vector2(x, y) * s;

    // Shaft + a 2-segment arrowhead chevron meeting at one point is a 3-way
    // junction (branch), which can't be a single continuous path — a path is
    // a linear chain, it can't fork. The chevron's own 2 segments CAN be one
    // continuous path (no seam between them), but the shaft joining that
    // same point is necessarily a second, separate stroke — so a small
    // filled circle "cap" sits over the junction, sized to the stroke width,
    // masking whatever seam the two independently anti-aliased strokes would
    // otherwise leave right at the corner.
    private static void ArmWithChevron(ImDrawListPtr dl, Vector2 o, float s, uint color,
        float x1, float y1, float x2, float y2, float ax, float ay, float bx, float by, float thickness)
    {
        dl.AddLine(P(o, s, x1, y1), P(o, s, x2, y2), color, thickness);
        dl.PathLineTo(P(o, s, ax, ay));
        dl.PathLineTo(P(o, s, x2, y2));
        dl.PathLineTo(P(o, s, bx, by));
        dl.PathStroke(color, ImDrawFlags.None, thickness);
        dl.AddCircleFilled(P(o, s, x2, y2), thickness / 2f, color);
    }

    public static void Spread(ImDrawListPtr dl, Vector2 o, float size, uint color)
    {
        var s = size / 44f;
        var t = 2.5f * s;
        ArmWithChevron(dl, o, s, color, 22, 22, 36, 8, 29, 8, 36, 15, t);
        ArmWithChevron(dl, o, s, color, 22, 22, 8, 36, 15, 36, 8, 29, t);
        ArmWithChevron(dl, o, s, color, 22, 22, 8, 8, 15, 8, 8, 15, t);
        ArmWithChevron(dl, o, s, color, 22, 22, 36, 36, 29, 36, 36, 29, t);
    }

    public static void Stack(ImDrawListPtr dl, Vector2 o, float size, uint color)
    {
        var s = size / 44f;
        var t = 2.5f * s;
        // Same junction as Spread's arms, just pointing inward (chevron end
        // is the shaft's START here, x1/y1, rather than its end).
        ArmWithChevron(dl, o, s, color, 25, 19, 36, 8, 29, 8, 36, 15, t);
        ArmWithChevron(dl, o, s, color, 19, 25, 8, 36, 15, 36, 8, 29, t);
        ArmWithChevron(dl, o, s, color, 19, 19, 8, 8, 15, 8, 8, 15, t);
        ArmWithChevron(dl, o, s, color, 25, 25, 36, 36, 29, 36, 36, 29, t);
        dl.AddCircleFilled(P(o, s, 22, 22), 4 * s, color);
    }

    public static void Still(ImDrawListPtr dl, Vector2 o, float size, uint color)
    {
        var s = size / 44f;
        dl.AddRectFilled(P(o, s, 10, 9), P(o, s, 20, 35), color, 2 * s);
        dl.AddRectFilled(P(o, s, 24, 9), P(o, s, 34, 35), color, 2 * s);
    }

    public static void Move(ImDrawListPtr dl, Vector2 o, float size, uint color)
    {
        var s = size / 44f;
        dl.AddTriangleFilled(P(o, s, 9, 5), P(o, s, 9, 39), P(o, s, 39, 22), color);
    }

    // Eye lens approximated as two mirrored bezier arcs (no AddEllipse in
    // this binding) plus a filled pupil, same silhouette as icons.js's path.
    private static void EyeOutline(ImDrawListPtr dl, Vector2 o, float s, uint color, float thickness)
    {
        var left = P(o, s, 4, 22);
        var right = P(o, s, 40, 22);
        var topCtrl = P(o, s, 22, 6);
        var botCtrl = P(o, s, 22, 38);
        dl.PathLineTo(left);
        dl.PathBezierQuadraticCurveTo(topCtrl, right);
        dl.PathBezierQuadraticCurveTo(botCtrl, left);
        dl.PathStroke(color, ImDrawFlags.Closed, thickness);
    }

    public static void GazeIn(ImDrawListPtr dl, Vector2 o, float size, uint color)
    {
        var s = size / 44f;
        EyeOutline(dl, o, s, color, 2.5f * s);
        dl.AddCircleFilled(P(o, s, 22, 22), 7 * s, color);
    }

    public static void GazeOut(ImDrawListPtr dl, Vector2 o, float size, uint color)
    {
        var s = size / 44f;
        EyeOutline(dl, o, s, ScaleAlpha(color, 0.3f), 2.5f * s);
        dl.AddCircleFilled(P(o, s, 22, 22), 7 * s, ScaleAlpha(color, 0.3f));
        dl.AddLine(P(o, s, 8, 8), P(o, s, 36, 36), Theme.U32(Theme.Fake), 3f * s);
    }

    public static void Circle(ImDrawListPtr dl, Vector2 o, float size, uint color)
    {
        var s = size / 44f;
        dl.AddCircleFilled(P(o, s, 22, 22), 19 * s, ScaleAlpha(color, 0.85f));
    }

    public static void Donut(ImDrawListPtr dl, Vector2 o, float size, uint color)
    {
        var s = size / 44f;
        dl.AddCircle(P(o, s, 22, 22), 14.5f * s, ScaleAlpha(color, 0.85f), 0, 9 * s);
    }

    // Stroked zigzag rather than a filled hexagon: the previous version
    // triangulated the classic bolt silhouette into 3 manual triangles, but
    // adjoining triangle edges each anti-alias independently, leaving a
    // visible seam through the shape — much more noticeable at the small
    // sizes these icons render at (button icons, ~15-20px) than it was in
    // the original at-scale mockup. This zigzag is a genuinely sequential
    // path (unlike Spread/Stack's 3-way arm junctions), so it really can be
    // ONE continuous PathStroke — the previous version still used 3 separate
    // AddLine calls despite the comment here claiming otherwise, and had the
    // exact same joint-seam bug as the fill version it replaced.
    private static void BoltPath(ImDrawListPtr dl, Vector2 o, float s, uint color, float thickness)
    {
        dl.PathLineTo(P(o, s, 27, 3));
        dl.PathLineTo(P(o, s, 14, 23));
        dl.PathLineTo(P(o, s, 25, 23));
        dl.PathLineTo(P(o, s, 15, 41));
        dl.PathStroke(color, ImDrawFlags.None, thickness);
    }

    public static void Thunder(ImDrawListPtr dl, Vector2 o, float size, uint color)
    {
        var s = size / 44f;
        BoltPath(dl, o, s, color, 4f * s);
    }

    public static void ThunderFake(ImDrawListPtr dl, Vector2 o, float size, uint color)
    {
        var s = size / 44f;
        BoltPath(dl, o, s, ScaleAlpha(color, 0.3f), 4f * s);
        dl.AddLine(P(o, s, 6, 6), P(o, s, 38, 38), Theme.U32(Theme.Fake), 3.5f * s);
    }

    // Inferno (flame) and Tsunami (wave) — for the compact Floor AOE #1 Type
    // buttons, replacing the "Inferno"/"Tsunami" text buttons. Flame reuses
    // Droplet's triangle+circle construction (simple, no seam risk) with an
    // asymmetric lean so it doesn't read as a second water drop; Wave is a
    // stroked double-arc (no fill, same anti-seam reasoning as the bolt).
    public static void Flame(ImDrawListPtr dl, Vector2 o, float size, uint color)
    {
        var s = size / 44f;
        dl.AddTriangleFilled(P(o, s, 27, 3), P(o, s, 8, 29), P(o, s, 35, 24), color);
        dl.AddCircleFilled(P(o, s, 21, 29), 13 * s, color);
    }

    public static void Wave(ImDrawListPtr dl, Vector2 o, float size, uint color)
    {
        var s = size / 44f;
        var t = 3f * s;
        dl.PathLineTo(P(o, s, 4, 28));
        dl.PathBezierQuadraticCurveTo(P(o, s, 14, 14), P(o, s, 22, 28));
        dl.PathStroke(color, ImDrawFlags.None, t);
        dl.PathLineTo(P(o, s, 22, 28));
        dl.PathBezierQuadraticCurveTo(P(o, s, 30, 42), P(o, s, 40, 28));
        dl.PathStroke(color, ImDrawFlags.None, t);
    }

    private static void SnowflakePath(ImDrawListPtr dl, Vector2 o, float s, uint color, float thickness)
    {
        void L(float x1, float y1, float x2, float y2) => dl.AddLine(P(o, s, x1, y1), P(o, s, x2, y2), color, thickness);
        L(22, 4, 22, 40);
        L(4, 22, 40, 22);
        L(9, 9, 35, 35);
        L(35, 9, 9, 35);
        L(22, 4, 16, 11); L(22, 4, 28, 11);
        L(22, 40, 16, 33); L(22, 40, 28, 33);
        L(4, 22, 11, 16); L(4, 22, 11, 28);
        L(40, 22, 33, 16); L(40, 22, 33, 28);
    }

    public static void Blizzard(ImDrawListPtr dl, Vector2 o, float size, uint color)
    {
        SnowflakePath(dl, o, size / 44f, color, 2.5f * size / 44f);
    }

    public static void BlizzardFake(ImDrawListPtr dl, Vector2 o, float size, uint color)
    {
        var s = size / 44f;
        SnowflakePath(dl, o, s, ScaleAlpha(color, 0.3f), 2.5f * s);
        dl.AddLine(P(o, s, 6, 6), P(o, s, 38, 38), Theme.U32(Theme.Fake), 3.5f * s);
    }

    private static uint ScaleAlpha(uint packedColor, float alpha)
    {
        var a = (byte)(((packedColor >> 24) & 0xFF) * alpha);
        return (packedColor & 0x00FFFFFF) | ((uint)a << 24);
    }

    // Not from icons.js — added for the compact icon-only Real/Fake and
    // debuff-selection buttons (no equivalent in the webapp, which has room
    // for text labels; the in-game window doesn't).
    public static void Check(ImDrawListPtr dl, Vector2 o, float size, uint color)
    {
        // One continuous path, not two separate AddLine calls meeting at
        // (18,34) — two independent line draws can leave a visible seam/gap
        // at their shared corner since each is anti-aliased on its own; a
        // single stroked path has no such joint.
        var s = size / 44f;
        var t = 3.5f * s;
        dl.PathLineTo(P(o, s, 8, 24));
        dl.PathLineTo(P(o, s, 18, 34));
        dl.PathLineTo(P(o, s, 37, 10));
        dl.PathStroke(color, ImDrawFlags.None, t);
    }

    public static void Cross(ImDrawListPtr dl, Vector2 o, float size, uint color)
    {
        var s = size / 44f;
        var t = 3.5f * s;
        dl.AddLine(P(o, s, 10, 10), P(o, s, 34, 34), color, t);
        dl.AddLine(P(o, s, 34, 10), P(o, s, 10, 34), color, t);
    }

    public static void Droplet(ImDrawListPtr dl, Vector2 o, float size, uint color)
    {
        var s = size / 44f;
        dl.AddTriangleFilled(P(o, s, 22, 4), P(o, s, 9, 27), P(o, s, 35, 27), color);
        dl.AddCircleFilled(P(o, s, 22, 27), 13 * s, color);
    }

    public static void Bomb(ImDrawListPtr dl, Vector2 o, float size, uint color)
    {
        var s = size / 44f;
        dl.AddLine(P(o, s, 28, 14), P(o, s, 34, 6), color, 3f * s);
        dl.AddCircleFilled(P(o, s, 35, 5), 3f * s, color);
        dl.AddCircleFilled(P(o, s, 20, 27), 14 * s, color);
    }
}
