using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Anthropic;
using Anthropic.Models.Messages;
using ShortGenerator.Models;

namespace ShortGenerator.Services;

/// <summary>
/// Translates the shorts that will be generated into English: their caption lines (keyed by number so nothing
/// drifts and the original timing is kept) and their post texts (title, caption, hashtags, per-network text).
/// One request covers every short; it can go through Claude (API key) or be copied to ChatGPT and pasted back.
/// </summary>
public sealed class CaptionTranslator
{
    /// <summary>The work for one request: numbered transcript lines and the shorts whose post text is needed.</summary>
    public sealed class Job
    {
        public List<(int Id, TranscriptSegment Line)> Lines { get; } = new();
        public List<(string Key, ShortSuggestion Short)> Shorts { get; } = new();
        public string SourceLanguage { get; init; } = "the original language";
        public bool IsEmpty => Lines.Count == 0 && Shorts.Count == 0;
    }

    /// <summary>
    /// Lines of the given shorts without an up-to-date English version (missing, or the original changed since),
    /// and the shorts with no English post text yet. A line shared by two shorts is sent once.
    /// </summary>
    public static Job Plan(IEnumerable<ShortSuggestion> shorts, Transcript transcript, string sourceLanguage, bool forceLines = false)
    {
        var job = new Job { SourceLanguage = sourceLanguage };
        var seen = new HashSet<TranscriptSegment>(ReferenceEqualityComparer.Instance);
        int id = 1, key = 1;
        foreach (var s in shorts)
        {
            foreach (var seg in transcript.Segments.Where(x => x.End > s.StartSeconds && x.Start < s.EndSeconds))
            {
                if (string.IsNullOrWhiteSpace(seg.Text) || !seen.Add(seg)) continue;
                if (forceLines || seg.EnglishIsStale) job.Lines.Add((id++, seg));
            }
            if (s.English is null) job.Shorts.Add(($"s{key++}", s));
        }
        return job;
    }

    private const string Rules =
        "You translate short-form video captions and post texts into natural, spoken English for TikTok, Instagram Reels and YouTube Shorts. " +
        "Keep the meaning, the slang, the jokes and the energy; prefer how an English-speaking creator would say it over a literal translation. " +
        "Keep caption lines short and punchy; never merge, split, drop or reorder lines. " +
        "Keep bracketed reaction tags such as [laughs], [applause] or [shouting] as they are. Keep names, brands and numbers. " +
        "Hashtags and keywords: English, relevant, without the # symbol.";

    public static string BuildPrompt(Job job)
    {
        var sb = new StringBuilder();
        sb.AppendLine(Rules);
        sb.AppendLine();
        sb.AppendLine($"Source language: {job.SourceLanguage}.");
        if (job.Lines.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("# Caption lines (translate each one; keep the same id)");
            foreach (var (id, line) in job.Lines) sb.AppendLine($"{id}. {line.Text.Trim()}");
        }
        if (job.Shorts.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("# Post texts (write the English version of each short's texts; keep the same key)");
            foreach (var (key, s) in job.Shorts)
            {
                sb.AppendLine($"[{key}] title: {s.Title}");
                sb.AppendLine($"  hook: {s.Hook}");
                sb.AppendLine($"  caption: {s.SuggestedCaption}");
                sb.AppendLine($"  hashtags: {string.Join(" ", s.Hashtags ?? new List<string>())}");
                foreach (var (name, p) in new[] { ("youtube", s.Youtube), ("tiktok", s.Tiktok), ("instagram", s.Instagram) })
                    if (p is not null) sb.AppendLine($"  {name}: title \"{p.Title}\" | text \"{p.Description.Replace("\n", " / ")}\" | tags {string.Join(", ", p.Tags)}");
            }
        }
        sb.AppendLine();
        sb.AppendLine("# Output format");
        sb.AppendLine("Reply with ONLY a JSON object, no markdown, no commentary, no citation or source markers inside the strings, exactly in this shape:");
        sb.AppendLine("{\"lines\":[{\"id\":1,\"en\":\"...\"}],");
        sb.AppendLine(" \"shorts\":[{\"key\":\"s1\",\"title\":\"...\",\"caption\":\"...\",\"hashtags\":[\"...\"],");
        sb.AppendLine("   \"youtube\":{\"title\":\"...\",\"description\":\"...\",\"tags\":[\"...\"]},");
        sb.AppendLine("   \"tiktok\":{\"title\":\"\",\"description\":\"...\",\"tags\":[\"...\"]},");
        sb.AppendLine("   \"instagram\":{\"title\":\"\",\"description\":\"...\",\"tags\":[\"...\"]}}]}");
        sb.AppendLine("Every id listed above must appear in \"lines\" and every key in \"shorts\".");
        return sb.ToString();
    }

    public sealed record Result(Dictionary<int, string> Lines, Dictionary<string, EnglishPost> Shorts);

