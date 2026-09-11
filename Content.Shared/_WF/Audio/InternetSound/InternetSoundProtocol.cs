using Robust.Shared.Serialization;

namespace Content.Shared._WF.Audio.InternetSound;

/// <summary>
/// Wire format for admin internet sounds. Audio travels over the engine's bulk transfer channel under
/// <see cref="TransferKey"/>; stop and status messages are plain network events.
/// Payload: int32 id, string title, string admin, int32 length, Ogg Vorbis bytes (BinaryWriter encoding).
/// </summary>
public static class InternetSoundProtocol
{
    public const string TransferKey = "WolfgateInternetSound";
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
