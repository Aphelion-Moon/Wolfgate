using Content.Shared.StatusIcon;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Ghost;

/// <summary>
/// Sections of the ghost orbit menu, in display order.
/// </summary>
[Serializable, NetSerializable]
public enum GhostOrbitCategory : byte
{
    Interest,
    Antagonist,
    Critical,
    Alive,
    Disconnected,
    Dead,
    Ghost,
    Location,
    Ship,
    Npc,
}

/// <summary>
/// One thing a ghost can orbit.
/// </summary>
[Serializable, NetSerializable]
public record struct GhostOrbitTarget
{
    public NetEntity Entity;
    public string Name;
    public GhostOrbitCategory Category;

    /// <summary>Job name, or what kind of point of interest this is.</summary>
    public string? Detail;

    /// <summary>Prototype the client draws an icon from when there is no job icon.</summary>
    public string? Prototype;

    public ProtoId<JobIconPrototype>? JobIcon;

    /// <summary>Damage as a fraction of the dead threshold, 0 to 1. Null when the target has no health.</summary>
    public float? Damage;

    /// <summary>Ghosts currently following the target, admins excluded for non-admin viewers.</summary>
    public int Followers;

    /// <summary>Player mind with no client attached (SSD or catatonic).</summary>
    public bool Disconnected;

    /// <summary>Only listed because the viewer is an admin (admin warp points, admin ghosts).</summary>
    public bool AdminOnly;
}

/// <summary>
/// Client asks for everything it can orbit. Answered with <see cref="GhostOrbitTargetsEvent"/>.
/// </summary>
[Serializable, NetSerializable]
public sealed class GhostOrbitRequestEvent : EntityEventArgs;

[Serializable, NetSerializable]
public sealed class GhostOrbitTargetsEvent(List<GhostOrbitTarget> targets) : EntityEventArgs
{
    public List<GhostOrbitTarget> Targets = targets;
}

/// <summary>
/// Client asks to orbit a target. Anything that moves is followed; fixed warp points are teleported to.
/// </summary>
[Serializable, NetSerializable]
public sealed class GhostOrbitWarpEvent(NetEntity target) : EntityEventArgs
{
    public NetEntity Target = target;
}
