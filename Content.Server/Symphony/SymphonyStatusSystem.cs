using System.Text.Json.Nodes;
using Content.Shared.Symphony;
using Robust.Server.ServerStatus;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;

namespace Content.Server.Symphony;

/// <summary>
/// Reports the Symphony hook version in /status, so the SSymphony panel can check the build it is talking to.
/// A system of its own rather than a line in the game ticker's status shell, so upstream's file stays untouched.
/// </summary>
public sealed partial class SymphonyStatusSystem : EntitySystem
{
    [Dependency] private IStatusHost _statusHost = default!;

    public override void Initialize()
    {
        base.Initialize();
        _statusHost.OnStatusRequest += OnStatusRequest;
    }

    public override void Shutdown()
    {
        _statusHost.OnStatusRequest -= OnStatusRequest;
        base.Shutdown();
    }

    // Raised off the main thread. A constant is the one thing that is safe to write from there.
    private static void OnStatusRequest(JsonNode json)
    {
        json["symphony_module"] = SharedSymphony.ModuleVersion;
    }
}
