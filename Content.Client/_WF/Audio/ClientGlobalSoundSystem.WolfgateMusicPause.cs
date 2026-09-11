using Robust.Shared.Audio;
using Robust.Shared.Audio.Components;

namespace Content.Client.Audio;

/// <summary>
/// Wolfgate: lets internet sounds pause station event music (nuke countdown, round end), then resume it.
/// </summary>
public sealed partial class ClientGlobalSoundSystem
{
    private readonly HashSet<EntityUid> _wfPausedEventMusic = new();
    private bool _wfEventMusicPaused;

    /// <summary>
    /// Pauses event music and keeps any new event track paused until <see cref="ResumeEventMusicWolfgate"/>.
    /// </summary>
    public void PauseEventMusicWolfgate()
    {
        _wfEventMusicPaused = true;
        EnforceEventMusicPauseWolfgate();
    }

    /// <summary>
    /// Pauses event tracks started since the last call. Run every frame while paused.
    /// </summary>
    public void EnforceEventMusicPauseWolfgate()
    {
        if (!_wfEventMusicPaused)
            return;

        foreach (var uid in _eventAudio.Values)
        {
            if (uid == null || !TryComp<AudioComponent>(uid, out var audio) || audio.State != AudioState.Playing)
                continue;

            _audio.SetState(uid, AudioState.Paused, component: audio);
            _wfPausedEventMusic.Add(uid.Value);
        }
    }

    /// <summary>
    /// Resumes every event track paused by <see cref="PauseEventMusicWolfgate"/> that still exists.
    /// </summary>
    public void ResumeEventMusicWolfgate()
    {
        _wfEventMusicPaused = false;

        foreach (var uid in _wfPausedEventMusic)
        {
            if (TryComp<AudioComponent>(uid, out var audio) && audio.State == AudioState.Paused)
                _audio.SetState(uid, AudioState.Playing, component: audio);
        }

        _wfPausedEventMusic.Clear();
    }
}
