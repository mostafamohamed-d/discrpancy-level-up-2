using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

// General Information about an assembly is controlled through the following
// set of attributes. Change these attribute values to modify the information
// associated with an assembly.
[assembly: AssemblyTitle("Discrepancy Scanner")]
[assembly: AssemblyDescription("Revit add-in that compares a Revit structural model against its ETABS design model and reports the discrepancies.")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyCompany("")]
[assembly: AssemblyProduct("Discrepancy Scanner")]
[assembly: AssemblyCopyright("Copyright © 2026")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]

// Setting ComVisible to false makes the types in this assembly not visible
// to COM components.  If you need to access a type in this assembly from
// COM, set the ComVisible attribute to true on that type.
[assembly: ComVisible(false)]

// The following GUID is for the ID of the typelib if this project is exposed to COM
[assembly: Guid("11e0c118-bda7-4fd0-9369-5dc9b384c330")]

// Version information for an assembly consists of the following four values:
//
//      Major Version
//      Minor Version
//      Build Number
//      Revision
//
// You can specify all the values or you can default the Build and Revision Numbers
// by using the '*' as shown below:
// [assembly: AssemblyVersion("1.0.*")]
// The xlsx reader and the loading schedule parser are internal: they are how this assembly
// reads a file, not something anyone else should be calling. The tests do call them, because
// they are the one part of the add-in that can be run without Revit or ETABS on the machine.
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("DiscrepancyScanner.Tests")]

// AssemblyVersion is written by the build, a new one each time - see StampBuildVersion in
// the project file for why.
[assembly: AssemblyFileVersion("1.0.0.0")]
