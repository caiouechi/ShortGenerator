using ShortGenerator.Models;

namespace ShortGenerator.Services;

/// <summary>
/// The brief for turning a cover frame into a viral 9:16 thumbnail. Shared by "Copy prompt" (for ChatGPT) and
/// the Higgsfield dialog, so the persona and the publishing specs are the same everywhere.
/// </summary>
public static class ThumbnailBrief
{
    public const string HumanPlaceholder = "[describe here what you want: the expression, the headline words, the colours, what to exaggerate]";

    /// <summary>A sensible default for the "ideal thumbnail" line, built from what the AI said about the moment.</summary>
    public static string AutoIdeal(ShortSuggestion s) =>
        $"The person caught mid-reaction, the expression that goes with \"{s.Emotion}\" but as a real photo would catch it, " +
        $"and one shocking 2 to 4 word headline in the centre that makes people need to know the answer, drawn from: {s.WhyViral}";

    /// <param name="ideal">What the thumbnail should be; the placeholder for a human, free text or the auto line for a model.</param>
    /// <param name="withReference">Whether an image of the frame accompanies the prompt.</param>
    public static string Build(ShortSuggestion s, string language, string ideal, bool withReference = true)
    {
        var basis = withReference
            ? "Using the attached frame as the base (keep the same person, face and outfit recognizable), turn it into a scroll-stopping 9:16 (1080x1920) cover for a short."
            : "Create from scratch a scroll-stopping 9:16 (1080x1920) cover for a short (no reference image is provided; invent a fitting scene).";
        return
            "Act as a viral TikTok / Instagram Reels / YouTube Shorts creator and thumbnail designer with millions of views.\n" +
            basis + "\n\n" +
            $"Short title: \"{s.Title}\"\n" +
            $"Hook (first words): \"{s.Hook}\"\n" +
            $"Emotion: {s.Emotion}\n" +
            $"Why it could go viral: {s.WhyViral}\n\n" +
            // Viewers skip covers that look generated: the picture has to read as a real frame from the video.
            "Look, mandatory: it must look like a real photo taken from the video, not like an AI image. Keep the real skin texture, pores, stray hairs, " +
            "natural lighting and the real colours of the scene; no plastic or airbrushed skin, no glow, no over-sharpening, no HDR look, no fake bokeh, " +
            "no symmetrical perfection, no extra fingers or hands, no invented objects. Do not redraw the face: only the expression may be pushed, and only as far as a real " +
            "photo of this person could show it. The background stays the real place, at most slightly simplified or darkened behind the headline. Subtle film grain is fine.\n\n" +
            // The words are the hook: one shocking line, big, dead centre of the area every platform keeps.
            $"Headline, mandatory: ONE line of 2 to 4 words in {language}, written like a shocking claim, a cliffhanger or a question the viewer must get answered " +
            "(the single most surprising thing in this moment, not a description of it; no clickbait that the video does not deliver). Big, thick sans-serif capitals, " +
            "white or yellow with a thick dark outline, centred horizontally, placed around the middle of the image, between about 38% and 52% from the top, " +
            "just below or beside the face without covering the eyes. The headline must fit fully inside the picture with side margins of at least 8%; " +
            "no second line of small text, no subtitles, no emojis.\n\n" +
            "Publishing specs: vertical 9:16, 1080x1920 pixels, JPEG, safe for Instagram Reels, TikTok and YouTube Shorts covers. " +
            "One clear focal point on the face, high contrast and clean colours without over-saturation. " +
            // Profile grids on TikTok and Instagram show the 9:16 cover as a 3:4 tile cut from the middle (1080x1440),
            // so anything in the top or bottom 240 px disappears there; the full-screen view puts the caption UI low.
            "Layout safe zone, mandatory: the cover is also shown cropped to a centred 3:4 tile (the middle 1080x1440 px) in the TikTok and Instagram profile grids, " +
            "so keep the whole headline, the face and every important detail inside that middle area. Nothing important in the top 15% or the bottom 22% of the image " +
            "(those strips are only background); keep the face between about 20% and 70% from the top; " +
            "no logos, no watermarks, no extra text, sharp.\n\n" +
            "The ideal thumbnail will be: " + (string.IsNullOrWhiteSpace(ideal) ? AutoIdeal(s) : ideal);
    }
}
