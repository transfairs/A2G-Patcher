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
// 512 calculation:  only one of two selections cleared         ->  both cleared (no hang)
// 1024 patch 1.01: the program of 1.00                         ->  13 places jump to the code of 1.01
// 2048 buttons:    picture button's BITMAP left uncleared      ->  cleared ("A/B" and "i" in place)
// 4096 interviews: effects applied from pairs never written    ->  the engine copy's pairs, codes 10 and 12 mapped
// 8192 speeches:   a speech that doesn't fit does nothing      ->  takes its percentages' amounts off
// 16384 negotiations: monthly halving one byte too low         ->  halves the counters per selling club
// A2G-Patch writes the data files of patch 1.01 itself; its other GOG patches are not offered here.
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
#define FLAG_CALC_INIT    512
#define FLAG_V101         1024
#define FLAG_BUTTON_INIT  2048
#define FLAG_INTERVIEW    4096
#define FLAG_SPEECH       8192
#define FLAG_NEGOTIATION  16384
static volatile unsigned char g_config[] = "A2GPatchFlags\0\xFF\x7F";   // all on
#define CONFIG_FLAGS ((WORD)(g_config[14] | g_config[15] << 8))

// ---- finding and writing code ---------------------------------------------------------------------

#define ANY 0x100              // wildcard in a pattern
#define POLL_MS 250
#define POLL_COUNT 480         // two minutes: the packed exe decrypts its code right after start

// Whether memory with this protection can be read without a fault.
static BOOL Readable(DWORD protect)
{
    if (protect & (PAGE_GUARD | PAGE_NOACCESS)) return FALSE;
    return (protect & (PAGE_READONLY | PAGE_READWRITE | PAGE_WRITECOPY | PAGE_EXECUTE_READ
        | PAGE_EXECUTE_READWRITE | PAGE_EXECUTE_WRITECOPY)) != 0;
}

// Whether the n bytes at at match the pattern (ANY matches every byte).
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

// memcmp without the CRT.
static BOOL Equal(const BYTE *a, const BYTE *b, SIZE_T n)
{
    while (n--) if (*a++ != *b++) return FALSE;
    return TRUE;
}

// memcpy without the CRT.
static void Copy(BYTE *dst, const BYTE *src, SIZE_T n)
{
    while (n--) *dst++ = *src++;
}

// Writes the bytes into the game's code: made writable for the moment, then the old protection back.
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

// Sets the limit to 15 and turns both jle into jmp; NOT_YET until all three places are in memory.
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

// Rewrites the branch so the step is marked pending before it is posted.
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

// Rewrites the 75 bytes so a failed AVIStreamGetFrameOpen takes the release path.
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

// Rewrites the byte at code[at] (as OpcodePatch in Patches.cs): only if original and patched occurrences
// together are exactly as many as expected; mixed states are finished.
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

// Replaces "push &guid" with "push esi" (NULL) if both pointer mentions and the GUID agree.
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
    BYTE *site, *patchedSite, code[160];   // the longest block is 142 bytes (picture buttons)
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

// Copies the helper into fresh memory with its three import cells and points the call at it.
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

// ---- 512: preseason calculation ------------------------------------------------------------------

// As in Patches.cs (CalculationInit): the constructor of the calculation screen clears both selections,
// not only that of "Ermittlung der Zahlen" (+10D48).
static const WORD kCalc[] = { 0x66, 0xC7, 0x86, 0x48, 0x0D, 0x01, 0x00, 0x00, 0x00, 0x8B, 0xC6 };           // mov word [esi+10D48],0 / mov eax,esi
static const WORD kCalcPatched[] = { 0x8B, 0xC6, 0x83, 0xA6, 0x46, 0x0D, 0x01, 0x00, 0x00, 0x90, 0x90 };    // mov eax,esi / and dword [esi+10D46],0

// ---- 16384: negotiation counters ----------------------------------------------------------------

// As in Patches.cs (NegotiationCounter): the monthly club update halves the counters of negotiation
// dates per human manager at +1081..+1084, not the bytes at +107D..+1080.
#define NEGOTIATION_CODE(a, b, c, d) \
    0x8A, 0x8D, a, 0x10, 0x00, 0x00, 0x8A, 0x85, b, 0x10, 0x00, 0x00, \
    0x8A, 0x9D, c, 0x10, 0x00, 0x00, 0x8A, 0x95, d, 0x10, 0x00, 0x00, \
    0xD0, 0xE9, 0xD0, 0xE8, 0xD0, 0xEB, 0xD0, 0xEA, 0x55, \
    0x88, 0x8D, a, 0x10, 0x00, 0x00, 0x88, 0x85, b, 0x10, 0x00, 0x00, \
    0x88, 0x9D, c, 0x10, 0x00, 0x00, 0x88, 0x95, d, 0x10, 0x00, 0x00
static const WORD kNegotiation[] = { NEGOTIATION_CODE(0x7D, 0x7E, 0x7F, 0x80) };
static const WORD kNegotiationPatched[] = { NEGOTIATION_CODE(0x81, 0x82, 0x83, 0x84) };

// ---- 2048: picture buttons -----------------------------------------------------------------------

