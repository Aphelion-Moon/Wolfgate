namespace Content.Shared._WF.Administration.Ert;

/// <summary>
/// One ERT slot waiting for a ghost. Taking it builds a body from the player's character (or a random allowed
/// species), dresses it and sets up its ID. Filled in by the server when an admin spawns a team.
/// </summary>
[RegisterComponent]
public sealed partial class WolfgateErtSpawnerComponent : Component
{
    [ViewVariables]
    public string TeamName = string.Empty;

    /// <summary>
    /// Job title written on the ID.
    /// </summary>
    [ViewVariables]
    public string Title = string.Empty;

    /// <summary>
    /// Starting gear prototype id.
    /// </summary>
    [ViewVariables]
    public string Outfit = string.Empty;

    /// <summary>
    /// Allowed species ids. Empty allows any.
    /// </summary>
    [ViewVariables]
    public List<string> Species = new();

    [ViewVariables]
    public List<string> AccessGroups = new();

    [ViewVariables]
    public bool KeepOutfitAccess = true;

    /// <summary>
    /// Claimed this tick; the marker is deleted at the end of it. Stops a second ghost taking the same slot.
    /// </summary>
    [ViewVariables]
    public bool Taken;
}
