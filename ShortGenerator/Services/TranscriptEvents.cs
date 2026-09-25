using System.Text.RegularExpressions;

namespace ShortGenerator.Services;

/// <summary>
/// Non-speech reactions in a transcript (laughs, applause, music...). Whisper emits them as bracketed
/// or parenthesised notes such as "[Laughter]", "(risos)" or "[música]". We normalise them to a small
/// canonical set like "[laughs]" so prompts and captions can rely on a stable spelling, or strip them.
/// </summary>
public static class TranscriptEvents
{
    // Anything in [] or () that is short and non-sentence-like is treated as an annotation.
    private static readonly Regex Annotation = new(@"[\[\(]\s*([^\]\)]{1,40}?)\s*[\]\)]", RegexOptions.Compiled);

    private static readonly (Regex Pattern, string Tag)[] Canonical =
    {
        (new Regex(@"big laugh", RegexOptions.IgnoreCase | RegexOptions.Compiled), BigLaugh),
        (new Regex(@"loud cheer", RegexOptions.IgnoreCase | RegexOptions.Compiled), LoudCheering),
        (new Regex(@"shout|grit[ao]|scream|yell|berr", RegexOptions.IgnoreCase | RegexOptions.Compiled), Shouting),
        (new Regex(@"laugh|risada|risos|rindo|rire|risa|lach|haha|kkk|chuckl|giggl", RegexOptions.IgnoreCase | RegexOptions.Compiled), "[laughs]"),
        (new Regex(@"applau|aplaus|clap|palmas", RegexOptions.IgnoreCase | RegexOptions.Compiled), "[applause]"),
        (new Regex(@"music|música|musica|song|canção|♪|♫", RegexOptions.IgnoreCase | RegexOptions.Compiled), "[music]"),
        (new Regex(@"cheer|vibra|torcida", RegexOptions.IgnoreCase | RegexOptions.Compiled), "[cheering]"),
        (new Regex(@"cry|cries|chora|chorando|sob|lágrimas|lagrimas", RegexOptions.IgnoreCase | RegexOptions.Compiled), "[crying]"),
        (new Regex(@"sigh|suspir", RegexOptions.IgnoreCase | RegexOptions.Compiled), "[sighs]"),
        (new Regex(@"gasp|surpres|ofeg", RegexOptions.IgnoreCase | RegexOptions.Compiled), "[gasps]"),
        (new Regex(@"silence|silêncio|silencio|pause|pausa|inaudible|inaudível|unintelligible", RegexOptions.IgnoreCase | RegexOptions.Compiled), "[pause]"),
    };

    /// <summary>Tags added by the loudness analysis for unusually intense moments.</summary>
    public const string BigLaugh = "[big laugh]";
    public const string LoudCheering = "[loud cheering]";
    public const string Shouting = "[shouting]";

    private static readonly string[] IntenseTags = { BigLaugh, LoudCheering, Shouting };

    /// <summary>
    /// Upgrades a line's tags based on how loud its peak is compared with normal speech:
    /// "[laughs]" becomes "[big laugh]", "[cheering]" becomes "[loud cheering]", and a very loud line
    /// with no reaction tag gets "[shouting]" (anger, screams, excitement).
    /// </summary>
    public static string ApplyIntensity(string text, bool intense)
    {
        if (!intense) return text;
        if (text.Contains("[laughs]")) return text.Replace("[laughs]", BigLaugh);
        if (text.Contains("[cheering]")) return text.Replace("[cheering]", LoudCheering);
        if (text.Contains("[applause]") || text.Contains("[music]") || text.Contains("[crying]")) return text;
        return Clean(text + " " + Shouting);
    }

    /// <summary>Initial prompt that nudges Whisper into writing reactions as bracketed tags.</summary>
    public static string PromptFor(string language) => language.StartsWith("pt", StringComparison.OrdinalIgnoreCase)
        ? "[risos] [aplausos] [música] [choro] [gritos] E aí ele falou... [risos]"
        : language.StartsWith("es", StringComparison.OrdinalIgnoreCase)
            ? "[risas] [aplausos] [música] [llanto] Y entonces dijo... [risas]"
            : "[laughs] [applause] [music] [crying] [cheering] And then he said... [laughs]";

    /// <summary>Rewrites annotations into canonical tags. Unknown annotations are removed.</summary>
    public static string Normalize(string text) => Clean(Annotation.Replace(text, m =>
    {
        var inner = m.Groups[1].Value;
        foreach (var (pattern, tag) in Canonical)
            if (pattern.IsMatch(inner)) return " " + tag + " ";
        return " ";
    }));

    /// <summary>Removes every bracketed / parenthesised annotation.</summary>
    public static string Strip(string text) => Clean(Annotation.Replace(text, " "));

    public static bool IsReactionOnly(string text) => Strip(text).Length == 0 && text.Trim().Length > 0;

    public static bool ContainsReaction(string text) =>
        Canonical.Any(c => text.Contains(c.Tag, StringComparison.OrdinalIgnoreCase)) ||
        IntenseTags.Any(t => text.Contains(t, StringComparison.OrdinalIgnoreCase));

    public static bool ContainsIntense(string text) => IntenseTags.Any(t => text.Contains(t, StringComparison.OrdinalIgnoreCase));

    private static string Clean(string s) => Regex.Replace(s, @"\s{2,}", " ").Trim();
}
