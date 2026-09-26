# Galiluna Short Generator

Windows desktop app (.NET 9, WinForms) that turns a long video into captioned short-form clips:

1. **Download** a YouTube video/Short, Instagram Reel/post or TikTok (anything yt-dlp supports), or open a local file.
2. **Transcribe** it locally with Whisper (no API cost, runs on the CPU).
3. **Ask ChatGPT** (no API key): the app builds a prompt containing the timestamped transcript. Copy it into ChatGPT, paste the JSON answer back, and click Import.
4. **Suggest shorts**: clips imported from ChatGPT, or produced by Claude via the API, each with a hook, a virality score out of 10 and a plain-language explanation of *why* that moment could go viral.
5. **Edit & preview**: an in-app player (Edge WebView2) plays each selected short with the captions overlaid live in the chosen style. Fix wrong words in the transcript lines of that short, delete lines, and nudge the start/end.
6. **Generate** the selected clips with ffmpeg: 9:16 reframing (center crop, blurred background or original), burned-in captions in one of six styles, optional hook title, optional `[laughs]`-style reaction tags.

**Laughs and reactions.** The Transcript tab has a "Detect laughs / reactions" checkbox. When on, Whisper is nudged to write non-speech reactions as tags, normalised to `[laughs]`, `[applause]`, `[music]`, `[cheering]`, `[crying]`, `[sighs]`, `[gasps]`, `[pause]`. A loudness pass over the audio then flags genuine bursts (a moment in the loudest 5% of the recording, well above normal speech and clearly louder than the seconds around it): `[laughs]` becomes `[big laugh]`, `[cheering]` becomes `[loud cheering]`, and a loud line with no reaction tag gets `[shouting]`. Each line's peak above normal speech is shown in the "Peak dB" column, intense lines in red. The ChatGPT and Claude prompts are told to treat these as the emotional peaks. When the checkbox is off, all annotations are stripped. A separate checkbox on the Generate tab decides whether tags are shown in the burned captions. Detection is heuristic: Whisper only tags what it hears as a distinct reaction, and the loudness rule can be fooled by music stings or a change of speaker.

**Existing transcripts.** "Load transcript file..." on the Transcript tab imports `.srt`, `.vtt`, or a JSON sidecar instead of running Whisper.

**Caption placement.** In Edit & preview, drag the caption on the video to set its position from the current time on. Positions are keyframed per short and used when rendering (an ASS `\pos` override per caption).

**Camera.** For the vertical crop the camera follows faces: frames are sampled and analysed with the face detector built into Windows (`Windows.Media.FaceAnalysis`, no model download) and turned into a few stable camera cuts (position + zoom) per short. Runs automatically on generation when a short has no camera cuts ("Auto camera" on the Generate tab), or on demand in the editor. "Camera mode" in the editor shows the full frame with a draggable 9:16 box (scroll to zoom) to add manual cuts at the current time. Rendering uses an ffmpeg trim/concat graph so each cut can have its own zoom.

**Stickers.** Transparent PNG stickers live in `%AppData%\ShortGenerator\stickers` (a starter set generated with Higgsfield ships with the app: laugh, big-laugh, love, heart-eyes, inspiring, anger, fire, shock, applause, cry, money, thinking; drop your own PNGs in the folder). In the editor, double-click a sticker to place it at the current time, drag it on the video, scroll to resize, pick an animation (pop, float, shake, none) and duration. "Auto from reactions" places matching stickers on tagged lines (`[laughs]` -> laugh, `[big laugh]` -> big-laugh, `[shouting]` -> anger, `[applause]` -> applause...). Rendering composites them with fade in/out and the position animation.

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
| `Services/TranscriptEvents.cs` | Normalises / strips reaction tags such as `[laughs]`, intensity upgrades |
| `Services/AudioEnergy.cs` | Loudness profile of the audio used to flag big laughs / shouting |
| `Services/TranscriptImporter.cs` | Loads .srt / .vtt / JSON transcripts |
| `Services/FaceFramer.cs` | Face detection -> camera cuts per short |
| `Services/CameraMath.cs` | Crop geometry and the ffmpeg graph for camera cuts |
| `Services/StickerLibrary.cs` | Sticker folder, reaction -> sticker mapping, auto placement |
| `Assets/stickers/` | Starter sticker set (transparent PNGs) |
| `Services/VideoDownloader.cs` | yt-dlp wrapper (YoutubeDLSharp), MP4 preferred |
| `Services/Transcriber.cs` | Whisper.net transcription with segment timestamps |
| `Services/ShortSuggester.cs` | Claude call with structured JSON output |
| `Services/ChatGptExchange.cs` | Prompt builder and lenient parser for the copy/paste ChatGPT flow |
| `Services/CaptionBuilder.cs` | Transcript -> ASS subtitles per caption style |
| `Services/ShortRenderer.cs` | ffmpeg cut, reframe and caption burn-in |
| `Models/CaptionStyle.cs` | The caption style catalogue |

## Caption styles

Classic, Bold Pop (MrBeast/Hormozi), Karaoke Highlight (spoken word lights up), Minimal Box, Neon Glow, Yellow Punch. Words per caption and font size are adjustable; all styles are rendered by libass through ffmpeg's `subtitles` filter.

## Notes

- Everything about a video (transcript, suggestions and which are ticked, camera cuts, caption positions, stickers, generated files) is saved next to it as `<video>.shortgen.json`. The app reopens the last video at startup, so you can go straight to editing or regenerating a short without downloading again. Any other downloaded video can be picked from the library on the Video tab.
- Word-level caption timing is interpolated inside each Whisper segment by character length.
- Private Instagram/TikTok posts cannot be downloaded. If a site breaks, use Settings -> "Update yt-dlp".
