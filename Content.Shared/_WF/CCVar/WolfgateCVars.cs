using Robust.Shared.Configuration;

namespace Content.Shared._WF.CCVar;

/// <summary>
/// Wolfgate settings.
/// </summary>
[CVarDefs]
public sealed class WolfgateCVars
{
    /// <summary>
    /// Which Wolfgate UI skin the client draws. Matches a <c>WolfgateSkin</c> id: "Wolfgate" is the hyper-futurist
    /// look, "WolfgateRetro" the Aphelion cassette-futurism look. Changing it restyles the client immediately.
    /// </summary>
    public static readonly CVarDef<string> UiStyle =
        CVarDef.Create("wf.ui_style", "Wolfgate", CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    /// Kill switch for the anatomy system. When false every anatomy gate fails and every anatomy UI is hidden.
    /// </summary>
    public static readonly CVarDef<bool> AnatomyEnabled =
        CVarDef.Create("wf.anatomy_enabled", true, CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    /// Version of the adult content notice this client has acknowledged; 0 means never.
    /// </summary>
    public static readonly CVarDef<int> AnatomyNoticeSeen =
        CVarDef.Create("wf.anatomy_notice_seen", 0, CVar.CLIENTONLY | CVar.ARCHIVE);
}
