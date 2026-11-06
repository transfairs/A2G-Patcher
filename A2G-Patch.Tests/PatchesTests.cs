using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Xunit;

namespace A2GPatch.Tests
{
    /// <summary>Game code for the tests: the places of both patches, as the builds have them.</summary>
    internal static class GameCode
    {
        // The original build's limit (eax, short "and al, -4") and GOG's (ecx, long form), with the next instruction.
        internal static readonly byte[] OriginalLimit = { 0xC0, 0x48, 0x24, 0xFC, 0x83, 0xC0, 0x0A, 0x89, 0x44 };
        internal static readonly byte[] GogLimit = { 0xC1, 0x49, 0x83, 0xE1, 0xFC, 0x83, 0xC1, 0x0A, 0x89 };
        // The arrow repeat check, as in all three builds.
        internal static readonly byte[] ArrowCheck = { 0x8B, 0x86, 0xF4, 0x00, 0x00, 0x00, 0x85, 0xC0, 0x75, 0x29 };
        // GOG's and the 2007 CD's message variable (the original's is 0x00C1BC58).
        internal static readonly byte[] GogMessage = { 0x98, 0xED, 0xC1, 0x00 };

        internal static byte[] Goals(byte[] limit = null, byte home = GoalLimits.Jle, byte away = GoalLimits.Jle) =>
            GoalLimits.LimitPattern.Concat(limit ?? OriginalLimit).Concat(new byte[] { 0xCC, 0xCC })
                .Concat(GoalLimits.HomeBrakePattern).Concat(new byte[] { home, 0x09, 0xCC })
                .Concat(GoalLimits.AwayBrakePattern).Concat(new byte[] { away, 0x02, 0xCC }).ToArray();

        internal static byte[] Arrow(bool patched = false, byte[] message = null) =>
            ArrowCheck.Concat(patched ? ArrowRepeat.PatchedBranch(message ?? GogMessage) : ArrowRepeat.OriginalBranch(message ?? GogMessage)).Concat(new byte[] { 0xCC }).ToArray();

        // An exe image: filler, then the given code pieces, filler between them.
        internal static byte[] Exe(params byte[][] pieces)
        {
            List<byte> exe = new List<byte>(Enumerable.Repeat((byte)0xCC, 300));
            foreach (byte[] piece in pieces)
                exe.AddRange(piece.Concat(Enumerable.Repeat((byte)0xCC, 16)));
            return exe.ToArray();
        }

        internal static byte[] Both() => Exe(Goals(), Arrow());
    }

    /// <summary>
    /// Minimal 32-bit exes for the patches that read the headers: an import section at 0x1000 (file
    /// 0x200, 0x200 bytes) and a code section at 0x2000 (file 0x400), headers listing the imports first.
    /// In GOG's layout the import section is executable and listed last, as in GOG's run.exe. Sections
    /// are aligned to 0x1000 in memory and 0x200 in the file.
    /// </summary>
    internal static class TestExe
    {
        internal const uint Base = 0x400000, ImportRva = 0x1000, CodeRva = 0x2000, CodeAt = 0x400;
        // The PE header behind 0x7C, where GOG's run.exe keeps its CRC (see CopyProtection).
        internal const int PeAt = 0x80, OptionalAt = PeAt + 24, SectionsAt = OptionalAt + 0xE0;
        internal const int ImportDirectoryAt = OptionalAt + 104, CertificateDirectoryAt = OptionalAt + 128, SizeOfImageAt = OptionalAt + 56;
        internal static readonly string[][] GameImports =
        {
            new[] { "USER32.dll", "GetDC", "ReleaseDC" },
            new[] { "GDI32.dll", "#5", "GetBitmapBits", "GetDIBits" },   // #5: by ordinal
        };

