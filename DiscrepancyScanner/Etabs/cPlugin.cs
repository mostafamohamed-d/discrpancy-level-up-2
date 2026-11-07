
using CSiAPIv1;

namespace DiscrepancyScanner
{
    /// <summary>
    /// The entry point ETABS calls when this assembly is loaded as an ETABS plugin rather than
    /// as a Revit add-in.
    ///
    /// Dormant. Nothing in this assembly reaches it, the .addin file registers only the two
    /// Revit commands, and it is reached - if at all - by ETABS itself.
    ///
    /// It is worth knowing that this signature is now the CSiAPIv1 one rather than the ETABSv1
    /// one, because the whole add-in moved to the API that drives both ETABS and SAFE. ETABS
    /// hands its plugins ETABSv1 types, so if this is ever wanted as a live ETABS plugin again
    /// it needs the ETABSv1 reference put back and this one file compiled against it - the
    /// rest of the add-in has no such constraint, because it attaches to a running program
    /// rather than being handed a model by one.
    /// </summary>
    public class cPlugin
    {
        public void Main(ref cSapModel spModel, ref cPluginCallback pCallBack)
        {
            EtabsForm form = new EtabsForm(ref spModel, ref pCallBack);


            form.Show();



        }
        public long Info(ref string str)
        {
            str = "This plugin is used to automaticly check sections name and section dimensions \n  by Abdelrahman Sayed  ";

            return 0;
        }


    }
}
