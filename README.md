# A2G-Patch

Patches for **Anstoss 2 Gold**, switched on and off in one small window, one tile each in three groups:

"Patches": the game's bugs under one switch, patch 1.01, and fixes for crashes and current Windows.

- **Spielfehler** (Behebt: Geldanlagen, Kadergröße, …), one switch for nine bug fixes; each keeps its own places
  and DLL flag, and the switch counts as on once one of them is applied (an exe patched by an older
  version keeps its fixes, and applying completes the rest):
  - **Geldanlagen** (Wertzuwachs von Geldanlagen nicht mehr vom Kontostand abziehen): the game takes every
    gain of an investment off the bank balance, so the better it does, the less money is in the account
    (and the less interest it earns, or the more overdraft interest it costs). Closing it brings everything
    back at once, which looks like double interest. With the patch the balance only goes down by the money
    paid in, and the gain arrives once when the investment is closed.
  - **Kadergröße** (Kader nicht über 30 Spieler wachsen lassen): a club with players on loan can get a 31st
    player, which overwrites data behind the squad array. The patch counts the actual squad too.
  - **Pokalauslosung** (Vereinsnamen bei der Pokalauslosung richtig darstellen): on current Windows and
    under Wine, the club names on the sign in the cup draw video come out twice as wide, half as high and
    dotted.
  - **Aufstellung** (Knöpfe „A/B“ und „i“ in der Aufstellung an ihrem Platz): picture buttons take their
    size from memory they never cleared before their picture is loaded. In the team lineup before a match,
    "A/B" and "i" are placed by that size, so with old data there they end up shifted or off screen. Seen
    under Wine; on Windows that memory is usually 0, the patch makes sure.
  - **Kalkulation** (Kein Hänger beim Öffnen der Kalkulation in der Vorsaison): the calculation screen sets
    up one of its two selections from memory it never cleared. With a large value there it stays hidden
    and the game waits for it. Seen under Wine; on Windows that memory is usually 0, the patch makes sure.
  - **Große Festplatten** (Keine falsche Warnung „Festplatte fast voll“ beim Speichern): the free space is
    computed in 32 bits, so on large disks the warning comes up now and then for no reason.
  - **Pfeile gedrückt halten** (Pfeile zählen beim Gedrückthalten weiter): on current Windows, the game's
    arrow buttons for numeric values count one or two steps and then stop while held down. Until now the
    workaround was to run the game in the "Windows 98" compatibility mode.
  - **Bildschirmauflösung** (Desktop-Auflösung auch nach einem Absturz zurückholen): the game switches the
    screen to 640x480 in a way that outlives it; after a crash the desktop stayed at 640x480. With the
    patch Windows restores the resolution itself when the game ends. The GOG release already does this;
    there this fix is left out.
  - **Langer Windows-Betrieb** (Kein Einfrieren nach über 24 Tagen Windows-Laufzeit): a 3-second wait in a
    reply sequence turns into weeks once Windows has run for 2^31 ms (Fast Startup keeps the uptime).
- **Patch 1.01** (Trainingslager in der Winterpause, Zweitpositionen für neue Spieler und mehr): the GOG
  release and the 2007 CD release are built from the 1.00 source. The patch brings program and data to the
  official patch 1.01, which the 1999 release contains already, among them the training camp in the winter
  break. Greyed out for the 1999 release.
- **Nicht abspielbare Videos (WM, EM, Finale) überspringen statt abzustürzen**: on current Windows the game
  crashes when it plays `WM.AVI`, `EM.AVI` or `FINALE.AVI` (e.g. after winning the World Cup), because
  their Indeo codec is gone. With the patch these videos are skipped and the game goes on; with an Indeo
  codec installed they still play.
- **Ton über das Standard-Audiogerät von Windows**: the game picks the last enumerated sound device instead
  of the default one, so with several outputs (monitor, headset) the sound may go to the wrong one.

"Balancing": changes to how the game plays.

