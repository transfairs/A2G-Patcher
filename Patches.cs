using System;
using System.Collections.Generic;
using System.Linq;

namespace A2GPatch
{
    /// <summary>One rewritable place in the game's code: where it starts, and both versions of its bytes.</summary>
    internal sealed class Site
    {
        public long Offset;
        public byte[] Original, Patched;
        public bool IsPatched;
    }

    /// <summary>
    /// A change to the game's code. Each finds its places by the instructions around them, in the exe
    /// on disk (original, GOG) or in memory (the packed 2007 CD exe, see dll/msvfw32.c, which repeats
    /// both patches in C). A place must occur exactly once and hold either the original or the
    /// patched instructions; otherwise the patch isn't offered.
    /// </summary>
    internal abstract class Patch
    {
        /// <summary>Every patch, in the order the window lists them.</summary>
        public static readonly Patch[] All =
        {
            new GoalLimits(), new ArrowRepeat(), new VideoCrash(), new SleepFreeze(), new DefaultSoundDevice(), new DiskSpaceWarning(),
            new SquadLimit(), new DisplayModeRestore(),
        };

        /// <summary>What the window's switch says.</summary>
        public abstract string Name { get; }

        /// <summary>Its bit in the flags of msvfw32.dll (A2GPatchFlags in dll/msvfw32.c).</summary>
        public abstract byte Flag { get; }

        /// <summary>The patterns that locate the patch; none of them in an exe means its code isn't readable there (packed).</summary>
        public abstract IEnumerable<byte?[]> Patterns { get; }

        /// <summary>The places in <paramref name="image"/> with their current state, or null if they aren't as expected.</summary>
        public abstract Site[] Find(byte[] image);

        /// <summary>Every position where <paramref name="pattern"/> starts in <paramref name="bytes"/>; null entries match any byte.</summary>
        internal static List<long> FindAll(byte[] bytes, byte?[] pattern)
        {
            List<long> found = new List<long>();
            byte first = pattern[0].Value;
            for (int i = 0; i + pattern.Length <= bytes.Length; i++)
            {
                if (bytes[i] != first)
                    continue;
                int j = 1;
                while (j < pattern.Length && (pattern[j] == null || bytes[i + j] == pattern[j].Value))
                    j++;
                if (j == pattern.Length)
                    found.Add(i);
            }
            return found;
        }

        /// <summary>The bytes as a pattern without wildcards.</summary>
        internal static byte?[] Exact(params byte[] bytes) => bytes.Select(b => (byte?)b).ToArray();
    }

    /// <summary>
    /// Lifts the match engine's goal limits: a team scores only while it has fewer than 6 goals up to
    /// minute 45 and fewer than 10 after, and from 7 goals on its chances are halved. The patch raises
    /// the limit to 15 (the most a result nibble holds) and skips the halving. The same change as the
    /// trainer's GoalLimitPatch (A2G-Trainer, Controller/GoalLimitBytes.cs):
    /// <code>
    ///   limit:  setg r8 / dec r32 / and r, -4 / add r, 10   ->  setg r8 / mov r32, 15 / nop...
    ///   brakes: jle (7E)                                     ->  jmp (EB)
    /// </code>
    /// </summary>
    internal sealed class GoalLimits : Patch
    {
        internal const short MaxGoals = 15;
        internal const byte Jle = 0x7E;
        internal const byte Jmp = 0xEB;
        private const byte Nop = 0x90;

        // The instructions right before each rewritten byte: cmp word [clock+8], 45 ... setg; cmp word
        // [esi+0xE], 6 / mov [esp+0x30], eax; mov dx, [esi+0x10] / add esp, 4 / cmp dx, 6 / mov ecx, eax.
        internal static readonly byte[] LimitPattern     = { 0x66, 0x83, 0x7F, 0x08, 0x2D, 0x57, 0x6A, 0x01, 0x0F, 0x9F };
        internal static readonly byte[] HomeBrakePattern = { 0x66, 0x83, 0x7E, 0x0E, 0x06, 0x89, 0x44, 0x24, 0x30 };
        internal static readonly byte[] AwayBrakePattern = { 0x66, 0x8B, 0x56, 0x10, 0x83, 0xC4, 0x04, 0x66, 0x83, 0xFA, 0x06, 0x8B, 0xC8 };

