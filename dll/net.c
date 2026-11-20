// A2GNet: network play for Anstoss 2 Gold (prototype; 1999 exe and GOG run.exe).
//
// The game ships a nearly complete client/server layer that was never switched on: data accessors ask
// "am I the server?" and fetch records from the server otherwise, the main loop lets only the server
// simulate, and a message dispatcher (0x5DD6A0) handles requests, replies and the transfer of the whole
// game state to joining computers. What is missing is the runtime: DirectPlay is never created, nothing
// sends the queued packets, nothing calls the receive pump, and the packet size is never set.
// (Analysis: A2G-Trainer-XP docs/pointer-investigation-guide.md, "Netzwerkspiel".)
//
// This module supplies that runtime instead of DirectPlay:
//   - the game's enqueue function (0x5EBF70) is redirected here; each slot goes out over TCP at once in
//     the packet format the game's receive side expects, then the slot is freed, so the game's send list
//     stays empty and its "wait until sent" loops pass straight through;
//   - the broadcast to "all other computers" (0x5D3C00) goes to every connected computer;
//   - a small object with the DirectPlay 1 vtable sits in the game's DirectPlay pointer (0xC1FAE0); its
//     Receive hands over what came in, and the network thread calls the game's receive pump (0x5DD580);
//   - the host is computer 1 (the game's own ID in single player), clients get 2, 3, 4 from the host.
//     A joining client is announced to the host's game as DPSYS_ADDPLAYER, so the game itself tells the
//     client the server's ID (message 0x65/0x1FA);
//   - on the host, Ctrl+F11 sends the game state to the clients (the game's own distribution, 0x4280E0).
//
// Wire format between the computers: frames of u32 length | u8 type | payload over one TCP connection per
// client (star around the host). GAME frames carry u32 from, u32 to and the game's packet.
//
// Enabled only when a2gnet.ini next to the exe says Enabled=1 and the exe is a known build.
// Settings (section [A2GNet]): Mode=host|join, Host=<address of the host>, Port=47720, Name=<shown name>,
// SecondInstance=1 (allows a second game on the same computer, for tests), LogVerbose=1.
// Log: a2gnet.log next to the exe.

#include <winsock2.h>
#include <windows.h>
#include <stdarg.h>
#include "net.h"

// ---- small helpers (no C runtime) -------------------------------------------------------------------

#pragma function(memcpy)
void *memcpy(void *dst, const void *src, size_t count)
{
    unsigned char *d = (unsigned char *)dst;
    const unsigned char *s = (const unsigned char *)src;
    while (count--) *d++ = *s++;
    return dst;
}

static void *Alloc(DWORD n) { return HeapAlloc(GetProcessHeap(), HEAP_ZERO_MEMORY, n); }
static void Free(void *p) { if (p) HeapFree(GetProcessHeap(), 0, p); }

// ---- the game ---------------------------------------------------------------------------------------

// The two builds whose code is readable when the DLL loads: the 1999 exe (needs the original CD and a
// 16-bit desktop) and GOG's run.exe. Data lies 0x3140 higher in GOG's; code moved by varying amounts.
typedef struct {
    const char *name;
    DWORD send;         // int  cdecl (from, to, guaranteed, data, size): splits into slots, enqueues
    DWORD free;         // void cdecl (ptr): the game's heap free
    DWORD netInit;      // void cdecl (): buffers, slots, events of the network layer
    DWORD pump;         // void cdecl (): Receive until no messages, dispatch each
    DWORD distribute;   // void cdecl (): send names, teams, leagues ... to all other computers
    DWORD enqueue;      // hooked: void cdecl (slot)
    DWORD broadcast;    // hooked: void cdecl (message, size)
    DWORD mutexJe;      // je to "already running" after CreateMutex("ANSTOSS2MUTEX")
    DWORD data;         // added to the 1999 data addresses below
    BYTE sig[8][11];    // first bytes of send, free, netInit, pump, distribute, enqueue, broadcast, mutexJe - 5
    DWORD netActive;    // hooked stub "network game?" (xor eax,eax / ret), FUN_005d3fb0
    DWORD taskSend;     // void cdecl (): sends pending tasks to the computers they belong to (orphaned)
    DWORD addJoiner;    // void cdecl (first name, last name, difficulty, computer): waiting list of new managers
    DWORD takeoverCall; // call of the managers' table entry when the server takes over a waiting manager
    DWORD loadCall;     // call of "load a game" in the game's logic thread (menu choice 1)
    DWORD bulk;         // void cdecl (class, kind, data, size, extra): broadcast of a block (FUN_005d3850)
    DWORD menuCall;     // call of FUN_00644070 (wait for the main menu's answer) in the logic thread
    DWORD leave;        // void cdecl (manager, keep): a manager leaves the game (FUN_005d57c0)
    DWORD waitSent;     // hooked: int cdecl (): wait until the send list is empty (FUN_005ebfb0)
    DWORD eventCall[2]; // FUN_00590a00's calls of FUN_005d2480 that add a manager's event as tasks for the computers
} Build;

static const Build kBuilds[] = {
    { "anstoss2.exe 1999", 0x5C7A60, 0x66CD97, 0x5DD370, 0x5DD580, 0x4280E0, 0x5EBF70, 0x5D3C00, 0x40C50A, 0, {
        { 0x8B, 0x44, 0x24, 0x14, 0x53, 0x55, 0x8B, 0x6C, 0x24, 0x18, 0x56 },
        { 0xFF, 0x74, 0x24, 0x04, 0xE8, 0x70, 0xBE, 0xFE, 0xFF, 0x59, 0xC3 },
        { 0x57, 0xB9, 0xC4, 0x09, 0x00, 0x00, 0x33, 0xC0, 0xBF, 0x30, 0x25 },
        { 0xA1, 0xE0, 0xFA, 0xC1, 0x00, 0x83, 0xEC, 0x0C, 0x85, 0xC0, 0x74 },
        { 0x6A, 0x01, 0x6A, 0x02, 0x6A, 0x05, 0xE8, 0x75, 0x88, 0x16, 0x00 },
        { 0x8B, 0x44, 0x24, 0x04, 0x66, 0x83, 0x78, 0x0C, 0x00, 0x75, 0x12 },
        { 0x83, 0xEC, 0x10, 0x8D, 0x44, 0x24, 0x00, 0x50, 0xE8, 0x43, 0xA9 },
        { 0x3D, 0xB7, 0x00, 0x00, 0x00, 0x0F, 0x84, 0x24, 0x05, 0x00, 0x00 } },
      0x5D3FB0, 0x5D2BC0, 0x5D56A0, 0x5D5780, 0x644A44, 0x5D3850, 0x644A0D, 0x5D57C0, 0x5EBFB0, { 0x590A70, 0x590AC7 } },
    { "GOG run.exe", 0x5CA790, 0x671BC7, 0x5E0D10, 0x5E0F20, 0x42A1B0, 0x5EF910, 0x5D7340, 0x40C4EA, 0x3140, {
        { 0x8B, 0x44, 0x24, 0x14, 0x53, 0x55, 0x8B, 0x6C, 0x24, 0x18, 0x56 },
        { 0xFF, 0x74, 0x24, 0x04, 0xE8, 0x70, 0xBE, 0xFE, 0xFF, 0x59, 0xC3 },
        { 0x57, 0xB9, 0xC4, 0x09, 0x00, 0x00, 0x33, 0xC0, 0xBF, 0x70, 0x56 },
        { 0xA1, 0x20, 0x2C, 0xC2, 0x00, 0x83, 0xEC, 0x0C, 0x85, 0xC0, 0x74 },
        { 0x6A, 0x01, 0x6A, 0x02, 0x6A, 0x05, 0xE8, 0x45, 0x8A, 0x16, 0x00 },
        { 0x8B, 0x44, 0x24, 0x04, 0x66, 0x83, 0x78, 0x0C, 0x00, 0x75, 0x12 },
        { 0x83, 0xEC, 0x10, 0x8D, 0x44, 0x24, 0x00, 0x50, 0xE8, 0xA3, 0xAB },
        { 0x3D, 0xB7, 0x00, 0x00, 0x00, 0x0F, 0x84, 0x24, 0x05, 0x00, 0x00 } },
      0x5D76F0, 0x5D6300, 0x5D8DE0, 0x5D8EC0, 0x649874, 0x5D6F90, 0x64983D, 0x5D8F00, 0x5EF950, { 0x592D10, 0x592D67 } },
};
static const Build *g_b;

#define G_SEND          (g_b->send)
#define G_FREE          (g_b->free)
#define G_NET_INIT      (g_b->netInit)
#define G_PUMP          (g_b->pump)
#define G_DISTRIBUTE    (g_b->distribute)
#define G_ENQUEUE       (g_b->enqueue)
#define G_BROADCAST     (g_b->broadcast)
#define G_MUTEX_JE      (g_b->mutexJe)

