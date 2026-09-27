using ShortGenerator.Forms.Controls;
using ShortGenerator.Services;

namespace ShortGenerator.Forms;

/// <summary>
/// "Sign in with galiluna" with a device code: the app asks galiluna for a short code, shows it large,
/// opens the browser where the person signs in as usual and approves the code, and polls until galiluna
/// hands over a personal API key. No password enters this app, no local port is opened, and the key is
/// stored DPAPI-protected and never shown or logged. A manual paste fallback is behind an expander.
/// </summary>
public sealed class GaliLunaSignInForm : Form
{
    private readonly AppSettings _settings;

    private readonly ComboBox _env = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 300 };
    private readonly TextBox _url = new() { Width = 300 };
    private readonly FancyButton _signIn = new() { Text = "Sign in with galiluna", Width = 300, Height = 40, Glyph = "" };
    private readonly FancyButton _cancel = new() { Text = "Cancel", Width = 120, Visible = false };
    private readonly Label _status = new() { AutoSize = true, MaximumSize = new Size(430, 0), ForeColor = Color.DimGray };
    private readonly BrandProgressBar _spinner = new() { Width = 300, Visible = false };

    // the code card: shown while a sign-in is in progress
    private readonly Panel _codeCard = new() { Width = 430, Height = 132, Visible = false };
    private readonly Label _codeTitle = new() { Text = "Approve this code on galiluna", AutoSize = false, Height = 22, TextAlign = ContentAlignment.MiddleCenter };
    private readonly Label _code = new() { AutoSize = false, Height = 56, TextAlign = ContentAlignment.MiddleCenter };
    private readonly LinkLabel _codeHelp = new() { AutoSize = false, Height = 40, TextAlign = ContentAlignment.MiddleCenter };

    private readonly CheckBox _advanced = new() { Text = "Advanced: paste a key instead", AutoSize = true, Appearance = Appearance.Normal };
    private readonly Panel _advancedPanel = new() { AutoSize = true, Visible = false };
    private readonly TextBox _key = new() { UseSystemPasswordChar = true, Width = 300, PlaceholderText = "glk_..." };
    private readonly FancyButton _test = new() { Text = "Test and save", Width = 140 };

    private CancellationTokenSource? _cts;
    private string? _verificationUrl;

    /// <summary>Set when the form closes after a successful sign-in (also on the manual path).</summary>
    [System.ComponentModel.Browsable(false), System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public GaliLunaClient.Accounts? Accounts { get; private set; }

    public GaliLunaSignInForm(AppSettings settings)
    {
        _settings = settings;
        Text = "Sign in with galiluna";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(480, 560);
        BackColor = Theme.Bg;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(24, 16, 24, 16), AutoSize = true };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        // brand row
        var brand = new Panel { Height = 56, Width = 430 };
        var mark = Theme.LoadImage("galiluna-icon.png");
        var logo = Theme.LoadImage("galiluna-logo.png");
        brand.Paint += (_, e) =>
        {
            e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            int x = 0;
            if (mark is not null) { e.Graphics.DrawImage(mark, new Rectangle(0, 4, 48, 48)); x = 56; }
            if (logo is not null) e.Graphics.DrawImage(logo, new Rectangle(x, 14, (int)(logo.Width * (28.0 / logo.Height)), 28));
        };
        root.Controls.Add(brand);
        root.Controls.Add(new Label { Text = "Publish your shorts to the Instagram, TikTok and YouTube accounts connected on galiluna.", AutoSize = true, MaximumSize = new Size(430, 0), ForeColor = Theme.TextSecondary, Margin = new Padding(0, 4, 0, 12) });
        root.Controls.Add(new Label { Text = "Environment", AutoSize = true, ForeColor = Theme.TextMuted });
        root.Controls.Add(_env);
        root.Controls.Add(_url);
        root.Controls.Add(new Label { Text = "Your browser opens galiluna, where you sign in as usual and approve a short code shown here. No password is typed into this app.", AutoSize = true, MaximumSize = new Size(430, 0), ForeColor = Theme.TextMuted, Margin = new Padding(0, 10, 0, 8) });
        var actions = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        actions.Controls.Add(_signIn);
        actions.Controls.Add(_cancel);
        root.Controls.Add(actions);

        // code card
        _codeCard.Margin = new Padding(0, 12, 0, 4);
        _codeCard.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var r = new Rectangle(0, 0, _codeCard.Width - 1, _codeCard.Height - 1);
            using var path = FancyButton.Rounded(r, 14);
            using var fill = new SolidBrush(Theme.SurfaceSoft);
            using var pen = new Pen(Theme.Border);
            e.Graphics.FillPath(fill, path);
            e.Graphics.DrawPath(pen, path);
        };
        _codeTitle.Font = Theme.Body(9.5f); _codeTitle.ForeColor = Theme.TextSecondary; _codeTitle.BackColor = Color.Transparent;
        _codeTitle.SetBounds(0, 12, 430, 22);
        _code.Font = Theme.HeadingFont(30f); _code.ForeColor = Theme.PurpleDeep; _code.BackColor = Color.Transparent;
        _code.SetBounds(0, 36, 430, 56);
        _codeHelp.Font = Theme.Body(8.5f); _codeHelp.BackColor = Color.Transparent; _codeHelp.LinkColor = Theme.Purple; _codeHelp.ActiveLinkColor = Theme.PurpleDeep;
        _codeHelp.SetBounds(12, 92, 406, 36);
        _codeHelp.LinkClicked += (_, _) => OpenBrowser(_verificationUrl);
        _codeCard.Controls.Add(_codeTitle);
        _codeCard.Controls.Add(_code);
        _codeCard.Controls.Add(_codeHelp);
        root.Controls.Add(_codeCard);

        _spinner.Margin = new Padding(0, 8, 0, 4);
        root.Controls.Add(_spinner);
        root.Controls.Add(_status);

        _advanced.Margin = new Padding(0, 18, 0, 4);
        root.Controls.Add(_advanced);
        var adv = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        adv.Controls.Add(_key);
        adv.Controls.Add(_test);
        _advancedPanel.Controls.Add(adv);
        _advancedPanel.Controls.Add(new Label { Text = "Create a key on galiluna under Connections (Galiluna Shorts tile) or Connected apps, paste it, then Test.", AutoSize = true, MaximumSize = new Size(430, 0), ForeColor = Theme.TextMuted, Location = new Point(0, 40) });
        root.Controls.Add(_advancedPanel);

        Controls.Add(root);

        _env.Items.AddRange(new object[] { "Production (galiluna.com)", "Development (torontodeveloper.ca)", "Custom address" });
        _url.Text = string.IsNullOrWhiteSpace(settings.GaliLunaBaseUrl) ? GaliLunaClient.PrdBaseUrl : settings.GaliLunaBaseUrl;
        _env.SelectedIndex = _url.Text.TrimEnd('/') == GaliLunaClient.PrdBaseUrl ? 0 : _url.Text.TrimEnd('/') == GaliLunaClient.DevBaseUrl ? 1 : 2;
        _url.ReadOnly = _env.SelectedIndex != 2;
        _env.SelectedIndexChanged += (_, _) =>
        {
            if (_env.SelectedIndex == 0) _url.Text = GaliLunaClient.PrdBaseUrl;
            else if (_env.SelectedIndex == 1) _url.Text = GaliLunaClient.DevBaseUrl;
            _url.ReadOnly = _env.SelectedIndex != 2;
        };

        _signIn.Click += async (_, _) => await SignInAsync();
        _cancel.Click += (_, _) => _cts?.Cancel();
        _advanced.CheckedChanged += (_, _) => _advancedPanel.Visible = _advanced.Checked;
        _test.Click += async (_, _) => await TestPastedKeyAsync();

        Theme.Primary(_signIn);
        Theme.Apply(this);
        _cancel.Kind = ButtonKind.Danger;
    }

    private string BaseUrl => _url.Text.Trim().TrimEnd('/');

    // ------------------------------------------------------------------ device-code sign-in

    private async Task SignInAsync()
    {
        if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var baseUri) || (baseUri.Scheme != "https" && baseUri.Scheme != "http"))
        {
            SetStatus("Enter a valid galiluna address (https://...).", Theme.Warning);
            return;
        }
        if (_cts is not null) return;

        var baseUrl = BaseUrl;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        SetBusy(true);
        SetStatus("Asking galiluna for a sign-in code...", Theme.TextMuted);
        try
        {
            var start = await GaliLunaClient.DeviceStartAsync(baseUrl, ct);
            _verificationUrl = start.VerificationUrl;
            _code.Text = start.UserCode;
            _codeCard.Visible = true;
            bool opened = OpenBrowser(start.VerificationUrl);
            if (opened)
            {
                _codeHelp.Text = "Your browser opened galiluna. Didn't see it? Open it again";
                _codeHelp.LinkArea = new LinkArea(_codeHelp.Text.Length - 13, 13);
            }
            else
            {
                _codeHelp.Text = $"The browser could not be opened. Go to {start.VerificationUrlBase} and type the code.";
                _codeHelp.LinkArea = new LinkArea(0, 0);
            }
            SetStatus("Waiting for you to approve the code on galiluna...", Theme.TextMuted);

            var deadline = DateTime.UtcNow.AddSeconds(start.ExpiresInSeconds);
            var interval = TimeSpan.FromSeconds(start.IntervalSeconds);
            while (true)
            {
                await Task.Delay(interval, ct);
                if (DateTime.UtcNow >= deadline) { Finish("The code expired. Click Sign in to get a new one.", Theme.Warning); return; }
                GaliLunaClient.DevicePoll poll;
                try { poll = await GaliLunaClient.DevicePollAsync(baseUrl, start.PollToken, ct); }
                catch (GaliLunaClient.GaliLunaException ex) when (ex.StatusCode == 429) { await Task.Delay(interval, ct); continue; } // rate limited: back off one interval
                catch (HttpRequestException) { continue; } // transient network hiccup: keep polling until the deadline
                switch (poll.Status)
                {
                    case "pending":
                        continue;
                    case "approved" when !string.IsNullOrWhiteSpace(poll.Key):
                        SettingsStore.SetGaliLunaKey(_settings, poll.Key);
                        _settings.GaliLunaBaseUrl = baseUrl;
                        try { SettingsStore.Save(_settings); } catch { }
                        await ShowSignedInAsync();
                        return;
                    case "denied":
                        Finish("Sign-in was cancelled on galiluna.", Theme.Warning);
                        return;
                    case "expired":
                    case "claimed":
                    case "approved": // approved without a key = already handed over
                        Finish("The code expired. Click Sign in to get a new one.", Theme.Warning);
                        return;
                    default:
                        Finish("galiluna answered with an unknown state: " + poll.Status, Theme.Danger);
                        return;
                }
            }
        }
        catch (OperationCanceledException) { Finish("Sign-in cancelled.", Theme.TextMuted); }
        catch (Exception ex) { Finish("Could not reach galiluna: " + ex.Message, Theme.Danger); }
    }

    private void Finish(string message, Color color)
    {
        StopPolling();
        SetBusy(false);
        _codeCard.Visible = false;
        SetStatus(message, color);
    }

    private static bool OpenBrowser(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); return true; }
        catch { return false; }
    }

    /// <summary>Calls GET accounts with the stored key, shows who is signed in, and closes on success.</summary>
    private async Task ShowSignedInAsync()
    {
        StopPolling();
        _codeCard.Visible = false;
        SetStatus("Checking who is signed in...", Theme.TextMuted);
        try
        {
            using var client = SettingsStore.CreateGaliLunaClient(_settings) ?? throw new InvalidOperationException("No key stored.");
            Accounts = await client.GetAccountsAsync(CancellationToken.None);
            SetStatus(Summary(Accounts), Theme.Success);
            await Task.Delay(900);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            SetBusy(false);
            SetStatus("Signed in, but galiluna could not list your accounts: " + ex.Message, Theme.Danger);
        }
    }

    public static string Summary(GaliLunaClient.Accounts a) =>
        $"Signed in as {a.User.Email} at {a.Organization.Name}: {a.Instagram.Count} Instagram, {a.Youtube.Channels.Count} YouTube, TikTok {(a.Tiktok is null ? "no" : "yes")}.";

    // ------------------------------------------------------------------ manual paste fallback

    private async Task TestPastedKeyAsync()
    {
        var pasted = _key.Text.Trim();
        if (pasted.Length == 0) { SetStatus("Paste the key first.", Theme.Warning); return; }
        _test.Enabled = false;
        SetStatus("Contacting galiluna...", Theme.TextMuted);
        try
        {
            using var client = new GaliLunaClient(BaseUrl, pasted);
            var accounts = await client.GetAccountsAsync(CancellationToken.None);
            SettingsStore.SetGaliLunaKey(_settings, pasted);
            _settings.GaliLunaBaseUrl = BaseUrl;
            try { SettingsStore.Save(_settings); } catch { }
            Accounts = accounts;
            _key.Text = "";
            SetStatus(Summary(accounts), Theme.Success);
            await Task.Delay(900);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex) { SetStatus(ex.Message, Theme.Danger); }
        finally { _test.Enabled = true; }
    }

    // ------------------------------------------------------------------ plumbing

    private void StopPolling()
    {
        _cts?.Dispose();
        _cts = null;
    }

    private void SetBusy(bool busy)
    {
        _signIn.Enabled = !busy;
        _env.Enabled = !busy;
        _url.Enabled = !busy || _env.SelectedIndex != 2;
        _cancel.Visible = busy;
        _spinner.Visible = busy;
        _spinner.Value = busy ? 35 : 0;
        _advanced.Enabled = !busy;
    }

    private void SetStatus(string text, Color color) { _status.Text = text; _status.ForeColor = color; }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_cts is not null && !_cts.IsCancellationRequested) _cts.Cancel();
        base.OnFormClosing(e);
    }
}
