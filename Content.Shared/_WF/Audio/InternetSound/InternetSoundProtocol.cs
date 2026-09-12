using Robust.Shared.Serialization;

namespace Content.Shared._WF.Audio.InternetSound;

/// <summary>
/// Wire format for admin internet sounds. Audio travels over the engine's bulk transfer channel under
/// <see cref="TransferKey"/>; stop and status messages are plain network events.
/// Payload: int32 header length, header (int32 id, string title, string admin, BinaryWriter encoding), then an
/// IMA ADPCM WAV file. The header comes first so clients can show the radio while the audio is still arriving.
/// </summary>
public static class InternetSoundProtocol
{
    public const string TransferKey = "WolfgateInternetSound";

    /// <summary>
    /// Clients reject longer headers as corrupt.
    /// </summary>
    public const int MaxHeaderBytes = 64 * 1024;

    /// <summary>
    /// Clients abort transfers larger than this. Well above the server's own size limit.
    /// </summary>
    public const int MaxPayloadBytes = 64 * 1024 * 1024;
}

/// <summary>
/// Stops the internet sound with this id on every client. Id 0 stops whatever is playing.
/// </summary>
[Serializable, NetSerializable]
public sealed class InternetSoundStopEvent : EntityEventArgs
{
    public int Id;

    public InternetSoundStopEvent(int id)
    {
        Id = id;
    }
}

/// <summary>
/// Admin window asks for the current internet sound state. Ignored for non-admins.
/// </summary>
[Serializable, NetSerializable]
public sealed class InternetSoundStateRequestEvent : EntityEventArgs;

/// <summary>
/// What the server is doing with internet sounds. Sent to admins whenever it changes, and on request.
/// </summary>
[Serializable, NetSerializable]
public sealed class InternetSoundStateEvent : EntityEventArgs
{
    public bool Fetching;
    public string? Title;
    public string? Admin;

    /// <summary>
    /// A sound is being fetched or is still playing, so a new one would be refused.
    /// </summary>
    public bool Busy => Fetching || Title != null;

    public InternetSoundStateEvent(bool fetching, string? title, string? admin)
    {
        Fetching = fetching;
        Title = title;
        Admin = admin;
    }
}

/// <summary>
/// Progress or error for the admin who requested an internet sound.
/// </summary>
[Serializable, NetSerializable]
public sealed class InternetSoundStatusEvent : EntityEventArgs
{
    public string Message;
    public bool IsError;

    public InternetSoundStatusEvent(string message, bool isError)
    {
        Message = message;
        IsError = isError;
    }
}