// Data addresses of the 1999 build.
#define D_DPLAY         (0xC1FAE0 + g_b->data)  // IDirectPlay *
#define D_SERVER_ID     (0xC1FAE8 + g_b->data)
#define D_MY_ID         (0xC1FAEC + g_b->data)
#define D_PACKET_SIZE   (0xC1FA60 + g_b->data)  // bytes per piece of a slot; never set by the game
#define D_RECV_EVENT    (0xCAD9C4 + g_b->data)  // HANDLE the clients wait on; DirectPlay would set it per message
#define D_LAST_SEQ      (0xC210D8 + g_b->data)  // Int16 [5]: last taken sequence number per computer index
#define D_APP_WND       (0x6FD908 + g_b->data)  // CWnd * of the main window, HWND at +0x1C
#define D_MANAGERS      (0x826666 + g_b->data)  // Int16 number of managers in the running game
#define D_GLOBALS       (0x826620 + g_b->data)  // the game's collected globals (calendar, managers ...), 0x15B0 bytes
#define D_JOIN_NAMES    (0xC1FB28 + g_b->data)  // waiting new managers, stride 0x24: first name
#define D_JOIN_COMPUTER (0xC1FB44 + g_b->data)  // ... and the computer they play on
#define D_TABLE         (0xCA43D0 + g_b->data)  // managers' table: entries of 0x54 bytes from +4
#define D_TASKS         (0xCA4288 + g_b->data)  // tasks: Int16 count, entries of 0x20 bytes from +4
#define D_SHUTDOWN      (0xCAD9DC + g_b->data)  // Int16, the game is being left
#define D_REQUESTS      (0xC1DD28 + g_b->data)  // 20 request slots of 0x16C bytes, +0x14 Int16 1 = waiting for the reply
#define D_REQ_USED      (0xC1F998 + g_b->data)  // Int16 per request slot, in use
#define GLOBALS_SIZE    0x15B0

#define PACKET_SIZE     20000       // one piece per slot (the game splits messages at 20000 bytes)
#define DP_OK           0
#define DPERR_NOMESSAGES      0x887700BEL
#define DPERR_BUFFERTOOSMALL  0x8877001EL
#define E_NOTIMPL_DP          0x80004001L

typedef int  (__cdecl *SendFn)(DWORD, DWORD, DWORD, const void *, DWORD);
typedef void (__cdecl *VoidFn)(void);
typedef void (__cdecl *FreeFn)(void *);
typedef int  (__cdecl *IntFn)(int);
typedef void (__cdecl *AddJoinerFn)(const char *, const char *, int, DWORD);
typedef void (__cdecl *BulkFn)(int, int, const void *, DWORD, DWORD);
typedef void (__cdecl *PtrFn)(void *);
typedef void (__cdecl *LeaveFn)(int, int);

#define GLOBAL(type, addr) (*(volatile type *)(addr))

// The game's slot (100 bytes, see FUN_005c7a60): +0 in use, +4 from, +8 to, +0x0C sequence,
// +0x10 data (game heap), +0x14 size.
#define SLOT_FROM(s)  (*(DWORD *)((BYTE *)(s) + 0x04))
#define SLOT_TO(s)    (*(DWORD *)((BYTE *)(s) + 0x08))
#define SLOT_DATA(s)  (*(BYTE **)((BYTE *)(s) + 0x10))
#define SLOT_SIZE(s)  (*(DWORD *)((BYTE *)(s) + 0x14))

// The packet the receive side (0x5DD3B0/0x5DD1F0) takes apart: piece index (bit 7: last piece),
// highest piece index, a tag, the sequence number (must grow per sender), the length, then the data.
#define PACKET_HEADER 16


// ---- settings and log -------------------------------------------------------------------------------

#define MODE_UNSET  -1
#define MODE_OFF    0
#define MODE_HOST   1
#define MODE_CLIENT 2

static char g_dir[MAX_PATH];        // the exe's folder, with trailing backslash
static char g_ini[MAX_PATH];
static volatile LONG g_mode = MODE_UNSET;
static int g_ask, g_verbose;
static char g_hostName[128];
static char g_firstName[10], g_lastName[16];   // the client's manager (as in the manager's record)
static int g_difficulty;
static WORD g_port;

#define IS_HOST   (g_mode == MODE_HOST)
#define IS_CLIENT (g_mode == MODE_CLIENT)

static CRITICAL_SECTION g_logLock;
static HANDLE g_log = INVALID_HANDLE_VALUE;

static void Log(const char *format, ...)
{
    char line[512];
    SYSTEMTIME t;
    va_list args;
    int n;
    if (g_log == INVALID_HANDLE_VALUE) return;
    GetLocalTime(&t);
    n = wsprintfA(line, "%02d:%02d:%02d.%03d ", t.wHour, t.wMinute, t.wSecond, t.wMilliseconds);
    va_start(args, format);
    n += wvsprintfA(line + n, format, args);
    va_end(args);
    line[n++] = '\r';
    line[n++] = '\n';
    EnterCriticalSection(&g_logLock);
    {
        DWORD written;
        WriteFile(g_log, line, n, &written, NULL);
    }
    LeaveCriticalSection(&g_logLock);
}

static int ModeFromText(const char *text)
{
    if (!lstrcmpiA(text, "host")) return MODE_HOST;
    if (!lstrcmpiA(text, "join")) return MODE_CLIENT;
    return MODE_OFF;
}

static void ReadSettings(void)
{
    char mode[16];
    GetPrivateProfileStringA("A2GNet", "Mode", "off", mode, sizeof mode, g_ini);
    g_ask = GetPrivateProfileIntA("A2GNet", "Ask", 1, g_ini);
    g_mode = g_ask ? MODE_UNSET : ModeFromText(mode);
    GetPrivateProfileStringA("A2GNet", "Host", "", g_hostName, sizeof g_hostName, g_ini);
    g_port = (WORD)GetPrivateProfileIntA("A2GNet", "Port", 47720, g_ini);
    g_verbose = GetPrivateProfileIntA("A2GNet", "LogVerbose", 0, g_ini);
    GetPrivateProfileStringA("A2GNet", "FirstName", "", g_firstName, sizeof g_firstName, g_ini);
    GetPrivateProfileStringA("A2GNet", "LastName", "", g_lastName, sizeof g_lastName, g_ini);
    g_difficulty = GetPrivateProfileIntA("A2GNet", "Difficulty", 2, g_ini);
}

static void SaveSettings(int mode)
{
    WritePrivateProfileStringA("A2GNet", "Mode", mode == MODE_HOST ? "host" : mode == MODE_CLIENT ? "join" : "off", g_ini);
    WritePrivateProfileStringA("A2GNet", "Host", g_hostName, g_ini);
    WritePrivateProfileStringA("A2GNet", "FirstName", g_firstName, g_ini);
    WritePrivateProfileStringA("A2GNet", "LastName", g_lastName, g_ini);
}

// ---- code changes -----------------------------------------------------------------------------------

static void WriteCode(DWORD at, const void *bytes, DWORD n)
{
    DWORD old;
    VirtualProtect((void *)at, n, PAGE_EXECUTE_READWRITE, &old);
    memcpy((void *)at, bytes, n);
    VirtualProtect((void *)at, n, old, &old);
    FlushInstructionCache(GetCurrentProcess(), (void *)at, n);
}

static void Jump(DWORD from, void *to)
{
    BYTE code[5];
    DWORD rel = (DWORD)to - (from + 5);
    code[0] = 0xE9;
    memcpy(code + 1, &rel, 4);
    WriteCode(from, code, 5);
}

// Points a call instruction at `to`; returns its old target.
static DWORD RedirectCall(DWORD call, void *to)
{
    DWORD old = call + 5 + *(const DWORD *)(call + 1), rel = (DWORD)to - (call + 5);
    WriteCode(call + 1, &rel, 4);
    return old;
}

static DWORD CallTarget(DWORD call) { return call + 5 + *(const DWORD *)(call + 1); }

static BOOL Starts(DWORD at, const BYTE *bytes, int n)
{
    int k;
    if (IsBadReadPtr((const void *)at, n)) return FALSE;
    for (k = 0; k < n; k++) if (((const BYTE *)at)[k] != bytes[k]) return FALSE;
    return TRUE;
}

// The same in both builds (the table's address after B9 differs and is not compared).
static const BYTE kNetActive[] = { 0x33, 0xC0, 0xC3, 0x90 };
static const BYTE kTaskSend[]  = { 0x83, 0xEC, 0x30, 0x53, 0x55, 0x56, 0x57, 0x6A, 0x00, 0x68 };
static const BYTE kAddJoiner[] = { 0x0F, 0xBF, 0x05 };
static const BYTE kTakeover[]  = { 0x6A, 0x00, 0x6A, 0x01, 0x51, 0x57, 0x8D, 0x56, 0xFF, 0x53, 0x52, 0xB9 };
static const BYTE kLoadCall[]  = { 0x53, 0xE8 };
static const BYTE kMenuCall[]  = { 0x8D, 0x54, 0x24, 0x14, 0x52, 0xE8 };
static const BYTE kLeave[]     = { 0x0F, 0xBF, 0x44, 0x24, 0x04, 0x66, 0x83, 0x7C, 0x24, 0x08, 0x00 };
static const BYTE kWaitSent[]  = { 0x85, 0xC0, 0x74, 0x2F, 0x57, 0x8B, 0x3D };   // from +5
static const BYTE kEventCall[] = { 0x6A, 0x00, 0x56, 0x8D, 0x4C, 0x24, 0x40, 0xE8 };  // push 0 / push esi / lea ecx,[esp+40] / call

