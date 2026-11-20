using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace A2GPatch
{
    /// <summary>One rewritable place in the game's code: where it starts, and both versions of its bytes.</summary>
    internal sealed class Site
    {
        /// <summary>Where the place starts in the image.</summary>
        public long Offset;
        /// <summary>Its bytes original and patched.</summary>
        public byte[] Original, Patched;
        /// <summary>Whether it holds the patched bytes now.</summary>
        public bool IsPatched;
    }

    /// <summary>The groups of tiles in the window, in this order.</summary>
    internal enum PatchGroup
    {
        /// <summary>Fixes: the game's plain bugs (one switch) and fixes for particular setups.</summary>
        Patches,
        /// <summary>Changes to how the game plays, each a matter of taste.</summary>
        Balancing,
        /// <summary>Only for GOG's release.</summary>
        Gog,
    }

    /// <summary>
    /// A change to the game's code. Each finds its places by the instructions around them, in the exe
    /// on disk (original, GOG) or in memory (the packed 2007 CD exe, see dll/msvfw32.c, which repeats
    /// both patches in C). A place must occur exactly once and hold either the original or the
    /// patched instructions; otherwise the patch isn't offered.
    /// </summary>
    internal abstract class Patch
    {
        /// <summary>Every switch, in the order the window lists them; the game's bugs are one (<see cref="GameBugs"/>).</summary>
        public static readonly Patch[] All =
        {
            new GameBugs(), new Patch101(), new VideoCrash(), new DefaultSoundDevice(), new MoneyLimit(),
            new NegotiationCounter(), new InterviewEffects(), new GoalLimits(), new SpeechPenalty(), new CoachSupply(), new FishPoisoning(), new ManagerEvents(),
            new CopyProtection(), new DialogStyles(), new AnimationEdge(), new WrapperClipping(),
        };

        /// <summary>The patch of type <typeparamref name="T"/>, a switch of its own or one of the game's bugs.</summary>
        internal static T Get<T>() where T : Patch =>
            All.Concat(All.OfType<GameBugs>().SelectMany(b => b.Members)).OfType<T>().Single();

        /// <summary>What the patch does, in a sentence: the text under its tile in the window.</summary>
        public abstract string Name { get; }

        /// <summary>The tile's heading in the window, a few words.</summary>
        public abstract string Title { get; }

        /// <summary>The group the window shows the tile in.</summary>
        public abstract PatchGroup Group { get; }

        /// <summary>Its bits in the four flags bytes of msvfw32.dll (A2GPatchFlags in dll/msvfw32.c, 1 to 67108864 taken); 0 for the GOG patches the DLL doesn't know.</summary>
        public abstract uint Flag { get; }

        /// <summary>Whether places found as <paramref name="sites"/> count as applied: all of them patched.</summary>
        internal virtual bool IsApplied(Site[] sites) => sites.All(s => s.IsPatched);

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

        /// <summary>Where the call or jump (E8, E9 with a 32-bit displacement) at <paramref name="at"/> leads.</summary>
        internal static long Target(byte[] image, long at) => at + 5 + BitConverter.ToInt32(image, (int)at + 1);
    }

    /// <summary>
    /// The game's plain bugs, fixed under one switch. Each stays a patch of its own
    /// with its own places and DLL flag; this one finds the places of those the exe has (the GOG build,
    /// for one, already restores the display mode itself) and switches them together. It counts as
    /// applied as soon as one of them is, so an exe that has some of them from an older version keeps
    /// them, and applying completes the rest.
    /// </summary>
    internal sealed class GameBugs : Patch
    {
        /// <summary>The bugs, in the order the tile names them.</summary>
        internal readonly Patch[] Members =
        {
            new InvestmentBalance(), new EventEffects(), new EventCrashes(), new RefereeGrudge(), new CompetenceBalance(), new GradeAverage(), new GradeChartLabel(), new AppearancesList(), new SquadLimit(), new CupDraw(), new PictureButtonInit(), new CalculationInit(),
            new DiskSpaceWarning(), new ArrowRepeat(), new DisplayModeRestore(), new SleepFreeze(),
        };

        /// <inheritdoc/>
        // Fixes of one subject share a title, named once.
        public override string Name => "Behebt: " + string.Join(", ", this.Members.Select(m => m.Title).Distinct());
        /// <inheritdoc/>
        public override string Title => "Spielfehler";
        /// <inheritdoc/>
        public override PatchGroup Group => PatchGroup.Patches;
        /// <inheritdoc/>
        public override uint Flag => this.Members.Aggregate(0u, (f, m) => f | m.Flag);
        /// <inheritdoc/>
        public override IEnumerable<byte?[]> Patterns => this.Members.SelectMany(m => m.Patterns);

        /// <inheritdoc/>
        public override Site[] Find(byte[] image)
        {
            Site[] sites = this.Members.Select(m => m.Find(image)).Where(f => f != null).SelectMany(f => f).ToArray();
            return sites.Length == 0 ? null : sites;
        }

        /// <inheritdoc/>
        internal override bool IsApplied(Site[] sites) => sites.Any(s => s.IsPatched);
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
        /// <summary>The most goals per team the patch allows.</summary>
        internal const short MaxGoals = 15;
        /// <summary>The brakes' original jump (jle rel8).</summary>
        internal const byte Jle = 0x7E;
        /// <summary>The brakes' patched jump (jmp rel8), which skips the halving.</summary>
        internal const byte Jmp = 0xEB;
        private const byte Nop = 0x90;

        /// <summary>The instructions right before the limit's code: cmp word [clock+8], 45 ... setg.</summary>
        internal static readonly byte[] LimitPattern     = { 0x66, 0x83, 0x7F, 0x08, 0x2D, 0x57, 0x6A, 0x01, 0x0F, 0x9F };
        /// <summary>The instructions right before the home team's brake (jle): cmp word [esi+0xE], 6 / mov [esp+0x30], eax.</summary>
        internal static readonly byte[] HomeBrakePattern = { 0x66, 0x83, 0x7E, 0x0E, 0x06, 0x89, 0x44, 0x24, 0x30 };
        /// <summary>The instructions right before the away team's brake (jle): mov dx, [esi+0x10] / add esp, 4 / cmp dx, 6 / mov ecx, eax.</summary>
        internal static readonly byte[] AwayBrakePattern = { 0x66, 0x8B, 0x56, 0x10, 0x83, 0xC4, 0x04, 0x66, 0x83, 0xFA, 0x06, 0x8B, 0xC8 };

        /// <inheritdoc/>
        public override string Name => "Torgrenze auf 15 Tore pro Mannschaft erhöhen";
        /// <inheritdoc/>
        public override string Title => "Mehr Tore";
        /// <inheritdoc/>
        public override PatchGroup Group => PatchGroup.Balancing;
        /// <inheritdoc/>
        public override uint Flag => 1;
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

        /// <summary>
        /// The limit's code for <paramref name="register"/>: dec r32 / and r, -4 / add r, 10 - with eax, the
        /// compiler may use the short "and al, -4". The long form comes first: patched, the short form is a prefix of it.
        /// </summary>
        internal static byte[][] LimitOriginals(int register)
        {
            byte[] add = { 0x83, (byte)(0xC0 + register), 0x0A };
            byte[] longForm = new byte[] { (byte)(0x48 + register), 0x83, (byte)(0xE0 + register), 0xFC }.Concat(add).ToArray();
            return register == 0
                ? new[] { longForm, new byte[] { 0x48, 0x24, 0xFC }.Concat(add).ToArray() }
                : new[] { longForm };
        }

        /// <summary>mov r32, 15 for <paramref name="register"/>, padded with nops to <paramref name="length"/>.</summary>
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
        public override string Title => "Pfeile gedrückt halten";
        /// <inheritdoc/>
        public override PatchGroup Group => PatchGroup.Patches;
        /// <inheritdoc/>
        public override uint Flag => 2;
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
        /// <summary>
        /// test eax,eax / je +21 / release: mov ecx,[ebp] / push ecx / call AVIFileRelease / lea ecx,[esp+10] /
        /// call / xor eax,eax / pop edi,esi,ebp,ebx / add esp,84 / ret C / mov eax,[edi] / lea edx,[esi+8498] /
        /// push 8C / push edx / push eax / call AVIStreamInfoA
        /// </summary>
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
        public override string Title => "Videos ohne Absturz";
        /// <inheritdoc/>
        public override PatchGroup Group => PatchGroup.Patches;
        /// <inheritdoc/>
        public override uint Flag => 4;
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
        public override string Title => "Langer Windows-Betrieb";
        /// <inheritdoc/>
        public override PatchGroup Group => PatchGroup.Patches;
        /// <inheritdoc/>
        public override uint Flag => 8;
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
        public override string Title => "Große Festplatten";
        /// <inheritdoc/>
        public override PatchGroup Group => PatchGroup.Patches;
        /// <inheritdoc/>
        public override uint Flag => 32;
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
        public override string Title => "Kadergröße";
        /// <inheritdoc/>
        public override PatchGroup Group => PatchGroup.Patches;
        /// <inheritdoc/>
        public override uint Flag => 64;
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
        public override string Title => "Bildschirmauflösung";
        /// <inheritdoc/>
        public override PatchGroup Group => PatchGroup.Patches;
        /// <inheritdoc/>
        public override uint Flag => 128;
    }

    /// <summary>
    /// Keeps the preseason calculation screen ("ANSTOSS2 - Kalkulation") from hanging. Its constructor
    /// (at 0x4a513f in the original) sets the selection of "Ermittlung der Zahlen" (+0x10D48) to 0 but
    /// leaves that of "Darstellung" (+0x10D46) as it comes from the heap. With a large value there,
    /// drawing the list reads past its entries, the access violation inside WM_INITDIALOG is swallowed
    /// by the window callback, and the screen stays hidden while the game waits for it. Seen under Wine;
    /// on Windows that memory is usually 0. The patch clears both selections in the same 11 bytes:
    /// <code>
    ///   mov word [esi+10D48],0 / mov eax,esi   ->   mov eax,esi / and dword [esi+10D46],0 / nop x2
    /// </code>
    /// </summary>
    internal sealed class CalculationInit : RewritePatch
    {
        /// <inheritdoc/>
        internal override byte?[] OriginalCode => Exact(0x66, 0xC7, 0x86, 0x48, 0x0D, 0x01, 0x00, 0x00, 0x00, 0x8B, 0xC6);
        /// <inheritdoc/>
        internal override byte?[] PatchedCode => Exact(0x8B, 0xC6, 0x83, 0xA6, 0x46, 0x0D, 0x01, 0x00, 0x00, 0x90, 0x90);

        /// <inheritdoc/>
        public override string Name => "Kein Hänger beim Öffnen der Kalkulation in der Vorsaison";
        /// <inheritdoc/>
        public override string Title => "Kalkulation";
        /// <inheritdoc/>
        public override PatchGroup Group => PatchGroup.Patches;
        /// <inheritdoc/>
        public override uint Flag => 512;
    }

    /// <summary>
    /// Keeps picture buttons such as "A/B" and "i" in the team lineup before a match where they belong.
    /// The constructor of the picture button class (FUN_005bc3a0 in the original) leaves the BITMAP it
    /// fills when a picture is loaded (+0x7C to +0x93, width and height at +0x80 and +0x84) as it comes
    /// from the heap. The lineup moves "A/B" and "i" to their template position with that width and height
    /// before loading their pictures, then aligns them to the bottom right corner of that window. With
    /// zeroed memory they land on the template's spot; with old data there the window gets a huge size,
    /// its corner is clipped, and the button ends up shifted or off screen until the lineup is opened
    /// again. Seen under Wine (A2G-Linux, a2start.c); on Windows that memory is usually 0. The patch
    /// clears the BITMAP as well. Room comes from encoding the constructor's other stores more tightly:
    /// the ones beyond +0x7F through edx = this+0x80, so they need one displacement byte instead of four.
    /// <code>
    ///   xor eax,eax / mov ecx,CString / mov [esi+..],eax|ecx ... (12 with a 4-byte displacement) ... / mov [esi],vtable / mov eax,esi / pop esi / ret
    /// ->
    ///   xor eax,eax / mov ecx,CString / mov [esi+..],eax|ecx ... / mov [esi+7C],eax / lea edx,[esi+80]
    ///   mov [edx+0..24],eax / mov [edx+30..48],eax / nop x13 / mov [esi],vtable / mov eax,esi / pop esi / ret
    /// </code>
    /// The constructor stores nothing but zeros and the empty string, so the order doesn't matter, and
    /// edx is free (the caller expects only eax and esi).
    /// </summary>
    internal sealed class PictureButtonInit : RewritePatch
    {
        private static readonly byte?[] Head = { 0x33, 0xC0, 0xB9, null, null, null, null };          // xor eax,eax / mov ecx,CString
        private static readonly byte?[] Tail = { 0xC7, 0x06, null, null, null, null, 0x8B, 0xC6, 0x5E, 0xC3 };   // mov [esi],vtable / mov eax,esi / pop esi / ret

        /// <inheritdoc/>
        internal override byte?[] OriginalCode => Head.Concat(Exact(
            0x89, 0x46, 0x5C, 0x89, 0x4E, 0x58, 0x89, 0x46, 0x64, 0x89, 0x4E, 0x60, 0x89, 0x46, 0x6C, 0x89, 0x4E, 0x68,
            0x89, 0x86, 0xA4, 0x00, 0x00, 0x00, 0x89, 0x46, 0x3C, 0x66, 0x89, 0x46, 0x46, 0x66, 0x89, 0x46, 0x48,
            0x89, 0x46, 0x50, 0x89, 0x46, 0x54, 0x89, 0x86, 0x98, 0x00, 0x00, 0x00, 0x89, 0x86, 0x9C, 0x00, 0x00, 0x00,
            0x89, 0x86, 0xA0, 0x00, 0x00, 0x00, 0x89, 0x86, 0xB0, 0x00, 0x00, 0x00, 0x89, 0x46, 0x74, 0x89, 0x46, 0x78,
            0x66, 0x89, 0x46, 0x44, 0x66, 0x89, 0x46, 0x4A, 0x66, 0x89, 0x46, 0x4C,
            0x89, 0x86, 0xB4, 0x00, 0x00, 0x00, 0x89, 0x86, 0xB8, 0x00, 0x00, 0x00, 0x89, 0x86, 0xBC, 0x00, 0x00, 0x00,
            0x89, 0x86, 0xC0, 0x00, 0x00, 0x00, 0x89, 0x86, 0xC4, 0x00, 0x00, 0x00, 0x89, 0x86, 0xC8, 0x00, 0x00, 0x00,
            0x89, 0x86, 0x94, 0x00, 0x00, 0x00)).Concat(Tail).ToArray();

        /// <inheritdoc/>
        internal override byte?[] PatchedCode => Head.Concat(Exact(
            0x89, 0x46, 0x5C, 0x89, 0x4E, 0x58, 0x89, 0x46, 0x64, 0x89, 0x4E, 0x60, 0x89, 0x46, 0x6C, 0x89, 0x4E, 0x68,
            0x89, 0x46, 0x3C, 0x66, 0x89, 0x46, 0x44, 0x66, 0x89, 0x46, 0x46, 0x66, 0x89, 0x46, 0x48,
            0x66, 0x89, 0x46, 0x4A, 0x66, 0x89, 0x46, 0x4C,
            0x89, 0x46, 0x50, 0x89, 0x46, 0x54, 0x89, 0x46, 0x74, 0x89, 0x46, 0x78, 0x89, 0x46, 0x7C,     // +7C: new
            0x8D, 0x96, 0x80, 0x00, 0x00, 0x00, 0x89, 0x02,                                         // lea edx,[esi+80] / +80: new
            0x89, 0x42, 0x04, 0x89, 0x42, 0x08, 0x89, 0x42, 0x0C, 0x89, 0x42, 0x10,                 // +84 to +90: new
            0x89, 0x42, 0x14, 0x89, 0x42, 0x18, 0x89, 0x42, 0x1C, 0x89, 0x42, 0x20, 0x89, 0x42, 0x24,   // +94 to +A4
            0x89, 0x42, 0x30, 0x89, 0x42, 0x34, 0x89, 0x42, 0x38, 0x89, 0x42, 0x3C, 0x89, 0x42, 0x40,   // +B0 to +C8
            0x89, 0x42, 0x44, 0x89, 0x42, 0x48,
            0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90)).Concat(Tail).ToArray();

        /// <inheritdoc/>
        public override string Name => "Knöpfe „A/B“ und „i“ in der Aufstellung an ihrem Platz";
        /// <inheritdoc/>
        public override string Title => "Aufstellung";
        /// <inheritdoc/>
        public override PatchGroup Group => PatchGroup.Patches;
        /// <inheritdoc/>
        public override uint Flag => 2048;
    }

    /// <summary>
    /// A patch that rewrites one block of code that occurs exactly once. Original and patched code have
    /// the same length and as many wildcards (null); those bytes (addresses, displacements) are kept
    /// from the game, in their order (usually at the same places; a call's displacement that moves is
    /// adjusted, see <see cref="FindOnce"/>).
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
            Site site = FindOnce(image, this.OriginalCode, this.PatchedCode);
            return site == null ? null : new[] { site };
        }

        /// <summary>
        /// The one place of a block in <paramref name="image"/>, original or patched, or null. The bytes at the
        /// found code's wildcards go to the wildcards of both, in order. Four wildcards right behind a call or
        /// jmp (E8, E9) in both are its displacement: where the other code has it elsewhere, it is moved by the
        /// difference, so that it still reaches the same target.
        /// </summary>
        internal static Site FindOnce(byte[] image, byte?[] originalCode, byte?[] patchedCode)
        {
            List<long> original = FindAll(image, originalCode), patched = FindAll(image, patchedCode);
            if (original.Count + patched.Count != 1)
                return null;
            bool isPatched = patched.Count == 1;
            long at = isPatched ? patched[0] : original[0];
            byte?[] found = isPatched ? patchedCode : originalCode;
            int[] foundHoles = Holes(found);
            byte[] kept = foundHoles.Select(i => image[at + i]).ToArray();
            byte[] Fill(byte?[] code)
            {
                int[] holes = Holes(code);
                byte[] result = code.Select(b => b ?? 0).ToArray();
                for (int k = 0; k < holes.Length; k++)
                    result[holes[k]] = kept[k];
                for (int k = 0; k < holes.Length; k++)
                {
                    if (IsDisplacement(code, holes, k) && IsDisplacement(found, foundHoles, k))
                        BitConverter.GetBytes(BitConverter.ToInt32(kept, k) + foundHoles[k] - holes[k]).CopyTo(result, holes[k]);
                }
                return result;
            }
            return new Site { Offset = at, Original = Fill(originalCode), Patched = Fill(patchedCode), IsPatched = isPatched };
        }

        // Where the wildcards of code are, in order.
        private static int[] Holes(byte?[] code) => Enumerable.Range(0, code.Length).Where(i => code[i] == null).ToArray();

        // Whether the wildcards from the k-th on are four in a row right behind E8 or E9, and no more (a
        // pattern never starts with a wildcard).
        private static bool IsDisplacement(byte?[] code, int[] holes, int k)
        {
            int at = holes[k];
            return k + 3 < holes.Length && holes[k + 3] == at + 3 && (code[at - 1] == 0xE8 || code[at - 1] == 0xE9)
                && (at + 4 == code.Length || code[at + 4] != null);
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
        public override string Title => "Audiogerät";
        /// <inheritdoc/>
        public override PatchGroup Group => PatchGroup.Patches;
        /// <inheritdoc/>
        public override uint Flag => 16;
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

    /// <summary>
    /// Makes the club names on the sign in the cup draw video readable again. The video's player
    /// (FUN_00419e10 in the original) draws the names with GDI into CreateCompatibleBitmap(screen, 128, 32)
    /// and copies them out with GetBitmapBits(bitmap, 0x2000, this+C), expecting 16 bits per pixel. On
    /// current Windows and under Wine the bitmap has 32 bits per pixel even in the game's 16-bit mode,
    /// so the game gets the upper half of the picture with each pixel split in two: names twice as wide,
    /// half as high and dotted. The patch sends the call to added code that fetches the picture with
    /// GetDIBits as a top-down 16-bit DIB.
    /// Wine ignores the masks of a BI_BITFIELDS request, so the code asks for BI_RGB (5-5-5) and converts
    /// the pixels to 5-6-5 itself if the game's surface uses that (green mask this+8040 = 7E0; esi is
    /// the player there). Black stays 0, the game's transparent color.
    /// <code>
    ///   call [GetBitmapBits]   ->   call helper / nop
    ///   helper: GetDC(NULL) / GetDIBits(dc, bitmap, 0, 32, bits, &amp;16-bit top-down 128x32, DIB_RGB_COLORS) /
    ///           ReleaseDC / if 5-6-5: each pixel (p &amp; 1F) | (p &amp; 7FE0) &lt;&lt; 1 | (p &amp; 200) &gt;&gt; 4 / ret C
    /// </code>
    /// The helper (177 bytes, through the game's own imports of GetDC, GetDIBits and ReleaseDC) goes at
    /// the end of the exe's last executable section, which is zero there: past the end of the original's
    /// code (the loader maps a section's whole raw data), at the end of GOG's import section. Taking the
    /// patch back zeroes it again. The sections of <see cref="Patch101"/> and <see cref="InterviewEffects"/>
    /// don't count: they can go again.
    /// </summary>
    internal sealed class CupDraw : Patch
    {
        // push ebx / push 0 / call [ReleaseDC] / mov edx,[esi+8010] / lea ecx,[esi+C] / push ecx / push 2000 / push edx
        private static readonly byte?[] Head =
        {
            0x53, 0x6A, 0x00, 0xFF, 0x15, null, null, null, null,
            0x8B, 0x96, 0x10, 0x80, 0x00, 0x00, 0x8D, 0x4E, 0x0C, 0x51, 0x68, 0x00, 0x20, 0x00, 0x00, 0x52,
        };
        private static readonly byte?[] OriginalPattern = Head.Concat(new byte?[] { 0xFF, 0x15, null, null, null, null }).ToArray();
        private static readonly byte?[] PatchedPattern = Head.Concat(new byte?[] { 0xE8, null, null, null, null, 0x90 }).ToArray();
        private const uint Executable = 0x20000000;   // IMAGE_SCN_MEM_EXECUTE

        // stdcall like GetBitmapBits(bitmap, size, bits); the three imports' addresses are left 0.
        private static readonly byte[] Helper =
        {
            0x55, 0x53, 0x83, 0xEC, 0x34,                                       // push ebp / push ebx / sub esp,34 (BITMAPINFO)
            0xC7, 0x04, 0x24, 0x28, 0x00, 0x00, 0x00,                           // biSize 40
            0xC7, 0x44, 0x24, 0x04, 0x80, 0x00, 0x00, 0x00,                     // biWidth 128
            0xC7, 0x44, 0x24, 0x08, 0xE0, 0xFF, 0xFF, 0xFF,                     // biHeight -32 (top-down)
            0xC7, 0x44, 0x24, 0x0C, 0x01, 0x00, 0x10, 0x00,                     // biPlanes 1, biBitCount 16
            0x33, 0xC0, 0x89, 0x44, 0x24, 0x10, 0x89, 0x44, 0x24, 0x14, 0x89, 0x44, 0x24, 0x18,
            0x89, 0x44, 0x24, 0x1C, 0x89, 0x44, 0x24, 0x20, 0x89, 0x44, 0x24, 0x24, // BI_RGB, the rest 0
            0x6A, 0x00, 0xFF, 0x15, 0x00, 0x00, 0x00, 0x00,                     // GetDC(NULL)
            0x8B, 0xD8, 0x8B, 0xCC,                                             // mov ebx,eax / mov ecx,esp
            0x6A, 0x00, 0x51, 0xFF, 0x74, 0x24, 0x50, 0x6A, 0x20, 0x6A, 0x00,   // DIB_RGB_COLORS, &info, bits, 32 lines, from 0
            0xFF, 0x74, 0x24, 0x54, 0x53, 0xFF, 0x15, 0x00, 0x00, 0x00, 0x00,   // bitmap, dc / GetDIBits
            0x8B, 0xE8, 0x53, 0x6A, 0x00, 0xFF, 0x15, 0x00, 0x00, 0x00, 0x00,   // mov ebp,eax / ReleaseDC(NULL, dc)
            0x81, 0xBE, 0x40, 0x80, 0x00, 0x00, 0xE0, 0x07, 0x00, 0x00,         // cmp dword [esi+8040],7E0 (5-6-5?)
            0x75, 0x30,                                                         // jne done
            0x8B, 0x54, 0x24, 0x48, 0xB9, 0x00, 0x10, 0x00, 0x00,               // mov edx,bits / mov ecx,4096
            0x0F, 0xB7, 0x02, 0x8B, 0xD8, 0x81, 0xE3, 0x1F, 0x00, 0x00, 0x00,   // next: movzx eax,word [edx] / blue
            0x25, 0xE0, 0x7F, 0x00, 0x00, 0xD1, 0xE0, 0x0B, 0xD8,               // red and green one bit up
            0x25, 0x00, 0x04, 0x00, 0x00, 0xC1, 0xE8, 0x05, 0x0B, 0xC3,         // top green bit also as the lowest
            0x66, 0x89, 0x02, 0x83, 0xC2, 0x02, 0x49, 0x75, 0xD9,               // store / add edx,2 / dec ecx / jne next
            0x8B, 0xC5, 0x83, 0xC4, 0x34, 0x5B, 0x5D, 0xC2, 0x0C, 0x00,         // done: return GetDIBits' result
        };
        private const int GetDCAt = 66, GetDIBitsAt = 92, ReleaseDCAt = 103;

        /// <inheritdoc/>
        public override string Name => "Vereinsnamen bei der Pokalauslosung richtig darstellen";
        /// <inheritdoc/>
        public override string Title => "Pokalauslosung";
        /// <inheritdoc/>
        public override PatchGroup Group => PatchGroup.Patches;
        /// <inheritdoc/>
        public override uint Flag => 256;
        /// <inheritdoc/>
        public override IEnumerable<byte?[]> Patterns => new[] { OriginalPattern, PatchedPattern };

        /// <summary>The added code for the import address table entries of GetDC, GetDIBits and ReleaseDC.</summary>
        internal static byte[] HelperBytes(uint getDC, uint getDIBits, uint releaseDC)
        {
            byte[] code = (byte[])Helper.Clone();
            BitConverter.GetBytes(getDC).CopyTo(code, GetDCAt);
            BitConverter.GetBytes(getDIBits).CopyTo(code, GetDIBitsAt);
            BitConverter.GetBytes(releaseDC).CopyTo(code, ReleaseDCAt);
            return code;
        }

        /// <summary>The last executable section, apart from those of <see cref="Patch101"/> and <see cref="InterviewEffects"/>; null if there's none.</summary>
        internal static PeFile.Section LastSection(PeFile pe) =>
            pe.Sections.LastOrDefault(s => (s.Characteristics & Executable) != 0 && s.Name != Patch101.SectionName && s.Name != InterviewEffects.SectionName);

        /// <summary>Where the helper goes in <paramref name="section"/>'s data in the file: at its end, on a 16-byte boundary.</summary>
        internal static long HelperAt(PeFile.Section section) => (section.Raw + section.RawSize - Helper.Length) & ~15L;

        /// <inheritdoc/>
        public override Site[] Find(byte[] image)
        {
            List<long> original = FindAll(image, OriginalPattern), patched = FindAll(image, PatchedPattern);
            if (original.Count + patched.Count != 1)
                return null;
            PeFile pe = PeFile.Read(image);
            uint[] imports = new[] { "GetBitmapBits", "GetDC", "GetDIBits", "ReleaseDC" }
                .Select(name => pe != null && pe.Imports.TryGetValue(name, out uint entry) ? entry : 0).ToArray();
            PeFile.Section last = pe == null ? null : LastSection(pe);
            if (imports.Contains(0u) || last == null)
                return null;
            bool isPatched = patched.Count == 1;
            long call = (isPatched ? patched[0] : original[0]) + Head.Length;
            long helper = HelperAt(last);
            long callVa = pe.VaAt(call), helperVa = pe.VaAt(helper);
            if (callVa < 0 || helperVa < 0)
                return null;

            byte[] code = HelperBytes(imports[1], imports[2], imports[3]);
            byte[] originalCall = new byte[] { 0xFF, 0x15 }.Concat(BitConverter.GetBytes(imports[0])).ToArray();
            byte[] patchedCall = new byte[] { 0xE8 }.Concat(BitConverter.GetBytes((int)(helperVa - (callVa + 5)))).Concat(new byte[] { 0x90 }).ToArray();
            if (!image.Skip((int)call).Take(6).SequenceEqual(isPatched ? patchedCall : originalCall)
                || !image.Skip((int)helper).Take(code.Length).SequenceEqual(isPatched ? code : new byte[code.Length]))
                return null;
            // The helper first, so the call never leads to zeros.
            return new[]
            {
                new Site { Offset = helper, Original = new byte[code.Length], Patched = code, IsPatched = isPatched },
                new Site { Offset = call, Original = originalCall, Patched = patchedCall, IsPatched = isPatched },
            };
        }
    }

    /// <summary>
    /// Lets the answers in interviews take the effect their texts give them. The interview engine (a
    /// pointer 0xC8 bytes before the interview context) works on a heap copy of the context (engine+11C)
    /// and stores the chosen answer's two effect pairs (code, value) there, at +14 to +20. The game then
    /// applies the pairs (FUN_004f8a00 in the original, called with "push context") from the context
    /// itself, whose pairs are never written: every answer applies "fans +0" twice. Two codes also don't
    /// match the cases of the effect function (FUN_004f87f0): the texts use 10 for the team's morale (no
    /// case) and 12 for a fine in DM (case 12 is the morale). The patch adds the code of A2G-Trainer's
    /// InterviewEffectsFix (Controller/InterviewEffectsFix.cs) byte for byte, so the trainer recognizes it
    /// by its signature, and sends two calls there:
    /// <code>
    ///   push context / call apply                        ->  push context / call stub+20
    ///   case 12: push value / push club / call morale    ->  ... / call stub+C0
    ///   stub+20: with [stub+8] set, the engine and its copy there: the copy's pairs into the context,
    ///            10 -> 12, 12 with the value marked (40 in the high byte); else the pairs 0 / jmp apply
    ///   stub+C0: marked: club+38C (other costs) += DM * [money factor] / 100 / ret; else jmp morale
    /// </code>
    /// The flag at +8 is set; A2G-Trainer switches the effects off and on with it. The money factor lies
    /// 0xC258 bytes behind the context in every build. The stub (256 bytes) goes right in front of
    /// <see cref="CupDraw"/>'s helper where that is zero (GOG's import section). The original exe has no
    /// room there; it gets a section of its own (<see cref="SectionName"/>), which goes again with the patch.
    /// </summary>
    internal sealed class InterviewEffects : FilePatch
    {
        /// <summary>The name of the section the patch adds where the last executable section has no room.</summary>
        internal const string SectionName = ".a2int";
        /// <summary>The size of the stub, and where its two entries are.</summary>
        internal const int StubSize = 0x100, ApplyEntry = 0x20, MoraleEntry = 0xC0;
        /// <summary>The distances of the engine pointer (before) and the money factor (behind) to the context.</summary>
        internal const uint EngineBeforeContext = 0xC8, MoneyFactorBehindContext = 0xC258;

        /// <summary>mov ecx,[engine] / push eax / call (store the answer) / push context / call apply / mov eax,[context+10]</summary>
        internal static readonly byte?[] ApplyCallPattern =
        {
            0x8B, 0x0D, null, null, null, null, 0x50, 0xE8, null, null, null, null,
            0x68, null, null, null, null, 0xE8, null, null, null, null, 0xA1, null, null, null, null,
        };
        private const int EngineAt = 2, ContextAt = 13, ApplyCallAt = 17, ContextFieldAt = 23;
        /// <summary>Case 12: mov ecx,[esp+50] / mov edx,[esp+5C] / push ecx / push edx / call morale / add esp,8 / pop edi / pop esi / add esp,40 / ret</summary>
        internal static readonly byte?[] MoraleCallPattern =
        {
            0x8B, 0x4C, 0x24, 0x50, 0x8B, 0x54, 0x24, 0x5C, 0x51, 0x52, 0xE8, null, null, null, null,
            0x83, 0xC4, 0x08, 0x5F, 0x5E, 0x83, 0xC4, 0x40, 0xC3,
        };
        private const int MoraleCallAt = 10;
        /// <summary>The first instructions of the effect application (as A2G-Trainer finds it).</summary>
        internal static readonly byte[] ApplyHead = { 0x53, 0x55, 0x56, 0x8B, 0x74, 0x24, 0x10, 0x57, 0x66, 0x8B, 0x46, 0x74, 0x50, 0xE8 };
        /// <summary>The first instructions of the morale change (as A2G-Trainer finds it).</summary>
        internal static readonly byte[] MoraleHead = { 0x8B, 0x4C, 0x24, 0x04, 0x8B, 0x44, 0x24, 0x08, 0x66, 0x0F, 0xB6, 0x91, 0x9C, 0x00, 0x00, 0x00, 0x03, 0xC2, 0x66, 0x3D, 0xC8, 0x00 };

        // A2G-Trainer's InterviewEffectsFix.BuildStub for the addresses 0 (the stub's own at 0), the flag set.
        private static readonly byte[] Stub = new[]
        {
            new byte[] { 0x41, 0x32, 0x47, 0x49, 0x4E, 0x54, 0x30, 0x32, 0x01 },        // "A2GINT02", flag 1
            new byte[ApplyEntry - 9],
            new byte[]
            {
                0x80, 0x3D, 0x08, 0x00, 0x00, 0x00, 0x00, 0x74, 0x6D,                   // cmp byte [flag],0 / je zero
                0xA1, 0x00, 0x00, 0x00, 0x00, 0x85, 0xC0, 0x74, 0x64,                   // mov eax,[engine] / test / je zero
                0x8B, 0x80, 0x1C, 0x01, 0x00, 0x00, 0x85, 0xC0, 0x74, 0x5A,             // mov eax,[eax+11C] (the copy) / test / je zero
                0x8B, 0x48, 0x14, 0x8B, 0x50, 0x18,                                     // first pair: mov ecx,[eax+14] / mov edx,[eax+18]
                0x83, 0xF9, 0x0C, 0x75, 0x0E,                                           // cmp ecx,12 / jne morale
                0x81, 0xE2, 0xFF, 0xFF, 0xFF, 0x00, 0x81, 0xCA, 0x00, 0x00, 0x00, 0x40, // and edx,FFFFFF / or edx,40000000 (marked fine)
                0xEB, 0x07,                                                             // jmp store
                0x83, 0xF9, 0x0A, 0x75, 0x02, 0xB1, 0x0C,                               // morale: cmp ecx,10 / jne store / mov cl,12
                0x89, 0x0D, 0x14, 0x00, 0x00, 0x00, 0x89, 0x15, 0x18, 0x00, 0x00, 0x00, // store: mov [context+14],ecx / mov [context+18],edx
                0x8B, 0x48, 0x1C, 0x8B, 0x50, 0x20,                                     // second pair, the same
                0x83, 0xF9, 0x0C, 0x75, 0x0E,
                0x81, 0xE2, 0xFF, 0xFF, 0xFF, 0x00, 0x81, 0xCA, 0x00, 0x00, 0x00, 0x40,
                0xEB, 0x07,
                0x83, 0xF9, 0x0A, 0x75, 0x02, 0xB1, 0x0C,
                0x89, 0x0D, 0x1C, 0x00, 0x00, 0x00, 0x89, 0x15, 0x20, 0x00, 0x00, 0x00,
                0xEB, 0x1A,                                                             // jmp done
                0x31, 0xC9,                                                             // zero: xor ecx,ecx
                0x89, 0x0D, 0x14, 0x00, 0x00, 0x00, 0x89, 0x0D, 0x18, 0x00, 0x00, 0x00, // mov [context+14..20],ecx
                0x89, 0x0D, 0x1C, 0x00, 0x00, 0x00, 0x89, 0x0D, 0x20, 0x00, 0x00, 0x00,
                0xB8, 0x00, 0x00, 0x00, 0x00, 0xFF, 0xE0,                               // done: mov eax,apply / jmp eax
            },
            new byte[MoraleEntry - 0xB7],
            new byte[]
            {
                0x80, 0x7C, 0x24, 0x0B, 0x40, 0x75, 0x27,                               // cmp byte [esp+B],40 (marked?) / jne morale
                0x8B, 0x44, 0x24, 0x08, 0x25, 0xFF, 0xFF, 0xFF, 0x00,                   // mov eax,[esp+8] / and eax,FFFFFF (DM)
                0x0F, 0xAF, 0x05, 0x00, 0x00, 0x00, 0x00,                               // imul eax,[money factor]
                0x99, 0xB9, 0x64, 0x00, 0x00, 0x00, 0xF7, 0xF9,                         // cdq / mov ecx,100 / idiv ecx
                0x8B, 0x54, 0x24, 0x04, 0x85, 0xD2, 0x74, 0x06,                         // mov edx,[esp+4] (club) / test / je return
                0x01, 0x82, 0x8C, 0x03, 0x00, 0x00,                                     // add [edx+38C],eax (other costs)
                0xC3,                                                                   // return: ret
                0xB8, 0x00, 0x00, 0x00, 0x00, 0xFF, 0xE0,                               // morale: mov eax,morale / jmp eax
            },
            new byte[StubSize - 0xF5],
        }.SelectMany(b => b).ToArray();

        // Where the addresses go in the stub: added to what it holds there (the offset within the stub or the context).
        private const int FlagRef = 0x22, EngineRef = 0x2A, ApplyRef = 0xB1, MoneyFactorRef = 0xD3, MoraleRef = 0xEF;
        private static readonly int[] ContextRefs = { 0x5E, 0x64, 0x8A, 0x90, 0x9A, 0xA0, 0xA6, 0xAC };

        /// <inheritdoc/>
        public override string Name => "Antworten in Interviews wirken sich aus (Fans, Vorstand, Moral, Strafen …)";
        /// <inheritdoc/>
        public override string Title => "Interviews";
        /// <inheritdoc/>
        public override PatchGroup Group => PatchGroup.Balancing;
        /// <inheritdoc/>
        public override uint Flag => 4096;

        /// <summary>The stub at <paramref name="stub"/> for the given addresses of the game.</summary>
        internal static byte[] StubBytes(uint stub, uint engine, uint context, uint apply, uint moneyFactor, uint morale)
        {
            byte[] code = (byte[])Stub.Clone();
            void Add(int at, uint value) => BitConverter.GetBytes(BitConverter.ToUInt32(code, at) + value).CopyTo(code, at);
            Add(FlagRef, stub);
            Add(EngineRef, engine);
            foreach (int at in ContextRefs)
                Add(at, context);
            Add(ApplyRef, apply);
            Add(MoneyFactorRef, moneyFactor);
            Add(MoraleRef, morale);
            return code;
        }

        // The two calls and what the patch needs to know about them.
        private sealed class Places
        {
            public PeFile Pe;
            public long ApplyCall, MoraleCall;          // the calls in the file
            public uint Engine, Context, Apply, Morale; // the game's addresses
            public long Stub = -1;                      // where the stub is in the file, if the patch is on
        }

        // The places in image, the patch on or off; null if they aren't as expected.
        private static Places Locate(byte[] image)
        {
            PeFile pe = PeFile.Read(image);
            List<long> applyCalls = FindAll(image, ApplyCallPattern).Where(at => U32(image, at + ContextFieldAt) == U32(image, at + ContextAt) + 0x10).ToList();
            List<long> moraleCalls = FindAll(image, MoraleCallPattern);
            if (pe == null || applyCalls.Count != 1 || moraleCalls.Count != 1)
                return null;
            Places places = new Places
            {
                Pe = pe, ApplyCall = applyCalls[0] + ApplyCallAt, MoraleCall = moraleCalls[0] + MoraleCallAt,
                Engine = U32(image, applyCalls[0] + EngineAt), Context = U32(image, applyCalls[0] + ContextAt),
            };
            long applyVa = pe.VaAt(places.ApplyCall), moraleVa = pe.VaAt(places.MoraleCall);
            if (places.Engine != places.Context - EngineBeforeContext || applyVa < 0 || moraleVa < 0)
                return null;
            uint apply = (uint)(applyVa + 5 + BitConverter.ToInt32(image, (int)places.ApplyCall + 1));
            uint morale = (uint)(moraleVa + 5 + BitConverter.ToInt32(image, (int)places.MoraleCall + 1));
            if (Holds(image, pe, apply, ApplyHead) && Holds(image, pe, morale, MoraleHead))
            {
                places.Apply = apply;
                places.Morale = morale;
                return places;
            }

            // On: both calls lead into one stub, which leads on to the two functions.
            uint stub = apply - ApplyEntry;
            byte[] found = image.Slice(Math.Max(0, pe.FileOffset(stub)), StubSize);
            if (morale != stub + MoraleEntry || pe.FileOffset(stub) < 0 || found.Length != StubSize)
                return null;
            places.Apply = BitConverter.ToUInt32(found, ApplyRef);
            places.Morale = BitConverter.ToUInt32(found, MoraleRef);
            byte[] expected = StubBytes(stub, places.Engine, places.Context, places.Apply, places.Context + MoneyFactorBehindContext, places.Morale);
            if (!found.SequenceEqual(expected) || !Holds(image, pe, places.Apply, ApplyHead) || !Holds(image, pe, places.Morale, MoraleHead))
                return null;
            places.Stub = pe.FileOffset(stub);
            return places;
        }

        private static uint U32(byte[] image, long at) => BitConverter.ToUInt32(image, (int)at);

        // Whether the code at the address va starts with head.
        private static bool Holds(byte[] image, PeFile pe, uint va, byte[] head) => pe.FileOffset(va) >= 0 && image.Slice(pe.FileOffset(va), head.Length).SequenceEqual(head);

        /// <summary>
        /// Where the stub goes in <paramref name="image"/>: right in front of <see cref="CupDraw"/>'s helper if
        /// that is zero, -1 for a section of its own (only if a header fits and the last section isn't
        /// <see cref="Patch101"/>'s, which has to stay last), null if neither.
        /// </summary>
        internal static long? Room(byte[] image, PeFile pe)
        {
            PeFile.Section last = CupDraw.LastSection(pe);
            long at = last == null ? -1 : CupDraw.HelperAt(last) - StubSize;
            if (last != null && at >= last.Raw && image.Slice(at, StubSize).All(b => b == 0))
                return at;
            return pe.HasRoomForSection && pe.Sections.Last().Name != Patch101.SectionName ? -1 : (long?)null;
        }

        /// <inheritdoc/>
        public override FileState State(GameFiles files)
        {
            Places places = Locate(files.Exe);
            if (places == null)
                return FileState.Unknown;
            if (places.Stub >= 0)
                return FileState.On;
            return Room(files.Exe, places.Pe).HasValue ? FileState.Off : FileState.Unknown;
        }

        /// <inheritdoc/>
        public override void Switch(GameFiles files, bool on)
        {
            Places places = Locate(files.Exe);
            if ((places.Stub >= 0) == on)
                return;
            byte[] image = (byte[])files.Exe.Clone();
            PeFile pe = places.Pe;
            if (on)
            {
                long at = Room(image, pe).Value;
                uint stub = at >= 0 ? (uint)pe.VaAt(at) : pe.ImageBase + pe.SizeOfImage;
                byte[] code = StubBytes(stub, places.Engine, places.Context, places.Apply, places.Context + MoneyFactorBehindContext, places.Morale);
                if (at >= 0)
                    code.CopyTo(image, at);
                else
                    image = PeFile.AddSection(image, SectionName, code);
                Call(image, pe, places.ApplyCall, stub + ApplyEntry);
                Call(image, pe, places.MoraleCall, stub + MoraleEntry);
            }
            else
            {
                Call(image, pe, places.ApplyCall, places.Apply);
                Call(image, pe, places.MoraleCall, places.Morale);
                if (pe.Sections.Last().Name == SectionName)
                    image = PeFile.RemoveLastSection(image);
                else
                    Array.Clear(image, (int)places.Stub, StubSize);
            }
            files.Exe = image;
        }

        // Points the call at the file offset call to target.
        private static void Call(byte[] image, PeFile pe, long call, uint target) =>
            BitConverter.GetBytes((int)(target - (pe.VaAt(call) + 5))).CopyTo(image, call + 1);
    }

    /// <summary>
    /// Lets a pre-match speech that doesn't fit the situation weaken the team, as the game's help says it
    /// does ("da sich sonst der positive Effekt ins Negative umkehren kann"): the game only shows a message
    /// ("Die Spieler finden diese Ansprache unpassend.") and takes the use. The check of the chosen speech
    /// (FUN_004c3a20 in the original) marks the team block only for a speech that works (+E4 = 1, +E5 =
    /// its number, factor +1C = 100), and before each strength calculation FUN_004c4bd0 applies +E5's
    /// percentages to defence (+10) and attack (+16) and sets its hardness (+31). The patch:
    /// <code>
    ///   motivation roll failed:  xor edi,edi (works no more)        ->  xor ebx,ebx (as if no use was taken)
    ///   marking the team block:  works, a use taken: +E4/+E5/+1C    ->  the same; a use taken but it
    ///                                                                    doesn't fit: +E5 = -number
    ///   FUN_004c4bd0:            the percentages of +E5             ->  a negative number takes their
    ///                                                                    amounts off and leaves the hardness
    /// </code>
    /// So "+3%" becomes -3 %, and "-5/-5 hart" stays -5 % without the hardness. A failed roll against the
    /// manager's motivation skill stays without effect, and so does a speech without uses left; an
    /// overconfidence isn't lifted. The function is rewritten in its own 176 bytes (more compact, so the
    /// sign fits); it reads the game's speech table (0x6B7D50 in the original), whose address the patch
    /// takes from the code.
    /// </summary>
    internal sealed class SpeechPenalty : Patch
    {
        /// <summary>The motivation roll's failure: call (the message) / {xor edi,edi} / jmp +36 / push 4 / call.</summary>
        internal static readonly byte?[] RollHead = { 0xE8, null, null, null, null };
        /// <summary>What follows the instruction: jmp +36 / push 4 / call.</summary>
        internal static readonly byte[] RollTail = { 0xEB, 0x36, 0x6A, 0x04, 0xE8 };
        /// <summary>The instruction: xor edi,edi (the speech works no more), patched xor ebx,ebx (as if no use was taken).</summary>
        internal static readonly byte[] RollOriginal = { 0x33, 0xFF }, RollPatched = { 0x33, 0xDB };

        /// <summary>Before the marking: lea ecx,[esp+6A0] / call (the message ends).</summary>
        internal static readonly byte?[] MarkHead = { 0x8D, 0x8C, 0x24, 0xA0, 0x06, 0x00, 0x00, 0xE8, null, null, null, null };
        /// <summary>After the marking: lea ecx,[esp+888].</summary>
        internal static readonly byte[] MarkTail = { 0x8D, 0x8C, 0x24, 0x88, 0x08, 0x00, 0x00 };

        /// <summary>ebx: a use was taken (1), edi: the speech works (1), ebp: the club, [esp+17C0]: the team block.</summary>
        internal static readonly byte[] MarkOriginal =
        {
            0x85, 0xDB,                               // 00: test ebx, ebx
            0x74, 0x24,                               // 02: je 0x28
            0x85, 0xFF,                               // 04: test edi, edi
            0x74, 0x20,                               // 06: je 0x28
            0x8B, 0x84, 0x24, 0xC0, 0x17, 0x00, 0x00, // 08: mov eax, dword ptr [esp + 0x17c0]
            0xC6, 0x80, 0xE4, 0x00, 0x00, 0x00, 0x01, // 0F: mov byte ptr [eax + 0xe4], 1
            0x8A, 0x8D, 0x40, 0x14, 0x00, 0x00,       // 16: mov cl, byte ptr [ebp + 0x1440]
            0x88, 0x88, 0xE5, 0x00, 0x00, 0x00,       // 1C: mov byte ptr [eax + 0xe5], cl
            0x66, 0xC7, 0x40, 0x1C, 0x64, 0x00,       // 22: mov word ptr [eax + 0x1c], 0x64
        };

        /// <summary>
        /// The same marking in the same 40 bytes, and +E5 = -number for a speech that doesn't fit. The team
        /// block's address is moved by 80 so the offsets fit in a byte, and "cmp ax,imm16" (66 3D) skips the
        /// "neg cl" behind it.
        /// </summary>
        internal static readonly byte[] MarkPatched =
        {
            0x4B,                                     // 00: dec ebx
            0x75, 0x25,                               // 01: jne 0x28
            0x8B, 0x84, 0x24, 0xC0, 0x17, 0x00, 0x00, // 03: mov eax, dword ptr [esp + 0x17c0]
            0x83, 0xE8, 0x80,                         // 0A: sub eax, -0x80
            0x8A, 0x8D, 0x40, 0x14, 0x00, 0x00,       // 0D: mov cl, byte ptr [ebp + 0x1440]
            0x85, 0xFF,                               // 13: test edi, edi
            0x74, 0x0C,                               // 15: je 0x23
            0xC6, 0x40, 0x64, 0x01,                   // 17: mov byte ptr [eax + 0x64], 1
            0x66, 0xC7, 0x40, 0x9C, 0x64, 0x00,       // 1B: mov word ptr [eax - 0x64], 0x64
            0x66, 0x3D, 0xF6, 0xD9,                   // 21: cmp ax, 0xd9f6
            0x88, 0x48, 0x65,                         // 25: mov byte ptr [eax + 0x65], cl
        };

        /// <summary>FUN_004c4bd0 as the game has it (cdecl: team block, match, home), with its padding; null: the table's addresses.</summary>
        internal static readonly byte?[] ApplyOriginal =
        {
            0x56,                                     // 00: push esi
            0x8B, 0x74, 0x24, 0x08,                   // 01: mov esi, dword ptr [esp + 8]
            0x57,                                     // 05: push edi
            0x8A, 0x86, 0xE5, 0x00, 0x00, 0x00,       // 06: mov al, byte ptr [esi + 0xe5]
            0x84, 0xC0,                               // 0C: test al, al
            0x0F, 0x84, 0x8F, 0x00, 0x00, 0x00,       // 0E: je 0xa3
            0x25, 0xFF, 0x00, 0x00, 0x00,             // 14: and eax, 0xff
            0x8D, 0x3C, 0x80,                         // 19: lea edi, [eax + eax*4]
            0xC1, 0xE7, 0x02,                         // 1C: shl edi, 2
            0xF6, 0x87, null, null, null, null, 0x01, // 1F: test byte ptr [edi + 0x6b7d60], 1
            0x74, 0x22,                               // 26: je 0x4a
            0x66, 0x83, 0x7C, 0x24, 0x14, 0x00,       // 28: cmp word ptr [esp + 0x14], 0
            0x8B, 0x44, 0x24, 0x10,                   // 2E: mov eax, dword ptr [esp + 0x10]
            0x74, 0x0C,                               // 32: je 0x40
            0x66, 0x8B, 0x48, 0x10,                   // 34: mov cx, word ptr [eax + 0x10]
            0x66, 0x3B, 0x48, 0x0E,                   // 38: cmp cx, word ptr [eax + 0xe]
            0x7F, 0x65,                               // 3C: jg 0xa3
            0xEB, 0x0A,                               // 3E: jmp 0x4a
            0x66, 0x8B, 0x50, 0x0E,                   // 40: mov dx, word ptr [eax + 0xe]
            0x66, 0x3B, 0x50, 0x10,                   // 44: cmp dx, word ptr [eax + 0x10]
            0x7F, 0x59,                               // 48: jg 0xa3
            0x0F, 0xBE, 0x8F, null, null, null, null, // 4A: movsx ecx, byte ptr [edi + 0x6b7d5c]
            0x0F, 0xBF, 0x46, 0x10,                   // 51: movsx eax, word ptr [esi + 0x10]
            0x83, 0xC1, 0x64,                         // 55: add ecx, 0x64
            0x0F, 0xAF, 0xC8,                         // 58: imul ecx, eax
            0xB8, 0x1F, 0x85, 0xEB, 0x51,             // 5B: mov eax, 0x51eb851f
            0xF7, 0xE9,                               // 60: imul ecx
            0xC1, 0xFA, 0x05,                         // 62: sar edx, 5
            0x8B, 0xCA,                               // 65: mov ecx, edx
            0xB8, 0x1F, 0x85, 0xEB, 0x51,             // 67: mov eax, 0x51eb851f
            0xC1, 0xE9, 0x1F,                         // 6C: shr ecx, 0x1f
            0x03, 0xD1,                               // 6F: add edx, ecx
            0x66, 0x89, 0x56, 0x10,                   // 71: mov word ptr [esi + 0x10], dx
            0x0F, 0xBE, 0x8F, null, null, null, null, // 75: movsx ecx, byte ptr [edi + 0x6b7d5d]
            0x0F, 0xBF, 0x56, 0x16,                   // 7C: movsx edx, word ptr [esi + 0x16]
            0x83, 0xC1, 0x64,                         // 80: add ecx, 0x64
            0x0F, 0xAF, 0xCA,                         // 83: imul ecx, edx
            0xF7, 0xE9,                               // 86: imul ecx
            0xC1, 0xFA, 0x05,                         // 88: sar edx, 5
            0x8B, 0xC2,                               // 8B: mov eax, edx
            0xC1, 0xE8, 0x1F,                         // 8D: shr eax, 0x1f
            0x03, 0xD0,                               // 90: add edx, eax
            0x66, 0x89, 0x56, 0x16,                   // 92: mov word ptr [esi + 0x16], dx
            0x8A, 0x87, null, null, null, null,       // 96: mov al, byte ptr [edi + 0x6b7d5e]
            0x3C, 0x63,                               // 9C: cmp al, 0x63
            0x74, 0x03,                               // 9E: je 0xa3
            0x88, 0x46, 0x31,                         // A0: mov byte ptr [esi + 0x31], al
            0x5F,                                     // A3: pop edi
            0x5E,                                     // A4: pop esi
            0xC3,                                     // A5: ret
            0x90,                                     // A6: nop
            0x90,                                     // A7: nop
            0x90,                                     // A8: nop
            0x90,                                     // A9: nop
            0x90,                                     // AA: nop
            0x90,                                     // AB: nop
            0x90,                                     // AC: nop
            0x90,                                     // AD: nop
            0x90,                                     // AE: nop
            0x90,                                     // AF: nop
        };

        /// <summary>The function rewritten: the same for a positive number, the amounts taken off for a negative one.</summary>
        internal static readonly byte?[] ApplyPatched = new byte?[]
        {
            0x56,                                     // 00: push esi
            0x8B, 0x74, 0x24, 0x08,                   // 01: mov esi, dword ptr [esp + 8]
            0x57,                                     // 05: push edi
            0x53,                                     // 06: push ebx
            0x0F, 0xBE, 0x86, 0xE5, 0x00, 0x00, 0x00, // 07: movsx eax, byte ptr [esi + 0xe5]
            0x99,                                     // 0E: cdq
            0x8B, 0xDA,                               // 0F: mov ebx, edx
            0x33, 0xC2,                               // 11: xor eax, edx
            0x2B, 0xC2,                               // 13: sub eax, edx
            0x74, 0x60,                               // 15: je 0x77
            0x8D, 0x3C, 0x80,                         // 17: lea edi, [eax + eax*4]
            0xC1, 0xE7, 0x02,                         // 1A: shl edi, 2
            0xF6, 0x87, null, null, null, null, 0x01, // 1D: test byte ptr [edi + 0x6b7d60], 1
            0x74, 0x22,                               // 24: je 0x48
            0x66, 0x83, 0x7C, 0x24, 0x18, 0x00,       // 26: cmp word ptr [esp + 0x18], 0
            0x8B, 0x44, 0x24, 0x14,                   // 2C: mov eax, dword ptr [esp + 0x14]
            0x74, 0x0C,                               // 30: je 0x3e
            0x66, 0x8B, 0x48, 0x10,                   // 32: mov cx, word ptr [eax + 0x10]
            0x66, 0x3B, 0x48, 0x0E,                   // 36: cmp cx, word ptr [eax + 0xe]
            0x7F, 0x3B,                               // 3A: jg 0x77
            0xEB, 0x0A,                               // 3C: jmp 0x48
            0x66, 0x8B, 0x50, 0x0E,                   // 3E: mov dx, word ptr [eax + 0xe]
            0x66, 0x3B, 0x50, 0x10,                   // 42: cmp dx, word ptr [eax + 0x10]
            0x7F, 0x2F,                               // 46: jg 0x77
            0x0F, 0xBE, 0x8F, null, null, null, null, // 48: movsx ecx, byte ptr [edi + 0x6b7d5c]
            0x8D, 0x46, 0x10,                         // 4F: lea eax, [esi + 0x10]
            0xE8, 0x24, 0x00, 0x00, 0x00,             // 52: call 0x7b
            0x0F, 0xBE, 0x8F, null, null, null, null, // 57: movsx ecx, byte ptr [edi + 0x6b7d5d]
            0x8D, 0x46, 0x16,                         // 5E: lea eax, [esi + 0x16]
            0xE8, 0x15, 0x00, 0x00, 0x00,             // 61: call 0x7b
            0x85, 0xDB,                               // 66: test ebx, ebx
            0x75, 0x0D,                               // 68: jne 0x77
            0x8A, 0x87, null, null, null, null,       // 6A: mov al, byte ptr [edi + 0x6b7d5e]
            0x3C, 0x63,                               // 70: cmp al, 0x63
            0x74, 0x03,                               // 72: je 0x77
            0x88, 0x46, 0x31,                         // 74: mov byte ptr [esi + 0x31], al
            0x5B,                                     // 77: pop ebx
            0x5F,                                     // 78: pop edi
            0x5E,                                     // 79: pop esi
            0xC3,                                     // 7A: ret
            0x85, 0xDB,                               // 7B: test ebx, ebx
            0x74, 0x06,                               // 7D: je 0x85
            0x85, 0xC9,                               // 7F: test ecx, ecx
            0x7C, 0x02,                               // 81: jl 0x85
            0xF7, 0xD9,                               // 83: neg ecx
            0x50,                                     // 85: push eax
            0x0F, 0xBF, 0x00,                         // 86: movsx eax, word ptr [eax]
            0x83, 0xC1, 0x64,                         // 89: add ecx, 0x64
            0x0F, 0xAF, 0xC8,                         // 8C: imul ecx, eax
            0xB8, 0x1F, 0x85, 0xEB, 0x51,             // 8F: mov eax, 0x51eb851f
            0xF7, 0xE9,                               // 94: imul ecx
            0xC1, 0xFA, 0x05,                         // 96: sar edx, 5
            0x8B, 0xCA,                               // 99: mov ecx, edx
            0xC1, 0xE9, 0x1F,                         // 9B: shr ecx, 0x1f
            0x03, 0xD1,                               // 9E: add edx, ecx
            0x58,                                     // A0: pop eax
            0x66, 0x89, 0x10,                         // A1: mov word ptr [eax], dx
            0xC3,                                     // A4: ret
        }.Concat(Enumerable.Repeat((byte?)0xCC, 11)).ToArray();

        /// <summary>
        /// Where the two functions read the speech table, and what they read there: an entry's flags (+10),
        /// defence (+C), attack (+D) and hardness (+E).
        /// </summary>
        internal static readonly int[] OriginalTableRefs = { 0x21, 0x4D, 0x78, 0x98 }, PatchedTableRefs = { 0x1F, 0x4B, 0x5A, 0x6C };
        private static readonly uint[] TableFields = { 0x10, 0x0C, 0x0D, 0x0E };

        /// <inheritdoc/>
        public override string Name => "Unpassende Ansprachen in der Mannschaftssitzung schwächen die Mannschaft";
        /// <inheritdoc/>
        public override string Title => "Ansprachen";
        /// <inheritdoc/>
        public override PatchGroup Group => PatchGroup.Balancing;
        /// <inheritdoc/>
        public override uint Flag => 8192;
        /// <inheritdoc/>
        public override IEnumerable<byte?[]> Patterns => new[]
        {
            Between(RollHead, RollOriginal, RollTail), Between(RollHead, RollPatched, RollTail),
            Between(MarkHead, MarkOriginal, MarkTail), Between(MarkHead, MarkPatched, MarkTail),
            ApplyOriginal, ApplyPatched,
        };

        private static byte?[] Between(byte?[] head, byte[] code, byte[] tail) => head.Concat(Exact(code)).Concat(Exact(tail)).ToArray();

        /// <inheritdoc/>
        public override Site[] Find(byte[] image)
        {
            Site[] sites = { Place(image, RollHead, RollOriginal, RollPatched, RollTail), Place(image, MarkHead, MarkOriginal, MarkPatched, MarkTail), Apply(image) };
            return sites.Contains(null) ? null : sites;
        }

        // The block between head and tail, original or patched, where that occurs exactly once.
        private static Site Place(byte[] image, byte?[] head, byte[] original, byte[] patched, byte[] tail)
        {
            List<long> found = FindAll(image, Between(head, original, tail)), foundPatched = FindAll(image, Between(head, patched, tail));
            if (found.Count + foundPatched.Count != 1)
                return null;
            bool isPatched = foundPatched.Count == 1;
            return new Site { Offset = (isPatched ? foundPatched[0] : found[0]) + head.Length, Original = original, Patched = patched, IsPatched = isPatched };
        }

        // The function, original or patched, with the table address it reads.
        private static Site Apply(byte[] image)
        {
            List<long> found = FindAll(image, ApplyOriginal), foundPatched = FindAll(image, ApplyPatched);
            if (found.Count + foundPatched.Count != 1)
                return null;
            bool isPatched = foundPatched.Count == 1;
            long at = isPatched ? foundPatched[0] : found[0];
            int[] refs = isPatched ? PatchedTableRefs : OriginalTableRefs;
            uint[] tables = refs.Select((r, i) => BitConverter.ToUInt32(image, (int)at + r) - TableFields[i]).ToArray();
            if (tables.Distinct().Count() != 1)
                return null;
            return new Site
            {
                Offset = at, IsPatched = isPatched,
                Original = WithTable(ApplyOriginal, OriginalTableRefs, tables[0]), Patched = WithTable(ApplyPatched, PatchedTableRefs, tables[0]),
            };
        }

        /// <summary>The <paramref name="code"/> reading the speech table at <paramref name="table"/>.</summary>
        internal static byte[] WithTable(byte?[] code, int[] refs, uint table)
        {
            byte[] bytes = code.Select(b => b ?? 0).ToArray();
            for (int i = 0; i < refs.Length; i++)
                BitConverter.GetBytes(table + TableFields[i]).CopyTo(bytes, refs[i]);
            return bytes;
        }
    }

    /// <summary>
    /// Lets the limit of 3 negotiation dates per selling club wear off. Every club keeps a counter for
    /// each human manager (club +0x1081 + manager number 1-4); arranging a negotiation with one of its
    /// players counts it up to at most 10, and from 3 the club "would under no circumstances release"
    /// the player (text 2626, checks at 0x4CF0E3 and 0x53E55F in the original). The monthly club update
    /// (FUN_00642d00, called from FUN_00642e20 at the month change) is meant to halve the counters, but
    /// addresses +0x107D to +0x1080, one byte too low: it halves three bytes before them and the
    /// counter of a manager number 0 that doesn't exist, while the real counters only go back to 0 at
    /// the season start. The patch moves the four loads and four stores by 4:
    /// <code>
    ///   mov cl/al/bl/dl, [ebp+107D..1080] / shr x4 / push ebp / mov [ebp+107D..1080], cl/al/bl/dl
    ///   ->  the same with +1081..1084
    /// </code>
    /// </summary>
    internal sealed class NegotiationCounter : RewritePatch
    {
        private static byte?[] Code(byte first) => Exact(
            0x8A, 0x8D, first, 0x10, 0x00, 0x00, 0x8A, 0x85, (byte)(first + 1), 0x10, 0x00, 0x00,     // mov cl,[ebp+..] / mov al,[ebp+..]
            0x8A, 0x9D, (byte)(first + 2), 0x10, 0x00, 0x00, 0x8A, 0x95, (byte)(first + 3), 0x10, 0x00, 0x00,   // mov bl,[ebp+..] / mov dl,[ebp+..]
            0xD0, 0xE9, 0xD0, 0xE8, 0xD0, 0xEB, 0xD0, 0xEA, 0x55,                                       // shr cl/al/bl/dl,1 / push ebp
            0x88, 0x8D, first, 0x10, 0x00, 0x00, 0x88, 0x85, (byte)(first + 1), 0x10, 0x00, 0x00,     // mov [ebp+..],cl / mov [ebp+..],al
            0x88, 0x9D, (byte)(first + 2), 0x10, 0x00, 0x00, 0x88, 0x95, (byte)(first + 3), 0x10, 0x00, 0x00);  // mov [ebp+..],bl / mov [ebp+..],dl

        /// <inheritdoc/>
        internal override byte?[] OriginalCode => Code(0x7D);
        /// <inheritdoc/>
        internal override byte?[] PatchedCode => Code(0x81);

        /// <inheritdoc/>
        public override string Name => "Sperre nach drei Verhandlungsterminen mit einem Verein baut sich jeden Monat ab";
        /// <inheritdoc/>
        public override string Title => "Verhandlungssperre";
        /// <inheritdoc/>
        public override PatchGroup Group => PatchGroup.Balancing;
        /// <inheritdoc/>
        public override uint Flag => 16384;
    }

    /// <summary>
    /// Keeps the growth of investments off the bank balance. The game doesn't store the balance but
    /// works it out each time (FUN_00571060 in the original, given the finance block club+0x154):
    /// the ledger (+0) minus the current value of the active investments (FUN_0058bf30: 6 slots of
    /// 0x10 bytes from +0xA8, each its base +4 times today's index over the index at purchase) plus the loans
    /// (FUN_0058bf10) plus the season's bookings (FUN_00570f80). The ledger takes no interest, so every
    /// rise of an index takes as much off the balance (and its daily interest, overdraft included),
    /// and closing the investment (FUN_0058bec0) gives the value back and books value minus deposit
    /// as interest on top: the gain shows twice at once. The patch subtracts the deposits (+0 of each
    /// slot, which only buying writes; the season change sets only the base and index anew) instead of the values, in the same 48 bytes; the club's
    /// worth (ledger + values − loans) stays as it was. Nothing is stored, so switching it off shows
    /// the old balance again.
    /// <code>
    ///   push esi / mov esi,[esp+8] / push edi / push esi / call values / mov edi,[esi] / add esp,4 / sub edi,eax /
    ///   push esi / call loans / add esp,4 / add edi,eax / push esi / call bookings / add esp,4 / add eax,edi /
    ///   pop edi / pop esi / ret / nop x4
    /// ->
    ///   push esi / mov esi,[esp+8] / push esi / call loans / add eax,[esi] / sub esi,-80 / push 6 / pop ecx /
    ///   next: test byte [esi+33],1 / je skip / sub eax,[esi+28] / skip: add esi,10 / loop next /
    ///   pop esi / push eax / push esi / call bookings / pop ecx / pop ecx / add eax,ecx / pop esi / ret / nop
    /// </code>
    /// Taking it back needs the call to "values" again: that function follows "loans" 0x20 bytes on in
    /// every build, which the original code must show to be offered.
    /// </summary>
    internal sealed class InvestmentBalance : Patch
    {
        private static readonly byte?[] OriginalPattern =
        {
            0x56, 0x8B, 0x74, 0x24, 0x08, 0x57, 0x56, 0xE8, null, null, null, null, 0x8B, 0x3E, 0x83, 0xC4, 0x04, 0x2B, 0xF8,
            0x56, 0xE8, null, null, null, null, 0x83, 0xC4, 0x04, 0x03, 0xF8,
            0x56, 0xE8, null, null, null, null, 0x83, 0xC4, 0x04, 0x03, 0xC7, 0x5F, 0x5E, 0xC3, 0x90, 0x90, 0x90, 0x90,
        };
        private const int OriginalValues = 8, OriginalLoans = 21, OriginalBookings = 32;   // the calls' displacements

        private static readonly byte?[] PatchedPattern =
        {
            0x56, 0x8B, 0x74, 0x24, 0x08, 0x56, 0xE8, null, null, null, null, 0x03, 0x06, 0x83, 0xEE, 0x80, 0x6A, 0x06, 0x59,
            0xF6, 0x46, 0x33, 0x01, 0x74, 0x03, 0x2B, 0x46, 0x28, 0x83, 0xC6, 0x10, 0xE2, 0xF2,
            0x5E, 0x50, 0x56, 0xE8, null, null, null, null, 0x59, 0x59, 0x03, 0xC1, 0x5E, 0xC3, 0x90,
        };
        private const int PatchedLoans = 7, PatchedBookings = 37;

        // "values" lies this far behind "loans", seen from the original's two calls.
        private const int ValuesBehindLoans = 0x20 + (OriginalLoans + 4) - (OriginalValues + 4);

        /// <summary>The bytes of <paramref name="pattern"/> with each pair's displacement written at its place.</summary>
        private static byte[] Code(byte?[] pattern, params int[] displacementAndPlace)
        {
            byte[] code = pattern.Select(b => b ?? 0).ToArray();
            for (int i = 0; i < displacementAndPlace.Length; i += 2)
                BitConverter.GetBytes(displacementAndPlace[i]).CopyTo(code, displacementAndPlace[i + 1]);
            return code;
        }

        /// <summary>The original code that calls loans and bookings with these displacements (values follows loans).</summary>
        internal static byte[] OriginalBytes(int loans, int bookings) =>
            Code(OriginalPattern, loans + ValuesBehindLoans, OriginalValues, loans, OriginalLoans, bookings, OriginalBookings);

        /// <summary>The patched code for the original's displacements of loans and bookings.</summary>
        internal static byte[] PatchedBytes(int loans, int bookings) =>
            Code(PatchedPattern, loans + (OriginalLoans - PatchedLoans), PatchedLoans, bookings + (OriginalBookings - PatchedBookings), PatchedBookings);

        /// <inheritdoc/>
        public override string Name => "Wertzuwachs von Geldanlagen nicht mehr vom Kontostand abziehen";
        /// <inheritdoc/>
        public override string Title => "Geldanlagen";
        /// <inheritdoc/>
        public override PatchGroup Group => PatchGroup.Patches;
        /// <inheritdoc/>
        public override uint Flag => 32768;
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
            int Displacement(int place) => BitConverter.ToInt32(image, (int)at + place);
            int loans, bookings;
            if (isPatched)
            {
                loans = Displacement(PatchedLoans) - (OriginalLoans - PatchedLoans);
                bookings = Displacement(PatchedBookings) - (OriginalBookings - PatchedBookings);
            }
            else
            {
                loans = Displacement(OriginalLoans);
                bookings = Displacement(OriginalBookings);
                if (Displacement(OriginalValues) != loans + ValuesBehindLoans)
                    return null;
            }
            return new[] { new Site { Offset = at, Original = OriginalBytes(loans, bookings), Patched = PatchedBytes(loans, bookings), IsPatched = isPatched } };
        }
    }

    /// <summary>
    /// Brings the pool of trainers and managers better newcomers. Whoever retires from it (about 66,
    /// unemployed) makes room for the next former player on a waiting list per country group
    /// (FUN_005a1500 in the original, 20 places at 0x826BDE; goalkeeper trainers FUN_005a15c0, 10 at
    /// 0x826EFE). A player only gets on the list if he ends his career at 36 or older (FUN_005a12f0),
    /// which outfield players rarely do, so the list stays empty and the place goes back to the same
    /// person: age 30, competence 2 (1 in the other trade), goalkeeper trainers age 35 and 2. They
    /// grow too little to get far, and in the long run the strong ones die out. The patch:
    /// <code>
    ///   former players on the list:   at 36 or older          ->  at 33 or older (cmp dl,23 -> 20)
    ///   the place back without one:   competence 2             ->  3 + four times random(3), 3 to 11
    ///                                                              around 7 (in the trade that was higher)
    /// </code>
    /// Both functions are rewritten in their own bytes (shorter, so the dice fit) and otherwise do what
    /// they did. They call the game's random function, which the patch takes from the call right behind
    /// the age check (push 3 / call random), in the same section as both.
    /// </summary>
    internal sealed class CoachSupply : Patch
    {
        /// <summary>mov dl,[esi+1E] (the age) / add ax,[esi+80 or 82] / cmp dl,{age} / jbe (not on the list)</summary>
        private static readonly byte?[] AgePattern =
        {
            0x8A, 0x56, 0x1E, 0x66, 0x03, 0x86, null, 0x00, 0x00, 0x00, 0x80, 0xFA, null, 0x0F, 0x86, 0x9E, 0x01, 0x00, 0x00,
        };
        private const int AgeAt = 12;
        /// <summary>The highest age that doesn't get on the list, original and patched.</summary>
        internal const byte AgeOriginal = 35, AgePatched = 32;
        /// <summary>push 3 / call random, this far behind the age check.</summary>
        private static readonly byte[] RandomCall = { 0x6A, 0x03, 0xE8 };
        private const int RandomCallAt = 0x52;

        // FUN_005a1500: the next former player of the group, or the place back with 1 and 2.
        private static readonly byte?[] CoachOriginal =
        {
            0x0F, 0xBF, 0x44, 0x24, 0x08, 0x53, 0x66, 0x0F, 0xB6, 0x88, null, null, null, null, 0x0F, 0xBF,
            0xC9, 0x33, 0xC0, 0x8D, 0x0C, 0x89, 0xC1, 0xE1, 0x02, 0x0F, 0xBF, 0xD0, 0x03, 0xD1, 0x66, 0x83,
            0x3C, 0xD5, null, null, null, null, 0x00, 0x75, 0x2D, 0x40, 0x66, 0x3D, 0x14, 0x00, 0x7C, 0xE9,
            0x8B, 0x44, 0x24, 0x08, 0x8A, 0x48, 0x05, 0x8A, 0x50, 0x04, 0x3A, 0xCA, 0xC6, 0x40, 0x07, 0x1E,
            0x7E, 0x0A, 0xC6, 0x40, 0x05, 0x02, 0xC6, 0x40, 0x04, 0x01, 0x5B, 0xC3, 0xC6, 0x40, 0x05, 0x01,
            0xC6, 0x40, 0x04, 0x02, 0x5B, 0xC3, 0x0F, 0xBF, 0xC0, 0x03, 0xC1, 0x8B, 0x4C, 0x24, 0x08, 0xC1,
            0xE0, 0x03, 0x8A, 0x59, 0x04, 0x66, 0x8B, 0x90, null, null, null, null, 0x66, 0x89, 0x11, 0x66,
            0x8B, 0x90, null, null, null, null, 0x66, 0x89, 0x51, 0x02, 0x8A, 0x51, 0x05, 0x3A, 0xD3, 0x8A,
            0x90, null, null, null, null, 0x7E, 0x11, 0x88, 0x51, 0x05, 0x0F, 0xBE, 0xD2, 0x8A, 0x92, null,
            null, null, null, 0x88, 0x51, 0x04, 0xEB, 0x0F, 0x88, 0x51, 0x04, 0x0F, 0xBE, 0xD2, 0x8A, 0x92,
            null, null, null, null, 0x88, 0x51, 0x05, 0x8A, 0x90, null, null, null, null, 0x5B, 0x88, 0x51,
            0x07, 0x66, 0xC7, 0x80, null, null, null, null, 0x00, 0x00, 0xC3,
        };
        private static readonly int[] CoachOriginalPlaces = { 10, 34, 104, 114, 129, 143, 160, 169, 180 };

        // The same, the place back with 3 to 11:
        //   push ebx / push esi / push edi / mov edi,[esp+10] / movsx eax,word [esp+14] / movzx eax,byte [eax+group] /
        //   lea eax,[eax+eax*4] / shl eax,5 / lea esi,[eax+list] / push 20 / pop ecx /
        //   next: cmp word [esi],0 / jne found / add esi,8 / loop next /
        //   mov bl,3 / push 4 / roll: push 3 / call random / add bl,al / pop eax / dec dword [esp] / jnz roll / pop eax /
        //   mov al,30 / mov ah,1 / jmp store /
        //   found: mov eax,[esi] / mov [edi],eax / mov bl,[esi+6] / mov word [esi],0 / movzx eax,bl / mov ah,[eax+table] / mov al,[esi+4] /
        //   store: mov [edi+7],al / mov cl,[edi+4] / cmp cl,[edi+5] / jl manager / mov [edi+4],bl / mov [edi+5],ah / jmp done /
        //   manager: mov [edi+5],bl / mov [edi+4],ah / done: pop edi / pop esi / pop ebx / ret / nop...
        private static readonly byte?[] CoachPatched =
        {
            0x53, 0x56, 0x57, 0x8B, 0x7C, 0x24, 0x10, 0x0F, 0xBF, 0x44, 0x24, 0x14, 0x0F, 0xB6, 0x80, null,
            null, null, null, 0x8D, 0x04, 0x80, 0xC1, 0xE0, 0x05, 0x8D, 0xB0, null, null, null, null, 0x6A,
            0x14, 0x59, 0x66, 0x83, 0x3E, 0x00, 0x75, 0x1F, 0x83, 0xC6, 0x08, 0xE2, 0xF5, 0xB3, 0x03, 0x6A,
            0x04, 0x6A, 0x03, 0xE8, null, null, null, null, 0x02, 0xD8, 0x58, 0xFF, 0x0C, 0x24, 0x75, 0xF1,
            0x58, 0xB0, 0x1E, 0xB4, 0x01, 0xEB, 0x18, 0x8B, 0x06, 0x89, 0x07, 0x8A, 0x5E, 0x06, 0x66, 0xC7,
            0x06, 0x00, 0x00, 0x0F, 0xB6, 0xC3, 0x8A, 0xA0, null, null, null, null, 0x8A, 0x46, 0x04, 0x88,
            0x47, 0x07, 0x8A, 0x4F, 0x04, 0x3A, 0x4F, 0x05, 0x7C, 0x08, 0x88, 0x5F, 0x04, 0x88, 0x67, 0x05,
            0xEB, 0x06, 0x88, 0x5F, 0x05, 0x88, 0x67, 0x04, 0x5F, 0x5E, 0x5B, 0xC3, 0x90, 0x90, 0x90, 0x90,
            0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90,
            0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90,
            0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90,
            0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90,
        };
        private static readonly int[] CoachPatchedPlaces = { 15, 27, 52, 88 };

        // FUN_005a15c0: the same for goalkeeper trainers, the place back with age 35 and 2.
        private static readonly byte?[] GoalkeeperOriginal =
        {
            0x0F, 0xBF, 0x44, 0x24, 0x08, 0x66, 0x0F, 0xB6, 0x88, null, null, null, null, 0x0F, 0xBF, 0xC9,
            0x33, 0xC0, 0x8D, 0x0C, 0x89, 0xD1, 0xE1, 0x0F, 0xBF, 0xD0, 0x03, 0xD1, 0x66, 0x83, 0x3C, 0xD5,
            null, null, null, null, 0x00, 0x75, 0x14, 0x40, 0x66, 0x3D, 0x0A, 0x00, 0x7C, 0xE9, 0x8B, 0x44,
            0x24, 0x04, 0xC6, 0x40, 0x07, 0x23, 0xC6, 0x40, 0x04, 0x02, 0xC3, 0x0F, 0xBF, 0xC0, 0x03, 0xC1,
            0x8B, 0x4C, 0x24, 0x04, 0xC1, 0xE0, 0x03, 0x66, 0x8B, 0x90, null, null, null, null, 0x66, 0x89,
            0x11, 0x66, 0x8B, 0x90, null, null, null, null, 0x66, 0x89, 0x51, 0x02, 0x8A, 0x90, null, null,
            null, null, 0x88, 0x51, 0x04, 0x8A, 0x90, null, null, null, null, 0x88, 0x51, 0x07, 0x66, 0xC7,
            0x80, null, null, null, null, 0x00, 0x00, 0xC3,
        };
        private static readonly int[] GoalkeeperOriginalPlaces = { 9, 32, 74, 84, 94, 103, 113 };

        // As the trainers' (imul eax,eax,50 / push 10 for the ten places), without the other trade:
        //   ... / mov al,35 / jmp store / found: ... / store: mov [edi+7],al / mov [edi+4],bl / pop edi / ...
        private static readonly byte?[] GoalkeeperPatched =
        {
            0x53, 0x56, 0x57, 0x8B, 0x7C, 0x24, 0x10, 0x0F, 0xBF, 0x44, 0x24, 0x14, 0x0F, 0xB6, 0x80, null,
            null, null, null, 0x6B, 0xC0, 0x50, 0x8D, 0xB0, null, null, null, null, 0x6A, 0x0A, 0x59, 0x66,
            0x83, 0x3E, 0x00, 0x75, 0x1D, 0x83, 0xC6, 0x08, 0xE2, 0xF5, 0xB3, 0x03, 0x6A, 0x04, 0x6A, 0x03,
            0xE8, null, null, null, null, 0x02, 0xD8, 0x58, 0xFF, 0x0C, 0x24, 0x75, 0xF1, 0x58, 0xB0, 0x23,
            0xEB, 0x0F, 0x8B, 0x06, 0x89, 0x07, 0x8A, 0x5E, 0x06, 0x66, 0xC7, 0x06, 0x00, 0x00, 0x8A, 0x46,
            0x04, 0x88, 0x47, 0x07, 0x88, 0x5F, 0x04, 0x5F, 0x5E, 0x5B, 0xC3, 0x90, 0x90, 0x90, 0x90, 0x90,
            0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90,
            0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90,
        };
        private static readonly int[] GoalkeeperPatchedPlaces = { 15, 24, 49 };
        // push ebx: how both patched functions start (the originals with movsx).
        private const byte PushEbx = 0x53;

        /// <inheritdoc/>
        public override string Name => "Neue Trainer: Ex-Spieler ab 33 Jahren, sonst Kompetenz 3 bis 11 statt 2";
        /// <inheritdoc/>
        public override string Title => "Trainer-Nachwuchs";
        /// <inheritdoc/>
        public override PatchGroup Group => PatchGroup.Balancing;
        /// <inheritdoc/>
        public override uint Flag => 65536;
        /// <inheritdoc/>
        public override IEnumerable<byte?[]> Patterns => new[] { AgePattern, CoachOriginal, CoachPatched, GoalkeeperOriginal, GoalkeeperPatched };

        /// <summary>The bytes of <paramref name="pattern"/> with the values at their places.</summary>
        private static byte[] Fill(byte?[] pattern, int[] places, params uint[] values)
        {
            byte[] code = pattern.Select(b => b ?? 0).ToArray();
            for (int i = 0; i < places.Length; i++)
                BitConverter.GetBytes(values[i]).CopyTo(code, places[i]);
            return code;
        }

        /// <summary>The trainers' original function for the group table, the waiting lists and the table of the other trade's competence.</summary>
        internal static byte[] CoachOriginalBytes(uint group, uint list, uint table) =>
            Fill(CoachOriginal, CoachOriginalPlaces, group, list, list, list + 2, list + 6, table, table, list + 4, list);

        /// <summary>The trainers' patched function; <paramref name="random"/> is the random function's distance to its start.</summary>
        internal static byte[] CoachPatchedBytes(uint group, uint list, uint table, int random) =>
            Fill(CoachPatched, CoachPatchedPlaces, group, list, (uint)(random - (CoachPatchedPlaces[2] + 4)), table);

        /// <summary>The goalkeeper trainers' original function for the group table and their waiting lists.</summary>
        internal static byte[] GoalkeeperOriginalBytes(uint group, uint list) =>
            Fill(GoalkeeperOriginal, GoalkeeperOriginalPlaces, group, list, list, list + 2, list + 6, list + 4, list);

        /// <summary>The goalkeeper trainers' patched function; <paramref name="random"/> is the random function's distance to its start.</summary>
        internal static byte[] GoalkeeperPatchedBytes(uint group, uint list, int random) =>
            Fill(GoalkeeperPatched, GoalkeeperPatchedPlaces, group, list, (uint)(random - (GoalkeeperPatchedPlaces[2] + 4)));

        // Where the one place of either pattern starts, or -1.
        private static long One(byte[] image, byte?[] original, byte?[] patched)
        {
            List<long> found = FindAll(image, original).Concat(FindAll(image, patched)).ToList();
            return found.Count == 1 ? found[0] : -1;
        }

        /// <inheritdoc/>
        public override Site[] Find(byte[] image)
        {
            List<long> ages = FindAll(image, AgePattern).Where(at => image[at + AgeAt] == AgeOriginal || image[at + AgeAt] == AgePatched).ToList();
            long coach = One(image, CoachOriginal, CoachPatched), keeper = One(image, GoalkeeperOriginal, GoalkeeperPatched);
            if (ages.Count != 1 || coach < 0 || keeper < 0 || !image.Slice(ages[0] + RandomCallAt, RandomCall.Length).SequenceEqual(RandomCall))
                return null;
            long age = ages[0], call = age + RandomCallAt + 2;
            long random = call + 5 + BitConverter.ToInt32(image, (int)call + 1);
            uint U32(long at) => BitConverter.ToUInt32(image, (int)at);
            bool coachPatched = image[coach] == PushEbx, keeperPatched = image[keeper] == PushEbx;
            int[] places = coachPatched ? new[] { 15, 27, 88 } : new[] { 10, 34, 143 };
            uint group = U32(coach + places[0]), list = U32(coach + places[1]), table = U32(coach + places[2]);
            uint keeperList = U32(keeper + (keeperPatched ? 24 : 32));
            Site[] sites =
            {
                new Site { Offset = age + AgeAt, Original = new[] { AgeOriginal }, Patched = new[] { AgePatched }, IsPatched = image[age + AgeAt] == AgePatched },
                new Site
                {
                    Offset = coach, Original = CoachOriginalBytes(group, list, table),
                    Patched = CoachPatchedBytes(group, list, table, (int)(random - coach)), IsPatched = coachPatched,
                },
                new Site
                {
                    Offset = keeper, Original = GoalkeeperOriginalBytes(group, keeperList),
                    Patched = GoalkeeperPatchedBytes(group, keeperList, (int)(random - keeper)), IsPatched = keeperPatched,
                },
            };
            // Each place holds one of its two versions exactly (which also checks that both lists use the same group table).
            return sites.All(s => image.Slice(s.Offset, s.Original.Length).SequenceEqual(s.IsPatched ? s.Patched : s.Original)) ? sites : null;
        }
    }

    /// <summary>
    /// Keeps the club's money from running over. The game books a club's week once, at the end of
    /// FUN_005722c0 in the original: the ledger at club +0x154 (an Int32, see <see cref="InvestmentBalance"/>)
    /// plus the week's result after tax, unchecked. Past 2,147,483,647 DM it turns into about -2.1 billion,
    /// and the game takes the club for broke (overdraft interest, a forced loan). The patch stops the
    /// ledger at 2,000,000,000 DM: the bank balance adds the week's bookings and the loans to it, so it
    /// keeps about 147 million DM of room before it could run over itself. A ledger that is higher
    /// already (set with a trainer) stays as it is and only stops growing; a sum below -2^31 stays at the
    /// lowest value.
    /// <para>
    /// The check needs more bytes than the booking has. The patch makes room in FUN_00572660, the week's
    /// result: it adds up the same fields as the bookings FUN_00570f80 (given club +0x154), except that
    /// the bookings also subtract +0x338 (the tax booked) and add +0x33C. Rewritten, it calls the
    /// bookings and takes those two back (the same result, the same calls in the same order) in 29 of its
    /// 208 bytes; the check follows, and the booking jumps to it:
    /// </para>
    /// <code>
    ///   week:    mov eax,[esp+4] / add eax,154 / push eax / call bookings / pop ecx / add eax,[ecx+338] /
    ///            sub eax,[ecx+33C] / ret / nop x3
    ///   check:   mov ecx,[esi+154] / mov eax,ecx / add eax,edi / jno sum / mov eax,edi / sar eax,31 /
    ///            xor eax,7FFFFFFF / sum: cmp eax,limit / jle store / cmp eax,ecx / jle store /
    ///            mov eax,limit / cmp eax,ecx / jge store / mov eax,ecx /
    ///            store: pop edi / mov [esi+154],eax / pop esi / pop ebx / add esp,24 / ret / nop...
    ///   booking: mov eax,[esi+154] / add eax,edi / pop edi / mov [esi+154],eax / pop esi / pop ebx /
    ///            add esp,24 / ret / nop x2   ->  jmp check / nop x18
    /// </code>
    /// The bookings lie 0x100 bytes behind the first function the week's result calls, in every build;
    /// their own three calls give the original's back.
    /// </summary>
    internal sealed class MoneyLimit : Patch
    {
        /// <summary>The highest ledger the weekly booking leads to.</summary>
        internal const int Limit = 2000000000;

        /// <summary>The bytes of <paramref name="hex"/> with a wildcard displacement behind each call at <paramref name="calls"/>.</summary>
        private static byte?[] Code(string hex, params int[] calls)
        {
            byte?[] code = hex.Hex().Select(b => (byte?)b).ToArray();
            foreach (int call in calls)
                for (int i = 1; i <= 4; i++)
                    code[call + i] = null;
            return code;
        }

        // FUN_00572660: push esi / push edi / lea edi,[eax+154] / call first ... call (0x570F60) ... call (0x570E30) / ... / ret.
        private static readonly byte?[] WeekOriginal = Code(
            "8B44240456578DB85401000057E80EE8FFFF8BB73C0200008B97280300008B8F580200002BF28B971803000003F18B8F1C030000"
            + "03F28B970802000003F18B8F000200002BF28B972C0300002BF18B8F1802000003F28B97100200002BF18B8F540200002BF2"
            + "8B97AC0100002BF18B8FB40100002BF28B97800100002BF18B8FF80100002BF28B97F00100002BF18B8FDC0100002BF2"
            + "8B977C0100002BF18B8F7401000003F28B976C01000083C40403F103F2572BF0E845E8FFFF83C4042BF057E80AE7FFFF"
            + "83C40403F08BC65F5EC3", 0x0D, 0xB6, 0xC1);
        private static readonly int[] WeekCalls = { 0x0D, 0xB6, 0xC1 };

        // The week's result rewritten (call bookings at 0x0A), the check from 0x20, nops to the original's 208 bytes.
        private static readonly byte?[] WeekPatched = Code(
            "8B442404" + "0554010000" + "50" + "E800000000" + "59" + "038138030000" + "2B813C030000" + "C3909090"
            + "8B8E540100008BC103C7710A8BC7C1F81F35FFFFFF7F3D" + Le(Limit) + "7E0F3BC17E0BB8" + Le(Limit)
            + "3BC17D028BC15F8986540100005E5B83C424C3"
            + string.Concat(Enumerable.Repeat("90", 208 - 0x59)), 0x0A);
        private const int WeekPatchedCall = 0x0A;
        /// <summary>Where the check starts in the rewritten week's result.</summary>
        internal const int CheckAt = 0x20;

        // FUN_00570F80, the bookings of the finance block: calls first, ... (0x570F60) ... (0x570E30).
        private static readonly byte?[] BookingsCode = Code(
            "568B7424085756E8F4FEFFFF8BBE6C0100008B96800100008B8EB40100002BFA8B96AC0100002BF98B8EDC0100002BFA"
            + "8B96F80100002BF98B8EF00100002BFA8B967C0100002BF98B8E7401000003FA8B963C02000003F98B8E3803000003FA"
            + "8B96280300002BF98B8E580200002BFA8B962C03000003F98B8E1802000003FA8B96100200002BF98B8E3C0300002BFA"
            + "8B960802000003F98B8E000200002BFA8B96180300002BF98B8E1C03000003FA8B965402000083C40403F92BFA562BF8"
            + "E81BFFFFFF83C4042BF856E8E0FDFFFF83C40403C75F5EC3", 0x07, 0xC0, 0xCB);
        private static readonly int[] BookingsCalls = { 0x07, 0xC0, 0xCB };
        private const int BookingsBehindFirst = 0x100;

        // The booking, behind the call that ends the human clubs' statistics (call / add esp,8).
        private static readonly byte?[] BookingOriginal = Code("E80000000083C408" + "8B865401000003C75F8986540100005E5B83C424C39090", 0);
        private static readonly byte?[] BookingPatched = Code("E80000000083C408" + "E900000000" + string.Concat(Enumerable.Repeat("90", 18)), 0, 8);
        private const int BookingAt = 8;

        private static string Le(int value) => string.Concat(BitConverter.GetBytes(value).Select(b => b.ToString("X2")));

        /// <inheritdoc/>
        public override string Name => "Vereinskonto stoppt bei 2 Mrd. DM, statt ins Minus zu kippen";
        /// <inheritdoc/>
        public override string Title => "Kontostand";
        /// <inheritdoc/>
        public override PatchGroup Group => PatchGroup.Patches;
        /// <inheritdoc/>
        public override uint Flag => 131072;
        /// <inheritdoc/>
        public override IEnumerable<byte?[]> Patterns => new[] { WeekOriginal, WeekPatched, BookingOriginal, BookingPatched };

        // The bytes of the pattern with the targets of its calls (or jumps) at places.
        private static byte[] Fill(byte?[] pattern, int[] places, long at, params long[] targets)
        {
            byte[] code = pattern.Select(b => b ?? 0).ToArray();
            for (int i = 0; i < places.Length; i++)
                BitConverter.GetBytes((int)(targets[i] - (at + places[i] + 5))).CopyTo(code, places[i] + 1);
            return code;
        }

        /// <summary>The week's result as the game has it at <paramref name="at"/>, calling first, second and third.</summary>
        internal static byte[] WeekOriginalBytes(long at, long first, long second, long third) => Fill(WeekOriginal, WeekCalls, at, first, second, third);

        /// <summary>The week's result rewritten at <paramref name="at"/>, calling the bookings at <paramref name="bookings"/>.</summary>
        internal static byte[] WeekPatchedBytes(long at, long bookings) => Fill(WeekPatched, new[] { WeekPatchedCall }, at, bookings);

        // Where the one place of either pattern starts, or -1.
        private static long One(byte[] image, byte?[] original, byte?[] patched)
        {
            List<long> found = FindAll(image, original).Concat(FindAll(image, patched)).ToList();
            return found.Count == 1 ? found[0] : -1;
        }

        /// <inheritdoc/>
        public override Site[] Find(byte[] image)
        {
            long week = One(image, WeekOriginal, WeekPatched), booking = One(image, BookingOriginal, BookingPatched);
            if (week < 0 || booking < 0)
                return null;
            booking += BookingAt;
            bool weekPatched = image[week + WeekPatchedCall] == 0xE8, bookingPatched = image[booking] == 0xE9;
            long bookings = weekPatched ? Target(image, week + WeekPatchedCall) : Target(image, week + WeekCalls[0]) + BookingsBehindFirst;
            if (bookings < 0 || FindAll(image.Slice(bookings, BookingsCode.Length), BookingsCode).Count != 1)
                return null;
            long[] calls = BookingsCalls.Select(at => Target(image, bookings + at)).ToArray();
            if (calls[0] + BookingsBehindFirst != bookings
                || !weekPatched && !WeekCalls.Select(at => Target(image, week + at)).SequenceEqual(calls)
                || bookingPatched && Target(image, booking) != week + CheckAt)
                return null;
            byte[] jump = new byte[] { 0xE9, 0, 0, 0, 0 }.Concat(Enumerable.Repeat((byte)0x90, 18)).ToArray();
            BitConverter.GetBytes((int)(week + CheckAt - (booking + 5))).CopyTo(jump, 1);
            return new[]
            {
                new Site { Offset = week, Original = WeekOriginalBytes(week, calls[0], calls[1], calls[2]), Patched = WeekPatchedBytes(week, bookings), IsPatched = weekPatched },
                new Site { Offset = booking, Original = BookingOriginal.Skip(BookingAt).Select(b => b.Value).ToArray(), Patched = jump, IsPatched = bookingPatched },
            };
        }
    }

    /// <summary>
    /// Makes random events do what their message says (texts of TEXTE\EreigGld.saq, places in the original):
    /// <code>
    ///   690 free copier "Sie sparen ..."       table 0x6BD058 {690,-120} books an expense    ->  {690,+120}
    ///   697 prize draw "Kostenpunkt ..."        table 0x6BD090 {697,+80} books an income      ->  {697,-80}
    ///   126 fan club tournament, cups cost      0x556D31 jmp to the income booking            ->  to the expense booking
    ///   143 the club sells a plot, receives     0x557050 mov/add/mov [esi+38C] (expenses)     ->  [esi+390] (income)
    ///   172 fans collect donations              0x5578B0 jmp to the expense booking           ->  to the income booking
    ///   224 chocolate sponsor, won negotiation  0x558662 books the plain amount               ->  twice the amount, as offered
    ///   307, 308, 369, 414 "verliert ... Frischepunkte"   push -5 / -10 to FUN_00623cc0      ->  push 5 / 10
    ///    56 hints for the co-trainer, form up   0x563CBC sound 2 (negative)                   ->  1 (positive)
    ///    38 a concussion in training            0x5632D7 injures the first of the two players ->  the second, whom the text names
    ///   392 a player ages, loses strength       0x560124 with ability bit 4 appends text 393  ->  text 392 alone
    ///   732 fish poisoning                      table 0x6C9534 {62, 20}: kind 62 has no name  ->  {68, 20}: "Fischvergiftung" (7 days)
    ///   284 riot, the club is sued              0x55933E shows 283 (all calm) with no amounts ->  284 with both amounts
    ///   339 bonus, "je einen Formpunkt"         0x55F6C0 books the bonus only                 ->  and every player form +1 (FUN_00565a50)
    ///   343 banana skin, "verliert einen Formpunkt"  0x55F77D raises the mood only            ->  and the player form -1 (FUN_00565bc0)
    ///   876/877 building delay, reason      0x55E5F3 random(10) of the 12 in 0x6BD3A0     ->  random(12): 888 "einer Fanblockade", 889 the weather
    /// </code>
    /// The money events (FUN_00556900) book +0x38C as expenses and +0x390 as income; the two booking ends
    /// are alike, so 126 and 172 trade their jump targets (their displacements are the same in every build).
    /// FUN_00623cc0 takes freshness (+0x31) into the recovery pool (+0x33) for a positive value and gives it
    /// back for a negative one; training and matches pass positive values. The sponsor's question offers
    /// twice the amount for a negotiation that works out (it stores 2*edi for the text), but its success
    /// books edi like accepting at once. The patch doubles edi there, in the same 18 bytes, comparing the
    /// question's outcome (a small number) as a byte instead of a word:
    /// <code>
    ///   cmp word [outcome],ax / push ebx / jne fail / push 3 / push 0 / push 0 / push 0 / call
    ///   ->  cmp byte [outcome],al / push ebx / jne fail / add edi,edi / push 3 / xor eax,eax / push eax x3 / call
    /// </code>
    /// The event plays the sound its message is given: 0 neutral, 1 positive, 2 negative.
    /// <para>
    /// For 339 the booking is written shorter (add [ebp+330],esi), which makes room for push 1 / push ebp /
    /// call; one add esp takes off both calls' arguments. For 343 the player (the 2nd and 3rd argument of
    /// the message) is pushed once more under them, with -1; it stays on the stack for FUN_00565bc0, and
    /// the event ends with a jump to the same ending of the case before (mov ax,1 / ... / ret). Both
    /// functions, like that ending, lie as far from the places in every build.
    /// </para>
    /// <para>
    /// The delay's message is "verzögern sich wegen %s"; reason 889 reads "wegen schlechtem Wetter" and would
    /// give "wegen wegen". Its two branches (one or two weeks, 0x55E604) are folded into one, which makes room
    /// to skip a reason's first 6 characters when it starts with "wege"; the jump at the end skips the
    /// original's last address and displacement, which taking the patch back needs:
    /// </para>
    /// <code>
    ///   push 2 / call random / pop ecx / movsx esi,si / neg eax / sbb eax,eax / inc eax (1: two weeks) / push eax /
    ///   mov ecx,texts / push edi / call text / cmp dword [eax],"wege" / jne / add eax,6 / push eax / lea ecx,[esi+41] /
    ///   push ecx / mov eax,[esp+8] / add eax,36C / push eax / mov ecx,texts / call text / push eax / lea edx,[esp+34] /
    ///   push edx / call sprintf / add esp,10 / pop eax / mov cl,[ebx+esi+884] / lea eax,[eax+ecx+1] / jmp message
    /// </code>
    /// <para>
    /// The referee events (FUN_005ee7f0) hurt a player through FUN_00633a60: the kind from a table of two
    /// shorts at 0x6C9534 {62, 20}, the days from the kinds' table (kind 20, "Kapselriß im Knie", 28 days,
    /// fits text 734). Kind 62 has no name; the patch enters 68, "Fischvergiftung", with its 7 days. The
    /// three weeks the message names are a switch of their own (<see cref="FishPoisoning"/>).
    /// </para>
    /// <para>
    /// The riot (FUN_005591c0, case 3) books the lawsuit's settlement (0x78BB10) but shows text 283 and
    /// formats both amounts for it, into the one buffer of the money format (FUN_004a6dd0), so either would
    /// show the same. The texts are kept in the table of FUN_00559b30 (ecx 0x7836E8, [ecx+4+number*4]), so
    /// text 282 needs no copy. Rewritten in the same 132 bytes, the settlement's amount is copied (32 bytes)
    /// into the first buffer before the claim (0x78BB84) is formatted:
    /// </para>
    /// <code>
    ///   mov ecx,texts / mov ebp,[ecx+46C] / mov esi,[settlement] / push esi / call money / mov esi,eax /
    ///   lea edi,[esp+3C] / push 8 / pop ecx / rep movsd / push [claim] / call money / lea ecx,[esp+40] /
    ///   mov [esp],ecx / push eax / mov ecx,texts / push [ecx+474] / lea edx,[esp+23C] / push edx / call sprintf /
    ///   add esp,10 / pop esi / lea eax,[esp+22C] / push eax / push ebp / push 2 / push 5B / push ebx / nop...
    /// </code>
    /// </summary>
    internal sealed class EventEffects : RewritesPatch
    {
        /// <inheritdoc/>
        internal override byte?[][][] Places => EventPlaces;

        /// <summary>Each place as original and patched code.</summary>
        internal static readonly byte?[][][] EventPlaces =
        {
            Place("B202000088FFFFFF", "B202000078000000"),                                          // 690
            Place("B902000050000000", "B9020000B0FFFFFF"),                                          // 697
            Place("6A7E6A6256E8????????83C414566A006A016A00E8????????83C410E9E70A0000",               // 126
                  "6A7E6A6256E8????????83C414566A006A016A00E8????????83C410E923120000"),
            Place("688F0000006A6256E8????????8BAE8C03000083C41403EF6AF65389AE8C030000",               // 143
                  "688F0000006A6256E8????????8BAE9003000083C41403EF6AF65389AE90030000"),
            Place("68AC0000006A6256E8????????E9A1060000", "68AC0000006A6256E8????????E965FFFFFF"),     // 172
            Place("6685C00F85????????663905????????5375156A036A006A006A00E8",                       // 224
                  "6685C00F85????????3805????????53751603FF6A0333C0505050E8"),
            Place("68330100006A6055E8????????83C4146AFE56E8????????83C4086AFB56E8",                   // 307
                  "68330100006A6055E8????????83C4146AFE56E8????????83C4086A0556E8"),
            Place("68340100006A6055E8????????83C4146AFB56E8", "68340100006A6055E8????????83C4146A0556E8"),   // 308
            Place("68710100006A6055E8????????83C4146AF656E8", "68710100006A6055E8????????83C4146A0A56E8"),   // 369
            Place("689E0100006A6055E8????????83C4186AF657E8????????83C4086AF656E8",                   // 414
                  "689E0100006A6055E8????????83C4186A0A57E8????????83C4086A0A56E8"),
            Place("6A026A386A6356E8", "6A016A386A6356E8"),                                          // 56
            Place("83C40C6A076A3253E8", "83C40C6A076A3255E8"),                                      // 38
            Place("F64328040F84CA0000008D430C", "F6432804E9CB000000908D430C"),                      // 392
            Place("3E001400524150544F52", "44001400524150544F52"),                                  // 732, the table
            Place("681A010000B9????????E8E30700008BF883C9FF33C08D542438F2AEF7D12BF98BC18BF78BFAC1E902F3A58BC883E103F3A4"   // 284
                  + "8B35????????56E8????????8B0D????????83C4045051E8????????83C404B9????????50681B010000E891070000"
                  + "8D9424340200005052E8????????83C4108D84242C0200008D4C243850516A026A5B53",
                  "B9????????8BA96C0400008B35????????56E8????????8BF08D7C243C6A0859F3A5FF35????????E8????????"
                  + "8D4C2440890C2450B9????????FFB1740400008D94243C02000052E8????????83C4105E8D84242C020000"
                  + "50556A026A5B53" + string.Concat(Enumerable.Repeat("90", 37))),
            Place("6A025668530100006A6055E8????????8B853003000083C41403C6898530030000",   // 339
                  "6A025668530100006A6055E8????????01B5300300006A0155E88263000083C41C"),
            Place("6A02505068570100006A6055E8????????83C4186A1455E8????????83C40866B801005F5E5D5B81C460080000C3",   // 343
                  "6AFF506A02505068570100006A6055E8????????83C418E8276400006A1455E8????????83C410EBA39090909090"),
            Place("6A0AE8????????8B3C85????????83C4046A02E8", "6A0CE8????????8B3C85????????83C4046A02E8"),   // 876/877, the reason
            Place("6A02E8????????83C404B9????????85C00FBFF6577432E810B5FFFF8D4E415051686C030000B9????????E8FCB4FFFF8D54242C5052E8??"
                  + "??????8A84338408000083C410FEC0EB30E8DEB4FFFF508D464150686D030000B9????????E8CAB4FFFF8D4C242C5051E8????????8A8433"
                  + "8408000083C4100402",   // 876/877, the message
                  "6A02E8????????590FBFF6F7D81BC04050B9????????57E810B5FFFF813877656765750383C006508D4E41518B442408056C03000050B9??"
                  + "??????E8ECB4FFFF508D54243452E8????????83C410588A8C33840800008D440801EB1D????????90????????9090909090909090909090"
                  + "909090909090909090"),
        };

        /// <inheritdoc/>
        public override string Name => "Ereignisse wirken wie in ihrer Meldung (Geld, Form, Frische, Verletzungen, Klang)";
        /// <inheritdoc/>
        public override string Title => "Ereignisse";
        /// <inheritdoc/>
        public override PatchGroup Group => PatchGroup.Patches;
        /// <inheritdoc/>
        public override uint Flag => 262144;
    }

    /// <summary>
    /// Keeps a player out for three weeks after the fish poisoning of text 732 ("fällt drei Wochen aus"),
    /// instead of the 7 days the kinds' table gives every fish poisoning. FUN_00633a60 enters the injury of
    /// the referee events (its only caller, FUN_005ee7f0): event 0 is the poisoning (text 732), event 1 the
    /// fall down the escalator (734, kind 20, 28 days, as its text says). The patch gives event 0 21 days,
    /// whatever kind <see cref="EventEffects"/> left in the table, in the function's bytes and 5 of the 10
    /// nops behind it:
    /// <code>
    ///   ... / mov [ecx+46],dl / movsx eax,word [eax*2+kinds] / lea eax,[eax+eax*2] / movzx dx,byte [eax*2+days] / mov [ecx+44],dx
    ///   ->  ... / mov [ecx+46],dl / test eax,eax / movsx eax,word [eax*2+kinds] / lea eax,[eax+eax*2] / movzx edx,byte [eax*2+days] /
    ///       jne / mov dl,21 / mov [ecx+44],dx
    /// </code>
    /// </summary>
    internal sealed class FishPoisoning : RewritePatch
    {
        /// <inheritdoc/>
        internal override byte?[] OriginalCode =>
            "0FBF4424088B4C24048A1445????????8851460FBF0445????????8D0440660FB61445????????6689514466A1????????66894142C39090909090".Pattern();
        /// <inheritdoc/>
        internal override byte?[] PatchedCode =>
            "0FBF4424088B4C24048A1445????????88514685C00FBF0445????????8D04400FB61445????????7502B2156689514466A1????????66894142C3".Pattern();

        /// <inheritdoc/>
        public override string Name => "Fischvergiftung nach der Thunfischpizza: drei Wochen Ausfall wie in der Meldung statt einer";
        /// <inheritdoc/>
        public override string Title => "Fischvergiftung";
        /// <inheritdoc/>
        public override PatchGroup Group => PatchGroup.Balancing;
        /// <inheritdoc/>
        public override uint Flag => 4194304;
    }

    /// <summary>
    /// Lets the manager's private life bring all its events. FUN_0055c900 (texts of EreigGld.saq, places in
    /// the original) has 39 cases; FUN_0055c840 draws one from a weighted table at 0x784CE8, which
    /// FUN_0055c820 builds, and skips one of the last 20 shown once. The game builds it from the table of
    /// FUN_0055c230 (0x6BD248, 14 pairs {weight, case}, all of the other kind's events), so cases 15 to 39
    /// never come; its own table (0x6BD280, 39 pairs, 56 bytes behind, referenced nowhere) gives them
    /// weights from 1 to 25 (the fans' honking and tomatoes 25 each, both only while the board's trust is
    /// below 60). The patch builds the dispatcher from it, in the builder's bytes and 4 of the 12 nops
    /// behind them (the dispatcher's start, add ecx,28000, tells it from the other one):
    /// <code>
    ///   push -1 / push 6BD248 / push 0E / mov ecx,dispatcher / call / ret
    ///   ->  push -1 / mov eax,6BD248 / add eax,38 / push eax / push 27 / mov ecx,dispatcher / call / ret
    /// </code>
    /// Case 32 (text 528, the honking) holds the manager's name twice, but FUN_00565860 passes it once, and
    /// sprintf reads the caller's esi, saved behind, as the second string. The patch passes it twice
    /// (keeping the buffer in esi saves the room); texts with one name ignore the second:
    /// <code>
    ///   lea eax,[esp+4] / lea ecx,[esp+1F8] / push eax / push esi / push ecx / call sprintf / ... / lea eax,[esp+1F8]
    ///   ->  lea eax,[esp+4] / push eax / push eax / push esi / lea esi,[esp+204] / push esi / call sprintf / ... / mov eax,esi
    /// </code>
    /// Six cases get code of their own (tools/manager_events.asm, <see cref="ManagerCode"/>): 13, 21, 35 and
    /// 39 have their texts but no code, 25 (an inheritance) builds its text but neither shows it nor books
    /// anything, 34 names "%s, der Sie eigentlich gar nicht leiden kann" with an empty string. The code
    /// goes into three functions of the game nothing refers to (the questions of 13, 21 and 35 behind it,
    /// where the question dialog reaches them), and the switch's jump table leads there. The builds are
    /// known by the switch's address (<see cref="ManagerCode.Builds"/>, the 2007 CD release has GOG's); the
    /// three functions go back to their original bytes when the patch is taken back.
    /// </summary>
    internal sealed class ManagerEvents : Patch
    {
        private static readonly byte?[][][] Places =
        {
            RewritesPatch.Place(
                "6AFF68????????6A0EB9????????E8????????C390909090909090909090909053565733F6B9????????E8????????8BD86685DB0F849500"
                + "0000668B15????????33C06685D27E260FBFCB81C1606D0000",
                "6AFFB8????????83C038506A27B9????????E8????????C3909090909090909053565733F6B9????????E8????????8BD86685DB0F849500"
                + "0000668B15????????33C06685D27E260FBFCB81C1606D0000"),
            RewritesPatch.Place(
                "8D480A515068????????52E8????????83C4108D4424048D8C24F8010000505651E8????????8B94240C0400008B8C240004000083C40C8D"
                + "8424F8010000",
                "8D480A515068????????52E8????????83C4108D4424045050568DB4240402000056E8????????8B9424100400008B8C240404000083C410"
                + "8BC690909090"),
        };

        /// <summary>The switch: movsx eax,word [esp+7F4] / dec eax / cmp eax,26 / ja return 1 / jmp [eax*4+table].</summary>
        internal static readonly byte?[] SwitchPattern = "0FBF8424F40700004883F8260F87????????FF2485????????".Pattern();
        private const int TableAt = 21;

        /// <summary>The weights of cases 32 to 36 as version 0.5.0 left them, case 34's set to 0, and as the game has them.</summary>
        internal static readonly byte[] OldWeights = "1900200019002100000022000200230001002400".Hex(), Weights = "1900200019002100040022000200230001002400".Hex();

        /// <summary>The block each of <see cref="ManagerCode.Blocks"/> goes into: MAIN and the questions into the first function.</summary>
        private static readonly int[] RegionOf = { 0, 0, 1, 2 };
        /// <summary>The size of an entry of the question table.</summary>
        internal const int QuestionSize = 0x2C;
        /// <summary>What the three functions hold where the code leaves room (int3).</summary>
        internal const byte Fill = 0xCC;

        /// <inheritdoc/>
        public override string Name => "Weitere Ereignisse rund um den Manager: Presse, Fans, Privatleben";
        /// <inheritdoc/>
        public override string Title => "Manager-Ereignisse";
        /// <inheritdoc/>
        public override PatchGroup Group => PatchGroup.Balancing;
        /// <inheritdoc/>
        public override uint Flag => 16777216;
        /// <inheritdoc/>
        public override IEnumerable<byte?[]> Patterns => Places.SelectMany(p => p).Concat(new[] { SwitchPattern });

        /// <summary>The three functions with the code of <paramref name="build"/> in place, and where the cases now start.</summary>
        internal static byte[][] Link(ManagerBuild build, out uint[] entries)
        {
            byte[][] blocks = ManagerCode.Blocks;
            long mainEnd = build.Regions[0] + blocks[0].Length;
            uint questions = (uint)(mainEnd + ((build.Questions - mainEnd) % QuestionSize + QuestionSize) % QuestionSize);
            uint[] bases = { build.Regions[0], questions, build.Regions[1], build.Regions[2] };
            int first = (int)((questions - (long)build.Questions) / QuestionSize);
            uint[] values = bases.Concat(build.Symbols).Concat(new[] { (uint)first, (uint)(first + 1), (uint)(first + 2) }).ToArray();
            byte[][] regions = build.Originals.Select(o => Enumerable.Repeat(Fill, o.Length).ToArray()).ToArray();
            for (int b = 0; b < blocks.Length; b++)
            {
                byte[] code = (byte[])blocks[b].Clone();
                int[] fields = ManagerCode.Fields[b];
                for (int i = 0; i < fields.Length; i += 4)
                {
                    uint value = values[fields[i + 2]] + (uint)fields[i + 3];
                    if (fields[i + 1] != 0)
                        value -= bases[b] + (uint)fields[i] + 4;
                    BitConverter.GetBytes(value).CopyTo(code, fields[i]);
                }
                int region = RegionOf[b];
                code.CopyTo(regions[region], bases[b] - build.Regions[region]);
            }
            entries = ManagerCode.Entries.Select(e => bases[e[0]] + (uint)e[1]).ToArray();
            return regions;
        }

        /// <inheritdoc/>
        public override Site[] Find(byte[] image) => Find(image, ManagerCode.Builds);

        /// <summary>The places in <paramref name="image"/> for one of <paramref name="builds"/>, as <see cref="Find(byte[])"/>.</summary>
        internal static Site[] Find(byte[] image, IEnumerable<ManagerBuild> builds)
        {
            Site[] places = Places.Select(p => RewritePatch.FindOnce(image, p[0], p[1])).ToArray();
            PeFile pe = PeFile.Read(image);
            List<long> switches = FindAll(image, SwitchPattern);
            if (places.Contains(null) || pe == null || switches.Count != 1)
                return null;
            uint at = (uint)pe.VaAt(switches[0]), table = BitConverter.ToUInt32(image, (int)switches[0] + TableAt);
            pe.Imports.TryGetValue("GetAsyncKeyState", out uint keyState);
            // GOG and the 2007 CD release share the addresses, not the imports.
            ManagerBuild build = builds.FirstOrDefault(b => b.Switch == at && b.Table == table && b.KeyState == keyState);
            Site[] code = build == null ? null : CodeSites(image, pe, build);
            if (code == null)
                return null;
            List<Site> sites = places.Concat(code).ToList();
            // Version 0.5.0 set case 34's weight to 0; it goes back to 4 whether the patch is switched on or off.
            List<long> old = FindAll(image, Exact(OldWeights));
            if (old.Count == 1)
            {
                sites.Add(new Site { Offset = old[0], Original = Weights, Patched = Weights, IsPatched = true });
                sites.Add(new Site { Offset = old[0], Original = Weights, Patched = Weights, IsPatched = false });
            }
            return sites.ToArray();
        }

        // The jump table's entries and the three functions for build; null if one holds neither its original nor the code.
        private static Site[] CodeSites(byte[] image, PeFile pe, ManagerBuild build)
        {
            byte[][] regions = Link(build, out uint[] entries);
            List<Site> sites = new List<Site>();
            for (int k = 0; k < ManagerCode.Cases.Length; k++)
                sites.Add(StateAt(image, pe.FileOffset(build.Table + 4 * (uint)(ManagerCode.Cases[k] - 1)), BitConverter.GetBytes(build.Cases[k]), BitConverter.GetBytes(entries[k])));
            for (int r = 0; r < regions.Length; r++)
                sites.Add(StateAt(image, pe.FileOffset(build.Regions[r]), build.Originals[r], regions[r]));
            return sites.Contains(null) ? null : sites.ToArray();
        }

        // The place at offset holding original or patched; null if it holds neither (or lies outside the file, -1).
        private static Site StateAt(byte[] image, long offset, byte[] original, byte[] patched)
        {
            byte[] now = image.Slice(Math.Max(offset, 0), offset < 0 ? 0 : original.Length);
            bool isPatched = now.SequenceEqual(patched);
            return isPatched || now.SequenceEqual(original) ? new Site { Offset = offset, Original = original, Patched = patched, IsPatched = isPatched } : null;
        }
    }

    /// <summary>A patch that rewrites several blocks, each found as by <see cref="RewritePatch.FindOnce"/>, offered only if all of them are there.</summary>
    internal abstract class RewritesPatch : Patch
    {
        /// <summary>Each place as original and patched code.</summary>
        internal abstract byte?[][][] Places { get; }

        /// <summary>A place from its original and patched code as hex ("??" a wildcard).</summary>
        internal static byte?[][] Place(string original, string patched) => new[] { original.Pattern(), patched.Pattern() };

        /// <inheritdoc/>
        public override IEnumerable<byte?[]> Patterns => this.Places.SelectMany(p => p);

        /// <inheritdoc/>
        public override Site[] Find(byte[] image)
        {
            Site[] sites = this.Places.Select(p => RewritePatch.FindOnce(image, p[0], p[1])).ToArray();
            return sites.Contains(null) ? null : sites;
        }
    }

    /// <summary>
    /// Lets the club list of the most appearances take in new players once it holds 50. Each human
    /// manager's club keeps two lists of 50 entries of 20 bytes (block at 0x793030 and 0x79347C plus
    /// 0x4210 per manager, places in the original): goals and appearances, each entry with the player's
    /// number (+0), birth year (+6) and a flag "at the club" (+8). After each match FUN_0061ef70 counts the
    /// appearances (FUN_0061ec50): a player found gets his count raised and the flag 1, a new one takes an
    /// empty entry or, from the bottom up to place 10, the first with flag 0; then the list is sorted.
    /// A player's leaving (FUN_00552d60 -> FUN_0061dc70) clears his flag in the goals list only, so once
    /// 50 players have appeared nobody new gets in. The patch sets the flags of the appearances list anew
    /// from the squad and the players lent out (number alone) before each count; a list frozen by an old
    /// save game takes in new players at once. Room comes from the sort, a bubble sort now (the same
    /// order, descending by appearances at +0xA) in 47 of its 139 bytes; the flags' routine goes in front of it
    /// (jumped over), called from where the count starts:
    /// <code>
    ///   start: movzx ax,byte [ebx+104] / add esp,8 / cmp ax,2 / mov [esp+10],eax / jle / mov dword [esp+10],2
    ///      -> add esp,8 / movzx eax,byte [ebx+104] / cmp al,2 / jbe / mov al,2 / mov [esp+10],eax / call flags / nop x4
    ///   flags: for each entry: flag = 0, then 1 if its number is one of [ebx+990] ([ebx+E6] of them) or [ebx+A1C] ([ebx+A19])
    /// </code>
    /// The two places lie 179 bytes apart in every build, so the call's displacement is the same.
    /// A squad holds at most 30 players, so the patch also keeps the first 20 places instead of 10 (the
    /// search for a former player's entry stops above place 21: cmp ax,0A -> cmp ax,14); the other 30
    /// leave room for the whole squad.
    /// </summary>
    internal sealed class AppearancesList : RewritesPatch
    {
        /// <inheritdoc/>
        internal override byte?[][][] Places => ListPlaces;

        private static readonly byte?[][][] ListPlaces =
        {
            Place("660FB6830401000083C408663D0200894424107E08C744241002000000",
                  "83C4080FB683040100003C027602B00289442410E89C00000090909090"),
            Place("8D442434BD01000000894424106683FD328D55FF8BC57D260FBFCD8D0C898D748C3E0FBFCA8D0C89668B4C8C3E663B0E7D028BD0"
                  + "4083C614663D32007CE46685D27E330FBFC2B9050000008D7C24208D14808D4494348B5424108BF0F3A5B9050000008BF28BF8F3A5"
                  + "B9050000008D7424208BFAF3A58B5424104583C2148D45FF89542410663D31007C82",
                  "EB585657558D7424446A325D6683660800668B066685C074240FBF8BE60000008DBB90090000E81C0000000FB68B190A00008DBB1C0A0000"
                  + "E80A00000083C6144D75C95D5F5EC3E3108B17663B027504C646080183C704E2F0C36A315A8D7424346A3159668B460A663B461E7D13"
                  + "516A05598B448EFC87448E1089448EFCE2F25983C614E2DE4A75D49090"),
            Place("0FBFC88D0C8966837C8F0800740948663D0A007DEB", "0FBFC88D0C8966837C8F0800740948663D14007DEB"),   // the first place a new player may take: 11 -> 21
        };

        /// <inheritdoc/>
        public override string Name => "Vereinsliste der meisten Einsätze nimmt nach 50 Spielern wieder neue auf";
        /// <inheritdoc/>
        public override string Title => "Meiste Einsätze";
        /// <inheritdoc/>
        public override PatchGroup Group => PatchGroup.Patches;
        /// <inheritdoc/>
        public override uint Flag => 8388608;
    }

    /// <summary>
    /// Keeps random events from crashing the game. FUN_00564ee0 (any player of the squad, places in the
    /// original) and FUN_00564c40 (one who is fit) return NULL where the one drawn has +0x98 set (with 1
    /// as second argument) or nobody fits. Of their 71 calls, four read from it without a test:
    /// <code>
    ///   321 0x55F1CB  cmp byte [esi+25],0A / jle out / ... three checks          ->  test esi,esi / je out first, the checks with short jumps to one jmp out
    ///   366 0x55FB4D  up to five draws for an older player: mov cl,[eax+1E] ...  ->  test eax,eax / je next first, cmp [eax+1E],bl
    ///   244 0x55B79C  the message and the injury right away                      ->  test esi,esi / je (return 0), both calls' arguments off at once
    ///       0x5BBCB0  movsx eax,word [eax] for the message (FUN_005bb160)        ->  test eax,eax / je (ret), push dword [eax]
    /// </code>
    /// Each in its own bytes: pop ecx twice instead of add esp,8 makes room. FUN_005bb160 reads the
    /// player's number as a word.
    /// </summary>
    internal sealed class EventCrashes : RewritesPatch
    {
        /// <inheritdoc/>
        internal override byte?[][][] Places => CrashPlaces;

        private static readonly byte?[][][] CrashPlaces =
        {
            Place("6A0155E8????????8BF083C408807E250A0F8E501B0000807E1F030F86461B000080BEC0000000020F8C391B0000",   // 321
                  "6A0155E8????????59599685F67415807E250A7E0F807E1F03760980BEC0000000027D0AE93E1B00009090909090"),
            Place("33F6B31E6A0155E8????????8A481E83C4083ACB760980B8C0000000017407466683FE057CDE",                    // 366
                  "33F6B31E6A0155E8????????595985C0740E38581E760980B8C00000000174064683FE057CDE"),
            Place("6A0157E8????????83C4088BF06A025668F40000006A6157E8????????83C4146A5A6A4D56E8????????83C40C",     // 244
                  "6A0157E8????????59598BF085F674E26A025668F40000006A6157E8????????6A5A6A4D56E8????????83C420"),
            Place("6A0FE8????????83C40485C0751A6A0156E8????????0FBF0083C408506A3D56E8????????83C40C5F5E5D5BC3",
                  "6A0FE8????????5985C0751C6A0156E8????????595985C0740EFF306A3D56E8????????83C40C905F5E5D5BC3"),
        };

        /// <inheritdoc/>
        public override string Name => "Kein Absturz bei Ereignissen, für die kein passender Spieler da ist";
        /// <inheritdoc/>
        public override string Title => "Ereignis-Abstürze";
        /// <inheritdoc/>
        public override PatchGroup Group => PatchGroup.Patches;
        /// <inheritdoc/>
        public override uint Flag => 2097152;
    }

    /// <summary>
    /// Lets a referee's grudge hit the club he holds it against in the match's scenes. A referee's record
    /// (+7, read by FUN_0057d550) holds the club's number or -1; the match copies it to +22E
    /// (FUN_005f0120), and the microphone (manager event 36), interview answers and the affair (event 39,
    /// <see cref="ManagerEvents"/>) set it. The cards (FUN_005f3640) compare it with the club's number,
    /// but the scenes (FUN_00404ec0 in the original) with the number plus one and skip 0, so they hit the
    /// club with the next lower number:
    /// <code>
    ///   mov ax,[ebx+22E] / test ax,ax / je / xor edx,edx / mov dl,[edi+102] / movsx eax,ax / inc edx / cmp eax,edx / jne
    ///   ->  ... / test ax,ax / nop x2 / ... / nop / cmp eax,edx / jne
    /// </code>
    /// No grudge (-1) never equals a number (0 to 255).
    /// </summary>
    internal sealed class RefereeGrudge : RewritePatch
    {
        /// <inheritdoc/>
        internal override byte?[] OriginalCode => "668B832E0200006685C0742733D28A97020100000FBFC0423BC27517".Pattern();
        /// <inheritdoc/>
        internal override byte?[] PatchedCode => "668B832E0200006685C0909033D28A97020100000FBFC0903BC27517".Pattern();

        /// <inheritdoc/>
        public override string Name => "Schiedsrichter mit Groll treffen in den Spielszenen den richtigen Verein";
        /// <inheritdoc/>
        public override string Title => "Schiedsrichter-Groll";
        /// <inheritdoc/>
        public override PatchGroup Group => PatchGroup.Patches;
        /// <inheritdoc/>
        public override uint Flag => 67108864;
    }

    /// <summary>
    /// Keeps appearances without a mark out of two averages of marks: the player's season average in his
    /// details (FUN_004fbd60 in the original) and the fans' check for a hated player (FUN_00639f70, the last
    /// matches, at least 4 marks, average 4.5 or worse). The match day byte (player +0x130) holds bit 7 for
    /// an appearance, the mark in bits 2-4 and cards in bits 0-1; who plays under 20 minutes gets no mark.
    /// Both test the mark and the cards together, so a short appearance with a card counts as mark 0 (better
    /// than 1). The team statistics test the mark alone; the patch does the same:
    /// <code>
    ///   test al,80 / je skip / test al,1F / je skip   ->   test al,1C
    /// </code>
    /// </summary>
    internal sealed class GradeAverage : OpcodePatch
    {
        /// <inheritdoc/>
        protected override byte?[] Code => Exact(0xA8, 0x80, 0x74, 0x10, 0xA8, 0x1F, 0x74, 0x0C);
        /// <inheritdoc/>
        protected override int At => 5;
        /// <inheritdoc/>
        internal override byte PatchedByte => 0x1C;
        /// <inheritdoc/>
        protected override int Occurrences => 2;
        /// <inheritdoc/>
        public override string Name => "Einsätze ohne Note zählen nicht im Notenschnitt (Spielerdetails, Fans)";
        /// <inheritdoc/>
        public override string Title => "Notenschnitt";
        /// <inheritdoc/>
        public override PatchGroup Group => PatchGroup.Patches;
        /// <inheritdoc/>
        public override uint Flag => 524288;
    }

    /// <summary>
    /// Labels the axis of the grade chart in the statistics. FUN_0045ceb0 (case 0x17, from 0x461476 in the
    /// original) draws the average marks per week and loads the axis label with FUN_00619c10 as text 5800 of
    /// RESDLL.dll, "Formpunkte/6", the form chart's (5803 "Formpunkte/20" belongs to the team form). RESDLL.dll
    /// has no text for the marks, so the patch assigns "Noten/6" from the exe, in the same 18 bytes; the call
    /// pushes the address behind it, which the add moves to the string in the 9 nops behind the function's ret:
    /// <code>
    ///   lea ecx,[esp+C] / push 16A8 / push ecx / call text / add esp,8
    ///   ->  call $+5 / add dword [esp],5C / lea ecx,[esp+10] / call CString::operator=(const char*)
    /// </code>
    /// The next label (5801, "Woche") calls the same FUN_00619c10, whose first call is the assignment (ret 4);
    /// both give the patch the targets in either state.
    /// </summary>
    internal sealed class GradeChartLabel : Patch
    {
        // From the second label to the function's end, as in all three builds.
        private const string Rest = "8D54241068A916000052E8????????83C404" + "8D4424108BCC50E8????????518D5424188BCC52E8????????"
            + "8B8EFC050100E8????????8D4C2410E8????????8D4C240CE8????????5F5E5B83C468C3";

        /// <summary>The place with the label and the nops behind the function, as the game has it.</summary>
        internal static readonly byte?[] OriginalCode = ("8D4C240C68A816000051E8????????83C408" + Rest + "909090909090909090").Pattern();
        /// <summary>The place patched: the string "Noten/6" behind the function.</summary>
        internal static readonly byte?[] PatchedCode = ("E800000000830424" + "5C" + "8D4C2410E8????????" + Rest + "4E6F74656E2F360090").Pattern();

        // FUN_00619c10: the text by its number into the CString, through CString::operator=.
        private static readonly byte?[] LoaderCode = ("8B4C240881EC000200008D44240068000100005051E8????????85C07518"
            + "8B8C240402000068????????E8????????81C400020000C3").Pattern();

        private const int TextCall = 0x0A, AssignCall = 0x0D, NextTextCall = 0x1C, LoaderAssignCall = 0x2A;

        /// <inheritdoc/>
        public override string Name => "Notenverlauf in der Statistik mit „Noten/6“ statt „Formpunkte/6“ beschriften";
        /// <inheritdoc/>
        public override string Title => "Notenschnitt";
        /// <inheritdoc/>
        public override PatchGroup Group => PatchGroup.Patches;
        /// <inheritdoc/>
        public override uint Flag => 33554432;
        /// <inheritdoc/>
        public override IEnumerable<byte?[]> Patterns => new[] { OriginalCode, PatchedCode };

        /// <inheritdoc/>
        public override Site[] Find(byte[] image)
        {
            Site site = RewritePatch.FindOnce(image, OriginalCode, PatchedCode);
            if (site == null)
                return null;
            long loader = Target(image, site.Offset + NextTextCall);
            if (loader < 0 || FindAll(image.Slice(loader, LoaderCode.Length), LoaderCode).Count != 1)
                return null;
            long assign = Target(image, loader + LoaderAssignCall);
            BitConverter.GetBytes((int)(loader - (site.Offset + TextCall + 5))).CopyTo(site.Original, TextCall + 1);
            BitConverter.GetBytes((int)(assign - (site.Offset + AssignCall + 5))).CopyTo(site.Patched, AssignCall + 1);
            // The call that changes must reach the same function as the one behind it, or the assignment.
            return image.Slice(site.Offset, site.Original.Length).SequenceEqual(site.IsPatched ? site.Patched : site.Original) ? new[] { site } : null;
        }
    }

    /// <summary>
    /// Lets the yearly raise of trainers' and managers' competence take effect. Once a year the game
    /// balances each country's competence (FUN_005cc740 in the original, for trainers and managers, then
    /// written back to the persons): with an average under 6 it gives every tenth person 1 or 2 points (up
    /// to 15), with one over 10 it takes some off. The raise adds the points to the sum and moves the
    /// person's count back to the old value, but never stores the new one; the cut does. The patch stores
    /// it, in the same 36 bytes:
    /// <code>
    ///   mov dx,[edi] / add esp,4 / inc eax / movsx ecx,dx / movsx ebp,ax / add ebp,ecx / cmp ebp,15 / jle /
    ///   mov eax,15 / sub eax,edx / movsx eax,ax / add ebx,eax / inc word [esi+ecx*2]
    /// ->
    ///   movsx edx,[edi] / add esp,4 / inc eax / movsx eax,ax / add eax,edx / cmp eax,15 / jle / mov eax,15 /
    ///   mov [edi],ax / sub eax,edx / add ebx,eax / movsx ecx,[edi] / inc word [esi+ecx*2]
    /// </code>
    /// </summary>
    internal sealed class CompetenceBalance : RewritePatch
    {
        /// <inheritdoc/>
        internal override byte?[] OriginalCode => "668B1783C404400FBFCA0FBFE803E983FD0F7E07B80F0000002BC20FBFC003D866FF044E".Pattern();
        /// <inheritdoc/>
        internal override byte?[] PatchedCode => "0FBF1783C404400FBFC003C283F80F7E05B80F0000006689072BC203D80FBF0F66FF044E".Pattern();

        /// <inheritdoc/>
        public override string Name => "Trainer und Manager in schwachen Ländern werden jährlich besser";
        /// <inheritdoc/>
        public override string Title => "Trainerkompetenz";
        /// <inheritdoc/>
        public override PatchGroup Group => PatchGroup.Patches;
        /// <inheritdoc/>
        public override uint Flag => 1048576;
    }

    /// <summary>What the patches need of a 32-bit exe's headers: its sections, where the headers are, and the imports by name.</summary>
    internal sealed class PeFile
    {
        /// <summary>A section header.</summary>
        internal sealed class Section
        {
            /// <summary>Its name (up to 8 characters).</summary>
            public string Name;
            /// <summary>Its virtual address, size in memory, file offset, size in the file, and flags.</summary>
            public uint Va, VirtualSize, Raw, RawSize, Characteristics;
        }

        private readonly byte[] image;
        /// <summary>Where the exe is loaded.</summary>
        public uint ImageBase { get; private set; }
        /// <summary>Where the optional header starts in the file.</summary>
        public long OptionalHeader { get; private set; }
        /// <summary>Where the section headers start in the file.</summary>
        public long SectionTable { get; private set; }
        /// <summary>The size of the loaded image.</summary>
        public uint SizeOfImage { get; private set; }
        /// <summary>The alignment of sections in memory.</summary>
        public uint SectionAlignment { get; private set; }
        /// <summary>The alignment of sections in the file.</summary>
        public uint FileAlignment { get; private set; }
        /// <summary>Where the signature (certificate table) starts in the file and its size; both 0 without one.</summary>
        public uint CertificateAt { get; private set; }
        /// <summary>The size of the signature.</summary>
        public uint CertificateSize { get; private set; }
        /// <summary>The sections in the order of the headers.</summary>
        public List<Section> Sections { get; } = new List<Section>();
        /// <summary>Each imported function's import address table entry (an address in the loaded exe).</summary>
        public Dictionary<string, uint> Imports { get; } = new Dictionary<string, uint>();

        private PeFile(byte[] image) => this.image = image;

        /// <summary>The exe's headers and imports, or null if <paramref name="image"/> isn't a readable PE file.</summary>
        public static PeFile Read(byte[] image)
        {
            try
            {
                PeFile pe = new PeFile(image);
                return pe.Parse() ? pe : null;
            }
            catch (FormatException)
            {
                return null;
            }
        }

        private bool Parse()
        {
            if (this.U16(0) != 0x5A4D)                         // MZ
                return false;
            long header = this.U32(0x3C);
            if (this.U32(header) != 0x4550)                    // PE\0\0
                return false;
            long optional = header + 24, sections = optional + this.U16(header + 20);
            this.OptionalHeader = optional;
            this.SectionTable = sections;
            this.ImageBase = this.U32(optional + 28);
            this.SectionAlignment = this.U32(optional + 32);
            this.FileAlignment = this.U32(optional + 36);
            this.SizeOfImage = this.U32(optional + 56);
            this.CertificateAt = this.U32(optional + 128);
            this.CertificateSize = this.U32(optional + 132);
            for (int i = 0; i < this.U16(header + 6); i++)
            {
                long at = sections + 40 * i;
                this.Sections.Add(new Section
                {
                    Name = this.Name(at, 8), VirtualSize = this.U32(at + 8), Va = this.U32(at + 12),
                    RawSize = this.U32(at + 16), Raw = this.U32(at + 20), Characteristics = this.U32(at + 36),
                });
            }
            // The import descriptors up to the empty one: names by the import lookup table, entries in the address table.
            for (long descriptor = this.Offset(this.U32(optional + 104)); this.U32(descriptor + 12) != 0; descriptor += 20)
            {
                long lookup = this.Offset(this.U32(descriptor)), table = this.U32(descriptor + 16);
                for (int i = 0; this.U32(lookup + 4 * i) != 0; i++)
                {
                    uint entry = this.U32(lookup + 4 * i);
                    if ((entry & 0x80000000) == 0)             // not by ordinal
                        this.Imports[this.Name(this.Offset(entry) + 2)] = (uint)(this.ImageBase + table + 4 * i);
                }
            }
            return true;
        }

        /// <summary>Whether one more section header fits before the first section's data.</summary>
        public bool HasRoomForSection => this.SectionTable + 40 * (this.Sections.Count + 1) <= this.Sections.Min(s => s.Raw);

        /// <summary>
        /// <paramref name="image"/> with a section <paramref name="name"/> added that holds <paramref name="data"/>
        /// as executable, writable code, loaded right behind the image. Its data goes behind everything in the
        /// file, a signature included. GOG's run.exe has leftovers behind its section headers; the 40 bytes the
        /// new header overwrites are kept right behind the data, so <see cref="RemoveLastSection"/> restores the
        /// exe to the byte.
        /// </summary>
        public static byte[] AddSection(byte[] image, string name, byte[] data)
        {
            PeFile pe = Read(image);
            long raw = FilePatch.Align(image.Length, pe.FileAlignment);
            uint rawSize = FilePatch.Align(data.Length + 40, pe.FileAlignment);
            byte[] result = new byte[raw + rawSize];
            image.CopyTo(result, 0);
            data.CopyTo(result, raw);
            int header = (int)(pe.SectionTable + 40 * pe.Sections.Count);
            Array.Copy(image, header, result, raw + data.Length, 40);
            Array.Clear(result, header, 40);
            Encoding.ASCII.GetBytes(name).CopyTo(result, header);
            Put(result, header + 8, (uint)data.Length);
            Put(result, header + 12, pe.SizeOfImage);
            Put(result, header + 16, rawSize);
            Put(result, header + 20, (uint)raw);
            Put(result, header + 36, WritableCode);
            BitConverter.GetBytes((ushort)(pe.Sections.Count + 1)).CopyTo(result, pe.OptionalHeader - 18);   // NumberOfSections
            Put(result, pe.OptionalHeader + 56, pe.SizeOfImage + FilePatch.Align(data.Length, pe.SectionAlignment));
            return result;
        }

        /// <summary><paramref name="image"/> without its last section, as before <see cref="AddSection"/>.</summary>
        public static byte[] RemoveLastSection(byte[] image)
        {
            PeFile pe = Read(image);
            Section section = pe.Sections.Last();
            List<Section> others = pe.Sections.Take(pe.Sections.Count - 1).ToList();
            // The file ends with the other sections' data or the signature behind it.
            long end = Math.Max(others.Max(s => (long)s.Raw + s.RawSize), (long)pe.CertificateAt + pe.CertificateSize);
            byte[] result = image.Slice(0, (int)end);
            Array.Copy(image, section.Raw + section.VirtualSize, result, pe.SectionTable + 40 * others.Count, 40);
            BitConverter.GetBytes((ushort)others.Count).CopyTo(result, pe.OptionalHeader - 18);
            Put(result, pe.OptionalHeader + 56, section.Va);
            return result;
        }

        // IMAGE_SCN_CNT_CODE | MEM_EXECUTE | MEM_READ | MEM_WRITE
        private const uint WritableCode = 0xE0000020;

        private static void Put(byte[] image, long at, uint value) => BitConverter.GetBytes(value).CopyTo(image, at);

        /// <summary>The address <paramref name="offset"/> in the file is loaded at, or -1 outside every section.</summary>
        public long VaAt(long offset)
        {
            Section section = this.Sections.FirstOrDefault(s => offset >= s.Raw && offset < s.Raw + s.RawSize);
            return section == null ? -1 : this.ImageBase + section.Va + (offset - section.Raw);
        }

        /// <summary>Where the loaded address <paramref name="va"/> lies in the file, or -1 outside every section's data.</summary>
        public long FileOffset(uint va)
        {
            uint rva = va - this.ImageBase;
            Section section = this.Sections.FirstOrDefault(s => rva >= s.Va && rva < s.Va + Math.Min(s.VirtualSize, s.RawSize));
            return section == null ? -1 : (long)section.Raw + (rva - section.Va);
        }

        // Where the address rva (relative to the image base) lies in the file.
        private long Offset(uint rva)
        {
            Section section = this.Sections.FirstOrDefault(s => rva >= s.Va && rva < s.Va + s.RawSize);
            return section != null ? section.Raw + (rva - section.Va) : throw new FormatException();
        }

        private byte Byte(long at) => at < this.image.Length ? this.image[at] : throw new FormatException();
        private int U16(long at) => this.Byte(at) | this.Byte(at + 1) << 8;
        private uint U32(long at) => (uint)(this.U16(at) | this.U16(at + 2) << 16);

        // The zero-terminated name at at, at most max characters long.
        private string Name(long at, int max = int.MaxValue)
        {
            StringBuilder name = new StringBuilder();
            for (byte c = this.Byte(at); c != 0 && name.Length < max; c = this.Byte(++at))
                name.Append((char)c);
            return name.ToString();
        }
    }
}
