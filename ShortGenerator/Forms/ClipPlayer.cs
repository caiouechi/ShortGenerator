using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using ShortGenerator.Models;
using ShortGenerator.Services;

namespace ShortGenerator.Forms;

/// <summary>
/// In-app video player (Edge WebView2 + HTML5 video) that plays one clip range of the source video with
/// live caption overlay, follows the clip's camera cuts, and offers a camera edit mode where the user
/// drags a 9:16 box over the full frame. Used by the Edit &amp; preview tab.
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
    /// <summary>User dragged the caption: new anchor as percent of the output frame (x from left, y from top), and the exact video time.</summary>
    public event Action<double, double, double>? CaptionMoved;
    /// <summary>User moved / zoomed the camera box: center as percent of the source frame, zoom, and the exact video time.</summary>
    public event Action<double, double, double, double>? CameraMoved;
    /// <summary>User double-clicked the caption at this video time: the host answers with <see cref="BeginEditAsync"/>.</summary>
    public event Action<double>? EditRequested;
    /// <summary>User finished editing the caption line that was shown at this video time.</summary>
    public event Action<double, string>? TextEdited;
    /// <summary>User right-clicked the caption at this video time: the host selects that line in its transcript grid.</summary>
    public event Action<double>? LineRequested;
    /// <summary>User clicked an image layer on the video.</summary>
    public event Action<string>? LayerSelected;
    /// <summary>User moved (percent centre), resized (percent width) or rotated (degrees) an image layer.</summary>
    public event Action<string, double, double, double, double>? LayerChanged;

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
            _web.AllowExternalDrop = false; // dropping a file on the player would otherwise replace the player with that file

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
                             "\n\nUse 'Render preview' to preview in your default video player instead.";
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
            if (root.TryGetProperty("pos", out var pos)) CaptionMoved?.Invoke(pos.GetProperty("x").GetDouble(), pos.GetProperty("y").GetDouble(), pos.GetProperty("t").GetDouble());
            if (root.TryGetProperty("camera", out var cam)) CameraMoved?.Invoke(cam.GetProperty("x").GetDouble(), cam.GetProperty("y").GetDouble(), cam.GetProperty("zoom").GetDouble(), cam.GetProperty("t").GetDouble());
            if (root.TryGetProperty("editAt", out var ea)) EditRequested?.Invoke(ea.GetDouble());
            if (root.TryGetProperty("lineAt", out var la)) LineRequested?.Invoke(la.GetDouble());
            if (root.TryGetProperty("ovSel", out var os)) LayerSelected?.Invoke(os.GetString() ?? "");
            if (root.TryGetProperty("ov", out var ov))
                LayerChanged?.Invoke(ov.GetProperty("id").GetString() ?? "", ov.GetProperty("x").GetDouble(), ov.GetProperty("y").GetDouble(), ov.GetProperty("size").GetDouble(), ov.GetProperty("rot").GetDouble());
            if (root.TryGetProperty("edited", out var ed)) TextEdited?.Invoke(ed.GetProperty("t").GetDouble(), ed.GetProperty("text").GetString() ?? "");
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

    /// <summary>Camera cuts (times relative to the clip start) that the vertical-crop preview follows.</summary>
    public Task SetCameraAsync(List<CameraKeyframe> camera) =>
        Exec($"setCamera({JsonSerializer.Serialize(camera.OrderBy(k => k.Time).Select(k => new { t = k.Time, x = k.X, y = k.Y, zoom = k.Zoom }))})");

    /// <summary>Caption anchors over time (relative to the clip start). Empty = style default placement.</summary>
    public Task SetCaptionPositionsAsync(List<CaptionKeyframe> positions) =>
        Exec($"setCaptionPositions({JsonSerializer.Serialize(positions.OrderBy(k => k.Time).Select(k => new { t = k.Time, x = k.X, y = k.Y }))})");

    /// <summary>Camera edit mode: shows the whole source frame with a draggable 9:16 box (scroll to zoom).</summary>
    public Task SetCameraModeAsync(bool on) => Exec($"setCameraMode({(on ? "true" : "false")})");
    /// <summary>Image layers of the clip (times relative to the clip start); <paramref name="selectedId"/> gets a dashed outline.</summary>
    public Task SetLayersAsync(IEnumerable<ImageOverlay> layers, string? selectedId) =>
        Exec($"setLayers({JsonSerializer.Serialize(layers.Where(o => File.Exists(o.Path)).Select(o => new { id = o.Id, src = new Uri(Path.GetFullPath(o.Path)).AbsoluteUri, t = o.Start, end = o.End, x = o.X, y = o.Y, size = o.Size, rot = o.Rotation, style = o.Style, anim = o.Animation }))},{JsonSerializer.Serialize(selectedId)})");

    /// <summary>Replays the entrance of an image layer once (the layer must be inside its window).</summary>
    public Task PreviewLayerAsync(string id) => Exec($"previewLayer({JsonSerializer.Serialize(id)})");

    /// <summary>Opens the inline editor over the caption with the full transcript line shown at that moment.</summary>
    public Task BeginEditAsync(string text) => Exec($"beginEdit({JsonSerializer.Serialize(text)})");

    /// <summary>Approximates the render's colour treatment with a CSS filter on the preview video.</summary>
    public Task SetLookAsync(string css) => Exec($"setLook({System.Text.Json.JsonSerializer.Serialize(css)})");

    /// <summary>Pushes caption chunks (absolute times) and the visual style to the page.</summary>
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
  #cam{position:absolute;display:none;border:2px solid #A855F7;box-shadow:0 0 0 9999px rgba(0,0,0,.55);cursor:move;touch-action:none;box-sizing:border-box}
  #cam .lbl{position:absolute;left:0;top:-22px;background:#6A38FF;color:#fff;font:12px Arial;padding:2px 6px;border-radius:4px;white-space:nowrap}
  #ovl{position:absolute;inset:0;pointer-events:none}
  #ovl .ov{position:absolute;pointer-events:auto;cursor:move;touch-action:none;user-select:none}
  #ovl .ov .pic{box-sizing:border-box;width:100%}
  #ovl .ov img{display:block;width:100%;height:auto;pointer-events:none;-webkit-user-drag:none}
  #ovl .ov .pic.frame{background:#fff;box-shadow:0 10px 26px rgba(0,0,0,.45)}
  #ovl .ov .pic.card{border-style:solid;border-color:#fff;box-shadow:0 10px 26px rgba(0,0,0,.45);overflow:hidden}
  #ovl .ov .pic.circle{border-style:solid;border-color:#fff;border-radius:50%;overflow:hidden;box-shadow:0 10px 26px rgba(0,0,0,.45)}
  #ovl .ov .pic.circle img{aspect-ratio:1/1;object-fit:cover;object-position:50% 33%}
  #ovl .ov.sel{outline:2px dashed #A855F7;outline-offset:4px}
  #ovl .ov .h{position:absolute;display:none;box-sizing:border-box;z-index:3}
  #ovl .ov.sel .h{display:block}
  #ovl .ov .h.c{width:14px;height:14px;background:#fff;border:2px solid #6A38FF;border-radius:3px}
  #ovl .ov .h.nw{left:-11px;top:-11px;cursor:nwse-resize} #ovl .ov .h.se{right:-11px;bottom:-11px;cursor:nwse-resize}
  #ovl .ov .h.ne{right:-11px;top:-11px;cursor:nesw-resize} #ovl .ov .h.sw{left:-11px;bottom:-11px;cursor:nesw-resize}
  #ovl .ov .h.r{width:14px;height:14px;background:#6A38FF;border:2px solid #fff;border-radius:50%;cursor:url("data:image/svg+xml;utf8,<svg xmlns='http://www.w3.org/2000/svg' width='26' height='26' viewBox='0 0 26 26'><path d='M13 4a9 9 0 1 1-8.3 5.5' fill='none' stroke='white' stroke-width='4.5' stroke-linecap='round'/><path d='M13 4a9 9 0 1 1-8.3 5.5' fill='none' stroke='black' stroke-width='2' stroke-linecap='round'/><path d='M1.5 5.5l3.5 6 5-4.5z' fill='black' stroke='white' stroke-width='1.2'/></svg>") 13 13, grab}
  #ovl .ov .h.rt{left:50%;top:-26px;margin-left:-7px} #ovl .ov .h.rb{left:50%;bottom:-26px;margin-left:-7px}
  #ovl .ov .h.rl{top:50%;left:-26px;margin-top:-7px} #ovl .ov .h.rr{top:50%;right:-26px;margin-top:-7px}
  #edit{position:absolute;left:4%;right:4%;bottom:18%;display:none;z-index:5;background:rgba(10,14,40,.92);border:2px solid #A855F7;border-radius:12px;padding:10px;box-shadow:0 10px 30px rgba(0,0,0,.5)}
  #edit textarea{width:100%;box-sizing:border-box;min-height:70px;background:transparent;border:0;outline:0;resize:none;color:#fff;font:600 16px Arial;line-height:1.35}
  #edit .hint{color:#C7CBE0;font:12px Arial;margin-top:6px}
  #camhint{position:absolute;left:8px;bottom:8px;color:#fff;font:12px Arial;background:rgba(0,0,0,.6);padding:4px 8px;border-radius:4px;display:none}
  @keyframes pop{from{transform:scale(.8)}to{transform:scale(1)}}
  .pop{animation:pop 110ms ease-out}
