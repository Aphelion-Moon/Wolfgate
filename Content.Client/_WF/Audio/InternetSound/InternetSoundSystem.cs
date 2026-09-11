using System.IO;
using System.Numerics;
using Content.Client.Audio;
using Content.Shared._WF.Audio.InternetSound;
using Content.Shared._WF.CCVar;
using Content.Shared.CCVar;
using Robust.Client.Audio;
using Robust.Client.Console;
using Robust.Shared.Asynchronous;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Components;
using Robust.Shared.Configuration;
using Robust.Shared.Network.Transfer;

namespace Content.Client._WF.Audio.InternetSound;

/// <summary>
/// Plays admin internet sounds sent by the server: decodes the Ogg payload, shows the radio popup,
/// and pauses music until the sound ends or the player stops it.
/// </summary>
public sealed partial class InternetSoundSystem : EntitySystem
{
    [Dependency] private ITransferManager _transfer = default!;
    [Dependency] private ITaskManager _task = default!;
    [Dependency] private IAudioManager _audioManager = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IClientConsoleHost _console = default!;
    [Dependency] private AudioSystem _audio = default!;
    [Dependency] private ContentAudioSystem _contentAudio = default!;
    [Dependency] private ClientGlobalSoundSystem _globalSound = default!;

    /// <summary>
    /// The transfer key outlives entity systems, which are rebuilt on reconnect, so it routes to the live instance.
    /// </summary>
    private static readonly Dictionary<ITransferManager, InternetSoundSystem?> Receivers = new();

    private sealed record Playback(int Id, EntityUid Entity, AudioStream Stream);

    private Playback? _current;
    private InternetSoundPopup? _popup;

    /// <summary>
    /// Stopped sounds whose decoded audio is freed once their entity is gone.
    /// </summary>
    private readonly List<(EntityUid Entity, AudioStream Stream)> _retired = new();

    /// <summary>
    /// Status messages for the admin who requested a sound. Also written to the console.
    /// </summary>
    public event Action<string, bool>? StatusReceived;

    public override void Initialize()
    {
        base.Initialize();

        lock (Receivers)
        {
            if (!Receivers.ContainsKey(_transfer))
            {
                var transfer = _transfer;
                transfer.RegisterTransferMessage(InternetSoundProtocol.TransferKey, ev => Route(transfer, ev));
            }

            Receivers[_transfer] = this;
        }

        SubscribeNetworkEvent<InternetSoundStopEvent>(OnStop);
        SubscribeNetworkEvent<InternetSoundStatusEvent>(OnStatus);

        Subs.CVar(_cfg, InternetSoundCVars.Volume, OnVolumeChanged);
        Subs.CVar(_cfg, CCVars.AdminSoundsEnabled, OnAdminSoundsToggled);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        StopPlayback(true);

        lock (Receivers)
        {
            if (Receivers.TryGetValue(_transfer, out var receiver) && receiver == this)
                Receivers[_transfer] = null;
        }
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        for (var i = _retired.Count - 1; i >= 0; i--)
        {
            var (entity, stream) = _retired[i];
            if (Exists(entity))
                continue;

            stream.Dispose();
            _retired.RemoveAt(i);
        }

        if (_current == null)
            return;

        // Music loops keep starting tracks while paused; hold them until we finish.
        _contentAudio.EnforceMusicPauseWolfgate();
        _globalSound.EnforceEventMusicPauseWolfgate();

        if (!TryComp<AudioComponent>(_current.Entity, out var audio) || audio.State == AudioState.Stopped || !audio.Playing)
            StopPlayback(true);
    }

    private static async void Route(ITransferManager transfer, TransferReceivedEvent ev)
    {
        InternetSoundSystem? receiver;
        lock (Receivers)
        {
            Receivers.TryGetValue(transfer, out receiver);
        }

        byte[] data;
        try
        {
            await using var stream = ev.DataStream;
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer);
            data = buffer.ToArray();
        }
        catch (Exception e)
        {
            receiver?.Log.Error($"Failed to receive internet sound: {e}");
            return;
        }

        receiver?._task.RunOnMainThread(() => receiver.Receive(data));
    }

    private void Receive(byte[] data)
    {
        int id;
        string title, admin;
        byte[] audio;
        try
        {
            using var reader = new BinaryReader(new MemoryStream(data));
            id = reader.ReadInt32();
            title = reader.ReadString();
            admin = reader.ReadString();
            audio = reader.ReadBytes(reader.ReadInt32());
        }
        // EndOfStreamException isn't sandbox-whitelisted; its base is.
        catch (IOException)
        {
            Log.Warning("Received a truncated internet sound.");
            return;
        }

        // Players who muted admin sounds opt out of these too.
        if (!_cfg.GetCVar(CCVars.AdminSoundsEnabled))
            return;

        StopPlayback(false);

        AudioStream stream;
        try
        {
            stream = _audioManager.LoadAudioOggVorbis(new MemoryStream(audio), title);
        }
        catch (Exception e)
        {
            Log.Error($"Failed to decode internet sound \"{title}\": {e}");
            StopPlayback(true);
            return;
        }

        if (_audio.PlayGlobal(stream, null, AudioParams.Default) is not { } played)
        {
            stream.Dispose();
            StopPlayback(true);
            return;
        }

        _audio.SetGain(played.Entity, _cfg.GetCVar(InternetSoundCVars.Volume), played.Component);
        _current = new Playback(id, played.Entity, stream);

        _contentAudio.PauseMusicWolfgate();
        _globalSound.PauseEventMusicWolfgate();

        ShowPopup(title, admin);
    }

    /// <summary>
    /// Stops the current sound and closes the popup. Music resumes unless another sound is about to start.
    /// </summary>
    private void StopPlayback(bool resumeMusic)
    {
        if (_current is { } current)
        {
            _audio.Stop(current.Entity);
            _retired.Add((current.Entity, current.Stream));
            _current = null;
        }

        ClosePopup();

        if (!resumeMusic)
            return;

        _contentAudio.ResumeMusicWolfgate();
        _globalSound.ResumeEventMusicWolfgate();
    }

    private void ShowPopup(string title, string admin)
    {
        var popup = new InternetSoundPopup(title, admin, _cfg.GetCVar(InternetSoundCVars.Volume));
        popup.VolumeChanged += volume => _cfg.SetCVar(InternetSoundCVars.Volume, volume);
        popup.VolumeReleased += () => _cfg.SaveToFile();
        popup.StopPressed += () => StopPlayback(true);

        // Closing the radio turns it off.
        popup.OnClose += () =>
        {
            if (_popup != popup)
                return;

            _popup = null;
            StopPlayback(true);
        };

        _popup = popup;
        popup.OpenCenteredAt(new Vector2(0.9f, 0.12f));
    }

    private void ClosePopup()
    {
        if (_popup is not { } popup)
            return;

        _popup = null;
        popup.Close();
    }

    private void OnStop(InternetSoundStopEvent ev)
    {
        if (_current != null && (ev.Id == 0 || ev.Id == _current.Id))
            StopPlayback(true);
    }

    private void OnStatus(InternetSoundStatusEvent ev)
    {
        if (ev.IsError)
            _console.WriteError(null, ev.Message);
        else
            _console.WriteLine(null, ev.Message);

        StatusReceived?.Invoke(ev.Message, ev.IsError);
    }

    private void OnVolumeChanged(float volume)
    {
        if (_current != null)
            _audio.SetGain(_current.Entity, volume);

        _popup?.SetVolume(volume);
    }

    private void OnAdminSoundsToggled(bool enabled)
    {
        if (!enabled)
            StopPlayback(true);
    }
}
