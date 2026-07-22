using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Plugin.Services;

namespace XIVRaidToolsPlugin;

// Real game status-icon textures, looked up by exact status name at
// startup. These are the actual debuff names from the fight this tracker is
// for (Compressed Water / Forked Lightning / Acceleration Bomb / Entropy /
// Dynamic Fluid), so the lookup finds the genuine, professionally-drawn
// debuff icon players already recognize from their own debuff bar - not a
// hand-drawn approximation.
public sealed class GameIcons
{
    private readonly ITextureProvider _textures;

    public readonly Icon? Water;
    public readonly Icon? Lightning;
    public readonly Icon? AccelBomb;
    public readonly Icon? Inferno;
    public readonly Icon? Tsunami;

    public GameIcons(IDataManager data, ITextureProvider textures)
    {
        _textures = textures;
        Water = LookupStatusIcon(data, "Compressed Water");
        Lightning = LookupStatusIcon(data, "Forked Lightning");
        AccelBomb = LookupStatusIcon(data, "Acceleration Bomb");
        Inferno = LookupStatusIcon(data, "Entropy");
        Tsunami = LookupStatusIcon(data, "Dynamic Fluid");
    }

    // ClientLanguage.English pinned explicitly - status names are localized,
    // and the client's own game-language setting could be anything, which
    // would silently fail an English-string match.
    private Icon? LookupStatusIcon(IDataManager data, string name)
    {
        var sheet = data.GetExcelSheet<Lumina.Excel.Sheets.Status>(ClientLanguage.English);
        foreach (var row in sheet)
        {
            if (row.Name.ExtractText() == name)
            {
                var iconId = (uint)row.Icon;
                return (dl, topLeft, size, color) => Draw(iconId, dl, topLeft, size, color);
            }
        }
        return null; // not found - caller falls back to the vector Icons.* glyph
    }

    private void Draw(uint iconId, ImDrawListPtr dl, Vector2 topLeft, float size, uint color)
    {
        // No caching of the resolved wrap at all - that was the actual bug,
        // twice over. First attempt cached a null "not ready yet" result
        // permanently (dictionaries store null just fine, so the icon never
        // appeared, ever). Second attempt fixed that but then cached
        // whatever non-null wrap came back - except for an icon ID Dalamud
        // can't resolve, "non-null" is a special disposed "unknown texture"
        // sentinel, not a usable texture, and reusing that cached sentinel
        // on a later frame threw ObjectDisposedException on .Handle.
        // ISharedImmediateTexture/GetWrapOrDefault is designed to be called
        // fresh every frame - Dalamud does its own caching internally - so
        // the fix is to just stop caching it ourselves.
        var wrap = _textures.GetFromGameIcon(new GameIconLookup(iconId)).GetWrapOrDefault();

        // Checked by type name rather than a reference/nullability check:
        // this sentinel is a real, non-null, allocated object - its type
        // just isn't public, so a name check is the only way to catch it
        // before touching the disposed .Handle it exposes.
        if (wrap is null || wrap.GetType().Name == "UnknownTextureWrap") return;

        // Unlike the vector Icons.* glyphs (single-color strokes/fills that
        // are MEANT to be recolored per accent tag), a real game icon is a
        // full-color piece of art - multiplying a saturated accent color
        // over it would wash it out and crush its actual colors. Keep the
        // icon's natural RGB and use only the caller's alpha byte (full for
        // "selected"/bright, reduced for "unselected"/dim - see
        // IconAccentButton), same dim-vs-bright effect as the vector icons
        // get from color, just via opacity instead of a tint.
        var tint = (color & 0xFF000000u) | 0x00FFFFFFu;

        // Many FFXIV status icons are NOT square (a lot of them are 24×32),
        // so forcing one into a size×size box like the vector Icons.* glyphs
        // (which ARE drawn to be square) stretched/squished it. Fit within
        // the box preserving aspect ratio and center it, rather than filling
        // the box edge to edge.
        var aspect = wrap.Height > 0 ? wrap.Width / (float)wrap.Height : 1f;
        var drawSize = aspect >= 1f
            ? new Vector2(size, size / aspect)
            : new Vector2(size * aspect, size);
        var p0 = topLeft + (new Vector2(size, size) - drawSize) / 2f;

        try
        {
            dl.AddImage(wrap.Handle, p0, p0 + drawSize, Vector2.Zero, Vector2.One, tint);
        }
        catch (ObjectDisposedException)
        {
            // Belt-and-suspenders against any other disposal timing this
            // type-name check doesn't catch - skip this frame rather than
            // crash the plugin's whole Draw().
        }
    }
}