        /// <inheritdoc/>
        public override string Name => "Torgrenzen aufheben";
        /// <inheritdoc/>
        public override byte Flag => 1;
        /// <inheritdoc/>
        public override IEnumerable<byte?[]> Patterns => new[] { Exact(LimitPattern), Exact(HomeBrakePattern), Exact(AwayBrakePattern) };

        /// <inheritdoc/>
        public override Site[] Find(byte[] image)
        {
            long? limit = After(image, LimitPattern), home = After(image, HomeBrakePattern), away = After(image, AwayBrakePattern);
            if (!limit.HasValue || !home.HasValue || !away.HasValue)
                return null;
            Site[] sites = { Limit(image, limit.Value), Jump(image, home.Value), Jump(image, away.Value) };
            return sites.Contains(null) ? null : sites;
        }

        // The position right after the pattern's only occurrence.
        private static long? After(byte[] image, byte[] pattern)
        {
            List<long> found = FindAll(image, Exact(pattern));
            return found.Count == 1 ? found[0] + pattern.Length : (long?)null;
        }

        private static Site Jump(byte[] image, long at)
        {
            byte jump = at < image.Length ? image[at] : (byte)0;
            return jump != Jle && jump != Jmp ? null : new Site { Offset = at, Original = new[] { Jle }, Patched = new[] { Jmp }, IsPatched = jump == Jmp };
        }

        // at is setg's ModR/M byte (C0+r); the rewritten instructions follow it.
        private static Site Limit(byte[] image, long at)
        {
            if (at + 8 > image.Length || image[at] < 0xC0 || image[at] > 0xC3)
                return null;
            int register = image[at] - 0xC0;
            foreach (byte[] original in LimitOriginals(register))
            {
                byte[] patched = LimitPatched(register, original.Length);
                foreach (byte[] candidate in new[] { original, patched })
                {
                    if (image.Skip((int)at + 1).Take(candidate.Length).SequenceEqual(candidate))
                        return new Site { Offset = at + 1, Original = original, Patched = patched, IsPatched = candidate == patched };
                }
            }
            return null;
        }

        // dec r32 / and r, -4 / add r, 10 - with eax, the compiler may use the short "and al, -4". The
        // long form comes first: patched, the short form is a prefix of it.
        internal static byte[][] LimitOriginals(int register)
        {
            byte[] add = { 0x83, (byte)(0xC0 + register), 0x0A };
            byte[] longForm = new byte[] { (byte)(0x48 + register), 0x83, (byte)(0xE0 + register), 0xFC }.Concat(add).ToArray();
            return register == 0
                ? new[] { longForm, new byte[] { 0x48, 0x24, 0xFC }.Concat(add).ToArray() }
                : new[] { longForm };
        }

        // mov r32, 15, padded with nops to the original's length.
        internal static byte[] LimitPatched(int register, int length) =>
            new[] { (byte)(0xB8 + register) }.Concat(BitConverter.GetBytes((int)MaxGoals)).Concat(Enumerable.Repeat(Nop, length - 5)).ToArray();
    }

    /// <summary>
    /// Makes the game's arrow buttons keep counting while held down (the bug the "Windows 98"
    /// compatibility mode works around). The buttons' repeat thread (FUN_005c6130 in the original)
    /// posts a step to the window only while no step is pending (+0xF4 == 0), and marks one pending
    /// only after posting, by reading the counter again. On Windows NT the woken window thread
    /// often handles the step first (its "decrement if pending" finds 0), then the thread writes 1:
    /// the counter stays 1 and the thread never posts again. Windows 98 mode's DisableBoostThread
    /// shim hides this. The patch marks the step pending before posting:
    /// <code>
    ///   mov eax, [msg] / mov ecx, [esi+1C] / push 0 / push 0 / push eax / push ecx / call ebx (PostMessageA)
    ///   mov eax, [esi+F4] / mov [esi+F0], 60 / inc eax / mov [esi+F4], eax / jmp next
    /// ->
    ///   mov [esi+F4], 1 / mov [esi+F0], 60
    ///   mov eax, [msg] / mov ecx, [esi+1C] / push 0 / push 0 / push eax / push ecx / call ebx
    ///   nop / nop / nop / jmp next
    /// </code>
    /// The branch is only taken with +0xF4 == 0, so "mov 1" is the increment. msg is the address of
    /// the registered message's ID, which differs between the builds.
    /// </summary>
    internal sealed class ArrowRepeat : Patch
    {
        // mov eax, [esi+F4] / test eax, eax / jne +29: the check before the rewritten branch.
        private static readonly byte[] Check = { 0x8B, 0x86, 0xF4, 0x00, 0x00, 0x00, 0x85, 0xC0, 0x75, 0x29 };
        private static readonly byte[] PostTail = { 0x8B, 0x4E, 0x1C, 0x6A, 0x00, 0x6A, 0x00, 0x50, 0x51, 0xFF, 0xD3 };
        private static readonly byte[] Pending = { 0xC7, 0x86, 0xF4, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00 };
        private static readonly byte[] Delay = { 0xC7, 0x86, 0xF0, 0x00, 0x00, 0x00, 0x3C, 0x00, 0x00, 0x00 };
        private static readonly byte[] Increment = { 0x8B, 0x86, 0xF4, 0x00, 0x00, 0x00 }; // then Delay, inc eax, store
        private static readonly byte[] Store = { 0x40, 0x89, 0x86, 0xF4, 0x00, 0x00, 0x00 };
        private static readonly byte[] JumpNext = { 0xEB, 0x0A };

