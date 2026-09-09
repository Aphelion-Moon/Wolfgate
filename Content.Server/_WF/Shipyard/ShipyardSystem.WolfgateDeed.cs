using Content.Server.Shuttles.Components;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared.Access.Components;
using Content.Shared.Shuttles.Components;
using Robust.Shared.Player;

namespace Content.Server._NF.Shipyard.Systems;

/// <summary>
/// Wolfgate: deed assignment for ships spawned outside the shipyard. Lives in the shipyard partial
/// because the deed and lock components are access-restricted to it.
/// </summary>
public sealed partial class ShipyardSystem
{
    /// <summary>
    /// Writes a deed for the ship onto an ID card and locks the ship's consoles to it,
    /// mirroring a shipyard purchase without payment, records or vouchers.
    /// </summary>
    public bool TryAssignDeed(EntityUid shuttleUid, EntityUid idCard, ICommonSession owner, string shuttleName)
    {
        if (!HasComp<ShuttleComponent>(shuttleUid) || !HasComp<IdCardComponent>(idCard))
            return false;

        var ownerName = owner.AttachedEntity is { Valid: true } ownerEntity ? Name(ownerEntity).Trim() : owner.Name;

        var deedId = EnsureComp<ShuttleDeedComponent>(idCard);
        AssignShuttleDeedProperties(deedId, shuttleUid, shuttleName, ownerName, false);
        deedId.DeedHolder = idCard;
        Dirty(idCard, deedId);

        var deedShuttle = EnsureComp<ShuttleDeedComponent>(shuttleUid);
        AssignShuttleDeedProperties(deedShuttle, shuttleUid, shuttleName, ownerName, false);
        Dirty(shuttleUid, deedShuttle);

        // Lock every console on the ship to the deed.
        var consoles = EntityQueryEnumerator<ShuttleConsoleComponent, TransformComponent>();
        while (consoles.MoveNext(out var consoleUid, out _, out var xform))
        {
            if (xform.GridUid != shuttleUid)
                continue;

            var lockComp = EnsureComp<ShuttleConsoleLockComponent>(consoleUid);
            _shuttleConsoleLock.SetShuttleId(consoleUid, shuttleUid.ToString(), lockComp);
        }

        _shipOwnership.RegisterShipOwnership(shuttleUid, owner);
        _metaData.SetEntityName(shuttleUid, GetFullName(deedShuttle));
        return true;
    }
}