- **Sperre nach drei Verhandlungsterminen mit einem Verein baut sich jeden Monat ab**: after three
  negotiation dates with players of the same club, that club "would under no circumstances release" any
  further player. The game means to halve this count every month, but its code halves the wrong bytes, so
  the block lasts until the next season. With the patch it wears off month by month as intended.
- **Antworten in Interviews wirken sich aus (Fans, Vorstand, Moral, Strafen …)**: every answer in an
  interview is meant to do something (more or fewer fans, a happier or angrier board, the team's morale, a
  fine), but the game applies the answers from a place they are never written to, so none of them has any
  effect. With the patch they do what their texts say. It is the same change as A2G-Trainer's "Wirkung der
  Antworten anwenden", which recognizes the patched game.
- **Torgrenze auf 15 Tore pro Mannschaft erhöhen**: the match engine lets a team score only while it has
  fewer than 6 goals up to minute 45 and fewer than 10 after, and halves its chances from 7 goals on. The
  patch raises the limit to 15 (the most the game can store) and drops the halving.
- **Unpassende Ansprachen in der Mannschaftssitzung schwächen die Mannschaft**: the game's help warns that a
  pre-match speech that doesn't fit the situation turns its effect into the negative, but the game only
  shows a message and takes the use. With the patch such a speech takes its percentages off defence and
  attack for that match ("+3%" becomes -3 %). A failed roll against the manager's motivation skill and a
  speech without uses left stay without effect.
- **Neue Trainer: Ex-Spieler ab 33, sonst Kompetenz 3 bis 11 statt 2** ("Trainer-Nachwuchs"): when a
  trainer or manager of the pool the clubs hire from (co-trainers included) retires, a former player from
  a waiting list takes his place. Players only get on that list if they end their career at 36 or older,
  which outfield players rarely do, so the list stays empty and the retired one comes back under the same
  name, aged 30 with competence 2 (goalkeeper trainers aged 35 with 2). They hardly grow, and in the long
  run the strong trainers die out. With the patch former players get on the list from 33, and a place
  without one comes back with competence 3 to 11, most often around 7, so that trainers with competence 15 keep coming.

"Nur für die GOG-Version", only for the GOG release and greyed out for the other releases (GOG sells the
2007 release by Kalypso, its `run.exe` taken out of the copy protection, which leaves things the other
releases don't need):

- **Keine Kopierschutz-Strafen (z. B. „Raubkopie“ als Vereinsname)** (bug fix): what is left of the copy
  protection fails in GOG's `run.exe`: 11 CD checks (from the second season all teams are called
  "Raubkopie", players drop to strength 1) and ten integrity checks (functions skip their work or double
  negative values). The patch lets all of them pass.
- **Dialoge mit Titelleiste und Rahmen wie im Original von 1999**: GOG's `RESDLL.dll` lacks the title bar
  of 342 dialogs, the sunken frame around 68 controls (e.g. the choices that look like buttons) and a
  few other styles. The patch gives them back.
- **Kein schwarzer Strich an den Animationen (z. B. im Hauptmenü)** (bug fix): GOG's DirectDraw wrapper
  (`ddraw.dll`) shows a black line along the right and bottom edge of the animations (the overhead kick in
  the main menu and the others). Offered only with GOG's `ddraw.dll` in the game folder.

Works with the original release (1999), the GOG release and the 2007 CD release. Like the game, the
user interface is in German.

