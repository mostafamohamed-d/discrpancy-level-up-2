using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DiscrepancyScanner.Wpf;

namespace DiscrepancyScanner
{
    /// <summary>
    /// Opens the WPF model check window: import ETABS and Revit, compare the beams and the
    /// columns, and read any result straight off the model.
    /// </summary>
    // Must match the Add-In Manager button used to load this DLL: its "Manual Mode" command
    // sets TransactionMode.Manual and its "Read Only Mode" command sets ReadOnly, and it warns
    // when a loaded command does not agree with that. Manual is the standard Revit mode and the
    // one to keep if this command ever needs to write to the model.
    [Transaction(TransactionMode.Manual)]
    public class ModelCheckCommand : IExternalCommand
    {
        // Runs when Revit creates the command, which is before Execute is compiled and so
        // before anything Execute reaches has to be found on disk. That is the whole point of
        // doing it here rather than inside Execute - see AddinAssemblies.
        static ModelCheckCommand()
        {
            AddinAssemblies.Install();
        }

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            if (!ToolLicensing.LicenseGate.EnsureLicensed(LicenseInfo.ProductId, LicenseInfo.Name))
            {
                return Result.Cancelled;
            }

            // The external event has to be created from a Revit API context, which this is; the
            // modeless window then uses it to run picks and other API work on the Revit thread.
            RevitRequestHandler requestHandler = RevitRequestHandler.Create();

            CheckWindow window = new CheckWindow(commandData, requestHandler);
            window.Show();
            return Result.Succeeded;
        }
    }
}
