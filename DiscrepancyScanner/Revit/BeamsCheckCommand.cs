using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.Attributes;



namespace DiscrepancyScanner
{
    [Transaction(TransactionMode.Manual)]
    public class BeamsCheckCommand : IExternalCommand
    {
        // Runs when Revit creates the command, which is before Execute is compiled and so
        // before anything Execute reaches has to be found on disk. That is the whole point of
        // doing it here rather than inside Execute - see AddinAssemblies.
        static BeamsCheckCommand()
        {
            AddinAssemblies.Install();
        }

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            Form1 form = new Form1(commandData);
            form.KeyPreview = true;
            
            form.Show();
            return Result.Succeeded;
            
        }
    }
}