Project page with download: <https://transfairs.github.io/A2G-Patcher/en/> (German:
<https://transfairs.github.io/A2G-Patcher/>). All versions are
on the [releases page](https://github.com/transfairs/A2G-Patcher/releases).

Sister projects: [A2G-Trainer](https://github.com/transfairs/A2G-Trainer) (its *Spielmechaniken*
switches lift the goal limits and let interview answers take effect while the trainer runs; it recognizes A2G-Patch's changes) and
[A2G-Scaler](https://github.com/transfairs/A2G-Scaler).

## Using it

1. Close the game.
2. Start `A2G-Patch.exe`, from anywhere. It offers its own folder and the installed CD and GOG
   releases (from the registry); *Durchsuchen …* picks another game folder.
3. Switch on the patches you want, switch off the ones to take back (an orange dot marks each change
   not applied yet), and click *Übernehmen*.

*Darstellung* in the banner picks light, dark or *Wie Windows* (the default), which follows Windows'
app mode (Settings → Personalisation → Colours), also when it is switched while the window is open.
The choice is kept in `HKEY_CURRENT_USER\Software\A2G-Patch`. Message boxes and the folder dialog
stay as Windows draws them.

What happens depends on the release:

| Release | Exe | How |
|---|---|---|
| Original (1999) | `anstoss2.exe` | Patched on disk. A backup `anstoss2.exe.vor-a2g-patch` is written first; once all patches are taken back, the exe is byte-identical again and the backup goes. |
| GOG | `run.exe` | Same, `run.exe.vor-a2g-patch`. A GOG Galaxy file check restores the original; then apply again. The GOG patches also change other files: `RESDLL.dll` (backup `RESDLL.dll.vor-a2g-patch`) and the data files of patch 1.01 (taken back without a backup, see below). |
| 2007 CD | `run.exe` (packed) | The code only exists decrypted in memory, so `msvfw32.dll` is put into the game folder. The game loads it instead of the Windows DLL; it forwards everything to the real one and patches the code at every game start. Taking all patches back deletes it. Patch 1.01's data files are changed on disk as for GOG. A `msvfw32.dll` from another version of A2G-Patch is replaced on *Übernehmen*, even with the same patches switched on. |

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

**Cup draw.** The cup draw video (`FUN_00419e10` in the original exe) draws the club names with GDI into
`CreateCompatibleBitmap(screen, 128, 32)` and copies them out with `GetBitmapBits(bitmap, 0x2000, ...)`,
expecting 16 bits per pixel. On current Windows and under Wine that bitmap has 32 bits per pixel even in
the game's 16-bit mode, so the game gets the upper half of the picture with each pixel split in two. The
patch sends the call to added code that fetches the picture with `GetDIBits` as a top-down 16-bit DIB:

```
call [GetBitmapBits]   ->   call helper / nop
helper: GetDC(NULL) / GetDIBits(dc, bitmap, 0, 32, bits, 128x-32 16 bit BI_RGB) / ReleaseDC
        if the game's surface is 5-6-5 (green mask [esi+8040] = 7E0): convert each 5-5-5 pixel
        ret C                                                          ; stdcall, like GetBitmapBits
```

Wine ignores the color masks of a `BI_BITFIELDS` request, hence `BI_RGB` (5-5-5) and the conversion;
black stays 0, the game's transparent color. The helper (177 bytes) is the only patch that needs room of
its own. On disk it goes to the end of the exe's last executable section, which is zero there: behind the
original's code in `.text` (the loader maps a section's whole raw data, also past `VirtualSize`), and at
the end of GOG's `.idata`. It calls `GetDC`, `GetDIBits` and `ReleaseDC` through the game's own import
entries, read from the exe's headers. Taking the patch back zeroes it again. In memory, `msvfw32.dll`
puts the same code into a `VirtualAlloc` block with the three addresses from `GetProcAddress`.

**Preseason calculation.** The constructor of the calculation screen ("ANSTOSS2 - Kalkulation", at
`0x4a513f` in the original exe) sets the selection of "Ermittlung der Zahlen" (`+0x10D48`) to 0 but leaves
that of "Darstellung" (`+0x10D46`) as it comes from the heap. With a large value there, drawing the list
reads past its entries, the access violation inside `WM_INITDIALOG` is swallowed by the window callback,
and the screen stays hidden while the game waits for it. Found under Wine (A2G-Linux), where that memory
can hold anything, depending on what was freed before. The patch clears both, in the same 11 bytes:

```
mov word [esi+10D48],0 / mov eax,esi   ->   mov eax,esi / and dword [esi+10D46],0 / nop / nop
```

**Picture buttons.** The constructor of the game's picture button class (`0x5bc3a0` in the original exe,
`0x5bf0d0` in GOG's and the 2007 CD's) clears most of its fields but leaves the `BITMAP` it fills when a
picture is loaded (`+0x7C` to `+0x93`, width and height at `+0x80` and `+0x84`) as it comes from the heap.
The team lineup before a match moves "A/B" and "i" to their template position with that width and height
before it loads their pictures, then aligns them to the bottom right corner of that window. With zeroed
memory they land on the template's spot; with old data there the window gets a huge size, its corner is
clipped, and the button ends up shifted or off screen until the lineup is opened again. Found under Wine
(A2G-Linux, `a2start.c`). The patch clears the `BITMAP` too, in the same 142 bytes: the twelve stores
beyond `+0x7F` had a 4-byte displacement and now go through `edx` = `this+0x80` with one byte, which
leaves room for the six new ones (`esi` is the button, `edx` is free there):

```
xor eax,eax / mov ecx,CString / mov [esi+5C..68],eax|ecx / mov [esi+A4],eax / ... / mov [esi+94],eax
mov [esi],vtable / mov eax,esi / pop esi / ret
->
xor eax,eax / mov ecx,CString / mov [esi+5C..68],eax|ecx / ... / mov [esi+74],eax / mov [esi+78],eax
mov [esi+7C],eax / lea edx,[esi+80] / mov [edx+0..24],eax / mov [edx+30..48],eax / nop x13
mov [esi],vtable / mov eax,esi / pop esi / ret
```

The string and vtable addresses differ between the builds and are kept. The constructor lies outside the
code the 2007 release checks with its CRCs.

**Interviews.** The interview engine (pointer at `0x77f898` in the original exe, its questions from
`TEXTE\inttext.crn`) works on a heap copy of the interview context `0x77f960` (engine `+0x11C`) and stores
the chosen answer's two effect pairs (code, value) there, at `+0x14` to `+0x20`. Then `0x4f8a90` applies the
pairs with `0x4f8a00`, but from the context itself (`push 0x77f960`), whose pairs are never written: every
answer applies "fans +0" twice. Two codes also don't match the cases of the effect function `0x4f87f0`: the
texts use 10 for the team's morale (no case) and 12 for a fine in DM (case 12 is the morale change
`0x5ffa50`). The patch adds the stub of A2G-Trainer's `InterviewEffectsFix` byte for byte (signature
`A2GINT02`, so the trainer recognizes it and can switch it with the flag at `+8`) and sends two calls there:

