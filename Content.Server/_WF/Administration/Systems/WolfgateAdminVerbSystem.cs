using Content.Shared._WF.Administration;
using Content.Shared.Database;
using Content.Shared.Verbs;
using Robust.Server.Console;
using Robust.Shared.Player;
using Robust.Shared.Utility;

namespace Content.Server._WF.Administration.Systems;

/// <summary>
/// Right-click verbs under the Admin category for the Wolfgate admin tooling.
/// </summary>
public sealed partial class WolfgateAdminVerbSystem : EntitySystem
{
    [Dependency] private IConGroupController _groupController = default!;
    [Dependency] private IServerConsoleHost _console = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<GetVerbsEvent<Verb>>(OnGetVerbs);
    }

    private void OnGetVerbs(GetVerbsEvent<Verb> args)
    {
        if (!TryComp<ActorComponent>(args.User, out var actor))
            return;

        var player = actor.PlayerSession;

        // Spawn as Outfit: the picker opens client-side, the spawn command itself is admin-gated.
        if (_groupController.CanCommand(player, WolfgateAdminCommands.SpawnOutfit))
        {
            args.Verbs.Add(new Verb
            {
                Text = Loc.GetString("wf-admin-verbs-spawn-outfit"),
                Message = Loc.GetString("wf-admin-verbs-spawn-outfit-description"),
                Category = VerbCategory.Admin,
                Icon = new SpriteSpecifier.Texture(new("/Textures/Interface/VerbIcons/outfit.svg.192dpi.png")),
                Act = () => _console.RemoteExecuteCommand(player, $"{WolfgateAdminCommands.SpawnOutfitUi} {GetNetEntity(args.Target)}"),
                Impact = LogImpact.Medium,
            });
        }
    }
}
