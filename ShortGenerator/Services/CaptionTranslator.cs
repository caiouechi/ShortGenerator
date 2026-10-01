using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Anthropic;
using Anthropic.Models.Messages;
using ShortGenerator.Models;

namespace ShortGenerator.Services;

/// <summary>
/// Translates the shorts that will be generated into English. The request is organised short by short: each
/// short carries its post texts (title, caption, hashtags, per-network text) and its whole transcript, numbered
/// from 1 within that short, and the answer comes back as one object per short with its own numbered lines.
/// That keeps 10 or 20 shorts readable and checkable, and every English line lands on the row it belongs to
/// (the original timing is kept). It can go through Claude (API key) or be copied to ChatGPT and pasted back.
/// </summary>
public sealed class CaptionTranslator
{
    /// <summary>One short of the request: its key, its lines in order (numbered 1..n in the prompt).</summary>
    public sealed record Group(string Key, ShortSuggestion Short, List<TranscriptSegment> Lines);

    public sealed class Job
    {
        public List<Group> Groups { get; } = new();
        public string SourceLanguage { get; init; } = "the original language";
        public bool IsEmpty => Groups.Count == 0;
        /// <summary>Caption lines sent, counted per short (a line in two shorts counts twice).</summary>
        public int LineCount => Groups.Sum(g => g.Lines.Count);
    }

    /// <summary>
    /// The shorts that need English: those with no English post text yet or with a line missing / out of date
    /// (or all of them when <paramref name="forceLines"/>). Each one is sent with its whole transcript, as edited.
    /// </summary>
    public static Job Plan(IEnumerable<ShortSuggestion> shorts, Transcript transcript, string sourceLanguage, bool forceLines = false)
    {
        var job = new Job { SourceLanguage = sourceLanguage };
        int key = 1;
        foreach (var s in shorts)
        {
            var lines = transcript.Segments.Where(x => x.End > s.StartSeconds && x.Start < s.EndSeconds && !string.IsNullOrWhiteSpace(x.Text))
                .OrderBy(x => x.Start).ToList();
            if (forceLines || s.English is null || lines.Any(x => x.EnglishIsStale))
                job.Groups.Add(new Group($"s{key++}", s, lines));
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
        sb.AppendLine($"There are {job.Groups.Count} short{(job.Groups.Count == 1 ? "" : "s")} below. Translate each one on its own: its post texts and every one of its caption lines. " +
                      "Line numbers restart at 1 in every short.");
        foreach (var g in job.Groups)
        {
            var s = g.Short;
            sb.AppendLine();
            sb.AppendLine($"## [{g.Key}] \"{s.Title}\" ({Clock(s.StartSeconds)} - {Clock(s.EndSeconds)})");
            sb.AppendLine($"title: {s.Title}");
            sb.AppendLine($"hook: {s.Hook}");
            sb.AppendLine($"caption: {s.SuggestedCaption}");
            sb.AppendLine($"hashtags: {string.Join(" ", s.Hashtags ?? new List<string>())}");
            foreach (var (name, p) in new[] { ("youtube", s.Youtube), ("tiktok", s.Tiktok), ("instagram", s.Instagram) })
                if (p is not null) sb.AppendLine($"{name}: title \"{p.Title}\" | text \"{p.Description.Replace("\n", " / ")}\" | tags {string.Join(", ", p.Tags)}");
            sb.AppendLine($"lines ({g.Lines.Count}):");
            for (int i = 0; i < g.Lines.Count; i++) sb.AppendLine($"{i + 1}. {g.Lines[i].Text.Trim()}");
        }
        sb.AppendLine();
        sb.AppendLine("# Output format");
        sb.AppendLine("Reply with ONLY a JSON object, no markdown, no commentary, no citation or source markers inside the strings. " +
                      "One object per short, in the same order and with the same key; each short's \"lines\" has every line number of that short:");
        sb.AppendLine("{\"shorts\":[");
        sb.AppendLine("  {\"key\":\"s1\",\"title\":\"...\",\"caption\":\"...\",\"hashtags\":[\"...\"],");
        sb.AppendLine("   \"youtube\":{\"title\":\"...\",\"description\":\"...\",\"tags\":[\"...\"]},");
        sb.AppendLine("   \"tiktok\":{\"title\":\"\",\"description\":\"...\",\"tags\":[\"...\"]},");
        sb.AppendLine("   \"instagram\":{\"title\":\"\",\"description\":\"...\",\"tags\":[\"...\"]},");
        sb.AppendLine("   \"lines\":[{\"id\":1,\"en\":\"...\"},{\"id\":2,\"en\":\"...\"}]}");
        sb.AppendLine("]}");
        return sb.ToString();
    }

    private static string Clock(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"mm\:ss");
    }

