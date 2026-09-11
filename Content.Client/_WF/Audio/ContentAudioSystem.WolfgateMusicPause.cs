using Robust.Shared.Audio;
using Robust.Shared.Audio.Components;

namespace Content.Client.Audio;

/// <summary>
/// Wolfgate: lets internet sounds pause ambient, combat and lobby music, then resume it. A partial so the
/// music systems stay untouched.
/// </summary>
public sealed partial class ContentAudioSystem
{
    private readonly HashSet<EntityUid> _wfPausedMusic = new();
    private bool _wfMusicPaused;

    /// <summary>
    /// Pauses current music and keeps any new track paused until <see cref="ResumeMusicWolfgate"/>.
    /// </summary>
    public void PauseMusicWolfgate()
    {
        _wfMusicPaused = true;
        EnforceMusicPauseWolfgate();
    }

    /// <summary>
    /// Pauses tracks the music loops started since the last call. Run every frame while paused.
    /// </summary>
    public void EnforceMusicPauseWolfgate()
    {
        if (!_wfMusicPaused)
            return;

        PauseMusicStream(_ambientMusicStream);
        PauseMusicStream(_lobbySoundtrackInfo?.MusicStreamEntityUid);
    }

    /// <summary>
    /// Resumes every track paused by <see cref="PauseMusicWolfgate"/> that still exists.
    /// </summary>
    public void ResumeMusicWolfgate()
    {
        _wfMusicPaused = false;

        foreach (var uid in _wfPausedMusic)
        {
            if (TryComp<AudioComponent>(uid, out var audio) && audio.State == AudioState.Paused)
                _audio.SetState(uid, AudioState.Playing, component: audio);
        }

        _wfPausedMusic.Clear();
    }

    private void PauseMusicStream(EntityUid? uid)
    {
        if (uid == null || !TryComp<AudioComponent>(uid, out var audio) || audio.State != AudioState.Playing)
            return;

        _audio.SetState(uid, AudioState.Paused, component: audio);
        _wfPausedMusic.Add(uid.Value);
    }
}
