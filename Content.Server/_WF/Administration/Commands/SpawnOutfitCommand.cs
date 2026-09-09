using System.Linq;
using Content.Server.Administration;
using Content.Server.Administration.Commands;
using Content.Server.Administration.Logs;
using Content.Server.Administration.Managers;
using Content.Server.Mind;
using Content.Server.Preferences.Managers;
using Content.Server.Station.Systems;
using Content.Shared._WF.Administration;
using Content.Shared.Administration;
using Content.Shared.Database;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Robust.Shared.Console;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Administration.Commands;

/// <summary>
/// Spawns a humanoid wearing a starting gear outfit at a target entity, optionally handing the admin control of it.
/// </summary>
[AdminCommand(AdminFlags.Spawn)]
public sealed partial class SpawnOutfitCommand : LocalizedEntityCommands
{
    [Dependency] private IPrototypeManager _prototypeManager = default!;
    [Dependency] private IServerPreferencesManager _preferences = default!;
    [Dependency] private IAdminManager _adminManager = default!;
    [Dependency] private IAdminLogManager _adminLogger = default!;
    [Dependency] private StationSpawningSystem _stationSpawning = default!;
    [Dependency] private MindSystem _mind = default!;

    public override string Command => WolfgateAdminCommands.SpawnOutfit;

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length is < 2 or > 3)
        {
            shell.WriteError(Loc.GetString("cmd-spawnoutfit-invalid-args"));
            shell.WriteLine(Help);
            return;
        }

        if (shell.Player is not { } player)
        {
            shell.WriteError(Loc.GetString("cmd-spawnoutfit-no-player"));
            return;
        }

        if (!int.TryParse(args[0], out var targetInt)
            || !EntityManager.TryGetEntity(new NetEntity(targetInt), out var target)
            || !EntityManager.TryGetComponent<TransformComponent>(target, out var targetXform))
        {
            shell.WriteError(Loc.GetString("cmd-spawnoutfit-invalid-target"));
            return;
        }

        if (!_prototypeManager.TryIndex<StartingGearPrototype>(args[1], out var gear))
        {
            shell.WriteError(Loc.GetString("cmd-spawnoutfit-unknown-gear", ("id", args[1])));
            return;
        }

        var control = false;
        if (args.Length == 3 && !bool.TryParse(args[2], out control))
        {
            shell.WriteError(Loc.GetString("cmd-spawnoutfit-invalid-control"));
            return;
        }

        // Possession is a Fun-level power, same as controlmob.
        if (control && !_adminManager.HasAdminFlag(player, AdminFlags.Fun))
        {
            shell.WriteError(Loc.GetString("cmd-spawnoutfit-control-forbidden"));
            return;
        }

        // Use the admin's own character when they take control, otherwise a random one.
        HumanoidCharacterProfile? profile = null;
        if (control)
            profile = _preferences.GetPreferencesOrNull(player.UserId)?.SelectedCharacter as HumanoidCharacterProfile;
        profile ??= HumanoidCharacterProfile.RandomWithSpecies();

        var mob = _stationSpawning.SpawnPlayerMob(targetXform.Coordinates, null, profile, null);
        SetOutfitCommand.SetOutfit(mob, gear.ID, EntityManager);

        if (control)
            _mind.ControlMob(player.UserId, mob);

        _adminLogger.Add(LogType.EntitySpawn, LogImpact.Medium,
            $"{player.Name} spawned {EntityManager.ToPrettyString(mob):mob} wearing {gear.ID} at {EntityManager.ToPrettyString(target.Value):target}{(control ? " and took control" : "")}");
        shell.WriteLine(Loc.GetString("cmd-spawnoutfit-success", ("mob", EntityManager.ToPrettyString(mob)), ("gear", gear.ID)));
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        switch (args.Length)
        {
            case 1:
                return CompletionResult.FromHint(Loc.GetString("cmd-spawnoutfit-hint-target"));
            case 2:
                var gears = _prototypeManager.EnumeratePrototypes<StartingGearPrototype>()
                    .Select(gear => gear.ID)
                    .OrderBy(id => id);
                return CompletionResult.FromHintOptions(gears, Loc.GetString("cmd-spawnoutfit-hint-gear"));
            case 3:
                return CompletionResult.FromHintOptions(new[] { "true", "false" }, Loc.GetString("cmd-spawnoutfit-hint-control"));
            default:
                return CompletionResult.Empty;
        }
    }
}
