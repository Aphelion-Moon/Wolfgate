using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Content.Server.Administration.Logs;
using Content.Server.Administration.Managers;
using Content.Shared._WF.Audio.InternetSound;
using Content.Shared._WF.CCVar;
using Content.Shared.Administration;
using Content.Shared.Database;
using Robust.Server.Player;
using Robust.Shared.Asynchronous;
using Robust.Shared.Configuration;
using Robust.Shared.Network;
using Robust.Shared.Network.Transfer;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._WF.Audio.InternetSound;

/// <summary>
/// Admin internet sounds: fetches a link with yt-dlp, sends the audio to every connected client over the bulk
/// transfer channel, and stops it on request. One sound at a time; it must finish or be stopped before the next.
/// </summary>
public sealed partial class InternetSoundSystem : EntitySystem
{
    [Dependency] private ITransferManager _transfer = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private ITaskManager _task = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IAdminLogManager _adminLogger = default!;
    [Dependency] private IAdminManager _adminManager = default!;
    [Dependency] private IGameTiming _timing = default!;

    private const int MaxTitleLength = 200;

    /// <summary>
    /// Clients start once their download arrives, so a sound counts as playing a little past its length.
    /// </summary>
    private static readonly TimeSpan ArrivalGrace = TimeSpan.FromSeconds(15);

    private sealed record NowPlaying(string Title, string Admin, TimeSpan Ends);

    private CancellationTokenSource? _fetch;
    private NowPlaying? _playing;
    private int _lastId;

    public override void Initialize()
    {
        base.Initialize();

        // Send-only here; clients register the receiving side.
        _transfer.RegisterTransferMessage(InternetSoundProtocol.TransferKey);

        SubscribeNetworkEvent<InternetSoundStateRequestEvent>(OnStateRequest);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _fetch?.Cancel();
        _fetch = null;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_playing == null || _timing.CurTime < _playing.Ends)
            return;

