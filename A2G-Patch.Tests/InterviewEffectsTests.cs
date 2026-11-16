using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Xunit;

namespace A2GPatch.Tests
{
    /// <summary>
    /// The interview code for the tests, in TestExe's code section (0x402000, file 0x400): the heads of the
    /// effect application and the morale change, a call that only looks like the application's (its
    /// context field is wrong), the two calls the patch redirects, and the goal limits (so GameInstall
    /// takes the exe for one to patch on disk). 0x200 bytes, then optionally zeros.
    /// </summary>
    internal static class InterviewCode
    {
        internal const uint CodeVa = TestExe.Base + TestExe.CodeRva;
        internal const uint Engine = 0x7829D8, Context = 0x782AA0;
        internal const int ApplyAt = 0x10, MoraleAt = 0x40, DecoyAt = 0x80, ApplySiteAt = 0xC0, MoraleSiteAt = 0x100, GoalsAt = 0x140, Length = 0x200;
        internal const uint Apply = CodeVa + ApplyAt, Morale = CodeVa + MoraleAt;
        // The calls in the file.
        internal const int ApplyCall = (int)TestExe.CodeAt + ApplySiteAt + 17, MoraleCall = (int)TestExe.CodeAt + MoraleSiteAt + 10;

        private static byte[] U(uint value) => BitConverter.GetBytes(value);

        /// <summary>mov ecx,[engine] / push eax / call / push context / call target / mov eax,[field], at siteVa.</summary>
        internal static byte[] ApplySite(uint siteVa, uint target, uint engine = Engine, uint field = Context + 0x10) =>
            "8B0D".Hex().Concat(U(engine)).Concat("50E8".Hex()).Concat(U(0x1234)).Concat(new byte[] { 0x68 }).Concat(U(Context))
                .Concat(new byte[] { 0xE8 }).Concat(U(target - (siteVa + 22))).Concat(new byte[] { 0xA1 }).Concat(U(field)).ToArray();

        /// <summary>Case 12's call of target, at siteVa.</summary>
        internal static byte[] MoraleSite(uint siteVa, uint target) =>
            "8B4C24508B54245C5152E8".Hex().Concat(U(target - (siteVa + 15))).Concat("83C4085F5E83C440C3".Hex()).ToArray();

        internal static byte[] Text(int zeros = 0, uint engine = Engine, uint applyTarget = Apply, uint moraleTarget = Morale, bool applySite = true, bool moraleSite = true)
        {
            byte[] text = Enumerable.Repeat((byte)0xCC, Length).Concat(new byte[zeros]).ToArray();
            InterviewEffects.ApplyHead.CopyTo(text, ApplyAt);
            InterviewEffects.MoraleHead.CopyTo(text, MoraleAt);
            ApplySite(CodeVa + DecoyAt, Apply, field: 0).CopyTo(text, DecoyAt);
            if (applySite)
                ApplySite(CodeVa + ApplySiteAt, applyTarget, engine).CopyTo(text, ApplySiteAt);
            if (moraleSite)
                MoraleSite(CodeVa + MoraleSiteAt, moraleTarget).CopyTo(text, MoraleSiteAt);
            GameCode.Goals().CopyTo(text, GoalsAt);
            return text;
        }

        /// <summary>As the original exe: the code section last and full, so the stub needs a section of its own.</summary>
        internal static byte[] OriginalExe() => TestExe.Build(_ => Text(), sizeOfImage: 0x3000);

        /// <summary>As GOG's run.exe: the empty import section executable and last, room for the stub there.</summary>
        internal static byte[] GogExe() => TestExe.Build(_ => Text(), gog: true, imports: new string[0][], sizeOfImage: 0x3000);

        /// <summary>The call at the file offset call: where it leads (in TestExe's code section).</summary>
        internal static uint Target(byte[] image, int call) => (uint)(CodeVa + (call - (int)TestExe.CodeAt) + 5 + BitConverter.ToInt32(image, call + 1));
    }

    /// <summary>Tests for InterviewEffects: the stub, where it goes, and switching the patch.</summary>
    public class InterviewEffectsTests
    {
        private static readonly InterviewEffects Interviews = Patch.All.OfType<InterviewEffects>().Single();