static const Build *DetectBuild(void)
{
    int i;
    for (i = 0; i < ARRAYSIZE(kBuilds); i++)
    {
        const Build *b = &kBuilds[i];
        if (Starts(b->send, b->sig[0], 11) && Starts(b->free, b->sig[1], 11) && Starts(b->netInit, b->sig[2], 11)
            && Starts(b->pump, b->sig[3], 11) && Starts(b->distribute, b->sig[4], 11) && Starts(b->enqueue, b->sig[5], 11)
            && Starts(b->broadcast, b->sig[6], 11) && Starts(b->mutexJe - 5, b->sig[7], 11)
            && Starts(b->netActive, kNetActive, sizeof kNetActive) && Starts(b->taskSend, kTaskSend, sizeof kTaskSend)
            && Starts(b->addJoiner, kAddJoiner, sizeof kAddJoiner) && Starts(b->takeoverCall - 16, kTakeover, sizeof kTakeover)
            && Starts(b->loadCall - 1, kLoadCall, sizeof kLoadCall) && Starts(b->menuCall - 5, kMenuCall, sizeof kMenuCall)
            && Starts(b->leave, kLeave, sizeof kLeave) && Starts(b->waitSent + 5, kWaitSent, sizeof kWaitSent)
            && Starts(b->eventCall[0] - 7, kEventCall, sizeof kEventCall) && Starts(b->eventCall[1] - 7, kEventCall, sizeof kEventCall)) return b;
    }
    return NULL;
}

// ---- winsock (loaded at run time, so the proxy has no new import) -----------------------------------

typedef int    (WINAPI *WSAStartupFn)(WORD, LPWSADATA);
typedef SOCKET (WINAPI *SocketFn)(int, int, int);
typedef int    (WINAPI *BindFn)(SOCKET, const struct sockaddr *, int);
typedef int    (WINAPI *ListenFn)(SOCKET, int);
typedef SOCKET (WINAPI *AcceptFn)(SOCKET, struct sockaddr *, int *);
typedef int    (WINAPI *ConnectFn)(SOCKET, const struct sockaddr *, int);
typedef int    (WINAPI *SendFnW)(SOCKET, const char *, int, int);
typedef int    (WINAPI *RecvFn)(SOCKET, char *, int, int);
typedef int    (WINAPI *SelectFn)(int, fd_set *, fd_set *, fd_set *, const struct timeval *);
typedef int    (WINAPI *CloseFn)(SOCKET);
typedef struct hostent *(WINAPI *GetHostFn)(const char *);
typedef unsigned long (WINAPI *InetAddrFn)(const char *);
typedef u_short (WINAPI *HtonsFn)(u_short);
typedef int    (WINAPI *SetOptFn)(SOCKET, int, int, const char *, int);
typedef int    (WINAPI *LastErrorFn)(void);

static struct {
    WSAStartupFn startup; SocketFn socket; BindFn bind; ListenFn listen; AcceptFn accept;
    ConnectFn connect; SendFnW send; RecvFn recv; SelectFn select; CloseFn close;
    GetHostFn gethostbyname; InetAddrFn inet_addr; HtonsFn htons; SetOptFn setsockopt; LastErrorFn lastError;
} ws;

static BOOL LoadWinsock(void)
{
    HMODULE m = LoadLibraryA("ws2_32.dll");
    WSADATA data;
    if (!m) return FALSE;
    ws.startup = (WSAStartupFn)GetProcAddress(m, "WSAStartup");
    ws.socket = (SocketFn)GetProcAddress(m, "socket");
    ws.bind = (BindFn)GetProcAddress(m, "bind");
    ws.listen = (ListenFn)GetProcAddress(m, "listen");
    ws.accept = (AcceptFn)GetProcAddress(m, "accept");
    ws.connect = (ConnectFn)GetProcAddress(m, "connect");
    ws.send = (SendFnW)GetProcAddress(m, "send");
    ws.recv = (RecvFn)GetProcAddress(m, "recv");
    ws.select = (SelectFn)GetProcAddress(m, "select");
    ws.close = (CloseFn)GetProcAddress(m, "closesocket");
    ws.gethostbyname = (GetHostFn)GetProcAddress(m, "gethostbyname");
    ws.inet_addr = (InetAddrFn)GetProcAddress(m, "inet_addr");
    ws.htons = (HtonsFn)GetProcAddress(m, "htons");
    ws.setsockopt = (SetOptFn)GetProcAddress(m, "setsockopt");
    ws.lastError = (LastErrorFn)GetProcAddress(m, "WSAGetLastError");
    if (!ws.startup || !ws.socket || !ws.bind || !ws.listen || !ws.accept || !ws.connect || !ws.send
        || !ws.recv || !ws.select || !ws.close || !ws.gethostbyname || !ws.inet_addr || !ws.htons
        || !ws.setsockopt || !ws.lastError) return FALSE;
    return ws.startup(MAKEWORD(2, 2), &data) == 0;
}

// ---- connections ------------------------------------------------------------------------------------

#define MAX_COMPUTERS 4             // computer IDs 1..4; the host is 1
#define FRAME_HELLO   1             // client -> host: char name[32]
#define FRAME_WELCOME 2             // host -> client: u32 id
#define FRAME_GAME    3             // u32 from, u32 to, packet
#define FRAME_JOIN    4             // client -> host: char first[10], char last[16], u16 difficulty
#define FRAME_STATE   5             // host -> client: runs of (u32 1999 address, u32 length, bytes)
#define FRAME_MAX     (64 * 1024)

typedef struct {
    SOCKET s;                       // INVALID_SOCKET if unused
    DWORD id;                       // computer ID behind this connection (0 until known)
    BYTE *buf;                      // incoming bytes not yet a whole frame
    DWORD have;
    char name[32];
    BOOL stateSent;                 // host: the game state went to this client
} Connection;

static Connection g_conn[MAX_COMPUTERS + 1];   // host: one per client (index = id); client: [1] = host
static CRITICAL_SECTION g_sendLock;
static volatile DWORD g_myId;
static volatile LONG g_ready;           // the game's network layer is initialized
static volatile LONG g_stateReceived;   // client: the host's collected globals (0x66/0x834) arrived
static volatile LONG g_joined;          // client: went into the host's game
static volatile LONG g_joinSent;
static DWORD g_loadGame, g_tableAdd, g_menuWait, g_taskAdd;    // original targets of the redirected calls
static HWND g_mainWnd;

// FD_ISSET calls __WSAFDIsSet in ws2_32, which would be a static import.
static BOOL IsSet(SOCKET s, const fd_set *set)
{
    u_int i;
    for (i = 0; i < set->fd_count; i++) if (set->fd_array[i] == s) return TRUE;
    return FALSE;
}

static BOOL SendAll(SOCKET s, const BYTE *p, int n)
{
    while (n > 0)
    {
        int k = ws.send(s, (const char *)p, n, 0);
        if (k <= 0) return FALSE;
        p += k;
        n -= k;
    }
    return TRUE;
}

static BOOL SendFrame(Connection *c, BYTE type, const void *a, DWORD na, const void *b, DWORD nb)
{
    BYTE head[5];
    DWORD len = 1 + na + nb;
    BOOL ok;
    if (c->s == INVALID_SOCKET) return FALSE;
    memcpy(head, &len, 4);
    head[4] = type;
    EnterCriticalSection(&g_sendLock);
    ok = c->s != INVALID_SOCKET && SendAll(c->s, head, 5) && (na == 0 || SendAll(c->s, a, na)) && (nb == 0 || SendAll(c->s, b, nb));
    LeaveCriticalSection(&g_sendLock);
    return ok;
}

// The connection a packet for computer `to` leaves on: the host sends to the client directly, a client
// always to the host (which forwards).
static Connection *Route(DWORD to)
{
    if (IS_HOST) return (to >= 2 && to <= MAX_COMPUTERS && g_conn[to].s != INVALID_SOCKET) ? &g_conn[to] : NULL;
    return g_conn[1].s != INVALID_SOCKET ? &g_conn[1] : NULL;
}

static int ConnectedClients(void)
{
    int n = 0, id;
    for (id = 2; id <= MAX_COMPUTERS; id++) if (g_conn[id].s != INVALID_SOCKET && g_conn[id].id == (DWORD)id) n++;
    return n;
}

// ---- incoming packets (for the fake DirectPlay's Receive) -------------------------------------------

typedef struct Incoming { struct Incoming *next; DWORD from; DWORD size; BYTE data[1]; } Incoming;
static Incoming *g_inHead, *g_inTail;
static CRITICAL_SECTION g_inLock;
static SHORT g_nextSeq;

static void Deliver(DWORD from, const void *data, DWORD size)
{
    Incoming *m = (Incoming *)Alloc(sizeof(Incoming) + size);
    if (!m) return;
    m->from = from;
    m->size = size;
    memcpy(m->data, data, size);
    EnterCriticalSection(&g_inLock);
    if (from != 0 && size >= PACKET_HEADER)
    {
        // The game drops a packet whose sequence number is not above the last one it took from that
        // computer. Numbering on arrival keeps the numbers rising for every sender.
        if (g_nextSeq >= 30000)
        {
            int i;
            for (i = 0; i < 5; i++) ((volatile SHORT *)D_LAST_SEQ)[i] = 0;
            g_nextSeq = 0;
        }
        *(SHORT *)(m->data + 8) = ++g_nextSeq;
    }
    if (g_inTail) g_inTail->next = m; else g_inHead = m;
    g_inTail = m;
    LeaveCriticalSection(&g_inLock);
}

// DPSYS_ADDPLAYER as DirectPlay 1 lays it out: type 3, player type, ID, short name[20], long name[52].
static void AnnouncePlayer(DWORD id, const char *name)
{
    BYTE msg[4 + 4 + 4 + 20 + 52];
    int i;
    ZeroMemory(msg, sizeof msg);
    *(DWORD *)msg = 3;
    *(DWORD *)(msg + 8) = id;
    for (i = 0; i < 19 && name[i]; i++) msg[12 + i] = name[i];
    Deliver(0, msg, sizeof msg);
}

