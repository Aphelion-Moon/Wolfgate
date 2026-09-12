using Content.Shared._WF.Shuttles;

namespace Content.Client.Shuttles.UI;

public sealed partial class ShuttleConsoleWindow
{
    /// <summary>
    /// Raised when the whole-ship view comes on or off screen, so the console can stop asking the
    /// server for hull telemetry nobody is looking at.
    /// </summary>
    public event Action<bool, ShipOverlays>? ShipStatusActiveChanged;

    private void WfInitialize()
    {
        // Flipping an overlay changes what the server needs to send, so re-request with the new mask.
        ShipContainer.OverlaysChanged += () =>
        {
            if (ShipContainer.Visible)
                ShipStatusActiveChanged?.Invoke(true, ShipContainer.Overlays);
        };
    }

    private void WfSetShipMode(bool active)
    {
        if (ShipContainer.Visible == active)
            return;

        ShipContainer.Visible = active;

        if (!active)
            ShipContainer.ClearStatus();

        ShipStatusActiveChanged?.Invoke(active, ShipContainer.Overlays);
    }

    public void UpdateShipStatus(ShipStatusMessage message)
    {
        ShipContainer.UpdateStatus(message);
    }
}
