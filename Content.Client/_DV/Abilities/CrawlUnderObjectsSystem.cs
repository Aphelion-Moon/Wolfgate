using Content.Shared._DV.Abilities;
using Robust.Client.GameObjects;
using Robust.Shared.GameStates;
using DrawDepth = Content.Shared.DrawDepth.DrawDepth;

namespace Content.Client._DV.Abilities;

public sealed partial class HideUnderTableAbilitySystem : SharedCrawlUnderObjectsSystem
{
    [Dependency] private AppearanceSystem _appearance = default!;
    [Dependency] private SpriteSystem _sprite = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;

    public override void Initialize()
    {
        base.Initialize();

        // WOLFGATE: driven off the networked flag as well as the appearance data. The appearance route
        // alone left the sprite at its walking depth, so a sneaking mob passed under the table in
        // collision but still drew on top of it.
        SubscribeLocalEvent<CrawlUnderObjectsComponent, AfterAutoHandleStateEvent>(OnHandleState);
        SubscribeLocalEvent<CrawlUnderObjectsComponent, AppearanceChangeEvent>(OnAppearanceChange);
    }

    private void OnHandleState(Entity<CrawlUnderObjectsComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        Log.Info($"WGDEBUG crawl: state -> Enabled={ent.Comp.Enabled}");
        Apply(ent, ent.Comp.Enabled);
    }

    private void OnAppearanceChange(EntityUid uid,
        CrawlUnderObjectsComponent component,
        AppearanceChangeEvent args)
    {
        var got = _appearance.TryGetData(uid, SneakMode.Enabled, out bool enabled);
        Log.Info($"WGDEBUG crawl: appearance -> got={got} enabled={enabled}");
        if (got)
            Apply((uid, component), enabled);
    }

    /// <summary>
    /// Drops the sprite to the depth mice and rats use while sneaking, and puts back whatever it was on
    /// the way out. Safe to call repeatedly: the stored depth is only taken once per sneak.
    /// </summary>
    private void Apply(Entity<CrawlUnderObjectsComponent> ent, bool sneaking)
    {
        if (!TryComp<SpriteComponent>(ent.Owner, out var sprite))
        {
            Log.Warning($"WGDEBUG crawl: {ToPrettyString(ent.Owner)} has no sprite");
            return;
        }

        Log.Info($"WGDEBUG crawl: {ToPrettyString(ent.Owner)} sneaking={sneaking} depth={sprite.DrawDepth} orig={ent.Comp.OriginalDrawDepth}");
        foreach (var other in _lookup.GetEntitiesInRange(Transform(ent.Owner).Coordinates, 0.45f))
        {
            if (other == ent.Owner)
                continue;
            if (TryComp<SpriteComponent>(other, out var otherSprite))
                Log.Info($"WGDEBUG   onTile: {ToPrettyString(other)} depth={otherSprite.DrawDepth} visible={otherSprite.Visible}");
        }

        if (sneaking)
        {
            ent.Comp.OriginalDrawDepth ??= sprite.DrawDepth;
            _sprite.SetDrawDepth((ent.Owner, sprite), (int) DrawDepth.SmallMobs);
        }
        else if (ent.Comp.OriginalDrawDepth is { } original)
        {
            _sprite.SetDrawDepth((ent.Owner, sprite), original);
            ent.Comp.OriginalDrawDepth = null;
        }
    }
}
