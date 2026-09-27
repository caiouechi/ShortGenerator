namespace ShortGenerator.Models;

/// <summary>
/// The spoken-language choices offered in the UI. "auto" lets Whisper detect the language, but because
/// detection can guess wrong on short or noisy audio, the two languages of the main audience (English and
/// Portuguese) are listed right after it so they can be forced with one click.
/// </summary>
public static class LanguageOptions
{
    /// <summary>Display name shown in the dropdown, and the Whisper language code it maps to.</summary>
    public sealed record Option(string Name, string Code);

    public static readonly IReadOnlyList<Option> All = new List<Option>
    {
        new("Auto-detect", "auto"),
        new("English", "en"),
        new("Portuguese", "pt"),
        new("Spanish", "es"),
        new("French", "fr"),
        new("German", "de"),
        new("Italian", "it"),
        new("Japanese", "ja"),
        new("Korean", "ko"),
        new("Chinese", "zh"),
    };

    public static string[] DisplayNames => All.Select(o => o.Name).ToArray();

    /// <summary>Index of the given code (case-insensitive); 0 (Auto-detect) when unknown or empty.</summary>
    public static int IndexOfCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return 0;
        for (int i = 0; i < All.Count; i++)
            if (string.Equals(All[i].Code, code.Trim(), StringComparison.OrdinalIgnoreCase)) return i;
        return 0;
    }

    /// <summary>The Whisper code for a dropdown index, or "auto" when out of range.</summary>
    public static string CodeAt(int index) => index >= 0 && index < All.Count ? All[index].Code : "auto";
}
