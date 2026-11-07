using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;

namespace DiscrepancyScanner
{
    /// <summary>
    /// Finds the DLLs the add-in was built against, for the times Revit cannot.
    ///
    /// Loaded the ordinary way - a .addin manifest naming DiscrepancyScanner.dll - .NET looks
    /// for everything the add-in needs in the folder the add-in came out of, and finds it: the
    /// build puts CSiAPIv1.dll and EtabsTextModel.dll in that same folder. None of this is
    /// needed then.
    ///
    /// The Add-in Manager loads it differently, and has to. So that the built DLL is left
    /// unlocked and can be rebuilt without closing Revit, it copies DiscrepancyScanner.dll on
    /// its own into a fresh folder under %TEMP%\RevitAddins and loads that copy. Nothing goes
    /// with it, so the first call into the CSI API fails with
    ///
    ///     Could not load file or assembly 'CSiAPIv1, Version=1.0.0.0, Culture=neutral,
    ///     PublicKeyToken=8a93c9e72eabf762'. The system cannot find the file specified.
    ///
    /// and reading load sets out of the ETABS text file would fail the same way on
    /// EtabsTextModel a moment later.
    ///
    /// So the search .NET does is done again by hand: a copy this Revit session has already
    /// loaded, then the copy the build carries inside this DLL, then the folders that actually
    /// hold these files - where the add-in was loaded from, where it was built, and - for
    /// CSiAPIv1 - the CSI install itself. Going to the folders first was refused with "An
    /// operation is not legal in the current state" whenever the installed add-in was loaded
    /// in the same session and already held its own CSiAPIv1. That last one is worth having well beyond the Add-in Manager.
    /// CSiAPIv1.dll is not ours to hand out, so a copy of this add-in can reach a machine
    /// without it; ETABS is carrying one, and a machine with no ETABS on it cannot run this
    /// check at all.
    /// </summary>
    internal static class AddinAssemblies
    {
        private static readonly object gate = new object();

        private static readonly Dictionary<string, Assembly> answered =
            new Dictionary<string, Assembly>(StringComparer.OrdinalIgnoreCase);

        private static bool installed;

        /// <summary>
        /// Puts the search in place, once per Revit session however many commands are run.
        ///
        /// Called from the static constructor of every command the manifest names, because
        /// that runs when Revit creates the command - before Execute is compiled, and so
        /// before anything Execute mentions has to be found. The first line of Execute would
        /// be cutting it fine: a method's types are resolved before its first line runs, so an
        /// Execute that so much as names one of these would fail before reaching the call.
        /// </summary>
        public static void Install()
        {
            lock (gate)
            {
                if (installed)
                {
                    return;
                }

                installed = true;
            }

            // Installed the ordinary way, the files are beside the add-in and .NET finds them
            // itself; the search is only a backstop and goes where any handler goes, last.
            if (!IsLoneCopy())
            {
                AppDomain.CurrentDomain.AssemblyResolve += Resolve;
                return;
            }

            // The Add-in Manager's copy answers in its own load context first, then falls back to
            // the domain-wide search like everything else.
            HookOwnLoadContext();
            AppDomain.CurrentDomain.AssemblyResolve += Resolve;

            LoadEverythingReferencedNow();
        }

        /// <summary>
        /// Loads every assembly this add-in references into its own load context now, while the
        /// command is still starting.
        ///
        /// The Add-in Manager loads the add-in into a collectible context and unloads it the
        /// moment the command's Execute returns. The check window stays open past that, and
        /// anything it needs that is not loaded by then can never be: the first ETABS import
        /// asked the context for CSiAPIv1 and got "AssemblyLoadContext is unloading or was
        /// already unloaded", whatever was done to find the file. Loaded here, from the static
        /// constructor, the context is still alive and the Add-in Manager's own search finds
        /// them; later the runtime takes them from what the context already holds and asks it
        /// for nothing.
        /// </summary>
        private static void LoadEverythingReferencedNow()
        {
            object context = OwnLoadContext();
            MethodInfo byName = context?.GetType().GetMethod("LoadFromAssemblyName", new[] { typeof(AssemblyName) });
            if (byName == null)
            {
                return;
            }

            foreach (AssemblyName reference in Assembly.GetExecutingAssembly().GetReferencedAssemblies())
            {
                try
                {
                    byName.Invoke(context, new object[] { reference });
                }
                catch (Exception)
                {
                    // One that cannot be loaded now is no worse off than before: it is looked for
                    // again when it is first used, by every route above.
                }
            }
        }

