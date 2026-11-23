# A2G-Patch

Patches for **Anstoss 2 Gold**, switched on and off in one small window, one tile each in groups:

"Patches": the game's bugs under one switch, patch 1.01, and fixes for crashes, current Windows and the
outro under VMware.

- **Spielfehler** (Behebt: Geldanlagen, Ereignisse, …), one switch for sixteen bug fixes; each keeps its own places
  and DLL flag, and the switch counts as on once one of them is applied (an exe patched by an older
  version keeps its fixes, and applying completes the rest):
  - **Geldanlagen** (Wertzuwachs von Geldanlagen nicht mehr vom Kontostand abziehen): the game takes every
    gain of an investment off the bank balance, so the better it does, the less money is in the account
    (and the less interest it earns, or the more overdraft interest it costs). Closing it brings everything
    back at once, which looks like double interest. With the patch the balance only goes down by the money
    paid in, and the gain arrives once when the investment is closed.
  - **Ereignisse** (Ereignisse wirken wie in ihrer Meldung (Geld, Form, Frische, Verletzungen, Klang)): some random
    events do something else than their message says. The free copier ("Sie sparen …") costs money and the
    prize draw ("Kostenpunkt …") brings money; the cups of a fan club tournament are booked as income, the
    sale of a plot and the fans' collection of donations as expenses. A won negotiation with the chocolate
    maker brings the plain amount instead of the doubled one the question offers. Four messages say that a
    player loses freshness points, but he gains them. The hints for the co-trainer, after which some players
    gain a form point, play the negative sound. A concussion in training hurts the other of the two
    players the message names; fish poisoning shows as an injury without a name; a player who grows old gets a request for a day off appended to his message; and
    after a riot that costs the club a lawsuit the message says all is calm. A bonus from the board
    promises every player a form point, a slip on a banana skin costs one, but neither happens. A delay of
    the stadium works never names two of its reasons, a fans' blockade and the weather. With the patch each
    does what its message says, and all reasons can hold up the works.
  - **Ereignis-Abstürze** (Kein Absturz bei Ereignissen, für die kein passender Spieler da ist): some random
    events draw a player and use him without checking that one was found (for instance when the one drawn
    is on loan). The game then crashes. With the patch the event doesn't happen.
  - **Schiedsrichter-Groll** (Schiedsrichter mit Groll treffen in den Spielszenen den richtigen Verein): a
    referee can hold a grudge against a club, for instance after the manager insulted him in front of a
    microphone. In league matches he then shows that club's players more cards, as intended, but the scenes
    he decides compare the grudge with the club's number plus one: they hit the club with the next lower
    number, and never club 0. With the patch they hit the club he holds the grudge against.
  - **Trainerkompetenz** (Trainer und Manager in schwachen Ländern werden jährlich besser): once a year the
    game raises every tenth trainer or manager by 1 or 2 points in countries where their average is below
    6, but it never stores the new value. With the patch they keep it; the cut in countries with an
    average above 10 already works.
  - **Notenschnitt** (Einsätze ohne Note zählen nicht im Notenschnitt (Spielerdetails, Fans)): a player who
    plays less than 20 minutes gets no mark. If he gets a card, the season average in his details and the
    fans' check for a hated player count that appearance as mark 0, better than a 1. With the patch only
    marks count, as in the team statistics. The chart of the average marks per week in the statistics
    labels its axis "Formpunkte/6", the label of the form chart; with the patch it reads "Noten/6".
  - **Meiste Einsätze** (Vereinsliste der meisten Einsätze nimmt nach 50 Spielern wieder neue auf): the
    club list of the players with the most appearances keeps 50 players. A player who leaves stays marked as
    still at the club, so once 50 have played for the club nobody new gets in, and the list stays as it is
    for good. With the patch the marks follow the squad again; a list frozen in a saved game takes in new
    players from the next match. The game counts a player's appearances for the club only in this list, so
    a new player enters at once and pushes out the former player with the fewest appearances; the first 20
    places (instead of 10) are never pushed out, the other 30 leave room for a full squad. Appearances that
    were never counted don't come back.
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
  - **Tippfehler** (Satzzeichen und Schreibfehler in Texten des Spiels (z. B. „rechter Verteidiger..“)): the
    message about a player's new main position ends with a full stop, and three of the position names it
    takes bring their own ("rechter Verteidiger.."). Also a doubled "mehr" when the club ends the
    contract, a comma at the start and a full stop in the middle of player descriptions, missing full
    stops, "das" for "daß", "UEFA - Pokals", "Nationalen Pokals", and the Supercup heading cut to
    "Europäischer Superc.". Changes texts like the switches under *Texte*, in every release.
- **Patch 1.01** (Trainingslager in der Winterpause, Zweitpositionen für neue Spieler, Eigenschaften neuer Torhüter, Schild der Pokalauslosung auf der 2007er-CD, Spielerlisten und Zähler wie in 1.01): the GOG
  release and the 2007 CD release are built from the 1.00 source. The patch brings program and data to the
  official patch 1.01, which the 1999 release contains already, among them the training camp in the winter
  break. Greyed out for the 1999 release.
- **Nicht abspielbare Videos (WM, EM, Finale) überspringen statt abzustürzen**: on current Windows the game
  crashes when it plays `WM.AVI`, `EM.AVI` or `FINALE.AVI` (e.g. after winning the World Cup), because
  their Indeo codec is gone. With the patch these videos are skipped and the game goes on; with an Indeo
  codec installed they still play.
- **Ton über das Standard-Audiogerät von Windows**: the game picks the last enumerated sound device instead
  of the default one, so with several outputs (monitor, headset) the sound may go to the wrong one.
