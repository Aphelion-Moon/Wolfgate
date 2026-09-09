using System.Linq;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Shared.Configuration;
using Robust.Shared.IoC;
using Content.Client._WF.Stylesheets; // WOLFGATE
using Content.Shared._WF.CCVar; // WOLFGATE
using Robust.Shared; // WOLFGATE

namespace Content.Client.Stylesheets
{
    public sealed partial class StylesheetManager : IStylesheetManager
    {
        [Dependency] private IUserInterfaceManager _userInterfaceManager = default!;
        [Dependency] private IResourceCache _resourceCache = default!;
        [Dependency] private IConfigurationManager _cfg = default!; // WOLFGATE

        public Stylesheet SheetNano { get; private set; } = default!;
        public Stylesheet SheetSpace { get; private set; } = default!;

        public void Initialize()
        {
            // WOLFGATE: the active skin comes from a CVar and is swapped live when it changes
            _cfg.OnValueChanged(WolfgateCVars.UiStyle, _ => ApplySkin(syncHudTheme: true));
            ApplySkin(syncHudTheme: false);
            // WOLFGATE end
        }

        // WOLFGATE: rebuilds both sheets over the stock ones with the chosen skin and pushes them to every root
        private void ApplySkin(bool syncHudTheme)
        {
            var skin = WolfgateSkins.Get(_cfg.GetCVar(WolfgateCVars.UiStyle));
            var wolfgate = new StyleWolfgate(_resourceCache, skin);
            SheetNano = wolfgate.Apply(new StyleNano(_resourceCache).Stylesheet);
            SheetSpace = wolfgate.Apply(new StyleSpace(_resourceCache).Stylesheet);
            _userInterfaceManager.Stylesheet = SheetNano;

            // Follow with the matching HUD theme unless the player picked a theme that is not one of ours
            if (!syncHudTheme)
                return;
            var theme = _cfg.GetCVar(CVars.InterfaceTheme);
            if (theme != skin.HudTheme && (theme == string.Empty || WolfgateSkins.All.Any(s => s.HudTheme == theme)))
                _cfg.SetCVar(CVars.InterfaceTheme, skin.HudTheme);
        }
        // WOLFGATE end
    }
}
