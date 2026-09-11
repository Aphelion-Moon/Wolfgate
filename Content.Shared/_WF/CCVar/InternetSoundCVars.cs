using Robust.Shared.Configuration;

namespace Content.Shared._WF.CCVar;

/// <summary>
/// Settings for admin internet sounds.
/// </summary>
[CVarDefs]
public sealed class InternetSoundCVars
{
    /// <summary>
    /// Whether admins can play internet sounds at all.
    /// </summary>
    public static readonly CVarDef<bool> Enabled =
        CVarDef.Create("wf.internet_sound.enabled", true, CVar.SERVERONLY);

    /// <summary>
    /// yt-dlp executable. A bare name is looked up on PATH.
    /// </summary>
    public static readonly CVarDef<string> YtDlpPath =
        CVarDef.Create("wf.internet_sound.ytdlp_path", "yt-dlp", CVar.SERVERONLY);

    /// <summary>
    /// ffmpeg executable used to convert downloads to Ogg Vorbis. A bare name is looked up on PATH.
    /// </summary>
    public static readonly CVarDef<string> FfmpegPath =
        CVarDef.Create("wf.internet_sound.ffmpeg_path", "ffmpeg", CVar.SERVERONLY);

    /// <summary>
    /// Longest sound in seconds. Longer links are refused. Clients hold the decoded audio in memory (~10 MB a minute).
    /// </summary>
    public static readonly CVarDef<int> MaxDuration =
        CVarDef.Create("wf.internet_sound.max_duration", 480, CVar.SERVERONLY);

    /// <summary>
    /// Seconds to wait for download and conversion before giving up.
    /// </summary>
    public static readonly CVarDef<int> Timeout =
        CVarDef.Create("wf.internet_sound.timeout", 120, CVar.SERVERONLY);

    /// <summary>
    /// Largest converted file in megabytes that will be sent to clients.
    /// </summary>
    public static readonly CVarDef<int> MaxSizeMb =
        CVarDef.Create("wf.internet_sound.max_size_mb", 16, CVar.SERVERONLY);

    /// <summary>
    /// Client volume for internet sounds, 0 to 1. Set from the radio popup.
    /// </summary>
    public static readonly CVarDef<float> Volume =
        CVarDef.Create("wf.internet_sound.volume", 0.5f, CVar.CLIENTONLY | CVar.ARCHIVE);
}