        /// <summary>The rewritten branch as the game has it, for the message variable at <paramref name="message"/>.</summary>
        internal static byte[] OriginalBranch(byte[] message) =>
            new byte[] { 0xA1 }.Concat(message).Concat(PostTail).Concat(Increment).Concat(Delay).Concat(Store).Concat(JumpNext).ToArray();

        /// <summary>The same branch, patched (same length).</summary>
        internal static byte[] PatchedBranch(byte[] message) =>
            Pending.Concat(Delay).Concat(new byte[] { 0xA1 }).Concat(message).Concat(PostTail).Concat(new byte[] { 0x90, 0x90, 0x90 }).Concat(JumpNext).ToArray();

        // Where the message address sits in check + branch: right after "mov eax, [msg]"'s A1.
        private static readonly int OriginalMessageAt = Check.Length + 1;
        private static readonly int PatchedMessageAt = Check.Length + Pending.Length + Delay.Length + 1;

        // The check followed by a branch, the message address left open.
        private static byte?[] Pattern(byte[] branch, int messageAt) =>
            Exact(Check.Concat(branch).ToArray()).Select((b, i) => i >= messageAt && i < messageAt + 4 ? null : b).ToArray();

        private static readonly byte?[] OriginalPattern = Pattern(OriginalBranch(new byte[4]), OriginalMessageAt);
        private static readonly byte?[] PatchedPattern = Pattern(PatchedBranch(new byte[4]), PatchedMessageAt);

        /// <inheritdoc/>
        public override string Name => "Pfeile zählen beim Gedrückthalten weiter";
        /// <inheritdoc/>
        public override byte Flag => 2;
        /// <inheritdoc/>
        public override IEnumerable<byte?[]> Patterns => new[] { OriginalPattern, PatchedPattern };

        /// <inheritdoc/>
        public override Site[] Find(byte[] image)
        {
            List<long> original = FindAll(image, OriginalPattern), patched = FindAll(image, PatchedPattern);
            if (original.Count + patched.Count != 1)
                return null;
            bool isPatched = patched.Count == 1;
            long at = isPatched ? patched[0] : original[0];
            byte[] message = image.Skip((int)at + (isPatched ? PatchedMessageAt : OriginalMessageAt)).Take(4).ToArray();
            return new[] { new Site { Offset = at + Check.Length, Original = OriginalBranch(message), Patched = PatchedBranch(message), IsPatched = isPatched } };
        }
    }