- **Abspann im richtigen Tempo unter VMware (Sound Blaster 16): Ton in 16 Bit Stereo** ("Abspann-Tempo"):
  in a Windows 98 VM under VMware, the outro the game plays when it ends ran in 40 instead of 80 seconds,
  its sound skipped. The game opens DirectSound at the normal level, so the primary buffer keeps the
  default format of 8 bits; once a sound has played through it, the next sound the same process plays
  with waveOut runs on a clock twice as fast with VMware's Sound Blaster 16 (the ones after it are right
  again). With the patch the game takes the priority level and sets the primary buffer to the format of
  its music, 22050 Hz, 16 bits, stereo. The code behind `DirectSoundCreate` is rewritten in its own 121
  bytes; the buffer description and the format (38 bytes) go into the zero room behind the end of
  `.rdata` (1999 release) or in front of the interview stub's room in GOG's import section. The exe
  keeps its size, so A2G-Trainer still recognizes the build.
- **Vereinskonto stoppt bei 2 Mrd. DM, statt ins Minus zu kippen** ("Kontostand"): the game keeps a club's
  money in a 32-bit number. Past 2,147,483,647 DM a profitable week turns it into about -2.1 billion DM,
  and the game takes the club for broke (overdraft interest, a forced loan). With the patch the weekly
  booking stops at 2,000,000,000 DM; what the club earns beyond that is lost. The room to the 32-bit limit
  keeps the shown balance (with the current week's income and the loans) from running over in between.

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
- **Neue Trainer: Ex-Spieler ab 33 Jahren, sonst Kompetenz 3 bis 11 statt 2** ("Trainer-Nachwuchs"): when a
  trainer or manager of the pool the clubs hire from (co-trainers included) retires, a former player from
  a waiting list takes his place. Players only get on that list if they end their career at 36 or older,
  which outfield players rarely do, so the list stays empty and the retired one comes back under the same
  name, aged 30 with competence 2 (goalkeeper trainers aged 35 with 2). They hardly grow, and in the long
  run the strong trainers die out. With the patch former players get on the list from age 33, and a place
  without one comes back with competence 3 to 11, most often around 7, so that trainers with competence 15 keep coming.
- **Neue Spieler: Nebenposition zufällig statt fest je Hauptposition, jeder Vierte davon mit einer zweiten**
  ("Nebenpositionen"): new players (youth players and the ones the game makes up) get a secondary
  position in 40 % of the cases, but always the same one for their main position: a striker only ever
  gets OM, a right midfielder OM, a left midfielder DM, full-backs MD, and so on. With the patch the
  secondary position is drawn from all nine field positions other than the main one, and a quarter of
  those players also get a second one. Goalkeepers stay without, as before; players already in the game
  keep theirs.
- **Fischvergiftung nach der Thunfischpizza: drei Wochen Ausfall wie in der Meldung statt einer**
  ("Fischvergiftung"): the message of this random event says the player is out for three weeks, but the
  game enters a fish poisoning with its usual 7 days. With the patch he misses the three weeks.
- **Weitere Ereignisse rund um den Manager: Presse, Fans, Privatleben** ("Manager-Ereignisse"): the
  events of the manager's private life draw from the wrong table, so 25 of their 39 cases never come. Their
  own table brings them: suspicion of rioting with hooligans, a national order, a book of his own, a speech
  the microphones caught, fans honking at night or throwing tomatoes while the board's trust is low, and
  more. The ones known so far come less often. Six cases had no working code in the game, and get it:
  - a standing-room debate (a question: for removing the standing room angers the fans; against it pleases
    fans and public, but not the board),
  - an anonymous caller who offers, for a fee from the manager's own money, to wreck the office of another
    human player's club (a question; the damage goes to that club or its insurance),
  - an inheritance of 100 shares of the club's arch rival (with the stock market on, a free slot and within
    the 10 % a manager may hold),
  - respect from a journalist who can't stand the manager (outlet and full name; he turns friendly in the
    interviews; only with the "Interviews" tile, which gives journalists their mood),
  - the player of the month (a question with three players to choose from: the best average mark of the
    last four match days gains three form points, another choice costs some players one),
  - an affair with the wife of the referee of the last league match, who then holds a grudge against the
    club.
  The honking fans' message names the manager twice and gets his name both times.

"Texte": names and terms in the game's texts (`RESDLL.dll`, the event texts and the tips), for every
release. They take up the Euro patch of 2022 (a `RESDLL.dll` for the 1999 release), change only texts and
leave out its swap of the season goals "Klassenerhalt" and "ehrliche Arbeit" (the game asks for 15th
and 13th place of 18).

- **Euro statt DM** (Beträge in € statt DM, auch in Ereignissen und Tipps; die Zahlen bleiben gleich): the
  three money formats every amount the game shows goes through, the amounts in 79 dialogs, a sentence with
  "Mark", four event texts and the tips. 1 DM shows as 1 €. "DM" alone, the defensive midfielder, stays.
- **Innenverteidiger** (Innenverteidiger (IV) statt Manndecker (MD)): the position's name and abbreviation
  in lists, search dialogs and the national squad, and in the message about a new main position.
- **Europapokale** (UEFA-Cup, Cup der Pokalsieger, Weltcup und ECL-Cup statt UEFA-Pokal, Pokal der
  Pokalsieger, Weltpokal und UI-Cup): the European cups in tables, results, trophies, contracts and
  questions.
- **Liganamen** (Heutige Ligennamen: 1. und 2. Bundesliga, Regionalliga West, Championship, League One,
  Ligue 1 und 2): the leagues' names and short names; Regionalliga West/Südwest becomes Regionalliga West,
  Nordost keeps its name, which it has again since 2012; Spain's short name "1a Division" becomes
  "Primera D.".
- **Gruppen A bis H** (Gruppen A bis H statt 1 bis 8): the groups of the cups and the
  championships.
- **Vertragsbegriffe** (Grundgehalt statt Festgehalt, Ausstiegsklausel statt Freigabeklausel und fixer
  Ablöse, Rekordtransfers statt Supertransfers): the contract dialogs, the player info, the search for
  players and the transfer records.
- **Quizfragen** (Quiz der Managerfortbildung auf dem Stand von 2026: Titel, Rekorde, Regeln): 77 of the
  633 questions of the manager course. Answers on the state of November 2026 (titles, records such as Lewandowski's 41
  goals, rules such as the ball touching the referee, people in office), four that were wrong already in
  1998 (the UEFA's seat, Rangers' colours, a city without two Bundesliga clubs, the last double), and
  questions about a time that is over name their year ("Wer war 1998 Präsident des FC Schalke 04?").
