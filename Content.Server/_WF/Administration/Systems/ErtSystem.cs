using System.Linq;
using Content.Server._NF.CryoSleep;
using Content.Server.Access.Systems;
using Content.Server.Administration.Commands;
using Content.Server.Administration.Logs;
using Content.Server.Administration.Managers;
using Content.Server.Ghost.Roles;
using Content.Server.Ghost.Roles.Components;
using Content.Server.Preferences.Managers;
using Content.Server.Shuttles.Components;
using Content.Server.Spawners.Components;
using Content.Server.Station.Systems;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared._WF.Administration.Ert;
using Content.Shared.Access;
using Content.Shared.Access.Systems;
using Content.Shared.Administration;
using Content.Shared.Database;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Mind.Components;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._WF.Administration.Systems;

/// <summary>
/// Admin ERT Builder: spawns an optional ship and one ghost role per member aboard it. Each role builds its body on
/// takeover, from the player's own character when its species is allowed.
/// </summary>
public sealed partial class ErtSystem : EntitySystem
{
    [Dependency] private IAdminManager _adminManager = default!;
    [Dependency] private IAdminLogManager _adminLogger = default!;
    [Dependency] private IPrototypeManager _prototypeManager = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private IServerPreferencesManager _preferences = default!;
    [Dependency] private AdminVesselSpawnSystem _vesselSpawn = default!;
    [Dependency] private GhostRoleSystem _ghostRoles = default!;
    [Dependency] private StationSpawningSystem _stationSpawning = default!;
    [Dependency] private IdCardSystem _idCard = default!;
    [Dependency] private SharedAccessSystem _access = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private MetaDataSystem _metaData = default!;