</style></head>
<body>
<div id="wrap"><div id="frame"><video id="bg" muted></video><video id="v" playsinline></video><div id="ovl"></div><div id="cap"></div><div id="cam"><span class="lbl"></span></div><div id="edit"><textarea spellcheck="true"></textarea><div class="hint">Enter to save, Shift+Enter for a new line, Esc to cancel. Edits the whole transcript line.</div></div><div id="camhint">Drag the box to move the camera, scroll to zoom. Each change creates a camera cut at the current time.</div></div></div>
<script>
const v=document.getElementById('v'),bg=document.getElementById('bg'),frame=document.getElementById('frame'),cap=document.getElementById('cap'),cam=document.getElementById('cam'),camHint=document.getElementById('camhint');
let range={s:0,e:0},chunks=[],st=null,lastIdx=-1,lastPost=0,drag=null;
let camera=[],capPos=[],cameraMode=false,camDrag=null,liveCam=null;   // liveCam: box being edited (percent center + zoom)
function post(o){window.chrome&&window.chrome.webview&&window.chrome.webview.postMessage(o);}
function load(src,s,e){range={s,e};v.src=src;bg.src=src;v.currentTime=s;bg.currentTime=s;liveCam=null;layout();}
function setRange(s,e){range={s,e};if(v.currentTime<s||v.currentTime>e){seek(s);}}
function seek(t){v.currentTime=t;bg.currentTime=t;render(true);}
function play(){if(v.currentTime>=range.e-0.05)v.currentTime=range.s;v.play();bg.play();}
function pause(){v.pause();bg.pause();}
function toggle(){v.paused?play():pause();}
function setCaptions(c,s){chunks=c;st=s;lastIdx=-1;layout();render(true);}
function setCamera(list){camera=list||[];liveCam=null;layout();render(true);}
function setCaptionPositions(list){capPos=list||[];render(true);}
function setCameraMode(on){cameraMode=!!on;liveCam=null;layout();render(true);}
function setLook(f){v.style.filter=f||'none';bg.style.filter=((f&&f!=='none')?f+' ':'')+'blur(24px) brightness(.7)';}

