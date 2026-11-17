using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Xunit;

namespace A2GPatch.Tests
{
    /// <summary>
    /// Tests for SpeechPenalty: its three places in original and patched code, refusing what it doesn't
    /// know, and the rewritten code run natively - the function against the game's own, and the marking.
    /// </summary>
    public class SpeechPenaltyTests
    {
        private static readonly SpeechPenalty Speech = Patch.All.OfType<SpeechPenalty>().Single();
        // The speech table of the original exe and of GOG / the 2007 CD.
        internal const uint OriginalTable = 0x6B7D50, GogTable = 0x6BAE48;

        private static byte?[] Code(byte?[] head, byte[] code, byte[] tail) => head.Concat(Patch.Exact(code)).Concat(Patch.Exact(tail)).ToArray();
        private static byte[] Fill(byte?[] pattern) => pattern.Select(b => b ?? (byte)0x12).ToArray();

        internal static byte[] Roll(bool patched = false) => Fill(Code(SpeechPenalty.RollHead, patched ? SpeechPenalty.RollPatched : SpeechPenalty.RollOriginal, SpeechPenalty.RollTail));
        internal static byte[] Mark(bool patched = false) => Fill(Code(SpeechPenalty.MarkHead, patched ? SpeechPenalty.MarkPatched : SpeechPenalty.MarkOriginal, SpeechPenalty.MarkTail));
        internal static byte[] Apply(uint table, bool patched = false) => patched
            ? SpeechPenalty.WithTable(SpeechPenalty.ApplyPatched, SpeechPenalty.PatchedTableRefs, table)
            : SpeechPenalty.WithTable(SpeechPenalty.ApplyOriginal, SpeechPenalty.OriginalTableRefs, table);

        // GameCode.Exe puts the pieces at 300, each followed by 16 bytes of filler.
        private const int RollAt = 300, MarkAt = RollAt + 12 + 16, ApplyAt = MarkAt + 59 + 16;

        private static byte[] Patched(byte[] image, Site[] sites)
        {
            byte[] result = (byte[])image.Clone();
            foreach (Site site in sites)
                (site.IsPatched ? site.Original : site.Patched).CopyTo(result, site.Offset);
            return result;
        }

        [Theory]
        [InlineData(OriginalTable)]
        [InlineData(GogTable)]
        public void Find_FindsTheThreePlaces_AndTheirPatchedForm(uint table)
        {
            byte[] image = GameCode.Exe(Roll(), Mark(), Apply(table));
            Site[] sites = Speech.Find(image);

            Assert.Equal(new long[] { RollAt + 5, MarkAt + 12, ApplyAt }, sites.Select(s => s.Offset));
            Assert.All(sites, s => Assert.False(s.IsPatched));
            Assert.Equal(new byte[] { 0x33, 0xFF }, sites[0].Original);
            Assert.Equal(new byte[] { 0x33, 0xDB }, sites[0].Patched);
            Assert.Equal(40, sites[1].Patched.Length);
            Assert.Equal(Apply(table), sites[2].Original);
            Assert.Equal(Apply(table, patched: true), sites[2].Patched);
            Assert.Equal(0xB0, sites[2].Patched.Length);

            byte[] patched = Patched(image, sites);
            Assert.Equal(GameCode.Exe(Roll(true), Mark(true), Apply(table, true)), patched);
            Site[] again = Speech.Find(patched);
            Assert.All(again, s => Assert.True(s.IsPatched));
            Assert.Equal(sites.Select(s => s.Original), again.Select(s => s.Original));
            Assert.Equal(image, Patched(patched, again));
        }

        [Fact]
        public void Find_TakesAHalfPatchedExe()
        {
            Site[] sites = Speech.Find(GameCode.Exe(Roll(true), Mark(), Apply(OriginalTable, true)));

            Assert.Equal(new[] { true, false, true }, sites.Select(s => s.IsPatched));
        }