        _playing = null;
        SendState();
    }

    /// <summary>
    /// Starts fetching a link; once converted, every connected client plays it. Refused while another sound is
    /// fetching or playing. Progress goes to the requesting admin.
    /// </summary>
    public void Play(ICommonSession? admin, string url)
    {
        if (!_cfg.GetCVar(InternetSoundCVars.Enabled))
        {
            Report(admin, Loc.GetString("wf-internet-sound-disabled"), true);
            return;
        }

        if (_fetch != null)
        {
            Report(admin, Loc.GetString("wf-internet-sound-busy-fetching"), true);
            return;
        }

        if (_playing != null)
        {
            Report(admin, Loc.GetString("wf-internet-sound-busy", ("title", _playing.Title)), true);
            return;
        }

        // http(s) only, so yt-dlp can't be pointed at server files.
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            Report(admin, Loc.GetString("wf-internet-sound-invalid-url"), true);
            return;
        }

        var fetch = _fetch = new CancellationTokenSource();

        var settings = new InternetSoundDownloader.Settings(
            _cfg.GetCVar(InternetSoundCVars.YtDlpPath),
            _cfg.GetCVar(InternetSoundCVars.FfmpegPath),
            _cfg.GetCVar(InternetSoundCVars.MaxDuration),
            _cfg.GetCVar(InternetSoundCVars.Timeout),
            _cfg.GetCVar(InternetSoundCVars.MaxSizeMb),
            Math.Clamp(_cfg.GetCVar(InternetSoundCVars.SampleRate), 8000, 48000),
            _cfg.GetCVar(InternetSoundCVars.Stereo) ? 2 : 1);
        var adminName = admin?.Name ?? Loc.GetString("wf-internet-sound-server-name");

        Report(admin, Loc.GetString("wf-internet-sound-fetching", ("url", uri.AbsoluteUri)), false);
        _adminLogger.Add(LogType.AdminCommands, LogImpact.Low, $"{adminName} requested internet sound {uri.AbsoluteUri}");
        SendState();

        Task.Run(async () =>
        {
            try
            {
                var result = await InternetSoundDownloader.Fetch(uri.AbsoluteUri, settings, fetch.Token);
                _task.RunOnMainThread(() => Broadcast(fetch, admin, adminName, uri.AbsoluteUri, result));
            }
            catch (OperationCanceledException) when (fetch.IsCancellationRequested)
            {
                // Stopped while fetching.
            }
            catch (OperationCanceledException)
            {
                _task.RunOnMainThread(() => Fail(fetch, admin, Loc.GetString("wf-internet-sound-error-timeout")));
            }
            catch (InternetSoundDownloader.FetchException e)
            {
                _task.RunOnMainThread(() => Fail(fetch, admin, Loc.GetString(e.LocKey, ("detail", e.Message))));
            }
            catch (Exception e)
            {
                _task.RunOnMainThread(() =>
                {
                    Log.Error($"Internet sound fetch for {uri.AbsoluteUri} failed: {e}");
                    Fail(fetch, admin, Loc.GetString("wf-internet-sound-error-unknown"));
                });
            }
        });
    }

    /// <summary>
    /// Cancels any fetch in progress and stops playback on every client.
    /// </summary>
    public void Stop(ICommonSession? admin)
    {
        _fetch?.Cancel();
        _fetch = null;
        _playing = null;

        RaiseNetworkEvent(new InternetSoundStopEvent(0));
        Report(admin, Loc.GetString("wf-internet-sound-stopped"), false);
        _adminLogger.Add(LogType.AdminCommands, LogImpact.Low, $"{admin?.Name ?? "Server"} stopped the internet sound");
        SendState();
    }

    private void Broadcast(CancellationTokenSource fetch, ICommonSession? admin, string adminName, string url, InternetSoundDownloader.Result result)
    {
        // Stopped while converting.
        if (_fetch != fetch)
            return;

        _fetch = null;

        var length = ImaAdpcmWav.GetDuration(result.Audio) ?? TimeSpan.FromSeconds(_cfg.GetCVar(InternetSoundCVars.MaxDuration));
        _playing = new NowPlaying(result.Title, adminName, _timing.CurTime + length + ArrivalGrace);

        var payload = Encode(++_lastId, result.Title, adminName, result.Audio);
        foreach (var session in _players.Sessions)
        {
            Send(session.Channel, payload);
        }

        Report(admin, Loc.GetString("wf-internet-sound-playing", ("title", result.Title), ("count", _players.PlayerCount)), false);
        _adminLogger.Add(LogType.AdminCommands, LogImpact.Low, $"{adminName} played internet sound \"{result.Title}\" ({url})");
        SendState();
    }

    private void Fail(CancellationTokenSource fetch, ICommonSession? admin, string message)
    {
        if (_fetch == fetch)
            _fetch = null;

        Report(admin, message, true);
        SendState();
    }

    private void OnStateRequest(InternetSoundStateRequestEvent ev, EntitySessionEventArgs args)
    {
        if (_adminManager.HasAdminFlag(args.SenderSession, AdminFlags.Fun))
            RaiseNetworkEvent(BuildState(), Filter.SinglePlayer(args.SenderSession));
    }

    /// <summary>
    /// Tells every admin who can play sounds what's going on, so their windows stay accurate.
    /// </summary>
    private void SendState()
    {
        var admins = _adminManager.ActiveAdmins
            .Where(session => _adminManager.HasAdminFlag(session, AdminFlags.Fun))
            .ToList();

        if (admins.Count > 0)
            RaiseNetworkEvent(BuildState(), Filter.Empty().AddPlayers(admins));
    }

    private InternetSoundStateEvent BuildState()
    {
        return new InternetSoundStateEvent(_fetch != null, _playing?.Title, _playing?.Admin);
    }

    private async void Send(INetChannel channel, byte[] payload)
    {
        try
        {
            await using var stream = _transfer.StartTransfer(channel, InternetSoundProtocol.TransferKey);
            await stream.WriteAsync(payload);
        }
        catch (Exception e)
        {
            // Usually a client that disconnected mid-transfer.
            Log.Warning($"Failed to send internet sound to {channel.UserName}: {e.Message}");
        }
    }

    private void Report(ICommonSession? admin, string message, bool isError)
    {
        if (admin == null)
        {
            Log.Info(message);
            return;
        }

        RaiseNetworkEvent(new InternetSoundStatusEvent(message, isError), Filter.SinglePlayer(admin));
    }

    /// <summary>
    /// Header first, length-prefixed, so clients can show the radio before the audio finishes arriving.
    /// </summary>
    private static byte[] Encode(int id, string title, string admin, byte[] audio)
    {
        using var header = new MemoryStream();
        using (var writer = new BinaryWriter(header, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(id);
            writer.Write(title.Length > MaxTitleLength ? title[..MaxTitleLength] : title);
            writer.Write(admin);
        }

        using var payload = new MemoryStream((int) header.Length + audio.Length + 4);
        using (var writer = new BinaryWriter(payload, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write((int) header.Length);
            writer.Write(header.GetBuffer(), 0, (int) header.Length);
            writer.Write(audio);
        }

        return payload.ToArray();
    }
}
