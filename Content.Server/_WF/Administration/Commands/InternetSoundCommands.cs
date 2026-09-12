using Content.Server._WF.Audio.InternetSound;
using Content.Server.Administration;
using Content.Shared._WF.Administration;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._WF.Administration.Commands;

/// <summary>
/// Plays a YouTube, SoundCloud or other yt-dlp supported link to every connected player.
/// </summary>
[AdminCommand(AdminFlags.Fun)]
public sealed partial class PlayInternetSoundCommand : LocalizedEntityCommands
{
    [Dependency] private InternetSoundSystem _internetSound = default!;

    public override string Command => WolfgateAdminCommands.PlayInternetSound;

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 1)
        {
            shell.WriteError(Loc.GetString("cmd-playinternetsound-invalid-args"));
            shell.WriteLine(Help);
            return;
        }

        _internetSound.Play(shell.Player, args[0]);
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length == 1
            ? CompletionResult.FromHint(Loc.GetString("cmd-playinternetsound-hint"))
            : CompletionResult.Empty;
    }
}

/// <summary>
/// Stops the current internet sound for everyone and cancels any fetch in progress.
/// </summary>
[AdminCommand(AdminFlags.Fun)]
public sealed partial class StopInternetSoundCommand : LocalizedEntityCommands
{
    [Dependency] private InternetSoundSystem _internetSound = default!;

    public override string Command => WolfgateAdminCommands.StopInternetSound;

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        _internetSound.Stop(shell.Player);
    }
}
