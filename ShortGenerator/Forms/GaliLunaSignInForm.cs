using ShortGenerator.Forms.Controls;
using ShortGenerator.Services;

namespace ShortGenerator.Forms;

/// <summary>
/// "Sign in with galiluna": the browser does the login on galiluna, galiluna mints a personal API key and
/// redirects to a loopback listener here. The key is stored DPAPI-protected; no password ever enters
/// this app and the key itself is never shown or logged. A manual paste fallback is behind an expander.
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

    private readonly CheckBox _advanced = new() { Text = "Advanced: paste a key instead", AutoSize = true, Appearance = Appearance.Normal };
    private readonly Panel _advancedPanel = new() { AutoSize = true, Visible = false };
    private readonly TextBox _key = new() { UseSystemPasswordChar = true, Width = 300, PlaceholderText = "glk_..." };
    private readonly FancyButton _test = new() { Text = "Test and save", Width = 140 };

    private LoopbackSignIn? _loopback;
    private CancellationTokenSource? _cts;

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
        ClientSize = new Size(480, 440);
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
        root.Controls.Add(new Label { Text = "Your browser opens galiluna, where you sign in as usual and approve this app. No password is typed here.", AutoSize = true, MaximumSize = new Size(430, 0), ForeColor = Theme.TextMuted, Margin = new Padding(0, 10, 0, 8) });
        var actions = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        actions.Controls.Add(_signIn);
        actions.Controls.Add(_cancel);
        root.Controls.Add(actions);
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

        _signIn.Click += (_, _) => StartSignIn();
        _cancel.Click += (_, _) => { UserCancelled = true; _cts?.Cancel(); };
        _advanced.CheckedChanged += (_, _) => _advancedPanel.Visible = _advanced.Checked;
        _test.Click += async (_, _) => await TestPastedKeyAsync();
        FormClosed += (_, _) => StopListener();

        Theme.Primary(_signIn);
        Theme.Apply(this);
        _cancel.Kind = ButtonKind.Danger;
    }

    private string BaseUrl => _url.Text.Trim().TrimEnd('/');

    // ------------------------------------------------------------------ loopback sign-in

    private void StartSignIn()
    {
        if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var baseUri) || (baseUri.Scheme != "https" && baseUri.Scheme != "http"))
        {
            SetStatus("Enter a valid galiluna address (https://...).", Theme.Warning);
            return;
        }

        try { _loopback = LoopbackSignIn.Start(); }
        catch (Exception ex) { SetStatus("Could not open a local port for the browser callback: " + ex.Message, Theme.Danger); return; }

        _cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        SetBusy(true);
        SetStatus("Waiting for you to approve in the browser...", Theme.TextMuted);

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(GaliLunaClient.AuthorizeUrl(BaseUrl, _loopback.Port, _loopback.State)) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            StopListener();
            SetBusy(false);
            SetStatus("Could not open the browser: " + ex.Message, Theme.Danger);
            return;
        }

        var loopback = _loopback;
        var ct = _cts.Token;
        // The wait runs off the UI thread; the result comes back through BeginInvoke.
        _ = Task.Run(async () =>
        {
            LoopbackSignIn.Result? result = null;
            string? failure = null;
            try { result = await loopback.WaitAsync(ct); }
            catch (Exception ex) { failure = "listener: " + ex.Message; }
            bool cancelled = result is null && failure is null;
            bool timedOut = cancelled && !UserCancelled;
            if (!IsDisposed) BeginInvoke(async () => await FinishAsync(result?.Key, result?.Error ?? failure, cancelled, timedOut));
        });
    }

    private bool UserCancelled;

    private async Task FinishAsync(string? key, string? error, bool cancelled, bool timedOut)
    {
        StopListener();
        if (key is not null)
        {
            SettingsStore.SetGaliLunaKey(_settings, key);
            _settings.GaliLunaBaseUrl = BaseUrl;
            try { SettingsStore.Save(_settings); } catch { }
            await ShowSignedInAsync();
            return;
        }
        SetBusy(false);
        if (error == "access_denied") SetStatus("Sign-in was cancelled on galiluna.", Theme.Warning);
        else if (error == "too_many_keys") SetStatus("You already have 10 keys on galiluna. Revoke one under Connected apps and try again.", Theme.Warning);
        else if (error is not null) SetStatus("galiluna reported: " + error, Theme.Danger);
        else if (timedOut) SetStatus("No answer from the browser after 5 minutes. Click Sign in to try again.", Theme.Warning);
        else if (cancelled) SetStatus("Sign-in cancelled.", Theme.TextMuted);
    }

    /// <summary>Calls GET accounts with the stored key, shows who is signed in, and closes on success.</summary>
    private async Task ShowSignedInAsync()
    {
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
        $"Signed in as {a.User.Email} at {a.Organization.Name}: {a.Instagram.Count} Instagram account{(a.Instagram.Count == 1 ? "" : "s")}, " +
        $"{a.Youtube.Channels.Count} YouTube channel{(a.Youtube.Channels.Count == 1 ? "" : "s")}, TikTok {(a.Tiktok is null ? "not connected" : "connected")}.";

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

    private void StopListener()
    {
        _loopback?.Dispose();
        _loopback = null;
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
        if (busy) UserCancelled = false;
    }

    private void SetStatus(string text, Color color) { _status.Text = text; _status.ForeColor = color; }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_cts is not null && !_cts.IsCancellationRequested) { UserCancelled = true; _cts.Cancel(); }
        base.OnFormClosing(e);
    }
}