function activeAt(list,t){const rel=t-range.s;let best=null;for(const k of list){if(k.t<=rel+0.001)best=k;else break;}return best||(list.length?list[0]:null);}
v.addEventListener('play',()=>post({playing:true}));
v.addEventListener('pause',()=>post({playing:false}));
v.addEventListener('loadedmetadata',()=>{layout();post({status:`loaded ${v.videoWidth}x${v.videoHeight}, ${v.duration.toFixed(1)}s`});});
v.addEventListener('error',()=>{const e=v.error;const msg='error: '+(e?('code '+e.code+' '+(e.message||'')):'unknown');
  fetch(v.currentSrc||v.src,{method:'HEAD'}).then(r=>post({status:msg+` | HEAD ${r.status}`})).catch(x=>post({status:msg+' | fetch failed: '+x}));});
window.addEventListener('resize',layout);
window.addEventListener('keydown',e=>{if(e.code==='Space'&&editT===null&&document.activeElement!==editText){e.preventDefault();toggle();}});

function vertical(){return st&&st.crop!=='Original'&&!cameraMode;}
function layout(){
  const W=window.innerWidth,H=window.innerHeight;let w,h;
  const ar=(v.videoWidth&&v.videoHeight)?v.videoWidth/v.videoHeight:16/9;
  if(vertical()){h=H;w=Math.round(H*9/16);if(w>W){w=W;h=Math.round(W*16/9);}}
  else{w=W;h=Math.round(W/ar);if(h>H){h=H;w=Math.round(H*ar);}}
  frame.style.width=w+'px';frame.style.height=h+'px';
  const crop=st?st.crop:'VerticalCrop';
  bg.style.display=(!cameraMode&&crop==='VerticalBlurredBackground')?'block':'none';
  cam.style.display=cameraMode?'block':'none';camHint.style.display=cameraMode?'block':'none';
  cap.style.display=cameraMode?'none':'block';
  // default video fit; applyCamera() overrides for vertical crop with camera cuts
  v.style.left='0';v.style.top='0';v.style.width='100%';v.style.height='100%';
  v.style.objectFit=(!cameraMode&&crop==='VerticalCrop')?'cover':'contain';
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
  const mv=st.marginV*scale;
  cap.style.top='';cap.style.bottom='';cap.style.transform='';
  if(st.align===8){cap.style.top=mv+'px';}
  else if(st.align===5){cap.style.top='50%';cap.style.transform='translateY(-50%)';}
  else{cap.style.bottom=mv+'px';}
}