- **Neue Quizfragen** (150 weitere Fragen für die Managerfortbildung, von 1998 bis 2026): 46, 59 and 45
  questions at the end of the three levels, from the World Cup 1998 to the World Cup 2026, records such as
  Bayern's six titles in 2020/21 and the FC Gütersloh's Westfalenpokal, with wrong answers that may be jokes as in the game's own. Answers that may
  change name their state ("Stand November 2026").

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
- **Keine Verzögerung nach Mausklicks mit manchen Grafikkarten** (bug fix): GOG's `ddraw.dll` takes a slow
  way of its own when the graphics driver doesn't report one capability, and the game then answers each
  click two to three seconds late (seen with a Radeon RX 6800 XT). The patch changes `ddraw.dll`; on
  systems without the delay it makes no difference. Offered only with GOG's `ddraw.dll` in the game folder.

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

The switches under *Texte* and the typing errors change `RESDLL.dll`, `TEXTE\EreigGld.saq`,
`TEXTE\EreigGold.saq`, `TEXTE\tips.txt` and `TEXTE\fort.saq` in every release, without a backup: taken back, the files
are the originals to the byte.

Under *Program Files*, writing needs administrator rights; A2G-Patch then offers to restart itself
as administrator. A `msvfw32.dll` in the game folder that isn't A2G-Patch's is never touched.

Some virus scanners flag an unsigned DLL in a game folder (DLL hijacking is a malware pattern too).

Requirements: Windows XP (SP3) or later with .NET Framework 4.0 or later. For Windows 2000 (SP4), 98 SE
and ME each release has `A2G-Patch-Win98-2000.exe` (also in `A2G-Patch-Latest.zip`), built against .NET
Framework 2.0, with the same switches. On 98 SE and ME it runs with .NET Framework 2.0 without a service
pack, the last version for these systems; of the three versions of the game only the one from 1999 runs
there.

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

**Outro speed.** The game plays `EXTRO.AVI` with `MCIWnd` when it ends. Before that, it has opened
DirectSound behind `DirectSoundCreate` (`FUN_00653440` in the original exe) at the normal cooperative
level, which leaves the primary buffer at DirectSound's default of 8 bits. With VMware's Sound Blaster 16,
the next sound the same process plays through `waveOut` after a DirectSound sound then runs on a clock
twice as fast. The patch rewrites the 121 bytes between the way out of a failed `DirectSoundCreate` and
the function's end:

```
SetCooperativeLevel(hwnd, NORMAL) / je on, else way out      ->  (hwnd, PRIORITY) / jne to the way out in front
clear caps / [argument] / [open] = 1 / size 60 / GetCaps      ->  [open] = 1 / [argument] / size 60 / GetCaps
no third argument: [3D buffers] = caps+2C                      ->  the same
                                                                  CreateSoundBuffer(primary) / SetFormat / Release
```