    /// <summary>A short's translation: its post texts and its lines by number (1-based, within the short).</summary>
    public sealed record ShortResult(EnglishPost Post, Dictionary<int, string> Lines);

    public sealed record Result(Dictionary<string, ShortResult> Shorts);

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
        var shorts = new Dictionary<string, ShortResult>(StringComparer.OrdinalIgnoreCase);
        if (root.TryGetProperty("shorts", out var ss) && ss.ValueKind == JsonValueKind.Array)
        {
            int index = 1;
            foreach (var s in ss.EnumerateArray())
            {
                // a missing key falls back to the position, so an answer that dropped the keys still lines up
                var key = Str(s, "key");
                if (string.IsNullOrWhiteSpace(key)) key = $"s{index}";
                index++;
                var lines = new Dictionary<int, string>();
                if (s.TryGetProperty("lines", out var ls) && ls.ValueKind == JsonValueKind.Array)
                {
                    int pos = 1;
                    foreach (var l in ls.EnumerateArray())
                    {
                        int id = pos++;
                        string? en = null;
                        if (l.ValueKind == JsonValueKind.String) en = l.GetString(); // ["line 1", "line 2"] works too
                        else
                        {
                            if (l.TryGetProperty("id", out var i)) id = i.ValueKind == JsonValueKind.Number ? i.GetInt32() : int.TryParse(i.GetString(), out var n) ? n : id;
                            en = Str(l, "en") ?? Str(l, "text") ?? Str(l, "english");
                        }
                        if (!string.IsNullOrWhiteSpace(en)) lines[id] = en.Trim();
                    }
                }
                shorts[key] = new ShortResult(new EnglishPost
                {
                    Title = Str(s, "title") ?? "",
                    Caption = Str(s, "caption") ?? "",
                    Hashtags = Tags(s, "hashtags"),
                    Youtube = Net(s, "youtube"), Tiktok = Net(s, "tiktok"), Instagram = Net(s, "instagram"),
                }, lines);
            }
        }
        if (shorts.Count == 0) throw new FormatException("The answer has no \"shorts\" list.");
        return new Result(shorts);
    }

    /// <summary>
    /// Writes each short's answer onto its own rows (line n of the short = its n-th transcript line, remembering the
    /// original it was made from) and its post texts. Returns translated lines, shorts and lines left without English.
    /// </summary>
    public static (int Lines, int Shorts, int Missing) Apply(Job job, Result r)
    {
        int lines = 0, shorts = 0, missing = 0;
        foreach (var g in job.Groups)
        {
            if (!r.Shorts.TryGetValue(g.Key, out var res)) { missing += g.Lines.Count; continue; }
            g.Short.English = res.Post;
            shorts++;
            for (int i = 0; i < g.Lines.Count; i++)
            {
                if (res.Lines.TryGetValue(i + 1, out var en)) { g.Lines[i].English = Transcriber.CleanSpeakerMarks(en); g.Lines[i].EnglishFrom = g.Lines[i].Text; lines++; }
                else missing++;
            }
        }
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
            type = "object", additionalProperties = false, required = new[] { "shorts" },
            properties = new
            {
                shorts = new
                {
                    type = "array",
                    items = new
                    {
                        type = "object", additionalProperties = false,
                        required = new[] { "key", "title", "caption", "hashtags", "youtube", "tiktok", "instagram", "lines" },
                        properties = new
                        {
                            key = new { type = "string" }, title = new { type = "string" }, caption = new { type = "string" },
                            hashtags = new { type = "array", items = new { type = "string" } },
                            youtube = Net(), tiktok = Net(), instagram = Net(),
                            lines = new
                            {
                                type = "array",
                                items = new { type = "object", additionalProperties = false, required = new[] { "id", "en" }, properties = new { id = new { type = "integer" }, en = new { type = "string" } } }
                            },
                        }
                    }
                }
            }
        };
        var schema = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(JsonSerializer.Serialize(schemaObj))!;
        var response = await client.Messages.Create(new MessageCreateParams
        {
            Model = string.IsNullOrWhiteSpace(model) ? "claude-opus-5" : model,
            MaxTokens = 32000,
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