```
push context / call 0x4f8a00                    ->  push context / call stub+20
case 12: push value / push club / call 0x5ffa50  ->  ... / call stub+C0
stub+20: flag set, engine and copy there: copy the pairs into the context, 10 -> 12,
         12 -> 12 with the value marked (0x40 in the high byte); otherwise the pairs 0 / jmp 0x4f8a00
stub+C0: marked: club+38C (other costs) += DM * [0x78bbb8] / 100 / ret; otherwise jmp 0x5ffa50
```

A fine is booked as the game books its own (money units = DM times the money factor `0x78bbb8` / 100).
The engine pointer is read from the code before the call (`mov ecx,[engine]`); it lies 0xC8 bytes before
the context, the money factor 0xC258 bytes behind it, in all three builds (GOG's and the 2007 CD's context
is `0x782aa0`). The stub (256 bytes) goes right in front of the cup draw helper's place where that is zero,
at the end of GOG's `.idata`. The original's `.text` has no room left there, so the original exe gets a
section of its own, `.a2int` (data behind the end of the file, loaded behind the image, executable and
writable), which taking the patch back removes again, restoring the exe to the byte. The cup draw patch
skips that section. In memory, `msvfw32.dll` puts the stub into a `VirtualAlloc` block. Both calls lie
outside the 2007 release's CRC stretches.

