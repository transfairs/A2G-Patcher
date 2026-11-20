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
        private static readonly GoalLimits Goals = Patch.Get<GoalLimits>();
        private static readonly ArrowRepeat Arrow = Patch.Get<ArrowRepeat>();
        private static readonly VideoCrash Video = Patch.Get<VideoCrash>();

        private static byte[] Apply(byte[] image, Site[] sites)
        {
            byte[] result = (byte[])image.Clone();
            foreach (Site site in sites)
                Array.Copy(site.Patched, 0, result, site.Offset, site.Patched.Length);
            return result;
        }

        [Fact]
        public void All_HasEverySwitch_WithDistinctFlags()
        {
            Assert.Equal(new[]
            {
                "Spielfehler", "Patch 1.01", "Videos ohne Absturz", "Audiogerät", "Kontostand",
                "Verhandlungssperre", "Interviews", "Mehr Tore", "Ansprachen", "Trainer-Nachwuchs", "Fischvergiftung", "Manager-Ereignisse",
                "Kopierschutz", "Fensterrahmen anzeigen", "Animationen", "Mausklicks", "Netzwerkspiel",
            }, Patch.All.Select(p => p.Title));
            Assert.Equal(new[]
            {
                "Behebt: Geldanlagen, Ereignisse, Ereignis-Abstürze, Schiedsrichter-Groll, Trainerkompetenz, Notenschnitt, Meiste Einsätze, Kadergröße, Pokalauslosung, Aufstellung, Kalkulation, Große Festplatten, Pfeile gedrückt halten, Bildschirmauflösung, Langer Windows-Betrieb",
                "Trainingslager in der Winterpause, Zweitpositionen für neue Spieler, Eigenschaften neuer Torhüter, Schild der Pokalauslosung auf der 2007er-CD, Spielerlisten und Zähler wie in 1.01",
                "Nicht abspielbare Videos (WM, EM, Finale) überspringen statt abzustürzen",
                "Ton über das Standard-Audiogerät von Windows",
                "Vereinskonto stoppt bei 2 Mrd. DM, statt ins Minus zu kippen",
                "Sperre nach drei Verhandlungsterminen mit einem Verein baut sich jeden Monat ab",
                "Antworten in Interviews wirken sich aus (Fans, Vorstand, Moral, Strafen …)",
                "Torgrenze auf 15 Tore pro Mannschaft erhöhen",
                "Unpassende Ansprachen in der Mannschaftssitzung schwächen die Mannschaft",
                "Neue Trainer: Ex-Spieler ab 33 Jahren, sonst Kompetenz 3 bis 11 statt 2",
                "Fischvergiftung nach der Thunfischpizza: drei Wochen Ausfall wie in der Meldung statt einer",
                "Weitere Ereignisse rund um den Manager: Presse, Fans, Privatleben",
                "Keine Kopierschutz-Strafen (z. B. „Raubkopie“ als Vereinsname)",
                "Dialoge mit Titelleiste und Rahmen wie im Original von 1999",
                "Kein schwarzer Strich an den Animationen (z. B. im Hauptmenü)",
                "Keine Verzögerung nach Mausklicks mit manchen Grafikkarten",
                "Bis zu vier Rechner spielen ein Spiel gemeinsam, im lokalen Netz oder über das Internet",
            }, Patch.All.Select(p => p.Name));
            // The bugs' switch carries all their bits; the GOG-only patches (0) last, under their own heading.
            Assert.Equal(new uint[] { 0x6BC8BEA, 1024, 4, 16, 131072, 16384, 4096, 1, 8192, 65536, 4194304, 16777216, 0, 0, 0, 0, 0 }, Patch.All.Select(p => p.Flag));
            PatchGroup f = PatchGroup.Patches, b = PatchGroup.Balancing, o = PatchGroup.Gog, x = PatchGroup.Extras;
            Assert.Equal(new[] { f, f, f, f, f, b, b, b, b, b, b, b, o, o, o, o, x }, Patch.All.Select(p => p.Group));

            // The bugs, each with a bit of its own; together with the switches every bit once.
            Patch[] bugs = Patch.Get<GameBugs>().Members;
            Assert.Equal(new[] { "Geldanlagen", "Ereignisse", "Ereignis-Abstürze", "Schiedsrichter-Groll", "Trainerkompetenz", "Notenschnitt", "Notenschnitt", "Meiste Einsätze", "Kadergröße", "Pokalauslosung", "Aufstellung", "Kalkulation", "Große Festplatten", "Pfeile gedrückt halten", "Bildschirmauflösung", "Langer Windows-Betrieb" },
                bugs.Select(p => p.Title));
            Assert.Equal(new[]
            {
                "Wertzuwachs von Geldanlagen nicht mehr vom Kontostand abziehen",
                "Ereignisse wirken wie in ihrer Meldung (Geld, Form, Frische, Verletzungen, Klang)",
                "Kein Absturz bei Ereignissen, für die kein passender Spieler da ist",
                "Schiedsrichter mit Groll treffen in den Spielszenen den richtigen Verein",
                "Trainer und Manager in schwachen Ländern werden jährlich besser",
                "Einsätze ohne Note zählen nicht im Notenschnitt (Spielerdetails, Fans)",
                "Notenverlauf in der Statistik mit „Noten/6“ statt „Formpunkte/6“ beschriften",
                "Vereinsliste der meisten Einsätze nimmt nach 50 Spielern wieder neue auf", "Kader nicht über 30 Spieler wachsen lassen",
                "Vereinsnamen bei der Pokalauslosung richtig darstellen", "Knöpfe „A/B“ und „i“ in der Aufstellung an ihrem Platz",
                "Kein Hänger beim Öffnen der Kalkulation in der Vorsaison", "Keine falsche Warnung „Festplatte fast voll“ beim Speichern",
                "Pfeile zählen beim Gedrückthalten weiter", "Desktop-Auflösung auch nach einem Absturz zurückholen",
                "Kein Einfrieren nach über 24 Tagen Windows-Laufzeit",
            }, bugs.Select(p => p.Name));
            Assert.Equal(new uint[] { 32768, 262144, 2097152, 67108864, 1048576, 524288, 33554432, 8388608, 64, 256, 2048, 512, 32, 2, 128, 8 }, bugs.Select(p => p.Flag));
            Assert.All(bugs, p => Assert.Equal(PatchGroup.Patches, p.Group));
            uint[] bits = Patch.All.Where(p => !(p is GameBugs)).Concat(bugs).Select(p => p.Flag).Where(bit => bit != 0).ToArray();
            Assert.Equal(27, bits.Length);
            Assert.Equal(0x7FFFFFFu, bits.Aggregate(0u, (all, bit) => all | bit));
        }

        private static readonly Patch Cup = Patch.Get<CupDraw>();

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
        public void RewritePatches_HaveAsManyWildcards_InBothVersions()
        {
            // FindOnce fills them in order, so they may move (FishPoisoning's by two bytes).
            foreach (RewritePatch patch in Patch.Get<GameBugs>().Members.Concat(Patch.All).OfType<RewritePatch>())
            {
                Assert.Equal(patch.OriginalCode.Length, patch.PatchedCode.Length);
                Assert.Equal(patch.OriginalCode.Count(b => b == null), patch.PatchedCode.Count(b => b == null));
            }
        }

        private static readonly Patch Display = Patch.Get<DisplayModeRestore>();
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

        private static readonly Patch Squad = Patch.Get<SquadLimit>();
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

        private static readonly Patch Calculation = Patch.Get<CalculationInit>();
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

        private static readonly Patch Negotiation = Patch.Get<NegotiationCounter>();
        // The original exe from 0x642DCF (GOG's from 0x647DFF, the 2007 CD's in memory from 0x647BFF): the
        // monthly club update loads, halves and stores the bytes at +107D..+1080.
        internal static readonly byte[] NegotiationCode = (
            "8A8D7D100000" + "8A857E100000" + "8A9D7F100000" + "8A9580100000" + "D0E9D0E8D0EBD0EA" + "55"
            + "888D7D100000" + "88857E100000" + "889D7F100000" + "889580100000" + "E8432EF4FF").Hex();

        [Fact]
        public void NegotiationCounter_HalvesTheCountersPerManager_InTheSameBytes()
        {
            Site site = Negotiation.Find(GameCode.Exe(NegotiationCode)).Single();

            Assert.Equal(300, site.Offset);
            Assert.Equal(57, site.Original.Length);
            Assert.Equal(NegotiationCode.Take(57), site.Original);
            // Only the eight displacements move by 4: +1081..+1084.
            Assert.Equal(new[] { 2, 8, 14, 20, 35, 41, 47, 53 }, Enumerable.Range(0, 57).Where(i => site.Original[i] != site.Patched[i]));
            Assert.Equal(new byte[] { 0x81, 0x82, 0x83, 0x84 }, new[] { 2, 8, 14, 20 }.Select(i => site.Patched[i]));
            Assert.Equal(new byte[] { 0x81, 0x82, 0x83, 0x84 }, new[] { 35, 41, 47, 53 }.Select(i => site.Patched[i]));
            Assert.True(Negotiation.Find(Apply(GameCode.Exe(NegotiationCode), new[] { site })).Single().IsPatched);
            Assert.Null(Negotiation.Find(GameCode.Exe(NegotiationCode, NegotiationCode)));
        }

        private static readonly Patch Investment = Patch.Get<InvestmentBalance>();
        // The original exe from 0x571060 (GOG's from 0x573310): the bank balance, the ledger minus the
        // investments' values (call 0x58BF30) plus the loans (call 0x58BF10) plus the bookings (call 0x570F80).
        internal static readonly byte[] InvestmentCode = (
            "568B7424085756E8C4AE01008B3E83C4042BF8" + "56E897AE010083C40403F8"
            + "56E8FCFEFFFF83C40403C75F5EC390909090").Hex();

        [Fact]
        public void InvestmentBalance_SubtractsTheDeposits_InTheSameBytes()
        {
            byte[] image = GameCode.Exe(InvestmentCode);
            Site site = Investment.Find(image).Single();

            Assert.False(site.IsPatched);
            Assert.Equal(300, site.Offset);
            Assert.Equal(InvestmentCode, site.Original);
            // As disassembled at 0x571060: loans (0x58BF10) and bookings (0x570F80) keep their targets.
            Assert.Equal((
                "568B74240856E8A5AE0100" + "0306" + "83EE80" + "6A0659"
                + "F64633017403" + "2B4628" + "83C610" + "E2F2"
                + "5E5056E8F7FEFFFF" + "595903C15EC390").Hex(), site.Patched);

            Site again = Investment.Find(Apply(image, new[] { site })).Single();
            Assert.True(again.IsPatched);
            Assert.Equal(site.Original, again.Original);
            Assert.Equal(site.Patched, again.Patched);
        }

        [Fact]
        public void InvestmentBalance_InUnknownCode_FindsNothing()
        {
            Assert.Null(Investment.Find(GameCode.Exe(InvestmentCode, InvestmentCode)));
            // "values" not 0x20 bytes behind "loans": the original call couldn't be restored.
            byte[] other = (byte[])InvestmentCode.Clone();
            other[8]++;
            Assert.Null(Investment.Find(GameCode.Exe(other)));
            Assert.Null(Investment.Find(new byte[0]));
        }

        private static readonly Patch Coach = Patch.Get<CoachSupply>();
        // The original exe from 0x5A1349: the age check (mov dl,[esi+1E] .. cmp dl,23 / jbe) up to the
        // call of the random function (0x645ED0) at 0x5A139D.
        internal static readonly byte[] CoachAgeCode = (
            "8A561E6603868000000080FA230F869E010000668B15606682006683FA017C156683FA0A7D0F0FBFD2663B0455C8296C00"
            + "7E0DEB06663D2C017E05B90100000085C90F8469010000807E20010F85BC0000006A03E82E4B0A00").Hex();
        // From 0x5A1500: the next trainer of the group from the waiting list (0x826BDE), or the place back with 1 and 2.
        internal static readonly byte[] CoachListCode = (
            "0FBF44240853660FB68818196C000FBFC933C08D0C89C1E1020FBFD003D166833CD5DE6B820000752D40663D14007CE9"
            + "8B4424088A48058A50043ACAC640071E7E0AC6400502C64004015BC3C6400501C64004025BC30FBFC003C18B4C2408C1E003"
            + "8A5904668B90DE6B8200668911668B90E06B8200668951028A51053AD38A90E46B82007E118851050FBED28A92E0296C00"
            + "885104EB0F8851040FBED28A92E0296C008851058A90E26B82005B88510766C780DE6B82000000C3").Hex();
        // From 0x5A15C0: the same for goalkeeper trainers (0x826EFE), the place back with 35 and 2.
        internal static readonly byte[] KeeperListCode = (
            "0FBF442408660FB68818196C000FBFC933C08D0C89D1E10FBFD003D166833CD5FE6E820000751440663D0A007CE98B442404"
            + "C6400723C6400402C30FBFC003C18B4C2404C1E003668B90FE6E8200668911668B90006F8200668951028A90046F8200"
            + "8851048A90026F820088510766C780FE6E82000000C3").Hex();

        // The three pieces as far apart as in the exe (0x5A1349, 0x5A1500, 0x5A15C0), from 300 on.
        internal static byte[] CoachImage(byte[] age = null, byte[] list = null, byte[] keeper = null)
        {
            List<byte> image = new List<byte>(Enumerable.Repeat((byte)0xCC, 300));
            void Put(byte[] piece, int at)
            {
                image.AddRange(Enumerable.Repeat((byte)0xCC, 300 + at - image.Count));
                image.AddRange(piece);
            }
            Put(age ?? CoachAgeCode, 0);
            Put(list ?? CoachListCode, 0x1B7);
            Put(keeper ?? KeeperListCode, 0x277);
            image.AddRange(Enumerable.Repeat((byte)0xCC, 16));
            return image.ToArray();
        }

        [Fact]
        public void CoachSupply_LowersTheAge_AndRollsTheCompetence_InTheSameBytes()
        {
            byte[] image = CoachImage();
            Site[] sites = Coach.Find(image);

            Assert.Equal(new long[] { 300 + 12, 300 + 0x1B7, 300 + 0x277 }, sites.Select(s => s.Offset));
            Assert.All(sites, s => Assert.False(s.IsPatched));
            Assert.Equal(new byte[] { 35 }, sites[0].Original);
            Assert.Equal(new byte[] { 32 }, sites[0].Patched);
            Assert.Equal(CoachListCode, sites[1].Original);
            Assert.Equal(KeeperListCode, sites[2].Original);
            // As disassembled at 0x5A1500 and 0x5A15C0: the same tables and lists, the call to 0x645ED0, nops to the end.
            Assert.Equal((
                "5356578B7C24100FBF4424140FB68018196C008D0480C1E0058DB0DE6B82006A145966833E00751F83C608E2F5"
                + "B3036A046A03E898490A0002D858FF0C2475F158B01EB401EB188B0689078A5E0666C70600000FB6C38AA0E0296C00"
                + "8A46048847078A4F043A4F057C08885F04886705EB06885F058867045F5E5BC3" + string.Concat(Enumerable.Repeat("90", 63))).Hex(),
                sites[1].Patched);
            Assert.Equal((
                "5356578B7C24100FBF4424140FB68018196C006BC0508DB0FE6E82006A0A5966833E00751D83C608E2F5"
                + "B3036A046A03E8DB480A0002D858FF0C2475F158B023EB0F8B0689078A5E0666C70600008A4604884707885F04"
                + "5F5E5BC3" + string.Concat(Enumerable.Repeat("90", 29))).Hex(),
                sites[2].Patched);

            Site[] again = Coach.Find(Apply(image, sites));
            Assert.All(again, s => Assert.True(s.IsPatched));
            Assert.Equal(sites.Select(s => s.Original), again.Select(s => s.Original));
            Assert.Equal(sites.Select(s => s.Patched), again.Select(s => s.Patched));

            // Half applied (the trainers' function only): each place is recognized on its own.
            Site[] half = Coach.Find(Apply(image, new[] { sites[1] }));
            Assert.Equal(new[] { false, true, false }, half.Select(s => s.IsPatched));
            Assert.Equal(sites.Select(s => s.Patched), half.Select(s => s.Patched));
        }

        [Fact]
        public void CoachSupply_InUnknownCode_FindsNothing()
        {
            byte[] Changed(byte[] code, int at, byte value)
            {
                byte[] copy = (byte[])code.Clone();
                copy[at] = value;
                return copy;
            }
            Assert.Null(Coach.Find(CoachImage(age: new byte[0])));
            Assert.Null(Coach.Find(CoachImage(age: Changed(CoachAgeCode, 12, 0x24))));    // another age
            Assert.Null(Coach.Find(CoachImage(list: new byte[0])));
            Assert.Null(Coach.Find(CoachImage(keeper: new byte[0])));
            Assert.Null(Coach.Find(CoachImage(age: Changed(CoachAgeCode, 0x53, 4))));      // push 4: not the random call
            Assert.Null(Coach.Find(CoachImage(keeper: Changed(KeeperListCode, 9, 0x19))));  // another group table
            Assert.Null(Coach.Find(new byte[0]));
        }

        private static readonly Patch Money = Patch.Get<MoneyLimit>();
        // The original exe from 0x570F80: the bookings of the finance block (calls 0x570E80, 0x570F60, 0x570E30).
        internal static readonly byte[] MoneyBookingsCode = (
            "568B7424085756E8F4FEFFFF8BBE6C0100008B96800100008B8EB40100002BFA8B96AC0100002BF98B8EDC0100002BFA"
            + "8B96F80100002BF98B8EF00100002BFA8B967C0100002BF98B8E7401000003FA8B963C02000003F98B8E3803000003FA"
            + "8B96280300002BF98B8E580200002BFA8B962C03000003F98B8E1802000003FA8B96100200002BF98B8E3C0300002BFA"
            + "8B960802000003F98B8E000200002BFA8B96180300002BF98B8E1C03000003FA8B965402000083C40403F92BFA562BF8"
            + "E81BFFFFFF83C4042BF856E8E0FDFFFF83C40403C75F5EC3").Hex();
        // From 0x5724F1: the call that ends the human clubs' statistics, then the booking to the function's end.
        internal static readonly byte[] MoneyBookingCode = ("E86AC8000083C408" + "8B865401000003C75F8986540100005E5B83C424C39090").Hex();
        // From 0x572660: the week's result, calling the same three functions.
        internal static readonly byte[] MoneyWeekCode = (
            "8B44240456578DB85401000057E80EE8FFFF8BB73C0200008B97280300008B8F580200002BF28B971803000003F18B8F1C030000"
            + "03F28B970802000003F18B8F000200002BF28B972C0300002BF18B8F1802000003F28B97100200002BF18B8F540200002BF2"
            + "8B97AC0100002BF18B8FB40100002BF28B97800100002BF18B8FF80100002BF28B97F00100002BF18B8FDC0100002BF2"
            + "8B977C0100002BF18B8F7401000003F28B976C01000083C40403F103F2572BF0E845E8FFFF83C4042BF057E80AE7FFFF"
            + "83C40403F08BC65F5EC3").Hex();
        private const int MoneyBookingsAt = 400, MoneyBookingAt = MoneyBookingsAt + 0x5724F1 - 0x570F80, MoneyWeekAt = MoneyBookingsAt + 0x572660 - 0x570F80;

        // The three pieces as far apart as in the exe (0x570F80, 0x5724F1, 0x572660), from 400 on.
        internal static byte[] MoneyImage(byte[] bookings = null, byte[] booking = null, byte[] week = null)
        {
            byte[] image = Enumerable.Repeat((byte)0xCC, MoneyWeekAt + 208 + 16).ToArray();
            (bookings ?? MoneyBookingsCode).CopyTo(image, MoneyBookingsAt);
            (booking ?? MoneyBookingCode).CopyTo(image, MoneyBookingAt);
            (week ?? MoneyWeekCode).CopyTo(image, MoneyWeekAt);
            return image;
        }

        private static byte[] Changed(byte[] code, int at, byte value)
        {
            byte[] copy = (byte[])code.Clone();
            copy[at] = value;
            return copy;
        }

        [Fact]
        public void MoneyLimit_ChecksTheBooking_InTheWeeksResultsBytes()
        {
            byte[] image = MoneyImage();
            Site[] sites = Money.Find(image);

            Assert.Equal(new long[] { MoneyWeekAt, MoneyBookingAt + 8 }, sites.Select(s => s.Offset));
            Assert.All(sites, s => Assert.False(s.IsPatched));
            Assert.Equal(MoneyWeekCode, sites[0].Original);
            Assert.Equal(MoneyBookingCode.Skip(8), sites[1].Original);
            // As disassembled at 0x572660: call 0x570F80, the check from 0x572680 with the limit 0x77359400.
            Assert.Equal((
                "8B4424040554010000" + "50E811E9FFFF59" + "038138030000" + "2B813C030000" + "C3909090"
                + "8B8E540100008BC103C7710A" + "8BC7C1F81F35FFFFFF7F" + "3D009435777E0F3BC17E0B"
                + "B8009435773BC17D028BC1" + "5F8986540100005E5B83C424C3" + string.Concat(Enumerable.Repeat("90", 208 - 0x59))).Hex(),
                sites[0].Patched);
            // At 0x5724F9: jmp 0x572680.
            Assert.Equal(("E982010000" + string.Concat(Enumerable.Repeat("90", 18))).Hex(), sites[1].Patched);
            Assert.Equal(2000000000, MoneyLimit.Limit);

            Site[] again = Money.Find(Apply(image, sites));
            Assert.All(again, s => Assert.True(s.IsPatched));
            Assert.Equal(sites.Select(s => s.Original), again.Select(s => s.Original));
            Assert.Equal(sites.Select(s => s.Patched), again.Select(s => s.Patched));

            // Half applied: each place is recognized on its own.
            Assert.Equal(new[] { true, false }, Money.Find(Apply(image, new[] { sites[0] })).Select(s => s.IsPatched));
            Assert.Equal(new[] { false, true }, Money.Find(Apply(image, new[] { sites[1] })).Select(s => s.IsPatched));
        }

        [Fact]
        public void MoneyLimit_InUnknownCode_FindsNothing()
        {
            Assert.Null(Money.Find(MoneyImage(week: new byte[0])));
            Assert.Null(Money.Find(MoneyImage(booking: new byte[0])));
            Assert.Null(Money.Find(MoneyImage().Concat(MoneyWeekCode).ToArray()));
            // The first call elsewhere: no bookings 0x100 behind it, or none at all before the image.
            Assert.Null(Money.Find(MoneyImage(week: Changed(MoneyWeekCode, 0x0E, 0x0F))));
            Assert.Null(Money.Find(MoneyImage(week: Changed(MoneyWeekCode, 0x11, 0x80))));
            // The week's result calling another second function than the bookings.
            Assert.Null(Money.Find(MoneyImage(week: Changed(MoneyWeekCode, 0xB7, 0x46))));
            Assert.Null(Money.Find(MoneyImage(bookings: Changed(MoneyBookingsCode, 0x0E, 0x6D))));

            Site[] sites = Money.Find(MoneyImage());
            byte[] patched = Apply(MoneyImage(), sites);
            // Patched: the bookings' first call no longer 0x100 before them, or the jump not to the check.
            Assert.Null(Money.Find(Changed(patched, MoneyBookingsAt + 8, 0xF5)));
            Assert.Null(Money.Find(Changed(patched, MoneyBookingAt + 9, 0x83)));
            Assert.Null(Money.Find(new byte[0]));
        }

        private static readonly Patch Events = Patch.Get<EventEffects>();
        // The original exe's places in the order of EventEffects.Places: the money table's entries for 690 and
        // 697 (0x6BD058, 0x6BD090), the money events 126, 143, 172 (from 0x556D15, 0x557043, 0x5578A3), the
        // sponsor from 0x558659, the freshness of 307, 308, 369, 414 and the sound of 56 (from 0x563CBC).
        internal static readonly byte[][] EventCode =
        {
            "B202000088FFFFFF".Hex(), "B902000050000000".Hex(),
            "6A7E6A6256E86122000083C414566A006A016A00E8F20FECFF83C410E9E70A0000".Hex(),
            "688F0000006A6256E8301F00008BAE8C03000083C41403EF6AF65389AE8C030000".Hex(),
            "68AC0000006A6256E8D0160000E9A1060000".Hex(),
            "6685C00F856EE6FFFF663905A01EC2005375156A036A006A006A00E8".Hex(),
            "68330100006A6055E8DB69000083C4146AFE56E8C06E000083C4086AFB56E8".Hex(),
            "68340100006A6055E88E69000083C4146AFB56E8".Hex(),
            "68710100006A6055E8535A000083C4146AF656E8".Hex(),
            "689E0100006A6055E87D51000083C4186AF657E8E2360C0083C4086AF656E8".Hex(),
            "6A026A386A6356E8".Hex(),
            // 38 from 0x5632D0, 392 from 0x560120, 732's table at 0x6C9534, 284 from 0x55933E.
            "83C40C6A076A3253E8".Hex(),
            "F64328040F84CA0000008D430C".Hex(),
            "3E001400524150544F52".Hex(),
            ("681A010000B9E8367800E8E30700008BF883C9FF33C08D542438F2AEF7D12BF98BC18BF78BFAC1E902F3A58BC883E103F3A4"
                + "8B3510BB780056E854DAF4FF8B0D84BB780083C4045051E844DAF4FF83C404B9E836780050681B010000E891070000"
                + "8D9424340200005052E893D30F0083C4108D84242C0200008D4C243850516A026A5B53").Hex(),
            // 339 from 0x55F6B0, 343 from 0x55F77D, the building delay from 0x55E5F3.
            "6A025668530100006A6055E8C098FFFF8B853003000083C41403C6898530030000".Hex(),
            "6A02505068570100006A6055E8C25F000083C4186A1455E8B7020A0083C40866B801005F5E5D5B81C460080000C3".Hex(),
            "6A0AE8D6780E008B3C85A0D36B0083C4046A02E8".Hex(),
            // The delay's message from 0x55E604.
            ("6A02E8C5780E0083C404B9E836780085C00FBFF6577432E810B5FFFF8D4E415051686C030000B9E8367800E8FCB4FFFF8D54242C5052E801"
                + "810F008A84338408000083C410FEC0EB30E8DEB4FFFF508D464150686D030000B9E8367800E8CAB4FFFF8D4C242C5051E8CF800F008A8433"
                + "8408000083C4100402").Hex(),
        };
        // GOG's sponsor (the 2007 CD's too): the question's outcome at 0xC24FE0.
        internal static readonly byte[] GogSponsorCode = "6685C00F856EE6FFFF663905E04FC2005375156A036A006A006A00E8".Hex();

        // The bytes of each place that the patch changes, at their index.
        private static Dictionary<int, byte> Changes(Site site) =>
            Enumerable.Range(0, site.Original.Length).Where(i => site.Original[i] != site.Patched[i]).ToDictionary(i => i, i => site.Patched[i]);

        [Fact]
        public void EventEffects_BookAndTakeWhatTheMessagesSay()
        {
            byte[] image = GameCode.Exe(EventCode);
            Site[] sites = Events.Find(image);

            Assert.Equal(EventCode.Length, sites.Length);
            Assert.All(sites, s => Assert.False(s.IsPatched));
            Assert.Equal(EventCode, sites.Select(s => s.Original));
            Assert.Equal(new Dictionary<int, byte> { [4] = 0x78, [5] = 0, [6] = 0, [7] = 0 }, Changes(sites[0]));            // +120: income
            Assert.Equal(new Dictionary<int, byte> { [4] = 0xB0, [5] = 0xFF, [6] = 0xFF, [7] = 0xFF }, Changes(sites[1]));   // -80: expense
            Assert.Equal("E923120000".Hex(), sites[2].Patched.Skip(28));                                                    // jmp 0x557F59: expenses
            Assert.Equal(new Dictionary<int, byte> { [15] = 0x90, [29] = 0x90 }, Changes(sites[3]));                       // [esi+390]: income
            Assert.Equal("E965FFFFFF".Hex(), sites[4].Patched.Skip(13));                                                    // jmp 0x55781A: income
            Assert.Equal(new Dictionary<int, byte> { [28] = 0x05 }, Changes(sites[6]));                                     // the second push: 5
            Assert.Equal(new Dictionary<int, byte> { [17] = 0x05 }, Changes(sites[7]));
            Assert.Equal(new Dictionary<int, byte> { [17] = 0x0A }, Changes(sites[8]));
            Assert.Equal(new Dictionary<int, byte> { [17] = 0x0A, [28] = 0x0A }, Changes(sites[9]));
            Assert.Equal(new Dictionary<int, byte> { [1] = 0x01 }, Changes(sites[10]));                                     // positive sound
            Assert.Equal(new Dictionary<int, byte> { [7] = 0x55 }, Changes(sites[11]));                                     // push ebp: the second player
            Assert.Equal("E9CB00000090".Hex(), sites[12].Patched.Skip(4).Take(6));                                          // jmp: text 392 alone
            Assert.Equal(new Dictionary<int, byte> { [0] = 0x44 }, Changes(sites[13]));                                     // kind 68, "Fischvergiftung"
            // The riot: both calls of the money format and sprintf moved, still reaching 0x4A6DD0 and 0x656740.
            Assert.Equal(("B9E83678008BA96C0400008B3510BB780056E87BDAF4FF8BF08D7C243C6A0859F3A5FF3584BB7800E865DAF4FF"
                + "8D4C2440890C2450B9E8367800FFB1740400008D94243C02000052E8B5D30F0083C4105E8D84242C02000050556A026A5B53").Hex()
                .Concat(Enumerable.Repeat((byte)0x90, 37)), sites[14].Patched);
            // add [ebp+330],esi / push 1 / push ebp / call 0x565A50 (every player's form) / add esp,1C
            Assert.Equal("6A025668530100006A6055E8C098FFFF01B5300300006A0155E88263000083C41C".Hex(), sites[15].Patched);
            // push -1 / push player under the message's arguments, call 0x565BC0 (his form) after it, jmp to the ending at 0x55F749
            Assert.Equal("6AFF506A02505068570100006A6055E8BF5F000083C418E8276400006A1455E8AF020A0083C410EBA39090909090".Hex(), sites[16].Patched);
            Assert.Equal(new Dictionary<int, byte> { [1] = 0x0C }, Changes(sites[17]));                                     // random(12): all reasons
            // One branch for one or two weeks, skipping a reason's "wegen "; the calls of random and sprintf still reach 0x645ED0 and 0x656740
            Assert.Equal(("6A02E8C5780E00590FBFF6F7D81BC04050B9E836780057E810B5FFFF813877656765750383C006508D4E41518B442408056C03000050B9E8"
                + "367800E8ECB4FFFF508D54243452E8F1800F0083C410588A8C33840800008D440801EB1DE836780090CF800F009090909090909090909090"
                + "909090909090909090").Hex(), sites[18].Patched);

            Site[] again = Events.Find(Apply(image, sites));
            Assert.All(again, s => Assert.True(s.IsPatched));
            Assert.Equal(sites.Select(s => s.Original), again.Select(s => s.Original));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void EventEffects_DoubleTheSponsorsAmount_KeepingTheOutcomesAddress(bool gog)
        {
            byte[][] code = (byte[][])EventCode.Clone();
            if (gog)
                code[5] = GogSponsorCode;
            Site site = Events.Find(GameCode.Exe(code))[5];
            byte[] outcome = code[5].Skip(12).Take(4).ToArray();

            // cmp byte [outcome],al / push ebx / jne fail / add edi,edi / push 3 / xor eax,eax / push eax x3 / call
            Assert.Equal(code[5].Take(9).Concat("3805".Hex()).Concat(outcome).Concat("53751603FF6A0333C0505050E8".Hex()), site.Patched);
            Site again = Events.Find(Apply(GameCode.Exe(code), Events.Find(GameCode.Exe(code))))[5];
            Assert.True(again.IsPatched);
            Assert.Equal(code[5], again.Original);
        }

        [Fact]
        public void EventEffects_WithAPlaceMissingOrTwice_FindNothing()
        {
            Assert.Null(Events.Find(GameCode.Exe(EventCode.Take(10).ToArray())));
            Assert.Null(Events.Find(GameCode.Exe(EventCode.Concat(new[] { EventCode[10] }).ToArray())));
            Assert.All(EventEffects.EventPlaces.Concat(((RewritesPatch)Crashes).Places), p => Assert.Equal(p[0].Length, p[1].Length));
            Assert.All(EventEffects.EventPlaces.Concat(((RewritesPatch)Crashes).Places), p => Assert.Equal(p[0].Count(b => b == null), p[1].Count(b => b == null)));
        }

        private static readonly Patch Crashes = Patch.Get<EventCrashes>();
        // The original exe from 0x55F1C3 (321), 0x55FB41 (366), 0x55B794 (244) and 0x5BBCB0 (the news).
        internal static readonly byte[][] CrashCode =
        {
            "6A0155E8155D00008BF083C408807E250A0F8E501B0000807E1F030F86461B000080BEC0000000020F8C391B0000".Hex(),
            "33F6B31E6A0155E8935300008A481E83C4083ACB760980B8C0000000017407466683FE057CDE".Hex(),
            "6A0157E8A494000083C4088BF06A025668F40000006A6157E81F9F000083C4146A5A6A4D56E822800D0083C40C".Hex(),
            "6A0FE819A2080083C40485C0751A6A0156E81A92FAFF0FBF0083C408506A3D56E88BF4FFFF83C40C5F5E5D5BC3".Hex(),
        };

        [Fact]
        public void EventCrashes_TestTheDrawnPlayer_BeforeReadingFromIt()
        {
            byte[] image = GameCode.Exe(CrashCode);
            Site[] sites = Crashes.Find(image);

            Assert.Equal(CrashCode, sites.Select(s => s.Original));
            // pop ecx x2 / xchg eax,esi / test esi,esi / je out / the three checks, jumping short to one jmp out
            Assert.Equal("6A0155E8155D000059599685F67415807E250A7E0F807E1F03760980BEC0000000027D0AE93E1B00009090909090".Hex(), sites[0].Patched);
            // pop ecx x2 / test eax,eax / je next / cmp [eax+1E],bl / ... / inc esi / cmp esi,5 / jl draw
            Assert.Equal("33F6B31E6A0155E893530000595985C0740E38581E760980B8C00000000174064683FE057CDE".Hex(), sites[1].Patched);
            // test esi,esi / je (return 0); the message's call three bytes later (0x9F1F -> 0x9F1C), one add esp at the end
            Assert.Equal("6A0157E8A494000059598BF085F674E26A025668F40000006A6157E81C9F00006A5A6A4D56E822800D0083C420".Hex(), sites[2].Patched);
            // pop ecx / ... / test eax,eax / je (ret) / push dword [eax]; the calls one and two bytes earlier
            Assert.Equal("6A0FE819A208005985C0751C6A0156E81C92FAFF595985C0740EFF306A3D56E88CF4FFFF83C40C905F5E5D5BC3".Hex(), sites[3].Patched);

            Site[] again = Crashes.Find(Apply(image, sites));
            Assert.All(again, s => Assert.True(s.IsPatched));
            Assert.Equal(CrashCode, again.Select(s => s.Original));
            Assert.Null(Crashes.Find(GameCode.Exe(CrashCode.Take(3).ToArray())));
        }

        [Fact]
        public void FindOnce_MovesCallDisplacements_ToKeepTheirTargets()
        {
            // push 1 / call +10 / nop / jmp +20, the call moved to the front: its displacement grows by 2.
            byte?[] original = "6A01E8????????90E9????????".Pattern(), patched = "E8????????6A0190E9????????".Pattern();
            byte[] image = GameCode.Exe("6A01E81000000090E920000000".Hex());
            Site site = RewritePatch.FindOnce(image, original, patched);

            Assert.Equal("E8120000006A0190E920000000".Hex(), site.Patched);
            Site again = RewritePatch.FindOnce(Apply(image, new[] { site }), original, patched);
            Assert.True(again.IsPatched);
            Assert.Equal("6A01E81000000090E920000000".Hex(), again.Original);

            // Not a displacement: behind another opcode, behind E8 only in the patched code, five in a row, or split.
            original = "B8????????90E8??????????90E8????90????".Pattern();
            patched = "E8????????90E8??????????90E8????90????".Pattern();
            site = RewritePatch.FindOnce(GameCode.Exe("B81122334490E8556677889990E8AABB90CCDD".Hex()), original, patched);
            Assert.Equal("E81122334490E8556677889990E8AABB90CCDD".Hex(), site.Patched);
        }

        private static readonly Patch Grades = Patch.Get<GradeAverage>();
        // The original exe from 0x4FCC40 (the player's details; the fans' check at 0x639FC7 has the same test).
        internal static readonly byte[] GradeCode = "8A01A8807410A81F740CC0E8022407".Hex();

        [Fact]
        public void GradeAverage_CountsTheMarkAlone_InBothAverages()
        {
            byte[] image = GameCode.Exe(GradeCode, GradeCode);
            Site[] sites = Grades.Find(image);

            Assert.Equal(new long[] { 307, 338 }, sites.Select(s => s.Offset));
            Assert.All(sites, s => Assert.Equal(new byte[] { 0x1F }, s.Original));
            Assert.All(sites, s => Assert.Equal(new byte[] { 0x1C }, s.Patched));
            Assert.All(Grades.Find(Apply(image, sites)), s => Assert.True(s.IsPatched));
            Assert.Null(Grades.Find(GameCode.Exe(GradeCode)));
        }

        private static readonly Patch ChartLabel = Patch.Get<GradeChartLabel>();
        // The original exe from 0x461476: the grade chart's axis label to the nops behind FUN_0045ceb0.
        internal static readonly byte[] ChartCode = ("8D4C240C68A816000051E88B871B0083C4088D54241068A916000052E879871B0083C404"
            + "8D4424108BCC50E8BDBF2000518D5424188BCC52E8B0BF20008B8EFC050100E832A71A008D4C2410E8D7C02000"
            + "8D4C240CE8CEC020005F5E5B83C468C3909090909090909090").Hex();
        // From 0x619C10: FUN_00619c10, the text loader, whose first call is CString::operator=.
        internal static readonly byte[] ChartLoaderCode = "8B4C240881EC000200008D44240068000100005051E8604B050085C075188B8C240402000068C87C6B00E8A73A050081C400020000C3".Hex();
        private const int ChartAssignAt = 300, ChartLoaderAt = 400, ChartAt = 800;

        // The loader at 400 calling the assignment at 300, the chart at 800 with both labels' calls to loader.
        internal static byte[] ChartImage(int loader = ChartLoaderAt)
        {
            byte[] image = Enumerable.Repeat((byte)0xCC, 1000).ToArray();
            ChartLoaderCode.CopyTo(image, ChartLoaderAt);
            BitConverter.GetBytes(ChartAssignAt - (ChartLoaderAt + 0x2F)).CopyTo(image, ChartLoaderAt + 0x2B);
            ChartCode.CopyTo(image, ChartAt);
            BitConverter.GetBytes(loader - (ChartAt + 0x0F)).CopyTo(image, ChartAt + 0x0B);
            BitConverter.GetBytes(loader - (ChartAt + 0x21)).CopyTo(image, ChartAt + 0x1D);
            return image;
        }

        [Fact]
        public void GradeChartLabel_AssignsNotenFromBehindTheFunction()
        {
            byte[] image = ChartImage();
            Site site = ChartLabel.Find(image).Single();

            Assert.Equal(ChartAt, site.Offset);
            Assert.False(site.IsPatched);
            Assert.Equal(image.Slice(ChartAt, ChartCode.Length), site.Original);
            // call $+5 / add dword [esp],5C / lea ecx,[esp+10] / call 300 (operator=), the rest kept, "Noten/6" behind the ret
            Assert.Equal("E8000000008304245C8D4C2410E8".Hex().Concat(BitConverter.GetBytes(ChartAssignAt - (ChartAt + 0x12))), site.Patched.Take(0x12));
            Assert.Equal(site.Original.Skip(0x12).Take(0x4F), site.Patched.Skip(0x12).Take(0x4F));
            Assert.Equal("Noten/6\0", Encoding.ASCII.GetString(site.Patched, 0x61, 8));
            Assert.Equal(0x90, site.Patched[0x69]);
            Assert.Equal(ChartAt + 5 + 0x5C, ChartAt + 0x61);

            Site again = ChartLabel.Find(Apply(image, new[] { site })).Single();
            Assert.True(again.IsPatched);
            Assert.Equal(site.Original, again.Original);
            Assert.Equal(site.Patched, again.Patched);
        }

        [Fact]
        public void GradeChartLabel_InUnknownCode_FindsNothing()
        {
            Assert.Null(ChartLabel.Find(ChartImage().Concat(ChartCode).ToArray()));
            Assert.Null(ChartLabel.Find(new byte[0]));
            // The labels' calls not to the loader: none there, or before the image.
            Assert.Null(ChartLabel.Find(ChartImage(loader: 500)));
            Assert.Null(ChartLabel.Find(ChartImage(loader: -100)));
            // The first label's call elsewhere than the second's.
            Assert.Null(ChartLabel.Find(Changed(ChartImage(), ChartAt + 0x0B, 0x00)));
            // Patched, but the call not to the assignment.
            byte[] image = ChartImage();
            Assert.Null(ChartLabel.Find(Changed(Apply(image, ChartLabel.Find(image)), ChartAt + 0x0E, 0x00)));
        }

        private static readonly Patch Competence = Patch.Get<CompetenceBalance>();
        // The original exe at 0x5CC79A (GOG's and the 2007 CD's at 0x5CFECA), behind the call of random(2).
        internal static readonly byte[] CompetenceCode = "668B1783C404400FBFCA0FBFE803E983FD0F7E07B80F0000002BC20FBFC003D866FF044E".Hex();

        [Fact]
        public void CompetenceBalance_StoresTheRaise_InTheSameBytes()
        {
            Site site = Competence.Find(GameCode.Exe(CompetenceCode)).Single();

            Assert.Equal(300, site.Offset);
            Assert.Equal(CompetenceCode, site.Original);
            // movsx edx,[edi] / add esp,4 / inc eax / movsx eax,ax / add eax,edx / cmp eax,15 / jle / mov eax,15 /
            // mov [edi],ax / sub eax,edx / add ebx,eax / movsx ecx,[edi] / inc word [esi+ecx*2]
            Assert.Equal("0FBF1783C404400FBFC003C283F80F7E05B80F0000006689072BC203D80FBF0F66FF044E".Hex(), site.Patched);
            Assert.True(Competence.Find(Apply(GameCode.Exe(CompetenceCode), new[] { site })).Single().IsPatched);
            Assert.Null(Competence.Find(GameCode.Exe(CompetenceCode, CompetenceCode)));
        }

        private static readonly Patch Fish = Patch.Get<FishPoisoning>();
        // The original exe from 0x633A60: FUN_00633a60 and the nops behind it.
        internal static readonly byte[] FishCode = "0FBF4424088B4C24048A144534956C008851460FBF044534956C008D0440660FB61445C1926C006689514466A16C66820066894142C39090909090".Hex();

        [Fact]
        public void FishPoisoning_GivesTheFirstRefereeEvent_ThreeWeeks()
        {
            Site site = Fish.Find(GameCode.Exe(FishCode)).Single();

            Assert.Equal(FishCode, site.Original);
            // test eax,eax (event 0) / ... / movzx edx,byte [eax*2+days] / jne / mov dl,21 / mov [ecx+44],dx / ... / ret
            Assert.Equal("0FBF4424088B4C24048A144534956C0088514685C00FBF044534956C008D04400FB61445C1926C007502B2156689514466A16C66820066894142C3".Hex(), site.Patched);
            Assert.True(Fish.Find(Apply(GameCode.Exe(FishCode), new[] { site })).Single().IsPatched);
            Assert.Equal(PatchGroup.Balancing, Fish.Group);
        }

        private static readonly Patch Appearances = Patch.Get<AppearancesList>();
        // The original exe from 0x61EFFE (where the count starts) and 0x61F0B1 (the sort), 179 bytes apart.
        internal static readonly byte[] ListStartCode = "660FB6830401000083C408663D0200894424107E08C744241002000000".Hex();
        internal static readonly byte[] ListSortCode = ("8D442434BD01000000894424106683FD328D55FF8BC57D260FBFCD8D0C898D748C3E0FBFCA8D0C89668B4C8C3E663B0E7D028BD0"
            + "4083C614663D32007CE46685D27E330FBFC2B9050000008D7C24208D14808D4494348B5424108BF0F3A5B9050000008BF28BF8F3A5"
            + "B9050000008D7424208BFAF3A58B5424104583C2148D45FF89542410663D31007C82").Hex();
        // From 0x61ED0F: where a new player looks for a former one's entry, from place 50 down to place 11.
        internal static readonly byte[] ListKeepCode = "0FBFC88D0C8966837C8F0800740948663D0A007DEB".Hex();

        [Fact]
        public void AppearancesList_SetsTheFlagsFromTheSquad_BeforeEachCount()
        {
            byte[] image = GameCode.Exe(ListStartCode, ListSortCode, ListKeepCode);
            Site[] sites = Appearances.Find(image);

            Assert.Equal(new[] { ListStartCode, ListSortCode, ListKeepCode }, sites.Select(s => s.Original));
            Assert.Equal(new Dictionary<int, byte> { [17] = 0x14 }, Changes(sites[2]));                                     // down to place 21
            // ... / mov [esp+10],eax / call flags: from behind the call (+25) to the routine (sort + 2)
            Assert.Equal("E89C000000".Hex(), sites[0].Patched.Skip(20).Take(5));
            Assert.Equal(179 + 2 - 25, BitConverter.ToInt32(sites[0].Patched, 21));
            // jmp over the routine (88 bytes) to the bubble sort; nops to the original's 139 bytes
            Assert.Equal("EB58".Hex(), sites[1].Patched.Take(2));
            Assert.Equal(139, sites[1].Patched.Length);
            Assert.All(Appearances.Find(Apply(image, sites)), s => Assert.True(s.IsPatched));
            Assert.Null(Appearances.Find(GameCode.Exe(ListStartCode, ListSortCode)));
        }

        private static readonly Patch Manager = Patch.Get<ManagerEvents>();
        // The original exe from 0x55C820 (the private life's dispatcher built and drawn from) and FUN_00565860 from 0x565883.
        internal static readonly byte[][] ManagerPlaces =
        {
            ("6AFF6848D26B006A0EB9E84C7800E84D560000C390909090909090909090909053565733F6B9E84C7800E8815600008BD86685DB0F849500"
                + "0000668B15E04D780033C06685D27E260FBFCB81C1606D0000").Hex(),
            ("8D480A5150683CBF6A0052E8AD0E0F0083C4108D4424048D8C24F8010000505651E8970E0F008B94240C0400008B8C240004000083C40C8D"
                + "8424F8010000").Hex(),
        };
        private const uint ManagerCodeVa = TestExe.Base + TestExe.CodeRva;
        private const int CodeFileAt = (int)TestExe.CodeAt;
        private const int SwitchAt = 0x400, TableAt = 0x500, CasesAt = 0x480;
        private static readonly int[] RegionsAt = { 0x1000, 0x1800, 0x2000 };

        // A build for a test exe: the switch at code +400, its table at +500, the three functions at +1000, +1800 and +2000.
        private static ManagerBuild TestBuild(uint keyState) => new ManagerBuild
        {
            Switch = ManagerCodeVa + SwitchAt, Table = ManagerCodeVa + TableAt, Questions = 0x6BD610, KeyState = keyState,
            Cases = Enumerable.Repeat(ManagerCodeVa + CasesAt, ManagerCode.Cases.Length).ToArray(),
            Regions = RegionsAt.Select(at => ManagerCodeVa + (uint)at).ToArray(),
            Originals = new[] { 1216, 640, 400 }.Select((n, r) => Enumerable.Repeat((byte)(0x11 * (r + 1)), n).ToArray()).ToArray(),
            Symbols = Enumerable.Range(0, ManagerCode.Symbols.Length - 3).Select(i => 0x500000u + 0x10u * (uint)i).ToArray(),
        };

        // A test exe for its build: the two places, the switch with its table and the three functions, all original.
        private static byte[] ManagerExe(out ManagerBuild build, byte[] weights = null)
        {
            ManagerBuild made = null;
            byte[] image = TestExe.Build(entries =>
            {
                made = TestBuild(entries["GetAsyncKeyState"]);
                byte[] text = Enumerable.Repeat((byte)0xCC, 0x2200).ToArray();
                ManagerPlaces[0].CopyTo(text, 0x10);
                ManagerPlaces[1].CopyTo(text, 0x200);
                weights?.CopyTo(text, 0x300);
                "0FBF8424F40700004883F8260F8700000000FF2485".Hex().Concat(BitConverter.GetBytes(made.Table)).ToArray().CopyTo(text, SwitchAt);
                for (int k = 0; k < ManagerCode.Cases.Length; k++)
                    BitConverter.GetBytes(made.Cases[k]).CopyTo(text, TableAt + 4 * (ManagerCode.Cases[k] - 1));
                for (int r = 0; r < 3; r++)
                    made.Originals[r].CopyTo(text, RegionsAt[r]);
                return text;
            }, imports: TestExe.GameImports.Concat(new[] { new[] { "USER32.dll", "GetAsyncKeyState" } }).ToArray());
            build = made;
            return image;
        }

        private static byte[] Revert(byte[] image, Site[] sites)
        {
            byte[] result = (byte[])image.Clone();
            foreach (Site site in sites)
                site.Original.CopyTo(result, site.Offset);
            return result;
        }

        [Fact]
        public void ManagerEvents_DrawFromTheirOwnTable_NameTheManagerTwice_AndJumpToTheirCode()
        {
            byte[] image = ManagerExe(out ManagerBuild build);
            Site[] sites = ManagerEvents.Find(image, new[] { build });

            Assert.Equal(2 + 6 + 3, sites.Length);
            Assert.All(sites, s => Assert.False(s.IsPatched));
            // push -1 / mov eax,6BD248 / add eax,38 / push eax / push 27 (39 pairs) / mov ecx,dispatcher / call / ret
            Assert.Equal(ManagerPlaces, sites.Take(2).Select(s => s.Original));
            Assert.Equal("6AFFB848D26B0083C038506A27B9E84C7800E8".Hex(), sites[0].Patched.Take(19));
            // the name twice, the buffer kept in esi
            Assert.Equal("8D4424045050568DB4240402000056E8".Hex(), sites[1].Patched.Skip(19).Take(16));
            // the jump table's entries of cases 13, 21, 25, 34, 35 and 39 lead into the code
            byte[][] regions = ManagerEvents.Link(build, out uint[] entries);
            for (int k = 0; k < 6; k++)
            {
                Site entry = sites[2 + k];
                Assert.Equal(CodeFileAt + TableAt + 4 * (ManagerCode.Cases[k] - 1), entry.Offset);
                Assert.Equal(BitConverter.GetBytes(ManagerCodeVa + CasesAt), entry.Original);
                Assert.Equal(BitConverter.GetBytes(entries[k]), entry.Patched);
            }
            // the three functions, original and with the code
            for (int r = 0; r < 3; r++)
            {
                Assert.Equal(CodeFileAt + RegionsAt[r], sites[8 + r].Offset);
                Assert.Equal(build.Originals[r], sites[8 + r].Original);
                Assert.Equal(regions[r], sites[8 + r].Patched);
            }

            byte[] patched = Apply(image, sites);
            Site[] again = ManagerEvents.Find(patched, new[] { build });
            Assert.All(again, s => Assert.True(s.IsPatched));
            Assert.Equal(image, Revert(patched, again));
            // the build the game has isn't the test's
            Assert.Null(Manager.Find(image));
        }

        [Fact]
        public void ManagerEvents_Link_PutsTheQuestionsBehindTheCode_WhereTheDialogReachesThem()
        {
            ManagerBuild build = TestBuild(0);
            byte[][] regions = ManagerEvents.Link(build, out uint[] entries);

            Assert.Equal(new[] { 1216, 640, 400 }, regions.Select(r => r.Length));
            // MAIN from the start; the questions at the first entry of the question table behind it
            int main = ManagerCode.Blocks[0].Length, asks = ManagerCode.Blocks[1].Length;
            long slot = build.Regions[0] + main;
            while ((slot - build.Questions) % ManagerEvents.QuestionSize != 0)
                slot++;
            int at = (int)(slot - build.Regions[0]);
            Assert.Equal(new uint[] { 0x4E34, 503, 0x113, 0x114 }, Enumerable.Range(0, 4).Select(i => BitConverter.ToUInt32(regions[0], at + 4 * i)));
            Assert.Equal(514u, BitConverter.ToUInt32(regions[0], at + ManagerEvents.QuestionSize + 4));
            Assert.Equal(531u, BitConverter.ToUInt32(regions[0], at + 2 * ManagerEvents.QuestionSize + 4));
            Assert.All(regions[0].Skip(main).Take(at - main).Concat(regions[0].Skip(at + asks)), b => Assert.Equal(ManagerEvents.Fill, b));
            Assert.All(regions[1].Skip(ManagerCode.Blocks[2].Length), b => Assert.Equal(ManagerEvents.Fill, b));
            Assert.All(regions[2].Skip(ManagerCode.Blocks[3].Length), b => Assert.Equal(ManagerEvents.Fill, b));
            // the entries: block and offset
            uint[] starts = { build.Regions[0], (uint)slot, build.Regions[1], build.Regions[2] };
            for (int k = 0; k < 6; k++)
                Assert.Equal(starts[ManagerCode.Entries[k][0]] + (uint)ManagerCode.Entries[k][1], entries[k]);
        }

        [Fact]
        public void ManagerEvents_KnowTheThreeBuilds_AndLinkThemAsTheGeneratorDid()
        {
            Assert.Equal(new uint[] { 0x6775A8, 0xCBAD30, 0x67C5A8 }, ManagerCode.Builds.Select(b => b.KeyState));
            Assert.All(ManagerCode.Builds, b => Assert.Equal(new[] { 1216, 640, 400 }, b.Originals.Select(o => o.Length)));
            Assert.All(ManagerCode.Builds, b => Assert.Equal(ManagerCode.Symbols.Length - 3, b.Symbols.Length));
            // SHA-256 of the three functions and the entries as tools/make_manager.py links them (checked in an emulator)
            string[] expected =
            {
                "7E89777C13FA1F82465A21906F09A7D4A76D70D35F9683BE9445D9668BF4CD4E",
                "D1D4C88FBD9FFC02E454E4D484C74509FF8B9407119906686C362BAE8E35917F",
                "D1D4C88FBD9FFC02E454E4D484C74509FF8B9407119906686C362BAE8E35917F",
            };
            for (int i = 0; i < 3; i++)
            {
                byte[][] regions = ManagerEvents.Link(ManagerCode.Builds[i], out uint[] entries);
                byte[] all = regions.SelectMany(r => r).Concat(entries.SelectMany(BitConverter.GetBytes)).ToArray();
                using (System.Security.Cryptography.SHA256 sha = System.Security.Cryptography.SHA256.Create())
                    Assert.Equal(expected[i], BitConverter.ToString(sha.ComputeHash(all)).Replace("-", ""));
            }
        }

        [Fact]
        public void ManagerEvents_AreNotOffered_WhereAPlaceHoldsNeither()
        {
            byte[] image = ManagerExe(out ManagerBuild build);
            ManagerBuild[] builds = { build };
            int table = CodeFileAt + TableAt, region = CodeFileAt + RegionsAt[1];
            byte[] switchCode = image.Slice(CodeFileAt + SwitchAt, 25);

            Assert.NotNull(ManagerEvents.Find(image, builds));
            Assert.Null(ManagerEvents.Find(image.With(CodeFileAt + 0x10, 0xCCCCCCCC), builds));                 // no dispatcher builder
            Assert.Null(ManagerEvents.Find(image.With(CodeFileAt + SwitchAt, 0xCCCCCCCC), builds));             // no switch
            Assert.Null(ManagerEvents.Find(GameCode.Exe(ManagerPlaces[0], ManagerPlaces[1], switchCode), builds));   // no exe
            byte[] twice = (byte[])image.Clone();
            switchCode.CopyTo(twice, CodeFileAt + 0x600);
            Assert.Null(ManagerEvents.Find(twice, builds));                                                              // two switches
            Assert.Null(ManagerEvents.Find(image, new[] { TestBuild(build.KeyState + 4) }));                          // another import
            ManagerBuild moved = TestBuild(build.KeyState);
            moved.Switch++;
            Assert.Null(ManagerEvents.Find(image, new[] { moved }));                                                     // another switch
            moved = TestBuild(build.KeyState);
            moved.Table += 4;
            Assert.Null(ManagerEvents.Find(image, new[] { moved }));                                                     // another table
            Assert.Null(ManagerEvents.Find(image.With(table + 4 * 12, 0x401000), builds));                            // case 13 elsewhere
            Assert.Null(ManagerEvents.Find(image.With(region + 100, 0x90909090), builds));                            // KEYS changed
            moved = TestBuild(build.KeyState);
            moved.Regions[2] = 0x900000;
            Assert.Null(ManagerEvents.Find(image, new[] { moved }));                                                     // TABS outside the exe
        }

        [Fact]
        public void ManagerEvents_PutBackTheWeightVersion050Took()
        {
            byte[] image = ManagerExe(out ManagerBuild build, ManagerEvents.OldWeights);
            Site[] sites = ManagerEvents.Find(image, new[] { build });

            Assert.Equal(13, sites.Length);
            Site off = sites[11], on = sites[12];
            Assert.Equal(CodeFileAt + 0x300, off.Offset);
            Assert.Equal(off.Offset, on.Offset);
            Assert.True(off.IsPatched);
            Assert.False(on.IsPatched);
            Assert.All(new[] { off, on }, s => Assert.Equal(ManagerEvents.Weights, s.Original));
            Assert.All(new[] { off, on }, s => Assert.Equal(ManagerEvents.Weights, s.Patched));
            // case 34's weight back to 4 either way, then nothing more to do there
            Assert.Equal(4, ManagerEvents.Weights[8]);
            Assert.Equal(11, ManagerEvents.Find(Apply(image, sites), new[] { build }).Length);
            Assert.Equal(11, ManagerEvents.Find(Revert(image, sites), new[] { build }).Length);
        }

        private static readonly Patch Referee = Patch.Get<RefereeGrudge>();
        // The original exe from 0x404FA1: the referee's grudge compared with the defending club's number plus one.
        internal static readonly byte[] RefereeCode = "668B832E0200006685C0742733D28A97020100000FBFC0423BC27517".Hex();

        [Fact]
        public void RefereeGrudge_ComparesTheClubsNumber()
        {
            byte[] image = GameCode.Exe(RefereeCode);
            Site site = Referee.Find(image).Single();

            Assert.Equal(RefereeCode, site.Original);
            // je (skips club 0) and inc edx (number plus one) gone
            Assert.Equal(new Dictionary<int, byte> { [10] = 0x90, [11] = 0x90, [23] = 0x90 }, Changes(site));
            Assert.True(Referee.Find(Apply(image, new[] { site })).Single().IsPatched);
        }

        [Fact]
        public void Pattern_ReadsHexWithWildcards()
        {
            Assert.Equal(new byte?[] { 0x6A, null, 0xFF }, "6A??FF".Pattern());
        }

        private static readonly Patch Button = Patch.Get<PictureButtonInit>();
        // The original exe from 0x5BC3B6 (GOG's from 0x5BF0E6, there with CString 0x67C9E8 and vtable 0x6A0A78) to the ret.
        internal static readonly byte[] ButtonCode = (
            "33C0B9E8796700" + "89465C894E58894664894E6089466C894E68" + "8986A4000000" + "89463C" + "66894646" + "66894648"
            + "894650894654" + "898698000000" + "89869C000000" + "8986A0000000" + "8986B0000000" + "894674894678"
            + "66894644" + "6689464A" + "6689464C" + "8986B4000000" + "8986B8000000" + "8986BC000000" + "8986C0000000"
            + "8986C4000000" + "8986C8000000" + "898694000000" + "C70678BA6900" + "8BC6" + "5E" + "C3").Hex();

        [Fact]
        public void PictureButtonInit_ClearsTheBitmap_InTheSameBytes()
        {
            Site site = Button.Find(GameCode.Exe(ButtonCode)).Single();

            Assert.Equal(300, site.Offset);
            Assert.Equal(ButtonCode, site.Original);
            Assert.Equal(ButtonCode.Length, site.Patched.Length);
            // CString and vtable kept; +7C and edx = esi+80, then [edx+0..48] instead of [esi+80..C8]; ends as before.
            Assert.Equal(ButtonCode.Take(7), site.Patched.Take(7));
            Assert.Equal("894678 89467C 8D9680000000 8902 894204".Replace(" ", "").Hex(), site.Patched.Skip(57).Take(17));
            Assert.Equal(ButtonCode.Skip(ButtonCode.Length - 10), site.Patched.Skip(ButtonCode.Length - 10));
            Assert.True(Button.Find(Apply(GameCode.Exe(ButtonCode), new[] { site })).Single().IsPatched);
            Assert.Null(Button.Find(GameCode.Exe(ButtonCode, ButtonCode)));
        }

        private static readonly Patch Sleep = Patch.Get<SleepFreeze>();
        private static readonly Patch Sound = Patch.Get<DefaultSoundDevice>();
        private static readonly Patch Disk = Patch.Get<DiskSpaceWarning>();

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