    /// <summary>
    /// Keeps the game from crashing at the World Cup, European Championship and final videos
    /// (WM.AVI, EM.AVI, FINALE.AVI). They are Indeo 3.2, which current Windows can't decode:
    /// AVIStreamGetFrameOpen then returns NULL, the game doesn't check it, and the player
    /// (FUN_00418cd0 in the original) reads the first frame's size through a NULL pointer. The patch
    /// checks the result and, on NULL, takes the way the function already takes when
    /// AVIFileGetStream fails: release the file, return 0. The video is skipped, as when the file is
    /// missing. Room for the check comes from encoding the BITMAPINFOHEADER setup before the call
    /// more tightly (edi = this+0x8494 is the stream, so +0x84D4/+0x84D8/+0x848C are edi+40/+44/-8,
    /// and planes and bit count are one dword store):
    /// <code>
    ///   ... mov ecx,[esi+84D4] / ... mov edx,[esi+84D8] / ... mov word [eax+C],1 / mov word [eax+E],8 / ...
    ///   mov eax,[edi] / push eax / call AVIStreamGetFrameOpen / mov [esi+848C],eax
    /// ->
    ///   ... mov ecx,[edi+40] / ... mov ecx,[edi+44] / ... mov dword [eax+C],80001 / ...
    ///   push [edi] / call AVIStreamGetFrameOpen / test eax,eax / jnz ok
    ///   mov [edi],eax / jmp release   ; stream field 0, as the destructor and FUN_00419040 expect
    ///   ok: mov [edi-8],eax / nop x4
    /// </code>
    /// The pattern includes the AVIFileGetStream failure path right before, so the jump back to it
    /// is the same in every build; only the call's displacement differs and is taken over.
    /// </summary>
    internal sealed class VideoCrash : Patch
    {
        // test eax,eax / je +21 / release: mov ecx,[ebp] / push ecx / call AVIFileRelease / lea ecx,[esp+10] /
        // call / xor eax,eax / pop edi,esi,ebp,ebx / add esp,84 / ret C / mov eax,[edi] / lea edx,[esi+8498] /
        // push 8C / push edx / push eax / call AVIStreamInfoA
        internal static readonly byte?[] Prefix =
        {
            0x85, 0xC0, 0x74, 0x21, 0x8B, 0x4D, 0x00, 0x51, 0xE8, null, null, null, null,
            0x8D, 0x4C, 0x24, 0x10, 0xE8, null, null, null, null, 0x33, 0xC0, 0x5F, 0x5E, 0x5D, 0x5B,
            0x81, 0xC4, 0x84, 0x00, 0x00, 0x00, 0xC2, 0x0C, 0x00,
            0x8B, 0x07, 0x8D, 0x96, 0x98, 0x84, 0x00, 0x00, 0x68, 0x8C, 0x00, 0x00, 0x00, 0x52, 0x50, 0xE8, null, null, null, null,
        };
        // Where "release" starts in the prefix.
        private const int Release = 4;

        // lea eax,[esi+8064] / push eax / mov [eax],28 / mov ecx,[esi+84D4] / mov [eax+4],ecx /
        // mov edx,[esi+84D8] / mov [eax+8],edx / mov word [eax+C],1 / mov word [eax+E],8 / mov [eax+10..24],ebx /
        // mov eax,[edi] / push eax / call AVIStreamGetFrameOpen / mov [esi+848C],eax
        private static readonly byte[] OriginalCode =
        {
            0x8D, 0x86, 0x64, 0x80, 0x00, 0x00, 0x50, 0xC7, 0x00, 0x28, 0x00, 0x00, 0x00,
            0x8B, 0x8E, 0xD4, 0x84, 0x00, 0x00, 0x89, 0x48, 0x04, 0x8B, 0x96, 0xD8, 0x84, 0x00, 0x00, 0x89, 0x50, 0x08,
            0x66, 0xC7, 0x40, 0x0C, 0x01, 0x00, 0x66, 0xC7, 0x40, 0x0E, 0x08, 0x00,
            0x89, 0x58, 0x10, 0x89, 0x58, 0x14, 0x89, 0x58, 0x18, 0x89, 0x58, 0x1C, 0x89, 0x58, 0x20, 0x89, 0x58, 0x24,
            0x8B, 0x07, 0x50, 0xE8, 0x00, 0x00, 0x00, 0x00, 0x89, 0x86, 0x8C, 0x84, 0x00, 0x00,
        };
        private const int OriginalCall = 65;   // the call's displacement in OriginalCode

        private static readonly byte[] PatchedCode =
        {
            0x8D, 0x86, 0x64, 0x80, 0x00, 0x00, 0x50, 0xC7, 0x00, 0x28, 0x00, 0x00, 0x00,
            0x8B, 0x4F, 0x40, 0x89, 0x48, 0x04, 0x8B, 0x4F, 0x44, 0x89, 0x48, 0x08,      // mov ecx,[edi+40/44]
            0xC7, 0x40, 0x0C, 0x01, 0x00, 0x08, 0x00,                                   // planes 1, bit count 8
            0x89, 0x58, 0x10, 0x89, 0x58, 0x14, 0x89, 0x58, 0x18, 0x89, 0x58, 0x1C, 0x89, 0x58, 0x20, 0x89, 0x58, 0x24,
            0xFF, 0x37, 0xE8, 0x00, 0x00, 0x00, 0x00,                                   // push [edi] / call
            0x85, 0xC0, 0x75, 0x07,                                                     // test eax,eax / jnz ok
            0x89, 0x07, 0xE9, 0x00, 0x00, 0x00, 0x00,                                   // mov [edi],eax / jmp release
            0x89, 0x47, 0xF8, 0x90, 0x90, 0x90, 0x90,                                   // ok: mov [edi-8],eax
        };
        private const int PatchedCall = 53;
        private const int PatchedJump = 64;