        private static FileState State(byte[] image) => Interviews.State(new GameFiles(null, image));

        private static byte[] Switch(byte[] image, bool on)
        {
            GameFiles files = new GameFiles(null, image);
            Interviews.Switch(files, on);
            return files.Exe;
        }

        private static byte[] ExpectedStub(uint stub) =>
            InterviewEffects.StubBytes(stub, InterviewCode.Engine, InterviewCode.Context, InterviewCode.Apply, InterviewCode.Context + 0xC258, InterviewCode.Morale);

        // A2G-Trainer's InterviewEffectsFix.BuildStub for the addresses 0 and for 0x11000000 to 0x66000000.
        private const string TrainerStub =
            "413247494E543032000000000000000000000000000000000000000000000000803D0800000000746DA10000000085C074648B801C01000085C0745A8B4814" +
            "8B501883F90C750E81E2FFFFFF0081CA00000040EB0783F90A7502B10C890D140000008915180000008B481C8B502083F90C750E81E2FFFFFF0081CA0000" +
            "0040EB0783F90A7502B10C890D1C000000891520000000EB1A31C9890D14000000890D18000000890D1C000000890D20000000B800000000FFE000000000" +
            "0000000000807C240B4075278B44240825FFFFFF000FAF050000000099B964000000F7F98B54240485D2740601828C030000C3B800000000FFE000000000" +
            "00000000000000";
        private const string TrainerStubWithAddresses =
            "413247494E543032000000000000000000000000000000000000000000000000803D0800001100746DA10000002285C074648B801C01000085C0745A8B4814" +
            "8B501883F90C750E81E2FFFFFF0081CA00000040EB0783F90A7502B10C890D140000338915180000338B481C8B502083F90C750E81E2FFFFFF0081CA0000" +
            "0040EB0783F90A7502B10C890D1C000033891520000033EB1A31C9890D14000033890D18000033890D1C000033890D20000033B800000044FFE000000000" +
            "0000000000807C240B4075278B44240825FFFFFF000FAF050000005599B964000000F7F98B54240485D2740601828C030000C3B800000066FFE000000000" +
            "00000000000000";

        // Byte for byte the trainer's stub, with the flag set (the trainer sets it after writing the stub).
        [Fact]
        public void Stub_IsTheTrainers_WithTheFlagSet()
        {
            byte[] trainer = TrainerStub.Hex(), withAddresses = TrainerStubWithAddresses.Hex();
            trainer[8] = withAddresses[8] = 1;
            Assert.Equal(trainer, InterviewEffects.StubBytes(0, 0, 0, 0, 0, 0));
            Assert.Equal(withAddresses, InterviewEffects.StubBytes(0x11000000, 0x22000000, 0x33000000, 0x44000000, 0x55000000, 0x66000000));
        }

        // The original exe: the stub goes into a section of its own behind the image, both calls lead there,
        // and taking the patch back restores the exe to the byte.
        [Fact]
        public void OriginalExe_GetsASectionOfItsOwn_AndLosesItAgain()
        {
            byte[] original = InterviewCode.OriginalExe();
            Assert.Equal(FileState.Off, State(original));

            byte[] image = Switch(original, true);
            Assert.Equal(FileState.On, State(image));
            PeFile pe = PeFile.Read(image);
            PeFile.Section section = pe.Sections.Last();
            Assert.Equal(InterviewEffects.SectionName, section.Name);
            Assert.Equal(0x3000u, section.Va);
            Assert.Equal(0xE0000020u, section.Characteristics);
            Assert.Equal(0x4000u, pe.SizeOfImage);
            Assert.Equal(ExpectedStub(0x403000), image.Slice(section.Raw, InterviewEffects.StubSize));
            Assert.Equal(0x403020u, InterviewCode.Target(image, InterviewCode.ApplyCall));
            Assert.Equal(0x4030C0u, InterviewCode.Target(image, InterviewCode.MoraleCall));
            Assert.Same(image, Switch(image, true));

            Assert.Equal(original, Switch(image, false));
        }

