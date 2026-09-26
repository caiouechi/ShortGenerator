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
    /// <summary>User dragged the caption: new anchor as percent of the output frame (x from left, y from top).</summary>
    public event Action<double, double>? CaptionMoved;
    /// <summary>User moved / zoomed the camera box: center as percent of the source frame, and zoom.</summary>
    public event Action<double, double, double>? CameraMoved;
    /// <summary>User dragged / resized a sticker: id, center percent x/y, width percent.</summary>
    public event Action<string, double, double, double>? OverlayMoved;

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
            if (root.TryGetProperty("pos", out var pos)) CaptionMoved?.Invoke(pos.GetProperty("x").GetDouble(), pos.GetProperty("y").GetDouble());
            if (root.TryGetProperty("camera", out var cam)) CameraMoved?.Invoke(cam.GetProperty("x").GetDouble(), cam.GetProperty("y").GetDouble(), cam.GetProperty("zoom").GetDouble());
            if (root.TryGetProperty("overlay", out var ov)) OverlayMoved?.Invoke(ov.GetProperty("id").GetString() ?? "", ov.GetProperty("x").GetDouble(), ov.GetProperty("y").GetDouble(), ov.GetProperty("size").GetDouble());
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

    /// <summary>Sticker overlays (times relative to the clip start). Files are resolved through the sticker library.</summary>
    public Task SetOverlaysAsync(List<OverlayItem> overlays)
    {
        var items = overlays.Select(o => new
        {
            id = o.Id, t = o.Time, dur = o.Duration, x = o.X, y = o.Y, size = o.Size, anim = o.Animation,
            src = StickerLibrary.Resolve(o.File) is { } p ? new Uri(p).AbsoluteUri : ""
        }).Where(o => o.src.Length > 0);
        return Exec($"setOverlays({JsonSerializer.Serialize(items)})");
    }

    /// <summary>Camera edit mode: shows the whole source frame with a draggable 9:16 box (scroll to zoom).</summary>
    public Task SetCameraModeAsync(bool on) => Exec($"setCameraMode({(on ? "true" : "false")})");

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
  #camhint{position:absolute;left:8px;bottom:8px;color:#fff;font:12px Arial;background:rgba(0,0,0,.6);padding:4px 8px;border-radius:4px;display:none}
  @keyframes pop{from{transform:scale(.8)}to{transform:scale(1)}}
  .pop{animation:pop 110ms ease-out}
  #ovs{position:absolute;inset:0;pointer-events:none}
  .ov{position:absolute;transform:translate(-50%,-50%);pointer-events:auto;cursor:grab;user-select:none;touch-action:none;display:none}
  .ov.on{display:block}
  .ov.sel{outline:2px dashed #A855F7;outline-offset:4px}
  .ov img{width:100%;height:auto;display:block;pointer-events:none;-webkit-user-drag:none}
  @keyframes ovpop{0%{transform:scale(.2)}60%{transform:scale(1.15)}100%{transform:scale(1)}}
  @keyframes ovfloat{0%,100%{transform:translateY(-4%)}50%{transform:translateY(4%)}}
  @keyframes ovshake{0%,100%{transform:rotate(0)}20%{transform:rotate(-12deg)}40%{transform:rotate(10deg)}60%{transform:rotate(-8deg)}80%{transform:rotate(6deg)}}
  .ov.pop img{animation:ovpop .45s cubic-bezier(.34,1.56,.64,1) both}
  .ov.float img{animation:ovfloat 1.4s ease-in-out infinite}
  .ov.shake img{animation:ovshake .6s ease-in-out both}
</style></head>
<body>
<div id="wrap"><div id="frame"><video id="bg" muted></video><video id="v" playsinline></video><div id="ovs"></div><div id="cap"></div><div id="cam"><span class="lbl"></span></div><div id="camhint">Drag the box to move the camera, scroll to zoom. Each change creates a camera cut at the current time.</div></div></div>
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

// ---- sticker overlays ----
const ovs=document.getElementById('ovs');let overlays=[],ovDrag=null,ovWheelTimer=null;
function setOverlays(list){
  overlays=list||[];ovs.innerHTML='';
  for(const o of overlays){
    const d=document.createElement('div');d.className='ov '+(o.anim||'none');d.dataset.id=o.id;
    const img=document.createElement('img');img.src=o.src;d.appendChild(img);
    d.addEventListener('pointerdown',e=>{if(cameraMode)return;ovDrag={o,x0:e.clientX,y0:e.clientY,cx:o.x,cy:o.y};d.classList.add('sel');d.setPointerCapture(e.pointerId);e.preventDefault();e.stopPropagation();});
    d.addEventListener('pointermove',e=>{if(!ovDrag||ovDrag.o!==o)return;const fw=frame.clientWidth,fh=frame.clientHeight;
      o.x=Math.max(2,Math.min(98,ovDrag.cx+(e.clientX-ovDrag.x0)/fw*100));o.y=Math.max(2,Math.min(98,ovDrag.cy+(e.clientY-ovDrag.y0)/fh*100));placeOverlay(d,o);});
    d.addEventListener('pointerup',e=>{if(!ovDrag||ovDrag.o!==o)return;ovDrag=null;d.classList.remove('sel');post({overlay:{id:o.id,x:+o.x.toFixed(1),y:+o.y.toFixed(1),size:+o.size.toFixed(1)}});});
    d.addEventListener('wheel',e=>{if(cameraMode)return;e.preventDefault();e.stopPropagation();o.size=Math.max(6,Math.min(90,o.size+(e.deltaY<0?2:-2)));placeOverlay(d,o);
      clearTimeout(ovWheelTimer);ovWheelTimer=setTimeout(()=>post({overlay:{id:o.id,x:+o.x.toFixed(1),y:+o.y.toFixed(1),size:+o.size.toFixed(1)}}),350);},{passive:false});
    ovs.appendChild(d);placeOverlay(d,o);
  }
  render(true);
}
function placeOverlay(d,o){d.style.left=o.x+'%';d.style.top=o.y+'%';d.style.width=o.size+'%';}
function renderOverlays(t){
  const rel=t-range.s;
  for(const d of ovs.children){
    const o=overlays.find(x=>x.id===d.dataset.id);if(!o)continue;
    const on=!cameraMode&&rel>=o.t&&rel<o.t+o.dur;
    if(on&&!d.classList.contains('on')){d.classList.add('on');const img=d.firstChild;img.style.animation='none';void img.offsetWidth;img.style.animation='';}
    else if(!on&&d.classList.contains('on')&&!(ovDrag&&ovDrag.o===o)){d.classList.remove('on');}
  }
}
function activeAt(list,t){const rel=t-range.s;let best=null;for(const k of list){if(k.t<=rel+0.001)best=k;else break;}return best||(list.length?list[0]:null);}
v.addEventListener('play',()=>post({playing:true}));
v.addEventListener('pause',()=>post({playing:false}));
v.addEventListener('loadedmetadata',()=>{layout();post({status:`loaded ${v.videoWidth}x${v.videoHeight}, ${v.duration.toFixed(1)}s`});});
v.addEventListener('error',()=>{const e=v.error;const msg='error: '+(e?('code '+e.code+' '+(e.message||'')):'unknown');
  fetch(v.currentSrc||v.src,{method:'HEAD'}).then(r=>post({status:msg+` | HEAD ${r.status}`})).catch(x=>post({status:msg+' | fetch failed: '+x}));});
window.addEventListener('resize',layout);

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
function commitCam(){if(!liveCam)return;post({camera:{x:+liveCam.x.toFixed(1),y:+liveCam.y.toFixed(1),zoom:+liveCam.zoom.toFixed(2)}});}
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
cap.addEventListener('pointerup',e=>{if(!drag)return;drag=null;cap.classList.remove('dragging');if(dragPos)post({pos:{x:+dragPos.x.toFixed(1),y:+dragPos.y.toFixed(1)}});});

function esc(s){return s.replace(/&/g,'&amp;').replace(/</g,'&lt;');}
function render(force){
  const t=v.currentTime;
  if(v.paused===false&&t>=range.e){pause();v.currentTime=range.s;post({t:range.s});return;}
  applyCamera(t);
  renderOverlays(t);
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