        // The call moves this much towards the start, so its displacement grows by as much.
        private const int CallShift = (OriginalCall + 4) - (PatchedCall + 4);

        /// <summary>The original code for the call displacement <paramref name="call"/>.</summary>
        internal static byte[] OriginalBytes(int call)
        {
            byte[] code = (byte[])OriginalCode.Clone();
            BitConverter.GetBytes(call).CopyTo(code, OriginalCall);
            return code;
        }

        /// <summary>The patched code (same length) for the original's call displacement <paramref name="call"/>.</summary>
        internal static byte[] PatchedBytes(int call)
        {
            byte[] code = (byte[])PatchedCode.Clone();
            BitConverter.GetBytes(call + CallShift).CopyTo(code, PatchedCall);
            // Back to "release" in the prefix, from the end of the jump.
            BitConverter.GetBytes(Release - (Prefix.Length + PatchedJump + 4)).CopyTo(code, PatchedJump);
            return code;
        }

        // The prefix followed by code, the call's displacement left open.
        private static byte?[] Pattern(byte[] code, int call) =>
            Prefix.Concat(Exact(code).Select((b, i) => i >= call && i < call + 4 ? null : b)).ToArray();

        private static readonly byte?[] OriginalPattern = Pattern(OriginalBytes(0), OriginalCall);
        private static readonly byte?[] PatchedPattern = Pattern(PatchedBytes(0), PatchedCall);

        /// <inheritdoc/>
        public override string Name => "Nicht abspielbare Videos (WM, EM, Finale) überspringen statt abzustürzen";
        /// <inheritdoc/>
        public override byte Flag => 4;
        /// <inheritdoc/>
        public override IEnumerable<byte?[]> Patterns => new[] { OriginalPattern, PatchedPattern };

        /// <inheritdoc/>
        public override Site[] Find(byte[] image)
        {
            List<long> original = FindAll(image, OriginalPattern), patched = FindAll(image, PatchedPattern);
            if (original.Count + patched.Count != 1)
                return null;
            bool isPatched = patched.Count == 1;
            long at = (isPatched ? patched[0] : original[0]) + Prefix.Length;
            int call = BitConverter.ToInt32(image, (int)at + (isPatched ? PatchedCall : OriginalCall)) - (isPatched ? CallShift : 0);
            return new[] { new Site { Offset = at, Original = OriginalBytes(call), Patched = PatchedBytes(call), IsPatched = isPatched } };
        }
    }

    /// <summary>
    /// A patch that changes one byte (a jump's opcode) in code that occurs a known number of times.
    /// Each occurrence must hold the original or the patched byte; mixed states are fine (applying
    /// finishes them).
    /// </summary>
    internal abstract class OpcodePatch : Patch
    {
        /// <summary>The code around the byte, with the original byte at <see cref="At"/>.</summary>
        protected abstract byte?[] Code { get; }
        /// <summary>Where the changed byte is in <see cref="Code"/>.</summary>
        protected abstract int At { get; }
        /// <summary>The byte the patch writes.</summary>
        internal abstract byte PatchedByte { get; }
        /// <summary>How often the code occurs in the game.</summary>
        protected virtual int Occurrences => 1;

        private byte?[] With(byte value)
        {
            byte?[] code = (byte?[])this.Code.Clone();
            code[this.At] = value;
            return code;
        }

        /// <inheritdoc/>
        public override IEnumerable<byte?[]> Patterns => new[] { this.Code, this.With(this.PatchedByte) };

        /// <inheritdoc/>
        public override Site[] Find(byte[] image)
        {
            byte original = this.Code[this.At].Value;
            List<Site> sites = FindAll(image, this.Code).Select(at => MakeSite(at, false))
                .Concat(FindAll(image, this.With(this.PatchedByte)).Select(at => MakeSite(at, true)))
                .OrderBy(s => s.Offset).ToList();
            return sites.Count == this.Occurrences ? sites.ToArray() : null;

            Site MakeSite(long at, bool patched) =>
                new Site { Offset = at + this.At, Original = new[] { original }, Patched = new[] { this.PatchedByte }, IsPatched = patched };
        }
    }

