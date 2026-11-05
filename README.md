# A2G-Patch

Patches for **Anstoss 2 Gold**, switched on and off in one small window:

- **Pfeile zählen beim Gedrückthalten weiter** (bug fix): on current Windows, the game's arrow
  buttons for numeric values count one or two steps and then stop while held down. Until now the
  workaround was to run the game in the "Windows 98" compatibility mode.
- **Torgrenzen aufheben** (optional gameplay change): the match engine lets a team score only while
  it has fewer than 6 goals up to minute 45 and fewer than 10 after, and halves its chances from 7
  goals on. The patch raises the limit to 15 (the most the game can store) and drops the halving.
- **Nicht abspielbare Videos (WM, EM, Finale) überspringen statt abzustürzen** (bug fix): on current
  Windows the game crashes when it plays `WM.AVI`, `EM.AVI` or `FINALE.AVI` (e.g. after winning the
  World Cup), because their Indeo codec is gone. With the patch these videos are skipped and the game
  goes on; with an Indeo codec installed they still play.
- **Kein Einfrieren nach über 24 Tagen Windows-Laufzeit** (bug fix): a 3-second wait in a reply
  sequence turns into weeks once Windows has run for 2^31 ms (Fast Startup keeps the uptime).
- **Ton über das Standard-Audiogerät von Windows** (bug fix): the game picks the last enumerated sound
  device instead of the default one, so with several outputs (monitor, headset) the sound may go to the
  wrong one.
- **Keine falsche Warnung „Festplatte fast voll“ beim Speichern** (bug fix): the free space is computed
  in 32 bits, so on large disks the warning comes up now and then for no reason.
- **Kader nicht über 30 Spieler wachsen lassen** (bug fix): a club with players on loan can get a 31st
  player, which overwrites data behind the squad array. The patch counts the actual squad too.
- **Desktop-Auflösung auch nach einem Absturz zurückholen** (bug fix): the game switches the screen to
  640x480 in a way that outlives it; after a crash the desktop stayed at 640x480. With the patch Windows
  restores the resolution itself when the game ends. The GOG release already does this; there the switch
  is greyed out.

Works with the original release (1999), the GOG release and the 2007 CD release. Like the game, the
user interface is in German.

