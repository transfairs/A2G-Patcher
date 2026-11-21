using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace A2GPatch
{
    /// <summary>
    /// Network play: the game holds a client/server layer for up to four computers that was never
    /// switched on (DirectPlay never created, no send thread, no receive pump). msvfw32.dll supplies it
    /// (dll/net.c) once a2gnet.ini next to the exe says Enabled=1; then the game asks at
    /// its first main menu whether to play alone, open a network game or join one. On the exes patched on
    /// disk the DLL carries no patch flags, it only adds network play. Offered where the exe holds the
    /// network layer's places (the 1999 exe and GOG's run.exe; the 2007 CD exe is packed). Taking it back
    /// removes the DLL and sets Enabled=0, so the names and the address the player entered stay.
    /// </summary>
    internal sealed class NetworkPlay : FilePatch
    {
        /// <summary>The settings next to the exe, written by the DLL as well (mode, address, names).</summary>
        internal const string IniName = "a2gnet.ini";
        private const string Section = "[A2GNet]";

        /// <summary>
        /// Places of the network layer, each once in both builds: the game's enqueue function, the start
        /// of its network init, the task sender and the call that enters a new manager in the table.
        /// </summary>
        internal static readonly byte?[][] Places =
        {
            "8B4424046683780C007512".Hex().Select(b => (byte?)b).ToArray(),
            new byte?[] { 0x57, 0xB9, 0xC4, 0x09, 0x00, 0x00, 0x33, 0xC0, 0xBF, null, null, null, 0x00, 0xF3, 0xAB, 0xB9, 0x90, 0x01, 0x00, 0x00 },
            new byte?[] { 0x83, 0xEC, 0x30, 0x53, 0x55, 0x56, 0x57, 0x6A, 0x00, 0x68, null, null, null, 0x00, 0x8D, 0x4C, 0x24, 0x2C, 0xE8 },
            "6A006A0151578D56FF5352B9".Hex().Select(b => (byte?)b).ToArray(),
        };

        /// <inheritdoc/>
        public override string Name => "Bis zu vier Rechner spielen ein Spiel gemeinsam, im lokalen Netz oder über das Internet";
        /// <inheritdoc/>
        public override string Title => "Netzwerkspiel";
        /// <inheritdoc/>
        public override PatchGroup Group => PatchGroup.Extras;

        /// <inheritdoc/>
        public override FileState State(GameFiles files)
        {
            if (files.Exe == null || Places.Any(p => FindAll(files.Exe, p).Count != 1))
                return FileState.Unknown;
            byte[] dll = files.Read(GameInstall.DllName);
            if (dll != null && GameInstall.MarkerAt(dll) < 0)
                return FileState.Unknown;
            return dll != null && IsEnabled(files.Read(IniName)) ? FileState.On : FileState.Off;
        }

        /// <inheritdoc/>
        public override void Switch(GameFiles files, bool on)
        {
            byte[] ini = files.Read(IniName);
            if (on)
            {
                byte[] dll = (byte[])GameInstall.DllBytes()?.Clone();
                int marker = dll == null ? -1 : GameInstall.MarkerAt(dll);
                if (marker < 0)
                    throw new InvalidOperationException($"In diesem Build von A2G-Patch fehlt die {GameInstall.DllName}.");
                for (int i = 0; i < GameInstall.FlagsBytes; i++)
                    dll[marker + GameInstall.DllMarker.Length + i] = 0;
                files.Write(GameInstall.DllName, dll);
                files.Write(IniName, SetEnabled(ini, true));
            }
            else
            {
                if (files.Read(GameInstall.DllName) != null)
                    files.Write(GameInstall.DllName, null);
                if (ini != null)
                    files.Write(IniName, SetEnabled(ini, false));
            }
        }

        // The ini's lines; it is ASCII (the DLL writes it through the ANSI profile functions).
        private static List<string> Lines(byte[] ini) =>
            ini == null ? new List<string>() : Encoding.Default.GetString(ini).Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).ToList();

        private static bool IsKey(string line) => line.TrimStart().StartsWith("Enabled", StringComparison.OrdinalIgnoreCase)
            && line.Substring(line.IndexOf("Enabled", StringComparison.OrdinalIgnoreCase) + 7).TrimStart().StartsWith("=");

        /// <summary>Whether the ini switches network play on (Enabled=1, as GetPrivateProfileInt reads it).</summary>
        internal static bool IsEnabled(byte[] ini) =>
            Lines(ini).Where(IsKey).Select(l => l.Substring(l.IndexOf('=') + 1).Trim()).FirstOrDefault() is string value
            && int.TryParse(value, out int number) && number != 0;

        /// <summary>The ini with Enabled set to <paramref name="on"/>; the other settings stay.</summary>
        internal static byte[] SetEnabled(byte[] ini, bool on)
        {
            string line = "Enabled=" + (on ? "1" : "0");
            List<string> lines = Lines(ini);
            int key = lines.FindIndex(l => IsKey(l));
            if (key >= 0)
                lines[key] = line;
            else
            {
                int section = lines.FindIndex(l => l.Trim().Equals(Section, StringComparison.OrdinalIgnoreCase));
                if (section < 0)
                {
                    if (lines.Count > 0 && lines[lines.Count - 1].Length == 0)
                        lines.RemoveAt(lines.Count - 1);
                    lines.Add(Section);
                    section = lines.Count - 1;
                }
                lines.Insert(section + 1, line);
            }
            if (lines[lines.Count - 1].Length != 0)
                lines.Add("");
            return Encoding.Default.GetBytes(string.Join("\r\n", lines));
        }
    }
}