    /// <summary>
    /// Keeps the game from freezing once Windows has been running for more than 24.9 days. A reply
    /// sequence (FUN_00598960 in the original, twice) waits up to 3 seconds since a timestamp, but
    /// overwrites the timestamp with the difference; reached again without a fresh one, the
    /// "difference" is about timeGetTime(), which as an int is negative after 2^31 ms. Then
    /// Sleep(3000 - negative) sleeps for weeks. The patch makes the comparison unsigned:
    /// <code>
    ///   sub eax,[ebp+17C] / cmp eax,0BB8 / mov [ebp+17C],eax / jge skip (7D)  ->  jae skip (73)
    /// </code>
    /// 0 to 2999 ms wait as before; a "negative" difference counts as large and doesn't wait.
    /// </summary>
    internal sealed class SleepFreeze : OpcodePatch
    {
        /// <inheritdoc/>
        protected override byte?[] Code => Exact(0x2B, 0x85, 0x7C, 0x01, 0x00, 0x00, 0x3D, 0xB8, 0x0B, 0x00, 0x00, 0x89, 0x85, 0x7C, 0x01, 0x00, 0x00, 0x7D, 0x0E);
        /// <inheritdoc/>
        protected override int At => 17;
        /// <inheritdoc/>
        internal override byte PatchedByte => 0x73;
        /// <inheritdoc/>
        protected override int Occurrences => 2;
        /// <inheritdoc/>
        public override string Name => "Kein Einfrieren nach über 24 Tagen Windows-Laufzeit";
        /// <inheritdoc/>
        public override byte Flag => 8;
    }

    /// <summary>
    /// Stops the "disk almost full" warning on saving (text 2753) from coming up for no reason. The
    /// game multiplies GetDiskFreeSpaceA's values in 32 bits, so on today's disks it sees the free
    /// space modulo 4 GB (FUN_005ef380 in the original). The patch skips the warning:
    /// <code>
    ///   imul eax,[esp+x] / cmp eax,177000 / jae ok (73)  ->  jmp ok (EB)
    /// </code>
    /// The message when the file can't be written stays.
    /// </summary>
    internal sealed class DiskSpaceWarning : OpcodePatch
    {
        // imul eax,[esp+?] / cmp eax,177000 / jae ? / lea ecx,[esp+?] / push 0AC1 (the warning's text)
        /// <inheritdoc/>
        protected override byte?[] Code => new byte?[] { 0x0F, 0xAF, 0x44, 0x24, null, 0x3D, 0x00, 0x70, 0x17, 0x00, 0x73, null, 0x8D, 0x4C, 0x24, null, 0x68, 0xC1, 0x0A, 0x00, 0x00 };
        /// <inheritdoc/>
        protected override int At => 10;
        /// <inheritdoc/>
        internal override byte PatchedByte => 0xEB;
        /// <inheritdoc/>
        public override string Name => "Keine falsche Warnung „Festplatte fast voll“ beim Speichern";
        /// <inheritdoc/>
        public override byte Flag => 32;
    }

