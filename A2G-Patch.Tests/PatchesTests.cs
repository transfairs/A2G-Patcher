using System;
using System.Collections.Generic;
using System.Linq;
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

        /// <summary>The bytes written as hex digits.</summary>
        internal static byte[] Hex(this string hex) =>
            Enumerable.Range(0, hex.Length / 2).Select(i => Convert.ToByte(hex.Substring(2 * i, 2), 16)).ToArray();
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
                "Desktop-Auflösung auch nach einem Absturz zurückholen",
            }, Patch.All.Select(p => p.Name));
            Assert.Equal(new byte[] { 1, 2, 4, 8, 16, 32, 64, 128 }, Patch.All.Select(p => p.Flag));
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