        // GOG: the stub goes right in front of the cup draw helper's place at the end of the import section.
        [Fact]
        public void GogExe_TakesTheStubInFrontOfTheCupDrawHelper()
        {
            byte[] original = InterviewCode.GogExe();
            Assert.Equal(FileState.Off, State(original));

            byte[] image = Switch(original, true);
            Assert.Equal(FileState.On, State(image));
            Assert.Equal(original.Length, image.Length);
            Assert.Equal(2, PeFile.Read(image).Sections.Count);
            // The import section at file 0x200 is loaded at 0x401000; the helper would start at 0x340.
            Assert.Equal(ExpectedStub(0x401040), image.Slice(0x240, InterviewEffects.StubSize));
            Assert.Equal(new byte[0xC0], image.Slice(0x340, 0xC0));
            Assert.Equal(0x401060u, InterviewCode.Target(image, InterviewCode.ApplyCall));
            Assert.Equal(0x401100u, InterviewCode.Target(image, InterviewCode.MoraleCall));

            Assert.Equal(original, Switch(image, false));
        }

        // An exe with the stub at stub's place in the zeros behind the code section's 0x200 bytes.
        private static byte[] WithStub(uint stub, byte[] code, uint applyTarget = 0, uint moraleTarget = 0) =>
            TestExe.Build(_ =>
            {
                byte[] text = InterviewCode.Text(0x100, applyTarget: applyTarget == 0 ? stub + 0x20 : applyTarget, moraleTarget: moraleTarget == 0 ? stub + 0xC0 : moraleTarget);
                code?.CopyTo(text, (int)(stub - InterviewCode.CodeVa));
                return text;
            }, sizeOfImage: 0x3000);

        public static IEnumerable<object[]> UnknownExes()
        {
            const uint end = InterviewCode.CodeVa + InterviewCode.Length;
            yield return new object[] { GameCode.Exe(InterviewCode.Text()) };                                                 // no PE headers
            yield return new object[] { TestExe.Build(_ => InterviewCode.Text(applySite: false)) };                           // no application call
            yield return new object[] { TestExe.Build(_ => InterviewCode.Text(moraleSite: false)) };                          // no morale call
            yield return new object[] { TestExe.Build(_ => InterviewCode.Text(engine: InterviewCode.Engine + 4)) };           // another engine pointer
            yield return new object[] { TestExe.Build(_ => InterviewCode.Text(applySite: false), overlay: InterviewCode.ApplySite(0, 0)) };   // a call outside the sections
            yield return new object[] { TestExe.Build(_ => InterviewCode.Text(moraleSite: false), overlay: InterviewCode.MoraleSite(0, 0)) };
            yield return new object[] { TestExe.Build(_ => InterviewCode.Text(applyTarget: InterviewCode.CodeVa + 0x1F0)) };   // somewhere else
            yield return new object[] { TestExe.Build(_ => InterviewCode.Text(moraleTarget: InterviewCode.CodeVa + 0x1F0)) };
            yield return new object[] { TestExe.Build(_ => InterviewCode.Text(applyTarget: 0x500020, moraleTarget: 0x5000C0)) };   // a "stub" outside the image
            yield return new object[] { WithStub(end + 0xF0, null) };                                                          // cut off by the end of the file
            yield return new object[] { WithStub(end, null) };                                                                 // zeros, not the stub
            yield return new object[] { WithStub(end, InterviewEffects.StubBytes(end, InterviewCode.Engine, InterviewCode.Context, end - 0x10, InterviewCode.Context + 0xC258, InterviewCode.Morale)) };
            yield return new object[] { WithStub(end, InterviewEffects.StubBytes(end, InterviewCode.Engine, InterviewCode.Context, InterviewCode.Apply, InterviewCode.Context + 0xC258, end - 0x10)) };
            yield return new object[] { WithStub(end, InterviewEffects.StubBytes(end, InterviewCode.Engine, InterviewCode.Context, InterviewCode.Apply, InterviewCode.Context, InterviewCode.Morale)) };
            yield return new object[] { TestExe.Build(_ => InterviewCode.Text(), emptyCodeSectionLast: true) };                // no room: no header fits, no zeros
            // The code section named as patch 1.01's, which must stay last; no other executable section.
            yield return new object[] { TestExe.Build(_ => InterviewCode.Text()).With(TestExe.SectionsAt + 40, 0x7632612E).With(TestExe.SectionsAt + 44, 0x313031) };
        }

