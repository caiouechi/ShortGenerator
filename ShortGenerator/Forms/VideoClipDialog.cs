using System.Globalization;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using ShortGenerator.Forms.Controls;

namespace ShortGenerator.Forms;

/// <summary>
/// Picks the moment of another video to show over the short, and how it is framed. The source plays with normal
/// controls; "Start here" / "End here" mark the clip. Full screen: a 9:16 box over the source is dragged to frame it
/// and the mouse wheel zooms (what the viewer sees). Picture in picture: the whole source frame, placed and sized on
/// the short afterwards like an image.
/// </summary>
public sealed class VideoClipDialog : Form
{
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public double SourceStart { get; private set; }
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public double Length { get; private set; }
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool FullFrame { get; private set; } = true;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public double CropX { get; private set; } = 50;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public double CropY { get; private set; } = 50;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public double Zoom { get; private set; } = 1;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public double Aspect { get; private set; } = 9.0 / 16;
    private readonly ComboBox _shape = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140 };

    private readonly WebView2 _web = new() { Dock = DockStyle.Fill };
    private readonly NumericUpDown _start = new() { DecimalPlaces = 1, Increment = 0.5M, Maximum = 100000, Width = 90 };
    private readonly NumericUpDown _end = new() { DecimalPlaces = 1, Increment = 0.5M, Maximum = 100000, Width = 90 };
    private readonly RadioButton _full = new() { Text = "Full screen (9:16)", AutoSize = true, Checked = true };
    private readonly RadioButton _inset = new() { Text = "Picture in picture", AutoSize = true };
    private readonly Label _info = new() { AutoSize = true, ForeColor = Theme.TextMuted };
    private readonly string _path;
    private double _now, _duration;
    private bool _ready;

    public VideoClipDialog(string path, double sourceStart, double length, bool fullFrame, double cropX, double cropY, double zoom, double maxLength, double aspect = 9.0 / 16)
    {
        _path = path;
        Aspect = aspect;
        SourceStart = sourceStart; Length = length; FullFrame = fullFrame; CropX = cropX; CropY = cropY; Zoom = zoom;
        Text = "Video clip: " + Path.GetFileNameWithoutExtension(path);
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        MinimizeBox = false; ShowInTaskbar = false;
        ClientSize = new Size(1100, 760);
        MinimumSize = new Size(820, 560);
        BackColor = Theme.Bg;

        var head = new Label
        {
            Dock = DockStyle.Top, Height = 54, Padding = new Padding(16, 10, 16, 0), ForeColor = Theme.TextSecondary,
            Text = "Play the video and stop at the moment you want, then click \"Start here\" and \"End here\". Full screen: drag the 9:16 box to frame it, scroll to zoom. The short keeps its own sound and its captions on top.",
        };

        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(14, 10, 14, 12), WrapContents = true, BackColor = Theme.SurfaceStrong };
        var startHere = new FancyButton { Text = "Start here", Width = 120, Glyph = "" };
        var endHere = new FancyButton { Text = "End here", Width = 110, Glyph = "" };
        var preview = new FancyButton { Text = "Play the clip", Width = 130, Glyph = "" };
        var ok = new FancyButton { Text = "Use this clip", Width = 140, Height = 38 };
        var cancel = new FancyButton { Text = "Cancel", Width = 100, Height = 38 };
        Label L(string t) => new() { Text = t, AutoSize = true, Margin = new Padding(14, 9, 4, 0), ForeColor = Theme.TextSecondary };
        bar.Controls.Add(startHere);
        bar.Controls.Add(L("Start (s)")); bar.Controls.Add(_start);
        bar.Controls.Add(endHere);
        bar.Controls.Add(L("End (s)")); bar.Controls.Add(_end);
        bar.Controls.Add(preview);
        _full.Margin = new Padding(18, 8, 6, 0); _inset.Margin = new Padding(6, 8, 6, 0);
        bar.Controls.Add(_full); bar.Controls.Add(_inset);
        // picture in picture: the shape of the small window (the framing box takes it), shrunk and placed on the short later
        foreach (var (_, name) in Models.ImageOverlay.InsetShapes) _shape.Items.Add(name);
        _shape.SelectedIndex = Math.Max(0, Array.FindIndex(Models.ImageOverlay.InsetShapes, x => Math.Abs(x.Aspect - aspect) < 0.01));
        _shape.Margin = new Padding(4, 5, 6, 0);
        bar.Controls.Add(_shape);
        _info.Margin = new Padding(14, 9, 0, 0);
        bar.Controls.Add(_info);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(14, 6, 14, 12), BackColor = Theme.SurfaceStrong };
        buttons.Controls.Add(ok); buttons.Controls.Add(cancel);
        foreach (var b in new[] { startHere, endHere, preview }) b.Margin = new Padding(0, 2, 6, 2);

        Controls.Add(_web);
        Controls.Add(head);
        Controls.Add(bar);
        Controls.Add(buttons);
        Theme.Apply(this);
        Theme.Primary(ok);

        _start.Value = (decimal)Math.Max(0, Math.Round(sourceStart, 1));
        _end.Value = (decimal)Math.Max(0, Math.Round(sourceStart + length, 1));
        _full.Checked = fullFrame; _inset.Checked = !fullFrame;

        startHere.Click += (_, _) => { _start.Value = Clamp(_now); if (_end.Value <= _start.Value) _end.Value = Clamp(_now + Math.Min(5, maxLength)); ShowInfo(); };
        endHere.Click += (_, _) => { _end.Value = Clamp(Math.Max(_now, (double)_start.Value + 0.5)); ShowInfo(); };
        preview.Click += async (_, _) => await Js($"playRange({J((double)_start.Value)},{J((double)_end.Value)})");
        _start.ValueChanged += (_, _) => ShowInfo();
        _end.ValueChanged += (_, _) => ShowInfo();
        _full.CheckedChanged += async (_, _) => await PushShapeAsync();
        _shape.SelectedIndexChanged += async (_, _) => await PushShapeAsync();
        ok.Click += (_, _) =>
        {
            double a = (double)_start.Value, b = (double)_end.Value;
            if (b - a < 0.5) { AppDialog.Alert(this, "Pick the clip", "Mark where the clip starts and ends: it has to last at least half a second.", AppDialog.Kind.Warning); return; }
            SourceStart = a; Length = Math.Min(b - a, maxLength); FullFrame = _full.Checked;
            Aspect = Models.ImageOverlay.InsetShapes[Math.Max(0, _shape.SelectedIndex)].Aspect;
            DialogResult = DialogResult.OK; Close();
        };
        cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        CancelButton = cancel;
        Load += async (_, _) => await InitAsync();
        ShowInfo();
    }

    /// <summary>The framing box: 9:16 for full screen, the chosen shape for picture in picture (0 = the whole source frame).</summary>
    private async Task PushShapeAsync()
    {
        _shape.Enabled = _inset.Checked;
        double a = _full.Checked ? 9.0 / 16 : Models.ImageOverlay.InsetShapes[Math.Max(0, _shape.SelectedIndex)].Aspect;
        await Js($"setShape({J(a)})");
    }

    private decimal Clamp(double t) => (decimal)Math.Round(Math.Clamp(t, 0, _duration > 0 ? _duration : 100000), 1);

    private void ShowInfo()
    {
        double len = (double)_end.Value - (double)_start.Value;
        _info.Text = len > 0 ? $"Clip: {len:0.0} s" : "Mark the start and the end";
    }

    private Task<string> Js(string code) => _ready ? _web.CoreWebView2.ExecuteScriptAsync(code) : Task.FromResult("");
    private static string J(double d) => d.ToString("F3", CultureInfo.InvariantCulture);

    private async Task InitAsync()
    {
        try
        {
            var dataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ShortGenerator", "WebView2");
            var env = await CoreWebView2Environment.CreateAsync(null, dataFolder);
            await _web.EnsureCoreWebView2Async(env);
            _web.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            _web.CoreWebView2.Settings.AreDevToolsEnabled = false;
            _web.AllowExternalDrop = false;
            _web.CoreWebView2.WebMessageReceived += (_, e) =>
            {
                try
                {
                    using var doc = JsonDocument.Parse(e.WebMessageAsJson);
                    var r = doc.RootElement;
                    if (r.TryGetProperty("t", out var t)) _now = t.GetDouble();
                    if (r.TryGetProperty("dur", out var d)) _duration = d.GetDouble();
                    if (r.TryGetProperty("box", out var box))
                    {
                        CropX = box.GetProperty("x").GetDouble(); CropY = box.GetProperty("y").GetDouble(); Zoom = box.GetProperty("zoom").GetDouble();
                    }
                }
                catch { }
            };
            var folder = Path.Combine(dataFolder, "player");
            Directory.CreateDirectory(folder);
            var page = Path.Combine(folder, "clip.html");
            await File.WriteAllTextAsync(page, Html);
            var done = new TaskCompletionSource<bool>();
            _web.CoreWebView2.NavigationCompleted += (_, _) => done.TrySetResult(true);
            _web.CoreWebView2.Navigate(new Uri(page).AbsoluteUri);
            await done.Task;
            _ready = true;
            var src = new Uri(Path.GetFullPath(_path)).AbsoluteUri;
            await Js($"init({JsonSerializer.Serialize(src)},{J(SourceStart)},{(FullFrame ? "true" : "false")},{J(CropX)},{J(CropY)},{J(Zoom)})");
            await PushShapeAsync();
        }
        catch (Exception ex)
        {
            AppDialog.Alert(this, "Cannot show the video", "The video preview needs the Microsoft Edge WebView2 runtime. " + ex.Message, AppDialog.Kind.Error);
        }
    }

    private const string Html = """
<!doctype html>
<html><head><meta charset="utf-8">
<style>
  html,body{margin:0;height:100%;background:#0B1028;overflow:hidden;font-family:Arial}
  #wrap{position:absolute;inset:0 0 0 0;display:flex;align-items:center;justify-content:center}
  #frame{position:relative}
  video{display:block;width:100%;height:100%}
  #box{position:absolute;border:2px solid #fff;box-shadow:0 0 0 9999px rgba(11,16,40,.55);cursor:move;display:none;box-sizing:border-box}
  #box .lbl{position:absolute;left:0;top:-22px;background:#6A38FF;color:#fff;font:12px Arial;padding:2px 6px;border-radius:4px;white-space:nowrap}
</style></head>
<body><div id="wrap"><div id="frame"><video id="v" controls preload="auto"></video><div id="box"><span class="lbl">what the viewer sees</span></div></div></div>
<script>
const v=document.getElementById('v'),frame=document.getElementById('frame'),box=document.getElementById('box');
let full=true,shape=9/16,k={x:50,y:50,zoom:1},drag=null,stopAt=null;
function post(o){window.chrome&&window.chrome.webview&&window.chrome.webview.postMessage(o);}
function init(src,t,f,x,y,z){v.src=src;v.currentTime=t;full=f;k={x,y,zoom:z};}
function setFull(f){full=f;draw();}
function setShape(a){shape=a;draw();}
function playRange(a,b){v.currentTime=a;stopAt=b;v.play();}
function layout(){const W=innerWidth,H=innerHeight-44,ar=(v.videoWidth&&v.videoHeight)?v.videoWidth/v.videoHeight:16/9;
  let w=W,h=W/ar;if(h>H){h=H;w=H*ar;}frame.style.width=w+'px';frame.style.height=h+'px';draw();}
function draw(){box.style.display='block';
  const fw=frame.clientWidth,fh=frame.clientHeight;   // the box maps to the whole frame, exactly what the render crops
  const a=shape>0?shape:fw/fh;                            // 0 = the whole source frame
  let bh=fh/k.zoom,bw=bh*a;if(bw>fw/k.zoom){bw=fw/k.zoom;bh=bw/a;}
  let cx=k.x/100*fw,cy=k.y/100*fh;cx=Math.max(bw/2,Math.min(fw-bw/2,cx));cy=Math.max(bh/2,Math.min(fh-bh/2,cy));
  box.style.left=(cx-bw/2)+'px';box.style.top=(cy-bh/2)+'px';box.style.width=bw+'px';box.style.height=bh+'px';}
box.addEventListener('pointerdown',e=>{drag={x0:e.clientX,y0:e.clientY,cx:k.x,cy:k.y};box.setPointerCapture(e.pointerId);e.preventDefault();});
box.addEventListener('pointermove',e=>{if(!drag)return;const fw=frame.clientWidth,fh=frame.clientHeight;
  k.x=Math.max(0,Math.min(100,drag.cx+(e.clientX-drag.x0)/fw*100));k.y=Math.max(0,Math.min(100,drag.cy+(e.clientY-drag.y0)/fh*100));draw();});
box.addEventListener('pointerup',()=>{if(!drag)return;drag=null;post({box:k});});
box.addEventListener('wheel',e=>{e.preventDefault();k.zoom=Math.max(1,Math.min(4,k.zoom+(e.deltaY<0?0.1:-0.1)));draw();post({box:k});},{passive:false});
v.addEventListener('loadedmetadata',()=>{layout();post({dur:v.duration,box:k});});
v.addEventListener('timeupdate',()=>{post({t:v.currentTime});if(stopAt!==null&&v.currentTime>=stopAt){v.pause();stopAt=null;}});
v.addEventListener('seeked',()=>post({t:v.currentTime}));
addEventListener('resize',layout);
</script></body></html>
""";
}