        public static IEnumerable<object[]> UnknownCode()
        {
            byte[] roll = Roll(), mark = Mark(), apply = Apply(OriginalTable);
            yield return new object[] { GameCode.Exe(mark, apply) };
            yield return new object[] { GameCode.Exe(roll, apply) };
            yield return new object[] { GameCode.Exe(roll, mark) };
            yield return new object[] { GameCode.Exe(roll, roll, mark, apply) };
            yield return new object[] { GameCode.Exe(roll, mark, Mark(true), apply) };
            yield return new object[] { GameCode.Exe(roll, mark, apply, Apply(GogTable, true)) };
            // The function reading two different tables.
            byte[] mixed = (byte[])apply.Clone();
            BitConverter.GetBytes(GogTable + 0xD).CopyTo(mixed, SpeechPenalty.OriginalTableRefs[2]);
            yield return new object[] { GameCode.Exe(roll, mark, mixed) };
            byte[] mixedPatched = Apply(OriginalTable, true);
            BitConverter.GetBytes(GogTable + 0xE).CopyTo(mixedPatched, SpeechPenalty.PatchedTableRefs[3]);
            yield return new object[] { GameCode.Exe(roll, mark, mixedPatched) };
        }

        [Theory]
        [MemberData(nameof(UnknownCode))]
        public void Find_InUnknownCode_FindsNothing(byte[] image)
        {
            Assert.Null(Speech.Find(image));
        }

        [Fact]
        public void Patterns_LocateEachPlaceOriginalAndPatched()
        {
            byte[] image = GameCode.Exe(Roll(), Mark(), Apply(OriginalTable), Roll(true), Mark(true), Apply(OriginalTable, true));

            Assert.All(Speech.Patterns, p => Assert.Single(Patch.FindAll(image, p)));
            Assert.Equal(6, Speech.Patterns.Count());
        }

        // The DLL carries the same code (the table's addresses filled in at run time).
        [Fact]
        public void EmbeddedDll_HoldsTheCode()
        {
            byte[] dll = GameInstall.DllBytes();
            if (dll == null)
                return;
            Assert.Single(Patch.FindAll(dll, Patch.Exact(SpeechPenalty.MarkPatched)));
            Assert.Single(Patch.FindAll(dll, Patch.Exact(SpeechPenalty.ApplyPatched.Select(b => b ?? 0).ToArray())));
        }

        // ---- running the code ----

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr VirtualAlloc(IntPtr address, UIntPtr size, uint type, uint protect);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool VirtualFree(IntPtr address, UIntPtr size, uint type);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void ApplySpeech(uint block, uint match, int home);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void MarkBlock(uint block, uint club, int useTaken, int works);

