using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using ShortGenerator.Models;
using ShortGenerator.Services;

namespace ShortGenerator.Forms;

/// <summary>
/// In-app video player (Edge WebView2 + HTML5 video) that plays one clip range of the source video
/// with live caption overlay rendered in the selected caption style. Used by the Edit &amp; preview tab.
/// </summary>
public sealed class ClipPlayer : UserControl
{
    private readonly WebView2 _web = new() { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.Black };
    private readonly Label _fallback = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.White, BackColor = Color.Black, Padding = new Padding(20) };
    private bool _ready;
    private TaskCompletionSource<bool>? _navigated;

    /// <summary>Current playback position in seconds (absolute in the source video).</summary>
    public event Action<double>? TimeChanged;
    public event Action<bool>? PlayingChanged;
    /// <summary>Diagnostics from the page: "loaded WxH" or a decode error message.</summary>
    public event Action<string>? Status;
    /// <summary>User dragged the caption: new anchor as percent of the frame (x from left, y from top).</summary>
    public event Action<double, double>? CaptionMoved;

    public bool IsReady => _ready;

    public ClipPlayer()
    {
        BackColor = Color.Black;
        Controls.Add(_web);
        _fallback.Visible = false;
        Controls.Add(_fallback);
    }

    public async Task<bool> InitAsync()
    {
        if (_ready) return true;
        try
        {
            var dataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ShortGenerator", "WebView2");
            var env = await CoreWebView2Environment.CreateAsync(null, dataFolder);
            await _web.EnsureCoreWebView2Async(env);
            var s = _web.CoreWebView2.Settings;
            s.AreDefaultContextMenusEnabled = false;
            s.AreDevToolsEnabled = false;
            s.IsStatusBarEnabled = false;
            s.IsZoomControlEnabled = false;
            _web.CoreWebView2.WebMessageReceived += OnMessage;

            // Serve the page from disk (file://) so the <video> element can load the source video by file URI.
            // A page loaded via NavigateToString has an opaque origin and cannot fetch local media.
            var pageFolder = Path.Combine(dataFolder, "player");
            Directory.CreateDirectory(pageFolder);
            var pagePath = Path.Combine(pageFolder, "player.html");
            await File.WriteAllTextAsync(pagePath, Html);

            _navigated = new TaskCompletionSource<bool>();
            _web.CoreWebView2.NavigationCompleted += (_, _) => _navigated?.TrySetResult(true);
            _web.CoreWebView2.Navigate(new Uri(pagePath).AbsoluteUri);
            await _navigated.Task;
            _ready = true;
            return true;
        }
        catch (Exception ex)
        {
            _web.Visible = false;
            _fallback.Text = "The in-app player needs the Microsoft Edge WebView2 runtime.\n\n" + ex.Message +
                             "\n\nUse 'Render preview clip' to preview in your default video player instead.";
            _fallback.Visible = true;
            return false;
        }
    }

    private void OnMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            var root = doc.RootElement;
            if (root.TryGetProperty("t", out var t)) TimeChanged?.Invoke(t.GetDouble());
            if (root.TryGetProperty("playing", out var p)) PlayingChanged?.Invoke(p.GetBoolean());
            if (root.TryGetProperty("status", out var s)) Status?.Invoke(s.GetString() ?? "");
            if (root.TryGetProperty("pos", out var pos)) CaptionMoved?.Invoke(pos.GetProperty("x").GetDouble(), pos.GetProperty("y").GetDouble());
        }
        catch { }
    }

    private Task<string> Exec(string js) => _ready ? _web.CoreWebView2.ExecuteScriptAsync(js) : Task.FromResult("");

    public async Task LoadAsync(string videoPath, double start, double end)
    {
        if (!_ready) return;
        var src = new Uri(Path.GetFullPath(videoPath)).AbsoluteUri;
        await Exec($"load({JsonSerializer.Serialize(src)},{J(start)},{J(end)})");
    }

    public Task SetRangeAsync(double start, double end) => Exec($"setRange({J(start)},{J(end)})");
    public Task SeekAsync(double t) => Exec($"seek({J(t)})");
    public Task PlayAsync() => Exec("play()");
    public Task PauseAsync() => Exec("pause()");
    public Task TogglePlayAsync() => Exec("toggle()");

    /// <summary>Places the caption at a custom anchor (percent of frame) or back at the style default when null.</summary>
    public Task SetCaptionPositionAsync(double? xPercent, double? yPercent) =>
        Exec(xPercent is { } x && yPercent is { } y ? $"setPos({J(x)},{J(y)})" : "setPos(null,null)");

    /// <summary>
    /// Pushes caption chunks (absolute times) and the visual style to the page.
    /// </summary>
    public Task SetCaptionsAsync(IReadOnlyList<TranscriptSegment> clipSegments, double clipStart, CaptionStyle style, int fontSizeOverride, int wordsPerCaption, CropMode crop, bool enabled, bool includeReactions)
    {
        var chunks = enabled
            ? CaptionBuilder.Chunk(CaptionBuilder.PrepareText(clipSegments, includeReactions), Math.Max(1, wordsPerCaption))
                .Select(c => new
                {
                    s = c[0].Start + clipStart,
                    e = c[^1].End + clipStart,
                    w = c.Select(x => new { t = x.Word, s = x.Start + clipStart, e = x.End + clipStart }).ToArray()
                }).ToArray()
            : Array.Empty<object>();

        var st = new
        {
            font = style.FontName,
            size = fontSizeOverride > 0 ? fontSizeOverride : style.FontSize,
            bold = style.Bold, italic = style.Italic, upper = style.Uppercase,
            color = Css(style.PrimaryColor), highlight = Css(style.HighlightColor),
            outline = style.BorderStyle == 1 ? style.Outline : 0, outlineColor = Css(style.OutlineColor),
            shadow = style.Shadow, box = style.BorderStyle == 3, boxColor = Css(style.BackColor),
            blur = style.Blur, align = style.Alignment, marginV = style.MarginV,
            karaoke = style.Karaoke, pop = style.PopAnimation,
            crop = crop.ToString()
        };
        return Exec($"setCaptions({JsonSerializer.Serialize(chunks)},{JsonSerializer.Serialize(st)})");
    }

    private static string J(double d) => d.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
    private static string Css(Color c) => $"rgba({c.R},{c.G},{c.B},{(c.A / 255.0).ToString("F2", System.Globalization.CultureInfo.InvariantCulture)})";

    private const string Html = """
<!doctype html>
<html><head><meta charset="utf-8">
<style>
  html,body{margin:0;height:100%;background:#000;overflow:hidden;font-family:Arial}
  #wrap{position:absolute;inset:0;display:flex;align-items:center;justify-content:center}
  #frame{position:relative;background:#000;overflow:hidden}
  #bg{position:absolute;inset:0;width:100%;height:100%;object-fit:cover;filter:blur(24px) brightness(.7);display:none}
  #v{position:absolute;inset:0;width:100%;height:100%;object-fit:contain}
  #cap{position:absolute;left:4%;right:4%;text-align:center;line-height:1.15;white-space:pre-wrap;word-wrap:break-word;cursor:grab;user-select:none;touch-action:none}
  #cap.custom{left:auto;right:auto;width:92%;transform:translate(-50%,-50%)}
  #cap.dragging{cursor:grabbing;outline:2px dashed rgba(255,255,255,.6);outline-offset:6px}
  #cap:empty{pointer-events:none}
  #cap span.box{display:inline-block;padding:.15em .4em;border-radius:.15em}
  @keyframes pop{from{transform:scale(.8)}to{transform:scale(1)}}
  .pop{animation:pop 110ms ease-out}
</style></head>
<body>
<div id="wrap"><div id="frame"><video id="bg" muted></video><video id="v" playsinline></video><div id="cap"></div></div></div>
<script>
const v=document.getElementById('v'),bg=document.getElementById('bg'),frame=document.getElementById('frame'),cap=document.getElementById('cap');
let range={s:0,e:0},chunks=[],st=null,lastIdx=-1,lastPost=0,pos=null,drag=null;
function setPos(x,y){pos=(x==null||y==null)?null:{x,y};layout();}
function applyPos(){
  if(pos){cap.classList.add('custom');cap.style.left=pos.x+'%';cap.style.top=pos.y+'%';cap.style.bottom='';cap.style.transform='translate(-50%,-50%)';}
  else{cap.classList.remove('custom');}
}
cap.addEventListener('pointerdown',e=>{
  const r=frame.getBoundingClientRect(),c=cap.getBoundingClientRect();
  const cx=pos?pos.x:((c.left+c.width/2-r.left)/r.width*100),cy=pos?pos.y:((c.top+c.height/2-r.top)/r.height*100);
  drag={x0:e.clientX,y0:e.clientY,cx,cy,w:r.width,h:r.height};cap.classList.add('dragging');cap.setPointerCapture(e.pointerId);e.preventDefault();});
cap.addEventListener('pointermove',e=>{if(!drag)return;
  const x=Math.min(95,Math.max(5,drag.cx+(e.clientX-drag.x0)/drag.w*100)),y=Math.min(97,Math.max(3,drag.cy+(e.clientY-drag.y0)/drag.h*100));
  pos={x:+x.toFixed(1),y:+y.toFixed(1)};applyPos();});
cap.addEventListener('pointerup',e=>{if(!drag)return;drag=null;cap.classList.remove('dragging');if(pos)post({pos});});
function post(o){window.chrome&&window.chrome.webview&&window.chrome.webview.postMessage(o);}
function load(src,s,e){range={s,e};v.src=src;bg.src=src;v.currentTime=s;bg.currentTime=s;layout();}
function setRange(s,e){range={s,e};if(v.currentTime<s||v.currentTime>e){seek(s);}}
function seek(t){v.currentTime=t;bg.currentTime=t;render(true);}
function play(){if(v.currentTime>=range.e-0.05)v.currentTime=range.s;v.play();bg.play();}
function pause(){v.pause();bg.pause();}
function toggle(){v.paused?play():pause();}
function setCaptions(c,s){chunks=c;st=s;lastIdx=-1;layout();render(true);}
v.addEventListener('play',()=>post({playing:true}));
v.addEventListener('pause',()=>post({playing:false}));
v.addEventListener('loadedmetadata',()=>{layout();post({status:`loaded ${v.videoWidth}x${v.videoHeight}, ${v.duration.toFixed(1)}s`});});
v.addEventListener('error',()=>{const e=v.error;const msg='error: '+(e?('code '+e.code+' '+(e.message||'')):'unknown');
  fetch(v.currentSrc||v.src,{method:'HEAD'}).then(r=>post({status:msg+` | HEAD ${r.status} type=${r.headers.get('content-type')} ranges=${r.headers.get('accept-ranges')}`})).catch(x=>post({status:msg+' | fetch failed: '+x}));});
window.addEventListener('resize',layout);
function layout(){
  const W=window.innerWidth,H=window.innerHeight;let w,h;
  const vertical=st&&st.crop!=='Original';
  if(vertical){h=H;w=Math.round(H*9/16);if(w>W){w=W;h=Math.round(W*16/9);}}
  else{const ar=(v.videoWidth&&v.videoHeight)?v.videoWidth/v.videoHeight:16/9;w=W;h=Math.round(W/ar);if(h>H){h=H;w=Math.round(H*ar);}}
  frame.style.width=w+'px';frame.style.height=h+'px';
  const crop=st?st.crop:'VerticalCrop';
  v.style.objectFit=crop==='VerticalCrop'?'cover':'contain';
  bg.style.display=crop==='VerticalBlurredBackground'?'block':'none';
  if(!st)return;
  const scale=h>=w?h/1920:(h/1080)*0.72;
  const px=st.size*scale;
  cap.style.fontFamily=`"${st.font}",Arial,sans-serif`;
  cap.style.fontSize=px+'px';
  cap.style.fontWeight=st.bold?'bold':'normal';
  cap.style.fontStyle=st.italic?'italic':'normal';
  cap.style.color=st.color;
  cap.style.textTransform=st.upper?'uppercase':'none';
  let shadow=[];
  if(st.outline>0){cap.style.webkitTextStroke=(st.outline*2*scale)+'px '+st.outlineColor;cap.style.paintOrder='stroke fill';}else{cap.style.webkitTextStroke='0';}
  if(st.shadow>0)shadow.push(`${st.shadow*2*scale}px ${st.shadow*2*scale}px 0 rgba(0,0,0,.7)`);
  if(st.blur>0)shadow.push(`0 0 ${st.blur*2*scale}px ${st.outlineColor}`,`0 0 ${st.blur*4*scale}px ${st.outlineColor}`);
  cap.style.textShadow=shadow.join(',');
  cap.style.filter='';
  const mv=st.marginV*scale;
  cap.style.top='';cap.style.bottom='';cap.style.transform='';
  if(st.align===8){cap.style.top=mv+'px';}
  else if(st.align===5){cap.style.top='50%';cap.style.transform='translateY(-50%)';}
  else{cap.style.bottom=mv+'px';}
  applyPos();
}
function esc(s){return s.replace(/&/g,'&amp;').replace(/</g,'&lt;');}
function render(force){
  const t=v.currentTime;
  if(v.paused===false&&t>=range.e){pause();v.currentTime=range.s;post({t:range.s});return;}
  let idx=-1;
  for(let i=0;i<chunks.length;i++){if(t>=chunks[i].s&&t<chunks[i].e){idx=i;break;}}
  if(idx!==lastIdx||force||(idx>=0&&st&&st.karaoke)){
    if(idx<0){cap.innerHTML='';}
    else{
      const c=chunks[idx];let html;
      if(st&&st.karaoke){html=c.w.map(w=>`<span style="color:${t>=w.s&&t<w.e||t>=w.e?st.highlight:st.color}">${esc(w.t)}</span>`).join(' ');}
      else{html=esc(c.w.map(w=>w.t).join(' '));}
      if(st&&st.box)html=`<span class="box" style="background:${st.boxColor}">${html}</span>`;
      if(idx!==lastIdx||force){cap.innerHTML=html;if(st&&st.pop){cap.classList.remove('pop');void cap.offsetWidth;cap.classList.add('pop');}}
      else{cap.innerHTML=html;}
    }
    lastIdx=idx;
  }
  const now=performance.now();
  if(now-lastPost>100||force){lastPost=now;post({t});}
}
(function loop(){render(false);requestAnimationFrame(loop);})();
</script>
</body></html>
""";
}
