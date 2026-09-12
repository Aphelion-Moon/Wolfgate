using Content.Shared._WF.Shuttles;

namespace Content.Client.Shuttles.BUI;

public sealed partial class ShuttleConsoleBoundUserInterface
{
    private void WfOpen()
    {
        if (_window == null)
            return;

        _window.ShipStatusActiveChanged += (active, overlays) =>
            SendMessage(new ShipStatusRequestMessage(active, overlays));
    }

    protected override void ReceiveMessage(BoundUserInterfaceMessage message)
    {
        base.ReceiveMessage(message);

        if (message is ShipStatusMessage status)
            _window?.UpdateShipStatus(status);
    }
}