        // Runs body with 0x4000 bytes of executable memory.
        private static void WithMemory(Action<uint> body)
        {
            IntPtr block = VirtualAlloc(IntPtr.Zero, (UIntPtr)0x4000, 0x3000, 0x40);   // MEM_COMMIT | MEM_RESERVE, PAGE_EXECUTE_READWRITE
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
        private static short Word(uint address) => Marshal.ReadInt16(new IntPtr(address));
        private static byte Byte(uint address) => Marshal.ReadByte(new IntPtr(address));

        // Speech entries (defence %, attack %, hardness or 99, flags): +3 %, -10/+10, -5/-5 hard, +15 % only
        // while not behind, 0/0 soft.
        private static readonly sbyte[][] Entries = { new sbyte[] { 3, 3, 99, 0 }, new sbyte[] { -10, 10, 99, 0 }, new sbyte[] { -5, -5, 0, 0 }, new sbyte[] { 15, 15, 99, 1 }, new sbyte[] { 0, 0, 4, 0 } };

        // A positive number does what the game's own function does; a negative one takes the amounts of the
        // percentages off (rounded towards zero like the game), leaves the hardness, and also waits for
        // "not behind" where the speech says so. 0 does nothing.
        [Fact]
        public void PatchedFunction_DoesTheGamesEffect_AndTakesTheAmountsOffForANegativeNumber() => WithMemory(memory =>
        {
            uint table = memory, original = memory + 0x400, patched = memory + 0x500, block = memory + 0x600, match = memory + 0x800;
            byte[] entries = new byte[(Entries.Length + 1) * 0x14];
            for (int n = 1; n <= Entries.Length; n++)
            {
                sbyte[] e = Entries[n - 1];
                entries[n * 0x14 + 0xC] = (byte)e[0];
                entries[n * 0x14 + 0xD] = (byte)e[1];
                entries[n * 0x14 + 0xE] = (byte)e[2];
                entries[n * 0x14 + 0x10] = (byte)e[3];
            }
            Put(table, entries);
            Put(original, Apply(table));
            Put(patched, Apply(table, patched: true));
            ApplySpeech game = Marshal.GetDelegateForFunctionPointer<ApplySpeech>(new IntPtr(original));
            ApplySpeech patch = Marshal.GetDelegateForFunctionPointer<ApplySpeech>(new IntPtr(patched));

            string Run(ApplySpeech function, int number, short defence, short attack, int home, short homeGoals, short awayGoals)
            {
                Put(block, new byte[0x100]);
                Put(block + 0x10, BitConverter.GetBytes(defence));
                Put(block + 0x16, BitConverter.GetBytes(attack));
                Put(block + 0x31, new byte[] { 2 });
                Put(block + 0xE5, new[] { (byte)(sbyte)number });
                Put(match + 0xE, BitConverter.GetBytes(homeGoals));
                Put(match + 0x10, BitConverter.GetBytes(awayGoals));
                function(block, match, home);
                return $"{Word(block + 0x10)}/{Word(block + 0x16)}/{Byte(block + 0x31)}";
            }

            foreach (short value in new short[] { 0, 1, 7, 150, 1234, -5, 32000 })
            {
                for (int home = 0; home < 2; home++)
                {
                    foreach (short[] goals in new[] { new short[] { 0, 0 }, new short[] { 1, 0 }, new short[] { 0, 1 } })
                    {
                        for (int number = 0; number <= Entries.Length; number++)
                            Assert.Equal(Run(game, number, value, value, home, goals[0], goals[1]), Run(patch, number, value, value, home, goals[0], goals[1]));
                        for (int number = 1; number <= Entries.Length; number++)
                        {
                            sbyte[] e = Entries[number - 1];
                            bool behind = e[3] == 1 && (home == 0 ? goals[0] > goals[1] : goals[1] > goals[0]);
                            string expected = behind ? $"{value}/{value}/2"
                                : $"{(short)((100 - Math.Abs(e[0])) * value / 100)}/{(short)((100 - Math.Abs(e[1])) * value / 100)}/2";
                            Assert.Equal(expected, Run(patch, -number, value, value, home, goals[0], goals[1]));
                        }
                    }
                }
            }
        });

        // The marking after the check, in a frame as the game has it: ebx a use taken, edi it works, ebp the
        // club (+1440 the speech), the team block at [esp+17C0]. Taken and working: +E4 = 1, +E5 = number,
        // +1C = 100; taken but not working: only +E5 = -number; no use taken (or the roll failed, which now
        // clears ebx): nothing.
        [Fact]
        public void PatchedMarking_MarksAWorkingSpeech_AndOneThatDoesntFitNegative() => WithMemory(memory =>
        {
            uint code = memory, block = memory + 0x400, club = memory + 0x1000;
            Put(code, new[]
            {
                "60".Hex(),                                     // pushad
                "8B442424".Hex(), "8B6C2428".Hex(),             // mov eax,[esp+24] (block) / mov ebp,[esp+28] (club)
                "8B5C242C".Hex(), "8B7C2430".Hex(),             // mov ebx,[esp+2C] / mov edi,[esp+30]
                "81ECC4170000".Hex(), "898424C0170000".Hex(),   // sub esp,17C4 / mov [esp+17C0],eax
                SpeechPenalty.MarkPatched,
                "81C4C4170000".Hex(), "61C3".Hex(),             // add esp,17C4 / popad / ret
            }.SelectMany(p => p).ToArray());
            MarkBlock mark = Marshal.GetDelegateForFunctionPointer<MarkBlock>(new IntPtr(code));

            foreach (short number in new short[] { 1, 28, 43 })
            {
                for (int taken = 0; taken < 2; taken++)
                {
                    for (int works = 0; works < 2; works++)
                    {
                        Put(block, Enumerable.Repeat((byte)0x11, 0x100).ToArray());
                        Put(club + 0x1440, BitConverter.GetBytes(number));
                        mark(block, club, taken, works);
                        string expected = taken == 0 ? "17/17/4369" : works == 1 ? $"1/{number}/100" : $"17/{-number}/4369";
                        Assert.Equal(expected, $"{Byte(block + 0xE4)}/{(sbyte)Byte(block + 0xE5)}/{Word(block + 0x1C)}");
                    }
                }
            }
        });
    }
}