static void LogMessage(const char *dir, DWORD who, const BYTE *msg, DWORD size)
{
    WORD cls = *(const WORD *)(msg + 4), kind = *(const WORD *)(msg + 6);
    if (cls == 0x6A && size >= 0x38)        // a task: its kind and manager lie in the task copy at +0x18
    {
        SHORT m = *(const SHORT *)(msg + 0x28);
        Log("%s %lu: Aufgabe Art %u fuer Manager %lu (+0x10 %d, vorhanden %d)", dir, who, *(const WORD *)(msg + 0x1A),
            *(const DWORD *)(msg + 0x20), m, m >= 0 && m <= 4 ? *(volatile SHORT *)(D_GLOBALS + 0xA + m * 2) : -1);
    }
    else if (g_verbose)
        Log("%s %lu: Klasse 0x%X Art 0x%X, %lu Byte", dir, who, cls, kind, size);
}

// ---- mirroring the game state -----------------------------------------------------------------------

// The game keeps parts of its state only on the server and fetches others on demand, but not all of it
// (e.g. the newspaper's items). So the host mirrors the whole saved game state to each client: before a
// step (0x65/0x200) or a task (0x6A) goes to a client, the parts that changed since the last time go
// first. The host keeps a shadow copy per client to find them.
// Savegame blocks (1999 data addresses and sizes, from A2G-Trainer-XP docs/savegame-format.md), overlaps merged.
static const DWORD kStateBlocks[][2] = {
    // not saved, but what the event screens show (number, manager, club, text) must match the server's
    { 0x7832D8, 0x210 }, { 0x784DE0, 0x10 }, { 0xC21E98, 0x10 },
    { 0x6AC290, 0x2A }, { 0x6AC2D0, 0x2A }, { 0x6C18C0, 0x54 }, { 0x6C1918, 0x4C }, { 0x6C1C78, 0xA },
    { 0x6FC910, 0x12C }, { 0x7000C0, 0x510 }, { 0x700630, 0x14 }, { 0x700648, 0x284 }, { 0x7008D0, 0x130 },
    { 0x700AA0, 0xA0 }, { 0x700BA8, 0x2BE }, { 0x7027F8, 0x1B2 }, { 0x702A00, 0x4C }, { 0x702A50, 0x4C2 },
    { 0x702F18, 0xF0 }, { 0x703010, 0x4C }, { 0x703068, 0x4C }, { 0x7030B8, 0xE }, { 0x703D78, 0x1F6 },
    { 0x77D970, 0xC80 }, { 0x7828C8, 0x23C }, { 0x782B28, 0x3EC }, { 0x782F18, 0x11A }, { 0x783038, 0x11A },
    { 0x785968, 0x1B90 }, { 0x78BE58, 0x8192 }, { 0x793FF0, 0x191C0 }, { 0x7AE160, 0x1B80 },
    { 0x7CB5A8, 0x8F28 }, { 0x7DA7D0, 0xFA0 }, { 0x7DB790, 0x1A28 }, { 0x7DD1C0, 0x23540 },
    { 0x800750, 0x7530 }, { 0x807E20, 0x118D8 }, { 0x819700, 0x9600 }, { 0x8256C0, 0x2F0 },
    { 0x8264B8, 0x4C }, { 0x8265D0, 0x4C }, { 0x826620, 0x1614 }, { 0x827C38, 0x4C }, { 0x827FD8, 0x4 },
    { 0x827FE8, 0x3C }, { 0x828028, 0x4B00 }, { 0x82CBF8, 0x1860 }, { 0x846878, 0x2 }, { 0x846880, 0x10F7C },
    { 0x85DD60, 0x15EC }, { 0x85FF18, 0x34A }, { 0x860688, 0x116F0 }, { 0x872100, 0x10 },
    { 0x8726B0, 0x4682 }, { 0x876D38, 0x272 }, { 0x876FB0, 0x1E78 }, { 0x878E30, 0xE2 }, { 0x878F18, 0x5DC0 },
    { 0x87F7B8, 0x562 }, { 0x87FD20, 0x124 }, { 0x87FF70, 0x32 }, { 0x87FFA8, 0x93DAA }, { 0x913E00, 0x1146 },
    { 0x914F58, 0x2DEF30 }, { 0xBF3E90, 0x9E70 }, { 0xC1BAB0, 0x152 }, { 0xC1BD08, 0x1DD0 },
    { 0xC1FAA8, 0x34 }, { 0xC1FB0C, 0x2 }, { 0xC1FB10, 0xA }, { 0xC1FB28, 0x90 }, { 0xC1FC08, 0x15E },
    { 0xC1FD70, 0x113A }, { 0xC20EB0, 0x1A4 }, { 0xC21060, 0xA }, { 0xC21B70, 0x294 }, { 0xC22198, 0x1A4 },
    { 0xC22340, 0x1A4 }, { 0xC9DCB8, 0x348 }, { 0xC9E030, 0xE1A }, { 0xC9EE50, 0x2D2 }, { 0xC9F138, 0xE1A },
    { 0xCA2280, 0x1B60 }, { 0xCA43D0, 0x154 }, { 0xCA96C8, 0x3844 }, { 0xCACF10, 0x7A }, { 0xCAD2C0, 0x288 },
};

#define STATE_UNIT 256              // compared in units of this size
#define STATE_FRAME 60000           // payload per FRAME_STATE

static BYTE *g_shadow[MAX_COMPUTERS + 1];
static BOOL g_shadowValid[MAX_COMPUTERS + 1];
static CRITICAL_SECTION g_stateLock;
static DWORD g_stateTotal;

static void FlushState(Connection *c, BYTE *frame, DWORD *used)
{
    if (*used) SendFrame(c, FRAME_STATE, frame, *used, NULL, 0);
    *used = 0;
}

static void SyncState(DWORD to)
{
    Connection *c;
    BYTE *frame, *shadow;
    DWORD used = 0, sent = 0, offset = 0;
    int b;
    if (to < 2 || to > MAX_COMPUTERS) return;
    c = &g_conn[to];
    if (c->s == INVALID_SOCKET) return;
    EnterCriticalSection(&g_stateLock);
    if (!g_shadow[to]) g_shadow[to] = (BYTE *)Alloc(g_stateTotal);
    frame = (BYTE *)Alloc(STATE_FRAME + 8 + STATE_UNIT);
    shadow = g_shadow[to];
    if (shadow && frame)
    {
        for (b = 0; b < ARRAYSIZE(kStateBlocks); b++)
        {
            const BYTE *live = (const BYTE *)(kStateBlocks[b][0] + g_b->data);
            DWORD size = kStateBlocks[b][1], at = 0;
            while (at < size)
            {
                DWORD n = size - at < STATE_UNIT ? size - at : STATE_UNIT, k;
                BOOL differs = !g_shadowValid[to];
                for (k = 0; !differs && k < n; k++) differs = live[at + k] != shadow[offset + at + k];
                if (differs)
                {
                    if (used + 8 + n > STATE_FRAME) FlushState(c, frame, &used);
                    *(DWORD *)(frame + used) = kStateBlocks[b][0] + at;
                    *(DWORD *)(frame + used + 4) = n;
                    memcpy(frame + used + 8, live + at, n);
                    memcpy(shadow + offset + at, frame + used + 8, n);
                    used += 8 + n;
                    sent += n;
                }
                at += n;
            }
            offset += size;
        }
        FlushState(c, frame, &used);
        g_shadowValid[to] = TRUE;
        if (sent) Log("Abgleich an Rechner %lu: %lu Byte", to, sent);
    }
    Free(frame);
    LeaveCriticalSection(&g_stateLock);
}

static BOOL InState(DWORD addr, DWORD n)
{
    int b;
    for (b = 0; b < ARRAYSIZE(kStateBlocks); b++)
        if (addr >= kStateBlocks[b][0] && addr + n <= kStateBlocks[b][0] + kStateBlocks[b][1]) return TRUE;
    return FALSE;
}

static void ApplyState(const BYTE *p, DWORD n)
{
    while (n >= 8)
    {
        DWORD addr = ((const DWORD *)p)[0], len = ((const DWORD *)p)[1];
        if (len > n - 8 || !InState(addr, len)) { Log("Abgleich: ungueltiger Bereich 0x%lX", addr); return; }
        memcpy((void *)(addr + g_b->data), p + 8, len);
        p += 8 + len;
        n -= 8 + len;
    }
}

// ---- the fake DirectPlay object ---------------------------------------------------------------------

typedef struct { void **vtbl; } FakeDirectPlay;

static HRESULT WINAPI DpNotImpl(FakeDirectPlay *self) { (void)self; return E_NOTIMPL_DP; }
static ULONG WINAPI DpAddRef(FakeDirectPlay *self) { (void)self; return 1; }
static ULONG WINAPI DpRelease(FakeDirectPlay *self) { (void)self; return 1; }
static HRESULT WINAPI DpClose(FakeDirectPlay *self) { (void)self; return DP_OK; }

static HRESULT WINAPI DpReceive(FakeDirectPlay *self, DWORD *from, DWORD *to, DWORD flags, void *buffer, DWORD *size)
{
    Incoming *m;
    (void)self; (void)flags;
    EnterCriticalSection(&g_inLock);
    m = g_inHead;
    if (m && m->size > *size)
    {
        *size = m->size;
        LeaveCriticalSection(&g_inLock);
        return DPERR_BUFFERTOOSMALL;
    }
    if (m)
    {
        g_inHead = m->next;
        if (!g_inHead) g_inTail = NULL;
    }
    LeaveCriticalSection(&g_inLock);
    if (!m) return DPERR_NOMESSAGES;
    *from = m->from;
    *to = g_myId;
    *size = m->size;
    memcpy(buffer, m->data, m->size);
    if (m->from != 0 && m->size >= PACKET_HEADER + 8)
    {
        if (*(WORD *)(m->data + PACKET_HEADER + 4) == 0x66 && *(WORD *)(m->data + PACKET_HEADER + 6) == 0x834)
            InterlockedExchange(&g_stateReceived, 1);   // the dispatcher copies it to 0x826620 right after
        LogMessage("ein  von", m->from, m->data + PACKET_HEADER, m->size - PACKET_HEADER);
    }
    Free(m);
    return DP_OK;
}

