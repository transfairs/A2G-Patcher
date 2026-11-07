using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace A2GPatch
{
    /// <summary>Where a <see cref="FilePatch"/> stands in a game folder.</summary>
    internal enum FileState
    {
        /// <summary>The files aren't as expected: the patch isn't offered.</summary>
        Unknown,
        /// <summary>Not applied, can be applied.</summary>
        Off,
        /// <summary>Applied, can be taken back.</summary>
        On,
        /// <summary>Applied, but can't be taken back (the original is missing).</summary>
        Fixed,
    }

    /// <summary>
    /// The files of a game folder as the <see cref="FilePatch"/>es see and change them: the exe's image in
    /// memory, other files from disk until a patch changes them. <see cref="GameInstall"/> writes the
    /// changes once every patch has made its own, so a patch that fails leaves nothing half written.
    /// </summary>
    internal sealed class GameFiles
    {
        private readonly string folder;
        private readonly Dictionary<string, byte[]> changed = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> order = new List<string>();

        /// <summary>The files in <paramref name="folder"/>, the exe's image being <paramref name="exe"/>.</summary>
        public GameFiles(string folder, byte[] exe)
        {
            this.folder = folder;
            this.Exe = exe;
        }

        /// <summary>The exe's image; patches replace it.</summary>
        public byte[] Exe { get; set; }

        /// <summary>The file <paramref name="name"/> (relative to the game folder) as it is or will be; null if it doesn't exist.</summary>
        public byte[] Read(string name)
        {
            if (this.changed.TryGetValue(name, out byte[] bytes))
                return bytes;
            string path = Path.Combine(this.folder, name);
            return File.Exists(path) ? File.ReadAllBytes(path) : null;
        }

        /// <summary>Will write <paramref name="bytes"/> to the file <paramref name="name"/>; null deletes it.</summary>
        public void Write(string name, byte[] bytes)
        {
            if (!this.changed.ContainsKey(name))
                this.order.Add(name);
            this.changed[name] = bytes;
        }

        /// <summary>The changed files in the order they were first written, null for deleted ones.</summary>
        public IEnumerable<KeyValuePair<string, byte[]>> Changes => this.order.Select(name => new KeyValuePair<string, byte[]>(name, this.changed[name]));

        /// <summary>The SHA-256 of <paramref name="bytes"/> as lowercase hex digits; null for null.</summary>
        internal static string Sha256(byte[] bytes)
        {
            if (bytes == null)
                return null;
            using (SHA256 sha = SHA256.Create())
                return string.Concat(sha.ComputeHash(bytes).Select(b => b.ToString("x2")));
        }
    }

    /// <summary>
    /// A patch for the GOG release. GOG sells the 2007 release by Kalypso, its run.exe taken out of
    /// the copy protection: that leaves things to fix the other releases don't need, and some of them
    /// change more than places in the code (the exe grows, other files of the game change). They are
    /// offered where the exe is patched on disk and the files are GOG's. msvfw32.dll knows only
    /// <see cref="Patch101"/>, which the 2007 CD release needs as well; the others have no flag.
    /// </summary>
    internal abstract class FilePatch : Patch
    {
        /// <inheritdoc/>
        public override ushort Flag => 0;
        /// <inheritdoc/>
        public sealed override IEnumerable<byte?[]> Patterns => Enumerable.Empty<byte?[]>();
        /// <inheritdoc/>
        public sealed override Site[] Find(byte[] image) => null;

        /// <summary>Where the patch stands in <paramref name="files"/>.</summary>
        public abstract FileState State(GameFiles files);

        /// <summary>
        /// Brings <paramref name="files"/> to the state <paramref name="on"/> (nothing changes if they are
        /// there already). Only called where <see cref="State"/> is <see cref="FileState.Off"/> or
        /// <see cref="FileState.On"/>, after the code patches. Throws InvalidOperationException if the result isn't
        /// as expected.
        /// </summary>
        public abstract void Switch(GameFiles files, bool on);

        /// <summary><paramref name="value"/> rounded up to a multiple of <paramref name="alignment"/>.</summary>
        internal static uint Align(long value, uint alignment) => (uint)((value + alignment - 1) / alignment * alignment);
    }

    /// <summary>
    /// Disarms what is left of the 2007 release's copy protection in GOG's run.exe. Two kinds of checks
    /// remain, and GOG's run.exe fails all of them; the penalties are silent:
    /// <list type="bullet">
    /// <item>The CD checks: 11 places call one signature routine and subtract the expected value; zero or
    /// more means "original CD". GOG replaced the subtractions with NOPs, which leaves the raw signature, so
    /// every check fails: from the second season all teams are called "Raubkopie", players drop to
    /// strength 1, manager data is overwritten. The patch puts in what a passed check leaves (<c>xor r32,r32</c>):
    /// <code>
    ///   call check / mov r32,eax / add esp,4 / nop x6   ->  ... / xor r32,r32 / nop x4
    ///   call check / add esp,4 / nop x5                 ->  ... / xor eax,eax / nop x3
    /// </code></item>
    /// <item>The integrity checks: five places compute a CRC32 of run.exe on disk (offset 0x80 to the end)
    /// and compare it with the value at offset 0x7C; five others a CRC32 of a stretch of their own code
    /// against a value stored in front of it (a descriptor {CRC, ?, length} right before the stub
    /// <c>sub esp,C0 / push ebx,ebp,esi,edi / mov ebp,esp</c>). On a mismatch a function skips its work or
    /// doubles negative values. The patch stores the correct values, after every other change to the exe.</item>
    /// </list>
    /// The 1999 release has none of these checks; the 2007 CD passes them with its CD and packed exe.
    /// Taking the patch back restores GOG's NOPs and values.
    /// </summary>
    internal sealed class CopyProtection : FilePatch
    {
        private const int DiscChecks = 11;
        private const int SelfChecks = 5;
        private const int FileCrcAt = 0x7C, FileCrcFrom = 0x80;
        // The self-checks lie in the first 0x2B0000 bytes of .text.
        private const int SelfCheckRange = 0x2B0000;
        private static readonly byte[] SelfCheckStub = { 0x81, 0xEC, 0xC0, 0x00, 0x00, 0x00, 0x53, 0x55, 0x56, 0x57, 0x8B, 0xEC };
        // The ModR/M byte of "mov esi|ebx|edi, eax" after a call -> the register's number.
        private static readonly Dictionary<byte, int> MoveFromEax = new Dictionary<byte, int> { [0xF0] = 6, [0xD8] = 3, [0xF8] = 7 };

        /// <summary>The values GOG's run.exe (1.0 fix2) has stored: the five code checks' CRCs in file order, then the file's.</summary>
        internal static readonly uint[] GogValues = { 0x31FEFB4F, 0x4C1D51B6, 0xB1E82794, 0x57DA615F, 0x97034F91, 0x0A73996B };

        /// <inheritdoc/>
        public override string Name => "Keine Kopierschutz-Strafen (z. B. „Raubkopie“ als Vereinsname)";

        /// <summary>The 11 CD checks: after each call of the signature routine, the subtraction's room.</summary>
        internal static Site[] DiscSites(byte[] image)
        {
            Dictionary<long, List<Site>> byTarget = new Dictionary<long, List<Site>>();
            // As a regular expression "E8 .... (8B (F0|D8|F8))? 83 C4 04", matches not overlapping.
            for (int i = 0; i + 8 <= image.Length; i++)
            {
                if (image[i] != 0xE8)
                    continue;
                bool move = i + 10 <= image.Length && image[i + 5] == 0x8B && MoveFromEax.ContainsKey(image[i + 6]);
                int add = move ? i + 7 : i + 5;
                if (image[add] != 0x83 || image[add + 1] != 0xC4 || image[add + 2] != 0x04)
                    continue;
                // With mov r32,eax the room is "sub r32,[mem]" (6 bytes), with eax itself "sub eax,imm32" (5).
                int register = move ? MoveFromEax[image[i + 6]] : 0, length = move ? 6 : 5, after = add + 3;
                byte[] nops = Enumerable.Repeat((byte)0x90, length).ToArray();
                byte[] xor = new byte[] { 0x31, (byte)(0xC0 + register * 9) }.Concat(nops.Skip(2)).ToArray();
                byte[] current = image.Slice(after, length);
                if (current.SequenceEqual(nops) || current.SequenceEqual(xor))
                {
                    long target = i + 5 + BitConverter.ToInt32(image, i + 1);
                    if (!byTarget.ContainsKey(target))
                        byTarget[target] = new List<Site>();
                    byTarget[target].Add(new Site { Offset = after, Original = nops, Patched = xor, IsPatched = current.SequenceEqual(xor) });
                }
                i = after - 1;
            }
            List<Site>[] checks = byTarget.Values.Where(s => s.Count == DiscChecks).ToArray();
            return checks.Length == 1 ? checks[0].ToArray() : null;
        }

        /// <summary>Where the five code checks' descriptors start, or null if they aren't there.</summary>
        internal static long[] SelfCheckDescriptors(byte[] image)
        {
            PeFile pe = PeFile.Read(image);
            if (pe == null)
                return null;
            PeFile.Section text = pe.Sections[0];
            byte[] code = image.Slice(text.Raw, (int)Math.Min(SelfCheckRange, text.RawSize));
            long[] found = Patch.FindAll(code, Exact(SelfCheckStub)).Select(at => at + text.Raw - 12).ToArray();
            return found.Length == SelfChecks ? found : null;
        }

        /// <inheritdoc/>
        public override FileState State(GameFiles files)
        {
            byte[] image = files.Exe;
            Site[] disc = DiscSites(image);
            long[] descriptors = SelfCheckDescriptors(image);
            if (disc == null || descriptors == null)
                return FileState.Unknown;
            return disc.All(s => s.IsPatched) && WithCorrectValues(image, descriptors).SequenceEqual(image) ? FileState.On : FileState.Off;
        }

        /// <inheritdoc/>
        public override void Switch(GameFiles files, bool on)
        {
            byte[] image = (byte[])files.Exe.Clone();
            foreach (Site site in DiscSites(image))
                (on ? site.Patched : site.Original).CopyTo(image, site.Offset);
            long[] descriptors = SelfCheckDescriptors(image);
            if (on)
                image = WithCorrectValues(image, descriptors);
            else
            {
                for (int i = 0; i < SelfChecks; i++)
                    BitConverter.GetBytes(GogValues[i]).CopyTo(image, descriptors[i]);
                BitConverter.GetBytes(GogValues[SelfChecks]).CopyTo(image, FileCrcAt);
            }
            files.Exe = image;
        }

        // A copy of image with the values the checks expect: the code's CRCs first, the file's last.
        private static byte[] WithCorrectValues(byte[] image, long[] descriptors)
        {
            byte[] result = (byte[])image.Clone();
            foreach (long at in descriptors)
            {
                int length = BitConverter.ToInt32(result, (int)at + 8);
                BitConverter.GetBytes(~Crc32.Compute(result, at + 12, length)).CopyTo(result, at);
            }
            BitConverter.GetBytes(Crc32.Compute(result, FileCrcFrom, result.Length - FileCrcFrom)).CopyTo(result, FileCrcAt);
            return result;
        }
    }

    /// <summary>CRC-32 (IEEE, as zlib's crc32).</summary>
    internal static class Crc32
    {
        private static readonly uint[] Table = Enumerable.Range(0, 256).Select(n =>
        {
            uint c = (uint)n;
            for (int k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            return c;
        }).ToArray();

        /// <summary>The CRC of <paramref name="length"/> bytes from <paramref name="at"/>; the game's own checks store it inverted.</summary>
        internal static uint Compute(byte[] bytes, long at, long length)
        {
            uint crc = 0xFFFFFFFF;
            for (long i = at; i < at + length; i++)
                crc = Table[(crc ^ bytes[i]) & 0xFF] ^ (crc >> 8);
            return ~crc;
        }
    }

    /// <summary>
    /// The changes of the official patch 1.01 for GOG's run.exe, which is built from the 1.00 source (the
    /// 1999 release contains them already). Found by comparing it with the 1.01 program and the 1999
    /// release, written anew in gog/a2v101.S (see there and README.md). The program changes go to a new
    /// section ".a2v101" at the end of the exe (behind GOG's signature, which no longer matches after any
    /// patch), and 13 places jump there or change in place. The data: two event texts end with "." instead
    /// of ":" (TEXTE\EreigGold.saq, also written as EreigGld.saq, the name 1.01 uses), one value in
    /// LAENDER.SAV changes from 3 to 4, and ARCHIVES\MoveList.dat (new in 1.01) is computed by
    /// <see cref="MoveList"/>. A data file that is neither GOG's nor 1.01's (e.g. from another data set)
    /// is left as it is. Taking the patch back removes the section and restores the files.
    /// The 2007 CD release is built from the same source (its run.exe, decrypted in memory, holds the
    /// 13 places at the same addresses) and has the same data files: there msvfw32.dll makes the program
    /// changes in memory (flag 1024, the code moved by <see cref="V101Code.Relative"/> and
    /// <see cref="V101Code.Absolute"/>), and A2G-Patch writes the data files (<see cref="SwitchData"/>).
    /// </summary>
    internal sealed class Patch101 : FilePatch
    {
        internal const string SectionName = ".a2v101";
        internal static readonly string Events = Path.Combine("TEXTE", "EreigGold.saq"), EventsShort = Path.Combine("TEXTE", "EreigGld.saq");
        internal static readonly string MoveListFile = Path.Combine("ARCHIVES", "MoveList.dat");
        internal static readonly string[] Countries = { Path.Combine("DATA.DEF", "LAENDER.SAV"), Path.Combine("DATA.VER", "LAENDER.SAV") };
        private const uint Code = 0x60000020;           // IMAGE_SCN_CNT_CODE | MEM_EXECUTE | MEM_READ
        private const uint Writable = 0x80000000;

        // The SHA-256 of the files of GOG and of 1.01; tests replace them.
        internal static string EventsGog = "f7d1b920fe90881a78a949748940185cc0c3bf3276b8511fc4544b723cbf04f5";
        internal static string Events101 = "df6a3684216271716d792e10e5b14a449701a92888c81fca2aba73078a9a6d0e";
        internal static string CountriesGog = "fe8818ea7024ad4650fb274501e6f6b524f6299249d2815d83f3124517a6a27f";
        internal static string Countries101 = "13762d5b8158a89952a1e724fc1a65046d0fd3a2e122089c6accc4d328d14675";
        private static readonly int[] EventTexts = { 10, 55 };
        private const int CountriesAt = 2065;

        /// <summary>One place the program changes: jumps to <see cref="Target"/> in the section, or gets <see cref="Bytes"/>.</summary>
        internal sealed class Hook
        {
            public uint Va, Target;
            public byte[] Original, Bytes;

            /// <summary>What the place holds patched.</summary>
            public byte[] Patched => this.Bytes ??
                new byte[] { 0xE9 }.Concat(BitConverter.GetBytes((int)(this.Target - (this.Va + 5)))).Concat(Enumerable.Repeat((byte)0x90, this.Original.Length - 5)).ToArray();
        }

        private static Hook Jump(uint va, string original, uint target) => new Hook { Va = va, Original = original.Hex(), Target = target };
        private static Hook Change(uint va, string original, string patched) => new Hook { Va = va, Original = original.Hex(), Bytes = patched.Hex() };

        /// <summary>The 13 places, as in run.exe (see the functions in gog/a2v101.S).</summary>
        internal static readonly Hook[] Hooks =
        {
            Jump(0x412DD0, "81ecf8090000", V101Code.Fix412dd0),                       // player lists (whole function)
            Jump(0x415F96, "74338a4e04", V101Code.Fix415f96),                         // message 0xC7 only above 47
            Jump(0x4160BD, "74328a066a01", V101Code.Fix4160bd),                       // the same, second place
            Change(0x468A4E, "688c097000", "6870097000"),                             // MoveList.dat from the game folder, not the CD
            Change(0x501DCF, "7454", "eb54"),                                         // message 0xA41 is gone
            Jump(0x557E3F, "808eae00000001", V101Code.Fix557e3f),                     // a block of counters cleared
            Change(0x557E64, "668b4e6083c41466014e5e", "83c41466ff465e90909090"),     // the counter grows by one
            Jump(0x568CA0, "83ec085355", V101Code.Fix568ca0),                         // counters below 1,000,000
            Jump(0x5A0BF7, "6a008d8c2478030000", V101Code.Fix5a0bf7),                 // a dialog's result into the team
            Jump(0x5A0F95, "e816d00c00", V101Code.Fix5a0f95),                         // the same, after DoModal
            Jump(0x5A2836, "33c98a4e208a91585a6c00885621", V101Code.Fix5a2836),       // second positions of new players
            Jump(0x5A2FC0, "568b742408", V101Code.Fix5a2fc0),                         // traits of new goalkeepers
            Jump(0x5BD540, "b86c9f0000", V101Code.Fix5bd540),                         // managers' data reordered with them
        };

        /// <inheritdoc/>
        public override string Name => "Patch 1.01 (Änderungen an Programm und Daten)";
        /// <inheritdoc/>
        public override ushort Flag => 1024;

        /// <summary>Where the program changes stand in <paramref name="image"/>.</summary>
        internal static FileState ProgramState(byte[] image)
        {
            PeFile pe = PeFile.Read(image);
            if (pe == null)
                return FileState.Unknown;
            if (pe.Sections.Last().Name == SectionName)
                return FileState.On;
            // The code is linked to where the image ends, and the next section header must fit before the first section's data.
            bool fits = pe.ImageBase + pe.SizeOfImage == V101Code.Address && pe.SectionTable + 40 * (pe.Sections.Count + 1) <= pe.Sections.Min(s => s.Raw);
            return fits && Hooks.All(h => Holds(image, pe.FileOffset(h.Va), h.Original)) ? FileState.Off : FileState.Unknown;
        }

        private static bool Holds(byte[] image, long at, byte[] bytes) => at >= 0 && image.Slice(at, bytes.Length).SequenceEqual(bytes);

        /// <summary>
        /// <paramref name="image"/> with the section added and the places changed. The section's data goes
        /// behind everything in the file, the signature included. GOG's run.exe has leftovers behind its
        /// section headers; the 40 bytes the new header overwrites are kept right behind the code, so taking
        /// the patch back restores the exe to the byte.
        /// </summary>
        internal static byte[] AddProgram(byte[] image)
        {
            PeFile pe = PeFile.Read(image);
            byte[] code = Convert.FromBase64String(V101Code.Code);
            long raw = Align(image.Length, pe.FileAlignment);
            uint rawSize = Align(code.Length + 40, pe.FileAlignment);
            byte[] result = new byte[raw + rawSize];
            image.CopyTo(result, 0);
            code.CopyTo(result, raw);
            int header = (int)(pe.SectionTable + 40 * pe.Sections.Count);
            Array.Copy(image, header, result, raw + code.Length, 40);
            Array.Clear(result, header, 40);
            Encoding.ASCII.GetBytes(SectionName).CopyTo(result, header);
            Put(result, header + 8, (uint)code.Length);
            Put(result, header + 12, pe.SizeOfImage);
            Put(result, header + 16, rawSize);
            Put(result, header + 20, (uint)raw);
            Put(result, header + 36, Code | Writable);
            BitConverter.GetBytes((ushort)(pe.Sections.Count + 1)).CopyTo(result, pe.OptionalHeader - 18);   // NumberOfSections
            Put(result, pe.OptionalHeader + 56, pe.SizeOfImage + Align(code.Length, pe.SectionAlignment));
            foreach (Hook hook in Hooks)
                hook.Patched.CopyTo(result, pe.FileOffset(hook.Va));
            return result;
        }

        /// <summary><paramref name="image"/> without the section (the last one) and with the places restored.</summary>
        internal static byte[] RemoveProgram(byte[] image)
        {
            PeFile pe = PeFile.Read(image);
            PeFile.Section section = pe.Sections.Last();
            List<PeFile.Section> others = pe.Sections.Take(pe.Sections.Count - 1).ToList();
            // The file ends with the other sections' data or the signature behind it.
            long end = Math.Max(others.Max(s => (long)s.Raw + s.RawSize), (long)pe.CertificateAt + pe.CertificateSize);
            byte[] result = image.Slice(0, (int)end);
            foreach (Hook hook in Hooks)
                hook.Original.CopyTo(result, pe.FileOffset(hook.Va));
            Array.Copy(image, section.Raw + section.VirtualSize, result, pe.SectionTable + 40 * others.Count, 40);
            BitConverter.GetBytes((ushort)others.Count).CopyTo(result, pe.OptionalHeader - 18);
            Put(result, pe.OptionalHeader + 56, section.Va);
            return result;
        }

        private static void Put(byte[] image, long at, uint value) => BitConverter.GetBytes(value).CopyTo(image, at);

        /// <summary>The event texts with the two texts ending in <paramref name="to"/> instead of <paramref name="from"/>; null if they don't.</summary>
        internal static byte[] ChangeEvents(byte[] data, byte from, byte to)
        {
            // Each byte is XORed with the previous encrypted byte, the first with the length's low byte.
            int length = BitConverter.ToInt32(data, 0);
            byte[] plain = new byte[length];
            byte key = (byte)length;
            for (int i = 0; i < length; i++)
            {
                plain[i] = (byte)(key ^ data[8 + i]);
                key = data[8 + i];
            }
            List<int> ends = Enumerable.Range(0, length).Where(i => plain[i] == 0).ToList();
            foreach (int text in EventTexts)
            {
                int last = ends[text] - 1;
                if (plain[last] != from)
                    return null;
                plain[last] = to;
            }
            byte[] result = (byte[])data.Clone();
            key = (byte)length;
            for (int i = 0; i < length; i++)
                result[8 + i] = key = (byte)(plain[i] ^ key);
            return result;
        }

        // What the patch can do with each data file: its state, and its contents patched or not.
        private sealed class DataFile
        {
            public string Name;
            public FileState State;
            public byte[] Off, On;
        }

        // The data files the patch changes, those that are neither GOG's nor 1.01's left out.
        private static List<DataFile> Data(GameFiles files)
        {
            List<DataFile> data = new List<DataFile>();
            byte[] events = files.Read(Events);
            string hash = GameFiles.Sha256(events);
            if (hash == EventsGog || hash == Events101)
            {
                byte[] gog = hash == EventsGog ? events : ChangeEvents(events, (byte)'.', (byte)':');
                byte[] v101 = hash == Events101 ? events : ChangeEvents(events, (byte)':', (byte)'.');
                data.Add(new DataFile { Name = Events, State = hash == Events101 ? FileState.On : FileState.Off, Off = gog, On = v101 });
                byte[] other = files.Read(EventsShort);
                if (other == null || other.SequenceEqual(v101))
                    data.Add(new DataFile { Name = EventsShort, State = other == null ? FileState.Off : FileState.On, On = v101 });
            }
            foreach (string name in Countries)
            {
                byte[] countries = files.Read(name);
                hash = GameFiles.Sha256(countries);
                if (hash == CountriesGog || hash == Countries101)
                {
                    byte[] gog = (byte[])countries.Clone(), v101 = (byte[])countries.Clone();
                    gog[CountriesAt] = (byte)'3';
                    v101[CountriesAt] = (byte)'4';
                    data.Add(new DataFile { Name = name, State = hash == Countries101 ? FileState.On : FileState.Off, Off = gog, On = v101 });
                }
            }
            byte[] moves = files.Read(MoveListFile);
            if (moves == null || MoveList.Hashes.Contains(GameFiles.Sha256(moves)))
                data.Add(new DataFile { Name = MoveListFile, State = moves == null ? FileState.Off : FileState.On, On = moves });   // built only when needed
            return data;
        }

        /// <inheritdoc/>
        public override FileState State(GameFiles files)
        {
            FileState program = ProgramState(files.Exe);
            if (program == FileState.Unknown)
                return FileState.Unknown;
            return program == FileState.On && Data(files).All(d => d.State == FileState.On) ? FileState.On : FileState.Off;
        }

        /// <inheritdoc/>
        public override void Switch(GameFiles files, bool on)
        {
            if (ProgramState(files.Exe) != (on ? FileState.On : FileState.Off))
                files.Exe = on ? AddProgram(files.Exe) : RemoveProgram(files.Exe);
            SwitchData(files, on);
        }

        /// <summary>Brings the data files to 1.01 (<paramref name="on"/>) or back; those that are neither GOG's nor 1.01's stay.</summary>
        internal static void SwitchData(GameFiles files, bool on)
        {
            foreach (DataFile file in Data(files).Where(d => d.State != (on ? FileState.On : FileState.Off)))
                files.Write(file.Name, on ? file.On ?? MoveList.Build() : file.Off);
        }
    }

    /// <summary>
    /// ARCHIVES\MoveList.dat, new in patch 1.01: the animation of a sign flying into a video window, 34
    /// frames. The game draws a 128x32 picture made at run time into each frame through a table: for each
    /// pixel of the frame, the byte offset of a pixel of the picture (16 bit per pixel, 256 bytes per row);
    /// 0 leaves the pixel out. File: u16 count, then per frame u16 number, x, y, width, height and
    /// width x height u16 offsets. This class holds only the sign's path (per frame its number, place and
    /// size, and the picture's four corners in it in 1/16 pixel) and computes each table as the perspective
    /// projection of the picture onto that quadrilateral, in decimal arithmetic so the result is the same
    /// everywhere. Inside the sign it is at most one pixel off the file of 1.01.
    /// </summary>
    internal static class MoveList
    {
        private const int Width = 128, Height = 32;

        // number, x, y, width, height, then the corners (top left, top right, bottom right, bottom left) or none for an empty frame
        private static readonly int[][] Path =
        {
            new[] { 27, 0, 0, 1, 1 },
            new[] { 28, 99, 284, 11, 11, -414, 0, 143, -5, 145, 156, -425, 152 },
            new[] { 29, 99, 284, 23, 11, -223, -4, 345, -6, 345, 155, -224, 153 },
            new[] { 30, 99, 284, 35, 11, -26, -5, 541, -5, 541, 154, -27, 154 },
            new[] { 31, 110, 284, 37, 11, -3, -5, 566, -5, 566, 154, -3, 154 },
            new[] { 32, 122, 284, 37, 11, 4, -5, 572, -5, 572, 154, 4, 155 },
            new[] { 33, 125, 287, 39, 12, -3, -3, 601, -2, 600, 168, -3, 168 },
            new[] { 34, 125, 292, 43, 13, 3, -1, 657, -1, 658, 184, 3, 184 },
            new[] { 35, 124, 299, 47, 14, -10, -2, 717, -2, 716, 204, -9, 204 },
            new[] { 36, 119, 308, 54, 16, 4, 7, 828, 7, 828, 239, 4, 239 },
            new[] { 37, 112, 321, 62, 18, 8, 0, 960, 0, 961, 271, 7, 271 },
            new[] { 38, 103, 337, 73, 22, -3, 6, 1119, 6, 1117, 323, -1, 323 },
            new[] { 39, 90, 358, 87, 25, 6, 0, 1338, 0, 1337, 374, 7, 374 },
            new[] { 40, 76, 381, 102, 30, 6, 3, 1578, 5, 1578, 448, 5, 449 },
            new[] { 41, 64, 402, 117, 35, 10, 8, 1806, 8, 1805, 520, 10, 520 },
            new[] { 42, 64, 402, 117, 35, 10, 8, 1806, 8, 1805, 520, 10, 520 },
            new[] { 43, 64, 402, 117, 35, 10, 8, 1806, 8, 1805, 520, 10, 520 },
            new[] { 44, 64, 402, 117, 35, 10, 8, 1806, 8, 1805, 520, 10, 520 },
            new[] { 45, 64, 402, 117, 35, 10, 8, 1806, 8, 1805, 520, 10, 520 },
            new[] { 46, 64, 402, 117, 35, 10, 8, 1806, 8, 1805, 520, 10, 520 },
            new[] { 47, 64, 402, 117, 35, 10, 8, 1806, 8, 1805, 520, 10, 520 },
            new[] { 48, 64, 402, 117, 35, 10, 8, 1806, 8, 1805, 520, 10, 520 },
            new[] { 49, 64, 402, 117, 35, 10, 8, 1806, 8, 1805, 520, 10, 520 },
            new[] { 50, 64, 402, 117, 35, 10, 8, 1806, 8, 1805, 520, 10, 520 },
            new[] { 51, 64, 402, 117, 35, 10, 8, 1806, 8, 1805, 520, 10, 520 },
            new[] { 52, 64, 402, 117, 35, 10, 8, 1806, 8, 1805, 520, 10, 520 },
            new[] { 53, 64, 402, 117, 35, 10, 8, 1806, 8, 1805, 520, 10, 520 },
            new[] { 54, 64, 409, 117, 35, 10, 8, 1806, 8, 1805, 520, 10, 520 },
            new[] { 55, 64, 416, 117, 35, 10, 8, 1806, 8, 1805, 520, 10, 520 },
            new[] { 56, 64, 423, 117, 33, 10, 8, 1806, 8, 1805, 520, 10, 520 },
            new[] { 57, 64, 430, 117, 26, 10, 8, 1806, 8, 1805, 520, 11, 520 },
            new[] { 58, 64, 437, 117, 19, 10, 8, 1806, 8, 1805, 520, 11, 520 },
            new[] { 59, 64, 444, 117, 12, 9, 11, 1806, 11, 1801, 492, 15, 492 },
            new[] { 60, 64, 450, 117, 6, 10, 24, 1806, 24, 1785, 525, 30, 525 },
        };

        /// <summary>
        /// The SHA-256 of the files this class and the Linux installer's a2movelist.py compute (which uses
        /// floating point): either counts as A2G-Patch's file.
        /// </summary>
        internal static readonly string[] Hashes =
        {
            "01766e2858010f13e0aa74fce4c8285db936b9bbb8f314aac963674acd443383",
            "832ea122aac02ce4f8fad4936351cd8ed91ed9991b0e2233cf210b1526859920",
        };

        /// <summary>The file's contents.</summary>
        internal static byte[] Build()
        {
            List<byte> data = new List<byte>(BitConverter.GetBytes((ushort)Path.Length));
            foreach (int[] frame in Path)
            {
                foreach (int value in frame.Take(5))
                    data.AddRange(BitConverter.GetBytes((ushort)value));
                foreach (int offset in Frame(frame))
                    data.AddRange(BitConverter.GetBytes((ushort)offset));
            }
            return data.ToArray();
        }

        // The table of one frame.
        private static IEnumerable<int> Frame(int[] frame)
        {
            int width = frame[3], height = frame[4];
            if (frame.Length == 5)
                return new int[width * height];
            decimal[] h = Homography(frame.Skip(5).Select(c => c / 16m).ToArray());
            return Enumerable.Range(0, height).SelectMany(y => Enumerable.Range(0, width).Select(x =>
            {
                decimal den = h[6] * x + h[7] * y + 1;
                int u = Clamp((h[0] * x + h[1] * y + h[2]) / den, Width), v = Clamp((h[3] * x + h[4] * y + h[5]) / den, Height);
                return v * Width * 2 + u * 2;
            })).ToList();
        }

        // The nearest pixel, kept inside the picture.
        private static int Clamp(decimal value, int size) => (int)Math.Min(Math.Max(Math.Floor(value + 0.5m), 0), size - 1);

        // The coefficients mapping a frame pixel (x, y) to picture coordinates (u, v) for the corners (x0, y0, ..., x3, y3).
        private static decimal[] Homography(decimal[] corners)
        {
            decimal[,] source = { { -0.5m, -0.5m }, { Width - 0.5m, -0.5m }, { Width - 0.5m, Height - 0.5m }, { -0.5m, Height - 0.5m } };
            decimal[][] rows = new decimal[8][];
            for (int i = 0; i < 4; i++)
            {
                decimal u = source[i, 0], v = source[i, 1], x = corners[2 * i], y = corners[2 * i + 1];
                rows[2 * i] = new[] { x, y, 1, 0, 0, 0, -u * x, -u * y, u };
                rows[2 * i + 1] = new[] { 0, 0, 0, x, y, 1, -v * x, -v * y, v };
            }
            // Gaussian elimination with pivoting.
            for (int c = 0; c < 8; c++)
            {
                int pivot = Enumerable.Range(c, 8 - c).OrderByDescending(r => Math.Abs(rows[r][c])).First();
                decimal[] swap = rows[c];
                rows[c] = rows[pivot];
                rows[pivot] = swap;
                for (int r = 0; r < 8; r++)
                {
                    decimal f = r == c ? 0 : rows[r][c] / rows[c][c];
                    rows[r] = rows[r].Select((value, i) => value - f * rows[c][i]).ToArray();
                }
            }
            return Enumerable.Range(0, 8).Select(i => rows[i][8] / rows[i][i]).ToArray();
        }
    }

    /// <summary>
    /// Gives the dialogs in GOG's RESDLL.dll the window styles of the 1999 release. All dialogs of the game
    /// are templates in RESDLL.dll, and GOG's file lacks styles the 1999 file has (found by comparing the two
    /// files; the tables are own data):
    /// <list type="bullet">
    /// <item>the title bar (WS_CAPTION) on 342 dialogs and DS_MODALFRAME on 65 of them (GOG's file centers
    /// 75 dialogs with DS_CENTER instead; the game centers every dialog with a title bar itself); five also
    /// get back DS_SYSMODAL, DS_FIXEDSYS, DS_3DLOOK or DS_SETFOREGROUND, one loses WS_EX_TOPMOST,</item>
    /// <item>the sunken frame (WS_EX_CLIENTEDGE) around 68 controls, e.g. the choices that look like buttons
    /// ("Frei (Vorsaison voreingestellt)"); two have WS_BORDER in GOG's file instead,</item>
    /// <item>WS_EX_TRANSPARENT on 16 radio buttons and group boxes of dialog 598,</item>
    /// <item>the dialog font's character set (DEFAULT_CHARSET) and name ("MS Sans Serif"), control IDs -1 as
    /// 32-bit values, and three titles with double instead of single quotes.</item>
    /// </list>
    /// Not changed are texts and pictures the 2007 release changed on purpose. The file keeps its size;
    /// input and result are checked against their SHA-256. The original is kept as
    /// RESDLL.dll.vor-a2g-patch and taking the patch back restores it.
    /// </summary>
    internal sealed class DialogStyles : FilePatch
    {
        internal const string Dll = "RESDLL.dll";
        internal const string Backup = Dll + GameInstall.BackupSuffix;
        // The SHA-256 of GOG's file (1.0 fix2) and of the result; tests replace them.
        internal static string Gog = "e47e0802188c00b51b227675c172b4fb05bf92af9b3feba95aba3f293c135aae";
        internal static string Result = "31b058abfc46c3bca421e85952ba57a517292ca7ae2ff6be009dc563d1015adc";

        private const int De = 1031, En = 1033, Gb = 2057;
        private const uint WsCaption = 0x00C00000, WsBorder = 0x00800000, DsSetFont = 0x40;
        private const uint WsExTransparent = 0x20, WsExClientEdge = 0x200;
        private const string FaceGog = "MS SANS SERIF", Face = "MS Sans Serif";

        private static int Key(int language, int dialog) => language << 16 | dialog;
        private static IEnumerable<int> Keys(int language, params int[] dialogs) => dialogs.Select(d => Key(language, d));

        // Dialogs without a title bar in the 1999 file as well.
        private static readonly HashSet<int> Captionless = new HashSet<int>(Keys(De,
            108, 129, 181, 183, 188, 189, 196, 203, 221, 228, 229, 236, 263, 266, 276, 279, 291, 304, 305, 306, 320, 321, 327,
            382, 394, 407, 475, 476, 503, 515, 519, 520, 536, 591, 594, 597, 601, 602, 605, 624, 625, 630, 631, 633)
            .Concat(Keys(En, 205, 206, 226, 621)));

        // Dialogs whose style differs by more than WS_CAPTION: dialog -> style of 1999.
        private static readonly Dictionary<int, uint> Styles = new[]
        {
            Styled(0x90C800C0, Keys(De, 116, 119, 200, 202, 209, 210, 224, 265, 270, 271, 274, 275, 289, 290, 302, 329, 331, 344, 346, 379,
                388, 396, 405, 406, 428, 445, 473, 478, 508, 509, 513, 517, 524, 531, 532, 559, 567, 570, 574, 575, 576, 592, 603, 606,
                608, 609, 613, 620, 626, 223, 492)),
            Styled(0x90CA00C0, Keys(De, 297, 314, 332, 378, 479, 482, 541, 102, 571)),
            Styled(0x90CA02C0, Keys(De, 319)),
            Styled(0x90C802C0, Keys(De, 337)),
            Styled(0x90C002C0, Keys(De, 505)),
            Styled(0x90C80AC0, Keys(De, 549)),
            Styled(0x44000240, Keys(De, 611)),
            Styled(0x80C800C4, Keys(De, 30721)),
            Styled(0x80C800C0, Keys(Gb, 231)),
        }.SelectMany(s => s).ToDictionary(s => s.Key, s => s.Value);

        private static IEnumerable<KeyValuePair<int, uint>> Styled(uint style, IEnumerable<int> dialogs) => dialogs.Select(d => new KeyValuePair<int, uint>(d, style));

        // Extended styles of 1999 (WS_EX_TOPMOST removed).
        private static readonly Dictionary<int, uint> ExStyles = new Dictionary<int, uint> { [Key(De, 613)] = 0x00040000 };

        // Controls (index in the dialog) that get WS_EX_CLIENTEDGE back.
        private static readonly Dictionary<int, int[]> ClientEdge = new Dictionary<int, int[]>
        {
            [Key(De, 116)] = new[] { 55, 56, 94 }, [Key(De, 193)] = new[] { 15, 16 }, [Key(De, 200)] = new[] { 45, 46 }, [Key(De, 234)] = new[] { 13 },
            [Key(De, 244)] = new[] { 4 }, [Key(De, 262)] = new[] { 0, 1, 3 }, [Key(De, 283)] = new[] { 16 }, [Key(De, 302)] = new[] { 2 },
            [Key(De, 322)] = new[] { 50 }, [Key(De, 369)] = new[] { 2, 3, 4 }, [Key(De, 371)] = new[] { 6, 7, 8 }, [Key(De, 383)] = new[] { 1, 2, 3 },
            [Key(De, 390)] = new[] { 2 }, [Key(De, 396)] = new[] { 44, 45 }, [Key(De, 403)] = new[] { 17, 18, 19 }, [Key(De, 404)] = new[] { 1, 2, 3 },
            [Key(De, 433)] = new[] { 4 }, [Key(De, 440)] = new[] { 8, 9 }, [Key(De, 466)] = new[] { 1 }, [Key(De, 470)] = new[] { 1, 2, 3 },
            [Key(De, 484)] = new[] { 1, 3 }, [Key(De, 487)] = new[] { 1, 2, 3 }, [Key(De, 490)] = new[] { 1, 2 }, [Key(De, 491)] = new[] { 1, 2, 3 },
            [Key(De, 510)] = new[] { 19 }, [Key(De, 516)] = new[] { 38, 39 }, [Key(De, 541)] = new[] { 2 }, [Key(De, 575)] = new[] { 34 },
            [Key(De, 576)] = new[] { 17 }, [Key(De, 581)] = new[] { 4, 5, 6 }, [Key(De, 583)] = new[] { 2, 3 }, [Key(De, 588)] = new[] { 25, 27 },
            [Key(De, 596)] = new[] { 1, 2 }, [Key(De, 635)] = new[] { 5 }, [Key(Gb, 231)] = new[] { 9, 10 },
        };

        // Controls with WS_BORDER in GOG's file instead, with WS_EX_TRANSPARENT in 1999, and with ID 0xFFFF in 1999 as well.
        private static readonly Dictionary<int, int[]> BorderInstead = new Dictionary<int, int[]> { [Key(De, 490)] = new[] { 1, 2 } };
        private static readonly Dictionary<int, int[]> Transparent = new Dictionary<int, int[]> { [Key(De, 598)] = new[] { 0, 1, 2, 3, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 } };
        private static readonly Dictionary<int, int[]> KeepId = new Dictionary<int, int[]> { [Key(De, 382)] = new[] { 5 } };
        // Titles with ' where 1999 has ".
        private static readonly HashSet<int> Quotes = new HashSet<int>(Keys(De, 237, 296, 593));

        /// <inheritdoc/>
        public override string Name => "Dialoge mit Titelleiste und Rahmen wie im Original von 1999";

        /// <inheritdoc/>
        public override FileState State(GameFiles files)
        {
            string hash = GameFiles.Sha256(files.Read(Dll));
            if (hash == Gog)
                return FileState.Off;
            if (hash != Result)
                return FileState.Unknown;
            return GameFiles.Sha256(files.Read(Backup)) == Gog ? FileState.On : FileState.Fixed;
        }

        /// <inheritdoc/>
        public override void Switch(GameFiles files, bool on)
        {
            bool isOn = GameFiles.Sha256(files.Read(Dll)) == Result;
            if (on == isOn)
                return;
            if (!on)
            {
                files.Write(Dll, files.Read(Backup));
                files.Write(Backup, null);
                return;
            }
            byte[] original = files.Read(Dll), result = Restore(original);
            if (GameFiles.Sha256(result) != Result)
                throw new InvalidOperationException($"{Dll}: Das Ergebnis ist nicht wie erwartet, die Dialoge bleiben unverändert.");
            files.Write(Backup, original);
            files.Write(Dll, result);
        }

        /// <summary>A copy of the DLL <paramref name="dll"/> with the 1999 styles in every dialog template.</summary>
        internal static byte[] Restore(byte[] dll)
        {
            byte[] result = (byte[])dll.Clone();
            foreach (int[] dialog in Dialogs(dll))
                FixDialog(result, dialog[2], dialog[0], dialog[1]);
            return result;
        }

        private static int U16(byte[] b, int at) => BitConverter.ToUInt16(b, at);
        private static uint U32(byte[] b, int at) => BitConverter.ToUInt32(b, at);
        private static void Put(byte[] b, int at, uint value) => BitConverter.GetBytes(value).CopyTo(b, at);

        /// <summary>(language, dialog, file offset) of each dialog template in <paramref name="dll"/>.</summary>
        internal static IEnumerable<int[]> Dialogs(byte[] dll)
        {
            int header = (int)U32(dll, 0x3C), optional = header + 24, sections = optional + U16(dll, header + 20);
            int[][] table = Enumerable.Range(0, U16(dll, header + 6))
                .Select(i => new[] { (int)U32(dll, sections + 40 * i + 8), (int)U32(dll, sections + 40 * i + 12), (int)U32(dll, sections + 40 * i + 16), (int)U32(dll, sections + 40 * i + 20) })
                .ToArray();   // virtual size, va, raw size, raw
            int Offset(int rva)
            {
                int[] s = table.First(t => rva >= t[1] && rva < t[1] + Math.Max(t[0], t[2]));
                return s[3] + rva - s[1];
            }
            int root = Offset((int)U32(dll, optional + 96 + 16));
            // The entries of a resource directory: (name or ID, offset of the subdirectory or data entry).
            IEnumerable<int[]> Entries(int at) =>
                Enumerable.Range(0, U16(dll, root + at + 12) + U16(dll, root + at + 14))
                    .Select(i => new[] { (int)U32(dll, root + at + 16 + 8 * i), (int)(U32(dll, root + at + 20 + 8 * i) & 0x7FFFFFFF) });
            return from type in Entries(0).Where(t => t[0] == 5)                          // RT_DIALOG
                   from dialog in Entries(type[1])
                   from language in Entries(dialog[1])
                   select new[] { language[0], dialog[0], Offset((int)U32(dll, root + language[1])) };
        }

        // The end of a name or ordinal field of a template.
        private static int StringEnd(byte[] b, int at)
        {
            if (U16(b, at) == 0xFFFF)
                return at + 4;
            while (U16(b, at) != 0)
                at += 2;
            return at + 2;
        }

        private static void FixDialog(byte[] b, int at, int language, int dialog)
        {
            int key = Key(language, dialog);
            bool extended = U16(b, at) == 1 && U16(b, at + 2) == 0xFFFF;
            int styleAt = extended ? at + 12 : at, exStyleAt = extended ? at + 8 : at + 4, countAt = extended ? at + 16 : at + 8;
            if (Styles.TryGetValue(key, out uint style))
                Put(b, styleAt, style);
            else if (!Captionless.Contains(key))
                Put(b, styleAt, U32(b, styleAt) | WsCaption);
            if (ExStyles.TryGetValue(key, out uint exStyle))
                Put(b, exStyleAt, exStyle);
            int count = U16(b, countAt);
            int p = StringEnd(b, StringEnd(b, countAt + 10));                               // menu, class
            int title = p;
            p = StringEnd(b, p);
            if (Quotes.Contains(key))
            {
                for (int c = title; c < p - 2; c += 2)
                    b[c] = b[c] == '\'' && b[c + 1] == 0 ? (byte)'"' : b[c];
            }
            if ((U32(b, styleAt) & DsSetFont) != 0)
            {
                if (extended)
                    b[p + 5] = 1;                                                           // DEFAULT_CHARSET
                p += extended ? 6 : 2;
                int end = StringEnd(b, p);
                if (Encoding.Unicode.GetString(b, p, end - 2 - p) == FaceGog)
                    Encoding.Unicode.GetBytes(Face).CopyTo(b, p);
                p = end;
            }
            for (int i = 0; i < count; i++)
            {
                p = (p + 3) & ~3;
                int controlStyle = extended ? p + 8 : p, controlExStyle = p + 4;
                if (extended && U32(b, p + 20) == 0xFFFF && !In(KeepId, key, i))
                    Put(b, p + 20, 0xFFFFFFFF);
                if (In(ClientEdge, key, i))
                    Put(b, controlExStyle, U32(b, controlExStyle) | WsExClientEdge);
                if (In(Transparent, key, i))
                    Put(b, controlExStyle, U32(b, controlExStyle) | WsExTransparent);
                if (In(BorderInstead, key, i))
                    Put(b, controlStyle, U32(b, controlStyle) & ~WsBorder);
                p = StringEnd(b, StringEnd(b, p + (extended ? 24 : 18)));                  // class, text
                p += 2 + U16(b, p);                                                         // creation data
            }
        }

        private static bool In(Dictionary<int, int[]> table, int key, int control) => table.TryGetValue(key, out int[] controls) && controls.Contains(control);
    }

    /// <summary>
    /// Takes the black line off the right and bottom edge of the animations (the overhead kick in the
    /// main menu and the other *.anm of ARCHIVES\ANIMDATA.CPR). The game plays them in a child window
    /// "DDOutput" as large as the picture (FUN_004093d0 in the original) and blits its offscreen surface
    /// onto the primary surface into the window's client rect (FUN_0040bc50). GOG's DirectDraw wrapper
    /// (ddraw.dll, "GOG.com DirectX 1-7 wrapper") shows the window's rect plus one column and one row of
    /// the primary surface, where nothing was drawn. The patch makes the window one pixel smaller each way
    /// and blits the whole picture there (the source rect as destination), so that column and row show the
    /// picture's last ones:
    /// <code>
    ///   window: add ecx,edx / ...      / add eax,edi / ...      / mov dword [esi+4C],0
    ///        -> add ecx,edx / dec ecx  / add eax,edi / dec eax  / and dword [esi+4C],0 / nop
    ///   blit:   mov eax,[esi+1C] / lea edx,[esp+28] / push edx / push eax / call [GetClientRect] / mov edx,[esi+480]
    ///        -> lea eax,[esp+18] / lea edx,[esp+28] / push eax / push edx / call [CopyRect]      / mov edx,[esi+1C] / nop x2
    /// </code>
    /// ([esi+1C] and [esi+480] both hold the window.) With any other DirectDraw the clipper keeps the blit
    /// in the window, which would cut off the picture's last column and row: the patch is offered only with
    /// GOG's wrapper in the folder (taken back without it as well). The 1999 release has the same code.
    /// </summary>
    internal sealed class AnimationEdge : FilePatch
    {
        internal const string Wrapper = "ddraw.dll";
        internal static readonly byte[] WrapperName = Encoding.Unicode.GetBytes("GOG.com DirectX 1-7 wrapper");

        internal static readonly byte[] WindowOriginal = "03ca8944241003c7894c241c83c4048bcec7464c0000000089442414".Hex();
        internal static readonly byte[] WindowPatched = "03ca498944241003c748894c241c83c4048bce83664c008944241490".Hex();
        // The call's import entry is left 0 (at 11 and 12).
        internal static readonly byte[] BlitOriginal = "8b461c8d5424285250ff15000000008b96800400008d4c24105152".Hex();
        internal static readonly byte[] BlitPatched = "8d4424188d5424285052ff15000000008b561c8d4c241051529090".Hex();
        private const int GetClientRectAt = 11, CopyRectAt = 12;

        /// <inheritdoc/>
        public override string Name => "Kein schwarzer Strich an den Animationen (z. B. im Hauptmenü)";

        /// <summary>The window's and the blit's place in <paramref name="image"/>, or null if they aren't both original or both patched.</summary>
        internal static Site[] Sites(byte[] image)
        {
            PeFile pe = PeFile.Read(image);
            if (pe == null || !pe.Imports.TryGetValue("GetClientRect", out uint getClientRect) || !pe.Imports.TryGetValue("CopyRect", out uint copyRect))
                return null;
            Site window = Place(image, WindowOriginal, WindowPatched);
            Site blit = Place(image, WithEntry(BlitOriginal, GetClientRectAt, getClientRect), WithEntry(BlitPatched, CopyRectAt, copyRect));
            return window == null || blit == null || window.IsPatched != blit.IsPatched ? null : new[] { window, blit };
        }

        /// <summary><paramref name="code"/> calling the import entry <paramref name="entry"/> (at <paramref name="at"/>).</summary>
        internal static byte[] WithEntry(byte[] code, int at, uint entry)
        {
            byte[] result = (byte[])code.Clone();
            BitConverter.GetBytes(entry).CopyTo(result, at);
            return result;
        }

        // The one place holding original or patched, or null.
        private static Site Place(byte[] image, byte[] original, byte[] patched)
        {
            List<long> found = FindAll(image, Exact(original)), changed = FindAll(image, Exact(patched));
            if (found.Count + changed.Count != 1)
                return null;
            bool isPatched = changed.Count == 1;
            return new Site { Offset = isPatched ? changed[0] : found[0], Original = original, Patched = patched, IsPatched = isPatched };
        }

        /// <inheritdoc/>
        public override FileState State(GameFiles files)
        {
            Site[] sites = Sites(files.Exe);
            if (sites == null)
                return FileState.Unknown;
            if (sites[0].IsPatched)
                return FileState.On;
            byte[] wrapper = files.Read(Wrapper);
            return wrapper != null && FindAll(wrapper, Exact(WrapperName)).Count > 0 ? FileState.Off : FileState.Unknown;
        }

        /// <inheritdoc/>
        public override void Switch(GameFiles files, bool on)
        {
            byte[] image = (byte[])files.Exe.Clone();
            foreach (Site site in Sites(image))
                (on ? site.Patched : site.Original).CopyTo(image, site.Offset);
            files.Exe = image;
        }
    }

    /// <summary>Helpers for byte strings.</summary>
    internal static class Bytes
    {
        /// <summary>A copy of up to <paramref name="length"/> bytes from <paramref name="at"/> (fewer at the end).</summary>
        internal static byte[] Slice(this byte[] bytes, long at, int length)
        {
            byte[] slice = new byte[Math.Max(0, Math.Min(length, bytes.Length - at))];
            Array.Copy(bytes, Math.Min(at, bytes.Length), slice, 0, slice.Length);
            return slice;
        }

        /// <summary>The bytes written as hex digits.</summary>
        internal static byte[] Hex(this string hex) =>
            Enumerable.Range(0, hex.Length / 2).Select(i => Convert.ToByte(hex.Substring(2 * i, 2), 16)).ToArray();
    }
}