        /// <summary>The exe with the code <paramref name="code"/> builds from the import entries' addresses.</summary>
        internal static byte[] Build(Func<Dictionary<string, uint>, byte[]> code, bool gog = false, string[][] imports = null,
            bool emptyCodeSectionLast = false, byte[] overlay = null, uint sizeOfImage = 0, byte[] afterSections = null)
        {
            Dictionary<string, uint> entries = new Dictionary<string, uint>();
            byte[] importSection = Imports(imports ?? GameImports, entries);
            byte[] text = code(entries);
            byte[] image = new byte[CodeAt + text.Length];
            image[0] = 0x4D;
            image[1] = 0x5A;
            Put(image, 0x3C, PeAt);
            Put(image, PeAt, 0x4550);
            Put(image, PeAt + 4, 0x14C);
            List<uint[]> sections = new List<uint[]>                     // va, raw size, raw, characteristics
            {
                new uint[] { ImportRva, 0x200, 0x200, gog ? 0xE0000020u : 0x40000040u },
                new uint[] { CodeRva, (uint)text.Length, CodeAt, 0x60000020 },
            };
            if (gog)
                sections.Reverse();
            if (emptyCodeSectionLast)
                sections.Add(new uint[] { 0x8000, 0, 0, 0x60000020 });
            image[PeAt + 6] = (byte)sections.Count;
            image[PeAt + 20] = 0xE0;                                     // size of the optional header
            Put(image, OptionalAt, 0x10B);
            Put(image, OptionalAt + 28, Base);
            Put(image, OptionalAt + 32, 0x1000);
            Put(image, OptionalAt + 36, 0x200);
            Put(image, SizeOfImageAt, sizeOfImage);
            Put(image, ImportDirectoryAt, ImportRva);
            afterSections?.CopyTo(image, SectionsAt + 40 * sections.Count);
            for (int i = 0; i < sections.Count; i++)
            {
                Put(image, SectionsAt + 40 * i + 8, sections[i][1]);
                Put(image, SectionsAt + 40 * i + 12, sections[i][0]);
                Put(image, SectionsAt + 40 * i + 16, sections[i][1]);
                Put(image, SectionsAt + 40 * i + 20, sections[i][2]);
                Put(image, SectionsAt + 40 * i + 36, sections[i][3]);
            }
            importSection.CopyTo(image, 0x200);
            text.CopyTo(image, CodeAt);
            return overlay == null ? image : image.Concat(overlay).ToArray();
        }

        // The import section: descriptors, then per DLL its lookup table, address table, name and the functions' names.
        private static byte[] Imports(string[][] dlls, Dictionary<string, uint> entries)
        {
            byte[] data = new byte[0x200];
            int next = (dlls.Length + 1) * 20;
            for (int d = 0; d < dlls.Length; d++)
            {
                string[] dll = dlls[d];
                int count = dll.Length - 1, lookup = next, table = lookup + 4 * (count + 1), name = table + 4 * (count + 1);
                Put(data, 20 * d, ImportRva + (uint)lookup);
                Put(data, 20 * d + 12, ImportRva + (uint)name);
                Put(data, 20 * d + 16, ImportRva + (uint)table);
                Encoding.ASCII.GetBytes(dll[0]).CopyTo(data, name);
                next = name + dll[0].Length + 1;
                for (int i = 0; i < count; i++)
                {
                    string function = dll[i + 1];
                    uint entry = function.StartsWith("#") ? 0x80000000 | uint.Parse(function.Substring(1)) : ImportRva + (uint)next;
                    if (!function.StartsWith("#"))
                    {
                        Encoding.ASCII.GetBytes(function).CopyTo(data, next + 2);
                        next += 2 + function.Length + 1;
                        entries[function] = Base + ImportRva + (uint)(table + 4 * i);
                    }
                    Put(data, lookup + 4 * i, entry);
                    Put(data, table + 4 * i, entry);
                }
            }
            return data;
        }

        internal static void Put(byte[] image, int at, uint value) => BitConverter.GetBytes(value).CopyTo(image, at);

        /// <summary>A copy of <paramref name="image"/> with <paramref name="value"/> at <paramref name="at"/>.</summary>
        internal static byte[] With(this byte[] image, int at, uint value)
        {
            byte[] copy = (byte[])image.Clone();
            Put(copy, at, value);
            return copy;
        }
    }

