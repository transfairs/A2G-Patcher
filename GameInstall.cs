using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace A2GPatch
{
    /// <summary>How the patches get into the game in a folder.</summary>
    internal enum PatchKind
    {
        /// <summary>No known Anstoss 2 Gold in the folder.</summary>
        None,
        /// <summary>The exe on disk holds the code (original anstoss2.exe, GOG run.exe) and is patched in place.</summary>
        Exe,
        /// <summary>The exe is packed (2007 CD run.exe): msvfw32.dll in the folder patches the code at game start.</summary>
        Dll,
    }

    /// <summary>
    /// The game in one folder: how the patches apply there, which are applied, and applying the
    /// chosen ones.
    /// </summary>
    internal sealed class GameInstall
    {
        internal const string DllName = "msvfw32.dll";
        internal const string BackupSuffix = ".vor-a2g-patch";
        // The exes that may hold the game's code, in this order (anstoss2.exe is only a launcher in the
        // 2007 CD and GOG releases, run.exe the game).
        internal static readonly string[] ExeNames = { "anstoss2.exe", "run.exe" };
        // In A2G-Patch's msvfw32.dll, followed by a NUL and the flags byte (see dll/msvfw32.c).
        internal static readonly byte[] DllMarker = Encoding.ASCII.GetBytes("A2GPatchFlags\0");

        /// <summary>The msvfw32.dll to install (embedded at build time, see the csproj); null if this build has none. Tests replace it.</summary>
        internal static Func<byte[]> DllBytes = () => ReadResource("A2GPatch.msvfw32.dll");

        /// <summary>The bytes of this assembly's resource <paramref name="name"/>; null if there is none.</summary>
        internal static byte[] ReadResource(string name)
        {
            using (Stream stream = typeof(GameInstall).Assembly.GetManifestResourceStream(name))
            {
                if (stream == null)
                    return null;
                byte[] bytes = new byte[stream.Length];
                stream.Read(bytes, 0, bytes.Length);
                return bytes;
            }
        }

        /// <summary>Whether a game exe from <paramref name="folder"/> is running; tests replace it.</summary>
        internal static Func<string, bool> IsGameRunning = folder => Process.GetProcesses().Any(p => RunsFrom(p, folder));

        // Exe: the places of each patch found in the exe (patches not found are missing).
        private readonly Dictionary<Patch, Site[]> sites = new Dictionary<Patch, Site[]>();
        // Dll: the flags of the installed msvfw32.dll (0 without one).
        private byte flags;

        /// <summary>The game folder.</summary>
        public string Folder { get; }
        /// <summary>How the patches apply there.</summary>
        public PatchKind Kind { get; }
        /// <summary>The exe that holds (Exe) or runs (Dll) the game's code; null for None.</summary>
        public string ExePath { get; }
        /// <summary>For Dll: a msvfw32.dll is there that isn't A2G-Patch's, so nothing may be installed.</summary>
        public bool ForeignDll { get; }
        /// <summary>
        /// For Dll: A2G-Patch's msvfw32.dll is there but from another version (it differs from the one
        /// this build carries in more than the flags), so applying replaces it even with the same choice.
        /// </summary>
        public bool DllOutdated { get; private set; }

        private string DllPath => Path.Combine(this.Folder, DllName);
        private string BackupPath => this.ExePath + BackupSuffix;

        private GameInstall(string folder, PatchKind kind, string exePath)
        {
            this.Folder = folder;
            this.Kind = kind;
            this.ExePath = exePath;
            if (kind == PatchKind.Dll && File.Exists(this.DllPath))
            {
                byte[] dll = File.ReadAllBytes(this.DllPath);
                int marker = MarkerAt(dll);
                this.ForeignDll = marker < 0;
                this.flags = marker < 0 ? (byte)0 : dll[marker + DllMarker.Length];
                this.DllOutdated = marker >= 0 && IsOtherVersion(dll, DllBytes());
            }
        }

        // Whether the installed dll differs from the embedded one apart from the flags byte (false
        // without an embedded one: nothing to replace it with).
        private static bool IsOtherVersion(byte[] installed, byte[] embedded)
        {
            int marker = embedded == null ? -1 : MarkerAt(embedded);
            if (marker < 0)
                return false;
            if (installed.Length != embedded.Length || MarkerAt(installed) != marker)
                return true;
            int flagsAt = marker + DllMarker.Length;
            return Enumerable.Range(0, installed.Length).Any(i => i != flagsAt && installed[i] != embedded[i]);
        }

        /// <summary>Whether <paramref name="patch"/> can be switched here.</summary>
        public bool CanApply(Patch patch) =>
            this.Kind == PatchKind.Dll ? !this.ForeignDll : this.sites.ContainsKey(patch);

        /// <summary>Whether <paramref name="patch"/> is applied here.</summary>
        public bool IsApplied(Patch patch) =>
            this.Kind == PatchKind.Dll ? (this.flags & patch.Flag) != 0 : this.sites.TryGetValue(patch, out Site[] found) && found.All(s => s.IsPatched);

        /// <summary>
        /// Looks at the game in <paramref name="folder"/>: an exe in which a patch finds its places is
        /// patched on disk; without one, an existing run.exe that holds none of the patterns is taken
        /// as the packed 2007 CD exe.
        /// </summary>
        public static GameInstall Detect(string folder)
        {
            foreach (string name in ExeNames)
            {
                string path = Path.Combine(folder, name);
                if (!File.Exists(path))
                    continue;
                byte[] exe = File.ReadAllBytes(path);
                GameInstall install = new GameInstall(folder, PatchKind.Exe, path);
                foreach (Patch patch in Patch.All)
                {
                    Site[] found = patch.Find(exe);
                    if (found != null)
                        install.sites[patch] = found;
                }
                if (install.sites.Count > 0)
                    return install;
                if (name == "run.exe" && Patch.All.SelectMany(p => p.Patterns).All(p => Patch.FindAll(exe, p).Count == 0))
                    return new GameInstall(folder, PatchKind.Dll, path);
            }
            return new GameInstall(folder, PatchKind.None, null);
        }

        /// <summary>Where the marker starts in <paramref name="dll"/>, or -1 if it isn't A2G-Patch's msvfw32.dll.</summary>
        internal static int MarkerAt(byte[] dll)
        {
            List<long> found = Patch.FindAll(dll, Patch.Exact(DllMarker));
            return found.Count == 1 && found[0] + DllMarker.Length < dll.Length ? (int)found[0] : -1;
        }

        /// <summary>
        /// Applies exactly the patches in <paramref name="wanted"/> (others are taken back). The exe gets
        /// a backup next to it before it's changed for the first time; once it's back to the original,
        /// the backup goes again. Throws IOException or UnauthorizedAccessException where the files
        /// can't be written, InvalidOperationException where nothing may be changed.
        /// </summary>
        public void Apply(ICollection<Patch> wanted)
        {
            if (this.Kind == PatchKind.Exe)
                this.WriteExe(wanted);
            else if (this.Kind == PatchKind.Dll)
                this.WriteDll(wanted);
            else
                throw new InvalidOperationException("Kein bekanntes Anstoss 2 Gold in diesem Ordner.");
        }

        private void WriteExe(ICollection<Patch> wanted)
        {
            List<Site> changes = this.sites
                .SelectMany(p => p.Value.Where(s => s.IsPatched != wanted.Contains(p.Key)))
                .ToList();
            if (changes.Count == 0)
                return;
            if (!File.Exists(this.BackupPath))
                File.Copy(this.ExePath, this.BackupPath);
            using (FileStream exe = new FileStream(this.ExePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                foreach (Site site in changes)
                {
                    byte[] bytes = site.IsPatched ? site.Original : site.Patched;
                    exe.Position = site.Offset;
                    exe.Write(bytes, 0, bytes.Length);
                    site.IsPatched = !site.IsPatched;
                }
            }
            if (this.sites.Values.All(s => s.All(x => !x.IsPatched)) && File.ReadAllBytes(this.BackupPath).SequenceEqual(File.ReadAllBytes(this.ExePath)))
                File.Delete(this.BackupPath);
        }

        private void WriteDll(ICollection<Patch> wanted)
        {
            if (this.ForeignDll)
                throw new InvalidOperationException($"Die {DllName} in diesem Ordner gehört nicht zu A2G-Patch.");
            byte flags = (byte)wanted.Aggregate(0, (f, p) => f | p.Flag);
            if (flags == this.flags && (!this.DllOutdated || flags == 0))
                return;
            if (flags == 0)
                File.Delete(this.DllPath);
            else
            {
                byte[] dll = (byte[])DllBytes()?.Clone();
                int marker = dll == null ? -1 : MarkerAt(dll);
                if (marker < 0)
                    throw new InvalidOperationException($"In diesem Build von A2G-Patch fehlt die {DllName}.");
                dll[marker + DllMarker.Length] = flags;
                File.WriteAllBytes(this.DllPath, dll);
            }
            this.flags = flags;
            this.DllOutdated = false;
        }

        /// <summary>
        /// Folders that may hold the game, without duplicates and in this order: the given one, the
        /// program's own folder, the CD install (Ascaron's registry key) and the GOG install.
        /// </summary>
        public static List<string> CandidateFolders(string given, string own, Func<string, string, string> registry)
        {
            string[] folders =
            {
                given,
                own,
                registry(@"SOFTWARE\Ascaron\ANSTOSS 2\1.00", "InstalledTo"),
                registry(@"SOFTWARE\GOG.com\Games\1808817480", "path"),
            };
            List<string> result = new List<string>();
            foreach (string folder in folders.Where(f => !string.IsNullOrEmpty(f)))
            {
                string full = Path.GetFullPath(folder).TrimEnd('\\');
                if (Directory.Exists(full) && !result.Any(r => string.Equals(r, full, StringComparison.OrdinalIgnoreCase)))
                    result.Add(full);
            }
            return result;
        }

        /// <summary>A value under HKLM (the 32-bit view, as this x86 process sees it), or null.</summary>
        internal static string ReadRegistry(string key, string value)
        {
            using (RegistryKey opened = Registry.LocalMachine.OpenSubKey(key))
                return opened?.GetValue(value) as string;
        }

        // Whether the process runs a game exe from folder; processes we may not look into don't.
        private static bool RunsFrom(Process process, string folder)
        {
            try
            {
                string path = process.MainModule.FileName;
                return string.Equals(Path.GetDirectoryName(path), folder.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)
                    && ExeNames.Contains(Path.GetFileName(path), StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                return false;
            }
            finally
            {
                process.Dispose();
            }
        }
    }
}