**Pre-match speeches.** The check of the chosen speech `0x4c3a20` decides whether it fits, takes a use and,
from speech 28 on, rolls against the manager's motivation skill. Only for a speech that works it marks the
team block (`+0xE4` = 1, `+0xE5` = the number, the factor `+0x1C` = 100), and before each strength
calculation `0x4c4bd0` applies the percentages of `+0xE5` from the speech table `0x6b7d50` to defence
(`+0x10`) and attack (`+0x16`) and sets its hardness (`+0x31`). One that doesn't fit only gets a message.
The patch changes three places; the function is rewritten in its own 176 bytes:

```
roll failed (0x4c4a74):  xor edi,edi              ->  xor ebx,ebx (as if no use was taken: no effect)
marking (0x4c4aba):      works: +E4/+E5/+1C       ->  the same; doesn't fit: +E5 = -number
0x4c4bd0:                percentages of +E5       ->  negative number: their amounts taken off, hardness kept
```

So "-5/-5 hart" stays -5 % without the hardness, and an overconfidence isn't lifted. The table's address
differs between the builds and is taken from the code.

**Negotiation counters.** Every club keeps a counter for each human manager (club `+0x1081` + manager
number 1-4). Arranging a negotiation date with one of its players counts it up to at most 10; from 3 the
club "would under no circumstances release" the player (text 2626, checks at `0x4cf0e3` and `0x53e55f`).
The monthly club update `0x642d00` (from `0x642e20` at the month change) halves the counters, but one byte
too low: it halves `+0x107d` to `+0x1080`, so the real counters only go back to 0 at the season start. The
patch moves its four loads and four stores to `+0x1081` to `+0x1084` (8 bytes, at `0x642dcf`; GOG
and 2007 CD `0x647bff`):

```
mov cl/al/bl/dl,[ebp+107D..1080] / shr x4 / push ebp / mov [ebp+107D..1080],cl/al/bl/dl
->  the same with +1081..1084
```

**Investments.** The game doesn't store the bank balance; `0x571060` works it out from the club's finance
block (club `+0x154`) each time: the ledger `+0` minus the current value of the active investments
(`0x58bf30`: six slots of 0x10 bytes from `+0xa8`, each worth its base `+4` times today's index over the
index at purchase) plus the loans (`0x58bf10`) plus the season's bookings (`0x570f80`). The ledger takes no
interest, so every rise of an index takes as much off the balance, and with it off the daily interest
(`0x58ba80`, overdraft included). Closing an investment (`0x58bec0`) clears its slot, which gives the value
back, and books value minus deposit as interest on top. The patch subtracts the deposits (`+0` of each
slot, written only when buying; the season change `0x58c290` sets only base and index anew) instead of the
values, in the same 48 bytes (GOG and 2007 CD `0x573310`). The club's worth (ledger + values - loans) stays
as it was, and as nothing is stored, switching the patch off shows the old balance again:

```
push esi / mov esi,[esp+8] / push edi / push esi / call values / mov edi,[esi] / add esp,4 / sub edi,eax /
push esi / call loans / add esp,4 / add edi,eax / push esi / call bookings / add esp,4 / add eax,edi /
pop edi / pop esi / ret / nop x4
->
push esi / mov esi,[esp+8] / push esi / call loans / add eax,[esi] / sub esi,-80 / push 6 / pop ecx /
next: test byte [esi+33],1 / je skip / sub eax,[esi+28] / skip: add esi,10 / loop next /
pop esi / push eax / push esi / call bookings / pop ecx / pop ecx / add eax,ecx / pop esi / ret / nop
```

Taking it back restores the call to `values`, which lies 0x20 bytes behind `loans` in every build (the
patch is offered only where the original code shows that). The function lies outside the 2007 release's
CRC stretches.