    /// <summary>Tests for the patches: finding their places in original and patched code, and refusing what they don't know.</summary>
    public class PatchesTests
    {
        private static readonly GoalLimits Goals = (GoalLimits)Patch.All[0];
        private static readonly ArrowRepeat Arrow = (ArrowRepeat)Patch.All[1];
        private static readonly VideoCrash Video = (VideoCrash)Patch.All[2];

        private static byte[] Apply(byte[] image, Site[] sites)
        {
            byte[] result = (byte[])image.Clone();
            foreach (Site site in sites)
                Array.Copy(site.Patched, 0, result, site.Offset, site.Patched.Length);
            return result;
        }

        [Fact]
        public void All_HasEveryPatch_WithDistinctFlags()
        {
            Assert.Equal(new[]
            {
                "Torgrenzen aufheben", "Pfeile zählen beim Gedrückthalten weiter",
                "Nicht abspielbare Videos (WM, EM, Finale) überspringen statt abzustürzen",
                "Kein Einfrieren nach über 24 Tagen Windows-Laufzeit", "Ton über das Standard-Audiogerät von Windows",
                "Keine falsche Warnung „Festplatte fast voll“ beim Speichern", "Kader nicht über 30 Spieler wachsen lassen",
                "Desktop-Auflösung auch nach einem Absturz zurückholen", "Vereinsnamen bei der Pokalauslosung richtig darstellen",
                "Kein Hänger beim Öffnen der Kalkulation in der Vorsaison", "Patch 1.01 (Änderungen an Programm und Daten)",
                "Keine Kopierschutz-Strafen (z. B. „Raubkopie“ als Vereinsname)",
                "Dialoge mit Titelleiste und Rahmen wie im Original von 1999",
            }, Patch.All.Select(p => p.Name));
            // The GOG-only patches (0) last, as the window lists them under their own heading.
            Assert.Equal(new ushort[] { 1, 2, 4, 8, 16, 32, 64, 128, 256, 512, 1024, 0, 0 }, Patch.All.Select(p => p.Flag));
        }

        private static readonly Patch Cup = Patch.All[8];

        // The original exe from 0x419ED1: ReleaseDC(NULL, dc), then GetBitmapBits(bitmap, 2000, this+C).
        internal static byte[] CupCall(Dictionary<string, uint> imports, uint? bits = null) =>
            new byte[] { 0x53, 0x6A, 0x00, 0xFF, 0x15 }.Concat(BitConverter.GetBytes(imports["ReleaseDC"]))
                .Concat("8B96108000008D4E0C51680020000052FF15".Hex()).Concat(BitConverter.GetBytes(bits ?? imports["GetBitmapBits"])).ToArray();

        // The code section: the call at 0x400 + 300, then zeros up to 603 bytes.
        internal static byte[] CupCode(Dictionary<string, uint> imports) => GameCode.Exe(CupCall(imports)).Concat(new byte[256]).ToArray();

        private const int CupCallAt = 0x400 + 300 + 25;

        [Theory]
        [InlineData(false, 0x5A0, 0x4021A0)]   // the code section's end
        [InlineData(true, 0x340, 0x401140)]    // GOG: the import section's end
        public void CupDraw_CallsAddedCode_AtTheEndOfTheLastExecutableSection(bool gog, long helper, long helperVa)
        {
            Dictionary<string, uint> imports = null;
            byte[] image = TestExe.Build(i => CupCode(imports = i), gog);
            Site[] sites = Cup.Find(image);

            Assert.Equal(new[] { helper, CupCallAt }, sites.Select(s => s.Offset));
            Assert.All(sites, s => Assert.False(s.IsPatched));
            Assert.Equal(new byte[177], sites[0].Original);
            Assert.Equal(CupDraw.HelperBytes(imports["GetDC"], imports["GetDIBits"], imports["ReleaseDC"]), sites[0].Patched);
            Assert.Equal(image.Skip(CupCallAt).Take(6), sites[1].Original);
            // call helper / nop, from the call's address 0x402000 + 300 + 25.
            Assert.Equal(new byte[] { 0xE8 }, sites[1].Patched.Take(1));
            Assert.Equal(0x90, sites[1].Patched[5]);
            Assert.Equal(helperVa, 0x402000 + 325 + 5 + BitConverter.ToInt32(sites[1].Patched, 1));

            Site[] again = Cup.Find(Apply(image, sites));
            Assert.All(again, s => Assert.True(s.IsPatched));
            Assert.Equal(sites.Select(s => s.Original), again.Select(s => s.Original));
            Assert.Equal(sites.Select(s => s.Patched), again.Select(s => s.Patched));
        }

