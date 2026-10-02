using ShortGenerator.Forms.Controls;
using ShortGenerator.Services;

namespace ShortGenerator.Forms;

/// <summary>One account a finished render is published to: "instagram" / "tiktok" / "youtube" and galiluna's id.</summary>
public sealed record AutoDestination(string Network, int Id, string Label)
{
    public string Key => $"{Network}:{Id}";
}

/// <summary>
/// Render preview from Edit &amp; preview: which caption versions to render and, for each one, the accounts to
/// publish it to as soon as it is ready. The posts use the text written for each network (edited on Publish, not
/// here). The choices are remembered per version, so "original to the Brazilian account, English to the
/// international ones and TikTok" is one click the next time.
/// </summary>
public sealed class RenderPublishDialog : Form
{
    public List<string> Languages { get; } = new();
    /// <summary>Per version ("original" / "en"): where it goes once rendered. Empty = render only.</summary>
    public Dictionary<string, List<AutoDestination>> Publish { get; } = new();

    private sealed class Version
    {
        public required string Lang { get; init; }
        public required CheckBox Render { get; init; }
        public List<(CheckBox Box, AutoDestination Dest)> Targets { get; } = new();
    }

    private readonly List<Version> _versions = new();
    private readonly FancyButton _go = new() { Text = "Render", Height = 38 };

    /// <param name="accounts">galiluna's connected accounts; null when not signed in (publishing is then not offered).</param>
    /// <param name="tiktokHow">How TikTok posts go, from the Publish step's TikTok card ("post directly, PUBLIC_TO_EVERYONE").</param>
    /// <param name="remembered">The destinations ticked last time, per version.</param>
    public RenderPublishDialog(string shortTitle, bool english, int missingEnglish, GaliLunaClient.Accounts? accounts, string? tiktokHow,
        IReadOnlyDictionary<string, List<string>> remembered)
    {
        Text = "Render preview";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Theme.Bg;
        const int width = 600, textWidth = width - 60;

        var root = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(22, 18, 22, 8), BackColor = Theme.Bg };
        root.Controls.Add(new Label { Text = "Render this short", AutoSize = true, Font = Theme.HeadingFont(12.5f), ForeColor = Theme.Heading, Margin = new Padding(0, 0, 0, 2) });
        root.Controls.Add(new Label { Text = shortTitle, AutoSize = true, MaximumSize = new Size(textWidth, 0), ForeColor = Theme.TextSecondary, Margin = new Padding(0, 0, 0, 12), UseMnemonic = false });

