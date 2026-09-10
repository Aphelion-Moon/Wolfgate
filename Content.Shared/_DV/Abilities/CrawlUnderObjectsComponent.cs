using System.Numerics;
using Content.Shared.Actions;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._DV.Abilities;

/// <summary>
/// Lets a mob toggle sneaking: it moves slower and its circle fixtures shrink, so it can squeeze past mobs and
/// furniture. Tables still have to be climbed. See <see cref="SharedCrawlUnderObjectsSystem"/>.
/// </summary>
// WOLFGATE: HardLight balance. The Delta-V original dropped the mob under tables instead.
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CrawlUnderObjectsComponent : Component
{
    [DataField]
    public EntityUid? ToggleHideAction;

    [DataField]
    public EntProtoId? ActionProto;

    [DataField, AutoNetworkedField]
    public bool Enabled;

    [DataField]
    public float SneakSpeedModifier = 0.7f;

    /// <summary>
    /// Circle fixture radius multiplier while sneaking, relative to the unsqueezed radius.
    /// </summary>
    [DataField]
    public float SqueezeRadiusScale = 1f;

    /// <summary>
    /// Circle fixture radius multiplier applied once at startup, so a species can be bulkier than its prototype
    /// radius while standing.
    /// </summary>
    [DataField]
    public float UnsqueezedRadiusScale = 1f;

    /// <summary>
    /// Circle geometry captured when sneaking starts and restored when it ends, so cycles never drift.
    /// </summary>
    public List<(string key, Vector2 position, float radius)> ChangedCircles = new();

    /// <summary>
    /// Guards the unsqueezed baseline inflation so it is only applied once.
    /// </summary>
    public bool BaselineInflationApplied;

    /// <summary>
    /// Geometry captured while downed, which uses the squeeze scale too.
    /// </summary>
    public List<(string key, Vector2 position, float radius)> DownedCircles = new();

    public bool DownedScaleApplied;
}

public sealed partial class ToggleCrawlingStateEvent : InstantActionEvent { }