        [Theory]
        [MemberData(nameof(UnknownExes))]
        public void UnexpectedCode_IsUnknown(byte[] image) => Assert.Equal(FileState.Unknown, State(image));

        // The stub itself with the right addresses is found as switched on, wherever it lies.
        [Fact]
        public void StubBehindTheCode_IsOn_AndTakenBackByZeroing()
        {
            const uint end = InterviewCode.CodeVa + InterviewCode.Length;
            byte[] image = WithStub(end, ExpectedStub(end));
            Assert.Equal(FileState.On, State(image));
            Assert.Equal(WithStub(end, null, InterviewCode.Apply, InterviewCode.Morale), Switch(image, false));
        }

        // The cup draw patch doesn't take the interviews' section for the last executable one.
        [Fact]
        public void CupDraw_SkipsTheInterviewSection()
        {
            byte[] image = TestExe.Build(PatchesTests.CupCode);
            Site[] before = GameInstallTests.Cup.Find(image);
            Site[] after = GameInstallTests.Cup.Find(PeFile.AddSection(image, InterviewEffects.SectionName, new byte[16]));
            Assert.Equal(before.Select(s => s.Offset), after.Select(s => s.Offset));
        }

        // In a game folder: applied with a section of its own and taken back to the byte.
        [Fact]
        public void GameInstall_AppliesAndTakesItBack()
        {
            using (GameFolder folder = new GameFolder())
            {
                byte[] original = InterviewCode.OriginalExe();
                File.WriteAllBytes(folder.File("anstoss2.exe"), original);
                GameInstall install = GameInstall.Detect(folder.Path);
                Assert.True(install.CanApply(Interviews) && !install.IsApplied(Interviews));

                install.Apply(new Patch[] { Interviews });
                Assert.True(GameInstall.Detect(folder.Path).IsApplied(Interviews));
                Assert.True(File.ReadAllBytes(folder.File("anstoss2.exe")).Length > original.Length);

                GameInstall.Detect(folder.Path).Apply(new Patch[0]);
                Assert.Equal(original, File.ReadAllBytes(folder.File("anstoss2.exe")));
                Assert.False(File.Exists(folder.File("anstoss2.exe" + GameInstall.BackupSuffix)));
            }
        }

        // The DLL carries the same stub (the addresses added at run time).
        [Fact]
        public void EmbeddedDll_HoldsTheStub()
        {
            byte[] dll = GameInstall.DllBytes();
            if (dll != null)
                Assert.Single(Patch.FindAll(dll, Patch.Exact(InterviewEffects.StubBytes(0, 0, 0, 0, 0, 0))));
        }

        // ---- running the stub ----

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr VirtualAlloc(IntPtr address, UIntPtr size, uint type, uint protect);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool VirtualFree(IntPtr address, UIntPtr size, uint type);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void ApplyEntry(uint context);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void MoraleEntry(uint club, int value);

        // Runs body with 0x1000 bytes of executable memory.
        private static void WithMemory(Action<uint> body)
        {
            IntPtr block = VirtualAlloc(IntPtr.Zero, (UIntPtr)0x1000, 0x3000, 0x40);   // MEM_COMMIT | MEM_RESERVE, PAGE_EXECUTE_READWRITE
            try
            {
                body((uint)block.ToInt32());
            }
            finally
            {
                VirtualFree(block, UIntPtr.Zero, 0x8000);                                  // MEM_RELEASE
            }
        }

        private static void Put(uint address, byte[] bytes) => Marshal.Copy(bytes, 0, new IntPtr(address), bytes.Length);
        private static void Put(uint address, uint value) => Put(address, BitConverter.GetBytes(value));
        private static int Int(uint address) => Marshal.ReadInt32(new IntPtr(address));

