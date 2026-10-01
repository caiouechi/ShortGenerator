namespace ShortGenerator.Models;

/// <summary>A rendered short on disk, persisted in the project sidecar so it survives a restart.
/// Since the galiluna integration it also remembers the post text it was rendered from and where
/// it was published, so the Publish step can show "live on @salon.downtown, failed on TikTok".</summary>
public sealed class GeneratedFile
{
    public string Title { get; set; } = "";
    public string Path { get; set; } = "";
    /// <summary>Title of the suggestion this file was rendered from (the post title can be its English version).</summary>
    public string? ShortTitle { get; set; }
    /// <summary>null for the original captions, "en" for the English version.</summary>
    public string? Language { get; set; }
    public DateTime When { get; set; }

    /// <summary>Caption and hashtags of the suggestion this file was rendered from, copied at render time
    /// so the publish step never has to find the suggestion again (older sidecars leave them null).</summary>
    public string? Caption { get; set; }
    public List<string>? Hashtags { get; set; }

    /// <summary>Per-network post text copied from the suggestion (null when the AI was not asked for that network).</summary>
    public NetworkPost? Youtube { get; set; }
    public NetworkPost? Tiktok { get; set; }
    public NetworkPost? Instagram { get; set; }

    /// <summary>Cover / thumbnail image exported next to the video (JPEG), when one was made.</summary>
    public string? CoverPath { get; set; }
    /// <summary>When the cover is a frame of the short: its time in seconds. TikTok can only use a frame as
    /// its cover, so this is what it gets. Null for a custom image.</summary>
    public double? CoverTimeSeconds { get; set; }

    /// <summary>Post text for a network with fallbacks to the generic caption and hashtags.</summary>
    public NetworkPost PostFor(string network)
    {
        var p = network.ToLowerInvariant() switch { "youtube" => Youtube, "tiktok" => Tiktok, "instagram" => Instagram, _ => null };
        return new NetworkPost
        {
            Title = string.IsNullOrWhiteSpace(p?.Title) ? Title : p!.Title,
            Description = string.IsNullOrWhiteSpace(p?.Description) ? (Caption ?? "") : p!.Description,
            Tags = p?.Tags is { Count: > 0 } ? p.Tags : (Hashtags ?? new List<string>())
        };
    }

    /// <summary>galiluna's id for this short once it was sent; null until then.</summary>
    public int? GaliLunaShortId { get; set; }
    public DateTime? SentAt { get; set; }
    public List<PublishOutcome> Publications { get; set; } = new();

    public bool Sent => GaliLunaShortId is not null;
}

/// <summary>One account's outcome, mirrored from galiluna's answer.</summary>
public sealed class PublishOutcome
{
    public string Network { get; set; } = "";
    public string? Account { get; set; }
    /// <summary>published | drafted | processing | failed</summary>
    public string Status { get; set; } = "";
    public string? Permalink { get; set; }
    public string? Error { get; set; }
}