        var langs = new List<(string Lang, string Title)> { ("original", "original captions") };
        if (english) langs.Add(("en", "English captions"));
        foreach (var (lang, title) in langs)
        {
            var card = new Card { Width = textWidth, Margin = new Padding(0, 0, 0, 12), Padding = new Padding(16, 14, 16, 12) };
            var body = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Theme.Elevated };
            var render = new CheckBox { Text = "Render with " + title, Checked = true, AutoSize = true, Font = Theme.Body(10f, FontStyle.Bold), ForeColor = Theme.Heading, Margin = new Padding(0, 0, 0, 4) };
            body.Controls.Add(render);
            if (lang == "en" && missingEnglish > 0)
                body.Controls.Add(new Label { Text = $"{missingEnglish} line(s) have no English yet and will show the original text.", AutoSize = true, MaximumSize = new Size(textWidth - 40, 0), ForeColor = Theme.Warning, Margin = new Padding(20, 0, 0, 4) });
            var v = new Version { Lang = lang, Render = render };
            if (accounts is not null)
            {
                body.Controls.Add(new Label { Text = "Publish when ready (optional)", AutoSize = true, ForeColor = Theme.TextMuted, Margin = new Padding(20, 6, 0, 2) });
                var dests = new List<AutoDestination>();
                dests.AddRange(accounts.Instagram.Select(a => new AutoDestination("instagram", a.Id, a.Label)));
                if (accounts.Tiktok is { } t) dests.Add(new AutoDestination("tiktok", t.Id, t.Label));
                dests.AddRange(accounts.Youtube.Channels.Select(c => new AutoDestination("youtube", c.Id, c.Label)));
                var ticked = remembered.TryGetValue(lang, out var r) ? r.ToHashSet() : new HashSet<string>();
                foreach (var d in dests)
                {
                    var network = d.Network switch { "instagram" => "Instagram", "tiktok" => "TikTok", _ => "YouTube" };
                    var label = d.Network == "tiktok" && !string.IsNullOrWhiteSpace(tiktokHow) ? $"{network}  ·  {d.Label}  ({tiktokHow})" : $"{network}  ·  {d.Label}";
                    var box = new CheckBox { Text = label, AutoSize = true, Checked = ticked.Contains(d.Key), Margin = new Padding(20, 2, 0, 2), UseMnemonic = false };
                    box.CheckedChanged += (_, _) => Sync();
                    body.Controls.Add(box);
                    v.Targets.Add((box, d));
                }
                if (dests.Count == 0)
                    body.Controls.Add(new Label { Text = "No account is connected on galiluna yet.", AutoSize = true, ForeColor = Theme.TextMuted, Margin = new Padding(20, 2, 0, 2) });
            }
            render.CheckedChanged += (_, _) => { foreach (var (b, _) in v.Targets) b.Enabled = render.Checked; Sync(); };
            card.Controls.Add(body);
            body.SizeChanged += (_, _) => card.Height = body.Height + card.Padding.Vertical;
            // clip to the rounded shape so no square corner shows against the page
            card.SizeChanged += (_, _) => { using var gp = FancyButton.Rounded(new Rectangle(0, 0, card.Width, card.Height), card.Radius); card.Region = new Region(gp); };
            card.Height = body.PreferredSize.Height + card.Padding.Vertical;
            root.Controls.Add(card);
            _versions.Add(v);
        }

        var notes = accounts is null
            ? new[] { "Sign in with galiluna (Settings) to publish automatically when the render is ready." }
            : new[]
            {
                "Each post uses the text written for that network; to change a caption or hashtags, edit it on Publish before rendering, or publish from there.",
                "Posts go out as soon as the render finishes and show in the Publishing queue on Publish.",
            };
        foreach (var n in notes)
            root.Controls.Add(new Label { Text = n, AutoSize = true, MaximumSize = new Size(textWidth, 0), ForeColor = Theme.TextMuted, Margin = new Padding(0, 0, 0, 4) });

        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Padding = new Padding(22, 12, 22, 14), BackColor = Theme.SurfaceStrong };
        var cancel = new FancyButton { Text = "Cancel", Width = 100, Height = 38, Margin = new Padding(10, 0, 0, 0) };
        _go.Margin = new Padding(10, 0, 0, 0);
        bar.Controls.Add(_go);
        bar.Controls.Add(cancel);
        Controls.Add(root);
        Controls.Add(bar);

        _go.Click += (_, _) =>
        {
            foreach (var v in _versions.Where(v => v.Render.Checked))
            {
                Languages.Add(v.Lang);
                Publish[v.Lang] = v.Targets.Where(t => t.Box.Checked).Select(t => t.Dest).ToList();
            }
            DialogResult = DialogResult.OK;
            Close();
        };
        cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        AcceptButton = _go; CancelButton = cancel;
        Theme.Apply(this);
        Theme.Primary(_go);
        foreach (Control c in root.Controls) if (c is Card k) foreach (Control inner in k.Controls) inner.BackColor = Theme.Elevated;
        Sync();
        Load += (_, _) =>
        {
            int h = root.Controls.Cast<Control>().Sum(c => c.Height + c.Margin.Vertical) + root.Padding.Vertical + bar.Height + 8;
            ClientSize = new Size(width, Math.Min(h, Screen.FromControl(this).WorkingArea.Height - 80));
        };
    }

    /// <summary>The main button says what will happen: render, or render and publish N posts.</summary>
    public void Sync()
    {
        int renders = _versions.Count(v => v.Render.Checked);
        int posts = _versions.Where(v => v.Render.Checked).Sum(v => v.Targets.Count(t => t.Box.Checked));
        _go.Enabled = renders > 0;
        _go.Text = posts > 0 ? $"Render & publish ({posts})" : renders > 1 ? "Render both" : "Render";
        _go.Width = TextRenderer.MeasureText(_go.Text, _go.Font).Width + 48;
    }
}
