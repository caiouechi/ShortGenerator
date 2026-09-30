# Galiluna Short Generator

Windows desktop app (.NET 9, WinForms) that turns a long video into captioned short-form clips:

1. **Download** a YouTube video/Short, Instagram Reel/post or TikTok (anything yt-dlp supports), or open a local file.
2. **Transcribe** it locally with Whisper (no API cost, runs on the CPU).
3. **Ask ChatGPT** (no API key): the app builds a prompt containing the timestamped transcript. Copy it into ChatGPT, paste the JSON answer back, and click Import.
4. **Suggest shorts**: clips imported from ChatGPT, or produced by Claude via the API, each with a hook, a virality score out of 10 and a plain-language explanation of *why* that moment could go viral.
5. **Edit & preview**: an in-app player (Edge WebView2) plays each selected short with the captions overlaid live in the chosen style. Fix wrong words in the transcript lines of that short, delete lines, and nudge the start/end. Auto camera runs by default the first time a short opens (face detection places and zooms the 9:16 crop); "Change camera" cycles through the other places worth looking at (another person, both, the wide shot): with Camera mode off it changes the whole cut under the playhead, with Camera mode on it starts a new cut at the playhead so only the scene from there onward changes. Click anywhere in the player (the black bars too) to play or pause, right-click the caption to jump to its line in the transcript table with the cursor ready to type, double-click the caption to fix its words in place (the whole transcript line opens in an inline editor), and use "Remove from selected" (or Delete) to drop a short from the editor without going back to Suggestions. A manual camera change starts exactly at the frame on screen and hands back to the automatic framing 4 seconds later (a "resume" cut you can delete to keep the manual framing longer). Camera cuts and caption positions show as marks on the scrub bar; one click on a transcript line or on a cut jumps the player there. The "Look" dropdown on Generate shorts (Auto enhance by default, or Do nothing, Brighten, Tame, Vivid, Cinematic, Warm) is previewed live and burned into the render and the cover. Downloaded videos, transcripts and generated shorts can each be deleted from their step.
6. **Generate** the selected clips with ffmpeg: 9:16 reframing (center crop, blurred background or original), burned-in captions in one of six styles, optional hook title, optional `[laughs]`-style reaction tags.
7. **Publish**: send the rendered shorts to the Instagram, TikTok and YouTube accounts connected on galiluna. Sign in with galiluna from Settings or from the Publish step: the app shows a short code and opens your browser on galiluna, where you sign in as usual and approve that code. No password is typed into this app and no local port is opened; galiluna then hands the app a personal API key, stored encrypted. Settings also offers Refresh (re-reads your accounts and tells you when the key was revoked or replaced) and Sign out (revokes the key on galiluna and forgets it here). The Publish step shows one card per network that is actually connected on galiluna (Instagram accounts, YouTube channels with privacy, TikTok as drafts or direct); networks that are not connected are named in one line instead of showing dead controls. Each card carries its own post text for the selected short and its own Publish button, and "Publish to all" sends to every card at once. The shorts list on the left is resizable. Each account is sent as its own request, so its result appears on the card and in the Published column the moment galiluna answers, and one account failing never hides another succeeding. A "Cancel publishing" button stops before the next upload (an upload that already finished may still be posted by galiluna). "Delete short" removes a rendered file, its cover and its record. Outcomes are recorded per network and per account in the project file. No galiluna password is ever stored here.

**Post text per network.** On the Ask ChatGPT and Suggestions steps, tick YouTube / TikTok / Instagram to have the AI write tailored post text for each short: a searchable YouTube title, description and keyword tags; a native TikTok caption with hashtags; an Instagram Reels caption with hashtags. The Publish step shows a "Text for" selector to review or edit each version, and sends one request per network so every platform receives its own text (the generic caption is the fallback).