// DirectPlay 1 vtable: 0 QueryInterface, 1 AddRef, 2 Release, 4 Close, 21 Receive; the rest is unused.
static void *g_dpVtbl[32];
static FakeDirectPlay g_dp = { g_dpVtbl };

static void SetUpDirectPlay(void)
{
    int i;
    for (i = 0; i < ARRAYSIZE(g_dpVtbl); i++) g_dpVtbl[i] = (void *)DpNotImpl;
    g_dpVtbl[1] = (void *)DpAddRef;
    g_dpVtbl[2] = (void *)DpRelease;
    g_dpVtbl[4] = (void *)DpClose;
    g_dpVtbl[21] = (void *)DpReceive;
    GLOBAL(void *, D_DPLAY) = &g_dp;
}

// ---- hooks ------------------------------------------------------------------------------------------

// Replaces FUN_005ebf70 (enqueue a slot for the send thread the game no longer has): sends the slot as
// one packet, frees its data and the slot.
static void __cdecl NetEnqueue(BYTE *slot)
{
    DWORD to = SLOT_TO(slot), size = SLOT_SIZE(slot);
    BYTE *data = SLOT_DATA(slot);
    BYTE head[PACKET_HEADER];
    ZeroMemory(head, sizeof head);
    head[0] = 0x80;                                 // piece 0, the last
    head[1] = 0;                                    // highest piece index
    *(DWORD *)(head + 4) = to;
    *(SHORT *)(head + 8) = 1;                       // renumbered on arrival
    *(DWORD *)(head + 12) = size;
    if (size >= 8) LogMessage("aus  an", to, data, size);
    if (IS_HOST && to >= 2 && size >= 8 && (*(WORD *)(data + 4) == 0x6A
        || (*(WORD *)(data + 4) == 0x65 && *(WORD *)(data + 6) == 0x200)))
        SyncState(to);
    if (to == g_myId)
    {
        BYTE *whole = (BYTE *)Alloc(PACKET_HEADER + size);
        if (whole)
        {
            memcpy(whole, head, PACKET_HEADER);
            memcpy(whole + PACKET_HEADER, data, size);
            Deliver(g_myId, whole, PACKET_HEADER + size);
            Free(whole);
        }
    }
    else
    {
        Connection *c = Route(to);
        DWORD ids[2];
        ids[0] = g_myId;
        ids[1] = to;
        if (!c) { if (g_verbose) Log("Paket an Rechner %lu verworfen: keine Verbindung", to); }
        else
        {
            BYTE *frame = (BYTE *)Alloc(PACKET_HEADER + size);
            if (frame)
            {
                memcpy(frame, head, PACKET_HEADER);
                memcpy(frame + PACKET_HEADER, data, size);
                if (!SendFrame(c, FRAME_GAME, ids, sizeof ids, frame, PACKET_HEADER + size))
                    Log("Senden an Rechner %lu fehlgeschlagen", to);
                Free(frame);
            }
        }
    }
    ((FreeFn)G_FREE)(data);
    SLOT_DATA(slot) = NULL;
    *(WORD *)slot = 0;
}

// While the game state goes to a newly connected client, the broadcasts of the distribution thread go to
// that client only; the others have it already.
static volatile DWORD g_distThread, g_distTo;

// Replaces FUN_005d3c00 (send a message to every other computer listed in the managers' table): sends it
// to every connected computer that has the game state, so a client without a manager yet gets it too.
static void __cdecl NetBroadcast(const void *message, DWORD size)
{
    DWORD id;
    if (g_distThread && GetCurrentThreadId() == g_distThread)
    {
        ((SendFn)G_SEND)(g_myId, g_distTo, 1, message, size);
        return;
    }
    if (IS_HOST)
    {
        for (id = 2; id <= MAX_COMPUTERS; id++)
            if (g_conn[id].s != INVALID_SOCKET && g_conn[id].id == id && g_conn[id].stateSent)
                ((SendFn)G_SEND)(g_myId, id, 1, message, size);
    }
    else if (IS_CLIENT && g_conn[1].s != INVALID_SOCKET)
        ((SendFn)G_SEND)(g_myId, 1, 1, message, size);
}

static DWORD g_netThreadId;

// Replaces FUN_005ebfb0 (wait until the send thread emptied the list). A request to the server
// (FUN_005d2e70) sends, calls this and then loops forever ("jmp $") if its slot still waits for the
// reply: in the game the reply had to come in while the send thread was busy. Here the list is always
// empty, so this waits for the replies of all open requests instead (the network thread brings them in),
// at most 30 s; then they are given up so the game doesn't hang.
static int __cdecl NetWaitSent(void)
{
    DWORD start = GetTickCount();
    if (GetCurrentThreadId() == g_netThreadId) return 1;    // the replies come through this thread
    for (;;)
    {
        int i, open = 0;
        for (i = 0; i < 20; i++)
            if (((volatile SHORT *)D_REQ_USED)[i] && *(volatile SHORT *)(D_REQUESTS + i * 0x16C + 0x14) == 1) open++;
        if (!open) return 1;
        if (GetTickCount() - start > 30000)
        {
            Log("Keine Antwort vom Server auf %d Anfrage(n), aufgegeben", open);
            for (i = 0; i < 20; i++) *(volatile SHORT *)(D_REQUESTS + i * 0x16C + 0x14) = 0;
            return 1;
        }
        Sleep(2);
    }
}

// Replaces FUN_005d3fb0 ("network game?", always 0 in the shipped game). With it the server sends the
// collected globals (0x66/0x834) and further blocks in the course of the day.
static int __cdecl NetIsActive(void)
{
    if (!g_ready) return 0;
    if (IS_HOST) return ConnectedClients() > 0;
    return IS_CLIENT && g_joined;
}

// Replaces the call of "load a game" (menu "Altes Spiel fortsetzen") in the game's logic thread: a client
// that has the host's game state goes straight into the game's main loop, which waits for the server.
static int __cdecl NetLoadOrJoin(int arg)
{
    if (IS_CLIENT && g_stateReceived)
    {
        InterlockedExchange(&g_joined, 1);
        if (g_mainWnd) SetWindowTextA(g_mainWnd, "ANSTOSS 2");
        Log("Client tritt dem Spiel des Hosts bei");
        return 1;
    }
    return ((IntFn)g_loadGame)(arg);
}

// When the server takes a waiting manager over (FUN_005d5730) it enters him in the managers' table with
// computer 1, wherever he plays. a: table index, first name, last name, manager, computer, number. The
// first name points into the waiting list; its entry holds the computer.
static void __cdecl FixJoinArgs(DWORD *a)
{
    DWORD first = a[1], base = D_JOIN_NAMES;
    if (first >= base && first < base + 4 * 0x24 && (first - base) % 0x24 == 0)
    {
        DWORD computer = *(DWORD *)(D_JOIN_COMPUTER + (first - base));
        if (computer > 1) Log("Manager %lu aufgenommen, spielt auf Rechner %lu", a[3], computer);
        a[4] = computer;
    }
}

// The task code (FUN_005d2bc0, receive dedup) finds a computer's slot through the entry's +0x50, which the
// game always sets to 0. It must be the computer's position in the list FUN_005de550 builds: the distinct
// computers (+0x4C) of the used entries (+0x48 != 0) in table order.
static void __cdecl RenumberComputers(void)
{
    DWORD seen[4];
    int count = 0, i, k;
    for (i = 0; i < 4; i++)
    {
        BYTE *e = (BYTE *)D_TABLE + 4 + i * 0x54;
        DWORD computer = *(DWORD *)(e + 0x4C);
        if (*(DWORD *)(e + 0x48) == 0) continue;
        for (k = 0; k < count && seen[k] != computer; k++) {}
        if (k == count) seen[count++] = computer;
        *(SHORT *)(e + 0x50) = (SHORT)k;
    }
}

// In place of the call FUN_005d5730 makes to enter the manager (thiscall, six arguments, ret 0x18): fixes
// the computer, calls the original with the same arguments, then renumbers the computers.
static __declspec(naked) void JoinThunk(void)
{
    __asm {
        push ecx
        lea eax, [esp + 8]
        push eax
        call FixJoinArgs
        add esp, 4
        pop ecx
        push dword ptr [esp + 0x18]
        push dword ptr [esp + 0x18]
        push dword ptr [esp + 0x18]
        push dword ptr [esp + 0x18]
        push dword ptr [esp + 0x18]
        push dword ptr [esp + 0x18]
        call dword ptr [g_tableAdd]
        call RenumberComputers
        ret 0x18
    }
}

// FUN_00590a00 shows a manager's event: a task (place in the table, kind 1) for the computer he plays on,
// and for every other computer one without place, kind 2, before and after. The event's screens take
// the manager from the place, so on another computer they look up manager 0 and end the game
// ("GetMitspieler 0", abnormal program termination) or stay empty. The game never had a second computer
// and never got there; here those tasks are left out, the others see nothing of the event.
static __declspec(naked) void EventTaskThunk(void)
{
    __asm {
        cmp dword ptr [esp + 8], 0              // place in the table
        jne original
        cmp dword ptr [esp + 0x10], 2           // kind
        jne original
        ret 0x18
    original:
        jmp dword ptr [g_taskAdd]
    }
}

