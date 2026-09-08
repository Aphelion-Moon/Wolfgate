using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

public sealed partial class CCVars
{
    /// <summary>
    ///     Where the Symphony panel lives, e.g. https://symphony.example.com.
    ///     When set, players the manual whitelist turns away are handed a one-time link to get whitelisted through Discord.
    ///     Leave empty to disable.
    /// </summary>
    public static readonly CVarDef<string> SymphonyUrl =
        CVarDef.Create("symphony.url", string.Empty, CVar.SERVERONLY);
}