// ---- camera follow (vertical crop preview) ----
function applyCamera(t){
  if(!vertical()||!(st&&st.crop==='VerticalCrop')||!camera.length||!v.videoWidth)return;
  const k=activeAt(camera,t);if(!k)return;
  const fw=frame.clientWidth,fh=frame.clientHeight,ar=v.videoWidth/v.videoHeight;
  let zoom=Math.max(1,Math.min(4,k.zoom||1));
  // crop height = srcH/zoom must map to frame height => displayed video height = fh*zoom
  let dh=fh*zoom,dw=dh*ar;
  if(dw<fw){dw=fw;dh=dw/ar;}                      // crop wider than source: fit width instead
  const cx=k.x/100*dw,cy=k.y/100*dh;
  let left=fw/2-cx,top=fh/2-cy;
  left=Math.min(0,Math.max(fw-dw,left));top=Math.min(0,Math.max(fh-dh,top));
  v.style.objectFit='fill';v.style.width=dw+'px';v.style.height=dh+'px';v.style.left=left+'px';v.style.top=top+'px';
}

// ---- camera edit mode: draggable 9:16 box over the full frame ----
function camBox(){const k=liveCam||activeAt(camera,v.currentTime)||{x:50,y:50,zoom:1};return {x:k.x,y:k.y,zoom:Math.max(1,Math.min(4,k.zoom||1))};}
function drawCam(){
  if(!cameraMode)return;
  const fw=frame.clientWidth,fh=frame.clientHeight,k=camBox();
  let bh=fh/k.zoom,bw=bh*9/16;if(bw>fw){bw=fw;bh=bw*16/9;}
  let cx=k.x/100*fw,cy=k.y/100*fh;
  cx=Math.max(bw/2,Math.min(fw-bw/2,cx));cy=Math.max(bh/2,Math.min(fh-bh/2,cy));
  cam.style.left=(cx-bw/2)+'px';cam.style.top=(cy-bh/2)+'px';cam.style.width=bw+'px';cam.style.height=bh+'px';
  cam.querySelector('.lbl').textContent=`camera ${k.x.toFixed(0)}% / ${k.y.toFixed(0)}%  zoom ${k.zoom.toFixed(2)}x`;
}
function commitCam(){if(!liveCam)return;post({camera:{x:+liveCam.x.toFixed(1),y:+liveCam.y.toFixed(1),zoom:+liveCam.zoom.toFixed(2),t:v.currentTime}});}
cam.addEventListener('pointerdown',e=>{if(!cameraMode)return;const k=camBox();camDrag={x0:e.clientX,y0:e.clientY,cx:k.x,cy:k.y};liveCam={...k};cam.setPointerCapture(e.pointerId);e.preventDefault();});
cam.addEventListener('pointermove',e=>{if(!camDrag)return;const fw=frame.clientWidth,fh=frame.clientHeight;
  liveCam.x=Math.max(0,Math.min(100,camDrag.cx+(e.clientX-camDrag.x0)/fw*100));liveCam.y=Math.max(0,Math.min(100,camDrag.cy+(e.clientY-camDrag.y0)/fh*100));drawCam();});