    /// <summary>
    /// Keeps a club's squad from growing past 30 players, the size of its player array (club+0x990,
    /// followed by the per-position counters at +0xA08, which a 31st player overwrites). The game asks
    /// "squad full?" (FUN_005fcbb0 in the original) with a forecast: the count +0xE6 minus pending
    /// departures plus pending arrivals, where every loan counts as a departure until it ends. A club
    /// with 30 players and players on loan therefore isn't "full" and gets more, e.g. through AI
    /// transfers, loans and youth promotion (FUN_004131b0). The patch also calls it full at 30 actual
    /// players, in the same 32 bytes (the call keeps its place, so its displacement stays):
    /// <code>
    ///   push [esp+4] / call forecast / add esp,4 / xor ecx,ecx / cmp ax,1E / setge cl / mov eax,ecx / ret / nop x7
    /// ->
    ///   push [esp+4] / call forecast / pop ecx / cmp ax,1E / jge full / cmp word [ecx+E6],1E
    ///   full: setge al / movzx eax,al / ret
    /// </code>
    /// All callers handle "full" before anyone leaves another club. Side effect: a club at 30 with a
    /// sale still pending (late in the season) can only buy once the sale is done.
    /// </summary>
    internal sealed class SquadLimit : RewritePatch
    {
        /// <inheritdoc/>
        internal override byte?[] OriginalCode => new byte?[]
        {
            0x8B, 0x44, 0x24, 0x04, 0x50, 0xE8, null, null, null, null, 0x83, 0xC4, 0x04, 0x33, 0xC9,
            0x66, 0x3D, 0x1E, 0x00, 0x0F, 0x9D, 0xC1, 0x8B, 0xC1, 0xC3, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90,
        };
        /// <inheritdoc/>
        internal override byte?[] PatchedCode => new byte?[]
        {
            0x8B, 0x44, 0x24, 0x04, 0x50, 0xE8, null, null, null, null, 0x59,
            0x66, 0x3D, 0x1E, 0x00, 0x7D, 0x08, 0x66, 0x83, 0xB9, 0xE6, 0x00, 0x00, 0x00, 0x1E,
            0x0F, 0x9D, 0xC0, 0x0F, 0xB6, 0xC0, 0xC3,
        };

        /// <inheritdoc/>
        public override string Name => "Kader nicht über 30 Spieler wachsen lassen";
        /// <inheritdoc/>
        public override byte Flag => 64;
    }

    /// <summary>
    /// Gives the desktop its resolution back when the game ends, crashes included. The game switches
    /// to 640x480 with ChangeDisplaySettingsA(&amp;mode, 0) (FUN_0040faa0 in the original), a change that
    /// outlives the process; only its normal exit switches back. With CDS_FULLSCREEN (4) Windows takes
    /// the mode back by itself when the game ends. The room is an instruction that clears a DEVMODE
    /// field "rep stosd" has already cleared; the flags were pushed first (push ebx = 0 / push edx), so
    /// they are at [esp+4]:
    /// <code>
    ///   mov [esi+1C8],ebx   ->   mov byte [esp+4],4 / nop
    /// </code>
    /// The GOG build already does this itself (its own code before the call sets the flags to 4), so
    /// there the patch finds nothing.
    /// </summary>
    internal sealed class DisplayModeRestore : RewritePatch
    {
        private static byte?[] Code(params byte[] clearOrFlags) =>
            new byte?[]
            {
                0xF3, 0xAB, 0xBF, 0xE0, 0x01, 0x00, 0x00, 0x53, 0x52,                     // rep stosd / mov edi,1E0 / push ebx / push edx
                0x66, 0xC7, 0x86, 0x74, 0x01, 0x00, 0x00, 0x9C, 0x00,                     // dmSize
                0x66, 0x89, 0x9E, 0x76, 0x01, 0x00, 0x00,                                 // dmDriverExtra
                0xC7, 0x86, 0x78, 0x01, 0x00, 0x00, 0x00, 0x00, 0x5C, 0x00,               // dmFields
                0xC7, 0x86, 0xBC, 0x01, 0x00, 0x00, 0x80, 0x02, 0x00, 0x00,               // 640
                0x89, 0xBE, 0xC0, 0x01, 0x00, 0x00,                                       // 480
                0xC7, 0x86, 0xB8, 0x01, 0x00, 0x00, 0x10, 0x00, 0x00, 0x00,               // 16 bit
            }
            .Concat(clearOrFlags.Select(b => (byte?)b))
            .Concat(new byte?[] { 0xFF, 0x15, null, null, null, null })                   // call [ChangeDisplaySettingsA]
            .ToArray();

        /// <inheritdoc/>
        internal override byte?[] OriginalCode => Code(0x89, 0x9E, 0xC8, 0x01, 0x00, 0x00);     // mov [esi+1C8],ebx
        /// <inheritdoc/>
        internal override byte?[] PatchedCode => Code(0xC6, 0x44, 0x24, 0x04, 0x04, 0x90);      // mov byte [esp+4],4 / nop

        /// <inheritdoc/>
        public override string Name => "Desktop-Auflösung auch nach einem Absturz zurückholen";
        /// <inheritdoc/>
        public override byte Flag => 128;
    }

    /// <summary>
    /// A patch that rewrites one block of code that occurs exactly once. Original and patched code have
    /// the same length and wildcards (null) at the same places; those bytes (addresses, displacements)
    /// are kept from the game.
    /// </summary>
    internal abstract class RewritePatch : Patch
    {
        /// <summary>The block as the game has it.</summary>
        internal abstract byte?[] OriginalCode { get; }
        /// <summary>The block patched.</summary>
        internal abstract byte?[] PatchedCode { get; }

