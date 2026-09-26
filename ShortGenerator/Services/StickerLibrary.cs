using ShortGenerator.Models;

namespace ShortGenerator.Services;

/// <summary>
/// Transparent PNG stickers (laugh, love, fire...) kept in %AppData%\ShortGenerator\stickers. The app ships a
/// starter set generated with Higgsfield; users can drop any PNG into the folder and it shows up in the editor.
/// </summary>
public static class StickerLibrary
{
    public static string Folder => Path.Combine(SettingsStore.AppDataFolder, "stickers");

    /// <summary>Copies the starter set shipped next to the exe into the user's library (only files that are missing).</summary>
    public static void EnsureSeeded()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            var shipped = System.IO.Path.Combine(AppContext.BaseDirectory, "stickers");
            if (!Directory.Exists(shipped)) return;
            foreach (var f in Directory.EnumerateFiles(shipped, "*.png"))
            {
                var target = System.IO.Path.Combine(Folder, System.IO.Path.GetFileName(f));
                if (!System.IO.File.Exists(target)) System.IO.File.Copy(f, target);
            }
        }
        catch { /* best effort */ }
    }

    /// <summary>All sticker files, by file name without extension.</summary>
    public static List<(string Name, string Path)> List()
    {
        EnsureSeeded();
        return Directory.EnumerateFiles(Folder)
            .Where(f => f.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".webp", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".gif", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f)
            .Select(f => (System.IO.Path.GetFileNameWithoutExtension(f), f))
            .ToList();
    }

    /// <summary>Full path of a sticker referenced by an overlay (library name or absolute path). Null when missing.</summary>
    public static string? Resolve(string file)
    {
        if (string.IsNullOrWhiteSpace(file)) return null;
        if (System.IO.Path.IsPathRooted(file)) return System.IO.File.Exists(file) ? file : null;
        var direct = System.IO.Path.Combine(Folder, file);
        if (System.IO.File.Exists(direct)) return direct;
        foreach (var ext in new[] { ".png", ".webp", ".gif" })
            if (System.IO.File.Exists(direct + ext)) return direct + ext;
        return null;
    }

    /// <summary>Which sticker fits a reaction tag or an emotion word. Returns a library name or null.</summary>
    public static string? ForReaction(string textOrEmotion)
    {
        var t = textOrEmotion.ToLowerInvariant();
        var available = List().Select(s => s.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        string? Pick(params string[] names) => names.FirstOrDefault(available.Contains);

        if (t.Contains("[big laugh]")) return Pick("big-laugh", "laugh");
        if (t.Contains("[laughs]") || t.Contains("humor") || t.Contains("funny")) return Pick("laugh", "big-laugh");
        if (t.Contains("[applause]") || t.Contains("[cheering]") || t.Contains("[loud cheering]")) return Pick("applause", "fire");
        if (t.Contains("[crying]") || t.Contains("sad")) return Pick("cry");
        if (t.Contains("[shouting]") || t.Contains("anger") || t.Contains("outrage") || t.Contains("angry")) return Pick("anger", "fire");
        if (t.Contains("love") || t.Contains("romance")) return Pick("heart-eyes", "love");
        if (t.Contains("inspir") || t.Contains("motivat") || t.Contains("idea")) return Pick("inspiring");
        if (t.Contains("surprise") || t.Contains("shock") || t.Contains("[gasps]")) return Pick("shock");
        if (t.Contains("money") || t.Contains("rich") || t.Contains("business")) return Pick("money");
        if (t.Contains("curios") || t.Contains("question") || t.Contains("think")) return Pick("thinking");
        return null;
    }

    /// <summary>
    /// Proposes overlays for a clip from its reaction tags: one sticker per tagged line, placed near the top-right
    /// so it rarely covers a face, starting a moment after the line begins.
    /// </summary>
    public static List<OverlayItem> Suggest(ShortSuggestion s, IReadOnlyList<TranscriptSegment> clipSegments)
    {
        var result = new List<OverlayItem>();
        foreach (var seg in clipSegments)
        {
            if (!TranscriptEvents.ContainsReaction(seg.Text)) continue;
            var name = ForReaction(seg.Text);
            if (name is null) continue;
            bool intense = TranscriptEvents.ContainsIntense(seg.Text);
            result.Add(new OverlayItem
            {
                Time = Math.Round(Math.Max(0, seg.Start + 0.2), 1),
                Duration = Math.Clamp(seg.Duration + 0.5, 1.5, 3.5),
                File = name,
                X = 78, Y = 22,
                Size = intense ? 34 : 26,
                Animation = intense ? "shake" : "pop"
            });
        }
        // avoid stacking two stickers at the same time: stagger overlaps
        for (int i = 1; i < result.Count; i++)
            if (result[i].Time < result[i - 1].End) result[i].X = result[i - 1].X > 50 ? 22 : 78;
        return result;
    }
}