Project page with download (German): <https://transfairs.github.io/A2G-Patcher/>. All versions are
on the [releases page](https://github.com/transfairs/A2G-Patcher/releases).

Sister projects: [A2G-Trainer](https://github.com/transfairs/A2G-Trainer) (its *Spielmechaniken*
switch lifts the goal limits while the trainer runs; it recognizes A2G-Patch's change) and
[A2G-Scaler](https://github.com/transfairs/A2G-Scaler).

## Using it

1. Close the game.
2. Start `A2G-Patch.exe`, from anywhere. It offers its own folder and the installed CD and GOG
   releases (from the registry); *Durchsuchen …* picks another game folder.
3. Tick the patches you want, untick the ones to take back, and click *Übernehmen*.

What happens depends on the release:

| Release | Exe | How |
|---|---|---|
| Original (1999) | `anstoss2.exe` | Patched on disk. A backup `anstoss2.exe.vor-a2g-patch` is written first; once all patches are taken back, the exe is byte-identical again and the backup goes. |
| GOG | `run.exe` | Same, `run.exe.vor-a2g-patch`. A GOG Galaxy file check restores the original; then apply again. |
| 2007 CD | `run.exe` (packed) | The code only exists decrypted in memory, so `msvfw32.dll` is put into the game folder. The game loads it instead of the Windows DLL; it forwards everything to the real one and patches the code at every game start. Taking all patches back deletes it. A `msvfw32.dll` from another version of A2G-Patch is replaced on *Übernehmen*, even with the same patches ticked. |

Under *Program Files*, writing needs administrator rights; A2G-Patch then offers to restart itself
as administrator. A `msvfw32.dll` in the game folder that isn't A2G-Patch's is never touched.

Some virus scanners flag an unsigned DLL in a game folder (DLL hijacking is a malware pattern too).

## How the patches work

Each patch finds its places by the instructions around them; they must occur exactly once and hold
either the original or the patched instructions, otherwise the patch isn't offered (its switch is
greyed out). The same patterns are used on disk (`Patches.cs`) and in memory (`dll/msvfw32.c`).

**Arrow repeat.** Holding an arrow button starts a thread (`FUN_005c6130` in the original exe) that
posts one step to the window every 60 ms, but only while no step is pending (`+0xF4 == 0`). It marks
the step pending only *after* `PostMessageA`, by reading the counter again:

```
mov eax,[msg] / mov ecx,[esi+1C] / push 0 / push 0 / push eax / push ecx / call ebx   ; PostMessageA
mov eax,[esi+F4] / mov [esi+F0],60 / inc eax / mov [esi+F4],eax
```

On Windows NT the woken window thread (with its priority boost) often handles the step first; its
"decrement if pending" finds 0, then the thread writes 1. The counter stays at 1 and the thread never
posts again. The "Windows 98" layer hides this with its `DisableBoostThread` (and
`SingleProcAffinity`) shims. The patch marks the step pending before posting, in the same 41 bytes:

```
mov [esi+F4],1 / mov [esi+F0],60
mov eax,[msg] / mov ecx,[esi+1C] / push 0 / push 0 / push eax / push ecx / call ebx
nop / nop / nop / jmp next
```

The branch only runs with `+0xF4 == 0`, so writing 1 is the increment. The address of the message
variable (`[msg]`) differs between the releases and is taken over.

**Goal limits.** As in A2G-Trainer (`Controller/GoalLimitBytes.cs`):

```
limit:  setg r8 / dec r32 / and r, -4 / add r, 10   ->  setg r8 / mov r32, 15 / nop...
brakes: jle (7E)                                     ->  jmp (EB)
```

15 is the most the game can keep: results and half-time scores are stored as a nibble per team.

**Video crash.** `WM.AVI`, `EM.AVI` and `FINALE.AVI` are Indeo 3.2, which current Windows no longer
decodes. The game's own AVI player (`FUN_00418cd0` in the original exe) doesn't check
`AVIStreamGetFrameOpen` for NULL and then reads the first frame's size through a NULL pointer. The
patch adds the check and, on NULL, takes the way the function already takes when `AVIFileGetStream`
fails (release the file, return 0), so the video is skipped as if the file were missing. The room comes
from encoding the setup before the call more tightly (`edi` holds `this+0x8494`), in the same 75 bytes:

```
mov ecx,[esi+84D4] ... mov edx,[esi+84D8] ... mov word [eax+C],1 / mov word [eax+E],8 ...
mov eax,[edi] / push eax / call AVIStreamGetFrameOpen / mov [esi+848C],eax
->
mov ecx,[edi+40] ... mov ecx,[edi+44] ... mov dword [eax+C],80001 ...
push [edi] / call AVIStreamGetFrameOpen / test eax,eax / jnz ok
mov [edi],eax / jmp release            ; stream 0: playback and destructor skip it
ok: mov [edi-8],eax / nop x4
```

The pattern includes the failure path right before, so the jump back is the same in all builds; the
call's displacement is taken over. With no stream, the player takes the same way as for a missing
video file: it starts no playback thread, and the destructor skips every field that is 0. Only the
three videos above use this player; intro and extro play through `MCIWnd` and don't crash.

**Freeze, sound device, disk warning, squad limit, display mode.**

- *Freeze:* the wait stores the time difference over its own timestamp, so a later pass without a
  fresh timestamp compares against roughly `timeGetTime()`. Past 2^31 ms that is negative as a signed
  value, and `Sleep(3000 - difference)` blocks for weeks. An unsigned comparison treats it as large and
  skips the wait; 0 to 2999 ms behave as before.
- *Sound device:* the device enumeration keeps the device with the most 3D hardware buffers. Since
  Vista every device reports 0, so the last one wins. Passing NULL to `DirectSoundCreate` uses the
  Windows default device.
- *Disk warning:* the cluster count and sizes from `GetDiskFreeSpaceA` are multiplied in 32 bits,
  which yields the free space modulo 4 GB. Only the warning is skipped; saving works as before.
- *Squad limit:* the "squad full?" check counts a forecast (squad minus pending departures plus
  arrivals), and every loan counts as a departure until it ends. Late-season transfers and some loans
  can then add a 31st player. The patched check also tests the actual squad size. Callers handle "full"
  before moving anyone, so no player is left without a club.
- *Display mode:* `ChangeDisplaySettingsA(&mode, 0)` changes the mode beyond the game's lifetime;
  `CDS_FULLSCREEN` (4) ties it to the game. The overwritten store writes 0 into a field that is
  already 0.

```
freeze:  sub eax,[ebp+17C] / cmp eax,0BB8 / mov [ebp+17C],eax / jge (7D)   ->  jae (73), both places
sound:   push &guid (68 xx xx xx xx) before DirectSoundCreate                ->  push esi / nop x4 (NULL = default)
disk:    cmp eax,177000 / jae (73) before the warning                        ->  jmp (EB)
squad:   "full?" = forecast >= 30                                            ->  forecast >= 30 or squad >= 30
display: mov [esi+1C8],ebx (field already 0) before ChangeDisplaySettingsA(&mode, 0)
                                                                             ->  mov byte [esp+4],4 / nop (CDS_FULLSCREEN)
```

The sound patch rebuilds the original instruction from the patched code: the kept GUID lies 0x18
bytes before the DirectSound pointer, which the code mentions twice. The squad check keeps its call in
place, so the new code fits the function's 32 bytes.

## msvfw32.dll (`dll/`)

A proxy like A2G-Scaler's `dsound.dll`: `msvfw32` is not a KnownDLL, so the copy in the program folder
wins the loader search (checked against the running 2007 `run.exe`, whose `MSVFW32.dll` otherwise
comes from SysWOW64). `dsound.dll` would work too, but A2G-Scaler uses that name.

- All 47 exports of the real `msvfw32.dll` are `jmp` stubs (ordinals kept in `msvfw32.def`). The real
  DLL is loaded lazily on the first call, never in `DllMain` (loader lock).
- Which patches it applies is a flags byte after the marker `A2GPatchFlags` in its `.data`; A2G-Patch
  sets it when installing (1 goal limits, 2 arrow repeat, 4 video crash, 8 freeze, 16 sound device,
  32 disk warning, 64 squad limit, 128 display mode; the build has all on). An
  installed copy that differs from the embedded one in more than this byte is from another version
  and gets replaced.
- On load, a thread polls the host exe's image every 250 ms for up to two minutes until each enabled
  patch's code is in memory, then rewrites it (`VirtualProtect`, `FlushInstructionCache`). Code that
  is ambiguous or not as expected is left alone; in other programs (Editor.exe) nothing turns up.
- No C runtime (`/Zl /GS- /NODEFAULTLIB`, subsystem 5.01), so it loads on Windows XP.

## Building

Needs Visual Studio (or Build Tools) with the "Desktop development with C++" workload and the VS 2022
MSBuild (the one of VS 2019 can't handle SDK 8 from `global.json`).

```
dll\build.cmd
msbuild A2G-Patch.csproj /restore /t:Rebuild /p:Configuration=Release /p:Platform=x86
```

`dll\msvfw32.dll` is not committed; the exe embeds it when it exists, so build it first. An exe built
without it can patch the original and GOG exes but not the 2007 CD release, and says so.

The logo (`A2G-Patch.ico`, `site/favicon.ico`, `site/logo.png`) is drawn by `tools/MakeIcon.cs`; the
file's header says how to run it. It is not part of the build.

Tests (xUnit, net472, x86), restore and build in separate calls:

```
msbuild A2G-Patch.Tests/A2G-Patch.Tests.csproj /t:Restore
msbuild A2G-Patch.Tests/A2G-Patch.Tests.csproj /t:Build /p:Configuration=Release
dotnet test A2G-Patch.Tests/A2G-Patch.Tests.csproj --configuration Release --no-build --no-restore --collect:"XPlat Code Coverage" --results-directory TestResults
powershell -File tools/CheckCoverage.ps1 TestResults
```

The tests cover every line and branch of A2G-Patch, and `tools/CheckCoverage.ps1` fails otherwise.
Some of them open the real dialogs and answer them (`A2G-Patch.Tests/Dialogs.cs`), so run them on a
desktop session. `dll/msvfw32.c` has no tests of its own; it runs only inside the game.

GitHub Actions runs the tests with this check on every push and pull request (`.github/workflows/tests.yml`).
A tag `v*` does the same and then builds the release (`release.yml`). A push to `main` that changes
`site/` deploys the project page (`pages.yml`).

## License

GPL-3.0, see [LICENSE](LICENSE).
