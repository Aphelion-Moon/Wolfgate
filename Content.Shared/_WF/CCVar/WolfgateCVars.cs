using Robust.Shared.Configuration;

namespace Content.Shared._WF.CCVar;

/// <summary>
/// Client-side Wolfgate settings.
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
}
