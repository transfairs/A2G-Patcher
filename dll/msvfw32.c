// A2G-Patch msvfw32 proxy (x86): applies A2G-Patch's patches to the game's code at game start.
//
// For game builds whose exe can't be patched on disk (the 2007 CD run.exe is packed, its code only
// exists decrypted in memory). A2G-Patch.exe drops this file next to run.exe; the game then loads it
// in place of the system msvfw32.dll: msvfw32 is not a KnownDLL, and the copy in the program folder
// wins the loader search (dsound.dll would too, but A2G-Scaler's proxy already uses that name).
//
// It forwards every Video for Windows entry point to the real system msvfw32.dll, and on load starts a
// thread that waits until the game's code is in memory, finds the places by the same patterns as
// Patches.cs and rewrites them, for each patch switched on in g_config (A2G-Patch.exe sets the flags
// byte when it installs the DLL):
//   1 goal limits:  setg r8 / dec r32 / and r, -4 / add r, 10  ->  setg r8 / mov r32, 15 / nop...
//                   brakes jle (7E)                            ->  jmp (EB)
//   2 arrow repeat: post the step, then mark it pending        ->  mark it pending, then post
//   4 video crash:  AVIStreamGetFrameOpen's NULL unchecked     ->  checked, skip the video
//   8 sleep freeze: jge after the 3 s wait's difference        ->  jae (unsigned)
//  16 sound device: DirectSoundCreate(&last enumerated GUID)   ->  DirectSoundCreate(NULL)
//  32 disk warning: jae after the 32-bit free space product    ->  jmp (no warning)
//  64 squad limit:  "full?" by forecast only                   ->  also full at 30 actual players
// 128 display mode: ChangeDisplaySettingsA(&640x480, 0)        ->  flags CDS_FULLSCREEN (undone at exit)
// 256 cup draw:     GetBitmapBits of the names' bitmap         ->  GetDIBits as 16-bit DIB
// If a patch's places are ambiguous or not as expected (an unknown build), that patch is left out;
// if they never show up (another program, e.g. Editor.exe), nothing is changed. Remove the patches by
// deleting this msvfw32.dll.
//
// Build: run build.cmd (needs Visual Studio's "Desktop development with C++" workload). Must be x86.
// Built without the C runtime (the static CRT of current toolsets needs APIs Windows XP lacks), so only
// kernel32 functions that exist since XP may be used here.

#include <windows.h>

// The compiler may emit memset calls (e.g. for ZeroMemory); without the CRT this file provides it.
#pragma function(memset)
void *memset(void *dst, int value, size_t count)
{
    unsigned char *p = (unsigned char *)dst;
    while (count--) *p++ = (unsigned char)value;
    return dst;
}

// Every export of the real msvfw32.dll (msvfw32.def keeps their ordinals), so nothing the game or
// avifil32 expects goes missing.
#define MSVFW_FUNCS(X) \
    X(VideoForWindowsVersion) X(DrawDibBegin) X(DrawDibChangePalette) X(DrawDibClose) X(DrawDibDraw) \
    X(DrawDibEnd) X(DrawDibGetBuffer) X(DrawDibGetPalette) X(DrawDibOpen) X(DrawDibProfileDisplay) \
    X(DrawDibRealize) X(DrawDibSetPalette) X(DrawDibStart) X(DrawDibStop) X(DrawDibTime) \
    X(GetOpenFileNamePreview) X(GetOpenFileNamePreviewA) X(GetOpenFileNamePreviewW) \
    X(GetSaveFileNamePreviewA) X(GetSaveFileNamePreviewW) X(ICClose) X(ICCompress) \
    X(ICCompressorChoose) X(ICCompressorFree) X(ICDecompress) X(ICDraw) X(ICDrawBegin) \
    X(ICGetDisplayFormat) X(ICGetInfo) X(ICImageCompress) X(ICImageDecompress) X(ICInfo) X(ICInstall) \
    X(ICLocate) X(ICMThunk32) X(ICOpen) X(ICOpenFunction) X(ICRemove) X(ICSendMessage) \
    X(ICSeqCompressFrame) X(ICSeqCompressFrameEnd) X(ICSeqCompressFrameStart) X(MCIWndCreate) \
    X(MCIWndCreateA) X(MCIWndCreateW) X(MCIWndRegisterClass) X(StretchDIB)

#define DECLARE_PTR(name) static FARPROC p_##name;
MSVFW_FUNCS(DECLARE_PTR)
#undef DECLARE_PTR

static CRITICAL_SECTION g_lock;
static volatile LONG g_loaded;