        /// <summary>
        /// Where the development machine keeps the CSI API, loaded from its bytes by the Add-in
        /// Manager's copy - see <see cref="HotLoadCsiApi"/>. Not used by an installed add-in,
        /// which has its own copy beside it, nor where this file is missing.
        /// </summary>
        internal const string DevCsiApiPath = @"C:\Program Files\Computers and Structures\ETABS 22\CSiAPIv1.dll";

        private const string CsiApiName = "CSiAPIv1";

        /// <summary>
        /// True where this add-in is running without CSiAPIv1.dll beside it: the Add-in
        /// Manager's lone copy under %TEMP%, or one loaded from bytes with no folder at all.
        /// </summary>
        private static bool IsLoneCopy()
        {
            string folder = LoadedFrom();
            try
            {
                return folder == null || !File.Exists(Path.Combine(folder, CsiApiName + ".dll"));
            }
            catch (Exception)
            {
                return true;
            }
        }

        /// <summary>
        /// The CSI API and the text model loaded into this copy's own load context, by name.
        /// Kept apart from <see cref="answered"/>, which holds what the domain-wide search found.
        /// </summary>
        private static readonly Dictionary<string, Assembly> loadedInOwnContext =
            new Dictionary<string, Assembly>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// The load context this copy of the add-in was loaded into, or null on .NET Framework,
        /// which has none. Revit 2025 runs on .NET 8, and the Add-in Manager gives the copy it
        /// loads a context of its own, so that a rebuild can be loaded beside the last one.
        /// Reached by reflection because the add-in is compiled against .NET Framework.
        /// </summary>
        private static object OwnLoadContext()
        {
            try
            {
                Type type = typeof(object).Assembly.GetType("System.Runtime.Loader.AssemblyLoadContext");
                MethodInfo of = type?.GetMethod("GetLoadContext", BindingFlags.Public | BindingFlags.Static);
                return of?.Invoke(null, new object[] { Assembly.GetExecutingAssembly() });
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Answers this copy's requests for the CSI API and the text model in its own load
        /// context, which .NET asks before any domain-wide handler - so before the installed
        /// add-in's, when that is loaded in the same Revit session too.
        ///
        /// What it answers with is loaded into that same context. Handing the Add-in Manager's
        /// copy an assembly that lives in another context - the installed add-in's CSiAPIv1, or
        /// one loaded from a folder into the default context - is what .NET refused with "An
        /// operation is not legal in the current state". Development only: an installed add-in
        /// has both files beside it and never comes here.
        /// </summary>
        private static void HookOwnLoadContext()
        {
            try
            {
                object context = OwnLoadContext();
                EventInfo resolving = context?.GetType().GetEvent("Resolving");
                if (resolving == null)
                {
                    return;
                }

                MethodInfo answer = typeof(AddinAssemblies).GetMethod(nameof(ResolveInOwnContext),
                                                                     BindingFlags.NonPublic | BindingFlags.Static);
                resolving.AddEventHandler(context, Delegate.CreateDelegate(resolving.EventHandlerType, answer));
            }
            catch (Exception)
            {
                // The domain-wide search is still in place behind it.
            }
        }

        /// <summary>The Resolving handler: the context asking, and the assembly it is asking for.</summary>
        private static Assembly ResolveInOwnContext(object context, AssemblyName requested)
        {
            string name = requested?.Name;
            if (!string.Equals(name, CsiApiName, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(name, "EtabsTextModel", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            lock (gate)
            {
                Assembly already;
                if (loadedInOwnContext.TryGetValue(name, out already))
                {
                    return already;
                }
            }

            Assembly loaded = LoadInto(context, BytesFor(name));
            if (loaded != null)
            {
                lock (gate)
                {
                    loadedInOwnContext[name] = loaded;
                }
            }
            return loaded;
        }

        /// <summary>
        /// The bytes to load an assembly from: the development machine's CSI API where it is
        /// there, otherwise the copy the build carries inside this DLL.
        /// </summary>
        private static byte[] BytesFor(string name)
        {
            if (string.Equals(name, CsiApiName, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    if (File.Exists(DevCsiApiPath))
                    {
                        return File.ReadAllBytes(DevCsiApiPath);
                    }
                }
                catch (Exception)
                {
                    // Unreadable. The carried copy will do.
                }
            }

            return EmbeddedBytes(name);
        }

        /// <summary>Loads an assembly from bytes into the given load context.</summary>
        private static Assembly LoadInto(object context, byte[] bytes)
        {
            if (context == null || bytes == null)
            {
                return null;
            }

            try
            {
                MethodInfo load = context.GetType().GetMethod("LoadFromStream", new[] { typeof(Stream) });
                using (MemoryStream stream = new MemoryStream(bytes))
                {
                    return load?.Invoke(context, new object[] { stream }) as Assembly;
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// True for the failure to load the CSI API that <see cref="HotLoadCsiApi"/> is for.
        /// </summary>
        internal static bool IsCsiApiLoadFailure(Exception e)
        {
            for (Exception at = e; at != null; at = at.InnerException)
            {
                FileLoadException load = at as FileLoadException;
                FileNotFoundException missing = at as FileNotFoundException;
                string name = load?.FileName ?? missing?.FileName ?? "";
                if (name.StartsWith(CsiApiName, StringComparison.OrdinalIgnoreCase)
                    || (at.Message ?? "").IndexOf("'" + CsiApiName, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Loads the CSI API from <see cref="DevCsiApiPath"/>, from its bytes, and makes it the
        /// answer to every later request for it, so a failed import can be tried again. True
        /// when there was a file there to load.
        /// </summary>
        internal static bool HotLoadCsiApi()
        {
            if (!File.Exists(DevCsiApiPath))
            {
                return false;
            }

            // Into this copy's own load context where it has one, so the retry is answered from
            // the same place the Resolving handler answers from.
            object context = OwnLoadContext();
            Assembly loaded = context != null
                ? LoadInto(context, BytesFor(CsiApiName))
                : FromBytesOf(DevCsiApiPath);
            if (loaded == null)
            {
                return false;
            }

            lock (gate)
            {
                answered[CsiApiName] = loaded;
                if (context != null)
                {
                    loadedInOwnContext[CsiApiName] = loaded;
                }
            }
            return true;
        }

        private static Assembly FromBytesOf(string path)
        {
            try
            {
                return File.Exists(path) ? Assembly.Load(File.ReadAllBytes(path)) : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static Assembly Resolve(object sender, ResolveEventArgs e)
        {
            AssemblyName requested;
            string name;
            try
            {
                requested = new AssemblyName(e.Name);
                name = requested.Name;
            }
            catch (Exception)
            {
                return null;
            }

            // A by-name request for this add-in itself - which serialization makes, and which
            // only arises at all because the Add-in Manager loaded it from a copy - has to be
            // answered with the copy already running. Loading the built one again would put a
            // second set of the same types in play, and no object of one would be an object of
            // the other.
            //
            // Every rebuild the Add-in Manager loads is another copy with this handler, and the
            // oldest is asked first. Each build has a version of its own, so a request naming
            // one is answered with that copy rather than whichever copy happens to be asked.
            Assembly self = Assembly.GetExecutingAssembly();
            if (string.Equals(name, self.GetName().Name, StringComparison.OrdinalIgnoreCase))
            {
                return LoadedCopy(name, requested.Version) ?? self;
            }

            // Satellite and serializer assemblies are asked for by name and are meant to be
            // missing. Going to the disk to say no would be the slow way of saying it.
            if (name.EndsWith(".resources", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith(".XmlSerializers", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            lock (gate)
            {
                Assembly already;
                if (answered.TryGetValue(name, out already))
                {
                    return already;
                }
            }

            // The Add-in Manager's copy loads the CSI API from bytes - the development copy, then
            // the one carried inside - rather than share the installed add-in's.
            Assembly loaded = IsLoneCopy() && string.Equals(name, CsiApiName, StringComparison.OrdinalIgnoreCase)
                ? FromBytesOf(DevCsiApiPath) ?? Embedded(name) ?? AlreadyLoaded(requested) ?? Find(name)
                : AlreadyLoaded(requested) ?? Embedded(name) ?? Find(name);

            // Only an answer is kept. A miss is not worth remembering: looking through the
            // folders again next time costs a few File.Exists calls, on a path that is about
            // to end in a message box anyway.
            if (loaded != null)
            {
                lock (gate)
                {
                    answered[name] = loaded;
                }
            }

            return loaded;
        }

        /// <summary>
        /// The loaded copy of this add-in with exactly this version, or null when the request
        /// names none or no copy has it.
        /// </summary>
        private static Assembly LoadedCopy(string name, Version version)
        {
            if (version == null)
            {
                return null;
            }

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    AssemblyName loaded = assembly.GetName();
                    if (string.Equals(loaded.Name, name, StringComparison.OrdinalIgnoreCase)
                        && loaded.Version == version)
                    {
                        return assembly;
                    }
                }
                catch (Exception)
                {
                    // An assembly that will not say its name is not the one being looked for.
                }
            }

            return null;
        }

        /// <summary>
        /// A signed assembly of exactly the identity asked for that something in this Revit
        /// session has loaded already - the installed copy of this add-in's CSiAPIv1, while the
        /// Add-in Manager runs a build beside it. The same name, version and key are the same
        /// assembly, so it is handed back rather than loaded a second time from another folder,
        /// which is what was refused with "An operation is not legal in the current state".
        ///
        /// Signed only: an unsigned name says nothing about which build it came from, and
        /// EtabsTextModel from yesterday's install is not the one this build was compiled with.
        /// </summary>
        private static Assembly AlreadyLoaded(AssemblyName requested)
        {
            byte[] token = requested.GetPublicKeyToken();
            if (token == null || token.Length == 0)
            {
                return null;
            }

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    AssemblyName loaded = assembly.GetName();
                    if (string.Equals(loaded.Name, requested.Name, StringComparison.OrdinalIgnoreCase)
                        && (requested.Version == null || loaded.Version == requested.Version)
                        && token.SequenceEqual(loaded.GetPublicKeyToken() ?? new byte[0]))
                    {
                        return assembly;
                    }
                }
                catch (Exception)
                {
                    // An assembly that will not say its name is not the one being looked for.
                }
            }

            return null;
        }

        /// <summary>
        /// The copy the build carries inside this DLL - see EmbedAddinManagerDependencies in the
        /// project file - loaded from its bytes. Bytes have no folder and no load context to
        /// clash with another copy's, so this answers wherever the Add-in Manager put us.
        /// </summary>
        private static Assembly Embedded(string name)
        {
            byte[] bytes = EmbeddedBytes(name);
            if (bytes == null)
            {
                return null;
            }

            try
            {
                return Assembly.Load(bytes);
            }
            catch (Exception)
            {
                // Not loadable. The folders on disk are still there to look in.
                return null;
            }
        }

        /// <summary>The bytes of a DLL the build carries inside this one, or null when it carries none.</summary>
        private static byte[] EmbeddedBytes(string name)
        {
            try
            {
                using (Stream stream = Assembly.GetExecutingAssembly()
                           .GetManifestResourceStream("DiscrepancyScanner.Embedded." + name + ".dll"))
                {
                    if (stream == null)
                    {
                        return null;
                    }

                    using (MemoryStream bytes = new MemoryStream())
                    {
                        stream.CopyTo(bytes);
                        return bytes.ToArray();
                    }
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static Assembly Find(string name)
        {
            foreach (string folder in FoldersFor(name))
            {
                string path;
                try
                {
                    path = Path.Combine(folder, name + ".dll");
                }
                catch (ArgumentException)
                {
                    continue;
                }

                try
                {
                    if (File.Exists(path))
                    {
                        return Assembly.LoadFrom(path);
                    }
                }
                catch (Exception)
                {
                    // Unreadable, or not an assembly after all. Keep looking.
                }
            }

            return null;
        }

        /// <summary>
        /// Where to look, nearest first. The folder the add-in is running out of comes first,
        /// so that a proper deployment answers with the copy it was deployed with; the build
        /// folder second, so that the Add-in Manager's lone copy still finds what the build
        /// put beside the original.
        /// </summary>
        private static IEnumerable<string> FoldersFor(string name)
        {
            string loadedFrom = LoadedFrom();
            if (loadedFrom != null)
            {
                yield return loadedFrom;
            }

            if (!string.IsNullOrEmpty(AddinBuildFolder.Path))
            {
                yield return AddinBuildFolder.Path;
            }

            if (string.Equals(name, "CSiAPIv1", StringComparison.OrdinalIgnoreCase))
            {
                foreach (string folder in CsiFolders())
                {
                    yield return folder;
                }
            }
        }

        /// <summary>
        /// The folder this add-in is running out of, or null when there is not one - which is
        /// what an assembly loaded from bytes rather than from a file reports.
        /// </summary>
        private static string LoadedFrom()
        {
            try
            {
                string location = Assembly.GetExecutingAssembly().Location;
                return string.IsNullOrEmpty(location) ? null : Path.GetDirectoryName(location);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// The CSI installs on the machine that hold a CSiAPIv1.dll, likeliest first: a
        /// program that is running, then whatever is installed, newest ETABS before older
        /// ETABS before SAFE.
        ///
        /// A running program is asked first because it is the one the check is about to attach
        /// to, so its copy is the copy that matches. The rest is a backstop for the case where
        /// nothing is open yet.
        /// </summary>
        private static IEnumerable<string> CsiFolders()
        {
            List<string> folders = new List<string>();
            folders.AddRange(RunningCsiFolders());
            folders.AddRange(InstalledCsiFolders());

            return folders.Where(folder => File.Exists(Path.Combine(folder, "CSiAPIv1.dll")))
                          .Distinct(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// The folders the running ETABS and SAFE processes were started from.
        ///
        /// The name test is the one <see cref="CsiProducts"/> makes, written out again rather
        /// than shared: that class is the one that needs CSiAPIv1, and calling into it here
        /// would ask for the very assembly this is in the middle of finding.
        /// </summary>
        private static IEnumerable<string> RunningCsiFolders()
        {
            List<string> folders = new List<string>();

            Process[] processes;
            try
            {
                processes = Process.GetProcesses();
            }
            catch (Exception)
            {
                return folders;
            }

            foreach (Process process in processes)
            {
                try
                {
                    string name = process.ProcessName ?? "";
                    if (!name.StartsWith("ETABS", StringComparison.OrdinalIgnoreCase)
                        && !name.StartsWith("SAFE", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    folders.Add(Path.GetDirectoryName(process.MainModule.FileName));
                }
                catch (Exception)
                {
                    // A process that will not be opened, or one that ended while the list was
                    // being walked. There are other places to look.
                }
            }

            return folders;
        }

        /// <summary>
        /// Every CSI product folder under Program Files, best first. ETABS comes before SAFE
        /// because it is the product every user of this add-in has, and a newer release before
        /// an older one.
        /// </summary>
        private static IEnumerable<string> InstalledCsiFolders()
        {
            List<string> folders = new List<string>();

            foreach (string root in ProgramFilesRoots())
            {
                try
                {
                    string csi = Path.Combine(root, "Computers and Structures");
                    if (Directory.Exists(csi))
                    {
                        folders.AddRange(Directory.GetDirectories(csi));
                    }
                }
                catch (Exception)
                {
                    // Not readable. Try the other root.
                }
            }

            return folders.OrderByDescending(Rank);
        }

        private static IEnumerable<string> ProgramFilesRoots()
        {
            // ProgramW6432 is the 64-bit Program Files whatever the process is, which is where
            // CSI installs; the other is what a 64-bit Revit reports, and is the same folder.
            // Both are listed so that neither one being unset can leave nothing to look at.
            List<string> roots = new List<string>
            {
                Environment.GetEnvironmentVariable("ProgramW6432"),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)
            };

            return roots.Where(root => !string.IsNullOrEmpty(root))
                        .Distinct(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// How good a guess one product folder is: ETABS above SAFE, and a later release above
        /// an earlier one.
        /// </summary>
        private static long Rank(string folder)
        {
            string name;
            try
            {
                name = Path.GetFileName(folder) ?? "";
            }
            catch (ArgumentException)
            {
                return 0;
            }

            long product = 0;
            if (name.StartsWith("ETABS", StringComparison.OrdinalIgnoreCase))
            {
                product = 2;
            }
            else if (name.StartsWith("SAFE", StringComparison.OrdinalIgnoreCase))
            {
                product = 1;
            }

            return product * 10000 + Release(name);
        }

        /// <summary>
        /// The release number at the end of a product folder's name - the 22 of "ETABS 22".
        ///
        /// CSI numbered releases by year up to ETABS 2016 and by version from ETABS 17 on, so
        /// a year is folded down to the version it stands for. Otherwise "ETABS 2016" would
        /// read as the newest thing on the machine when it is the oldest.
        /// </summary>
        private static long Release(string name)
        {
            int end = name.Length;
            while (end > 0 && !char.IsDigit(name[end - 1]))
            {
                end--;
            }

            int start = end;
            while (start > 0 && char.IsDigit(name[start - 1]))
            {
                start--;
            }

            long release;
            if (start == end || !long.TryParse(name.Substring(start, end - start), out release))
            {
                return 0;
            }

            return release >= 2000 ? release - 2000 : release;
        }
    }
}