// As in Patches.cs (PictureButtonInit): the constructor of the picture button class also clears the
// BITMAP at +7C..+93; the stores beyond +7F go through edx = this+80 to make room.
#define BUTTON_HEAD 0x33, 0xC0, 0xB9, ANY, ANY, ANY, ANY                          // xor eax,eax / mov ecx,CString
#define BUTTON_TAIL 0xC7, 0x06, ANY, ANY, ANY, ANY, 0x8B, 0xC6, 0x5E, 0xC3        // mov [esi],vtable / mov eax,esi / pop esi / ret
static const WORD kButton[] = { BUTTON_HEAD,
    0x89, 0x46, 0x5C, 0x89, 0x4E, 0x58, 0x89, 0x46, 0x64, 0x89, 0x4E, 0x60, 0x89, 0x46, 0x6C, 0x89, 0x4E, 0x68,
    0x89, 0x86, 0xA4, 0x00, 0x00, 0x00, 0x89, 0x46, 0x3C, 0x66, 0x89, 0x46, 0x46, 0x66, 0x89, 0x46, 0x48,
    0x89, 0x46, 0x50, 0x89, 0x46, 0x54, 0x89, 0x86, 0x98, 0x00, 0x00, 0x00, 0x89, 0x86, 0x9C, 0x00, 0x00, 0x00,
    0x89, 0x86, 0xA0, 0x00, 0x00, 0x00, 0x89, 0x86, 0xB0, 0x00, 0x00, 0x00, 0x89, 0x46, 0x74, 0x89, 0x46, 0x78,
    0x66, 0x89, 0x46, 0x44, 0x66, 0x89, 0x46, 0x4A, 0x66, 0x89, 0x46, 0x4C,
    0x89, 0x86, 0xB4, 0x00, 0x00, 0x00, 0x89, 0x86, 0xB8, 0x00, 0x00, 0x00, 0x89, 0x86, 0xBC, 0x00, 0x00, 0x00,
    0x89, 0x86, 0xC0, 0x00, 0x00, 0x00, 0x89, 0x86, 0xC4, 0x00, 0x00, 0x00, 0x89, 0x86, 0xC8, 0x00, 0x00, 0x00,
    0x89, 0x86, 0x94, 0x00, 0x00, 0x00, BUTTON_TAIL };
static const WORD kButtonPatched[] = { BUTTON_HEAD,
    0x89, 0x46, 0x5C, 0x89, 0x4E, 0x58, 0x89, 0x46, 0x64, 0x89, 0x4E, 0x60, 0x89, 0x46, 0x6C, 0x89, 0x4E, 0x68,
    0x89, 0x46, 0x3C, 0x66, 0x89, 0x46, 0x44, 0x66, 0x89, 0x46, 0x46, 0x66, 0x89, 0x46, 0x48,
    0x66, 0x89, 0x46, 0x4A, 0x66, 0x89, 0x46, 0x4C,
    0x89, 0x46, 0x50, 0x89, 0x46, 0x54, 0x89, 0x46, 0x74, 0x89, 0x46, 0x78, 0x89, 0x46, 0x7C,
    0x8D, 0x96, 0x80, 0x00, 0x00, 0x00, 0x89, 0x02,                                     // lea edx,[esi+80] / mov [edx],eax
    0x89, 0x42, 0x04, 0x89, 0x42, 0x08, 0x89, 0x42, 0x0C, 0x89, 0x42, 0x10,
    0x89, 0x42, 0x14, 0x89, 0x42, 0x18, 0x89, 0x42, 0x1C, 0x89, 0x42, 0x20, 0x89, 0x42, 0x24,
    0x89, 0x42, 0x30, 0x89, 0x42, 0x34, 0x89, 0x42, 0x38, 0x89, 0x42, 0x3C, 0x89, 0x42, 0x40,
    0x89, 0x42, 0x44, 0x89, 0x42, 0x48,
    0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, BUTTON_TAIL };

// ---- 1024: patch 1.01 ----------------------------------------------------------------------------

