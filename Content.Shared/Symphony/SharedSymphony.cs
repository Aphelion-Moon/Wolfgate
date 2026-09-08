namespace Content.Shared.Symphony;

/// <summary>
/// The contract between this build and the SSymphony panel. Bump <see cref="ModuleVersion"/> whenever the status
/// field, the refusal keys or the use of the link ticket table changes; the panel then says which half is behind.
/// </summary>
public static class SharedSymphony
{
    /// <summary>
    /// Reported as symphony_module in /status. The panel compares it with the version it was written against.
    /// </summary>
    public const int ModuleVersion = 1;

    /// <summary>
    /// Key of the one-time Discord link URL in a whitelist refusal's structured properties.
    /// </summary>
    public const string LinkKey = "symphony_link";
}