cam.addEventListener('pointerup',e=>{if(!camDrag)return;camDrag=null;commitCam();});
let wheelTimer=null;
frame.addEventListener('wheel',e=>{if(!cameraMode)return;e.preventDefault();const k=camBox();liveCam={...k,zoom:Math.max(1,Math.min(4,k.zoom+(e.deltaY<0?0.1:-0.1)))};drawCam();
  clearTimeout(wheelTimer);wheelTimer=setTimeout(commitCam,350);},{passive:false});

// ---- caption drag: creates a caption position at the current time ----
function applyCapPos(t){
  const k=capPos.length?activeAt(capPos,t):null;
  if(k){cap.classList.add('custom');cap.style.left=k.x+'%';cap.style.top=k.y+'%';cap.style.bottom='';cap.style.transform='translate(-50%,-50%)';}
  else if(!drag){cap.classList.remove('custom');if(st){const fh=frame.clientHeight,fw=frame.clientWidth,scale=fh>=fw?fh/1920:(fh/1080)*0.72,mv=st.marginV*scale;
    cap.style.left='4%';cap.style.top='';cap.style.bottom='';cap.style.transform='';
    if(st.align===8){cap.style.top=mv+'px';}else if(st.align===5){cap.style.top='50%';cap.style.transform='translateY(-50%)';}else{cap.style.bottom=mv+'px';}}}
}
let dragPos=null;
cap.addEventListener('pointerdown',e=>{
  const r=frame.getBoundingClientRect(),c=cap.getBoundingClientRect();
  const cx=(c.left+c.width/2-r.left)/r.width*100,cy=(c.top+c.height/2-r.top)/r.height*100;
  drag={x0:e.clientX,y0:e.clientY,cx,cy,w:r.width,h:r.height};dragPos={x:cx,y:cy};cap.classList.add('dragging');cap.setPointerCapture(e.pointerId);e.preventDefault();});
cap.addEventListener('pointermove',e=>{if(!drag)return;
  dragPos={x:Math.min(95,Math.max(5,drag.cx+(e.clientX-drag.x0)/drag.w*100)),y:Math.min(97,Math.max(3,drag.cy+(e.clientY-drag.y0)/drag.h*100))};
  cap.classList.add('custom');cap.style.left=dragPos.x+'%';cap.style.top=dragPos.y+'%';cap.style.bottom='';cap.style.transform='translate(-50%,-50%)';});
cap.addEventListener('pointerup',e=>{if(!drag)return;const moved=Math.hypot(e.clientX-drag.x0,e.clientY-drag.y0)>4;drag=null;cap.classList.remove('dragging');
  // a click without movement is not a drag: it must not create a caption position (it may be half of a double-click)
  if(moved&&dragPos)post({pos:{x:+dragPos.x.toFixed(1),y:+dragPos.y.toFixed(1),t:v.currentTime}});else render(true);});