// As in GogPatches.cs (Patch101): the code of gog/a2v101.S (v101code.h, generated together with
// V101Code.cs) goes into a block of its own, moved there by its relocations, and 13 places of the 1.00
// program jump there or change in place. They are the places of GOG's run.exe, which the 2007 CD's
// run.exe holds at the same addresses once decrypted. Patched only once all 13 hold the bytes of 1.00;
// until then (still encrypted, or another build) it waits, and gives up after two minutes.
#include "v101code.h"
#define IMAGE_BASE 0x400000    // the code calls the game at the addresses it has there
#define V101_CHANGE 0xFFFF     // not a jump: the place gets bytes of its own
typedef struct
{
    DWORD va;
    BYTE length;
    BYTE original[14];
    WORD target;               // offset in kV101Code, or V101_CHANGE
    BYTE changed[11];
} V101Place;
static const V101Place kV101Places[] = {
    { 0x412DD0, 6, { 0x81, 0xEC, 0xF8, 0x09, 0x00, 0x00 }, V101_FIX_412DD0 },                       // player lists
    { 0x415F96, 5, { 0x74, 0x33, 0x8A, 0x4E, 0x04 }, V101_FIX_415F96 },                             // message 0xC7
    { 0x4160BD, 6, { 0x74, 0x32, 0x8A, 0x06, 0x6A, 0x01 }, V101_FIX_4160BD },                       // the same
    { 0x468A4E, 5, { 0x68, 0x8C, 0x09, 0x70, 0x00 }, V101_CHANGE, { 0x68, 0x70, 0x09, 0x70, 0x00 } },   // MoveList.dat not from the CD
    { 0x501DCF, 2, { 0x74, 0x54 }, V101_CHANGE, { 0xEB, 0x54 } },                                   // message 0xA41
    { 0x557E3F, 7, { 0x80, 0x8E, 0xAE, 0x00, 0x00, 0x00, 0x01 }, V101_FIX_557E3F },                 // counters cleared
    { 0x557E64, 11, { 0x66, 0x8B, 0x4E, 0x60, 0x83, 0xC4, 0x14, 0x66, 0x01, 0x4E, 0x5E }, V101_CHANGE,
        { 0x83, 0xC4, 0x14, 0x66, 0xFF, 0x46, 0x5E, 0x90, 0x90, 0x90, 0x90 } },                      // counter grows by one
    { 0x568CA0, 5, { 0x83, 0xEC, 0x08, 0x53, 0x55 }, V101_FIX_568CA0 },                             // counters below 1,000,000
    { 0x5A0BF7, 9, { 0x6A, 0x00, 0x8D, 0x8C, 0x24, 0x78, 0x03, 0x00, 0x00 }, V101_FIX_5A0BF7 },     // a dialog's result
    { 0x5A0F95, 5, { 0xE8, 0x16, 0xD0, 0x0C, 0x00 }, V101_FIX_5A0F95 },                             // the same, after DoModal
    { 0x5A2836, 14, { 0x33, 0xC9, 0x8A, 0x4E, 0x20, 0x8A, 0x91, 0x58, 0x5A, 0x6C, 0x00, 0x88, 0x56, 0x21 }, V101_FIX_5A2836 },   // second positions
    { 0x5A2FC0, 5, { 0x56, 0x8B, 0x74, 0x24, 0x08 }, V101_FIX_5A2FC0 },                             // goalkeepers' traits
    { 0x5BD540, 5, { 0xB8, 0x6C, 0x9F, 0x00, 0x00 }, V101_FIX_5BD540 },                             // managers' data
};

// Whether the n bytes at p are committed and readable (within one region).
static BOOL ReadableAt(const BYTE *p, SIZE_T n)
{
    MEMORY_BASIC_INFORMATION mbi;
    if (VirtualQuery(p, &mbi, sizeof mbi) == 0 || mbi.State != MEM_COMMIT || !Readable(mbi.Protect)) return FALSE;
    return p + n <= (const BYTE *)mbi.BaseAddress + mbi.RegionSize;
}

// Copies the 1.01 code into fresh memory and writes its places, once every place holds its original bytes.
static int PatchV101(BYTE *base, SIZE_T size)
{
    if (base != (BYTE *)IMAGE_BASE) return DONE;
    for (int i = 0; i < ARRAYSIZE(kV101Places); i++)
    {
        const V101Place *place = &kV101Places[i];
        BYTE *at = base + (place->va - IMAGE_BASE);
        if (place->va - IMAGE_BASE + place->length > size || !ReadableAt(at, place->length)) return NOT_YET;
        if (!Equal(at, place->original, place->length)) return NOT_YET;
    }

    BYTE *code = (BYTE *)VirtualAlloc(NULL, sizeof kV101Code, MEM_COMMIT | MEM_RESERVE, PAGE_EXECUTE_READWRITE);
    if (!code) return DONE;
    Copy(code, kV101Code, sizeof kV101Code);
    DWORD moved = (DWORD)(ULONG_PTR)code - V101_ADDRESS;
    for (int i = 0; i < ARRAYSIZE(kV101Relative); i++) *(DWORD *)(code + kV101Relative[i]) -= moved;
    for (int i = 0; i < ARRAYSIZE(kV101Absolute); i++) *(DWORD *)(code + kV101Absolute[i]) += moved;
    FlushInstructionCache(GetCurrentProcess(), code, sizeof kV101Code);

    for (int i = 0; i < ARRAYSIZE(kV101Places); i++)
    {
        const V101Place *place = &kV101Places[i];
        BYTE *at = base + (place->va - IMAGE_BASE), bytes[14];
        if (place->target == V101_CHANGE)
            Copy(bytes, place->changed, place->length);
        else
        {
            LONG rel = (LONG)((code + place->target) - (at + 5));
            bytes[0] = 0xE9;                                    // jmp code+target / nop...
            Copy(bytes + 1, (const BYTE *)&rel, 4);
            for (int j = 5; j < place->length; j++) bytes[j] = 0x90;
        }
        Write(at, bytes, place->length);
    }
    return DONE;
}

// ---- 4096: interviews ----------------------------------------------------------------------------

// As in Patches.cs (InterviewEffects): the game applies an answer's two effect pairs from the interview
// context, but the engine stores them only in its copy (engine+11C). Both calls go to a stub (that of
// A2G-Trainer's InterviewEffectsFix, which recognizes it by its signature) that copies the pairs over,
// sends code 10 (team morale) to case 12 and books code 12 (a fine in DM) on the club's other costs.
// mov ecx,[engine] / push eax / call / push context / call apply (byte 17) / mov eax,[context+10]
static const WORD kInterviewApply[] = {
    0x8B, 0x0D, ANY, ANY, ANY, ANY, 0x50, 0xE8, ANY, ANY, ANY, ANY,
    0x68, ANY, ANY, ANY, ANY, 0xE8, ANY, ANY, ANY, ANY, 0xA1, ANY, ANY, ANY, ANY };
