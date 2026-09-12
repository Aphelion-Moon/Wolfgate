using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Content.Server._WF.Audio.InternetSound;

/// <summary>
/// Fetches audio from a link with yt-dlp and converts it to IMA ADPCM WAV with ffmpeg. Runs off the main thread.
/// ADPCM rather than Ogg because clients can decode it off their main thread; the engine's Ogg loader can't be.
/// </summary>
public static class InternetSoundDownloader
{
    public sealed record Settings(string YtDlpPath, string FfmpegPath, int MaxDurationSeconds, int TimeoutSeconds, int MaxSizeMb, int SampleRate, int Channels);

    public sealed record Result(string Title, byte[] Audio);

    /// <summary>
    /// A fetch failure with a loc key; <see cref="Exception.Message"/> is passed to it as "detail".
    /// </summary>
    public sealed class FetchException(string locKey, string detail = "") : Exception(detail)
    {
        public readonly string LocKey = locKey;
    }

    private const string TitlePrefix = "WFTITLE ";
    private const string PathPrefix = "WFPATH ";

    /// <summary>
    /// Downloads and converts one link. Throws <see cref="FetchException"/> on failure and
    /// <see cref="OperationCanceledException"/> on cancel or timeout.
    /// </summary>
    public static async Task<Result> Fetch(string url, Settings settings, CancellationToken cancel)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        timeout.CancelAfter(TimeSpan.FromSeconds(settings.TimeoutSeconds));

        await EnsurePublicHost(new Uri(url), timeout.Token);

        var dir = Path.Combine(Path.GetTempPath(), "wolfgate-internet-sound", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        try
        {
            // "--" stops the link being read as an option; the filter refuses livestreams and long videos up front.
            var download = await Run(settings.YtDlpPath,
                new[]
                {
                    "--no-playlist", "--no-warnings", "--no-progress", "--no-simulate",
                    "--match-filter", $"!is_live & duration <=? {settings.MaxDurationSeconds}",
                    "-f", "bestaudio/best",
                    "-o", Path.Combine(dir, "source.%(ext)s"),
                    "--print", $"before_dl:{TitlePrefix}%(title)s",
                    "--print", $"after_move:{PathPrefix}%(filepath)s",
                    "--", url,
                },
                "wf-internet-sound-error-ytdlp-missing",
                timeout.Token);

            var lines = download.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var title = lines.FirstOrDefault(l => l.StartsWith(TitlePrefix))?[TitlePrefix.Length..] ?? url;
            var source = lines.FirstOrDefault(l => l.StartsWith(PathPrefix))?[PathPrefix.Length..];

            if (download.ExitCode != 0)
                throw new FetchException("wf-internet-sound-error-download", LastLine(download.Error));

            // A clean exit with no file means the match filter skipped it.
            if (source == null || !File.Exists(source))
                throw new FetchException("wf-internet-sound-error-rejected", settings.MaxDurationSeconds.ToString());

            var output = Path.Combine(dir, "sound.wav");
            var convert = await Run(settings.FfmpegPath,
                new[]
                {
                    "-hide_banner", "-loglevel", "error", "-nostdin", "-y",
                    "-i", source,
                    "-vn", "-map_metadata", "-1",
                    "-ac", settings.Channels.ToString(), "-ar", settings.SampleRate.ToString(),
                    "-c:a", "adpcm_ima_wav",
                    "-t", settings.MaxDurationSeconds.ToString(),
                    output,
                },
                "wf-internet-sound-error-ffmpeg-missing",
                timeout.Token);

            if (convert.ExitCode != 0 || !File.Exists(output))
                throw new FetchException("wf-internet-sound-error-transcode", LastLine(convert.Error));

            var audio = await File.ReadAllBytesAsync(output, timeout.Token);
            if (audio.Length > settings.MaxSizeMb * 1024 * 1024)
                throw new FetchException("wf-internet-sound-error-too-large", (audio.Length / (1024 * 1024)).ToString());

            return new Result(title, audio);
        }
        finally
        {
            try
            {
                Directory.Delete(dir, true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Leftover temp files are harmless; don't let cleanup fail a good fetch.
            }
        }
    }

    /// <summary>
    /// Runs a tool with arguments passed directly (no shell), killing it on cancel.
    /// </summary>
    private static async Task<(int ExitCode, string Output, string Error)> Run(string exe, IEnumerable<string> args, string missingKey, CancellationToken cancel)
    {
        var info = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        foreach (var arg in args)
        {
            info.ArgumentList.Add(arg);
        }

        // yt-dlp is Python; without this, non-ASCII titles break on Windows.
        info.Environment["PYTHONIOENCODING"] = "utf-8";
        info.Environment["PYTHONUTF8"] = "1";

        Process process;
        try
        {
            process = Process.Start(info) ?? throw new FetchException(missingKey, exe);
        }
        catch (Win32Exception)
        {
            throw new FetchException(missingKey, exe);
        }

        using (process)
        {
            var output = process.StandardOutput.ReadToEndAsync(cancel);
            var error = process.StandardError.ReadToEndAsync(cancel);

            try
            {
                await process.WaitForExitAsync(cancel);
            }
            catch (OperationCanceledException)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                    // Already exited.
                }

                throw;
            }

            return (process.ExitCode, await output, await error);
        }
    }

    /// <summary>
    /// Refuses links whose host resolves to a loopback, private, link-local or other non-public address, so the
    /// server can't be pointed at its own network. Redirects are followed by yt-dlp and aren't re-checked.
    /// </summary>
    private static async Task EnsurePublicHost(Uri uri, CancellationToken cancel)
    {
        IPAddress[] addresses;
        try
        {
            addresses = IPAddress.TryParse(uri.Host.Trim('[', ']'), out var literal)
                ? new[] { literal }
                : await Dns.GetHostAddressesAsync(uri.DnsSafeHost, cancel);
        }
        catch (SocketException)
        {
            throw new FetchException("wf-internet-sound-error-host", uri.Host);
        }

        if (addresses.Length == 0 || addresses.Any(IsNonPublic))
            throw new FetchException("wf-internet-sound-error-host", uri.Host);
    }

    private static bool IsNonPublic(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
            return true;

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
            return address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6UniqueLocal || address.IsIPv6Multicast;

        var b = address.GetAddressBytes();
        return b[0] is 0 or 10 or 127
               || b[0] == 172 && b[1] is >= 16 and <= 31
               || b[0] == 192 && b[1] == 168
               || b[0] == 169 && b[1] == 254
               || b[0] == 100 && b[1] is >= 64 and <= 127 // Carrier-grade NAT.
               || b[0] >= 224; // Multicast and reserved.
    }

    private static string LastLine(string text)
    {
        var line = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault() ?? string.Empty;
        return line.Length > 200 ? line[..200] : line;
    }
}