**Images on the short.** Edit & preview has an Images table under the transcript: right-click a transcript line and choose "Add image for this line..." (or use Add image...) to show a picture of the person, object or place being mentioned, timed to that line. Each image has From / To, a style (Photo frame, Card, Circle for faces, Plain to keep PNG transparency), an entrance (Pop in, Fade in, Slide up, None), size and rotation; on the video drag it to move, scroll to resize and Shift+scroll to rotate. Rendering stays light: each image is pre-drawn once at its final size with its style and rotation baked in, and ffmpeg only composites that still PNG inside its window (the test short rendered as fast with four images as without).

**Cover / thumbnail.** In Edit & preview, "Use this frame" makes the current frame the cover, "Choose image..." uses your own picture, and the preview shows the result. Under "Make it viral", "Copy image" puts the full-resolution cover on the clipboard and "Copy prompt" copies a thumbnail brief (viral short-form creator persona, title, hook, emotion, why it could go viral, layout rules, and a last line for you to fill in) so both can be pasted into ChatGPT to redraw the thumbnail. A third button, "Higgsfield", appears only when `HF_API_KEY` (and optionally `HF_API_SECRET`) is set in the environment or a debugger is attached: it first opens a dialog where you choose whether the current frame is sent as the reference and type what you want the thumbnail to be (the viral-creator persona and the 9:16 publishing specs are always included, and the final prompt is shown before sending); it then uploads the frame if chosen, asks the image model (`HF_IMAGE_MODEL`, default `xai/grok-imagine-image-2.0`; reference field `HF_IMAGE_REF_FIELD`, default `input_images`) to redraw it as a 9:16 thumbnail from the same brief, and uses the result as the cover. Re-rendering a short replaces its earlier file and cover unless that file was already published, so the folder holds one current file per short. The editor renders previews only; rendering the final files happens on Generate shorts. On generation the cover is exported as `<short>.cover.jpg` next to the video (framed with the camera cut active at that moment) and sent to galiluna as the `cover` part of the publish request. galiluna's Shorts API needs to accept that part to forward it as the Reel cover, the TikTok cover frame and the YouTube thumbnail; until then the field is ignored server-side.

**Laughs and reactions.** The Transcript tab has a "Detect laughs / reactions" checkbox. When on, Whisper is nudged to write non-speech reactions as tags, normalised to `[laughs]`, `[applause]`, `[music]`, `[cheering]`, `[crying]`, `[sighs]`, `[gasps]`, `[pause]`. A loudness pass over the audio then flags genuine bursts (a moment in the loudest 5% of the recording, well above normal speech and clearly louder than the seconds around it): `[laughs]` becomes `[big laugh]`, `[cheering]` becomes `[loud cheering]`, and a loud line with no reaction tag gets `[shouting]`. Each line's peak above normal speech is shown in the "Peak dB" column, intense lines in red. The ChatGPT and Claude prompts are told to treat these as the emotional peaks. When the checkbox is off, all annotations are stripped. A separate checkbox on the Generate tab decides whether tags are shown in the burned captions. Detection is heuristic: Whisper only tags what it hears as a distinct reaction, and the loudness rule can be fooled by music stings or a change of speaker.

**Existing transcripts.** "Load transcript file..." on the Transcript tab imports `.srt`, `.vtt`, or a JSON sidecar instead of running Whisper.

**Caption placement.** In Edit & preview, drag the caption on the video to set its position from the current time on. Positions are keyframed per short and used when rendering (an ASS `\pos` override per caption).

**Camera.** For the vertical crop the camera follows faces: frames are sampled and analysed with the face detector built into Windows (`Windows.Media.FaceAnalysis`, no model download) and turned into a few stable camera cuts (position + zoom) per short. Runs automatically on generation when a short has no camera cuts ("Auto camera" on the Generate tab), or on demand in the editor. "Camera mode" in the editor shows the full frame with a draggable 9:16 box (scroll to zoom) to add manual cuts at the current time. Rendering uses an ffmpeg trim/concat graph so each cut can have its own zoom.


