using System.IO;
using System.Numerics;
using System.Threading.Tasks;
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
/// Plays admin internet sounds sent by the server. The radio opens as soon as the header arrives; the audio is decoded
/// on a worker thread and only uploaded on the main thread, then music pauses until the sound ends or is stopped.
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

    private sealed record Header(int Id, string Title, string Admin);

    private sealed record Playback(int Id, EntityUid Entity, AudioStream Stream);

    private Playback? _current;

    /// <summary>
    /// Id of a sound still downloading or decoding, or 0.
    /// </summary>
    private int _loadingId;

    private InternetSoundPopup? _popup;

    /// <summary>
    /// Stopped sounds whose audio buffer is freed once their entity is gone.
    /// </summary>
    private readonly List<(EntityUid Entity, AudioStream Stream)> _retired = new();

    /// <summary>
    /// Status messages for the admin who requested a sound. Also written to the console.
    /// </summary>
    public event Action<string, bool>? StatusReceived;

    /// <summary>
    /// Server state changes, for admins only.
    /// </summary>
    public event Action<InternetSoundStateEvent>? StateReceived;

    /// <summary>
    /// Last state the server sent, so windows opened later know what's playing.
    /// </summary>
    public InternetSoundStateEvent? State { get; private set; }

    public override void Initialize()
    {
        base.Initialize();

        // The engine starts new music on its own audio frame; run after it so we can re-pause in the same frame.
        UpdatesAfter.Add(typeof(AudioSystem));

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
        SubscribeNetworkEvent<InternetSoundStateEvent>(OnState);

        Subs.CVar(_cfg, InternetSoundCVars.Volume, OnVolumeChanged);
        Subs.CVar(_cfg, CCVars.AdminSoundsEnabled, OnAdminSoundsToggled);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        StopPlayback(true);

        // FrameUpdate won't run again to free these, and entities are already gone by now.
        foreach (var (_, stream) in _retired)
        {
            stream.Dispose();
        }

        _retired.Clear();

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

    /// <summary>
    /// Reads the header first so the radio shows while audio is still arriving, then decodes on a worker thread.
    /// </summary>
    private static async void Route(ITransferManager transfer, TransferReceivedEvent ev)
    {
        InternetSoundSystem? receiver;
        lock (Receivers)
        {
            Receivers.TryGetValue(transfer, out receiver);
        }

        Header? header = null;
        try
        {
            await using var stream = ev.DataStream;

            var lengthBytes = new byte[4];
            await stream.ReadExactlyAsync(lengthBytes);
            var headerLength = lengthBytes[0] | lengthBytes[1] << 8 | lengthBytes[2] << 16 | lengthBytes[3] << 24;
            if (headerLength is <= 0 or > InternetSoundProtocol.MaxHeaderBytes)
                throw new IOException($"Bad internet sound header length {headerLength}.");

            var headerBytes = new byte[headerLength];
            await stream.ReadExactlyAsync(headerBytes);
            var loaded = header = ReadHeader(headerBytes);
            receiver?._task.RunOnMainThread(() => receiver.BeginLoading(loaded));

            // Capped so a bad or oversized transfer can't exhaust client memory.
            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(chunk)) > 0)
            {
                if (buffer.Length + read > InternetSoundProtocol.MaxPayloadBytes)
                    throw new IOException("Internet sound is larger than the client limit.");

                buffer.Write(chunk, 0, read);
            }

            var wav = buffer.ToArray();

            // Decoding a whole song takes a while; keep it off the main thread.
            var audio = await Task.Run(() => ImaAdpcmWav.Decode(wav))
                        ?? throw new IOException("Internet sound audio isn't IMA ADPCM WAV.");

            receiver?._task.RunOnMainThread(() => receiver.StartPlaying(loaded, audio));
        }
        catch (Exception e)
        {
            var failed = header;
            receiver?._task.RunOnMainThread(() => receiver.FailLoading(failed, e));
        }
    }

    private static Header ReadHeader(byte[] bytes)
    {
        using var reader = new BinaryReader(new MemoryStream(bytes));
        return new Header(reader.ReadInt32(), reader.ReadString(), reader.ReadString());
    }

    /// <summary>
    /// Replaces any current sound and opens the radio in its loading state.
    /// </summary>
    private void BeginLoading(Header header)
    {
        // Players who muted admin sounds opt out of these too.
        if (!_cfg.GetCVar(CCVars.AdminSoundsEnabled))
            return;

        StopPlayback(true);
        _loadingId = header.Id;
        ShowPopup(header);
    }

    private void StartPlaying(Header header, ImaAdpcmWav.DecodedAudio audio)
    {
        // Stopped, replaced or muted while loading.
        if (_loadingId != header.Id)
            return;

        _loadingId = 0;

        AudioStream stream;
        try
        {
            stream = _audioManager.LoadAudioRaw(audio.Samples, audio.Channels, audio.SampleRate, header.Title);
        }
        catch (Exception e)
        {
            Log.Error($"Failed to load internet sound \"{header.Title}\": {e}");
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
        _current = new Playback(header.Id, played.Entity, stream);

        _contentAudio.PauseMusicWolfgate();
        _globalSound.PauseEventMusicWolfgate();

        _popup?.SetPlaying();
    }

    private void FailLoading(Header? header, Exception e)
    {
        Log.Error($"Failed to receive internet sound: {e}");

        if (header != null && _loadingId == header.Id)
            StopPlayback(true);
    }

    /// <summary>
    /// Stops the current or loading sound and closes the radio.
    /// </summary>
    private void StopPlayback(bool resumeMusic)
    {
        _loadingId = 0;

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

    private void ShowPopup(Header header)
    {
        ClosePopup();

        var popup = new InternetSoundPopup(header.Title, header.Admin, _cfg.GetCVar(InternetSoundCVars.Volume));
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

        // Middle top; the window keeps itself on screen.
        popup.OpenCenteredAt(new Vector2(0.5f, 0f));
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
        var playing = _current != null && (ev.Id == 0 || ev.Id == _current.Id);
        var loading = _loadingId != 0 && (ev.Id == 0 || ev.Id == _loadingId);

        if (playing || loading)
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

    /// <summary>
    /// Asks the server what's playing. Admin windows call this when they open.
    /// </summary>
    public void RequestState()
    {
        RaiseNetworkEvent(new InternetSoundStateRequestEvent());
    }

    private void OnState(InternetSoundStateEvent ev)
    {
        State = ev;
        StateReceived?.Invoke(ev);
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
