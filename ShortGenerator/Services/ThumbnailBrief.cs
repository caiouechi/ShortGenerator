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
        $"The person's expression pushed to match the emotion \"{s.Emotion}\", a 2 to 4 word headline taken from the hook, and the drama of this idea: {s.WhyViral}";

    /// <param name="ideal">What the thumbnail should be; the placeholder for a human, free text or the auto line for a model.</param>
    /// <param name="withReference">Whether an image of the frame accompanies the prompt.</param>
    public static string Build(ShortSuggestion s, string language, string ideal, bool withReference = true)
    {
        var basis = withReference
            ? "Using the attached frame as the base (keep the same person, face and outfit recognizable), redesign it into a scroll-stopping 9:16 (1080x1920) cover for a short."
            : "Create from scratch a scroll-stopping 9:16 (1080x1920) cover for a short (no reference image is provided; invent a fitting scene).";
        return
            "Act as a viral TikTok / Instagram Reels / YouTube Shorts creator and thumbnail designer with millions of views.\n" +
            basis + "\n\n" +
            $"Short title: \"{s.Title}\"\n" +
            $"Hook (first words): \"{s.Hook}\"\n" +
            $"Emotion: {s.Emotion}\n" +
            $"Why it could go viral: {s.WhyViral}\n\n" +
            "Publishing specs: vertical 9:16, 1080x1920 pixels, JPEG, safe for Instagram Reels, TikTok and YouTube Shorts covers. " +
            "One clear focal point on the face, exaggerated but natural expression, high contrast and saturated but clean colours, simplified background, " +
            $"a big bold headline of 2 to 5 words in {language} with a thick outline. " +
            // Profile grids on TikTok and Instagram show the 9:16 cover as a 3:4 tile cut from the middle (1080x1440),
            // so anything in the top or bottom 240 px disappears there; the full-screen view puts the caption UI low.
            "Layout safe zone, mandatory: the cover is also shown cropped to a centred 3:4 tile (the middle 1080x1440 px) in the TikTok and Instagram profile grids, " +
            "so keep the whole headline, the face and every important detail inside that middle area. Nothing important in the top 15% or the bottom 22% of the image " +
            "(those strips are only background). Put the headline in the upper half of the safe area (roughly 15% to 40% from the top), never touching the top edge, " +
            "with side margins of at least 8%; keep the face between about 30% and 75% from the top; " +
            "no logos, no watermarks, no extra text, photorealistic, sharp.\n\n" +
            "The ideal thumbnail will be: " + (string.IsNullOrWhiteSpace(ideal) ? AutoIdeal(s) : ideal);
    }
}
