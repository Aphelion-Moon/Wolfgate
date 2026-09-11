using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Content.Server.Administration.Logs;
using Content.Shared._WF.Audio.InternetSound;
using Content.Shared._WF.CCVar;
using Content.Shared.Database;
using Robust.Server.Player;
using Robust.Shared.Asynchronous;
using Robust.Shared.Configuration;
using Robust.Shared.Network;
using Robust.Shared.Network.Transfer;
using Robust.Shared.Player;

namespace Content.Server._WF.Audio.InternetSound;

/// <summary>
/// Admin internet sounds: fetches a link with yt-dlp, sends the audio to every connected client over the bulk
/// transfer channel, and stops it on request. A new sound replaces the current one.
/// </summary>
public sealed partial class InternetSoundSystem : EntitySystem
{
    [Dependency] private ITransferManager _transfer = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private ITaskManager _task = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IAdminLogManager _adminLogger = default!;

    private CancellationTokenSource? _fetch;
    private int _lastId;

    public override void Initialize()
    {
        base.Initialize();

        // Send-only here; clients register the receiving side.
        _transfer.RegisterTransferMessage(InternetSoundProtocol.TransferKey);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _fetch?.Cancel();
        _fetch = null;
    }

    /// <summary>
    /// Starts fetching a link; once converted, every connected client plays it. Progress goes to the requesting admin.
    /// </summary>
    public void Play(ICommonSession? admin, string url)
    {
        if (!_cfg.GetCVar(InternetSoundCVars.Enabled))
        {
            Report(admin, Loc.GetString("wf-internet-sound-disabled"), true);
            return;
        }

        // http(s) only, so yt-dlp can't be pointed at server files.
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            Report(admin, Loc.GetString("wf-internet-sound-invalid-url"), true);
            return;
        }

        _fetch?.Cancel();
        var fetch = _fetch = new CancellationTokenSource();

        var settings = new InternetSoundDownloader.Settings(
            _cfg.GetCVar(InternetSoundCVars.YtDlpPath),
            _cfg.GetCVar(InternetSoundCVars.FfmpegPath),
            _cfg.GetCVar(InternetSoundCVars.MaxDuration),
            _cfg.GetCVar(InternetSoundCVars.Timeout),
            _cfg.GetCVar(InternetSoundCVars.MaxSizeMb));
        var adminName = admin?.Name ?? Loc.GetString("wf-internet-sound-server-name");

        Report(admin, Loc.GetString("wf-internet-sound-fetching", ("url", uri.AbsoluteUri)), false);
        _adminLogger.Add(LogType.AdminCommands, LogImpact.Low, $"{adminName} requested internet sound {uri.AbsoluteUri}");

        Task.Run(async () =>
        {
            try
            {
                var result = await InternetSoundDownloader.Fetch(uri.AbsoluteUri, settings, fetch.Token);
                _task.RunOnMainThread(() => Broadcast(fetch, admin, adminName, uri.AbsoluteUri, result));
            }
            catch (OperationCanceledException) when (fetch.IsCancellationRequested)
            {
                // Replaced by a newer sound or stopped.
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

        RaiseNetworkEvent(new InternetSoundStopEvent(0));
        Report(admin, Loc.GetString("wf-internet-sound-stopped"), false);
        _adminLogger.Add(LogType.AdminCommands, LogImpact.Low, $"{admin?.Name ?? "Server"} stopped the internet sound");
    }

    private void Broadcast(CancellationTokenSource fetch, ICommonSession? admin, string adminName, string url, InternetSoundDownloader.Result result)
    {
        // Stopped or replaced while converting.
        if (_fetch != fetch)
            return;

        _fetch = null;

        var payload = Encode(++_lastId, result.Title, adminName, result.Audio);
        foreach (var session in _players.Sessions)
        {
            Send(session.Channel, payload);
        }

        Report(admin, Loc.GetString("wf-internet-sound-playing", ("title", result.Title), ("count", _players.PlayerCount)), false);
        _adminLogger.Add(LogType.AdminCommands, LogImpact.Low, $"{adminName} played internet sound \"{result.Title}\" ({url})");
    }

    private void Fail(CancellationTokenSource fetch, ICommonSession? admin, string message)
    {
        if (_fetch == fetch)
            _fetch = null;

        Report(admin, message, true);
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

    private static byte[] Encode(int id, string title, string admin, byte[] audio)
    {
        using var buffer = new MemoryStream(audio.Length + 256);
        using (var writer = new BinaryWriter(buffer, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(id);
            writer.Write(title);
            writer.Write(admin);
            writer.Write(audio.Length);
            writer.Write(audio);
        }

        return buffer.ToArray();
    }
}