static void SendJoin(void)
{
    BYTE p[28];
    ZeroMemory(p, sizeof p);
    lstrcpynA((char *)p, g_firstName, 10);
    lstrcpynA((char *)p + 10, g_lastName, 16);
    *(WORD *)(p + 26) = (WORD)g_difficulty;
    if (SendFrame(&g_conn[1], FRAME_JOIN, p, sizeof p, NULL, 0))
        Log("Manager %s %s beim Host angemeldet", g_firstName, g_lastName);
}

// ---- choosing how to play ---------------------------------------------------------------------------

#define ID_OFF    101
#define ID_HOST   102
#define ID_JOIN   103
#define ID_ADDR   104
#define ID_FIRST  105
#define ID_LAST   106
#define ID_START  107

static volatile LONG g_dialogDone;
static int g_dialogMode;

static void EnableJoinFields(HWND w)
{
    BOOL join = IsDlgButtonChecked(w, ID_JOIN) == BST_CHECKED;
    EnableWindow(GetDlgItem(w, ID_ADDR), join);
    EnableWindow(GetDlgItem(w, ID_FIRST), join);
    EnableWindow(GetDlgItem(w, ID_LAST), join);
}

static LRESULT CALLBACK ChoiceProc(HWND w, UINT msg, WPARAM wp, LPARAM lp)
{
    if (msg == WM_COMMAND)
    {
        int id = LOWORD(wp);
        if (id == ID_OFF || id == ID_HOST || id == ID_JOIN) EnableJoinFields(w);
        else if (id == ID_START || id == IDOK)
        {
            char addr[128], first[10], last[16];
            int mode = IsDlgButtonChecked(w, ID_HOST) == BST_CHECKED ? MODE_HOST
                     : IsDlgButtonChecked(w, ID_JOIN) == BST_CHECKED ? MODE_CLIENT : MODE_OFF;
            GetDlgItemTextA(w, ID_ADDR, addr, sizeof addr);
            GetDlgItemTextA(w, ID_FIRST, first, sizeof first);
            GetDlgItemTextA(w, ID_LAST, last, sizeof last);
            if (mode == MODE_CLIENT && (!addr[0] || !first[0] || !last[0])) { MessageBeep(MB_ICONWARNING); return 0; }
            lstrcpynA(g_hostName, addr, sizeof g_hostName);
            lstrcpynA(g_firstName, first, sizeof g_firstName);
            lstrcpynA(g_lastName, last, sizeof g_lastName);
            g_dialogMode = mode;
            DestroyWindow(w);
        }
        return 0;
    }
    if (msg == WM_CLOSE) { g_dialogMode = MODE_OFF; DestroyWindow(w); return 0; }
    if (msg == WM_DESTROY) { InterlockedExchange(&g_dialogDone, 1); return 0; }
    return DefWindowProcA(w, msg, wp, lp);
}

static HWND Control(HWND parent, const char *cls, const char *text, DWORD style, int x, int y, int cx, int cy, int id, HFONT font)
{
    HWND c = CreateWindowExA(lstrcmpA(cls, "EDIT") ? 0 : WS_EX_CLIENTEDGE, cls, text, WS_CHILD | WS_VISIBLE | style,
                             x, y, cx, cy, parent, (HMENU)(INT_PTR)id, NULL, NULL);
    SendMessageA(c, WM_SETFONT, (WPARAM)font, FALSE);
    return c;
}

// Shown over the main menu the first time it opens. Returns the chosen mode and keeps it in a2gnet.ini.
static int ChooseMode(void)
{
    static const char cls[] = "A2GNetChoice";
    WNDCLASSA wc;
    HWND w;
    RECT r;
    MSG m;
    typedef HGDIOBJ (WINAPI *StockFn)(int);     // gdi32 at run time, so the proxy has no new import
    StockFn stock = (StockFn)GetProcAddress(LoadLibraryA("gdi32.dll"), "GetStockObject");
    HFONT font = stock ? (HFONT)stock(DEFAULT_GUI_FONT) : NULL;
    char mode[16];
    int cx = 330, cy = 240, x = 200, y = 150, start;
    ZeroMemory(&wc, sizeof wc);
    wc.lpfnWndProc = ChoiceProc;
    wc.hInstance = GetModuleHandleA(NULL);
    wc.hCursor = LoadCursorA(NULL, IDC_ARROW);
    wc.hbrBackground = (HBRUSH)(COLOR_BTNFACE + 1);
    wc.lpszClassName = cls;
    RegisterClassA(&wc);
    if (g_mainWnd && GetWindowRect(g_mainWnd, &r)) { x = (r.left + r.right - cx) / 2; y = (r.top + r.bottom - cy) / 2; }
    w = CreateWindowExA(WS_EX_TOPMOST | WS_EX_DLGMODALFRAME, cls, "ANSTOSS 2 - Netzwerkspiel",
                        WS_POPUP | WS_CAPTION | WS_SYSMENU, x, y, cx, cy, NULL, NULL, wc.hInstance, NULL);
    if (!w) return MODE_OFF;
    Control(w, "BUTTON", "Ohne Netzwerk spielen", BS_AUTORADIOBUTTON | WS_GROUP | WS_TABSTOP, 16, 12, 290, 20, ID_OFF, font);
    Control(w, "BUTTON", "Netzwerkspiel er\366ffnen", BS_AUTORADIOBUTTON, 16, 34, 290, 20, ID_HOST, font);
    Control(w, "BUTTON", "Netzwerkspiel beitreten", BS_AUTORADIOBUTTON, 16, 56, 290, 20, ID_JOIN, font);
    Control(w, "STATIC", "Adresse des Hosts", 0, 34, 86, 110, 18, 0, font);
    Control(w, "EDIT", g_hostName, ES_AUTOHSCROLL | WS_TABSTOP | WS_GROUP, 150, 83, 156, 22, ID_ADDR, font);
    Control(w, "STATIC", "Vorname", 0, 34, 114, 110, 18, 0, font);
    Control(w, "EDIT", g_firstName, ES_AUTOHSCROLL | WS_TABSTOP, 150, 111, 156, 22, ID_FIRST, font);
    Control(w, "STATIC", "Nachname", 0, 34, 142, 110, 18, 0, font);
    Control(w, "EDIT", g_lastName, ES_AUTOHSCROLL | WS_TABSTOP, 150, 139, 156, 22, ID_LAST, font);
    Control(w, "BUTTON", "Weiter", BS_DEFPUSHBUTTON | WS_TABSTOP | WS_GROUP, 206, 176, 100, 26, ID_START, font);
    SendDlgItemMessageA(w, ID_ADDR, EM_LIMITTEXT, sizeof g_hostName - 1, 0);
    SendDlgItemMessageA(w, ID_FIRST, EM_LIMITTEXT, sizeof g_firstName - 1, 0);
    SendDlgItemMessageA(w, ID_LAST, EM_LIMITTEXT, sizeof g_lastName - 1, 0);
    GetPrivateProfileStringA("A2GNet", "Mode", "off", mode, sizeof mode, g_ini);
    start = ModeFromText(mode);
    CheckRadioButton(w, ID_OFF, ID_JOIN, start == MODE_HOST ? ID_HOST : start == MODE_CLIENT ? ID_JOIN : ID_OFF);
    EnableJoinFields(w);
    ShowWindow(w, SW_SHOW);
    SetForegroundWindow(w);
    g_dialogMode = MODE_OFF;
    g_dialogDone = 0;
    while (!g_dialogDone && GetMessageA(&m, NULL, 0, 0) > 0)
    {
        if (IsDialogMessageA(w, &m)) continue;
        TranslateMessage(&m);
        DispatchMessageA(&m);
    }
    SaveSettings(g_dialogMode);
    return g_dialogMode;
}

// Replaces the call of FUN_00644070 in the game's logic thread, which waits until the main menu was answered
// (struct +2 the choice, +4 answered). The first time the choice of network play comes first. A client
// answers "Altes Spiel fortsetzen" itself once the host's game state is there; if the player answers
// before that, the network is left and the game goes on alone.
static void __cdecl NetMenuWait(BYTE *answer)
{
    static BOOL asked;
    if (!asked)
    {
        asked = TRUE;
        if (g_mode == MODE_UNSET) InterlockedExchange(&g_mode, ChooseMode());
        Log("Spielweise: %s", IS_HOST ? "Netzwerkspiel eroeffnen" : IS_CLIENT ? "Netzwerkspiel beitreten" : "ohne Netzwerk");
    }
    if (IS_CLIENT && !g_joined)
    {
        while (!*(volatile SHORT *)(answer + 4) && !GLOBAL(SHORT, D_SHUTDOWN))
        {
            if (g_stateReceived)
            {
                *(SHORT *)(answer + 2) = 1;             // "Altes Spiel fortsetzen", taken by NetLoadOrJoin
                *(volatile SHORT *)(answer + 4) = 1;
                break;
            }
            if (g_mainWnd) SetWindowTextA(g_mainWnd, g_conn[1].s != INVALID_SOCKET ? "ANSTOSS 2 - Verbunden, warte auf das Spiel des Hosts"
                                                                                  : "ANSTOSS 2 - Suche den Host");
            Sleep(250);
        }
        if (!g_stateReceived)
        {
            Log("Ohne den Host weitergespielt, Netzwerk aus");
            InterlockedExchange(&g_mode, MODE_OFF);
        }
    }
    ((PtrFn)g_menuWait)(answer);
}

// ---- the network thread -----------------------------------------------------------------------------

static volatile LONG g_distributing;