GetCaps fills the whole structure, so clearing it first isn't needed, and the failed
`SetCooperativeLevel` reuses the identical way out in front; that makes room for the primary buffer.
Its description and the format (`DSBUFFERDESC` with `DSBCAPS_PRIMARYBUFFER`, then PCM, 2 channels,
22050 Hz, 16 bits; 38 bytes) go behind the virtual end of the first section that isn't executable and
has zero room there (`.rdata` in the 1999 release), or else in front of the room of the interview stub
(GOG's import section). Taking the patch back zeroes them again. The DirectSound pointer, the way out's
call and the other addresses are taken from the game; every mention of the pointer must agree.

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

**Money limit.** The ledger (club `+0x154`, an Int32) changes almost only once a week: at the end of
`0x5722c0` the game adds the week's result after tax, unchecked. Past 2^31 - 1 it wraps to about -2.1
billion, and the balance (see above) with it, which the interest (`0x58ba80`) takes for an overdraft and
answers with a forced loan. The patch caps the booking at 2,000,000,000 DM; the balance adds the current week's
bookings and the loans to the ledger, and the remaining 147 million DM keep those from running over. A
ledger that is higher already (set with a trainer) stays and only stops growing; a sum below -2^31 stays
at the lowest value. The 23 bytes of the booking are too few for the check, so the patch makes room in the
week's result `0x572660`: it adds up the same 20 fields as the bookings `0x570f80` (given club `+0x154`),
which also subtract `+0x338` (the tax booked) and add `+0x33c`. Rewritten as a call of the bookings that
takes those two back (the same result, the same calls in the same order), it needs 29 of its 208 bytes, and
the check goes behind it:

```
week:    mov eax,[esp+4] / add eax,154 / push eax / call bookings / pop ecx / add eax,[ecx+338] /
         sub eax,[ecx+33C] / ret / nop x3
check:   mov ecx,[esi+154] / mov eax,ecx / add eax,edi / jno sum / mov eax,edi / sar eax,31 /
         xor eax,7FFFFFFF / sum: cmp eax,2000000000 / jle store / cmp eax,ecx / jle store /
         mov eax,2000000000 / cmp eax,ecx / jge store / mov eax,ecx /
         store: pop edi / mov [esi+154],eax / pop esi / pop ebx / add esp,24 / ret / nop...
booking: mov eax,[esi+154] / add eax,edi / pop edi / mov [esi+154],eax / pop esi / pop ebx /
         add esp,24 / ret / nop x2   ->  jmp check / nop x18
```

The bookings lie 0x100 bytes behind the first function the week's result calls, in every build, and their
own three calls give the original's back for taking the patch off. Places `0x5724f9` and `0x572660` (GOG
and 2007 CD `0x5747a9` and `0x574910`, outside the 2007 release's CRC stretches). The few one-off changes
of the ledger outside the booking stay unchecked.

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

**Secondary positions** (Balancing). Four functions create players: `0x59fdd0` (youth players),
`0x5a02d0`, `0x5a0560` and `0x5a08b0`. Each clears `+0x21` and `+0x22` (secondary positions), and for a
field player (main position `+0x20` not 1) sets `+0x21` from the table `0x6c2948` (GOG `0x6c5a58`) when
`random(100) < 40`:

| main | L | MD | LV | RV | RM | LM | DM | OM | S |
|---|---|---|---|---|---|---|---|---|---|
| secondary | MD | L | MD | MD | OM | DM | L | S | OM |

(positions 1 TW, 2 L, 3 MD, 4 LV, 5 RV, 6 RM, 7 LM, 8 DM, 9 OM, 10 S). Only the 1999 release's youth
players get a second one, in 20 % of those cases, from a switch behind it (LV -> LM, RV -> RM, RM -> RV,
LM -> LV, the others `random(10) + 1`, which can even be TW); GOG's build lacks it, Patch 1.01 brings it
back. The patch sends all four places (`0x59ff79`, `0x5a0386`, `0x5a063b`, `0x5a094e`; GOG and 2007 CD
`0x5a2836`, `0x5a2bb6`, `0x5a2e6b`, `0x5a315e`) to a helper of 83 bytes:

```
xor ecx,ecx / mov cl,[esi+20] / mov dl,[ecx+table] / mov [esi+21],dl     (the fourth with edx and al)
->  xor ecx,ecx / call helper / jmp +5 / table / nop
helper: ebx = main / eax = random(8) + 2, one up from the main on / [esi+21] = al /
        random(100) < 25: eax = random(7) + 2, one up from the lower and from the higher of both on /
        [esi+22] = al / ret
```

The 40 % stay. The random function (`0x645ed0`) is the one the check right in front of each place calls,
the table's address stays in the place, so taking the patch back restores it. The helper keeps `ebx`,
`esi`, `edi` and the stack; `eax`, `ecx` and `edx` were already used by the check's call. In the 1999
release it goes into the youth players' switch, which it replaces (that place becomes `call helper / jmp`
behind the switch, the switch's jump table address stays at its end); in GOG's `run.exe` in front of the
outro speed's data at the end of the import section, zero there; in the DLL into memory of its own. It
was run in an emulator for every main position and every outcome of the three draws: each of the eight
other field positions comes up as the first secondary position, each of the remaining seven as the
second, and nothing else. With Patch 1.01 on, the youth players' place holds 1.01's jump; the patch
takes it over (and gives it back when it goes while 1.01's section is there), and Patch 1.01 leaves the
place alone while the patch holds it, so both can be switched in any order. All places lie outside the
2007 release's CRC stretches.

**Events.** The random events of `0x556900` (texts from `TEXTE\EreigGld.saq`) book a club's expenses at
`+0x38c` and its income at `+0x390`; the hundred money events of the table at `0x6bcd80` are pairs of text
and amount, negative for expenses. Wrong are:

```
690 free copier "Sie sparen %s ein"          table 0x6bd058 {690, -120}         ->  {690, +120}
697 prize draw "Kostenpunkt: %s"             table 0x6bd090 {697, +80}          ->  {697, -80}
126 fan club tournament, cups "kosten %s"    0x556d31 jmp 0x55781d (income)     ->  jmp 0x557f59 (expenses)
143 a plot sold, "erhalten Sie %s"           0x557050, 0x55705e [esi+38c]       ->  [esi+390]
172 the fans' donations "kommen ... zusammen" 0x5578b0 jmp 0x557f56 (expenses)  ->  jmp 0x55781a (income)
```

The two ends 126 and 172 jump to are alike apart from the field, so the two trade their targets (the
displacements are the same in all builds). The chocolate maker (text 224, `0x558606`) offers a sponsor
deal and twice the amount for a negotiation with a 50:50 chance (`[0xc21eb0] = 2 * edi` for the text), but
a success books `edi`, like accepting at once. The patch doubles `edi` there; the room comes from comparing
the question's outcome (a small number, written as a dword) as a byte, so its address moves one byte:

```
cmp word [outcome],ax / push ebx / jne fail / push 3 / push 0 / push 0 / push 0 / call
->  cmp byte [outcome],al / push ebx / jne fail / add edi,edi / push 3 / xor eax,eax / push eax x3 / call
```

`0x623cc0` moves a player's freshness (`+0x31`) into his recovery pool (`+0x33`) for a positive value and
back for a negative one; training and matches pass positive values, the events 307, 308, 369 and 414
("verliert ... Frischepunkte", `0x55ed03`, `0x55ed45`, `0x55fc80`, `0x5605d6`, `0x5605e1`) pass -5 and
-10. The patch makes them 5 and 10. A message's sound is the last argument of its call (`0x4771f0`: 0
neutral, 1 positive, 2 negative, 3 question); text 56, hints for the co-trainer that give three players a
form point (`0x563cbc`), passes 2, the patch 1. In GOG and the 2007 CD the places lie 0x22F0 bytes (code)
and 0x30F8 bytes (table) further on.

Four more events don't do what their message says. Text 38, a concussion after two players collide in
training (`0x5632d7`), injures the first of the two while the text names the second (`push ebx` ->
`push ebp`). Text 392, a player losing strength with age (`0x560124`), appends text 393 raw when he has
ability bit 4; that text is now the request for a day off, with a literal "%1". The patch always takes
the plain branch (`je` -> `jmp`); the bit is cleared as before. Text 732, fish poisoning, is one of the
referee events (`0x5ee7f0`); it hurts the player through `0x633a60`, which takes the kind from a table of
two shorts at `0x6c9534` ({62, 20}) and the days from the kinds' table: kind 62 has no name (text 734 gets
kind 20, 28 days, as it says). The patch enters 68, "Fischvergiftung", with its 7 days.