        // A stand-in that records its two stack arguments and counts its calls (cdecl).
        private static byte[] Recorder(uint record) => new[]
        {
            new byte[] { 0x8B, 0x44, 0x24, 0x04, 0xA3 }, BitConverter.GetBytes(record),         // mov eax,[esp+4] / mov [rec],eax
            new byte[] { 0x8B, 0x44, 0x24, 0x08, 0xA3 }, BitConverter.GetBytes(record + 4),     // mov eax,[esp+8] / mov [rec+4],eax
            new byte[] { 0xFF, 0x05 }, BitConverter.GetBytes(record + 8),                       // inc dword [rec+8]
            new byte[] { 0xC3 },                                                                // ret
        }.SelectMany(p => p).ToArray();

        // The application entry, as the game calls it: with the flag set it copies the engine copy's pairs
        // into the context, 10 to the morale case and a fine marked; with the flag clear, or without an
        // engine or its copy, it zeroes them; then it goes on to the application.
        [Fact]
        public void ApplyEntry_CopiesAndMapsThePairs_ThenApplies() => WithMemory(block =>
        {
            uint stub = block, apply = block + 0x100, record = block + 0x140, engine = block + 0x180, engineObject = block + 0x200,
                copy = block + 0x340, context = block + 0x380;
            Put(apply, Recorder(record));
            Put(stub, InterviewEffects.StubBytes(stub, engine, context, apply, 0, 0));
            Put(engine, engineObject);
            Put(engineObject + 0x11C, copy);
            void Pairs(params int[] values) => Put(copy + 0x14, values.SelectMany(BitConverter.GetBytes).ToArray());
            int[] Effects() => Enumerable.Range(0, 4).Select(i => Int(context + 0x14 + (uint)i * 4)).ToArray();
            ApplyEntry run = Marshal.GetDelegateForFunctionPointer<ApplyEntry>(new IntPtr(stub + InterviewEffects.ApplyEntry));

            Pairs(11, -3, 12, 20000);
            run(context);
            Assert.Equal(new[] { 11, -3, 12, 0x40000000 | 20000 }, Effects());
            Pairs(12, 100000, 10, -5);
            run(context);
            Assert.Equal(new[] { 12, 0x40000000 | 100000, 12, -5 }, Effects());
            Pairs(5, -2, 1, 4);
            run(context);
            Assert.Equal(new[] { 5, -2, 1, 4 }, Effects());

            Put(engineObject + 0x11C, 0u);
            run(context);
            Assert.Equal(new int[4], Effects());
            Put(engineObject + 0x11C, copy);
            run(context);
            Put(engine, 0u);
            run(context);
            Assert.Equal(new int[4], Effects());
            Put(engine, engineObject);
            run(context);
            Put(stub + 8, 0u);
            run(context);
            Assert.Equal(new int[4], Effects());

            Assert.Equal((int)context, Int(record));
            Assert.Equal(8, Int(record + 8));
        });

        // The morale entry, as case 12 calls it: a marked fine goes on the club's other costs in money units
        // (DM * factor / 100), anything else on to the morale change.
        [Fact]
        public void MoraleEntry_BooksFines_AndPassesTheRestOn() => WithMemory(block =>
        {
            uint stub = block, morale = block + 0x100, record = block + 0x140, moneyFactor = block + 0x180, club = block + 0x200;
            Put(morale, Recorder(record));
            Put(moneyFactor, 1000u);
            Put(club + 0x38C, 5000u);
            Put(stub, InterviewEffects.StubBytes(stub, 0, 0, 0, moneyFactor, morale));
            MoraleEntry run = Marshal.GetDelegateForFunctionPointer<MoraleEntry>(new IntPtr(stub + InterviewEffects.MoraleEntry));

            run(club, 0x40000000 | 20000);
            Assert.Equal(5000 + 200000, Int(club + 0x38C));
            run(0, 0x40000000 | 100000);
            Assert.Equal(0, Int(record + 8));

            run(club, -5);
            Assert.Equal((int)club, Int(record));
            Assert.Equal(-5, Int(record + 4));
            run(club, 7);
            Assert.Equal(7, Int(record + 4));
            Assert.Equal(2, Int(record + 8));
            Assert.Equal(205000, Int(club + 0x38C));
        });
    }
}
