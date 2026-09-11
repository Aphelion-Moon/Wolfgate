using Content.Shared._WF.Administration.Ert;

namespace Content.Client._WF.Administration.Ert;

/// <summary>
/// Sends ERT Builder requests and relays the server's answer to the window.
/// </summary>
public sealed class ErtSystem : EntitySystem
{
    public event Action<string, bool>? ResultReceived;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<ErtSpawnResultEvent>(ev => ResultReceived?.Invoke(ev.Message, ev.IsError));
    }

    public void RequestSpawn(ErtConfig config)
    {
        RaiseNetworkEvent(new ErtSpawnRequestEvent(config));
    }
}