// Loads the real system msvfw32.dll and resolves the forward pointers. Done lazily on the first
// forwarded call, never from DllMain: LoadLibrary under the loader lock corrupts the binding of other
// modules still being loaded (seen with A2G-Scaler's dsound proxy).
static void EnsureLoaded(void)
{
    if (g_loaded) return;
    EnterCriticalSection(&g_lock);
    if (!g_loaded)
    {
        wchar_t path[MAX_PATH];
        UINT n = GetSystemDirectoryW(path, MAX_PATH);
        if (n != 0 && n <= MAX_PATH - 14)
        {
            lstrcpyW(path + n, L"\\msvfw32.dll");
            HMODULE real = LoadLibraryW(path);
            if (real)
            {
                #define RESOLVE(name) p_##name = GetProcAddress(real, #name);
                MSVFW_FUNCS(RESOLVE)
                #undef RESOLVE
            }
        }
        g_loaded = 1;
    }
    LeaveCriticalSection(&g_lock);
}

// Internal names (w_*) so they do not clash with the SDK's prototypes; msvfw32.def re-exports each
// under its plain name. Each ensures the real msvfw32 is loaded (first call only), then jumps straight
// into the real function with the caller's arguments and stack untouched.
#define DEFINE_STUB(name) \
    __declspec(naked) void w_##name(void) \
    { \
        __asm { pushad } \
        __asm { call EnsureLoaded } \
        __asm { popad } \
        __asm { jmp dword ptr [p_##name] } \
    }
MSVFW_FUNCS(DEFINE_STUB)
#undef DEFINE_STUB


// ---- configuration --------------------------------------------------------------------------------

// The marker lets A2G-Patch.exe recognize its own msvfw32.dll (it never deletes or overwrites someone
// else's) and find the two flags bytes right after it (low byte first), which it sets on installing.
// volatile and not const, so the compiler keeps it in .data and reads the bytes the exe wrote.
#define FLAG_GOAL_LIMITS  1
#define FLAG_ARROW_REPEAT 2
#define FLAG_VIDEO_CRASH  4
#define FLAG_SLEEP_FREEZE 8
#define FLAG_DEFAULT_SOUND 16
#define FLAG_DISK_WARNING 32
#define FLAG_SQUAD_LIMIT  64
#define FLAG_DISPLAY_MODE 128
#define FLAG_CUP_DRAW     256   // the first bit of the second flags byte
static volatile unsigned char g_config[] = "A2GPatchFlags\0\xFF\x01";   // all on
#define CONFIG_FLAGS ((WORD)(g_config[14] | g_config[15] << 8))

// ---- finding and writing code ---------------------------------------------------------------------

#define ANY 0x100              // wildcard in a pattern
#define POLL_MS 250
#define POLL_COUNT 480         // two minutes: the packed exe decrypts its code right after start

static BOOL Readable(DWORD protect)
{
    if (protect & (PAGE_GUARD | PAGE_NOACCESS)) return FALSE;
    return (protect & (PAGE_READONLY | PAGE_READWRITE | PAGE_WRITECOPY | PAGE_EXECUTE_READ
        | PAGE_EXECUTE_READWRITE | PAGE_EXECUTE_WRITECOPY)) != 0;
}

static BOOL Matches(const BYTE *at, const WORD *pattern, SIZE_T n)
{
    for (SIZE_T i = 0; i < n; i++)
        if (pattern[i] != ANY && at[i] != pattern[i]) return FALSE;
    return TRUE;
}

// Counts the pattern's occurrences in the committed, readable memory of the image; *found gets the
// start of the first one.
static int Count(BYTE *base, SIZE_T size, const WORD *pattern, SIZE_T n, BYTE **found)
{
    int count = 0;
    BYTE *p = base, *end = base + size;
    *found = NULL;
    while (p < end)
    {
        MEMORY_BASIC_INFORMATION mbi;
        if (VirtualQuery(p, &mbi, sizeof mbi) == 0) break;
        BYTE *regionEnd = (BYTE *)mbi.BaseAddress + mbi.RegionSize;
        if (regionEnd > end) regionEnd = end;
        if (mbi.State == MEM_COMMIT && Readable(mbi.Protect))
        {
            for (BYTE *q = p; q + n <= regionEnd; q++)
            {
                if (*q == (BYTE)pattern[0] && Matches(q, pattern, n) && ++count == 1)
                    *found = q;
            }
        }
        p = regionEnd;
    }
    return count;
}

static BOOL Equal(const BYTE *a, const BYTE *b, SIZE_T n)
{
    while (n--) if (*a++ != *b++) return FALSE;
    return TRUE;
}

static void Copy(BYTE *dst, const BYTE *src, SIZE_T n)
{
    while (n--) *dst++ = *src++;
}

static void Write(BYTE *at, const BYTE *bytes, SIZE_T n)
{
    DWORD old;
    if (!VirtualProtect(at, n, PAGE_EXECUTE_READWRITE, &old)) return;
    Copy(at, bytes, n);
    VirtualProtect(at, n, old, &old);
    FlushInstructionCache(GetCurrentProcess(), at, n);
}

// Result of one attempt at a patch.
#define NOT_YET 0              // its code isn't in memory (yet)
#define DONE 1                 // patched, already patched, or not as expected (then left alone)

// ---- 1: goal limits -------------------------------------------------------------------------------

// The instructions right before each rewritten byte, as in Patches.cs (GoalLimits).
static const WORD kLimit[] = { 0x66, 0x83, 0x7F, 0x08, 0x2D, 0x57, 0x6A, 0x01, 0x0F, 0x9F };
static const WORD kHome[]  = { 0x66, 0x83, 0x7E, 0x0E, 0x06, 0x89, 0x44, 0x24, 0x30 };
static const WORD kAway[]  = { 0x66, 0x8B, 0x56, 0x10, 0x83, 0xC4, 0x04, 0x66, 0x83, 0xFA, 0x06, 0x8B, 0xC8 };
#define JLE 0x7E
#define JMP 0xEB

// The rewritten limit (after setg's ModR/M byte at site[0]): which original is there and its
// patched form. Returns its length, 0 if already patched, -1 if neither.
static int LimitBytes(const BYTE *site, BYTE *patched)
{
    if (site[0] < 0xC0 || site[0] > 0xC3) return -1;
    BYTE r = (BYTE)(site[0] - 0xC0);
    BYTE longForm[7] = { (BYTE)(0x48 + r), 0x83, (BYTE)(0xE0 + r), 0xFC, 0x83, (BYTE)(0xC0 + r), 0x0A };
    BYTE shortForm[6] = { 0x48, 0x24, 0xFC, 0x83, 0xC0, 0x0A };
    BYTE mov[7] = { (BYTE)(0xB8 + r), 15, 0, 0, 0, 0x90, 0x90 };
    // The long form first: patched, the short form is a prefix of it.
    if (Equal(site + 1, mov, 7)) return 0;
    if (Equal(site + 1, longForm, 7)) { Copy(patched, mov, 7); return 7; }
    if (r == 0 && Equal(site + 1, mov, 6)) return 0;
    if (r == 0 && Equal(site + 1, shortForm, 6)) { Copy(patched, mov, 6); return 6; }
    return -1;
}

static int PatchGoalLimits(BYTE *base, SIZE_T size)
{
    BYTE *limit, *home, *away;
    int countLimit = Count(base, size, kLimit, ARRAYSIZE(kLimit), &limit);
    int countHome = Count(base, size, kHome, ARRAYSIZE(kHome), &home);
    int countAway = Count(base, size, kAway, ARRAYSIZE(kAway), &away);
    if (countLimit == 0 || countHome == 0 || countAway == 0) return NOT_YET;
    if (countLimit != 1 || countHome != 1 || countAway != 1) return DONE;
    limit += ARRAYSIZE(kLimit); home += ARRAYSIZE(kHome); away += ARRAYSIZE(kAway);

    BYTE patched[7];
    int length = LimitBytes(limit, patched);
    if (length < 0 || (*home != JLE && *home != JMP) || (*away != JLE && *away != JMP)) return DONE;

    static const BYTE jmp = JMP;
    if (length > 0) Write(limit + 1, patched, (SIZE_T)length);
    if (*home == JLE) Write(home, &jmp, 1);
    if (*away == JLE) Write(away, &jmp, 1);
    return DONE;
}

// ---- 2: arrow repeat ------------------------------------------------------------------------------

// The arrow buttons' repeat thread, as in Patches.cs (ArrowRepeat): the check "no step pending", then
// the branch that posts the step and only afterwards marks it pending. The message variable's address
// (bytes 11..14) differs between the builds.
static const WORD kArrow[] = {
    0x8B, 0x86, 0xF4, 0x00, 0x00, 0x00, 0x85, 0xC0, 0x75, 0x29,             // mov eax,[esi+F4] / test / jne
    0xA1, ANY, ANY, ANY, ANY,                                               // mov eax,[msg]
    0x8B, 0x4E, 0x1C, 0x6A, 0x00, 0x6A, 0x00, 0x50, 0x51, 0xFF, 0xD3,       // ... call ebx (PostMessageA)
    0x8B, 0x86, 0xF4, 0x00, 0x00, 0x00,                                     // mov eax,[esi+F4]
    0xC7, 0x86, 0xF0, 0x00, 0x00, 0x00, 0x3C, 0x00, 0x00, 0x00,             // mov [esi+F0],60
    0x40, 0x89, 0x86, 0xF4, 0x00, 0x00, 0x00,                               // inc eax / mov [esi+F4],eax
    0xEB, 0x0A };                                                           // jmp next
// How the patched branch starts, to recognize it.
static const WORD kArrowPatchedStart[] = {
    0x8B, 0x86, 0xF4, 0x00, 0x00, 0x00, 0x85, 0xC0, 0x75, 0x29,
    0xC7, 0x86, 0xF4, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00,             // mov [esi+F4],1
    0xC7, 0x86, 0xF0, 0x00, 0x00, 0x00, 0x3C, 0x00, 0x00, 0x00,             // mov [esi+F0],60
    0xA1 };

static int PatchArrowRepeat(BYTE *base, SIZE_T size)
{
    BYTE *site, *patchedSite;
    int count = Count(base, size, kArrow, ARRAYSIZE(kArrow), &site);
    int countPatched = Count(base, size, kArrowPatchedStart, ARRAYSIZE(kArrowPatchedStart), &patchedSite);
    if (count + countPatched == 0) return NOT_YET;
    if (count != 1 || countPatched != 0) return DONE;

    // The branch from byte 10 (41 bytes): mark pending, set the delay, then post; the address of the
    // message variable taken over.
    BYTE branch[41] = {
        0xC7, 0x86, 0xF4, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00,
        0xC7, 0x86, 0xF0, 0x00, 0x00, 0x00, 0x3C, 0x00, 0x00, 0x00,
        0xA1, 0, 0, 0, 0,
        0x8B, 0x4E, 0x1C, 0x6A, 0x00, 0x6A, 0x00, 0x50, 0x51, 0xFF, 0xD3,
        0x90, 0x90, 0x90,
        0xEB, 0x0A };
    Copy(branch + 21, site + 11, 4);
    Write(site + 10, branch, sizeof branch);
    return DONE;
}

// ---- 4: video crash -------------------------------------------------------------------------------

// The video player's setup, as in Patches.cs (VideoCrash): the AVIFileGetStream failure path
// ("release" at byte 4), AVIStreamInfoA, then the 75 rewritten bytes from byte 57: the
// BITMAPINFOHEADER setup and AVIStreamGetFrameOpen, whose NULL result the game doesn't check.
#define VIDEO_PREFIX \
    0x85, 0xC0, 0x74, 0x21, 0x8B, 0x4D, 0x00, 0x51, 0xE8, ANY, ANY, ANY, ANY, \
    0x8D, 0x4C, 0x24, 0x10, 0xE8, ANY, ANY, ANY, ANY, 0x33, 0xC0, 0x5F, 0x5E, 0x5D, 0x5B, \
    0x81, 0xC4, 0x84, 0x00, 0x00, 0x00, 0xC2, 0x0C, 0x00, \
    0x8B, 0x07, 0x8D, 0x96, 0x98, 0x84, 0x00, 0x00, 0x68, 0x8C, 0x00, 0x00, 0x00, 0x52, 0x50, 0xE8, ANY, ANY, ANY, ANY
#define VIDEO_PREFIX_LENGTH 57
#define VIDEO_RELEASE 4
static const WORD kVideo[] = {
    VIDEO_PREFIX,
    0x8D, 0x86, 0x64, 0x80, 0x00, 0x00, 0x50, 0xC7, 0x00, 0x28, 0x00, 0x00, 0x00,
    0x8B, 0x8E, 0xD4, 0x84, 0x00, 0x00, 0x89, 0x48, 0x04, 0x8B, 0x96, 0xD8, 0x84, 0x00, 0x00, 0x89, 0x50, 0x08,
    0x66, 0xC7, 0x40, 0x0C, 0x01, 0x00, 0x66, 0xC7, 0x40, 0x0E, 0x08, 0x00,
    0x89, 0x58, 0x10, 0x89, 0x58, 0x14, 0x89, 0x58, 0x18, 0x89, 0x58, 0x1C, 0x89, 0x58, 0x20, 0x89, 0x58, 0x24,
    0x8B, 0x07, 0x50, 0xE8, ANY, ANY, ANY, ANY, 0x89, 0x86, 0x8C, 0x84, 0x00, 0x00 };
#define VIDEO_ORIGINAL_CALL 65
// How the patched code starts, to recognize it.
static const WORD kVideoPatchedStart[] = {
    VIDEO_PREFIX,
    0x8D, 0x86, 0x64, 0x80, 0x00, 0x00, 0x50, 0xC7, 0x00, 0x28, 0x00, 0x00, 0x00,
    0x8B, 0x4F, 0x40, 0x89, 0x48, 0x04, 0x8B, 0x4F, 0x44, 0x89, 0x48, 0x08 };

static int PatchVideoCrash(BYTE *base, SIZE_T size)
{
    BYTE *site, *patchedSite;
    int count = Count(base, size, kVideo, ARRAYSIZE(kVideo), &site);
    int countPatched = Count(base, size, kVideoPatchedStart, ARRAYSIZE(kVideoPatchedStart), &patchedSite);
    if (count + countPatched == 0) return NOT_YET;
    if (count != 1 || countPatched != 0) return DONE;

    // Same length; the call to AVIStreamGetFrameOpen moves 12 bytes towards the start.
    BYTE code[75] = {
        0x8D, 0x86, 0x64, 0x80, 0x00, 0x00, 0x50, 0xC7, 0x00, 0x28, 0x00, 0x00, 0x00,
        0x8B, 0x4F, 0x40, 0x89, 0x48, 0x04, 0x8B, 0x4F, 0x44, 0x89, 0x48, 0x08,   // mov ecx,[edi+40/44]
        0xC7, 0x40, 0x0C, 0x01, 0x00, 0x08, 0x00,                                // planes 1, bit count 8
        0x89, 0x58, 0x10, 0x89, 0x58, 0x14, 0x89, 0x58, 0x18, 0x89, 0x58, 0x1C, 0x89, 0x58, 0x20, 0x89, 0x58, 0x24,
        0xFF, 0x37, 0xE8, 0, 0, 0, 0,                                            // push [edi] / call
        0x85, 0xC0, 0x75, 0x07,                                                  // test eax,eax / jnz ok
        0x89, 0x07, 0xE9, 0, 0, 0, 0,                                            // mov [edi],eax / jmp release
        0x89, 0x47, 0xF8, 0x90, 0x90, 0x90, 0x90 };                              // ok: mov [edi-8],eax
    BYTE *at = site + VIDEO_PREFIX_LENGTH;
    LONG call, jump = VIDEO_RELEASE - (VIDEO_PREFIX_LENGTH + 64 + 4);
    Copy((BYTE *)&call, at + VIDEO_ORIGINAL_CALL, 4);
    call += 12;
    Copy(code + 53, (const BYTE *)&call, 4);
    Copy(code + 64, (const BYTE *)&jump, 4);
    Write(at, code, sizeof code);
    return DONE;
}

// ---- 8, 32: one jump's opcode ---------------------------------------------------------------------

// As in Patches.cs (SleepFreeze, DiskSpaceWarning): the code around one byte that is rewritten in each
// of a known number of occurrences.
static const WORD kSleep[] = {
    0x2B, 0x85, 0x7C, 0x01, 0x00, 0x00, 0x3D, 0xB8, 0x0B, 0x00, 0x00,     // sub eax,[ebp+17C] / cmp eax,0BB8
    0x89, 0x85, 0x7C, 0x01, 0x00, 0x00, 0x7D, 0x0E };                     // mov [ebp+17C],eax / jge (-> jae)
static const WORD kDisk[] = {
    0x0F, 0xAF, 0x44, 0x24, ANY, 0x3D, 0x00, 0x70, 0x17, 0x00,           // imul eax,[esp+?] / cmp eax,177000
    0x73, ANY, 0x8D, 0x4C, 0x24, ANY, 0x68, 0xC1, 0x0A, 0x00, 0x00 };     // jae (-> jmp) / lea / push 0AC1

static int PatchOpcode(BYTE *base, SIZE_T size, const WORD *code, SIZE_T n, SIZE_T at, BYTE patched, int occurrences)
{
    WORD patchedCode[32];
    BYTE *found;
    if (n > ARRAYSIZE(patchedCode)) return DONE;
    for (SIZE_T i = 0; i < n; i++) patchedCode[i] = code[i];
    patchedCode[at] = patched;
    int count = Count(base, size, code, n, &found);
    int total = count + Count(base, size, patchedCode, n, &found);
    if (total < occurrences) return NOT_YET;
    if (total > occurrences) return DONE;
    // One at a time (Count reports the first); bounded in case a write fails.
    for (int i = 0; i < count && Count(base, size, code, n, &found) > 0; i++) Write(found + at, &patched, 1);
    return DONE;
}

// ---- 16: default sound device ---------------------------------------------------------------------

// As in Patches.cs (DefaultSoundDevice): push esi / push &ds / push &guid / call DirectSoundCreate /
// test / je / call / mov [&ds],esi. "push &guid" (byte 6) becomes "push esi / nop x4", so DirectSound
// gets NULL, the default device.
#define SOUND_TAIL 0xE8, ANY, ANY, ANY, ANY, 0x85, 0xC0, 0x74, 0x15, 0xE8, ANY, ANY, ANY, ANY, 0x89, 0x35, ANY, ANY, ANY, ANY
static const WORD kSound[] = { 0x56, 0x68, ANY, ANY, ANY, ANY, 0x68, ANY, ANY, ANY, ANY, SOUND_TAIL };
static const WORD kSoundPatched[] = { 0x56, 0x68, ANY, ANY, ANY, ANY, 0x56, 0x90, 0x90, 0x90, 0x90, SOUND_TAIL };

static int PatchSound(BYTE *base, SIZE_T size)
{
    BYTE *site, *patchedSite;
    int count = Count(base, size, kSound, ARRAYSIZE(kSound), &site);
    int countPatched = Count(base, size, kSoundPatched, ARRAYSIZE(kSoundPatched), &patchedSite);
    if (count + countPatched == 0) return NOT_YET;
    if (count != 1 || countPatched != 0) return DONE;
    // Both mentions of the DirectSound pointer agree, the GUID lies 0x18 bytes before it.
    DWORD pointer, again, guid;
    Copy((BYTE *)&pointer, site + 2, 4);
    Copy((BYTE *)&again, site + 27, 4);
    Copy((BYTE *)&guid, site + 7, 4);
    if (pointer != again || guid != pointer - 0x18) return DONE;
    static const BYTE pushNull[5] = { 0x56, 0x90, 0x90, 0x90, 0x90 };
    Write(site + 6, pushNull, sizeof pushNull);
    return DONE;
}

// ---- 64: squad limit ------------------------------------------------------------------------------

// As in Patches.cs (SquadLimit): "squad full?" also says yes at 30 actual players, not only by the
// forecast. Same 32 bytes; the call (bytes 6..9) keeps its place and displacement.
static const WORD kSquad[] = {
    0x8B, 0x44, 0x24, 0x04, 0x50, 0xE8, ANY, ANY, ANY, ANY, 0x83, 0xC4, 0x04, 0x33, 0xC9,
    0x66, 0x3D, 0x1E, 0x00, 0x0F, 0x9D, 0xC1, 0x8B, 0xC1, 0xC3, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90 };
static const WORD kSquadPatched[] = {
    0x8B, 0x44, 0x24, 0x04, 0x50, 0xE8, ANY, ANY, ANY, ANY, 0x59,                  // push [esp+4] / call / pop ecx
    0x66, 0x3D, 0x1E, 0x00, 0x7D, 0x08, 0x66, 0x83, 0xB9, 0xE6, 0x00, 0x00, 0x00, 0x1E, // cmp ax,1E / jge / cmp [ecx+E6],1E
    0x0F, 0x9D, 0xC0, 0x0F, 0xB6, 0xC0, 0xC3 };                                    // setge al / movzx eax,al / ret

// ---- 128: display mode restore --------------------------------------------------------------------

// As in Patches.cs (DisplayModeRestore): before ChangeDisplaySettingsA(&mode, flags) the needless
// "mov [esi+1C8],ebx" becomes "mov byte [esp+4],4 / nop", so the flags are CDS_FULLSCREEN and Windows
// restores the desktop mode when the game ends. (GOG's run.exe does this already; there nothing matches.)
#define DISPLAY_HEAD \
    0xF3, 0xAB, 0xBF, 0xE0, 0x01, 0x00, 0x00, 0x53, 0x52, 0x66, 0xC7, 0x86, 0x74, 0x01, 0x00, 0x00, 0x9C, 0x00, \
    0x66, 0x89, 0x9E, 0x76, 0x01, 0x00, 0x00, 0xC7, 0x86, 0x78, 0x01, 0x00, 0x00, 0x00, 0x00, 0x5C, 0x00, \
    0xC7, 0x86, 0xBC, 0x01, 0x00, 0x00, 0x80, 0x02, 0x00, 0x00, 0x89, 0xBE, 0xC0, 0x01, 0x00, 0x00, \
    0xC7, 0x86, 0xB8, 0x01, 0x00, 0x00, 0x10, 0x00, 0x00, 0x00
static const WORD kDisplay[] = { DISPLAY_HEAD, 0x89, 0x9E, 0xC8, 0x01, 0x00, 0x00, 0xFF, 0x15, ANY, ANY, ANY, ANY };
static const WORD kDisplayPatched[] = { DISPLAY_HEAD, 0xC6, 0x44, 0x24, 0x04, 0x04, 0x90, 0xFF, 0x15, ANY, ANY, ANY, ANY };

// Rewrites a block that occurs once (as RewritePatch in Patches.cs): ANY bytes are kept.
static int PatchRewrite(BYTE *base, SIZE_T size, const WORD *original, const WORD *patched, SIZE_T n)
{
    BYTE *site, *patchedSite, code[96];
    if (n > sizeof code) return DONE;
    int count = Count(base, size, original, n, &site);
    int countPatched = Count(base, size, patched, n, &patchedSite);
    if (count + countPatched == 0) return NOT_YET;
    if (count != 1 || countPatched != 0) return DONE;
    for (SIZE_T i = 0; i < n; i++) code[i] = patched[i] == ANY ? site[i] : (BYTE)patched[i];
    Write(site, code, n);
    return DONE;
}

// ---- 256: cup draw --------------------------------------------------------------------------------

// As in Patches.cs (CupDraw): the names on the cup draw sign come out of GetBitmapBits, which on current
// Windows and under Wine delivers 32 bits per pixel. The call goes to added code that fetches them with GetDIBits as a 16-bit
// top-down DIB (5-5-5, converted to 5-6-5 if the game's surface has green mask 7E0 at [esi+8040]).
// push ebx / push 0 / call [ReleaseDC] / mov edx,[esi+8010] / lea ecx,[esi+C] / push ecx / push 2000 /
// push edx / call [GetBitmapBits] (-> call helper / nop)
#define CUP_HEAD 0x53, 0x6A, 0x00, 0xFF, 0x15, ANY, ANY, ANY, ANY, \
    0x8B, 0x96, 0x10, 0x80, 0x00, 0x00, 0x8D, 0x4E, 0x0C, 0x51, 0x68, 0x00, 0x20, 0x00, 0x00, 0x52
#define CUP_CALL 25
static const WORD kCup[] = { CUP_HEAD, 0xFF, 0x15, ANY, ANY, ANY, ANY };
static const WORD kCupPatched[] = { CUP_HEAD, 0xE8, ANY, ANY, ANY, ANY, 0x90 };
// The helper as in Patches.cs; the operands of its three indirect calls (bytes 66, 92, 103) point to
// GetDC, GetDIBits and ReleaseDC. On disk those are the game's import entries; here, cells behind the code.
static const BYTE kCupHelper[177] = {
    0x55, 0x53, 0x83, 0xEC, 0x34, 0xC7, 0x04, 0x24, 0x28, 0x00, 0x00, 0x00, 0xC7, 0x44, 0x24, 0x04,
    0x80, 0x00, 0x00, 0x00, 0xC7, 0x44, 0x24, 0x08, 0xE0, 0xFF, 0xFF, 0xFF, 0xC7, 0x44, 0x24, 0x0C,
    0x01, 0x00, 0x10, 0x00, 0x33, 0xC0, 0x89, 0x44, 0x24, 0x10, 0x89, 0x44, 0x24, 0x14, 0x89, 0x44,
    0x24, 0x18, 0x89, 0x44, 0x24, 0x1C, 0x89, 0x44, 0x24, 0x20, 0x89, 0x44, 0x24, 0x24, 0x6A, 0x00,
    0xFF, 0x15, 0x00, 0x00, 0x00, 0x00, 0x8B, 0xD8, 0x8B, 0xCC, 0x6A, 0x00, 0x51, 0xFF, 0x74, 0x24,
    0x50, 0x6A, 0x20, 0x6A, 0x00, 0xFF, 0x74, 0x24, 0x54, 0x53, 0xFF, 0x15, 0x00, 0x00, 0x00, 0x00,
    0x8B, 0xE8, 0x53, 0x6A, 0x00, 0xFF, 0x15, 0x00, 0x00, 0x00, 0x00, 0x81, 0xBE, 0x40, 0x80, 0x00,
    0x00, 0xE0, 0x07, 0x00, 0x00, 0x75, 0x30, 0x8B, 0x54, 0x24, 0x48, 0xB9, 0x00, 0x10, 0x00, 0x00,
    0x0F, 0xB7, 0x02, 0x8B, 0xD8, 0x81, 0xE3, 0x1F, 0x00, 0x00, 0x00, 0x25, 0xE0, 0x7F, 0x00, 0x00,
    0xD1, 0xE0, 0x0B, 0xD8, 0x25, 0x00, 0x04, 0x00, 0x00, 0xC1, 0xE8, 0x05, 0x0B, 0xC3, 0x66, 0x89,
    0x02, 0x83, 0xC2, 0x02, 0x49, 0x75, 0xD9, 0x8B, 0xC5, 0x83, 0xC4, 0x34, 0x5B, 0x5D, 0xC2, 0x0C,
    0x00 };
static const SIZE_T kCupOperands[3] = { 66, 92, 103 };

static int PatchCupDraw(BYTE *base, SIZE_T size)
{
    BYTE *site, *patchedSite;
    int count = Count(base, size, kCup, ARRAYSIZE(kCup), &site);
    int countPatched = Count(base, size, kCupPatched, ARRAYSIZE(kCupPatched), &patchedSite);
    if (count + countPatched == 0) return NOT_YET;
    if (count != 1 || countPatched != 0) return DONE;

    HMODULE user = GetModuleHandleW(L"user32.dll"), gdi = GetModuleHandleW(L"gdi32.dll");
    if (!user || !gdi) return DONE;
    FARPROC functions[3] = { GetProcAddress(user, "GetDC"), GetProcAddress(gdi, "GetDIBits"), GetProcAddress(user, "ReleaseDC") };
    if (!functions[0] || !functions[1] || !functions[2]) return DONE;
    BYTE *helper = (BYTE *)VirtualAlloc(NULL, 4096, MEM_COMMIT | MEM_RESERVE, PAGE_EXECUTE_READWRITE);
    if (!helper) return DONE;
    FARPROC *cells = (FARPROC *)(helper + 192);
    Copy(helper, kCupHelper, sizeof kCupHelper);
    for (int i = 0; i < 3; i++)
    {
        DWORD cell = (DWORD)(ULONG_PTR)&cells[i];
        cells[i] = functions[i];
        Copy(helper + kCupOperands[i], (const BYTE *)&cell, 4);
    }
    FlushInstructionCache(GetCurrentProcess(), helper, sizeof kCupHelper);

    BYTE call[6] = { 0xE8, 0, 0, 0, 0, 0x90 };
    LONG rel = (LONG)(helper - (site + CUP_CALL + 5));
    Copy(call + 1, (const BYTE *)&rel, 4);
    Write(site + CUP_CALL, call, sizeof call);
    return DONE;
}

// ---- the patch thread -----------------------------------------------------------------------------

static DWORD WINAPI PatchThread(LPVOID param)
{
    (void)param;
    BYTE *base = (BYTE *)GetModuleHandleW(NULL);
    IMAGE_DOS_HEADER *dos = (IMAGE_DOS_HEADER *)base;
    IMAGE_NT_HEADERS32 *nt = (IMAGE_NT_HEADERS32 *)(base + dos->e_lfanew);
    SIZE_T size = nt->OptionalHeader.SizeOfImage;
    WORD flags = CONFIG_FLAGS;
    BOOL goalsDone = !(flags & FLAG_GOAL_LIMITS), arrowDone = !(flags & FLAG_ARROW_REPEAT);
    BOOL videoDone = !(flags & FLAG_VIDEO_CRASH), sleepDone = !(flags & FLAG_SLEEP_FREEZE);
    BOOL soundDone = !(flags & FLAG_DEFAULT_SOUND), diskDone = !(flags & FLAG_DISK_WARNING);
    BOOL squadDone = !(flags & FLAG_SQUAD_LIMIT), displayDone = !(flags & FLAG_DISPLAY_MODE);
    BOOL cupDone = !(flags & FLAG_CUP_DRAW);
    #define ALL_DONE (goalsDone && arrowDone && videoDone && sleepDone && soundDone && diskDone && squadDone && displayDone && cupDone)
    for (int i = 0; i < POLL_COUNT && !ALL_DONE; i++)
    {
        if (!goalsDone) goalsDone = PatchGoalLimits(base, size) == DONE;
        if (!arrowDone) arrowDone = PatchArrowRepeat(base, size) == DONE;
        if (!videoDone) videoDone = PatchVideoCrash(base, size) == DONE;
        if (!sleepDone) sleepDone = PatchOpcode(base, size, kSleep, ARRAYSIZE(kSleep), 17, 0x73, 2) == DONE;
        if (!soundDone) soundDone = PatchSound(base, size) == DONE;
        if (!diskDone) diskDone = PatchOpcode(base, size, kDisk, ARRAYSIZE(kDisk), 10, 0xEB, 1) == DONE;
        if (!squadDone) squadDone = PatchRewrite(base, size, kSquad, kSquadPatched, ARRAYSIZE(kSquad)) == DONE;
        if (!displayDone) displayDone = PatchRewrite(base, size, kDisplay, kDisplayPatched, ARRAYSIZE(kDisplay)) == DONE;
        if (!cupDone) cupDone = PatchCupDraw(base, size) == DONE;
        if (!ALL_DONE) Sleep(POLL_MS);
    }
    #undef ALL_DONE
    return 0;
}

BOOL WINAPI DllMain(HINSTANCE self, DWORD reason, LPVOID reserved)
{
    (void)reserved;
    if (reason == DLL_PROCESS_ATTACH)
    {
        DisableThreadLibraryCalls(self);
        InitializeCriticalSection(&g_lock);    // real msvfw32 is loaded lazily on the first forwarded call
        HANDLE t = CreateThread(NULL, 0, PatchThread, NULL, 0, NULL);
        if (t) CloseHandle(t);
    }
    return TRUE;
}