// ---- image layers: shown inside their window. Drag the picture to move it, a corner to resize, an edge dot
// to rotate (Shift snaps to 15 degrees); the wheel resizes and Shift+wheel rotates. Elements are updated in
// place, and updates arriving during a drag wait for the drag to end, so a selection never breaks a drag.
const ovl=document.getElementById('ovl');let layers=[],layerSel=null,shown={},ovDrag=null,ovWheel=null,pendingLayers=null;
function layerEl(id){for(const d of ovl.children)if(d.dataset.id===id)return d;return null;}
function makeLayer(o){
  const d=document.createElement('div');d.className='ov';d.dataset.id=o.id;
  const pic=document.createElement('div');pic.className='pic';const im=document.createElement('img');pic.appendChild(im);d.appendChild(pic);
  for(const h of ['nw','ne','sw','se'])d.appendChild(Object.assign(document.createElement('div'),{className:'h c '+h}));
  for(const h of ['rt','rb','rl','rr'])d.appendChild(Object.assign(document.createElement('div'),{className:'h r '+h}));
  d.addEventListener('pointerdown',e=>{
    e.stopPropagation();e.preventDefault();const o=d._o;if(!o)return;
    layerSel=o.id;post({ovSel:o.id});
    const r=d.getBoundingClientRect(),cx=r.left+r.width/2,cy=r.top+r.height/2;
    const h=e.target.classList;const mode=h.contains('c')?'size':h.contains('r')?'rot':'move';
    ovDrag={o,mode,x0:e.clientX,y0:e.clientY,ox:o.x,oy:o.y,size0:o.size,rot0:o.rot||0,cx,cy,
      dist0:Math.max(8,Math.hypot(e.clientX-cx,e.clientY-cy)),ang0:Math.atan2(e.clientY-cy,e.clientX-cx),moved:false};
    d.setPointerCapture(e.pointerId);styleLayers();});
  d.addEventListener('pointermove',e=>{
    const g=ovDrag;if(!g||g.o!==d._o)return;const fr=frame.getBoundingClientRect();if(fr.width<=0||fr.height<=0)return;
    if(Math.hypot(e.clientX-g.x0,e.clientY-g.y0)>3)g.moved=true;if(!g.moved)return;const o=g.o;
    if(g.mode==='move'){o.x=Math.max(0,Math.min(100,g.ox+(e.clientX-g.x0)/fr.width*100));o.y=Math.max(0,Math.min(100,g.oy+(e.clientY-g.y0)/fr.height*100));}
    else if(g.mode==='size'){o.size=Math.max(8,Math.min(100,g.size0*Math.hypot(e.clientX-g.cx,e.clientY-g.cy)/g.dist0));}
    else{let a=g.rot0+(Math.atan2(e.clientY-g.cy,e.clientX-g.cx)-g.ang0)*180/Math.PI;a=((a+540)%360)-180;
      if(e.shiftKey)a=Math.round(a/15)*15;else if(Math.abs(a)<3)a=0;o.rot=a;}
    styleLayers();});
  const end=()=>{const g=ovDrag;if(!g||g.o!==d._o)return;ovDrag=null;if(g.moved)commitLayer(g.o);
    if(pendingLayers){const [l,sl]=pendingLayers;pendingLayers=null;setLayers(l,sl);}};
  d.addEventListener('pointerup',end);d.addEventListener('pointercancel',end);
  d.addEventListener('click',e=>e.stopPropagation());
  d.addEventListener('wheel',e=>{e.preventDefault();e.stopPropagation();const o=d._o;if(!o)return;
    if(e.shiftKey)o.rot=Math.max(-180,Math.min(180,(o.rot||0)+(e.deltaY<0?3:-3)));else o.size=Math.max(8,Math.min(100,o.size+(e.deltaY<0?2:-2)));
    layerSel=o.id;styleLayers();clearTimeout(ovWheel);ovWheel=setTimeout(()=>commitLayer(o),300);},{passive:false});
  ovl.appendChild(d);return d;}
function setLayers(list,sel){
  if(ovDrag){pendingLayers=[list,sel];return;}
  layers=list||[];layerSel=sel||null;
  for(const d of [...ovl.children])if(!layers.some(l=>l.id===d.dataset.id))d.remove();
  for(const o of layers){const d=layerEl(o.id)||makeLayer(o);d._o=o;const pic=d.firstChild,im=pic.firstChild;
    pic.className='pic '+(o.style||'frame');if(im.getAttribute('src')!==o.src)im.src=o.src;}
  styleLayers();}
