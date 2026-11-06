using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace A2GPatch.Tests
{
    /// <summary>
    /// A stand-in for GOG's run.exe in TestExe's GOG layout: a code section large enough for the places of
    /// patch 1.01 (at their addresses in run.exe), the image ending where the 1.01 code is linked, leftovers
    /// behind the section headers and a signature behind the sections. The code holds the 11 CD checks, the
    /// five integrity self-checks and the goal limits' places (so A2G-Patch patches the exe on disk).
    /// </summary>
    internal static class GogExe
    {
        internal const int CodeLength = 0x1BC000;
        internal const int DiscTarget = 0x100, DiscAt = 0x200, SelfChecksAt = 0x1000, GoalsAt = 0x5000;
        internal static readonly byte[] Stale = Enumerable.Range(1, 48).Select(i => (byte)i).ToArray();
        internal static readonly byte[] Certificate = Enumerable.Repeat((byte)0x5C, 0x123).ToArray();
        internal static readonly byte[] SelfCheckStub = "81ECC0000000535556578BEC".Hex();

        /// <summary>Where the code byte for the loaded address <paramref name="va"/> lies in the code.</summary>
        internal static int CodeAt(uint va) => (int)(va - TestExe.Base - TestExe.CodeRva);

        // The CD checks' four forms: eax itself, or mov esi|ebx|edi,eax first.
        private static readonly string[] DiscForms = { "83C4049090909090", "8BF083C404909090909090", "8BD883C404909090909090", "8BF883C404909090909090" };

        /// <summary>The code: the given number of CD checks and self-checks, the 1.01 places if <paramref name="hooks"/>.</summary>
        internal static byte[] Code(int discChecks = 11, int selfChecks = 5, bool hooks = true, int length = CodeLength)
        {
            byte[] code = Enumerable.Repeat((byte)0xCC, length).ToArray();
            code[DiscTarget] = 0xC3;
            for (int i = 0; i < discChecks; i++)
                DiscCheck(code, DiscAt + 32 * i, DiscTarget, DiscForms[i % 4]);
            for (int i = 0; i < selfChecks; i++)
            {
                BitConverter.GetBytes(CopyProtection.GogValues[i % 5]).CopyTo(code, SelfChecksAt + 0x100 * i);
                BitConverter.GetBytes(0x40).CopyTo(code, SelfChecksAt + 0x100 * i + 8);
                SelfCheckStub.CopyTo(code, SelfChecksAt + 0x100 * i + 12);
            }
            GameCode.Goals().CopyTo(code, GoalsAt);
            if (hooks)
            {
                foreach (Patch101.Hook hook in Patch101.Hooks.Where(h => CodeAt(h.Va) < length))
                    hook.Original.CopyTo(code, CodeAt(hook.Va));
            }
            return code;
        }

        /// <summary>call target at <paramref name="at"/>, then <paramref name="tail"/>.</summary>
        internal static void DiscCheck(byte[] code, int at, int target, string tail)
        {
            code[at] = 0xE8;
            BitConverter.GetBytes(target - (at + 5)).CopyTo(code, at + 1);
            tail.Hex().CopyTo(code, at + 5);
        }

        /// <summary>
        /// The exe with <paramref name="code"/> (default: <see cref="Code"/>), the signature behind it unless
        /// left out. Like GOG's, it holds GOG's (wrong) integrity values.
        /// </summary>
        internal static byte[] Build(byte[] code = null, bool certificate = true, uint sizeOfImage = V101Code.Address - TestExe.Base, bool emptyCodeSectionLast = false)
        {
            byte[] image = TestExe.Build(i => code ?? Code(), gog: true, sizeOfImage: sizeOfImage, afterSections: Stale, emptyCodeSectionLast: emptyCodeSectionLast)
                .With(0x7C, CopyProtection.GogValues[5]);
            if (!certificate)
                return image;
            return image.With(TestExe.CertificateDirectoryAt, (uint)image.Length).With(TestExe.CertificateDirectoryAt + 4, (uint)Certificate.Length)
                .Concat(Certificate).ToArray();
        }
    }

    /// <summary>
    /// A stand-in for RESDLL.dll: three sections (.text at 0x1000, .data at 0x3000, .rsrc at 0x2000, in this
    /// order), the resource directory holding a string table entry and the given dialog templates.
    /// </summary>
    internal static class ResDll
    {
        /// <summary>A control of a template.</summary>
        internal sealed class Control
        {
            public uint Style, ExStyle, Id;
        }

        /// <summary>A dialog template (DLGTEMPLATEEX if <paramref name="extended"/>, else DLGTEMPLATE).</summary>
        internal static byte[] Template(bool extended, uint style, uint exStyle, string title, string face, byte charset, params Control[] controls)
        {
            List<byte> t = new List<byte>();
            void U16(int v) => t.AddRange(BitConverter.GetBytes((ushort)v));
            void U32(uint v) => t.AddRange(BitConverter.GetBytes(v));
            void Text(string s) => t.AddRange(Encoding.Unicode.GetBytes(s + "\0"));
            if (extended)
            {
                U16(1);
                U16(0xFFFF);
                U32(0);
                U32(exStyle);
                U32(style);
            }
            else
            {
                U32(style);
                U32(exStyle);
            }
            U16(controls.Length);
            U16(0); U16(0); U16(200); U16(100);
            U16(0xFFFF); U16(7);                                // menu by ordinal
            Text("");                                           // class: none
            Text(title);
            if ((style & 0x40) != 0)
            {
                U16(8);
                if (extended)
                {
                    U16(400);
                    t.Add(0);
                    t.Add(charset);
                }
                Text(face);
            }
            foreach (Control c in controls)
            {
                while (t.Count % 4 != 0)
                    t.Add(0);
                if (extended)
                {
                    U32(0);
                    U32(c.ExStyle);
                    U32(c.Style);
                }
                else
                {
                    U32(c.Style);
                    U32(c.ExStyle);
                }
                U16(1); U16(2); U16(30); U16(10);
                if (extended)
                    U32(c.Id);
                else
                    U16((int)c.Id);
                U16(0xFFFF); U16(0x80);                         // class: button
                Text("OK");
                U16(2); U16(0x1234);                            // creation data
            }
            return t.ToArray();
        }

        /// <summary>The DLL with the templates (language, dialog, template) in this order.</summary>
        internal static byte[] Build(params Tuple<int, int, byte[]>[] dialogs)
        {
            const int ResourceRva = 0x2000, ResourceAt = 0x600;
            List<IGrouping<int, Tuple<int, int, byte[]>>> ids = dialogs.GroupBy(d => d.Item2).ToList();
            int strings = 32, dialogDirectory = 48, next = dialogDirectory + 16 + 8 * ids.Count;
            List<byte> rsrc = new List<byte>(new byte[next]);
            void Put(int at, uint value) { byte[] v = BitConverter.GetBytes(value); for (int i = 0; i < 4; i++) rsrc[at + i] = v[i]; }
            void Directory(int at, int count) { while (rsrc.Count < at + 16 + 8 * count) rsrc.Add(0); rsrc[at + 14] = (byte)count; }
            Directory(0, 2);
            Put(16, 6);                                         // RT_STRING, an empty directory
            Put(20, 0x80000000 | (uint)strings);
            Put(24, 5);                                         // RT_DIALOG
            Put(28, 0x80000000 | (uint)dialogDirectory);
            Directory(strings, 0);
            Directory(dialogDirectory, ids.Count);
            List<Tuple<int, byte[]>> data = new List<Tuple<int, byte[]>>();   // data entry, template
            for (int i = 0; i < ids.Count; i++)
            {
                int languages = rsrc.Count;
                Put(dialogDirectory + 16 + 8 * i, (uint)ids[i].Key);
                Put(dialogDirectory + 20 + 8 * i, 0x80000000 | (uint)languages);
                Directory(languages, ids[i].Count());
                int j = 0;
                foreach (Tuple<int, int, byte[]> dialog in ids[i])
                {
                    int entry = rsrc.Count;
                    rsrc.AddRange(new byte[16]);
                    Put(languages + 16 + 8 * j, (uint)dialog.Item1);
                    Put(languages + 20 + 8 * j, (uint)entry);
                    data.Add(Tuple.Create(entry, dialog.Item3));
                    j++;
                }
            }
            foreach (Tuple<int, byte[]> d in data)
            {
                while (rsrc.Count % 4 != 0)
                    rsrc.Add(0);
                Put(d.Item1, (uint)(ResourceRva + rsrc.Count));
                Put(d.Item1 + 4, (uint)d.Item2.Length);
                rsrc.AddRange(d.Item2);
            }

            byte[] image = new byte[ResourceAt + rsrc.Count];
            image[0] = 0x4D;
            image[1] = 0x5A;
            TestExe.Put(image, 0x3C, 0x40);
            TestExe.Put(image, 0x40, 0x4550);
            image[0x46] = 3;
            image[0x54] = 0xE0;
            TestExe.Put(image, 0x58 + 112, ResourceRva);
            uint[][] sections = { new uint[] { 0x100, 0x1000, 0x200, 0x200 }, new uint[] { 0x100, 0x3000, 0x200, 0x400 }, new uint[] { (uint)rsrc.Count, ResourceRva, (uint)rsrc.Count, ResourceAt } };
            for (int i = 0; i < 3; i++)
            {
                for (int k = 0; k < 4; k++)
                    TestExe.Put(image, 0x138 + 40 * i + 8 + 4 * k, sections[i][k]);
            }
            rsrc.CopyTo(image, ResourceAt);
            return image;
        }
    }

    /// <summary>Tests for the GOG patches, the game files they work on and their parts.</summary>
    // Replaces the patches' hashes, which GameInstallTests and PatchFormTests read through Detect.
    [Collection("GameInstall statics")]
    public class GogPatchesTests
    {
        private static readonly Patch101 V101 = (Patch101)Patch.All[10];
        private static readonly CopyProtection Protection = (CopyProtection)Patch.All[11];
        private static readonly DialogStyles Dialogs = (DialogStyles)Patch.All[12];

        private static GameFiles Files(byte[] exe, string folder = null) => new GameFiles(folder ?? Path.GetTempPath(), exe);

        // ---- GameFiles, FilePatch ----

        [Fact]
        public void GameFiles_ReadsFromDiskUntilWritten_AndListsChangesInOrder()
        {
            using (GameFolder folder = new GameFolder())
            {
                File.WriteAllBytes(folder.File("a.dat"), new byte[] { 1 });
                GameFiles files = new GameFiles(folder.Path, new byte[] { 9 });

                Assert.Equal(new byte[] { 1 }, files.Read("a.dat"));
                Assert.Null(files.Read("b.dat"));
                files.Write("b.dat", new byte[] { 2 });
                files.Write("a.dat", null);
                files.Write("B.DAT", new byte[] { 3 });

                Assert.Null(files.Read("a.dat"));
                Assert.Equal(new byte[] { 3 }, files.Read("b.dat"));
                Assert.Equal(new[] { "b.dat", "a.dat" }, files.Changes.Select(c => c.Key));
                Assert.Equal(new byte[] { 9 }, files.Exe);
            }
        }

        [Fact]
        public void Sha256_IsLowercaseHex_AndNullForNoFile()
        {
            Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", GameFiles.Sha256(Encoding.ASCII.GetBytes("abc")));
            Assert.Null(GameFiles.Sha256(null));
        }

        [Fact]
        public void FilePatches_HaveNoCodePlaces_NorAFlagExceptPatch101()
        {
            foreach (FilePatch patch in Patch.All.OfType<FilePatch>())
            {
                Assert.Equal(patch is Patch101 ? 1024 : 0, patch.Flag);
                Assert.Empty(patch.Patterns);
                Assert.Null(patch.Find(GameCode.Both()));
            }
            Assert.Equal(0x400u, FilePatch.Align(0x201, 0x200));
            Assert.Equal(0x400u, FilePatch.Align(0x400, 0x200));
        }

        // What msvfw32.dll moves when it puts the code of patch 1.01 elsewhere: calls and jumps into the
        // game (relative, after E8 or E9) and addresses inside the code (absolute).
        [Fact]
        public void V101Code_Relocations_PointIntoTheGameOrIntoTheCode()
        {
            byte[] code = Convert.FromBase64String(V101Code.Code);
            Assert.Equal(31, V101Code.Relative.Length);
            Assert.All(V101Code.Relative, at =>
            {
                Assert.Contains(code[at - 1], new byte[] { 0xE8, 0xE9 });
                Assert.InRange(V101Code.Address + at + 4 + BitConverter.ToInt32(code, at), (long)TestExe.Base, V101Code.Address - 1L);
            });
            Assert.Equal(new ushort[] { 0x475, 0x4B1 }, V101Code.Absolute);
            Assert.All(V101Code.Absolute, at => Assert.InRange(BitConverter.ToUInt32(code, at), V101Code.Address, V101Code.Address + (uint)code.Length - 4));
        }

        [Fact]
        public void PeFile_ReadsSectionNamesAndHeaders_AndFindsAddressesInTheFile()
        {
            byte[] image = GogExe.Build();
            Encoding.ASCII.GetBytes("ABCDEFGH").CopyTo(image, TestExe.SectionsAt);
            PeFile pe = PeFile.Read(image);

            Assert.Equal(new[] { "ABCDEFGH", "" }, pe.Sections.Select(s => s.Name));
            Assert.Equal(TestExe.OptionalAt, pe.OptionalHeader);
            Assert.Equal(TestExe.SectionsAt, pe.SectionTable);
            Assert.Equal(0x1000u, pe.SectionAlignment);
            Assert.Equal(0x200u, pe.FileAlignment);
            Assert.Equal((uint)GogExe.Certificate.Length, pe.CertificateSize);
            Assert.Equal(image.Length - GogExe.Certificate.Length, (int)pe.CertificateAt);
            Assert.Equal(0x210, pe.FileOffset(0x401010));                   // the import section, listed second
            Assert.Equal(0x400 + 0x10, pe.FileOffset(0x402010));
            Assert.Equal(-1, pe.FileOffset(0x402000 + GogExe.CodeLength));
        }

        [Fact]
        public void Slice_StopsAtTheEnd()
        {
            byte[] bytes = { 1, 2, 3 };
            Assert.Equal(new byte[] { 2, 3 }, bytes.Slice(1, 5));
            Assert.Empty(bytes.Slice(7, 2));
        }

        // ---- CopyProtection ----

        [Fact]
        public void Crc32_IsZlibs()
        {
            Assert.Equal(0xCBF43926u, Crc32.Compute(Encoding.ASCII.GetBytes("x123456789"), 1, 9));
        }

        [Fact]
        public void CopyProtection_PassesTheCdChecks_AndStoresTheCrcs_AndTakesBothBack()
        {
            byte[] original = GogExe.Build();
            GameFiles files = Files(original);
            Assert.Equal(FileState.Off, Protection.State(files));

            Protection.Switch(files, true);
            byte[] image = files.Exe;
            Assert.Equal(FileState.On, Protection.State(files));
            // xor eax,eax / xor esi,esi / xor ebx,ebx / xor edi,edi, padded.
            int code = (int)TestExe.CodeAt;
            Assert.Equal("31C0909090".Hex(), image.Slice(code + GogExe.DiscAt + 8, 5));
            Assert.Equal("31F690909090".Hex(), image.Slice(code + GogExe.DiscAt + 32 + 10, 6));
            Assert.Equal("31DB90909090".Hex(), image.Slice(code + GogExe.DiscAt + 64 + 10, 6));
            Assert.Equal("31FF90909090".Hex(), image.Slice(code + GogExe.DiscAt + 96 + 10, 6));
            // Each self-check's CRC over its 0x40 bytes from the stub (stored inverted), the file's over everything from 0x80.
            for (int i = 0; i < 5; i++)
            {
                int descriptor = code + GogExe.SelfChecksAt + 0x100 * i;
                Assert.Equal(~Crc32.Compute(image, descriptor + 12, 0x40), BitConverter.ToUInt32(image, descriptor));
            }
            Assert.Equal(Crc32.Compute(image, 0x80, image.Length - 0x80), BitConverter.ToUInt32(image, 0x7C));
            // Again: nothing changes.
            Protection.Switch(files, true);
            Assert.Equal(image, files.Exe);

            Protection.Switch(files, false);
            Assert.Equal(original, files.Exe);
            Assert.Equal(FileState.Off, Protection.State(files));
        }

        [Fact]
        public void CopyProtection_WithAWrongCrc_IsOff()
        {
            GameFiles files = Files(GogExe.Build());
            Protection.Switch(files, true);
            byte[] image = files.Exe;
            image[TestExe.CodeAt + 0x2000] = 0;
            Assert.Equal(FileState.Off, Protection.State(Files(image)));
        }

        public static IEnumerable<object[]> NotGogsChecks => new[]
        {
            GameCode.Both(),                                    // no exe headers
            GogExe.Build(GogExe.Code(discChecks: 10)),
            GogExe.Build(GogExe.Code(selfChecks: 4)),
        }.Select(image => new object[] { image });

        [Theory]
        [MemberData(nameof(NotGogsChecks))]
        public void CopyProtection_WithoutGogsChecks_IsNotOffered(byte[] image)
        {
            Assert.Equal(FileState.Unknown, Protection.State(Files(image)));
        }

        [Fact]
        public void DiscSites_SkipWhatIsNoCheck_AndNeedOneRoutineWith11()
        {
            byte[] code = GogExe.Code(hooks: false, length: 0x6000);
            GogExe.DiscCheck(code, 0x400, 0x101, "8B0083C404");             // mov eax,[eax]: not one of the forms
            GogExe.DiscCheck(code, 0x420, 0x101, "83C504");                 // add ebp,4
            GogExe.DiscCheck(code, 0x440, 0x101, "83C4049090CC");           // too few NOPs
            GogExe.DiscCheck(code, 0x460, 0x101, "8BF083C504");             // mov esi,eax / add ebp,4
            GogExe.DiscCheck(code, 0x480, 0x101, "83C405");
            GogExe.DiscCheck(code, 0x4A0, 0x101, "8BF0CCC404");
            GogExe.DiscCheck(code, 0x4C0, 0x101, "8BF083CC04");
            GogExe.DiscCheck(code, 0x4E0, 0x101, "8BF083C405");
            GogExe.DiscCheck(code, 0x500, 0x101, "8BD083C404");             // mov edx,eax
            GogExe.DiscCheck(code, code.Length - 9, 0x101, "8BF083C4");     // too close to the end for the long form
            code[code.Length - 4] = 0xE8;                                   // too close to the end for any
            Site[] sites = CopyProtection.DiscSites(code);
            Assert.Equal(11, sites.Length);
            Assert.Equal(GogExe.DiscAt + 8, sites[0].Offset);   // behind call / add esp,4

            // A second routine with 11 checks makes it ambiguous; one with 12 doesn't count.
            byte[] twice = (byte[])code.Clone();
            for (int i = 0; i < 11; i++)
                GogExe.DiscCheck(twice, 0x3000 + 32 * i, 0x102, "83C4049090909090");
            Assert.Null(CopyProtection.DiscSites(twice));
            for (int i = 0; i < 12; i++)
                GogExe.DiscCheck(code, 0x3000 + 32 * i, 0x102, "83C4049090909090");
            Assert.Equal(11, CopyProtection.DiscSites(code).Length);
        }

        // ---- Patch 1.01: program ----

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Program101_AddsTheSection_AndTakesItBackToTheByte(bool certificate)
        {
            byte[] original = GogExe.Build(certificate: certificate);
            Assert.Equal(FileState.Off, Patch101.ProgramState(original));

            byte[] image = Patch101.AddProgram(original);
            PeFile pe = PeFile.Read(image);
            PeFile.Section section = pe.Sections.Last();
            byte[] code = Convert.FromBase64String(V101Code.Code);
            Assert.Equal(FileState.On, Patch101.ProgramState(image));
            Assert.Equal(3, pe.Sections.Count);
            Assert.Equal(Patch101.SectionName, section.Name);
            Assert.Equal(V101Code.Address - TestExe.Base, section.Va);
            Assert.Equal((uint)code.Length, section.VirtualSize);
            Assert.Equal(0xE0000020, section.Characteristics);
            // Behind everything, the signature included, at the file alignment.
            Assert.Equal(FilePatch.Align(original.Length, 0x200), section.Raw);
            // Behind the headers, only the 13 places changed (the signature included).
            int changed = Enumerable.Range(0x400, original.Length - 0x400).Count(i => image[i] != original[i]);
            Assert.InRange(changed, 13, Patch101.Hooks.Sum(h => h.Original.Length));
            Assert.Equal(code, image.Slice(section.Raw, code.Length));
            // The overwritten leftovers right behind the code.
            Assert.Equal(GogExe.Stale.Take(40), image.Slice(section.Raw + code.Length, 40));
            Assert.Equal(GogExe.Stale.Skip(40), image.Slice(TestExe.SectionsAt + 120, 8));
            Assert.Equal(V101Code.Address + 0x1000 - TestExe.Base, pe.SizeOfImage);
            // A jump to the function, padded; an in-place change.
            int at = (int)TestExe.CodeAt + GogExe.CodeAt(0x412DD0);
            Assert.Equal(0xE9, image[at]);
            Assert.Equal(V101Code.Fix412dd0, (uint)(0x412DD0 + 5 + BitConverter.ToInt32(image, at + 1)));
            Assert.Equal(0x90, image[at + 5]);
            Assert.Equal("eb54".Hex(), image.Slice(TestExe.CodeAt + GogExe.CodeAt(0x501DCF), 2));

            Assert.Equal(original, Patch101.RemoveProgram(image));
        }

        public static IEnumerable<object[]> NotGogsProgram => new[]
        {
            GameCode.Both(),                                                // no exe headers
            GogExe.Build(sizeOfImage: 0x8BC000),                            // the image ends elsewhere
            GogExe.Build(emptyCodeSectionLast: true),                       // no room for another section header
            GogExe.Build(GogExe.Code(hooks: false)),                        // other code at the places
            GogExe.Build(GogExe.Code(length: 0x100000)),                    // places outside the code
        }.Select(image => new object[] { image });

        [Theory]
        [MemberData(nameof(NotGogsProgram))]
        public void Program101_WhereItDoesNotFit_IsNotOffered(byte[] image)
        {
            Assert.Equal(FileState.Unknown, Patch101.ProgramState(image));
            Assert.Equal(FileState.Unknown, V101.State(Files(image)));
        }

        // ---- Patch 1.01: data ----

        // Event texts: length, count, then 60 texts ("Text n:" or "Text n."), encrypted.
        internal static byte[] EventFile(char end)
        {
            byte[] plain = Encoding.ASCII.GetBytes(string.Concat(Enumerable.Range(0, 60).Select(i => "Text " + i + (i == 10 || i == 55 ? end : ':') + "\0")));
            byte[] data = new byte[8 + plain.Length + 3];
            BitConverter.GetBytes(plain.Length).CopyTo(data, 0);
            BitConverter.GetBytes(60).CopyTo(data, 4);
            byte key = (byte)plain.Length;
            for (int i = 0; i < plain.Length; i++)
                data[8 + i] = key = (byte)(plain[i] ^ key);
            return data;
        }

        internal static byte[] CountriesFile(char value)
        {
            byte[] data = Enumerable.Repeat((byte)'1', 2100).ToArray();
            data[2065] = (byte)value;
            return data;
        }

        // Runs body with the data hashes of Patch101 and DialogStyles set to the test files'.
        internal static void WithTestHashes(Action body)
        {
            string[] previous = { Patch101.EventsGog, Patch101.Events101, Patch101.CountriesGog, Patch101.Countries101, DialogStyles.Gog, DialogStyles.Result };
            Patch101.EventsGog = GameFiles.Sha256(EventFile(':'));
            Patch101.Events101 = GameFiles.Sha256(EventFile('.'));
            Patch101.CountriesGog = GameFiles.Sha256(CountriesFile('3'));
            Patch101.Countries101 = GameFiles.Sha256(CountriesFile('4'));
            DialogStyles.Gog = GameFiles.Sha256(GogDll());
            DialogStyles.Result = GameFiles.Sha256(Dll1999());
            try
            {
                body();
            }
            finally
            {
                Patch101.EventsGog = previous[0];
                Patch101.Events101 = previous[1];
                Patch101.CountriesGog = previous[2];
                Patch101.Countries101 = previous[3];
                DialogStyles.Gog = previous[4];
                DialogStyles.Result = previous[5];
            }
        }

        // A folder with GOG's data files (as the tests make them) and run.exe.
        internal static void GogData(GameFolder folder)
        {
            foreach (string sub in new[] { "TEXTE", "DATA.DEF", "DATA.VER", "ARCHIVES" })
                Directory.CreateDirectory(folder.File(sub));
            File.WriteAllBytes(folder.File(Patch101.Events), EventFile(':'));
            foreach (string name in Patch101.Countries)
                File.WriteAllBytes(folder.File(name), CountriesFile('3'));
        }

        [Fact]
        public void ChangeEvents_SwapsTheEndOfTexts10And55_OrSaysNo()
        {
            Assert.Equal(EventFile('.'), Patch101.ChangeEvents(EventFile(':'), (byte)':', (byte)'.'));
            Assert.Equal(EventFile(':'), Patch101.ChangeEvents(EventFile('.'), (byte)'.', (byte)':'));
            Assert.Null(Patch101.ChangeEvents(EventFile('.'), (byte)':', (byte)'.'));
        }

        [Fact]
        public void MoveList_IsTheKnownFile()
        {
            byte[] data = MoveList.Build();
            Assert.Equal(169816, data.Length);
            Assert.Equal(MoveList.Hashes[0], GameFiles.Sha256(data));
            Assert.Equal(34, BitConverter.ToUInt16(data, 0));
        }

        [Fact]
        public void Patch101_ChangesProgramAndData_AndTakesThemBack() => WithTestHashes(() =>
        {
            using (GameFolder folder = new GameFolder())
            {
                GogData(folder);
                byte[] exe = GogExe.Build();
                GameFiles files = Files(exe, folder.Path);
                Assert.Equal(FileState.Off, V101.State(files));

                V101.Switch(files, true);
                Assert.Equal(FileState.On, V101.State(files));
                Assert.Equal(FileState.On, Patch101.ProgramState(files.Exe));
                Dictionary<string, byte[]> changes = files.Changes.ToDictionary(c => c.Key, c => c.Value);
                Assert.Equal(EventFile('.'), changes[Patch101.Events]);
                Assert.Equal(EventFile('.'), changes[Patch101.EventsShort]);
                Assert.All(Patch101.Countries, name => Assert.Equal(CountriesFile('4'), changes[name]));
                Assert.Equal(MoveList.Hashes[0], GameFiles.Sha256(changes[Patch101.MoveListFile]));

                V101.Switch(files, false);
                Assert.Equal(FileState.Off, V101.State(files));
                Assert.Equal(exe, files.Exe);
                changes = files.Changes.ToDictionary(c => c.Key, c => c.Value);
                Assert.Equal(EventFile(':'), changes[Patch101.Events]);
                Assert.Null(changes[Patch101.EventsShort]);
                Assert.Null(changes[Patch101.MoveListFile]);
            }
        });

        // Data files that are neither GOG's nor 1.01's (another data set) are left as they are; missing ones too.
        [Fact]
        public void Patch101_LeavesOtherDataAlone() => WithTestHashes(() =>
        {
            using (GameFolder folder = new GameFolder())
            {
                GogData(folder);
                File.WriteAllBytes(folder.File(Patch101.EventsShort), new byte[] { 1 });
                File.WriteAllBytes(folder.File(Patch101.Countries[0]), CountriesFile('7'));
                File.Delete(folder.File(Patch101.Countries[1]));
                File.WriteAllBytes(folder.File(Patch101.MoveListFile), new byte[] { 2 });
                GameFiles files = Files(GogExe.Build(), folder.Path);

                V101.Switch(files, true);
                Assert.Equal(new[] { Patch101.Events }, files.Changes.Select(c => c.Key));
                Assert.Equal(FileState.On, V101.State(files));

                // Event texts of another data set: only the program is left.
                GameFiles others = Files(files.Exe, folder.Path);
                others.Write(Patch101.Events, new byte[] { 3 });
                Assert.Equal(FileState.On, V101.State(others));
                V101.Switch(others, false);
                Assert.Equal(FileState.Off, V101.State(others));
                Assert.Single(others.Changes);
            }
        });

        // The program alone patched (e.g. by the Linux installer, which also took GOG's signature): off until the data follows.
        [Fact]
        public void Patch101_WithOnlyTheProgram_IsOff() => WithTestHashes(() =>
        {
            using (GameFolder folder = new GameFolder())
            {
                GogData(folder);
                GameFiles files = Files(Patch101.AddProgram(GogExe.Build(certificate: false)), folder.Path);
                Assert.Equal(FileState.Off, V101.State(files));

                V101.Switch(files, true);
                Assert.Equal(4 + 1, files.Changes.Count());
                Assert.Equal(FileState.On, V101.State(files));
            }
        });

        // ---- DialogStyles ----

        private const int De = 1031, En = 1033;
        private static ResDll.Control Control(uint style, uint exStyle, uint id) => new ResDll.Control { Style = style, ExStyle = exStyle, Id = id };

        // The test's dialogs as GOG's file has them (gog) or as of 1999.
        private static byte[] TestDll(bool gog)
        {
            uint caption = gog ? 0 : 0x00C00000u;
            return ResDll.Build(
                // Not in the tables: title bar, font name and character set, control IDs.
                Tuple.Create(De, 100, ResDll.Template(true, 0x80080840 | caption, 0, "Spiel", gog ? "MS SANS SERIF" : "MS Sans Serif", (byte)(gog ? 0 : 1),
                    Control(0x50000000, 0, gog ? 0xFFFFu : 0xFFFFFFFF), Control(0x50000000, 0, 5))),
                // Without a title bar in 1999 as well; control 5 keeps ID 0xFFFF.
                Tuple.Create(De, 382, ResDll.Template(true, 0x80000000, 0, "", "",  0,
                    Control(0, 0, gog ? 0xFFFFu : 0xFFFFFFFF), Control(0, 0, 1), Control(0, 0, 2), Control(0, 0, 3), Control(0, 0, 4), Control(0, 0, 0xFFFF))),
                Tuple.Create(En, 205, ResDll.Template(true, 0x80000000, 0, "Box", "", 0)),
                // Another style and extended style, another font kept.
                Tuple.Create(De, 613, ResDll.Template(true, gog ? 0x90080840 : 0x90C800C0, gog ? 0x00040008u : 0x00040000, "Liste", "Arial", 1)),
                // Quotes in the title (' as the low byte of another character stays); DLGTEMPLATE with font.
                Tuple.Create(De, 237, ResDll.Template(false, 0x80000040 | caption, 0, gog ? "Karl's ħ" : "Karl\"s ħ", gog ? "MS SANS SERIF" : "MS Sans Serif", 0,
                    Control(0x50000000, 0, 0xFFFF))),
                // Sunken frames instead of borders.
                Tuple.Create(De, 490, ResDll.Template(true, 0x80000000 | caption, 0, "Trikots", "", 0,
                    Control(0x50800000, 0, 1), Control(gog ? 0x50800000 : 0x50000000u, gog ? 0 : 0x200u, 2), Control(gog ? 0x50800000 : 0x50000000u, gog ? 0 : 0x200u, 3))),
                // Transparent from control 0 to 3, not 4.
                Tuple.Create(De, 598, ResDll.Template(true, 0x80000000 | caption, 0, "Taktik", "", 0,
                    Control(0, gog ? 0 : 0x20u, 1), Control(0, gog ? 0 : 0x20u, 2), Control(0, gog ? 0 : 0x20u, 3), Control(0, gog ? 0 : 0x20u, 4), Control(0, 0, 5))),
                // DLGTEMPLATE controls get frames too (0, 1 and 3, not 2).
                Tuple.Create(De, 262, ResDll.Template(false, 0x80000000 | caption, 0, "Werte", "", 0,
                    Control(0x50000000, gog ? 0 : 0x200u, 1), Control(0x50000000, gog ? 0 : 0x200u, 2), Control(0x50000000, 0, 3))));
        }

        internal static byte[] GogDll() => TestDll(true);
        internal static byte[] Dll1999() => TestDll(false);

        [Fact]
        public void DialogStyles_RestoresTheStylesOf1999()
        {
            Assert.Equal(8, DialogStyles.Dialogs(GogDll()).Count());
            Assert.Equal(Dll1999(), DialogStyles.Restore(GogDll()));
        }

        [Fact]
        public void DialogStyles_KeepsABackup_AndRestoresIt() => WithTestHashes(() =>
        {
            using (GameFolder folder = new GameFolder())
            {
                File.WriteAllBytes(folder.File(DialogStyles.Dll), GogDll());
                GameFiles files = Files(new byte[0], folder.Path);
                Assert.Equal(FileState.Off, Dialogs.State(files));

                Dialogs.Switch(files, true);
                Assert.Equal(new[] { DialogStyles.Backup, DialogStyles.Dll }, files.Changes.Select(c => c.Key));
                Assert.Equal(FileState.On, Dialogs.State(files));
                Assert.Equal(GogDll(), files.Read(DialogStyles.Backup));
                Dialogs.Switch(files, true);
                Assert.Equal(2, files.Changes.Count());

                Dialogs.Switch(files, false);
                Assert.Equal(GogDll(), files.Read(DialogStyles.Dll));
                Assert.Null(files.Read(DialogStyles.Backup));
                Assert.Equal(FileState.Off, Dialogs.State(files));

                // Applied, but without GOG's file as the backup it can't be taken back.
                files.Write(DialogStyles.Dll, Dll1999());
                Assert.Equal(FileState.Fixed, Dialogs.State(files));
                files.Write(DialogStyles.Dll, new byte[] { 1 });
                Assert.Equal(FileState.Unknown, Dialogs.State(files));
            }
        });

        [Fact]
        public void DialogStyles_WithAnUnexpectedResult_ChangesNothing() => WithTestHashes(() =>
        {
            using (GameFolder folder = new GameFolder())
            {
                File.WriteAllBytes(folder.File(DialogStyles.Dll), GogDll());
                DialogStyles.Result = GameFiles.Sha256(new byte[] { 1 });
                GameFiles files = Files(new byte[0], folder.Path);

                Assert.Throws<InvalidOperationException>(() => Dialogs.Switch(files, true));
                Assert.Empty(files.Changes);
            }
        });
    }
}
