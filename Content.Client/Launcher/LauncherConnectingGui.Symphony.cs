using Content.Shared.Symphony;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.IoC;
using Robust.Shared.Network;

namespace Content.Client.Launcher
{
    /// <summary>
    /// The Symphony buttons on the connect-failed screen: a whitelist refusal carries the Discord link that gets
    /// the player whitelisted, and Link Discord opens it while Copy link copies it. Built here rather than in the
    /// XAML, so upstream's screen stays as it is; the code-behind carries two one-line calls.
    /// </summary>
    public sealed partial class LauncherConnectingGui
    {
        private BoxContainer? _symphonyRow;
        private string? _symphonyLink;

        private void InitSymphony()
        {
            var open = new Button { Text = Loc.GetString("connecting-symphony-link"), HorizontalAlignment = HAlignment.Center };
            open.StyleClasses.Add("OpenRight");
            open.OnPressed += _ =>
            {
                if (_symphonyLink != null)
                    IoCManager.Resolve<IUriOpener>().OpenUri(_symphonyLink);
            };

            var copy = new Button { Text = Loc.GetString("connecting-symphony-copy-link"), HorizontalAlignment = HAlignment.Center };
            copy.StyleClasses.Add("OpenLeft");
            copy.OnPressed += _ => CopyText(_symphonyLink);

            _symphonyRow = new BoxContainer
            {
                Orientation = BoxContainer.LayoutOrientation.Horizontal,
                Align = BoxContainer.AlignMode.Center,
                Visible = false,
            };
            _symphonyRow.AddChild(open);
            _symphonyRow.AddChild(copy);

            // Straight under the reason text, above the Retry and Copy Message row.
            ConnectFail.AddChild(_symphonyRow);
            _symphonyRow.SetPositionInParent(1);
        }

        /// <summary>
        /// Shows the buttons when the refusal carried an http(s) link, and hides them otherwise.
        /// A prefix check rather than System.Uri: the content sandbox does not allow that type.
        /// </summary>
        private void UpdateSymphonyLink(INetStructuredReason? reason)
        {
            var link = reason?.Message.StringOf(SharedSymphony.LinkKey);
            _symphonyLink = link != null && (link.StartsWith("http://") || link.StartsWith("https://"))
                ? link
                : null;

            if (_symphonyRow != null)
                _symphonyRow.Visible = _symphonyLink != null;
        }
    }
}