function commitLayer(o){if(!isFinite(o.x)||!isFinite(o.y)||!isFinite(o.size))return;post({ov:{id:o.id,x:+o.x.toFixed(1),y:+o.y.toFixed(1),size:+o.size.toFixed(1),rot:+(o.rot||0).toFixed(1)}});}
function styleLayers(){const fw=frame.clientWidth;
  for(const d of ovl.children){const o=d._o;if(!o)continue;const w=fw*o.size/100,pic=d.firstChild;
    d.style.width=w+'px';d.style.left=o.x+'%';d.style.top=o.y+'%';d.style.transform=`translate(-50%,-50%) rotate(${o.rot||0}deg)`;
    pic.style.padding='';pic.style.borderWidth='';pic.style.borderRadius='';
    if(o.style==='frame'){pic.style.padding=`${w*0.045}px ${w*0.045}px ${w*0.14}px ${w*0.045}px`;}
    else if(o.style==='card'){pic.style.borderWidth=Math.max(2,w*0.012)+'px';pic.style.borderRadius=(w*0.07)+'px';}
    else if(o.style==='circle'){pic.style.borderWidth=Math.max(3,w*0.03)+'px';}
    d.classList.toggle('sel',o.id===layerSel);}}
function playEntrance(d,o){const base=`translate(-50%,-50%) rotate(${o.rot||0}deg)`;
  if(o.anim==='pop')d.animate([{transform:base+' scale(.55)'},{transform:base+' scale(1)'}],{duration:220,easing:'cubic-bezier(.2,1.4,.4,1)'});
  else if(o.anim==='fade')d.animate([{opacity:0},{opacity:1}],{duration:300});
  else if(o.anim==='slide')d.animate([{opacity:0,transform:base+' translateY(8vh)'},{opacity:1,transform:base}],{duration:350,easing:'ease-out'});}
/** Replays a layer's entrance once so the chosen effect can be judged. */
function previewLayer(id){const d=layerEl(id);if(!d||!d._o)return;d.style.display='block';shown[id]=true;playEntrance(d,d._o);}
function applyLayers(t){const rel=t-range.s;
  for(const d of ovl.children){const o=d._o;if(!o)continue;const on=!cameraMode&&((rel>=o.t&&rel<o.end)||(ovDrag&&ovDrag.o===o));
    d.style.display=on?'block':'none';
    if(on&&!shown[o.id]&&!ovDrag)playEntrance(d,o);
    shown[o.id]=on;}}
window.addEventListener('resize',styleLayers);

// ---- click the picture to play / pause; double-click the caption to edit its words ----
const editBox=document.getElementById('edit'),editText=editBox.querySelector('textarea');let editT=null;
document.getElementById('wrap').addEventListener('click',e=>{if(cameraMode||editT!==null)return;if(cap.contains(e.target)||editBox.contains(e.target)||cam.contains(e.target)||ovl.contains(e.target))return;toggle();});
// right-click the caption: the host selects that line in its transcript table, ready to edit
cap.addEventListener('contextmenu',e=>{e.preventDefault();e.stopPropagation();if(cameraMode)return;pause();post({lineAt:v.currentTime});});
cap.addEventListener('dblclick',e=>{if(cameraMode)return;e.stopPropagation();pause();editT=v.currentTime;post({editAt:editT});});
function beginEdit(text){if(editT===null)editT=v.currentTime;editText.value=text;editBox.style.display='block';cap.style.visibility='hidden';editText.focus();editText.select();}
function endEdit(save){if(editT===null)return;const t=editT,text=editText.value.trim();editT=null;editBox.style.display='none';cap.style.visibility='visible';if(save)post({edited:{t,text}});render(true);}
editText.addEventListener('keydown',e=>{if(e.key==='Enter'&&!e.shiftKey){e.preventDefault();endEdit(true);}else if(e.key==='Escape'){e.preventDefault();endEdit(false);}});
editText.addEventListener('blur',()=>endEdit(true));

function esc(s){return s.replace(/&/g,'&amp;').replace(/</g,'&lt;');}
function render(force){
  const t=v.currentTime;
  if(v.paused===false&&t>=range.e){pause();v.currentTime=range.s;post({t:range.s});return;}
  applyCamera(t);
  applyLayers(t);
  if(cameraMode){drawCam();}
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
  if(!drag)applyCapPos(t);
  const now=performance.now();
  if(now-lastPost>100||force){lastPost=now;post({t});}
}
(function loop(){render(false);requestAnimationFrame(loop);})();
</script>
</body></html>
""";
}