    /// <summary>Reads the answer: tolerant to code fences, text around the JSON and ChatGPT citation markers.</summary>
    public static Result Parse(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) throw new FormatException("The pasted text is empty.");
        var text = ChatGptExchange.StripCitations(raw).Trim();
        text = Regex.Replace(text, @"^```[a-zA-Z]*\s*", "", RegexOptions.Multiline).Replace("```", "");
        int first = text.IndexOf('{'), last = text.LastIndexOf('}');
        if (first < 0 || last <= first) throw new FormatException("Could not find a JSON object in the answer.");
        using var doc = JsonDocument.Parse(text[first..(last + 1)], new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        var root = doc.RootElement;
        var lines = new Dictionary<int, string>();
        if (root.TryGetProperty("lines", out var ls) && ls.ValueKind == JsonValueKind.Array)
            foreach (var l in ls.EnumerateArray())
            {
                int id = l.TryGetProperty("id", out var i) ? (i.ValueKind == JsonValueKind.Number ? i.GetInt32() : int.TryParse(i.GetString(), out var n) ? n : -1) : -1;
                var en = Str(l, "en") ?? Str(l, "text") ?? Str(l, "english");
                if (id > 0 && !string.IsNullOrWhiteSpace(en)) lines[id] = en.Trim();
            }
        var shorts = new Dictionary<string, EnglishPost>(StringComparer.OrdinalIgnoreCase);
        if (root.TryGetProperty("shorts", out var ss) && ss.ValueKind == JsonValueKind.Array)
            foreach (var s in ss.EnumerateArray())
            {
                var key = Str(s, "key");
                if (string.IsNullOrWhiteSpace(key)) continue;
                shorts[key] = new EnglishPost
                {
                    Title = Str(s, "title") ?? "",
                    Caption = Str(s, "caption") ?? "",
                    Hashtags = Tags(s, "hashtags"),
                    Youtube = Net(s, "youtube"), Tiktok = Net(s, "tiktok"), Instagram = Net(s, "instagram"),
                };
            }
        if (lines.Count == 0 && shorts.Count == 0) throw new FormatException("The answer has no translated lines or post texts.");
        return new Result(lines, shorts);
    }

    /// <summary>Writes the translation into the lines (remembering the original they came from) and the shorts.</summary>
    public static (int Lines, int Shorts, int Missing) Apply(Job job, Result r)
    {
        int lines = 0, shorts = 0, missing = 0;
        foreach (var (id, seg) in job.Lines)
        {
            if (r.Lines.TryGetValue(id, out var en)) { seg.English = en; seg.EnglishFrom = seg.Text; lines++; }
            else missing++;
        }
        foreach (var (key, s) in job.Shorts)
            if (r.Shorts.TryGetValue(key, out var p)) { s.English = p; shorts++; }
        return (lines, shorts, missing);
    }

    /// <summary>Same request through Claude with a strict JSON schema.</summary>
    public static async Task<string> TranslateWithClaudeAsync(string apiKey, string model, Job job, CancellationToken ct)
    {
        var client = new AnthropicClient { ApiKey = apiKey };
        object Net() => new
        {
            type = "object", additionalProperties = false, required = new[] { "title", "description", "tags" },
            properties = new { title = new { type = "string" }, description = new { type = "string" }, tags = new { type = "array", items = new { type = "string" } } }
        };
        var schemaObj = new
        {
            type = "object", additionalProperties = false, required = new[] { "lines", "shorts" },
            properties = new
            {
                lines = new
                {
                    type = "array",
                    items = new { type = "object", additionalProperties = false, required = new[] { "id", "en" }, properties = new { id = new { type = "integer" }, en = new { type = "string" } } }
                },
                shorts = new
                {
                    type = "array",
                    items = new
                    {
                        type = "object", additionalProperties = false,
                        required = new[] { "key", "title", "caption", "hashtags", "youtube", "tiktok", "instagram" },
                        properties = new
                        {
                            key = new { type = "string" }, title = new { type = "string" }, caption = new { type = "string" },
                            hashtags = new { type = "array", items = new { type = "string" } },
                            youtube = Net(), tiktok = Net(), instagram = Net(),
                        }
                    }
                }
            }
        };
        var schema = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(JsonSerializer.Serialize(schemaObj))!;
        var response = await client.Messages.Create(new MessageCreateParams
        {
            Model = string.IsNullOrWhiteSpace(model) ? "claude-opus-5" : model,
            MaxTokens = 16000,
            System = Rules,
            OutputConfig = new OutputConfig { Format = new JsonOutputFormat { Schema = schema } },
            Messages = [new() { Role = Role.User, Content = BuildPrompt(job) }]
        }, cancellationToken: ct);
        if (response.StopReason?.ToString() == "refusal") throw new InvalidOperationException("Claude declined to translate this content.");
        var json = string.Concat(response.Content.Select(b => b.Value).OfType<TextBlock>().Select(t => t.Text));
        if (string.IsNullOrWhiteSpace(json)) throw new InvalidOperationException("Claude returned an empty translation.");
        return json;
    }

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static List<string> Tags(JsonElement e, string name)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var v)) return new();
        IEnumerable<string> raw = v.ValueKind switch
        {
            JsonValueKind.Array => v.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString() ?? ""),
            JsonValueKind.String => (v.GetString() ?? "").Split(new[] { ',', ' ', '\n' }, StringSplitOptions.RemoveEmptyEntries),
            _ => Array.Empty<string>(),
        };
        return raw.Select(t => t.Trim().TrimStart('#')).Where(t => t.Length > 0).ToList();
    }

    private static NetworkPost? Net(JsonElement e, string name)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.Object) return null;
        return new NetworkPost { Title = Str(v, "title") ?? "", Description = Str(v, "description") ?? "", Tags = Tags(v, "tags") };
    }
}