Two messages name a form point that never comes: text 339, a bonus from the board ("Sämtliche Spieler
gewinnen je einen Formpunkt", `0x55f6c0`), books the money only, and text 343, a slip on a banana skin
("verliert einen Formpunkt", `0x55f77d`), only raises the team's mood. The game changes form with
`0x565bc0` (one player, kept between 0 and 20) and `0x565a50` (every player of the squad and the ones lent
out). For 339 the booking is written shorter, which makes room for the call; for 343 the player is pushed
once more under the message's arguments, with -1, and the event ends with a jump to the same ending of the
case before:

```
339: mov eax,[ebp+330] / add esp,14 / add eax,esi / mov [ebp+330],eax
->   add [ebp+330],esi / push 1 / push ebp / call 0x565a50 / add esp,1C
343: push 2 / push eax / push eax / ... / call message / add esp,18 / push 14 / push ebp / call mood / add esp,8 / mov ax,1 / ... / ret
->   push -1 / push eax / push 2 / push eax / push eax / ... / call message / add esp,18 / call 0x565bc0 /
     push 14 / push ebp / call mood / add esp,10 / jmp 0x55f749 (mov ax,1 / ... / ret)
```

A delay of the stadium works (texts 876, 877, "verzögern sich wegen %s") draws its reason with
`random(10)` (`0x55e5f3`) from a table of 12 (`0x6bd3a0`, texts 878 to 889), so 888 and 889 never come.
The patch draws from all 12. 889 reads "wegen schlechtem Wetter" and would give "wegen wegen schlechtem
Wetter"; instead of changing `TEXTE\EreigGld.saq` (which patch 1.01 recognizes by its SHA-256), the
message's two branches (one or two weeks, `0x55e604`, 121 bytes) are folded into one, which skips a
reason's first six characters when it starts with "wege":

```
push 2 / call random / pop ecx / movsx esi,si / neg eax / sbb eax,eax / inc eax (1: two weeks) / push eax /
mov ecx,texts / push edi / call text / cmp dword [eax],"wege" / jne / add eax,6 / push eax / ... /
mov eax,[esp+8] / add eax,36C (876 or 877) / ... / call sprintf / add esp,10 / pop eax /
mov cl,[ebx+esi+884] / lea eax,[eax+ecx+1] / jmp message
```

**Manager events** (Balancing). `0x55c900` holds the manager's private life, 39 cases (texts from 491 on);
`0x55c840` draws one from a weighted dispatcher (`0x784ce8`, pairs {weight, case}, one of the last 20
shown is drawn again once). `0x55c820` builds it from `0x6bd248`, the 14 pairs the other kind of events
(`0x55c230`) uses, so cases 15 to 39 never come. The table 56 bytes behind (`0x6bd280`, 39 pairs, weights
1 to 25) is referenced nowhere. The patch builds the dispatcher from it, in the builder's bytes and 4 of
the 12 nops behind (the dispatcher's start, `add ecx,6D60`, tells it from the other one):

```
push -1 / push 6BD248 / push 0E / mov ecx,dispatcher / call / ret
->  push -1 / mov eax,6BD248 / add eax,38 / push eax / push 27 / mov ecx,dispatcher / call / ret
```

The new cases were never played, so each was read: most show a message and change a value of the manager
or the club. Case 32 (text 528, fans honking at night) names the manager twice, but `0x565860` passes his
name once, and `sprintf` takes the caller's `esi`, saved behind the arguments, as the second string:
garbage or a crash. The patch passes the name twice; texts with one name ignore the second (`0x565896`):

```
lea eax,[esp+4] / lea ecx,[esp+1F8] / push eax / push esi / push ecx / call sprintf / ... / lea eax,[esp+1F8]
->  lea eax,[esp+4] / push eax / push eax / push esi / lea esi,[esp+204] / push esi / call sprintf / ... / mov eax,esi
```

The honking, the tomatoes (weight 25 each of 112) and the two front pages only come while the board's
trust (`0x639100`) is below 60, the front pages only in the first division (`+0x104` 0); else the draw
brings nothing that day. GOG and 2007 CD: `0x55eb10`, `0x6c03fc`, `0x567b73`.

Six cases get code of their own (`tools/manager_events.asm`, MASM, assembled by `tools/make_manager.py` into
`ManagerCode.cs` and `dll/managercode.h`):

| Case | Texts | In the game | With the patch |
|---|---|---|---|
| 13 | 503-505 | no code (drawn, nothing happens) | question (standing room); yes: fans -10, no: public and fans +5, board -5 |
| 21 | 514-517 | no code | with a second human player: question, fee 10,000 DM x money factor from the manager's own money; yes: damage 50,000 DM x factor to the other club, or its insurance pays (half the time) |
| 25 | 521 | text built, never shown, nothing booked | 100 shares of the arch rival (`+0xF4`) at 0 DM into the manager's slots (`+0xD8`, six of 16 bytes), with the stock market on (`0x826ba5`), the rival listed and within 10 % of its shares |
| 34 | 530 | name empty | the most hostile journalist of the club's country (mood `+0x30 + number - 1` of his record at `0x78d768 + slot * 0x19EA + i * 0x34`, 4 and above), outlet and full name; his mood becomes 1 (friendly) |
| 35 | 531-533 | no code | question with the three best average marks of the last four match days (`+0x130 + day`) as buttons, in random order; the best: +3 form, else some players -1 |
| 39 | 537 | no code | the referee of the last league match (`0x58a090`) holds a grudge against the club (`+7` of his record) |

The game keeps a journalist's outlet and last name only; case 34 reads his first name from `KLEINIG.SAV`
(the line behind outlet and last name) and leaves it out if the file doesn't hold them. The journalists'
moods only change with the interviews' effects (see **Interviews**). The questions are entries of the
question table (`0x6bd610`, 0x2C bytes each), which the dialog reads at table + number * 0x2C with a 16-bit
number, so they must lie within 0x160000 bytes below or above it.

The code goes into three functions nothing refers to (no call, jump, operand or table entry): `0x56e820`
(a news item never drawn, 1216 bytes with its padding) holds `MAIN` and behind it the questions, `0x603cd0`
(debug keys of the match, 640) `KEYS`, `0x5b45c0` (tab stops of a list box, 400) `TABS`; the rest is `int3`.
The jump table of `0x55c900` (`0x55d5d4`) then leads cases 13, 21, 25, 34, 35 and 39 there. Each entry
point keeps `0x55c900`'s frame and returns from it. GOG and 2007 CD: `0x570ad0`, `0x608630`, `0x5b7320`,
table `0x55f8c4`, outside the 2007 release's CRC stretches. The patch knows the three builds by the switch's
address and the import table entry of `GetAsyncKeyState` (GOG and the 2007 CD share addresses, not
imports), with their functions' original bytes (`tools/manager_builds.py`), and puts those back when it's
taken back. The code was checked in an x86 emulator against the game's functions as stand-ins; the DLL and
`ManagerEvents.Link` link it to the same bytes.

Version 0.5.0 left case 34 out with weight 0 (`0x6bd304`); the patch puts the 4 back, switched on or off.

**Fish poisoning** (Balancing). Text 732 says "fällt drei Wochen aus". `0x633a60` is called only by the
referee events, event 0 being the poisoning; the patch gives it 21 days whatever kind the table holds, in
the function's bytes and 5 of the 10 nops behind its `ret` (GOG and 2007 CD `0x6385c0`):

```
... / mov [ecx+46],dl / movsx eax,word [eax*2+kinds] / lea eax,[eax+eax*2] / movzx dx,byte [eax*2+days] /
mov [ecx+44],dx
->  ... / mov [ecx+46],dl / test eax,eax / movsx eax,word [eax*2+kinds] / lea eax,[eax+eax*2] /
    movzx edx,byte [eax*2+days] / jne / mov dl,21 / mov [ecx+44],dx
```

The riot (`0x5591c0`, case 3) books the lawsuit's settlement (`[0x78bb10]`) after an unlucky draw, but
shows text 283 ("unter Kontrolle gebracht") instead of 284 and formats both amounts for it into the one
buffer of the money format (`0x4a6dd0`), so 284 would show the same amount twice. The texts are kept in a
table (`0x559b30`, `[0x7836e8 + 4 + number * 4]`), so text 282 needs no copy. Rewritten in the same 132
bytes (`0x55933e`), the settlement is formatted first and copied (32 bytes) into the free buffer, then the
claim (`[0x78bb84]`), and `sprintf` writes 284 with both:

```
mov ecx,texts / mov ebp,[ecx+46C] / mov esi,[settlement] / push esi / call money / mov esi,eax /
lea edi,[esp+3C] / push 8 / pop ecx / rep movsd / push [claim] / call money / lea ecx,[esp+40] /
mov [esp],ecx / push eax / mov ecx,texts / push [ecx+474] / lea edx,[esp+23C] / push edx / call sprintf /
add esp,10 / pop esi / lea eax,[esp+22C] / push eax / push ebp / push 2 / push 5B / push ebx / nop...
```

The calls move within the block; a rewritten block keeps the four bytes behind a call (`E8`) as its
displacement and moves it by the distance the call moved, here and in the DLL (`RewritePatch.FindOnce`,
`PatchRewrite`).

**Event crashes.** `0x564ee0` (any player of a squad) and `0x564c40` (one who is fit) return NULL when the
one drawn has `+0x98` set (with 1 as second argument) or nobody fits. Of their 71 calls, four read from
the result without testing it:

```
0x55f1cb  text 321  cmp byte [esi+25],0A / jle out / ... (three checks)  ->  test esi,esi / je, the checks with short jumps to one jmp out
0x55fb4d  text 366  five draws for an older player: mov cl,[eax+1E] ...   ->  test eax,eax / je next / cmp [eax+1E],bl ...
0x55b79c  text 244  the message and the injury                           ->  test esi,esi / je (return 0), one add esp for both calls
0x5bbcb0  a news    movsx eax,word [eax] for the message                 ->  test eax,eax / je (ret) / push dword [eax]
```

Each in its own bytes: `pop ecx` twice instead of `add esp,8` makes room (`0x5bb160` reads the player's
number as a word). In GOG and the 2007 CD the places lie 0x22F0 bytes (`0x5bbcb0`: 0x2D30) further on.

**Referee grudge.** A referee's record (10 bytes at `0x9133e8 + id * 10`, read and written by `0x57d550` and
`0x57d590`) holds at `+7` the number of a club he holds a grudge against, or -1. The microphone (manager
event 36), two interview answers and the affair (event 39 with the manager events) set it; a match copies
it to `+0x22E` (`0x5f0120`). The cards (`0x5f3640`) compare it with the club's number and lower the threshold
by 10 in league matches. The scenes he decides (`0x404ec0`, at `0x404fa1` in all three builds) compare it with
the number plus one and skip 0, so they hit the club with the next lower number. The patch drops both:

```
mov ax,[ebx+22E] / test ax,ax / je / xor edx,edx / mov dl,[edi+102] / movsx eax,ax / inc edx / cmp eax,edx / jne
->  ... / test ax,ax / nop / nop / ... / nop / cmp eax,edx / jne
```

No grudge (-1) never equals a club's number (0 to 255).

**Appearances list.** Each human manager's club keeps two lists of 50 entries of 20 bytes (`0x793030` and
`0x79347c`, plus `0x4210` per manager): goals and appearances, each entry with the player's number (`+0`),
birth year (`+6`) and a flag "at the club" (`+8`). After a match `0x61ef70` counts the appearances
(`0x61ec50`): a player found gets his count raised and the flag 1, a new one takes an empty entry or, from
the bottom up to place 10, the first with flag 0; then the list is sorted. A player leaving
(`0x552d60` -> `0x61dc70`) clears his flag in the goals list only, so once 50 players have appeared the
appearances list never takes another one. The patch sets its flags anew before each count: 0, or 1 if the
number is one of the squad's (`[ebx+990]`, `[ebx+e6]` of them) or of the players lent out (`[ebx+a1c]`,
`[ebx+a19]`). Room comes from the sort, rewritten as a bubble sort (the same descending order) in 47 of
its 139 bytes (`0x61f0b1`); the routine goes in front of it and is called where the count starts:

```
movzx ax,byte [ebx+104] / add esp,8 / cmp ax,2 / mov [esp+10],eax / jle / mov dword [esp+10],2
->  add esp,8 / movzx eax,byte [ebx+104] / cmp al,2 / jbe / mov al,2 / mov [esp+10],eax / call flags / nop x4
```

Both places lie 179 bytes apart in every build (GOG and 2007 CD `0x623b8e`, `0x623c41`). A squad holds
at most 30 players, so the patch also keeps the first 20 places instead of 10: the search for a former
player's entry stops above place 21 (`0x61ed1e`, `cmp ax,0A` -> `cmp ax,14`; GOG and 2007 CD `0x6238ae`).

**Competence balance.** Once a year `0x5cc740` balances the competence of the trainers and of the managers
of each country group (lists of 6-byte entries, value at `+2`, written back to the persons afterwards):
with an average under 6, every tenth gets 1 or 2 points (at most 15), with one over 10 every tenth loses
some. The raise adds the points to the sum and counts the person at its old value again, but never stores
the new one; the cut does (`mov [edi],cx`). The patch stores it in the same 36 bytes (`0x5cc79a`, GOG and
2007 CD `0x5cfeca`):

```
mov dx,[edi] / add esp,4 / inc eax / movsx ecx,dx / movsx ebp,ax / add ebp,ecx / cmp ebp,15 / jle /
mov eax,15 / sub eax,edx / movsx eax,ax / add ebx,eax / inc word [esi+ecx*2]
->  movsx edx,[edi] / add esp,4 / inc eax / movsx eax,ax / add eax,edx / cmp eax,15 / jle / mov eax,15 /
    mov [edi],ax / sub eax,edx / add ebx,eax / movsx ecx,[edi] / inc word [esi+ecx*2]
```