## Requirements

- Windows 10/11, .NET 9 SDK (to build) or .NET 9 Desktop Runtime (to run).
- **ffmpeg / ffprobe** (with libass) - detected on PATH or downloaded from Settings.
- **yt-dlp** - downloaded from Settings ("Download missing tools"), or place `yt-dlp.exe` on PATH.
- **Anthropic API key** for the suggestion step. Enter it in Settings (stored with Windows DPAPI) or set `ANTHROPIC_API_KEY`.
- Whisper models are downloaded on first use to `%AppData%\ShortGenerator\whisper-models`.

## Build and run

```bash
dotnet build ShortGenerator/ShortGenerator.csproj
```

```bash
dotnet run --project ShortGenerator/ShortGenerator.csproj
```

## Project layout

| Path | Purpose |
|---|---|
| `Forms/MainForm.cs` | 4-step UI (Video, Transcript, Suggestions, Generate) |
| `Forms/SettingsForm.cs` | API key, model, Whisper model, folders, tool download/update |
| `Forms/Theme.cs` | Galiluna brand palette, header, buttons, progress bar |
| `Forms/CaptionPreview.cs` | GDI+ preview of the selected caption style |
| `Forms/ClipPlayer.cs` | WebView2 video player with live caption overlay (Edit & preview tab) |
| `Forms/Controls/PublishPanel.cs` | Publish step: account checklist, TikTok mode, caption, per-account outcome |
| `Services/GaliLunaClient.cs` | Client for galiluna's Shorts API (bearer API key); contract in the CampaignStudio repo, `docs/shorts-api.md` |
| `Services/TranscriptEvents.cs` | Normalises / strips reaction tags such as `[laughs]`, intensity upgrades |
| `Services/AudioEnergy.cs` | Loudness profile of the audio used to flag big laughs / shouting |
| `Services/TranscriptImporter.cs` | Loads .srt / .vtt / JSON transcripts |
| `Services/FaceFramer.cs` | Face detection -> camera cuts per short |
| `Services/CameraMath.cs` | Crop geometry and the ffmpeg graph for camera cuts |
| `Services/VideoDownloader.cs` | yt-dlp wrapper (YoutubeDLSharp), MP4 preferred |
| `Services/Transcriber.cs` | Whisper.net transcription with segment timestamps |
| `Services/ShortSuggester.cs` | Claude call with structured JSON output |
| `Services/ChatGptExchange.cs` | Prompt builder and lenient parser for the copy/paste ChatGPT flow |
| `Services/CaptionBuilder.cs` | Transcript -> ASS subtitles per caption style |
| `Services/ShortRenderer.cs` | ffmpeg cut, reframe and caption burn-in |
| `Models/CaptionStyle.cs` | The caption style catalogue |

## Design

Light work area for a public product (Galiluna light palette: #F6F7FC background, white cards, navy text) with the brand's dark nebula side navigation showing the six steps and their state. Owner-drawn gradient buttons (`FancyButton`), rounded input fields, a tinted page title band, empty-state illustrations and the sidebar artwork were generated with Higgsfield. See `Forms/Theme.cs` and `Forms/Controls/`.

## Caption styles

Classic, Bold Pop (MrBeast/Hormozi), Karaoke Highlight (spoken word lights up), Minimal Box, Neon Glow, Yellow Punch. Words per caption and font size are adjustable; all styles are rendered by libass through ffmpeg's `subtitles` filter.

## Notes

- Everything about a video (transcript, suggestions and which are ticked, camera cuts, caption positions, stickers, generated files) is saved next to it as `<video>.shortgen.json`. The app reopens the last video at startup, so you can go straight to editing or regenerating a short without downloading again. Any other downloaded video can be picked from the library on the Video tab.
- Word-level caption timing is interpolated inside each Whisper segment by character length.
- Private Instagram/TikTok posts cannot be downloaded. If a site breaks, use Settings -> "Update yt-dlp".