    private const string SpawnerPrototype = "WolfgateErtSpawner";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<ErtSpawnRequestEvent>(OnSpawnRequest);
        SubscribeLocalEvent<WolfgateErtSpawnerComponent, TakeGhostRoleEvent>(OnTakeRole);
    }

    private void OnSpawnRequest(ErtSpawnRequestEvent ev, EntitySessionEventArgs args)
    {
        var admin = args.SenderSession;
        if (!_adminManager.HasAdminFlag(admin, AdminFlags.Spawn))
            return;

        var spawned = TrySpawnTeam(admin, ev.Config, out var message);
        RaiseNetworkEvent(new ErtSpawnResultEvent(message, !spawned), Filter.SinglePlayer(admin));
    }

    /// <summary>
    /// Validates the config, spawns the ship if one was picked, and places one ghost role per member aboard it.
    /// </summary>
    public bool TrySpawnTeam(ICommonSession admin, ErtConfig config, out string message)
    {
        var team = Clean(config.TeamName, ErtLimits.MaxTextLength);
        if (team.Length == 0)
        {
            message = Loc.GetString("wf-ert-error-no-name");
            return false;
        }

        if (config.Members is < 1 or > ErtLimits.MaxMembers)
        {
            message = Loc.GetString("wf-ert-error-members", ("max", ErtLimits.MaxMembers));
            return false;
        }

        var leaderOutfit = string.IsNullOrEmpty(config.LeaderOutfit) ? config.MemberOutfit : config.LeaderOutfit;
        foreach (var outfit in new[] { config.MemberOutfit, leaderOutfit })
        {
            if (!_prototypeManager.HasIndex<StartingGearPrototype>(outfit))
            {
                message = Loc.GetString("wf-ert-error-outfit", ("id", outfit));
                return false;
            }
        }

        if (admin.AttachedEntity is not { Valid: true } adminEntity)
        {
            message = Loc.GetString("wf-ert-error-no-entity");
            return false;
        }

        var adminXform = Transform(adminEntity);
        if (adminXform.MapID == MapId.Nullspace)
        {
            message = Loc.GetString("wf-ert-error-no-map");
            return false;
        }

        // Unknown or non-round-start species and unknown groups are dropped rather than failing the whole team.
        var species = config.Species
            .Where(id => _prototypeManager.TryIndex<SpeciesPrototype>(id, out var proto) && proto.RoundStart)
            .Distinct()
            .ToList();
        var groups = config.AccessGroups
            .Where(id => _prototypeManager.HasIndex<AccessGroupPrototype>(id))
            .Distinct()
            .ToList();

        var spawnAt = adminXform.Coordinates;
        var shipNote = string.Empty;
        if (!string.IsNullOrEmpty(config.Vessel))
        {
            if (!_prototypeManager.TryIndex<VesselPrototype>(config.Vessel, out var vessel) || vessel.Abstract)
            {
                message = Loc.GetString("wf-ert-error-vessel", ("id", config.Vessel));
                return false;
            }

            if (!_vesselSpawn.TrySpawnVessel(vessel, adminXform.MapID, _transform.GetWorldPosition(adminXform), adminEntity, out var grid))
            {
                message = Loc.GetString("wf-ert-error-vessel-failed");
                return false;
            }

            _metaData.SetEntityName(grid.Value, $"{vessel.Name} ({team})");
            spawnAt = FindSpawnSpot(grid.Value) ?? spawnAt;
            shipNote = $" aboard {ToPrettyString(grid.Value)}";
        }

        var briefing = Clean(config.Briefing, ErtLimits.MaxBriefingLength);
        if (briefing.Length == 0)
            briefing = Loc.GetString("wf-ert-default-briefing", ("team", team));

        var leaderTitle = Clean(config.LeaderTitle, ErtLimits.MaxTextLength);
        if (leaderTitle.Length == 0)
            leaderTitle = Loc.GetString("wf-ert-default-leader-title", ("team", team));

        var memberTitle = Clean(config.MemberTitle, ErtLimits.MaxTextLength);
        if (memberTitle.Length == 0)
            memberTitle = Loc.GetString("wf-ert-default-member-title", ("team", team));

        for (var i = 0; i < config.Members; i++)
        {
            var leader = config.HasLeader && i == 0;
            SpawnSlot(spawnAt, new WolfgateErtSpawnerComponent
            {
                TeamName = team,
                Title = leader ? leaderTitle : memberTitle,
                Outfit = leader ? leaderOutfit : config.MemberOutfit,
                Species = species,
                AccessGroups = groups,
                KeepOutfitAccess = config.KeepOutfitAccess,
            }, briefing);
        }

        _adminLogger.Add(LogType.AdminCommands, LogImpact.High,
            $"{admin.Name} spawned ERT \"{team}\" with {config.Members} slots{shipNote}");

        message = Loc.GetString("wf-ert-spawned", ("team", team), ("count", config.Members));
        return true;
    }

    /// <summary>
    /// Creates one ghost role marker. It's filled in before initialising so the role registers with the right text.
    /// </summary>
    private void SpawnSlot(EntityCoordinates at, WolfgateErtSpawnerComponent settings, string briefing)
    {
        var uid = EntityManager.CreateEntityUninitialized(SpawnerPrototype, at);

        var slot = Comp<WolfgateErtSpawnerComponent>(uid);
        slot.TeamName = settings.TeamName;
        slot.Title = settings.Title;
        slot.Outfit = settings.Outfit;
        slot.Species = settings.Species;
        slot.AccessGroups = settings.AccessGroups;
        slot.KeepOutfitAccess = settings.KeepOutfitAccess;

        var role = Comp<GhostRoleComponent>(uid);
        role.RoleName = settings.Title;
        role.RoleDescription = briefing;

        _metaData.SetEntityName(uid, settings.Title);
        EntityManager.InitializeAndStartEntity(uid);
    }

    /// <summary>
    /// Somewhere sensible aboard: a spawn point, then a cryopod, then the shuttle console.
    /// </summary>
    private EntityCoordinates? FindSpawnSpot(EntityUid grid)
    {
        return FindOnGrid<SpawnPointComponent>(grid)
               ?? FindOnGrid<CryoSleepComponent>(grid)
               ?? FindOnGrid<ShuttleConsoleComponent>(grid);
    }

    private EntityCoordinates? FindOnGrid<T>(EntityUid grid) where T : IComponent
    {
        var query = EntityQueryEnumerator<T, TransformComponent>();
        while (query.MoveNext(out _, out _, out var xform))
        {
            if (xform.GridUid == grid)
                return xform.Coordinates;
        }

        return null;
    }

    /// <summary>
    /// Builds the responder: the player's own character if its species is allowed, otherwise a random allowed one.
    /// </summary>
    private void OnTakeRole(Entity<WolfgateErtSpawnerComponent> slot, ref TakeGhostRoleEvent args)
    {
        if (slot.Comp.Taken || !TryComp<GhostRoleComponent>(slot, out var role))
        {
            args.TookRole = false;
            return;
        }

        slot.Comp.Taken = true;
        _ghostRoles.UnregisterGhostRole((slot, role));

        var profile = PickProfile(args.Player, slot.Comp.Species);
        var mob = _stationSpawning.SpawnPlayerMob(Transform(slot).Coordinates, null, profile, null);
        _transform.AttachToGridOrMap(mob);

        SetOutfitCommand.SetOutfit(mob, slot.Comp.Outfit, EntityManager);
        SetUpId(mob, slot.Comp);

        EnsureComp<MindContainerComponent>(mob);
        _ghostRoles.GhostRoleInternalCreateMindAndTransfer(args.Player, slot, mob, role);

        QueueDel(slot);
        args.TookRole = true;

        _adminLogger.Add(LogType.Action, LogImpact.Low,
            $"{args.Player.Name} joined ERT \"{slot.Comp.TeamName}\" as {ToPrettyString(mob):mob} ({slot.Comp.Title})");
    }

    private HumanoidCharacterProfile PickProfile(ICommonSession player, List<string> species)
    {
        if (_preferences.GetPreferencesOrNull(player.UserId)?.SelectedCharacter is HumanoidCharacterProfile own
            && (species.Count == 0 || species.Contains(own.Species.Id)))
        {
            return own;
        }

        var pick = species.Count > 0 ? _random.Pick(species) : SharedHumanoidAppearanceSystem.DefaultSpecies;
        return HumanoidCharacterProfile.RandomWithSpecies(pick);
    }

    /// <summary>
    /// Puts the responder's name and team title on their ID, then applies the chosen access.
    /// </summary>
    private void SetUpId(EntityUid mob, WolfgateErtSpawnerComponent slot)
    {
        if (!_idCard.TryFindIdCard(mob, out var id))
            return;

        _idCard.TryChangeFullName(id, Name(mob), id.Comp);
        _idCard.TryChangeJobTitle(id, slot.Title, id.Comp);

        if (!slot.KeepOutfitAccess)
            _access.TrySetTags(id, Array.Empty<ProtoId<AccessLevelPrototype>>());

        if (slot.AccessGroups.Count > 0)
            _access.TryAddGroups(id, slot.AccessGroups.Select(group => new ProtoId<AccessGroupPrototype>(group)));
    }

    private static string Clean(string text, int maxLength)
    {
        var clean = text.Replace('\n', ' ').Replace('\r', ' ').Trim();
        return clean.Length > maxLength ? clean[..maxLength] : clean;
    }
}