        /// <inheritdoc/>
        public override IEnumerable<byte?[]> Patterns => new[] { this.OriginalCode, this.PatchedCode };

        /// <inheritdoc/>
        public override Site[] Find(byte[] image)
        {
            byte?[] originalCode = this.OriginalCode, patchedCode = this.PatchedCode;
            List<long> original = FindAll(image, originalCode), patched = FindAll(image, patchedCode);
            if (original.Count + patched.Count != 1)
                return null;
            bool isPatched = patched.Count == 1;
            long at = isPatched ? patched[0] : original[0];
            byte[] Fill(byte?[] code) => code.Select((b, i) => b ?? image[at + i]).ToArray();
            return new[] { new Site { Offset = at, Original = Fill(originalCode), Patched = Fill(patchedCode), IsPatched = isPatched } };
        }
    }

    /// <summary>
    /// Plays the sound on Windows' default audio device. The game enumerates the devices and keeps
    /// the one with the most 3D hardware buffers (FUN_00653390 in the original), but since Vista every
    /// device reports 0, and on a tie the last one wins (the "primary" entry with GUID NULL is
    /// skipped). The patch passes NULL instead of the kept GUID to DirectSoundCreate (FUN_00653440),
    /// which means the default device; the GUID isn't used anywhere else.
    /// <code>
    ///   push esi / push &amp;ds / push &amp;guid / call DirectSoundCreate ... mov [&amp;ds],esi
    ///   ->        push esi    / nop x4   (esi is 0 there, as the first push shows)
    /// </code>
    /// The kept GUID lies 0x18 bytes before the DirectSound pointer in every build, so the original
    /// instruction can be rebuilt from the patched code.
    /// </summary>
    internal sealed class DefaultSoundDevice : Patch
    {
        private const int Guid = 6;               // where "push &guid" starts
        private const int GuidBeforePointer = 0x18;
        private static readonly byte[] Patched = { 0x56, 0x90, 0x90, 0x90, 0x90 };

        // push esi / push &ds / {push &guid | push esi, nop x4} / call / test eax,eax / je +15 / call / mov [&ds],esi
        private static byte?[] Pattern(byte?[] guid) =>
            new byte?[] { 0x56, 0x68, null, null, null, null }.Concat(guid)
                .Concat(new byte?[] { 0xE8, null, null, null, null, 0x85, 0xC0, 0x74, 0x15, 0xE8, null, null, null, null, 0x89, 0x35, null, null, null, null })
                .ToArray();

        private static readonly byte?[] OriginalPattern = Pattern(new byte?[] { 0x68, null, null, null, null });
        private static readonly byte?[] PatchedPattern = Pattern(Exact(Patched));

        /// <inheritdoc/>
        public override string Name => "Ton über das Standard-Audiogerät von Windows";
        /// <inheritdoc/>
        public override byte Flag => 16;
        /// <inheritdoc/>
        public override IEnumerable<byte?[]> Patterns => new[] { OriginalPattern, PatchedPattern };

        /// <summary>"push &amp;guid" for the DirectSound pointer at <paramref name="pointer"/>.</summary>
        internal static byte[] PushGuid(int pointer) => new byte[] { 0x68 }.Concat(BitConverter.GetBytes(pointer - GuidBeforePointer)).ToArray();

        /// <inheritdoc/>
        public override Site[] Find(byte[] image)
        {
            List<long> original = FindAll(image, OriginalPattern), patched = FindAll(image, PatchedPattern);
            if (original.Count + patched.Count != 1)
                return null;
            bool isPatched = patched.Count == 1;
            int at = (int)(isPatched ? patched[0] : original[0]);
            int pointer = BitConverter.ToInt32(image, at + 2);
            // Both mentions of the pointer must agree, and the GUID must sit where it's expected.
            if (BitConverter.ToInt32(image, at + 27) != pointer)
                return null;
            byte[] push = PushGuid(pointer);
            if (!isPatched && !image.Skip(at + Guid).Take(push.Length).SequenceEqual(push))
                return null;
            return new[] { new Site { Offset = at + Guid, Original = push, Patched = (byte[])Patched.Clone(), IsPatched = isPatched } };
        }
    }
}