// The distribution (0x4280E0) is a status task for every computer (FUN_00590960(5,2,1)), six calls that
// send the blocks, and the task's end (5,2,0). The task posts a screen refresh to the main window; with a
// screen open that overruns the game's six working copies of teams (FUN_00584ad0 returns NULL). So only
// the six calls run here, on a thread of their own (the game's parts post work to the main window and
// wait for it). Their targets are read from the call instructions at +0x0E, +0x13 ... +0x27.
static DWORD WINAPI DistributeThread(LPVOID param)
{
    DWORD to = (DWORD)(INT_PTR)param;
    int i;
    g_distTo = to;
    g_distThread = GetCurrentThreadId();
    Log("Spielstand geht an Rechner %lu", to);
    for (i = 0; i < 6; i++)
    {
        DWORD at = G_DISTRIBUTE + 0x0E + 5 * i;
        if (*(const BYTE *)at != 0xE8) { Log("Verteilen: Aufruf %d nicht gefunden", i); break; }
        ((VoidFn)CallTarget(at))();
    }
    if (g_shadow[to]) g_shadowValid[to] = FALSE;         // a client coming back gets everything again
    SyncState(to);
    ((BulkFn)g_b->bulk)(0x66, 0x834, (const void *)D_GLOBALS, GLOBALS_SIZE, 0);   // marks the state complete
    g_distThread = 0;
    if (g_conn[to].s != INVALID_SOCKET) g_conn[to].stateSent = TRUE;
    Log("Spielstand an Rechner %lu gesendet", to);
    InterlockedExchange(&g_distributing, 0);
    return 0;
}

// Open tasks for a computer that is no longer connected count as done, so the server doesn't wait for
// them. Checked all the time: the game makes new ones for it while its managers leave (e.g. the
// message that a manager left goes to every computer in the table, the leaving one too).
static void FinishTasksOfGoneComputers(void)
{
    int i, n = GLOBAL(SHORT, D_TASKS), done = 0;
    for (i = 0; i < n && i < 10; i++)
    {
        BYTE *t = (BYTE *)D_TASKS + 4 + i * 0x20;
        DWORD id = *(DWORD *)(t + 0x0C);
        if (id >= 2 && id <= MAX_COMPUTERS && *(SHORT *)(t + 0x1C) != 3 && g_conn[id].s == INVALID_SOCKET)
        {
            *(DWORD *)(t + 0x14) = 1;
            *(SHORT *)(t + 0x1C) = 3;
            done++;
        }
    }
    if (done) SetEvent(GLOBAL(HANDLE, D_RECV_EVENT));
}

// A client is gone: its managers leave the game at the next point the main loop takes leavers over
// (FUN_005d57f0), and its open tasks count as done so the server doesn't wait for them.
static void ClientLeft(DWORD id)
{
    int i;
    for (i = 0; i < 4; i++)
    {
        BYTE *e = (BYTE *)D_TABLE + 4 + i * 0x54;
        DWORD manager = *(DWORD *)(e + 0x48);
        if (manager && *(DWORD *)(e + 0x4C) == id)
        {
            ((LeaveFn)g_b->leave)((int)manager, 0);
            Log("Manager %lu verlaesst das Spiel", manager);
        }
    }
    FinishTasksOfGoneComputers();
}

static void CloseConnection(Connection *c, const char *why)
{
    DWORD id = c->id;
    if (c->s == INVALID_SOCKET) return;
    Log("Verbindung zu Rechner %lu (%s) beendet: %s", id, c->name, why);
    EnterCriticalSection(&g_sendLock);
    ws.close(c->s);
    c->s = INVALID_SOCKET;
    LeaveCriticalSection(&g_sendLock);
    c->have = 0;
    c->id = 0;
    c->stateSent = FALSE;
    if (IS_HOST && id >= 2) ClientLeft(id);
    if (IS_CLIENT && g_stateReceived)
    {
        // The game can't go on without the server it plays with.
        MessageBoxA(NULL, "Die Verbindung zum Host ist abgebrochen. ANSTOSS 2 wird beendet.", "ANSTOSS 2 - Netzwerkspiel",
                    MB_OK | MB_ICONERROR | MB_TOPMOST | MB_SETFOREGROUND);
        ExitProcess(1);
    }
}

static void HandleFrame(Connection *c, BYTE type, const BYTE *p, DWORD n)
{
    if (type == FRAME_HELLO && IS_HOST && n >= 1)
    {
        DWORD id = (DWORD)(c - g_conn);
        int i;
        for (i = 0; i < 31 && i < (int)n && p[i]; i++) c->name[i] = p[i];
        c->name[i] = 0;
        c->id = id;
        SendFrame(c, FRAME_WELCOME, &id, 4, NULL, 0);
        Log("Client \"%s\" ist Rechner %lu", c->name, id);
        AnnouncePlayer(id, c->name);                // the game answers with the server's ID (0x65/0x1FA)
    }
    else if (type == FRAME_WELCOME && IS_CLIENT && n >= 4)
    {
        g_myId = *(const DWORD *)p;
        c->id = 1;
        GLOBAL(DWORD, D_MY_ID) = g_myId;
        GLOBAL(DWORD, D_SERVER_ID) = 1;
        Log("Beim Host angemeldet als Rechner %lu", g_myId);
    }
    else if (type == FRAME_JOIN && IS_HOST && n >= 28 && c->id)
    {
        char first[11], last[17];
        int i;
        SHORT managers = GLOBAL(SHORT, D_MANAGERS), waiting = GLOBAL(SHORT, D_JOIN_NAMES - 0x1C);
        for (i = 0; i < 10; i++) first[i] = p[i];
        first[10] = 0;
        for (i = 0; i < 16; i++) last[i] = p[10 + i];
        last[16] = 0;
        if (managers + waiting >= 4) Log("Manager %s %s von Rechner %lu abgewiesen: schon 4 Manager", first, last, c->id);
        else
        {
            ((AddJoinerFn)g_b->addJoiner)(first, last, *(const WORD *)(p + 26), c->id);
            Log("Manager %s %s von Rechner %lu wartet auf Aufnahme", first, last, c->id);
        }
    }
    else if (type == FRAME_STATE && IS_CLIENT)
    {
        if (g_inHead) ((VoidFn)G_PUMP)();           // messages that came first are handled first
        ApplyState(p, n);
    }
    else if (type == FRAME_GAME && n >= 8 + PACKET_HEADER)
    {
        DWORD from = ((const DWORD *)p)[0], to = ((const DWORD *)p)[1];
        if (IS_HOST && from != c->id)
        {
            Log("Paket mit falschem Absender %lu von Rechner %lu verworfen", from, c->id);
            return;
        }
        if (IS_HOST && to != g_myId)
        {
            Connection *t = Route(to);              // client to client: through the host
            if (t) SendFrame(t, FRAME_GAME, p, n, NULL, 0);
            else Log("Weiterleiten an Rechner %lu nicht moeglich", to);
            return;
        }
        Deliver(from, p + 8, n - 8);
    }
}

// Takes whole frames out of the connection's buffer; FALSE if the connection broke the format.
static BOOL ReadFrames(Connection *c)
{
    for (;;)
    {
        DWORD len;
        if (c->have < 5) return TRUE;
        memcpy(&len, c->buf, 4);
        if (len == 0 || len > FRAME_MAX) return FALSE;
        if (c->have < 4 + len) return TRUE;
        HandleFrame(c, c->buf[4], c->buf + 5, len - 1);
        if (c->s == INVALID_SOCKET) return TRUE;
        c->have -= 4 + len;
        memcpy(c->buf, c->buf + 4 + len, c->have);   // forward copy, overlapping is fine
    }
}

static void Receive(Connection *c)
{
    int k = ws.recv(c->s, (char *)c->buf + c->have, FRAME_MAX + 8 - c->have, 0);
    if (k <= 0) { CloseConnection(c, k == 0 ? "getrennt" : "Fehler"); return; }
    c->have += k;
    if (!ReadFrames(c)) CloseConnection(c, "ungueltige Daten");
}

static BOOL ResolveHost(struct sockaddr_in *a)
{
    ZeroMemory(a, sizeof *a);
    a->sin_family = AF_INET;
    a->sin_port = ws.htons(g_port);
    a->sin_addr.s_addr = ws.inet_addr(g_hostName);
    if (a->sin_addr.s_addr == INADDR_NONE)
    {
        struct hostent *h = ws.gethostbyname(g_hostName);
        if (!h || !h->h_addr_list[0]) return FALSE;
        memcpy(&a->sin_addr, h->h_addr_list[0], 4);
    }
    return TRUE;
}

static BOOL WaitForGame(void)
{
    // The main window exists once the game's InitInstance ran; the game's heap is up by then.
    int i;
    for (i = 0; i < 1200; i++)
    {
        DWORD app = GLOBAL(DWORD, D_APP_WND);
        if (app && !IsBadReadPtr((void *)(app + 0x1C), 4) && IsWindow(*(HWND *)(app + 0x1C)))
        {
            g_mainWnd = *(HWND *)(app + 0x1C);
            return TRUE;
        }
        Sleep(100);
    }
    return FALSE;
}