**Trainer supply.** Trainers and managers share one pool (1000 places of 32 bytes from `0x87ffa8`: `+4`
trainer level, `+5` manager level, the higher one is the trade, `+7` age); co-trainers are hired from it,
goalkeeper trainers have their own table (`0xbf31e8`). At the season change (`0x5cc3d0`, goalkeeper
trainers `0x5cc9e0`) whoever is unemployed and older than 65 plus up to 2 retires, and his place is taken
from a waiting list per country group: `0x5a1500` takes the next of 20 entries from `0x826bde`
(`0x5a15c0`: 10 from `0x826efe`). A player gets on it when he ends his career (`0x5a12f0`) at 36 or older
with enough games; his competence is 1 + (age - 30) / 3 + random(5) + his strength, at most 12. Players
usually end their career only when their strength has dropped to 0, outfield players rarely at 36, so in a
long game the list is empty and the place goes back to the same person: age 30, competence 2 in his trade
and 1 in the other (goalkeeper trainers 35 and 2). Up to 44 the competence grows by 1 with 20 % a season
(goalkeeper trainers 50 %), so from 2 nobody gets far, and the strong trainers of the start die out. The
patch lowers the age to 33 and rewrites both functions in their own bytes (shorter, so a roll fits):

```
cmp dl,23 / jbe (not on the list)             ->  cmp dl,20
place back without an entry: competence 2     ->  3 + random(3) + random(3) + random(3) + random(3),
                                                  3 to 11, most often 7 (1 %, 5 %, 12 %, 20 %, 24 %, ...)
```

Simulated over 60 seasons with the game's rules (growth, decline from 61, retirement, the per-group
balancing in `0x5cc740`), this keeps a bell around 7 to 8 with 1 to 2 % at 15; the original ends up with
nobody above 12. A goalkeeper trainer coming back reaches 15 with about 9 %. The random function
(`0x645ed0`) is taken from the call right behind the age check (`push 3 / call`), the tables from the
original code; the places are `0x5a1355`, `0x5a1500` and `0x5a15c0` (GOG `0x5a3b65`, `0x5a3d10`,
`0x5a3dd0`, the 2007 release the same in memory, outside its CRC stretches).

### GOG release

The GOG patches (`GogPatches.cs`) change more than places in the code, so they are offered only where the
exe is patched on disk and where the files are GOG's (1.0 fix2, recognized by their patterns or SHA-256);
patch 1.01 also on the 2007 CD release, see below.
The changes of all switches are made in memory first and then written together, `run.exe` first. They come
from porting the game to Linux (A2G-Linux: `a2patch.py`, `a2data101.py`, `a2movelist.py`, `a2resdll.py`).

**Copy protection.** The 2007 release checks its CD in 11 places of the game. Each calls one signature
routine and subtracts the expected value; zero or more means "original CD". GOG replaced the subtractions
with NOPs, which leaves the raw signature and makes every check fail. The patch puts in what a passed check
leaves:

```
call check / mov r32,eax / add esp,4 / nop x6   ->  ... / xor r32,r32 / nop x4
call check / add esp,4 / nop x5                 ->  ... / xor eax,eax / nop x3
```

Besides, five places compute a CRC32 of `run.exe` on disk (offset 0x80 to the end) and compare it with
the value at offset 0x7C, five others a CRC32 of a stretch of their own code (a descriptor `{CRC, ?,
length}` right before the stub `sub esp,C0 / push ebx,ebp,esi,edi / mov ebp,esp`). GOG's unpacked
`run.exe` fails all ten; GOG disabled four of the code checks, the others make a function skip its work
(`0x5cc9d0`, `0x63f320`) or double negative values (`0x418ca0`). With the patch on, A2G-Patch stores the
correct values after every other change to the exe, the file's CRC last. Taking it back restores GOG's
NOPs and values. All places that use the protection's hidden API calls were followed; the penalties found
all sit on the failure paths of these checks.

