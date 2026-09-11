using Content.Client._WF.Administration.UI.Ert;
using Content.Shared._WF.Administration;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Client._WF.Administration.Commands;

/// <summary>
/// Opens the ERT Builder window. Sent by the server's ertbuilder command, which does the permission check.
/// </summary>
[AnyCommand]
public sealed class ErtBuilderUiCommand : LocalizedCommands
{
    public override string Command => WolfgateAdminCommands.ErtBuilderUi;

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        new ErtBuilderWindow().OpenCentered();
    }
}