**Grade average.** A player's match day byte (`+0x130`, one per match day) holds bit 7 for an appearance,
the mark in bits 2-4 and cards in bits 0-1; who plays less than 20 minutes gets mark 0 (`0x5f4bf0`). The
team statistics count an appearance only with a mark (`0x460f0b`), but the season average in the player's
details (`0x4fcc46`) and the fans' check for a hated player (`0x639fcd`, at least 4 marks, average 4.5 or
worse) test mark and cards together, so a short appearance with a card counts as mark 0. The patch tests
the mark alone in both (`test al,1F` -> `test al,1C`; GOG and 2007 CD `0x4fee76`, `0x63eb0d`).

**Grade chart.** The statistics' charts (`0x45ceb0`) load their texts from `RESDLL.dll` through `0x619c10`
(text number into a `CString`). The grade chart (case `0x17`, title 2426 "Notenschnitt") takes text 5800
"Formpunkte/6" for its axis, the form chart's; `RESDLL.dll` has no text for marks. The patch assigns
"Noten/6" from the exe instead, in the same 18 bytes at `0x461476` (GOG and 2007 CD `0x463536`, outside the
2007 release's CRC stretches). The string goes into the 9 NOPs behind the function's `ret`; the call pushes
the address behind it, which the `add` moves to the string, so nothing depends on where the code is loaded:

```
lea ecx,[esp+C] / push 16A8 / push ecx / call 0x619c10 / add esp,8
->  call $+5 / add dword [esp],5C / lea ecx,[esp+10] / call CString::operator=(const char*)
```

`CString::operator=` (`ret 4`) is the first call in `0x619c10`, which the next label (5801 "Woche") calls
too; the patch takes both targets from there, in either state.

### Texts

The text switches (`TextPatches.cs`, the files in `GameTexts.cs`) need no code of the exe and work the
same for every release, the packed 2007 CD exe included. Each change names its texts by number: strings
of `RESDLL.dll`, dialogs (caption and controls), event texts; the tips as a whole. In strings, dialogs and
tips a text is replaced as a whole word, so "Gruppe 1" leaves "Gruppe 10" alone and " DM" behind an amount
isn't "DM" alone (string 1147, the defensive midfielder). A switch is offered only if every text it
changes is in the original file; a `RESDLL.dll` that isn't the game's (the Euro patch of 2022, for one)
greys them out.

**Resources.** A longer text doesn't fit in place, so the changed string table blocks (16 strings each)
and dialog templates go into a section `.a2gtxt` added at the end of `RESDLL.dll`, read-only data, and
the resource directory points there. The section starts with `A2GPatchTexts\0`, the names of the switches
on, and for each moved resource where its data entry is and the address and size it had. Every switch
builds the section anew from the original resources for the switches that are on, in a fixed order (the
cup names before the typing errors, which share "UEFA - Pokals"); with none on, the section and the
entries go back and the file is the original to the byte. A dialog keeps every byte but its texts; items
are aligned to four bytes again behind a text that changed its length. Styles stay: the game tells its
picture buttons from labels by `WS_EX_CLIENTEDGE` (it makes the pressed picture only for those), which the
Euro patch's file lost on 35 controls, among them the stadium screen's buttons.

**Event texts.** `TEXTE\EreigGld.saq` (GOG and 2007 CD also `EreigGold.saq`): the length of the text block
and the number of texts (1384), then the block, each byte XORed with the stored byte before it. The game
reads the block only at exactly 0x1DB3A bytes. Each switch replaces whole texts and keeps the sum of their
lengths: "€" instead of "DM" gets a space at the end of its text; "Innenverteidiger" is six bytes longer
than "Manndecker", so the message about a new main position (text 906) becomes "… hat eine neue
Hauptposition gelernt: %s." with spaces behind it; the typing errors put the bytes they drop behind the
stadium message (905). `TEXTE\tips.txt` is read line by line, so its length is free.

**Quiz.** `TEXTE\fort.saq` (`QuizPatches.cs`, `QuizQuestions.cs`): the number of questions of each of the three levels, the
number of texts (two per question), the length of the text block, a table of where each text starts, then
the block, each byte inverted. A question is followed by its answers, separated by commas, the right one
first; the game shows it and two of the next three. The game takes the counts and sizes from the header
and makes room for them (`0x487800`), so texts may change length. The switch replaces whole questions it
finds with their answers, keeps their place and level and writes the file anew; taken back, it is the
original to the byte. The new questions go to the end of their level and are taken out again; both
switches change the file independently of each other.

Patch 1.01 knows GOG's event file and the GOG dialog styles know GOG's `RESDLL.dll` by SHA-256; both take
the text changes back before they hash, and the dialog styles put the switches' texts back over the
dialogs they change.

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
- new players get a second position in 20 % of the cases (`0x5a2690`); with "Nebenpositionen" on, its
  helper takes this place instead (see "Secondary positions")
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

**Click delay.** GOG's `ddraw.dll` ("GOG.com DirectX 1-7 wrapper", Direct3D 9) stores, where it sets up the
device (two code paths, file offsets `0x1cd90` and `0x1e50e`), bit 9 of `D3DCAPS9.PrimitiveMiscCaps`,
`D3DPMISCCAPS_CLIPTLVERTS`, and without it clips the vertices itself. Current drivers don't report the
bit; A3-Rework (a patch for Anstoss 3, whose GOG release has a wrapper with the same code) found every click
answered 2 to 3 seconds late with a Radeon RX 6800 XT. The patch stores 1 (`shr eax,9 / and eax,ebp` ->
`mov eax,1`). It is offered when the folder's `ddraw.dll` names GOG's wrapper and holds both places, all
original or all patched; the name stays, so the animation edge still finds the wrapper. Without it (another
`ddraw.dll`, e.g. WineD3D's) the tile is greyed out.

## msvfw32.dll (`dll/`)

A proxy like A2G-Scaler's `dsound.dll`: `msvfw32` is not a KnownDLL, so the copy in the program folder
wins the loader search (checked against the running 2007 `run.exe`, whose `MSVFW32.dll` otherwise
comes from SysWOW64). `dsound.dll` would work too, but A2G-Scaler uses that name.

- All 47 exports of the real `msvfw32.dll` are `jmp` stubs (ordinals kept in `msvfw32.def`). The real
  DLL is loaded lazily on the first call, never in `DllMain` (loader lock).
- Which patches it applies are four flags bytes (low byte first) after the marker `A2GPatchFlags` in its
  `.data`; A2G-Patch sets them when installing (1 goal limits, 2 arrow repeat, 4 video crash, 8 freeze,
  16 sound device, 32 disk warning, 64 squad limit, 128 display mode, 256 cup draw, 512 preseason
  calculation, 1024 patch 1.01, 2048 picture buttons, 4096 interviews, 8192 speeches, 16384 negotiation
  counters, 32768 investments, 65536 trainer supply, 131072 money limit, 262144 events, 524288 grade
  average, 1048576 competence balance, 2097152 event crashes, 4194304 fish poisoning, 8388608 appearances list, 16777216 manager events, 33554432 grade chart, 67108864 referee grudge, 134217728 sound format, 268435456 secondary positions; the
  build has all on). Older versions had two or three bytes; the NUL ending their string and its padding
  follow them, so they read as the same flags. The other GOG patches have no flag: they change files on disk, which
  the DLL doesn't. An installed copy that differs from the
  embedded one in more than these bytes is from another version and gets replaced.
- On load, a thread polls the host exe's image every 250 ms for up to two minutes until each enabled
  patch's code is in memory, then rewrites it (`VirtualProtect`, `FlushInstructionCache`). Code that
  is ambiguous or not as expected is left alone; in other programs (Editor.exe) nothing turns up.
- No C runtime (`/Zl /GS- /NODEFAULTLIB`). Linked with subsystem 5.01, the lowest the linker writes;
  `build.cmd` then sets operating system and subsystem version in the header to 5.0, so it loads on
  Windows 2000 and XP.

## Building

Needs Visual Studio (or Build Tools) with the "Desktop development with C++" workload and the VS 2022
MSBuild (the one of VS 2019 can't handle SDK 8 from `global.json`).

```
dll\build.cmd
msbuild A2G-Patch.csproj /restore /t:Rebuild /p:Configuration=Release /p:Platform=x86
```

`dll\msvfw32.dll` is not committed; the exe embeds it when it exists, so build it first. An exe built
without it can patch the original and GOG exes but not the 2007 CD release, and says so.

Windows 2000 build: `msbuild A2G-Patch.csproj /restore /t:Rebuild /p:Configuration=Release /p:Platform=x86 /p:Win2000=true`
compiles the same sources against .NET Framework 2.0 into `bin\Win2000\Release\` (own `obj\Win2000\`).
The folder `Compat\` supplies what .NET 2.0 lacks (LINQ, `Func`/`Action`, `HashSet`, the bit of `Task`
that applying the switches uses) and is only compiled into that build. Code using further .NET 3.5/4.0
APIs breaks it; the test and release workflows build it too. The tests run against the normal build.

The logo (`A2G-Patch.ico`, `site/favicon.ico`, `site/logo.png`) is drawn by `tools/MakeIcon.cs`; the
file's header says how to run it. It is not part of the build.

`V101Code.cs` and `dll/v101code.h` (the code of patch 1.01) are generated from `gog/a2v101.S` and
committed. After changing the source, run `python3 tools/make_v101.py`; it needs GNU binutils (`as`,
`ld`, `objcopy`, `nm`), e.g. on Linux, in WSL or MSYS2. It links the code twice, 0x1004 bytes apart, and
takes the relocations from the differences. `python3 tools/make_v101.py --from-cs` rewrites both files
from `V101Code.cs` without binutils.

`ManagerCode.cs` and `dll/managercode.h` (the code of the manager events) are generated from
`tools/manager_events.asm` and the builds' addresses and original bytes in `tools/manager_builds.py`, and
committed. After changing either, run `python tools/make_manager.py`; it needs `ml.exe` from Visual Studio's
x86 C++ tools (found like `dll\build.cmd` does, or set `MASM_EXE`) and links each build once to check that
the code fits.

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