**Patch 1.01.** Found by comparing GOG's `run.exe` with the 1.01 program and the 1999 release; differences
that only concern the copy protection, GOG's own changes or the compiler were left out. The program
changes are written anew in `gog/a2v101.S` (GNU assembler); `tools/make_v101.py` assembles it at
`0xcbd000`, where GOG's image ends, into `V101Code.cs` and `dll/v101code.h`. The patch adds a section `.a2v101` behind
everything in the file (GOG's signature included, which no longer matches after any patch anyway) and
makes 13 places jump there or change in place:

- player lists with a count per position (`0x412dd0`): players are stored one after the other with
  their real count instead of at their list index
- message 0xC7 only if a value of the object is above 47 (`0x415da0`, two places)
- the cup draw loads `archives/movelist.dat` (the sign with the club names) from the install path, not
  from the CD root, where the videos are (`0x468a4e`). In GOG's `run.exe` both are the game folder, but
  the 2007 CD has no `MoveList.dat`: without this the sign never shows up there
- message 0xA41 is gone (`0x501dcf`)
- a block of counters is cleared, and a counter grows by one instead of by another value (`0x557e30`)
- five counters of an entry stay below 1,000,000, negative amounts are ignored (`0x568ca0`)
- a dialog returns its result into the team data (`0x5a0b70`)
- new players get a second position in 20 % of the cases (`0x5a2690`)
- traits of new goalkeepers as in 1.01 (`0x5a2fc0`)
- when the human managers are reordered, a data block of 0x160 bytes per manager is reordered with them
  (`0x5bd540`)

The new functions were checked in an x86 emulator against the 1.01 originals with thousands of random
inputs. GOG's `run.exe` has leftovers behind its section headers; the 40 bytes the new header
overwrites are kept right behind the code, so taking the patch back restores the exe to the byte.

1.01 also changes three data files; all of them are made from GOG's files and own code, without files of
the patch:

- `TEXTE\EreigGold.saq` (event texts): two texts end with "." instead of ":". The file is decrypted
  (each byte XOR the previous encrypted byte, starting with the length's low byte), changed and
  encrypted again, and also written as `EreigGld.saq`, the name 1.01 uses. Identical to 1.01.
- `LAENDER.SAV` in `DATA.DEF` and `DATA.VER`: one value changes from 3 to 4. Identical to 1.01.
- `ARCHIVES\MoveList.dat`, new in 1.01: the animation of a sign flying into a video window, per frame a
  table mapping the screen pixels to a 128x32 picture the game draws at run time. `MoveList` holds only
  the sign's path (position, size, the four corners per frame) and computes the tables as perspective
  projections, in decimal arithmetic so the result is the same on every machine. Inside the sign they
  differ from 1.01's file by at most one pixel.

Taking the patch back reverses these changes (the event texts and `LAENDER.SAV` both ways, checked by
their SHA-256) and deletes the two new files. A data file that is neither GOG's nor 1.01's (e.g. from
another data set) is left as it is.

The 2007 CD release needs the patch as well: its `run.exe`, read from the running game, holds all 13
places at the same addresses with the bytes of 1.00, and its three data files are GOG's. There
`msvfw32.dll` (flag 1024) puts the code into a `VirtualAlloc` block and moves it there: 31 `call`/`jmp`
into the game (relative, they change against the move) and two addresses inside the code (absolute, they
change with it), listed in `V101Code.Relative` and `Absolute`. Then the 13 places jump to the block or change. It
patches only once all 13 hold the bytes of 1.00. A2G-Patch changes the data files on disk, as for GOG. The
copy protection and dialog patches aren't needed there: the CD passes the checks, and the 2007
`RESDLL.dll` has the title bars of 1999.

**Dialogs.** All dialogs are templates in `RESDLL.dll`, and GOG's file lacks styles the 1999 file has
(found by comparing the two files; the tables in `DialogStyles` are own data): the title bar
(`WS_CAPTION`) on 342 dialogs and `DS_MODALFRAME` on 65 of them (GOG's file uses `DS_CENTER` instead; the
game centers every dialog with a title bar over its window itself), the sunken frame
(`WS_EX_CLIENTEDGE`) around 68 controls, `WS_EX_TRANSPARENT` on 16 radio buttons, the character set and
name of the dialog font, control IDs -1 as 32-bit values and three titles with double instead of single
quotes. Not changed are texts and pictures the 2007 release changed on purpose (version "1.0" in the info
dialog, "Championsleague", "Managerkarriere", four pictures). The file keeps its size; GOG's file and the
result are checked by their SHA-256. GOG's file can't be told from the result alone, so taking the patch
back restores the backup `RESDLL.dll.vor-a2g-patch`; without it the switch stays on and greyed out.

**Animation edge.** The animations (`*.anm` in `ARCHIVES\ANIMDATA.CPR`, e.g. `fallrz.anm`, the overhead
kick in the main menu) play in a child window "DDOutput" exactly as large as the picture
(`FUN_004093d0` in the original). Its thread blits the offscreen surface onto the primary surface into the
window's client rect (`0x40bc50`, `GetClientRect` / `ClientToScreen` / `OffsetRect`, a clipper on the
window). GOG's wrapper (`ddraw.dll`, "GOG.com DirectX 1-7 wrapper", Direct3D 9 underneath) shows the
window's rect plus one column and one row of the primary surface, where nothing was drawn: a black line
right of and below the picture. WineD3D, for example, shows the rect exactly. The
patch makes the window one pixel smaller each way (`dec ecx` / `dec eax` after `x + width`, `y + height`;
`and dword [esi+4C],0` instead of a 7-byte `mov` makes room) and blits the whole picture to the source rect
moved onto the window (`CopyRect(dest, source)` instead of `GetClientRect(window, dest)`; the window for
`ClientToScreen` from `[esi+1C]`, the same handle as `[esi+480]`, in 3 bytes instead of 6). So the column
and row the wrapper adds show the picture's last ones. With a correct DirectDraw the clipper keeps the blit
in the smaller window and the picture would lose them, so the patch is offered only when the folder's
`ddraw.dll` names GOG's wrapper (UTF-16 in its version resource); a patched exe is taken back without it as
well. The decoded animation frames (LZ77 with delta to the previous frame, `0x651a80` and `0x409970`) are
clean; the line came only from the presentation. The 1999 release has the same code (offered there too if
GOG's `ddraw.dll` is put in its folder); the 2007 CD release isn't offered it, as `msvfw32.dll` makes only
the patches with a flag.

## msvfw32.dll (`dll/`)

A proxy like A2G-Scaler's `dsound.dll`: `msvfw32` is not a KnownDLL, so the copy in the program folder
wins the loader search (checked against the running 2007 `run.exe`, whose `MSVFW32.dll` otherwise
comes from SysWOW64). `dsound.dll` would work too, but A2G-Scaler uses that name.

- All 47 exports of the real `msvfw32.dll` are `jmp` stubs (ordinals kept in `msvfw32.def`). The real
  DLL is loaded lazily on the first call, never in `DllMain` (loader lock).
- Which patches it applies are three flags bytes (low byte first) after the marker `A2GPatchFlags` in its
  `.data`; A2G-Patch sets them when installing (1 goal limits, 2 arrow repeat, 4 video crash, 8 freeze,
  16 sound device, 32 disk warning, 64 squad limit, 128 display mode, 256 cup draw, 512 preseason
  calculation, 1024 patch 1.01, 2048 picture buttons, 4096 interviews, 8192 speeches, 16384 negotiation
  counters, 32768 investments, 65536 trainer supply; the build has all on). Versions before the third
  byte had two; the NUL ending their string follows them, so they read as the same flags. The other GOG patches have no flag: they change files on disk, which
  the DLL doesn't. An installed copy that differs from the
  embedded one in more than these bytes is from another version and gets replaced.
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

`V101Code.cs` and `dll/v101code.h` (the code of patch 1.01) are generated from `gog/a2v101.S` and
committed. After changing the source, run `python3 tools/make_v101.py`; it needs GNU binutils (`as`,
`ld`, `objcopy`, `nm`), e.g. on Linux, in WSL or MSYS2. It links the code twice, 0x1004 bytes apart, and
takes the relocations from the differences. `python3 tools/make_v101.py --from-cs` rewrites both files
from `V101Code.cs` without binutils.

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