        [Fact]
        public void CupDraw_Helper_CallsTheGivenImportEntries()
        {
            // The original exe's GetDC, GetDIBits and ReleaseDC entries.
            byte[] code = CupDraw.HelperBytes(0x6775F8, 0x6770F0, 0x6775E8);

            Assert.Equal(177, code.Length);
            Assert.Equal("6A00FF15F8756700".Hex(), code.Skip(62).Take(8));        // push 0 / call [GetDC]
            Assert.Equal("53FF15F0706700".Hex(), code.Skip(89).Take(7));          // push dc / call [GetDIBits]
            Assert.Equal("536A00FF15E8756700".Hex(), code.Skip(98).Take(9));      // ReleaseDC(NULL, dc)
            Assert.Equal("5B5DC20C00".Hex(), code.Skip(172));                     // pop ebx / pop ebp / ret C
        }

        public static IEnumerable<object[]> UnknownCupCode()
        {
            byte[] exe = TestExe.Build(CupCode);
            Site[] sites = Cup.Find(exe);
            byte[] withHelper = Apply(exe, sites.Take(1).ToArray());
            int textCharacteristics = TestExe.SectionsAt + 40 + 36;
            return new[]
            {
                TestExe.Build(i => GameCode.Exe(CupCall(i), CupCall(i)).Concat(new byte[256]).ToArray()), // ambiguous
                GameCode.Exe(CupCall(new Dictionary<string, uint> { ["ReleaseDC"] = 1, ["GetBitmapBits"] = 2 })), // no exe headers
                exe.With(TestExe.PeAt, 0),                                                 // no PE signature
                exe.With(0x3C, 0x7FFFFF00),                                                // headers beyond the end
                exe.With(TestExe.ImportDirectoryAt, 0x9000),                               // imports outside every section
                TestExe.Build(CupCode, imports: new[] { TestExe.GameImports[0], new[] { "GDI32.dll", "GetBitmapBits" } }), // no GetDIBits
                exe.With(textCharacteristics, 0x40000040),                                 // no executable section
                TestExe.Build(CupCode, emptyCodeSectionLast: true),                        // no room in the last one
                TestExe.Build(i => new byte[600], overlay: CupCall(Imports())),           // call outside the sections
                TestExe.Build(i => GameCode.Exe(CupCall(i, 0x12345678)).Concat(new byte[256]).ToArray()), // not GetBitmapBits
                exe.With(0x5A0, 1),                                                        // the room isn't free
                Apply(exe, sites.Skip(1).ToArray()),                                       // calls into zeros
                withHelper.With(CupCallAt, 0xE8).With(CupCallAt + 2, 0x90000000),          // calls elsewhere (E8 0 / nop)
            }.Select(image => new object[] { image });
        }

        // The import entries as TestExe lays them out.
        private static Dictionary<string, uint> Imports()
        {
            Dictionary<string, uint> imports = null;
            TestExe.Build(i => { imports = i; return new byte[0]; });
            return imports;
        }

        [Theory]
        [MemberData(nameof(UnknownCupCode))]
        public void CupDraw_InUnknownCode_FindsNothing(byte[] image)
        {
            Assert.Null(Cup.Find(image));
        }

        [Fact]
        public void RewritePatches_HaveWildcardsAtTheSamePlaces()
        {
            foreach (RewritePatch patch in Patch.All.OfType<RewritePatch>())
            {
                Assert.Equal(patch.OriginalCode.Length, patch.PatchedCode.Length);
                Assert.Equal(patch.OriginalCode.Select(b => b == null), patch.PatchedCode.Select(b => b == null));
            }
        }

