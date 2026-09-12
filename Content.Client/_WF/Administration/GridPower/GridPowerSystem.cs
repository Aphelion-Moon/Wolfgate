using Content.Shared._WF.Administration.GridPower;

namespace Content.Client._WF.Administration.GridPower;

/// <summary>
/// Fetches the grid power list for the admin Grid Power window.
/// </summary>
public sealed class GridPowerSystem : EntitySystem
{
    public event Action<List<GridPowerInfo>>? ListReceived;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<GridPowerListEvent>(ev => ListReceived?.Invoke(ev.Grids));
    }

    public void RequestList()
    {
        RaiseNetworkEvent(new GridPowerListRequestEvent());
    }
}
