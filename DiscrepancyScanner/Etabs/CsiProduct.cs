using CSiAPIv1;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace DiscrepancyScanner
{
    /// <summary>
    /// Which CSI program a model is being read out of.
    ///
    /// The two are read through the same interfaces. CSI ships one assembly, CSiAPIv1.dll,
    /// with every one of its products, and the ETABS copy and the SAFE copy are the same
    /// interfaces down to the COM interface GUIDs, the method signatures and the values behind
    /// every enum. So there is one import engine rather than two, and this says only which
    /// running program it should attach to and what that program can be expected to hold.
    ///
    /// What differs is not the API but the model. ETABS is a building: many stories, and it
    /// says which one every object is on. SAFE is one floor, drawn about its own origin, with
    /// no story to speak of - which is why a SAFE model has to be told which Revit level it
    /// stands for before anything can be compared against it.
    /// </summary>
    public enum CsiProduct
    {
        Etabs,
        Safe
    }

    /// <summary>
    /// One ETABS or SAFE open on the machine, and the model it is holding.
    ///
    /// The process id is what makes it a particular instance rather than "whichever one
    /// answers": the API is attached to by process, so two models open at once can be told
    /// apart and the one that was meant can be read.
    /// </summary>
    public sealed class CsiInstance
    {
        public CsiInstance(CsiProduct product, int processId, string typeName, string modelPath)
        {
            Product = product;
            ProcessId = processId;
            TypeName = typeName ?? "";
            ModelPath = modelPath ?? "";
        }

        public CsiProduct Product { get; }

        public int ProcessId { get; }

        /// <summary>The COM type name this instance answered to, so it is attached the same way twice.</summary>
        public string TypeName { get; }

        /// <summary>Where the model is saved, or empty for one that has never been saved.</summary>
        public string ModelPath { get; }

        /// <summary>The model's file name, or a plain word for a model with no name yet.</summary>
        public string ModelName
        {
            get
            {
                if (string.IsNullOrEmpty(ModelPath))
                {
                    return "Unsaved model";
                }

                try
                {
                    return System.IO.Path.GetFileName(ModelPath);
                }
                catch (ArgumentException)
                {
                    return ModelPath;
                }
            }
        }

        /// <summary>
        /// What the picker shows: the program, the model, and the process id. The id is there
        /// because two instances can hold two models of the same name - the same model opened
        /// twice, most obviously - and then nothing else on the row tells them apart.
        /// </summary>
        public string DisplayName
        {
            get { return $"{CsiProducts.Name(Product)}  -  {ModelName}  (process {ProcessId})"; }
        }

        /// <summary>The folder the model is in, shown under the name so a path is not truncated.</summary>
        public string Location
        {
            get
            {
                if (string.IsNullOrEmpty(ModelPath))
                {
                    return "Not saved anywhere yet";
                }

                try
                {
                    return System.IO.Path.GetDirectoryName(ModelPath);
                }
                catch (ArgumentException)
                {
                    return "";
                }
            }
        }

        public override string ToString()
        {
            return DisplayName;
        }
    }

    /// <summary>
    /// Attaching to a running ETABS or SAFE, and the handful of things the import needs to
    /// know about which of the two it got.
    /// </summary>
    public static class CsiProducts
    {
        /// <summary>The name to put in front of the user - "ETABS" or "SAFE".</summary>
        public static string Name(CsiProduct product)
        {
            return product == CsiProduct.Safe ? "SAFE" : "ETABS";
        }

        /// <summary>
        /// True for a program that models one floor at a time rather than a whole building.
        ///
        /// Everything the check does is keyed by story - which level a wall rises to, which
        /// level a loading plan belongs to, which level the plan view is drawn at - so a model
        /// with no stories of its own is given one, named after the Revit level the alignment
        /// pairs it with. <see cref="SafeLevelLink"/> is what carries that pairing.
        /// </summary>
        public static bool IsSingleLevel(CsiProduct product)
        {
            return product == CsiProduct.Safe;
        }

        /// <summary>
        /// The COM type names each program registers. More than one is listed per product
        /// because CSI has renamed these across versions and the older name is still what some
        /// installs answer to; they are tried in turn and the first that answers wins.
        ///
        /// SAFE's really is called ETABSObject. That is not a mistake here - it is the name
        /// SAFE registers, left over from the API the two programs once shared.
        /// </summary>
        private static IEnumerable<string> TypeNames(CsiProduct product)
        {
            if (product == CsiProduct.Safe)
            {
                yield return "CSI.SAFE.API.ETABSObject";
                yield return "CSI.SAFE.API.SapObject";
            }
            else
            {
                yield return "CSI.ETABS.API.ETABSObject";
                yield return "CSI.ETABS.API.SapObject";
            }
        }

        /// <summary>
        /// The object every attach goes through, built straight out of the assembly the add-in
        /// was compiled against.
        ///
        /// Straight out of it, and never through the COM registry. This used to ask for the
        /// "CSiAPIv1.Helper" ProgID and cast what came back, which reads like the safer of the
        /// two and is the one thing here that cannot work. CSI registers that ProgID against
        /// NativeAPI/x64/CSiAPIv1.comhost.dll - the .NET COM host shipped beside the ordinary
        /// assembly - so what comes back is a raw COM object that answers to CSI's own
        /// interfaces and not to the .NET Framework interop's <see cref="cHelper"/>. The cast
        /// throws InvalidCastException, every caller here reports that the API could not be
        /// started, and the picker comes up empty with ETABS sitting open in front of the user.
        ///
        /// There was never anything to gain by asking. CSiAPIv1.dll is referenced by this
        /// project and sits beside the add-in wherever it is installed, so the class is simply
        /// there to be constructed - which is what CSI's own examples do.
        /// </summary>
        private static cHelper Helper(out string problem)
        {
            problem = null;
            try
            {
                return new Helper();
            }
            catch (Exception e)
            {
                problem = "The CSI API could not be started (CSiAPIv1.dll). "
                          + "Check that ETABS or SAFE is installed, and that CSiAPIv1.dll sits "
                          + "beside the add-in. " + e.Message;
                return null;
            }
        }

        /// <summary>
        /// Every ETABS and SAFE open on the machine, with the model each one holds.
        ///
        /// Worth asking rather than assuming. Attaching by name alone takes whichever instance
        /// the running object table happens to hand over, and on a machine with two models open
        /// - which is the ordinary way of working, a tower in one and a podium in the other -
        /// that is a coin toss the user does not see being tossed. A whole run against the
        /// wrong model looks exactly like a run against the right one until the findings make
        /// no sense.
        ///
        /// Found by process rather than by asking the API what is open, because the API has no
        /// way to say: one name, one answer. The process list says how many there are, and
        /// <c>GetObjectProcess</c> attaches to the one that was picked.
        /// </summary>
        public static IList<CsiInstance> RunningInstances(out string problem)
        {
            List<CsiInstance> found = new List<CsiInstance>();

            cHelper helper = Helper(out problem);
            if (helper == null)
            {
                return found;
            }

            Process[] processes;
            try
            {
                processes = Process.GetProcesses();
            }
            catch (Exception e)
            {
                problem = "The running programs could not be listed. " + e.Message;
                return found;
            }

            foreach (Process process in processes)
            {
                CsiProduct product;
                if (!IsCsiProcess(process, out product))
                {
                    continue;
                }

                CsiInstance instance = Describe(helper, process, product);
                if (instance != null)
                {
                    found.Add(instance);
                }
            }

            if (found.Count == 0 && problem == null)
            {
                problem = "No running ETABS or SAFE was found. Open the model and try again.";
            }

            return found.OrderBy(instance => instance.Product)
                        .ThenBy(instance => instance.ModelName, StringComparer.CurrentCultureIgnoreCase)
                        .ToList();
        }

        /// <summary>
        /// Whether a process is one of the two programs. Matched on the start of the name so
        /// that ETABS.exe, ETABS22.exe and SAFE.exe all read, whatever CSI called the release.
        /// </summary>
        private static bool IsCsiProcess(Process process, out CsiProduct product)
        {
            product = CsiProduct.Etabs;

            string name;
            try
            {
                name = process.ProcessName ?? "";
            }
            catch (Exception)
            {
                return false;
            }

            if (name.StartsWith("ETABS", StringComparison.OrdinalIgnoreCase))
            {
                product = CsiProduct.Etabs;
                return true;
            }

            if (name.StartsWith("SAFE", StringComparison.OrdinalIgnoreCase))
            {
                product = CsiProduct.Safe;
                return true;
            }

            return false;
        }

        /// <summary>
        /// What one running instance is holding, or null when it will not answer the API at
        /// all - which a process still starting up, or one on a licence it has not got, will
        /// not.
        /// </summary>
        private static CsiInstance Describe(cHelper helper, Process process, CsiProduct product)
        {
            foreach (string typeName in TypeNames(product))
            {
                try
                {
                    cOAPI application = helper.GetObjectProcess(typeName, process.Id);
                    if (application == null)
                    {
                        continue;
                    }

                    cSapModel model = application.SapModel;
                    if (model == null)
                    {
                        continue;
                    }

                    string path = "";
                    try
                    {
                        path = model.GetModelFilename(true) ?? "";
                    }
                    catch (Exception)
                    {
                        path = "";
                    }

                    return new CsiInstance(product, process.Id, typeName, path);
                }
                catch (Exception)
                {
                    // Not this name, or not answering. Try the other, then give up on it.
                }
            }

            return null;
        }

        /// <summary>
        /// The model held by one particular instance, the one the user picked off the list.
        /// </summary>
        public static cSapModel Attach(CsiInstance instance, out string problem)
        {
            problem = null;

            if (instance == null)
            {
                problem = "No model was picked.";
                return null;
            }

            cHelper helper = Helper(out problem);
            if (helper == null)
            {
                return null;
            }

            try
            {
                cOAPI application = helper.GetObjectProcess(instance.TypeName, instance.ProcessId);
                cSapModel model = application == null ? null : application.SapModel;

                if (model == null)
                {
                    problem = $"{instance.DisplayName} would not hand over its model. "
                              + "It may have been closed since the list was made.";
                }

                return model;
            }
            catch (Exception e)
            {
                problem = $"{instance.DisplayName} could not be attached to. "
                          + "It may have been closed since the list was made. " + e.Message;
                return null;
            }
        }

        /// <summary>
        /// The model of the running program, without asking which one.
        ///
        /// Kept for the callers that have no way to ask - the older forms, and anything running
        /// outside the check window. Where there is a user to ask, ask: see
        /// <see cref="RunningInstances"/>.
        /// </summary>
        public static cSapModel Attach(CsiProduct product, out string problem)
        {
            problem = null;
            string name = Name(product);

            cHelper helper = Helper(out problem);
            if (helper == null)
            {
                return null;
            }

            Exception last = null;
            foreach (string typeName in TypeNames(product))
            {
                try
                {
                    cOAPI application = helper.GetObject(typeName);
                    if (application == null)
                    {
                        continue;
                    }

                    cSapModel model = application.SapModel;
                    if (model != null)
                    {
                        return model;
                    }
                }
                catch (Exception e)
                {
                    last = e;
                }
            }

            problem = $"No running {name} was found. Open the model in {name} and try again."
                      + (last == null ? "" : " (" + last.Message + ")");
            return null;
        }
    }

    /// <summary>
    /// Which Revit level a SAFE model stands for.
    ///
    /// ETABS says what floor every object is on and the check reads it. SAFE has nothing to
    /// read: it is one floor, and nothing in the model says which floor of which building it
    /// is. It is not asked for separately. The alignment already pairs a design story with a
    /// Revit level to get its height, and a SAFE model has exactly one story, so that pairing
    /// is the answer - one place to say it, rather than two that could disagree.
    ///
    /// The SAFE model is never lifted. It stays where it was drawn, the same as an ETABS
    /// model, and the alignment's Z brings the Revit level down (or up) onto its floor. What
    /// this carries is what the alignment alone does not do: the name the one story is given,
    /// and the height the Revit model is narrowed to.
    /// </summary>
    public sealed class SafeLevelLink
    {
        /// <summary>The Revit level's name, which becomes the name of the SAFE story.</summary>
        public string LevelName = "";

        /// <summary>Held as well as the name so a renamed level still resolves.</summary>
        public long LevelId;

        /// <summary>
        /// Where the floor is in the Revit model, in mm, before the alignment has had anything
        /// to say about it: the level's elevation from Revit's internal origin, with the
        /// alignment's level offset added.
        ///
        /// Stored raw on purpose. The comparison happens in the design model's frame, so what
        /// the level is actually compared at is this plus whatever the alignment does to Z.
        /// </summary>
        public double LevelElevationMM;

        /// <summary>
        /// Where the level sits in the design model's frame: the Revit elevation with the
        /// alignment's Z added, read now rather than when the pairing was made. With the
        /// height taken from this same pairing, it is the elevation of the SAFE floor.
        /// </summary>
        public double AlignedElevationMM
        {
            get { return LevelElevationMM + DataContainer.ModelTransformation.Translation.Z; }
        }

        public bool IsPaired
        {
            get { return !string.IsNullOrWhiteSpace(LevelName); }
        }

        public override string ToString()
        {
            return IsPaired
                ? $"{LevelName} at {Math.Round(LevelElevationMM / 1000d, 3)} m"
                : "not paired with a Revit level";
        }
    }
}