static SOCKET Listen(void)
{
    struct sockaddr_in a;
    BOOL yes = TRUE;
    SOCKET l = ws.socket(AF_INET, SOCK_STREAM, IPPROTO_TCP);
    ws.setsockopt(l, SOL_SOCKET, SO_REUSEADDR, (const char *)&yes, sizeof yes);
    ZeroMemory(&a, sizeof a);
    a.sin_family = AF_INET;
    a.sin_port = ws.htons(g_port);
    if (ws.bind(l, (struct sockaddr *)&a, sizeof a) != 0 || ws.listen(l, 4) != 0)
    {
        Log("Kann nicht auf TCP-Port %u lauschen (Fehler %d), Netzwerk aus", g_port, ws.lastError());
        MessageBoxA(NULL, "Das Netzwerkspiel kann nicht er\366ffnet werden: Der Port ist belegt oder gesperrt.",
                    "ANSTOSS 2 - Netzwerkspiel", MB_OK | MB_ICONWARNING | MB_TOPMOST);
        ws.close(l);
        return INVALID_SOCKET;
    }
    Log("Host wartet auf TCP-Port %u", g_port);
    return l;
}

static void TryConnect(void)
{
    struct sockaddr_in a;
    SOCKET s;
    BOOL yes = TRUE;
    char name[32];
    if (!ResolveHost(&a)) { Log("Host \"%s\" nicht gefunden", g_hostName); return; }
    s = ws.socket(AF_INET, SOCK_STREAM, IPPROTO_TCP);
    ws.setsockopt(s, IPPROTO_TCP, TCP_NODELAY, (const char *)&yes, sizeof yes);
    if (ws.connect(s, (struct sockaddr *)&a, sizeof a) != 0)
    {
        Log("Verbindung zu %s:%u fehlgeschlagen (Fehler %d)", g_hostName, g_port, ws.lastError());
        ws.close(s);
        return;
    }
    g_conn[1].s = s;
    g_conn[1].have = 0;
    lstrcpynA(g_conn[1].name, g_hostName, sizeof g_conn[1].name);
    Log("Verbunden mit %s:%u", g_hostName, g_port);
    wsprintfA(name, "%s %s", g_firstName, g_lastName);
    SendFrame(&g_conn[1], FRAME_HELLO, name, lstrlenA(name) + 1, NULL, 0);
}

static void Accept(SOCKET listener)
{
    SOCKET s = ws.accept(listener, NULL, NULL);
    int slot = 0, i;
    BOOL yes = TRUE;
    if (s == INVALID_SOCKET) return;
    for (i = 2; i <= MAX_COMPUTERS; i++) if (g_conn[i].s == INVALID_SOCKET) { slot = i; break; }
    if (!slot) { Log("Neue Verbindung abgewiesen: alle Plaetze belegt"); ws.close(s); return; }
    ws.setsockopt(s, IPPROTO_TCP, TCP_NODELAY, (const char *)&yes, sizeof yes);
    g_conn[slot].s = s;
    g_conn[slot].have = 0;
    g_conn[slot].id = 0;
    g_conn[slot].name[0] = 0;
    g_conn[slot].stateSent = FALSE;
    Log("Neue Verbindung, Platz %d", slot);
}

// Host: a client that announced itself gets the game state once a game is loaded, one client at a time.
static void DistributeToNewClients(void)
{
    int id;
    if (g_distributing || GLOBAL(SHORT, D_MANAGERS) <= 0) return;
    for (id = 2; id <= MAX_COMPUTERS; id++)
        if (g_conn[id].s != INVALID_SOCKET && g_conn[id].id == (DWORD)id && !g_conn[id].stateSent)
        {
            HANDLE t;
            InterlockedExchange(&g_distributing, 1);
            t = CreateThread(NULL, 0, DistributeThread, (LPVOID)(INT_PTR)id, 0, NULL);
            if (t) CloseHandle(t); else InterlockedExchange(&g_distributing, 0);
            return;
        }
}

static DWORD WINAPI NetThread(LPVOID param)
{
    SOCKET listener = INVALID_SOCKET;
    DWORD lastTry = 0;
    int i;
    (void)param;
    g_netThreadId = GetCurrentThreadId();
    for (i = 0; i <= MAX_COMPUTERS; i++) { g_conn[i].s = INVALID_SOCKET; g_conn[i].buf = (BYTE *)Alloc(FRAME_MAX + 8); }
    if (!LoadWinsock()) { Log("Winsock nicht verfuegbar, Netzwerk aus"); return 0; }
    if (!WaitForGame()) { Log("Hauptfenster des Spiels nicht gefunden, Netzwerk aus"); return 0; }

    ((VoidFn)G_NET_INIT)();
    GLOBAL(DWORD, D_PACKET_SIZE) = PACKET_SIZE;
    SetUpDirectPlay();
    InterlockedExchange(&g_ready, 1);

    while (g_mode == MODE_UNSET) Sleep(100);        // chosen over the main menu (NetMenuWait)
    if (g_mode == MODE_OFF) return 0;
    if (IS_HOST)
    {
        g_myId = 1;
        GLOBAL(DWORD, D_MY_ID) = 1;
        GLOBAL(DWORD, D_SERVER_ID) = 1;
        listener = Listen();
        if (listener == INVALID_SOCKET) { InterlockedExchange(&g_mode, MODE_OFF); return 0; }
    }

    while (g_mode != MODE_OFF)
    {
        fd_set read;
        struct timeval wait;
        if (IS_CLIENT && g_conn[1].s == INVALID_SOCKET && GetTickCount() - lastTry > 3000)
        {
            lastTry = GetTickCount();
            TryConnect();
        }
        FD_ZERO(&read);
        if (listener != INVALID_SOCKET) FD_SET(listener, &read);
        for (i = 1; i <= MAX_COMPUTERS; i++) if (g_conn[i].s != INVALID_SOCKET) FD_SET(g_conn[i].s, &read);
        wait.tv_sec = 0;
        wait.tv_usec = 200000;
        if (read.fd_count == 0) Sleep(200);
        else if (ws.select(0, &read, NULL, NULL, &wait) > 0)
        {
            if (listener != INVALID_SOCKET && IsSet(listener, &read)) Accept(listener);
            for (i = 1; i <= MAX_COMPUTERS; i++)
                if (g_conn[i].s != INVALID_SOCKET && IsSet(g_conn[i].s, &read)) Receive(&g_conn[i]);
        }

        if (IS_CLIENT && g_stateReceived && !g_joinSent && g_conn[1].s != INVALID_SOCKET)
        {
            g_joinSent = 1;
            SendJoin();
        }
        if (IS_HOST)
        {
            DistributeToNewClients();
            FinishTasksOfGoneComputers();
            if (ConnectedClients()) ((VoidFn)g_b->taskSend)();   // tasks for clients' managers (0x6A)
        }
        if (g_inHead)                               // also packets a computer sent itself
        {
            ((VoidFn)G_PUMP)();                     // Receive until empty, the dispatcher handles each
            SetEvent(GLOBAL(HANDLE, D_RECV_EVENT));
        }
    }
    for (i = 1; i <= MAX_COMPUTERS; i++) if (g_conn[i].s != INVALID_SOCKET) { ws.close(g_conn[i].s); g_conn[i].s = INVALID_SOCKET; }
    if (listener != INVALID_SOCKET) ws.close(listener);
    return 0;
}

// ---- entry ------------------------------------------------------------------------------------------

void NetEarlyInit(void)
{
    char path[MAX_PATH], *slash = NULL, *p;
    if (!GetModuleFileNameA(NULL, path, MAX_PATH)) return;
    for (p = path; *p; p++) if (*p == '\\') slash = p;
    if (!slash) return;
    slash[1] = 0;
    lstrcpynA(g_dir, path, MAX_PATH);
    lstrcpynA(g_ini, path, MAX_PATH - 12);
    lstrcatA(g_ini, "a2gnet.ini");
    if (!GetPrivateProfileIntA("A2GNet", "Enabled", 0, g_ini)) return;

    InitializeCriticalSection(&g_logLock);
    InitializeCriticalSection(&g_sendLock);
    InitializeCriticalSection(&g_inLock);
    InitializeCriticalSection(&g_stateLock);
    {
        int b;
        for (b = 0; b < ARRAYSIZE(kStateBlocks); b++) g_stateTotal += kStateBlocks[b][1];
    }
    lstrcpynA(path, g_dir, MAX_PATH - 12);
    lstrcatA(path, "a2gnet.log");
    g_log = CreateFileA(path, GENERIC_WRITE, FILE_SHARE_READ, NULL, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);
    ReadSettings();

    g_b = DetectBuild();
    if (!g_b) { Log("Unbekannte Spielfassung (bekannt: anstoss2.exe von 1999, GOG run.exe), Netzwerk aus"); return; }
    Log("A2GNet, Spielfassung: %s", g_b->name);
    if (GetPrivateProfileIntA("A2GNet", "SecondInstance", 0, g_ini))
    {
        static const BYTE nops[6] = { 0x90, 0x90, 0x90, 0x90, 0x90, 0x90 };
        WriteCode(G_MUTEX_JE, nops, sizeof nops);
        Log("Zweite Instanz erlaubt");
    }
    Jump(G_ENQUEUE, (void *)NetEnqueue);
    Jump(G_BROADCAST, (void *)NetBroadcast);
    Jump(g_b->netActive, (void *)NetIsActive);
    Jump(g_b->waitSent, (void *)NetWaitSent);
    g_loadGame = RedirectCall(g_b->loadCall, (void *)NetLoadOrJoin);
    g_tableAdd = RedirectCall(g_b->takeoverCall, (void *)JoinThunk);
    g_menuWait = RedirectCall(g_b->menuCall, (void *)NetMenuWait);
    g_taskAdd = RedirectCall(g_b->eventCall[0], (void *)EventTaskThunk);
    RedirectCall(g_b->eventCall[1], (void *)EventTaskThunk);
    {
        HANDLE t = CreateThread(NULL, 0, NetThread, NULL, 0, NULL);
        if (t) CloseHandle(t);
    }
}