        private static readonly Patch Display = Patch.All[7];
        // The original exe from 0x40FAC5 (rep stosd) to the call at 0x40FB08; the 2007 build has the same bytes.
        internal static readonly byte[] OriginalDisplay = (
            "F3ABBFE00100005352" + "66C786740100009C00" + "66899E76010000" + "C7867801000000005C00" +
            "C786BC01000080020000" + "89BEC0010000" + "C786B801000010000000" + "899EC8010000" + "FF15B4756700").Hex();
        // GOG's: jumps into its own code that sets the flags to 4, 32 bit.
        internal static readonly byte[] GogDisplay = (
            "F3ABE9355A24005052" + "66C786740100009C00" + "66899E76010000" + "C7867801000000005C00" +
            "C786BC01000080020000" + "89BEC0010000" + "C786B801000020000000" + "899EC8010000" + "FF153CADCB00").Hex();

        [Fact]
        public void DisplayModeRestore_SetsTheFlagsToFullscreen_NotInGog()
        {
            Site site = Display.Find(GameCode.Exe(OriginalDisplay)).Single();

            Assert.Equal(300, site.Offset);
            Assert.Equal("899EC8010000FF15B4756700".Hex(), site.Original.Skip(61));
            // mov byte [esp+4],4 / nop, the call untouched.
            Assert.Equal("C64424040490FF15B4756700".Hex(), site.Patched.Skip(61));
            Assert.Equal(6, site.Original.Zip(site.Patched, (a, b) => a != b).Count(d => d));
            Assert.True(Display.Find(Apply(GameCode.Exe(OriginalDisplay), new[] { site })).Single().IsPatched);

            Assert.Null(Display.Find(GameCode.Exe(GogDisplay)));
        }

        private static readonly Patch Squad = Patch.All[6];
        // The original exe at 0x5FCBB0 (GOG and 2007 have the same bytes at 0x601510).
        internal static readonly byte[] SquadCode = "8B44240450E8A6FFFFFF83C40433C9663D1E000F9DC18BC1C390909090909090".Hex();

        [Fact]
        public void SquadLimit_AlsoCountsTheActualSquad_KeepingTheCall()
        {
            Site site = Squad.Find(GameCode.Exe(SquadCode)).Single();

            Assert.Equal(300, site.Offset);
            Assert.Equal(SquadCode, site.Original);
            // As disassembled: push [esp+4] / call 0x5FCB60 / pop ecx / cmp ax,1E / jge / cmp word [ecx+E6],1E / setge al / movzx eax,al / ret
            Assert.Equal("8B44240450E8A6FFFFFF59663D1E007D086683B9E60000001E0F9DC00FB6C0C3".Hex(), site.Patched);
            Site again = Squad.Find(Apply(GameCode.Exe(SquadCode), new[] { site })).Single();
            Assert.True(again.IsPatched);
            Assert.Equal(site.Original, again.Original);
            Assert.Null(Squad.Find(GameCode.Exe(SquadCode, SquadCode)));
        }

        private static readonly Patch Calculation = Patch.All[9];
        // The original exe at 0x4A513F (GOG's at 0x4A733F): mov word [esi+10D48],0 / mov eax,esi, then pop esi.
        internal static readonly byte[] CalculationCode = "66C786480D010000008BC65E".Hex();

        [Fact]
        public void CalculationInit_ClearsBothSelections_InTheSameBytes()
        {
            Site site = Calculation.Find(GameCode.Exe(CalculationCode)).Single();

            Assert.Equal(300, site.Offset);
            Assert.Equal(CalculationCode.Take(11), site.Original);
            // mov eax,esi / and dword [esi+10D46],0 / nop x2
            Assert.Equal("8BC683A6460D0100009090".Hex(), site.Patched);
            Assert.True(Calculation.Find(Apply(GameCode.Exe(CalculationCode), new[] { site })).Single().IsPatched);
            Assert.Null(Calculation.Find(GameCode.Exe(CalculationCode, CalculationCode)));
        }

        private static readonly Patch Sleep = Patch.All[3];
        private static readonly Patch Sound = Patch.All[4];
        private static readonly Patch Disk = Patch.All[5];