// case 12: mov ecx,[esp+50] / mov edx,[esp+5C] / push ecx / push edx / call morale (byte 10) / add esp,8 / ... / ret
static const WORD kInterviewMorale[] = {
    0x8B, 0x4C, 0x24, 0x50, 0x8B, 0x54, 0x24, 0x5C, 0x51, 0x52, 0xE8, ANY, ANY, ANY, ANY,
    0x83, 0xC4, 0x08, 0x5F, 0x5E, 0x83, 0xC4, 0x40, 0xC3 };
// The first instructions of the effect application and of the morale change.
static const BYTE kInterviewApplyHead[14] = { 0x53, 0x55, 0x56, 0x8B, 0x74, 0x24, 0x10, 0x57, 0x66, 0x8B, 0x46, 0x74, 0x50, 0xE8 };
static const BYTE kInterviewMoraleHead[22] = {
    0x8B, 0x4C, 0x24, 0x04, 0x8B, 0x44, 0x24, 0x08, 0x66, 0x0F, 0xB6, 0x91, 0x9C, 0x00, 0x00, 0x00, 0x03, 0xC2, 0x66, 0x3D, 0xC8, 0x00 };
#define ENGINE_BEFORE_CONTEXT 0xC8
#define MONEY_FACTOR_BEHIND_CONTEXT 0xC258
// The stub as in Patches.cs for the addresses 0, the flag set; the addresses are added at the STUB_* places.
static const BYTE kInterviewStub[256] = {
    0x41, 0x32, 0x47, 0x49, 0x4E, 0x54, 0x30, 0x32, 0x01, 0, 0, 0, 0, 0, 0, 0,                  // "A2GINT02", flag 1
    0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
    0x80, 0x3D, 0x08, 0x00, 0x00, 0x00, 0x00, 0x74, 0x6D, 0xA1, 0x00, 0x00, 0x00, 0x00, 0x85, 0xC0,   // +20: apply entry
    0x74, 0x64, 0x8B, 0x80, 0x1C, 0x01, 0x00, 0x00, 0x85, 0xC0, 0x74, 0x5A, 0x8B, 0x48, 0x14, 0x8B,
    0x50, 0x18, 0x83, 0xF9, 0x0C, 0x75, 0x0E, 0x81, 0xE2, 0xFF, 0xFF, 0xFF, 0x00, 0x81, 0xCA, 0x00,
    0x00, 0x00, 0x40, 0xEB, 0x07, 0x83, 0xF9, 0x0A, 0x75, 0x02, 0xB1, 0x0C, 0x89, 0x0D, 0x14, 0x00,
    0x00, 0x00, 0x89, 0x15, 0x18, 0x00, 0x00, 0x00, 0x8B, 0x48, 0x1C, 0x8B, 0x50, 0x20, 0x83, 0xF9,
    0x0C, 0x75, 0x0E, 0x81, 0xE2, 0xFF, 0xFF, 0xFF, 0x00, 0x81, 0xCA, 0x00, 0x00, 0x00, 0x40, 0xEB,
    0x07, 0x83, 0xF9, 0x0A, 0x75, 0x02, 0xB1, 0x0C, 0x89, 0x0D, 0x1C, 0x00, 0x00, 0x00, 0x89, 0x15,
    0x20, 0x00, 0x00, 0x00, 0xEB, 0x1A, 0x31, 0xC9, 0x89, 0x0D, 0x14, 0x00, 0x00, 0x00, 0x89, 0x0D,
    0x18, 0x00, 0x00, 0x00, 0x89, 0x0D, 0x1C, 0x00, 0x00, 0x00, 0x89, 0x0D, 0x20, 0x00, 0x00, 0x00,
    0xB8, 0x00, 0x00, 0x00, 0x00, 0xFF, 0xE0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
    0x80, 0x7C, 0x24, 0x0B, 0x40, 0x75, 0x27, 0x8B, 0x44, 0x24, 0x08, 0x25, 0xFF, 0xFF, 0xFF, 0x00,   // +C0: morale entry
    0x0F, 0xAF, 0x05, 0x00, 0x00, 0x00, 0x00, 0x99, 0xB9, 0x64, 0x00, 0x00, 0x00, 0xF7, 0xF9, 0x8B,
    0x54, 0x24, 0x04, 0x85, 0xD2, 0x74, 0x06, 0x01, 0x82, 0x8C, 0x03, 0x00, 0x00, 0xC3, 0xB8, 0x00,
    0x00, 0x00, 0x00, 0xFF, 0xE0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
#define STUB_FLAG 0x22                 // the stub's own address
#define STUB_ENGINE 0x2A
#define STUB_APPLY 0xB1
#define STUB_MONEY_FACTOR 0xD3
#define STUB_MORALE 0xEF
#define STUB_APPLY_ENTRY 0x20
#define STUB_MORALE_ENTRY 0xC0
static const SIZE_T kInterviewContextRefs[8] = { 0x5E, 0x64, 0x8A, 0x90, 0x9A, 0xA0, 0xA6, 0xAC };

// Adds value to the (unaligned) dword at p.
static void AddDword(BYTE *p, DWORD value)
{
    DWORD d;
    Copy((BYTE *)&d, p, 4);
    d += value;
    Copy(p, (const BYTE *)&d, 4);
}

// Where the call (E8 rel32) at call leads.
static BYTE *CallTarget(BYTE *call)
{
    LONG rel;
    Copy((BYTE *)&rel, call + 1, 4);
    return call + 5 + rel;
}

// Points the call at call to target.
static void Redirect(BYTE *call, BYTE *target)
{
    LONG rel = (LONG)(target - (call + 5));
    Write(call + 1, (const BYTE *)&rel, 4);
}

// Copies the stub into fresh memory, fills in the game's addresses and redirects both calls to it.
static int PatchInterview(BYTE *base, SIZE_T size)
{
    BYTE *site, *moraleSite;
    int count = Count(base, size, kInterviewApply, ARRAYSIZE(kInterviewApply), &site);
    int countMorale = Count(base, size, kInterviewMorale, ARRAYSIZE(kInterviewMorale), &moraleSite);
    if (count == 0 || countMorale == 0) return NOT_YET;
    if (count != 1 || countMorale != 1) return DONE;

    DWORD engine, context, field;
    Copy((BYTE *)&engine, site + 2, 4);
    Copy((BYTE *)&context, site + 13, 4);
    Copy((BYTE *)&field, site + 23, 4);
    if (field != context + 0x10 || engine != context - ENGINE_BEFORE_CONTEXT) return DONE;
    BYTE *applyCall = site + 17, *moraleCall = moraleSite + 10;
    BYTE *apply = CallTarget(applyCall), *morale = CallTarget(moraleCall);
    if (!ReadableAt(apply, sizeof kInterviewApplyHead) || !Equal(apply, kInterviewApplyHead, sizeof kInterviewApplyHead)
        || !ReadableAt(morale, sizeof kInterviewMoraleHead) || !Equal(morale, kInterviewMoraleHead, sizeof kInterviewMoraleHead)) return DONE;

    BYTE *stub = (BYTE *)VirtualAlloc(NULL, 4096, MEM_COMMIT | MEM_RESERVE, PAGE_EXECUTE_READWRITE);
    if (!stub) return DONE;
    Copy(stub, kInterviewStub, sizeof kInterviewStub);
    AddDword(stub + STUB_FLAG, (DWORD)(ULONG_PTR)stub);
    AddDword(stub + STUB_ENGINE, engine);
    for (int i = 0; i < ARRAYSIZE(kInterviewContextRefs); i++) AddDword(stub + kInterviewContextRefs[i], context);
    AddDword(stub + STUB_APPLY, (DWORD)(ULONG_PTR)apply);
    AddDword(stub + STUB_MONEY_FACTOR, context + MONEY_FACTOR_BEHIND_CONTEXT);
    AddDword(stub + STUB_MORALE, (DWORD)(ULONG_PTR)morale);
    FlushInstructionCache(GetCurrentProcess(), stub, sizeof kInterviewStub);

    // The morale entry first: it passes everything unmarked on, so it is harmless on its own.
    Redirect(moraleCall, stub + STUB_MORALE_ENTRY);
    Redirect(applyCall, stub + STUB_APPLY_ENTRY);
    return DONE;
}

// ---- 8192: speeches -------------------------------------------------------------------------------

// As in Patches.cs (SpeechPenalty): a pre-match speech that doesn't fit the situation weakens the team by
// its percentages' amounts (the game only shows "unpassend" and takes the use). Three places: the
// motivation roll's failure (xor edi,edi -> xor ebx,ebx, so it stays without effect), the marking of the
// team block after the check (+E5 = -number for a speech that doesn't fit) and FUN_004c4bd0, rewritten in
// its own 176 bytes so a negative number takes the amounts off and leaves the hardness. The function reads
// the speech table; its address comes from the original code.
#define ROLL_HEAD 0xE8, ANY, ANY, ANY, ANY
#define ROLL_TAIL 0xEB, 0x36, 0x6A, 0x04, 0xE8
static const WORD kSpeechRoll[] = { ROLL_HEAD, 0x33, 0xFF, ROLL_TAIL };       // xor edi,edi
static const BYTE kSpeechRollPatched[] = { 0x33, 0xDB };                      // xor ebx,ebx
#define ROLL_AT 5

// lea ecx,[esp+6A0] / call (the message ends), the marking (ebx: a use taken, edi: it works, ebp: the
// club, [esp+17C0]: the team block), lea ecx,[esp+888].
#define MARK_HEAD 0x8D, 0x8C, 0x24, 0xA0, 0x06, 0x00, 0x00, 0xE8, ANY, ANY, ANY, ANY
#define MARK_AT 12
static const WORD kSpeechMark[] = { MARK_HEAD,
    0x85, 0xDB,                                 // 00: test ebx, ebx
    0x74, 0x24,                                 // 02: je 0x28
    0x85, 0xFF,                                 // 04: test edi, edi
    0x74, 0x20,                                 // 06: je 0x28
    0x8B, 0x84, 0x24, 0xC0, 0x17, 0x00, 0x00,   // 08: mov eax, dword ptr [esp + 0x17c0]
    0xC6, 0x80, 0xE4, 0x00, 0x00, 0x00, 0x01,   // 0F: mov byte ptr [eax + 0xe4], 1
    0x8A, 0x8D, 0x40, 0x14, 0x00, 0x00,         // 16: mov cl, byte ptr [ebp + 0x1440]
    0x88, 0x88, 0xE5, 0x00, 0x00, 0x00,         // 1C: mov byte ptr [eax + 0xe5], cl
    0x66, 0xC7, 0x40, 0x1C, 0x64, 0x00,         // 22: mov word ptr [eax + 0x1c], 0x64
    0x8D, 0x8C, 0x24, 0x88, 0x08, 0x00, 0x00 };
// The same 40 bytes, +E5 = -number where it doesn't fit; "cmp ax,imm16" (66 3D) skips the neg cl behind it.
static const BYTE kSpeechMarkPatched[40] = {
    0x4B,                                       // 00: dec ebx
    0x75, 0x25,                                 // 01: jne 0x28
    0x8B, 0x84, 0x24, 0xC0, 0x17, 0x00, 0x00,   // 03: mov eax, dword ptr [esp + 0x17c0]
    0x83, 0xE8, 0x80,                           // 0A: sub eax, -0x80
    0x8A, 0x8D, 0x40, 0x14, 0x00, 0x00,         // 0D: mov cl, byte ptr [ebp + 0x1440]
    0x85, 0xFF,                                 // 13: test edi, edi
    0x74, 0x0C,                                 // 15: je 0x23
    0xC6, 0x40, 0x64, 0x01,                     // 17: mov byte ptr [eax + 0x64], 1
    0x66, 0xC7, 0x40, 0x9C, 0x64, 0x00,         // 1B: mov word ptr [eax - 0x64], 0x64
    0x66, 0x3D, 0xF6, 0xD9,                     // 21: cmp ax, 0xd9f6
    0x88, 0x48, 0x65,                           // 25: mov byte ptr [eax + 0x65], cl
};

// FUN_004c4bd0 with its padding; ANY: the speech table's addresses (an entry's +10, +C, +D, +E).
static const WORD kSpeechApply[176] = {
    0x56,                                       // 00: push esi
    0x8B, 0x74, 0x24, 0x08,                     // 01: mov esi, dword ptr [esp + 8]
    0x57,                                       // 05: push edi
    0x8A, 0x86, 0xE5, 0x00, 0x00, 0x00,         // 06: mov al, byte ptr [esi + 0xe5]
    0x84, 0xC0,                                 // 0C: test al, al
    0x0F, 0x84, 0x8F, 0x00, 0x00, 0x00,         // 0E: je 0xa3
    0x25, 0xFF, 0x00, 0x00, 0x00,               // 14: and eax, 0xff
    0x8D, 0x3C, 0x80,                           // 19: lea edi, [eax + eax*4]
    0xC1, 0xE7, 0x02,                           // 1C: shl edi, 2
    0xF6, 0x87, ANY, ANY, ANY, ANY, 0x01,       // 1F: test byte ptr [edi + 0x6b7d60], 1
    0x74, 0x22,                                 // 26: je 0x4a
    0x66, 0x83, 0x7C, 0x24, 0x14, 0x00,         // 28: cmp word ptr [esp + 0x14], 0
    0x8B, 0x44, 0x24, 0x10,                     // 2E: mov eax, dword ptr [esp + 0x10]
    0x74, 0x0C,                                 // 32: je 0x40
    0x66, 0x8B, 0x48, 0x10,                     // 34: mov cx, word ptr [eax + 0x10]
    0x66, 0x3B, 0x48, 0x0E,                     // 38: cmp cx, word ptr [eax + 0xe]
    0x7F, 0x65,                                 // 3C: jg 0xa3
    0xEB, 0x0A,                                 // 3E: jmp 0x4a
    0x66, 0x8B, 0x50, 0x0E,                     // 40: mov dx, word ptr [eax + 0xe]
    0x66, 0x3B, 0x50, 0x10,                     // 44: cmp dx, word ptr [eax + 0x10]
    0x7F, 0x59,                                 // 48: jg 0xa3
    0x0F, 0xBE, 0x8F, ANY, ANY, ANY, ANY,       // 4A: movsx ecx, byte ptr [edi + 0x6b7d5c]
    0x0F, 0xBF, 0x46, 0x10,                     // 51: movsx eax, word ptr [esi + 0x10]
    0x83, 0xC1, 0x64,                           // 55: add ecx, 0x64
    0x0F, 0xAF, 0xC8,                           // 58: imul ecx, eax
    0xB8, 0x1F, 0x85, 0xEB, 0x51,               // 5B: mov eax, 0x51eb851f
    0xF7, 0xE9,                                 // 60: imul ecx
    0xC1, 0xFA, 0x05,                           // 62: sar edx, 5
    0x8B, 0xCA,                                 // 65: mov ecx, edx
    0xB8, 0x1F, 0x85, 0xEB, 0x51,               // 67: mov eax, 0x51eb851f
    0xC1, 0xE9, 0x1F,                           // 6C: shr ecx, 0x1f
    0x03, 0xD1,                                 // 6F: add edx, ecx
    0x66, 0x89, 0x56, 0x10,                     // 71: mov word ptr [esi + 0x10], dx
    0x0F, 0xBE, 0x8F, ANY, ANY, ANY, ANY,       // 75: movsx ecx, byte ptr [edi + 0x6b7d5d]
    0x0F, 0xBF, 0x56, 0x16,                     // 7C: movsx edx, word ptr [esi + 0x16]
    0x83, 0xC1, 0x64,                           // 80: add ecx, 0x64
    0x0F, 0xAF, 0xCA,                           // 83: imul ecx, edx
    0xF7, 0xE9,                                 // 86: imul ecx
    0xC1, 0xFA, 0x05,                           // 88: sar edx, 5
    0x8B, 0xC2,                                 // 8B: mov eax, edx
    0xC1, 0xE8, 0x1F,                           // 8D: shr eax, 0x1f
    0x03, 0xD0,                                 // 90: add edx, eax
    0x66, 0x89, 0x56, 0x16,                     // 92: mov word ptr [esi + 0x16], dx
    0x8A, 0x87, ANY, ANY, ANY, ANY,             // 96: mov al, byte ptr [edi + 0x6b7d5e]
    0x3C, 0x63,                                 // 9C: cmp al, 0x63
    0x74, 0x03,                                 // 9E: je 0xa3
    0x88, 0x46, 0x31,                           // A0: mov byte ptr [esi + 0x31], al
    0x5F,                                       // A3: pop edi
    0x5E,                                       // A4: pop esi
    0xC3,                                       // A5: ret
    0x90,                                       // A6: nop
    0x90,                                       // A7: nop
    0x90,                                       // A8: nop
    0x90,                                       // A9: nop
    0x90,                                       // AA: nop
    0x90,                                       // AB: nop
    0x90,                                       // AC: nop
    0x90,                                       // AD: nop
    0x90,                                       // AE: nop
    0x90,                                       // AF: nop
};
// Rewritten: the same for a positive number, the amounts taken off for a negative one; 0: the table.
static const BYTE kSpeechApplyPatched[176] = {
    0x56,                                       // 00: push esi
    0x8B, 0x74, 0x24, 0x08,                     // 01: mov esi, dword ptr [esp + 8]
    0x57,                                       // 05: push edi
    0x53,                                       // 06: push ebx
    0x0F, 0xBE, 0x86, 0xE5, 0x00, 0x00, 0x00,   // 07: movsx eax, byte ptr [esi + 0xe5]
    0x99,                                       // 0E: cdq
    0x8B, 0xDA,                                 // 0F: mov ebx, edx
    0x33, 0xC2,                                 // 11: xor eax, edx
    0x2B, 0xC2,                                 // 13: sub eax, edx
    0x74, 0x60,                                 // 15: je 0x77
    0x8D, 0x3C, 0x80,                           // 17: lea edi, [eax + eax*4]
    0xC1, 0xE7, 0x02,                           // 1A: shl edi, 2
    0xF6, 0x87, 0x00, 0x00, 0x00, 0x00, 0x01,   // 1D: test byte ptr [edi], 1
    0x74, 0x22,                                 // 24: je 0x48
    0x66, 0x83, 0x7C, 0x24, 0x18, 0x00,         // 26: cmp word ptr [esp + 0x18], 0
    0x8B, 0x44, 0x24, 0x14,                     // 2C: mov eax, dword ptr [esp + 0x14]
    0x74, 0x0C,                                 // 30: je 0x3e
    0x66, 0x8B, 0x48, 0x10,                     // 32: mov cx, word ptr [eax + 0x10]
    0x66, 0x3B, 0x48, 0x0E,                     // 36: cmp cx, word ptr [eax + 0xe]
    0x7F, 0x3B,                                 // 3A: jg 0x77
    0xEB, 0x0A,                                 // 3C: jmp 0x48
    0x66, 0x8B, 0x50, 0x0E,                     // 3E: mov dx, word ptr [eax + 0xe]
    0x66, 0x3B, 0x50, 0x10,                     // 42: cmp dx, word ptr [eax + 0x10]
    0x7F, 0x2F,                                 // 46: jg 0x77
    0x0F, 0xBE, 0x8F, 0x00, 0x00, 0x00, 0x00,   // 48: movsx ecx, byte ptr [edi]
    0x8D, 0x46, 0x10,                           // 4F: lea eax, [esi + 0x10]
    0xE8, 0x24, 0x00, 0x00, 0x00,               // 52: call 0x7b
    0x0F, 0xBE, 0x8F, 0x00, 0x00, 0x00, 0x00,   // 57: movsx ecx, byte ptr [edi]
    0x8D, 0x46, 0x16,                           // 5E: lea eax, [esi + 0x16]
    0xE8, 0x15, 0x00, 0x00, 0x00,               // 61: call 0x7b
    0x85, 0xDB,                                 // 66: test ebx, ebx
    0x75, 0x0D,                                 // 68: jne 0x77
    0x8A, 0x87, 0x00, 0x00, 0x00, 0x00,         // 6A: mov al, byte ptr [edi]
    0x3C, 0x63,                                 // 70: cmp al, 0x63
    0x74, 0x03,                                 // 72: je 0x77
    0x88, 0x46, 0x31,                           // 74: mov byte ptr [esi + 0x31], al
    0x5B,                                       // 77: pop ebx
    0x5F,                                       // 78: pop edi
    0x5E,                                       // 79: pop esi
    0xC3,                                       // 7A: ret
    0x85, 0xDB,                                 // 7B: test ebx, ebx
    0x74, 0x06,                                 // 7D: je 0x85
    0x85, 0xC9,                                 // 7F: test ecx, ecx
    0x7C, 0x02,                                 // 81: jl 0x85
    0xF7, 0xD9,                                 // 83: neg ecx
    0x50,                                       // 85: push eax
    0x0F, 0xBF, 0x00,                           // 86: movsx eax, word ptr [eax]
    0x83, 0xC1, 0x64,                           // 89: add ecx, 0x64
    0x0F, 0xAF, 0xC8,                           // 8C: imul ecx, eax
    0xB8, 0x1F, 0x85, 0xEB, 0x51,               // 8F: mov eax, 0x51eb851f
    0xF7, 0xE9,                                 // 94: imul ecx
    0xC1, 0xFA, 0x05,                           // 96: sar edx, 5
    0x8B, 0xCA,                                 // 99: mov ecx, edx
    0xC1, 0xE9, 0x1F,                           // 9B: shr ecx, 0x1f
    0x03, 0xD1,                                 // 9E: add edx, ecx
    0x58,                                       // A0: pop eax
    0x66, 0x89, 0x10,                           // A1: mov word ptr [eax], dx
    0xC3,                                       // A4: ret
    0xCC, 0xCC, 0xCC, 0xCC, 0xCC, 0xCC, 0xCC, 0xCC, 0xCC, 0xCC, 0xCC };
// Where the original and the rewritten function read the speech table: an entry's flags, defence, attack, hardness.
static const SIZE_T kSpeechApplyRefs[4] = { 0x21, 0x4D, 0x78, 0x98 }, kSpeechPatchedRefs[4] = { 0x1F, 0x4B, 0x5A, 0x6C };
static const DWORD kSpeechFields[4] = { 0x10, 0x0C, 0x0D, 0x0E };

// Rewrites the speech function with the table address taken over, then the mark and the roll.
static int PatchSpeech(BYTE *base, SIZE_T size)
{
    BYTE *roll, *mark, *apply, code[sizeof kSpeechApplyPatched];
    int countRoll = Count(base, size, kSpeechRoll, ARRAYSIZE(kSpeechRoll), &roll);
    int countMark = Count(base, size, kSpeechMark, ARRAYSIZE(kSpeechMark), &mark);
    int countApply = Count(base, size, kSpeechApply, ARRAYSIZE(kSpeechApply), &apply);
    if (countRoll == 0 || countMark == 0 || countApply == 0) return NOT_YET;
    if (countRoll != 1 || countMark != 1 || countApply != 1) return DONE;

    DWORD table, value;
    Copy((BYTE *)&table, apply + kSpeechApplyRefs[0], 4);
    table -= kSpeechFields[0];
    Copy(code, kSpeechApplyPatched, sizeof code);
    for (int i = 0; i < 4; i++)
    {
        Copy((BYTE *)&value, apply + kSpeechApplyRefs[i], 4);
        if (value != table + kSpeechFields[i]) return DONE;
        value = table + kSpeechFields[i];
        Copy(code + kSpeechPatchedRefs[i], (BYTE *)&value, 4);
    }

    // The function first: on its own it does what the original does, as nothing marks a negative number yet.
    Write(apply, code, sizeof code);
    Write(mark + MARK_AT, kSpeechMarkPatched, sizeof kSpeechMarkPatched);
    Write(roll + ROLL_AT, kSpeechRollPatched, sizeof kSpeechRollPatched);
    return DONE;
}

// ---- the patch thread -----------------------------------------------------------------------------

// Tries every patch switched on in g_config every POLL_MS until each is done or POLL_COUNT runs out.
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
    BOOL cupDone = !(flags & FLAG_CUP_DRAW), calcDone = !(flags & FLAG_CALC_INIT), v101Done = !(flags & FLAG_V101);
    BOOL buttonDone = !(flags & FLAG_BUTTON_INIT), interviewDone = !(flags & FLAG_INTERVIEW), speechDone = !(flags & FLAG_SPEECH);
    BOOL negotiationDone = !(flags & FLAG_NEGOTIATION);
    #define ALL_DONE (goalsDone && arrowDone && videoDone && sleepDone && soundDone && diskDone && squadDone && displayDone && cupDone && calcDone && v101Done && buttonDone && interviewDone && speechDone && negotiationDone)
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
        if (!calcDone) calcDone = PatchRewrite(base, size, kCalc, kCalcPatched, ARRAYSIZE(kCalc)) == DONE;
        if (!v101Done) v101Done = PatchV101(base, size) == DONE;
        if (!buttonDone) buttonDone = PatchRewrite(base, size, kButton, kButtonPatched, ARRAYSIZE(kButton)) == DONE;
        if (!interviewDone) interviewDone = PatchInterview(base, size) == DONE;
        if (!speechDone) speechDone = PatchSpeech(base, size) == DONE;
        if (!negotiationDone) negotiationDone = PatchRewrite(base, size, kNegotiation, kNegotiationPatched, ARRAYSIZE(kNegotiation)) == DONE;
        if (!ALL_DONE) Sleep(POLL_MS);
    }
    #undef ALL_DONE
    return 0;
}

// Entry point (no CRT): on load only sets up the lock and starts the patch thread.
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
