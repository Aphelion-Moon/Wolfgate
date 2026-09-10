using System.Numerics;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Utility;

namespace Content.Client._WF.Humanoid;

/// <summary>
/// Full-size editor for the character description, opened from the small box under the preview so long
/// text is workable without the creator's preview column having to grow.
/// </summary>
public sealed class WolfgateDescriptionWindow : DefaultWindow
{
    private readonly TextEdit _edit;

    /// <summary>Raised on every keystroke, so the small box and the profile stay in step while typing.</summary>
    public Action<string>? OnTextChanged;

    public WolfgateDescriptionWindow(string text, string placeholder)
    {
        Title = Loc.GetString("humanoid-profile-editor-flavortext-tab");
        MinSize = new Vector2(620, 460);

        _edit = new TextEdit
        {
            HorizontalExpand = true,
            VerticalExpand = true,
            Placeholder = new Rope.Leaf(placeholder),
            TextRope = new Rope.Leaf(text),
        };
        _edit.OnTextChanged += _ => OnTextChanged?.Invoke(Text);

        Contents.AddChild(new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalExpand = true,
            Margin = new Thickness(8),
            Children = { _edit },
        });
    }

    public string Text => Rope.Collapse(_edit.TextRope);
}