        // The original exe at 0x59899B (and 0x598BFD with edx), and at 0x5EF42C; GOG's disk check.
        internal static readonly byte[] SleepCode = "2B857C0100003DB80B000089857C0100007D0EB9B80B0000".Hex();
        internal static readonly byte[] OriginalDisk = "0FAF4424183D0070170073348D4C240C68C10A0000".Hex();
        internal static readonly byte[] GogDisk = "0FAF4424383D0070170073388D4C241068C10A0000".Hex();
        // The original exe at 0x65349D; GOG's at 0x6582CD.
        internal static readonly byte[] OriginalSound = "566860E1CA006848E1CA00E8A338010085C07415E89A000000893560E1CA00".Hex();
        internal static readonly byte[] GogSound = "5668A012CB00688812CB00E8A338010085C07415E89A0000008935A012CB00".Hex();

        [Fact]
        public void SleepFreeze_MakesBothComparisonsUnsigned_AndFinishesAHalfPatchedExe()
        {
            byte[] image = GameCode.Exe(SleepCode, SleepCode);
            Site[] sites = Sleep.Find(image);

            Assert.Equal(new long[] { 317, 357 }, sites.Select(s => s.Offset));
            Assert.All(sites, s => Assert.Equal(new byte[] { 0x7D }, s.Original));
            Assert.All(sites, s => Assert.Equal(new byte[] { 0x73 }, s.Patched));
            byte[] half = Apply(image, sites.Take(1).ToArray());
            Site[] mixed = Sleep.Find(half);
            Assert.Equal(new[] { true, false }, mixed.Select(s => s.IsPatched));
            Assert.All(Sleep.Find(Apply(half, mixed.Skip(1).ToArray())), s => Assert.True(s.IsPatched));

            Assert.Null(Sleep.Find(GameCode.Exe(SleepCode)));
            Assert.Null(Sleep.Find(GameCode.Exe(SleepCode, SleepCode, SleepCode)));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void DiskSpaceWarning_JumpsOverTheWarning_InEachBuild(bool gog)
        {
            byte[] code = gog ? GogDisk : OriginalDisk;
            Site site = Disk.Find(GameCode.Exe(code)).Single();

            Assert.Equal(310, site.Offset);
            Assert.Equal(new byte[] { 0x73 }, site.Original);
            Assert.Equal(new byte[] { 0xEB }, site.Patched);
            Assert.True(Disk.Find(Apply(GameCode.Exe(code), new[] { site })).Single().IsPatched);
            Assert.Null(Disk.Find(GameCode.Exe(code, code)));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void DefaultSoundDevice_PassesNull_AndRebuildsTheGuidPush(bool gog)
        {
            byte[] code = gog ? GogSound : OriginalSound;
            Site site = Sound.Find(GameCode.Exe(code)).Single();

            Assert.Equal(306, site.Offset);
            Assert.Equal(code.Skip(6).Take(5), site.Original);
            Assert.Equal(new byte[] { 0x56, 0x90, 0x90, 0x90, 0x90 }, site.Patched);
            Site again = Sound.Find(Apply(GameCode.Exe(code), new[] { site })).Single();
            Assert.True(again.IsPatched);
            Assert.Equal(site.Original, again.Original);
        }

        [Fact]
        public void DefaultSoundDevice_InUnknownCode_FindsNothing()
        {
            byte[] otherGuid = (byte[])OriginalSound.Clone();
            otherGuid[7] = 0x40;
            byte[] otherPointer = (byte[])OriginalSound.Clone();
            otherPointer[27] = 0x64;

            Assert.Null(Sound.Find(GameCode.Exe(otherGuid)));
            Assert.Null(Sound.Find(GameCode.Exe(otherPointer)));
            Assert.Null(Sound.Find(GameCode.Exe(OriginalSound, OriginalSound)));
        }

        // The original exe's bytes from 0x418D83 (AVIFileGetStream's check) to 0x418E07, as on disk.
        internal static readonly byte[] OriginalVideo = (
            "85C074218B4D0051E802C723008D4C2410E80548250033C05F5E5D5B81C484000000C20C00" +
            "8B078D969884000068" + "8C0000005250E8D0C62300" +
            "8D866480000050C700280000008B8ED4840000894804" + "8B96D8840000895008" +
            "66C7400C010066C7400E0800895810895814895818" + "89581C895820895824" +
            "8B0750E885C6230089868C840000").Hex();

        [Fact]
        public void VideoCrash_ChecksTheFrameOpenResult_InTheSameBytes()
        {
            byte[] image = GameCode.Exe(OriginalVideo);
            Site site = Video.Find(image).Single();

            Assert.False(site.IsPatched);
            Assert.Equal(300 + 57, site.Offset);
            Assert.Equal(image.Skip(357).Take(75), site.Original);
            // As disassembled for the original exe at 0x418DBC: the call to AVIStreamGetFrameOpen's
            // thunk (0x655486) keeps its target, jmp goes back to 0x418D87 (release the file).
            Assert.Equal((
                "8D866480000050C70028000000" + "8B4F40894804" + "8B4F44894808" + "C7400C01000800" +
                "895810895814895818" + "89581C895820895824" +
                "FF37E891C62300" + "85C07507" + "8907E987FFFFFF" + "8947F890909090").Hex(), site.Patched);

            Site again = Video.Find(Apply(image, new[] { site })).Single();
            Assert.True(again.IsPatched);
            Assert.Equal(site.Original, again.Original);
            Assert.Equal(site.Patched, again.Patched);
        }

        [Fact]
        public void VideoCrash_InUnknownCode_FindsNothing()
        {
            byte[] patched = Apply(GameCode.Exe(OriginalVideo), Video.Find(GameCode.Exe(OriginalVideo)));
            Assert.Null(Video.Find(GameCode.Exe(OriginalVideo, OriginalVideo)));
            Assert.Null(Video.Find(patched.Concat(OriginalVideo).ToArray()));
            Assert.Null(Video.Find(GameCode.Exe(OriginalVideo.Take(100).ToArray())));
            Assert.Null(Video.Find(new byte[0]));
        }

        [Fact]
        public void GoalLimits_FindsTheThreePlaces_AndTheirPatchedForm()
        {
            byte[] image = GameCode.Exe(GameCode.Goals());
            Site[] sites = Goals.Find(image);

            Assert.Equal(3, sites.Length);
            Assert.All(sites, s => Assert.False(s.IsPatched));
            byte[] patched = Apply(image, sites);
            // mov eax, 15 / nop, then the next instruction untouched; both jle become jmp.
            Assert.Equal(new byte[] { 0xC0, 0xB8, 0x0F, 0x00, 0x00, 0x00, 0x90, 0x89, 0x44 }, patched.Skip(310).Take(9).ToArray());
            Assert.Equal(8, image.Zip(patched, (a, b) => a != b).Count(d => d));
            Assert.All(Goals.Find(patched), s => Assert.True(s.IsPatched));
        }

        [Fact]
        public void GoalLimits_WithTheLongEncoding_PadsWithTwoNops()
        {
            byte[] image = GameCode.Exe(GameCode.Goals(GameCode.GogLimit));

            byte[] patched = Apply(image, Goals.Find(image));

            Assert.Equal(new byte[] { 0xC1, 0xB9, 0x0F, 0x00, 0x00, 0x00, 0x90, 0x90, 0x89 }, patched.Skip(310).Take(9).ToArray());
        }

        public static IEnumerable<object[]> UnknownGoalCode => new[]
        {
            new object[] { GameCode.Exe(GameCode.Goals(), GameCode.Goals()) },                                                       // ambiguous
            new object[] { GameCode.Exe(GameCode.Goals(new byte[] { 0xC0, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90 })) },      // limit unknown
            new object[] { GameCode.Exe(GameCode.Goals(new byte[] { 0xC5, 0x48, 0x24, 0xFC, 0x83, 0xC0, 0x0A, 0x89, 0x44 })) },      // no setg register
            new object[] { GameCode.Exe(GameCode.Goals(home: 0x75)) },
            new object[] { GameCode.Exe(GameCode.Goals(away: 0x75)) },
            new object[] { GoalLimits.HomeBrakePattern.Concat(GoalLimits.AwayBrakePattern).Concat(GoalLimits.LimitPattern).ToArray() }, // cut off
            new object[] { GameCode.Exe(GoalLimits.LimitPattern.Concat(GameCode.OriginalLimit).ToArray()) },                          // brakes missing
            new object[] { GameCode.Goals().Take(GameCode.Goals().Length - 3).ToArray() },                                             // ends before the last jump
        };

        [Theory]
        [MemberData(nameof(UnknownGoalCode))]
        public void GoalLimits_InUnknownCode_FindsNothing(byte[] image)
        {
            Assert.Null(Goals.Find(image));
        }

        [Fact]
        public void LimitPatched_IsMovOfTheLimitPaddedToTheOriginalsLength()
        {
            foreach (int register in Enumerable.Range(0, 4))
            {
                foreach (byte[] original in GoalLimits.LimitOriginals(register))
                {
                    byte[] patched = GoalLimits.LimitPatched(register, original.Length);
                    Assert.Equal(original.Length, patched.Length);
                    Assert.Equal(0xB8 + register, patched[0]);
                    Assert.Equal(GoalLimits.MaxGoals, BitConverter.ToInt32(patched, 1));
                }
            }
            Assert.Equal(2, GoalLimits.LimitOriginals(0).Length);
            Assert.Single(GoalLimits.LimitOriginals(1));
        }

        // The bytes as the msvfw32.dll writes them (checked there in a test program).
        [Fact]
        public void ArrowRepeat_MarksTheStepPendingBeforePosting_KeepingTheMessageAddress()
        {
            byte[] image = GameCode.Exe(GameCode.Arrow());
            Site site = Arrow.Find(image).Single();

            Assert.False(site.IsPatched);
            Assert.Equal(310, site.Offset);
            Assert.Equal(41, site.Original.Length);
            Assert.Equal(image.Skip(310).Take(41), site.Original);
            Assert.Equal(new byte[]
            {
                0xC7, 0x86, 0xF4, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00,
                0xC7, 0x86, 0xF0, 0x00, 0x00, 0x00, 0x3C, 0x00, 0x00, 0x00,
                0xA1, 0x98, 0xED, 0xC1, 0x00,
                0x8B, 0x4E, 0x1C, 0x6A, 0x00, 0x6A, 0x00, 0x50, 0x51, 0xFF, 0xD3,
                0x90, 0x90, 0x90, 0xEB, 0x0A,
            }, site.Patched);

            Site again = Arrow.Find(Apply(image, new[] { site })).Single();
            Assert.True(again.IsPatched);
            Assert.Equal(site.Original, again.Original);
        }

        [Fact]
        public void ArrowRepeat_TakesEachBuildsMessageAddress()
        {
            byte[] original = { 0x58, 0xBC, 0xC1, 0x00 };

            Site site = Arrow.Find(GameCode.Exe(GameCode.Arrow(message: original))).Single();

            Assert.Equal(original, site.Original.Skip(1).Take(4));
            Assert.Equal(original, site.Patched.Skip(21).Take(4));
        }

        [Fact]
        public void ArrowRepeat_InUnknownCode_FindsNothing()
        {
            Assert.Null(Arrow.Find(GameCode.Exe(GameCode.Arrow(), GameCode.Arrow())));
            Assert.Null(Arrow.Find(GameCode.Exe(GameCode.Arrow(), GameCode.Arrow(patched: true))));
            Assert.Null(Arrow.Find(GameCode.Exe(GameCode.ArrowCheck)));
            Assert.Null(Arrow.Find(new byte[0]));
        }

        [Fact]
        public void FindAll_ReportsEveryStart_WithWildcards()
        {
            Assert.Equal(new long[] { 1, 4 }, Patch.FindAll(new byte[] { 1, 2, 3, 1, 2, 9, 1, 2 }, new byte?[] { 2, null }));
            Assert.Empty(Patch.FindAll(new byte[] { 1 }, Patch.Exact(1, 2)));
        }
    }
}
