using System.Numerics;
using Content.Client._WF.Stylesheets;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Utility;

namespace Content.Client._WF.Humanoid;

/// <summary>Grid tile for a marking: tinted icon with the name underneath, toggles like a radio or check button.</summary>
public sealed class WolfgateMarkingTile : ContainerButton
{
    public const string StyleClassTile = "MarkingTile";
    public const float TileWidth = 108f;

    private readonly TextureRect _icon;
    private readonly SpriteSpecifier? _sprite;

    /// <summary>Marking prototype id, null for the "none" tile.</summary>
    public string? MarkingId { get; }

    public WolfgateMarkingTile(string? markingId, string name, SpriteSpecifier? sprite, Direction direction)
    {
        MarkingId = markingId;
        _sprite = sprite;
        AddStyleClass(StyleClassTile);
        ToggleMode = true;
        ToolTip = name;
        MinSize = new Vector2(TileWidth - 4, 96);

        _icon = new TextureRect
        {
            TextureScale = new Vector2(2, 2),
            Stretch = TextureRect.StretchMode.KeepCentered,
            HorizontalAlignment = HAlignment.Center,
            MinSize = new Vector2(64, 64),
        };
        SetDirection(direction);

        var label = new Label
        {
            Text = name,
            ClipText = true,
            Align = Label.AlignMode.Center,
            HorizontalAlignment = HAlignment.Stretch,
            StyleClasses = { StyleWolfgate.StyleClassCreatorFieldLabel },
        };
        AddChild(new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 2,
            Margin = new Thickness(4, 4, 4, 2),
            Children = { _icon, label },
        });
    }

    /// <summary>Colour the icon is drawn in, so tiles preview the currently chosen colour.</summary>
    public Color Tint
    {
        set => _icon.ModulateSelfOverride = value;
    }

    /// <summary>Shows the sprite frame facing the given direction, matching the preview pawn.</summary>
    public void SetDirection(Direction direction)
    {
        _icon.Texture = _sprite == null ? null : FrameFor(_sprite, direction);
    }

    /// <summary>First frame of a marking sprite for a direction; sprites without directions give their only frame.</summary>
    public static Robust.Client.Graphics.Texture FrameFor(SpriteSpecifier sprite, Direction direction)
    {
        var sprites = IoCManager.Resolve<IEntitySystemManager>().GetEntitySystem<SpriteSystem>();
        return sprites.RsiStateLike(sprite).TextureFor(direction);
    }
}
