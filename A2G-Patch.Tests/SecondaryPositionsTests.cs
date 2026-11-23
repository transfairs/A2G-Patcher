using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace A2GPatch.Tests
{
    /// <summary>Tests for SecondaryPositions: the places sent to the helper, where the helper goes, and refusing what it doesn't know.</summary>
    public class SecondaryPositionsTests
    {
        private static readonly SecondaryPositions Positions = Patch.Get<SecondaryPositions>();
        private const uint Table = 0x6C2948, Jumps = 0x5A0288;
        // The pieces: a place with ecx or edx, the original's youth switch, and an ecx place whose check calls something else.
        private const int Ecx = 0, Edx = 1, Switch = 2, OtherRand = 3;

        // rand (a ret) at the code's start, then each piece behind its check and 16 bytes of filler, then the zeros.
        private static byte[] Exe(int[] pieces, int zeros = 0x300) => TestExe.Build(_ =>
        {
            List<byte> code = new List<byte> { 0xC3 };
            code.AddRange(Enumerable.Repeat((byte)0xCC, 15));
            foreach (int piece in pieces)
            {
                int at = code.Count;
                int rand = piece == OtherRand ? 1 : 0;
                code.AddRange(new byte[] { 0x6A, 0x64, 0xE8 }.Concat(BitConverter.GetBytes(rand - (at + 7))));
                code.AddRange(new byte[] { 0x83, 0xC4, 0x04, 0x83, 0xF8, 0x28, 0x73, piece == Switch ? (byte)SecondaryPositions.SwitchLength : (byte)0x0E });
                byte?[] place = piece == Switch ? SecondaryPositions.OriginalSwitch(-(at + 15), Table, Jumps) : SecondaryPositions.OriginalPlace(piece % 2, Table);
                code.AddRange(place.Select(b => b.Value));
                code.AddRange(Enumerable.Repeat((byte)0xCC, 16));
            }
            code.AddRange(new byte[zeros]);
            return code.ToArray();
        });

        private static byte[] Apply(byte[] image, IEnumerable<Site> sites, bool patched = true)
        {
            byte[] result = (byte[])image.Clone();
            foreach (Site site in sites)
                (patched ? site.Patched : site.Original).CopyTo(result, site.Offset);
            return result;
        }

        private static long Va(long offset) => TestExe.Base + TestExe.CodeRva + offset - TestExe.CodeAt;

        // Where the call at the file offset leads, as an address.
        private static long CallVa(byte[] image, long call) => Va(call) + 5 + BitConverter.ToInt32(image, (int)call + 1);

        [Fact]
        public void Find_SendsFourPlacesToTheHelper_InFrontOfTheSoundData()
        {
            int[] pieces = { Ecx, Ecx, Ecx, Edx };
            byte[] image = Exe(pieces);
            Site[] sites = Positions.Find(image);

            // The code section ends at 0x400 + 16 + 4 * 45 + 0x300; the helper's room in front of the CupDraw helper's, the stub's and the data's.
            long end = TestExe.CodeAt + 16 + 4 * 45 + 0x300;
            long helper = ((end - 177) & ~15L) - InterviewEffects.StubSize - SoundFormat.DataRoom - SecondaryPositions.Room;
            long[] places = Enumerable.Range(0, 4).Select(i => TestExe.CodeAt + 16 + 45 * i + 15L).ToArray();
            Assert.Equal(new[] { helper }.Concat(places), sites.Select(s => s.Offset));
            Assert.All(sites, s => Assert.False(s.IsPatched));
            Assert.Equal(new byte[83], sites[0].Original);
            Assert.Equal(SecondaryPositions.HelperCode(Va(TestExe.CodeAt) - Va(helper)).Select(b => b.Value), sites[0].Patched);

            byte[] patched = Apply(image, sites);
            foreach (int call in SecondaryPositions.RandCalls)
                Assert.Equal(Va(TestExe.CodeAt), CallVa(patched, helper + call - 1));          // the helper's rand calls the game's
            for (int i = 0; i < 4; i++)
            {
                Assert.Equal(SecondaryPositions.OriginalPlace(pieces[i], Table).Select(b => b.Value), sites[i + 1].Original);
                Assert.Equal(Va(helper), CallVa(patched, places[i] + 2));                       // xor / call helper
                Assert.Equal(new byte[] { 0xEB, 0x05 }, patched.Skip((int)places[i] + 7).Take(2));
                Assert.Equal(Table, BitConverter.ToUInt32(patched, (int)places[i] + 9));          // the table kept
            }
            Assert.Equal(new byte[] { 0x33, 0xD2 }, patched.Skip((int)places[3]).Take(2));       // the edx place stays one

            Site[] again = Positions.Find(patched);
            Assert.All(again, s => Assert.True(s.IsPatched));
            Assert.Equal(sites.Select(s => s.Original), again.Select(s => s.Original));
            Assert.Equal(sites.Select(s => s.Patched), again.Select(s => s.Patched));
            Assert.Equal(image, Apply(patched, again, false));
        }

        [Fact]
        public void Find_PutsTheHelperIntoTheYouthSwitch_WhereTheOriginalHasIt()
        {
            byte[] image = Exe(new[] { Switch, Ecx, Ecx, Edx }, 0);
            Site[] sites = Positions.Find(image);

            long at = TestExe.CodeAt + 16 + 15, helper = at + 7;
            Assert.Equal(new[] { at }.Concat(Enumerable.Range(0, 3).Select(i => TestExe.CodeAt + 16 + 15 + 0x74 + 16 + 45 * i + 15L)), sites.Select(s => s.Offset));
            Assert.Equal(image.Skip((int)at).Take(SecondaryPositions.SwitchLength), sites[0].Original);

            byte[] patched = Apply(image, sites);
            Assert.Equal(Va(helper), CallVa(patched, at));                                      // call helper / jmp behind the switch
            Assert.Equal(new byte[] { 0xEB, SecondaryPositions.SwitchLength - 7 }, patched.Skip((int)at + 5).Take(2));
            Assert.Equal(Jumps, BitConverter.ToUInt32(patched, (int)at + SecondaryPositions.SwitchLength - 4));
            Assert.Equal(Va(TestExe.CodeAt), CallVa(patched, helper + 8));
            Assert.All(sites.Skip(1), s => Assert.Equal(Va(helper), CallVa(patched, s.Offset + 2)));

            Site[] again = Positions.Find(patched);
            Assert.All(again, s => Assert.True(s.IsPatched));
            Assert.Equal(image, Apply(patched, again, false));
        }

        [Fact]
        public void Find_RefusesUnknownCodeAndTakenOrMissingRoom()
        {
            int[] pieces = { Ecx, Ecx, Ecx, Edx };
            byte[] image = Exe(pieces);
            Site[] sites = Positions.Find(image);

            Assert.Equal(7, Positions.Patterns.Count());
            Assert.Null(Positions.Find(GameCode.Exe(image.Skip((int)TestExe.CodeAt).ToArray())));       // no exe
            Assert.Null(Positions.Find(Exe(new[] { Ecx, Ecx, Edx })));                              // a place missing
            Assert.Null(Positions.Find(Exe(new[] { Switch, Ecx, Ecx, Ecx, Edx })));                 // one too many
            Assert.Null(Positions.Find(Exe(new[] { Switch, Switch, Ecx, Edx })));                   // two switches
            Assert.Null(Positions.Find(Exe(new[] { Ecx, Ecx, OtherRand, Edx })));                   // another rand
            Assert.Null(Positions.Find(Apply(image, sites.Take(2))));                               // patched halfway
            Assert.Null(Positions.Find(Apply(image, sites.Skip(1))));                               // patched without the helper
            Assert.Null(Positions.Find(image.With((int)sites[0].Offset + 80, 1)));                  // the helper's room taken
            Assert.Null(Positions.Find(Exe(pieces, 0x40)));                                         // no room
            Assert.Null(Positions.Find(image.With(TestExe.SectionsAt + 40 + 36, 0x40000040)));     // no code section
        }

        // ---- with Patch 1.01 ----

        private const uint GogTable = 0x6C5A58;
        private static readonly Patch101.Hook YouthHook = Patch101.Hooks.Single(h => h.Target == V101Code.Fix5a2836);

        // GOG's code (with the 1.01 places) and zeros at its end; the youth players' place is the hook's, the three others behind 0x10000.
        private static byte[] GogImage(bool secondHook = false)
        {
            byte[] code = GogExe.Code(length: GogExe.CodeLength + 0x300);
            Array.Clear(code, GogExe.CodeLength, 0x300);
            int youth = GogExe.CodeAt(YouthHook.Va);
            int[] checks = { youth - 15, 0x10000, 0x10040, 0x10080 };
            for (int i = 0; i < checks.Length; i++)
            {
                GogExe.DiscCheck(code, checks[i] + 2, GogExe.DiscTarget, "83C40483F828730E");
                code[checks[i]] = 0x6A;
                code[checks[i] + 1] = 0x64;
                if (i > 0)
                    SecondaryPositions.OriginalPlace(i / 3, GogTable).Select(b => b.Value).ToArray().CopyTo(code, checks[i] + 15);
            }
            if (secondHook)
                YouthHook.Patched.CopyTo(code, 0x10040 + 15);
            return TestExe.Build(_ => code, sizeOfImage: V101Code.Address - TestExe.Base);
        }

        private static long YouthAt => TestExe.CodeAt + GogExe.CodeAt(YouthHook.Va);

        [Fact]
        public void Find_TakesOverPatch101sJump_AndGivesItBack()
        {
            byte[] gog = GogImage();
            Assert.Equal(YouthHook.Original, gog.Slice(YouthAt, 14));
            Site[] plain = Positions.Find(gog);
            Assert.Equal(YouthHook.Original, plain.Single(s => s.Offset == YouthAt).Original);

            // 1.01 on: the place holds its jump, which counts as original and comes back.
            byte[] v101 = Patch101.AddProgram(gog);
            Site[] sites = Positions.Find(v101);
            Site youth = sites.Single(s => s.Offset == YouthAt);
            Assert.All(sites, s => Assert.False(s.IsPatched));
            Assert.Equal(YouthHook.Patched, youth.Original);
            Assert.Equal(plain.Single(s => s.Offset == YouthAt).Patched, youth.Patched);
            byte[] both = Apply(v101, sites);
            Assert.Equal(FileState.On, Patch101.ProgramState(both));
            Assert.Equal(v101, Apply(both, Positions.Find(both), false));

            // 1.01 off again: it leaves the place, which then goes back to GOG's bytes.
            byte[] positions = Patch101.RemoveProgram(both);
            Assert.Equal(youth.Patched, positions.Slice(YouthAt, 14));
            Assert.Equal(gog, Apply(positions, Positions.Find(positions), false));
        }

        [Fact]
        public void Patch101_LeavesThePlaceToThePositions()
        {
            byte[] gog = GogImage();
            byte[] positions = Apply(gog, Positions.Find(gog));
            Assert.Equal(FileState.Off, Patch101.ProgramState(positions));

            byte[] both = Patch101.AddProgram(positions);
            Assert.Equal(positions.Slice(YouthAt, 14), both.Slice(YouthAt, 14));
            Assert.All(Positions.Find(both), s => Assert.True(s.IsPatched));
            Assert.Equal(Patch101.AddProgram(gog), Apply(both, Positions.Find(both), false));    // positions off: the jump is back
            Assert.Equal(positions, Patch101.RemoveProgram(both));
        }

        [Fact]
        public void Find_RefusesTwoJumps()
        {
            Assert.Null(Positions.Find(Patch101.AddProgram(GogImage(secondHook: true))));
        }
    }
}
