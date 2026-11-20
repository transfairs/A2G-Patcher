; SPDX-License-Identifier: GPL-3.0-only
; The manager's private life (FUN_0055c900 in the original): code for the cases it draws but has
; none for (13, 21, 35, 39) or that don't work (25, 34). tools/make_manager.py assembles this file
; (MASM) into ManagerCode.cs and dll/managercode.h. The patch (ManagerEvents in Patches.cs, and
; msvfw32.dll) puts each segment into a function of the game nothing refers to: MAIN, followed by
; three questions for the dialog (which reaches them there), into FUN_0056e820 (a news item never
; drawn, 1216 bytes with the padding), KEYS into FUN_00603cd0 (debug keys of the match, 640 bytes),
; TABS into FUN_005b45c0 (tab stops of a list box, 400 bytes). The switch's jump table then leads to
; E13 ... E39.
;
; The extern names are the game's functions and data, found by byte patterns (make_manager.py,
; SYMBOLS). All game functions are cdecl, except TextOf (thiscall: ecx = Texts, callee pops).
; Q13, Q21 and Q35 are the question numbers of the three questions (negative: they lie below the
; game's question table).
;
; Each E.. is entered by the jump table inside FUN_0055c900's frame: esi and edi are saved there, its
; 0x7E0 bytes of locals free. E.. saves ebp and ebx and returns from FUN_0055c900 through Exit0 or Exit1
; (the event happened or not).

.386
.model flat
option casemap:none

EXTERN ShowText:NEAR, ShowString:NEAR, ShowTwo:NEAR, ShowTextArg:NEAR, Ask:NEAR, TextOf:NEAR
EXTERN Sprintf:NEAR, Money:NEAR, Standing:NEAR, Random:NEAR, Open:NEAR, Gets:NEAR, Trim:NEAR, Close:NEAR
EXTERN LoadClub:NEAR, ClubName:NEAR, FreeClub:NEAR, SaveClub:NEAR, StockBlock:NEAR
EXTERN HasLast:NEAR, LastRef:NEAR, RefGet:NEAR, RefPut:NEAR, RefName:NEAR
EXTERN Humans:NEAR, SameClub:NEAR, MgrName:NEAR, Matchday:NEAR, ClubCond:NEAR, PlayerForm:NEAR, SomeForm:NEAR
EXTERN Texts:BYTE, Journalists:BYTE, CountrySlot:BYTE, StockOption:BYTE
EXTERN Money10k:DWORD, Money50k:DWORD, StashA:DWORD, StashB:DWORD, StashC:DWORD, VictimManager:WORD, DataDir:DWORD
EXTERN Q13:ABS, Q21:ABS, Q35:ABS

PUBLIC E13, E21, E25, E34, E35, E39, FormatCaller, FormatPlayer, Question13, Question21, Question35

; ebp points 80h bytes into FUN_0055c900's locals (they start at ebp-70h), so the variables and the
; small buffers are in reach of a one-byte displacement.
CLUB    EQU <DWORD PTR [ebp+774h]>
MANAGER EQU <DWORD PTR [ebp+778h]>
V0      EQU -70h            ; dword variables
V1      EQU -6Ch
SCORE   EQU -68h            ; case 35: three scores, then five players
BEST    EQU -5Ch
B4      EQU -48h            ; 32 bytes
B5      EQU -28h            ; 32 bytes
B1      EQU -8h             ; 64 bytes
B3      EQU 38h             ; 128 bytes
B2      EQU 0B8h            ; 200 bytes, up to ebp+180h
PARTS   EQU 0F7FFh          ; the parts of a club's data LoadClub copies
PRIVATE EQU 5Eh             ; message senders: the private life, another club
OTHER   EQU 5Bh

ENTER_CASE MACRO
    push ebp
    push ebx
    lea ebp, [esp+80h]
ENDM

MAIN SEGMENT BYTE PUBLIC FLAT 'CODE'

; ---- returning from FUN_0055c900 ---------------------------------------------------------------------------
Exit1:
    mov eax, 1
    jmp short Exit
Exit0:
    xor eax, eax
Exit:
    lea esp, [ebp-80h]
    pop ebx
    pop ebp
    pop edi
    pop esi
    add esp, 7E0h
    ret

; ---- case 21, text 514: a caller offers to wreck another human manager's office for a fee from the
; manager's own money. Yes: "Kein Problem." (517, the dialog), the other club learns of the damage (515);
; half the time the insurance pays (516), else the club.
E21:
    ENTER_CASE
    mov esi, CLUB
    mov edi, MANAGER
    lea eax, [ebp+B1]
    push eax
    call Humans                     ; 4 entries of 8 bytes: +0 country, +2 number
    pop ecx
    movsx eax, ax
    mov [ebp+V0], eax
    xor ebx, ebx                    ; the others, their indices at B2
    and DWORD PTR [ebp+V1], 0
c21_other:
    mov eax, [ebp+V1]
    cmp eax, [ebp+V0]
    jge c21_drawn
    lea eax, [ebp+B1+eax*8]
    push esi
    push eax
    call SameClub
    add esp, 8
    test eax, eax
    jnz c21_next
    mov eax, [ebp+V1]
    mov BYTE PTR [ebp+B2+ebx], al
    inc ebx
c21_next:
    inc DWORD PTR [ebp+V1]
    jmp c21_other
c21_drawn:
    test ebx, ebx
    jz Exit0
    push ebx
    call Random
    pop ecx
    movzx eax, BYTE PTR [ebp+B2+eax]
    lea eax, [ebp+B1+eax*8]
    movzx ecx, WORD PTR [eax+2]
    movzx eax, BYTE PTR [eax]
    push PARTS
    push ecx
    push eax
    call LoadClub
    add esp, 12
    test eax, eax
    jz Exit0
    mov ebx, eax                    ; the other club
    movzx eax, BYTE PTR [ebx+87h]   ; its human manager
    test eax, eax
    jz c21_free0
    mov ecx, Money10k
    cmp [edi+90h], ecx              ; the manager's own money
    jl c21_free0
    mov StashA, ecx
    mov VictimManager, ax
    push Q21
    push esi
    push edi
    call Ask
    add esp, 12
    test ax, ax
    jnz c21_free1
    mov eax, StashA
    sub [edi+90h], eax
    push Money50k
    call Money
    pop ecx
    push eax
    push 203h
    mov ecx, OFFSET Texts
    call TextOf
    push eax
    lea eax, [ebp+B3]
    push eax
    call Sprintf
    add esp, 12
    push 2
    call Random
    pop ecx
    test eax, eax
    jnz c21_pay
    push 204h
    mov ecx, OFFSET Texts
    call TextOf
    push eax
    lea eax, [ebp+B3]
    push eax
    push 3
    push OTHER
    push ebx
    call ShowTwo
    add esp, 20
    jmp short c21_save
c21_pay:
    mov eax, Money50k
    add [ebx+38Ch], eax             ; other costs
    lea eax, [ebp+B3]
    push 2
    push eax
    push OTHER
    push ebx
    call ShowString
    add esp, 16
c21_save:
    push PARTS
    movzx eax, BYTE PTR [ebx+102h]
    push eax
    mov al, [ebx+103h]
    push eax
    push ebx
    call SaveClub
    jmp Exit1
c21_free1:
    push PARTS
    push ebx
    call FreeClub
    jmp Exit1
c21_free0:
    push PARTS
    push ebx
    call FreeClub
    jmp Exit0

; ---- case 34, text 530: the journalist of the club's country who can't stand the manager pays him
; respect. His mood towards each human manager (+30h + number - 1) is 0 neutral, 1 friendly, 4 and above
; (insults store -1 to -3) hostile; the most hostile one turns friendly. The game's conditions are kept.
E34:
    ENTER_CASE
    mov esi, CLUB
    push esi
    call Matchday
    pop ecx
    cmp ax, 10
    jle Exit0
    push esi
    call ClubCond
    pop ecx
    cmp ax, 2
    jle Exit0
    movzx eax, BYTE PTR [esi+103h]
    movzx eax, CountrySlot[eax]
    imul eax, eax, 19EAh
    add eax, OFFSET Journalists
    mov edi, MANAGER
    movzx edx, WORD PTR [edi+1Ah]
    add eax, edx                    ; eax+2Fh: the first journalist's mood
    xor ebx, ebx
    mov edx, 37Fh                   ; dh: three journalists, dl: the lowest mood (signed)
c34_mood:
    mov cl, [eax+2Fh]
    cmp cl, 4
    jb c34_calm
    cmp cl, dl
    jge c34_calm
    mov dl, cl
    mov ebx, eax
c34_calm:
    add eax, 34h
    dec dh
    jnz c34_mood
    test ebx, ebx
    jz Exit0
    movzx eax, WORD PTR [edi+1Ah]
    sub ebx, eax                    ; the journalist: +0 outlet, +10h last name
    call FirstName
    lea eax, [ebp+B1]
    mov ecx, OFFSET Space
    cmp BYTE PTR [eax], 0
    jne c34_named
    inc ecx                         ; no first name: no space
c34_named:
    lea edx, [ebx+10h]
    push edx
    push ecx
    push eax
    push ebx
    push OFFSET Reporter
    lea eax, [ebp+B3]
    push eax
    call Sprintf
    add esp, 24
    lea eax, [ebp+B3]
    push 1
    push eax
    push 212h
    push PRIVATE
    push esi
    call ShowTextArg
    add esp, 20
    movzx eax, WORD PTR [edi+1Ah]
    mov BYTE PTR [ebx+eax+2Fh], 1
    jmp Exit1

; The first name of the journalist ebx into B1 from KLEINIG.SAV, which the game reads but doesn't keep:
; the line behind his outlet and last name. Empty if the file doesn't hold them (another data set).
FirstName:
    push esi
    push edi
    mov BYTE PTR [ebp+B1], 0
    mov BYTE PTR [ebp+B4], 0
    mov BYTE PTR [ebp+B5], 0
    push DataDir
    push OFFSET Kleinig
    lea eax, [ebp+B2]
    push eax
    call Sprintf
    add esp, 12
    push OFFSET ReadMode
    lea eax, [ebp+B2]
    push eax
    call Open
    add esp, 8
    test eax, eax
    jz first_done
    mov edi, eax
first_line:
    push edi
    push 0C8h
    lea eax, [ebp+B2]
    push eax
    call Gets
    add esp, 12
    test eax, eax
    jz first_close
    lea eax, [ebp+B2]
    push eax
    call Trim
    pop ecx
    mov esi, eax
    lea eax, [ebp+B4]               ; two lines back: the outlet?
    mov edx, ebx
    call Equal
    jne first_shift
    lea eax, [ebp+B5]               ; one line back: the last name?
    lea edx, [ebx+10h]
    call Equal
    jne first_shift
    mov eax, esi
    lea edx, [ebp+B1]
    call Copy
    jmp short first_close
first_shift:
    lea eax, [ebp+B5]
    lea edx, [ebp+B4]
    call Copy
    mov eax, esi
    lea edx, [ebp+B5]
    call Copy
    jmp first_line
first_close:
    push edi
    call Close
    pop ecx
first_done:
    pop edi
    pop esi
    ret

; ZF set if the strings at eax and edx are equal (uses cl).
Equal:
    mov cl, [eax]
    cmp cl, [edx]
    jne equal_out
    inc eax
    inc edx
    test cl, cl
    jnz Equal
equal_out:
    ret

; Copies the string at eax to edx, at most 30 characters and the terminating zero (uses ecx).
Copy:
    push 1Eh
    pop ecx
copy_char:
    mov ch, [eax]
    test ch, ch
    jz copy_end
    mov [edx], ch
    inc eax
    inc edx
    dec cl
    jnz copy_char
copy_end:
    mov BYTE PTR [edx], 0
    ret

; ---- case 13, text 503: should the standing room go? Yes angers the fans (504), no pleases fans and
; public but not the board (505); the dialog shows the answer's text.
E13:
    ENTER_CASE
    push Q13
    push CLUB
    push MANAGER
    call Ask
    add esp, 12
    push MANAGER
    test ax, ax
    jnz c13_no
    push 0
    push 0
    push -10
    push 0
    jmp short c13_standing
c13_no:
    push -5
    push 0
    push 5
    push 5
c13_standing:
    call Standing
    jmp Exit1

Kleinig  DB "%s\kleinig.sav", 0
ReadMode DB "r", 0
Reporter DB "%s-Reporter %s%s%s", 0
FullName DB "%s %s", 0
Space    DB " ", 0
MAIN ENDS

KEYS SEGMENT BYTE PUBLIC FLAT 'CODE'

; ---- case 35, text 531: who was the team's player of the month? The three with the best average mark
; over the last four match days (two marks at least) are the answers, in random order. The best one gains
; three form points (532), else some players lose one (533).
E35:
    ENTER_CASE
    mov esi, CLUB
    push esi
    call Matchday
    pop ecx
    movsx eax, ax
    mov [ebp+V0], eax
    sub eax, 4
    mov [ebp+V1], eax               ; the oldest match day counted
    lea edi, [ebp+SCORE]
    push 3
    pop ecx
    or eax, -1
    rep stosd                       ; scores: none yet
    push 3
    pop ecx
    xor eax, eax
    rep stosd                       ; players: none yet
    movsx edi, WORD PTR [esi+0E6h]
c35_player:
    dec edi
    js c35_ranked
    mov ebx, [esi+990h+edi*4]
    mov ecx, [ebp+V0]
    xor eax, eax                    ; al: the sum of marks, ah: their count
c35_day:
    dec ecx
    js c35_counted
    cmp ecx, [ebp+V1]
    jl c35_counted
    movzx edx, BYTE PTR [ebx+ecx+130h]
    test dl, 80h                    ; played
    jz c35_day
    and edx, 1Ch                    ; with a mark
    jz c35_day
    shr edx, 2
    inc dh
    add eax, edx
    jmp c35_day
c35_counted:
    cmp ah, 2
    jb c35_player
    movzx ecx, ah
    movzx eax, al
    imul eax, eax, 12
    cdq
    div ecx                         ; the average, times 12
    mov ecx, 2
c35_insert:
    cmp eax, [ebp+SCORE+ecx*4]
    jae c35_player
    cmp ecx, 2
    je c35_place
    mov edx, [ebp+SCORE+ecx*4]
    mov [ebp+SCORE+4+ecx*4], edx
    mov edx, [ebp+BEST+ecx*4]
    mov [ebp+BEST+4+ecx*4], edx
c35_place:
    mov [ebp+SCORE+ecx*4], eax
    mov [ebp+BEST+ecx*4], ebx
    dec ecx
    jns c35_insert
    jmp c35_player
c35_ranked:
    cmp DWORD PTR [ebp+BEST+8], 0
    je Exit0
    push 3
    call Random
    pop ecx
    mov ecx, [ebp+BEST]
    mov [ebp+BEST+12], ecx
    mov ecx, [ebp+BEST+4]
    mov [ebp+BEST+16], ecx
    lea ecx, [ebp+BEST+eax*4]       ; the buttons: players r, r+1, r+2
    mov edx, [ecx]
    mov StashA, edx
    mov edx, [ecx+4]
    mov StashB, edx
    mov edx, [ecx+8]
    mov StashC, edx
    neg eax
    add eax, 3
    cmp al, 3
    jne c35_button
    xor eax, eax
c35_button:
    mov edi, eax                    ; the best one's button
    push Q35
    push esi
    push MANAGER
    call Ask
    add esp, 12
    movsx eax, ax
    cmp eax, edi
    jne c35_wrong
    push 3
    push DWORD PTR [ebp+BEST]
    call PlayerForm
    add esp, 8
    push 1
    push 214h
    jmp short c35_tell
c35_wrong:
    push -1
    push esi
    call SomeForm
    add esp, 8
    push 2
    push 215h
c35_tell:
    push PRIVATE
    push esi
    call ShowText
    jmp Exit1

; ---- the questions' formatters, cdecl f(dest, manager, club, text) -> 1, called by the dialog ------------

; Text 514 with the fee and the other manager's name.
FormatCaller:
    sub esp, 40h
    mov eax, esp
    push eax
    movsx eax, VictimManager
    push eax
    call MgrName
    add esp, 8
    mov eax, esp
    push eax
    push StashA
    call Money
    pop ecx
    push eax
    push DWORD PTR [esp+58h]        ; text
    mov ecx, OFFSET Texts
    call TextOf
    push eax
    push DWORD PTR [esp+50h]        ; dest
    call Sprintf
    add esp, 50h
    mov eax, 1
    ret

; The buttons of question 35 (texts 1 to 3): the players' names.
FormatPlayer:
    mov eax, [esp+10h]
    mov ecx, StashA
    dec eax
    jz fplayer_player
    mov ecx, StashB
    dec eax
    jz fplayer_player
    mov ecx, StashC
fplayer_player:
    lea eax, [ecx+0Ch]
    push eax
    lea eax, [ecx+2]
    push eax
    push OFFSET FullName
    push DWORD PTR [esp+10h]
    call Sprintf
    add esp, 16
    mov eax, 1
    ret

; ---- case 39, text 537: an affair with the wife of the referee of the club's last league match; he holds
; a grudge against the club (+7: more cards in league matches), as after the microphone (case 36).
E39:
    ENTER_CASE
    mov esi, CLUB
    push esi
    call HasLast
    pop ecx
    test eax, eax
    jz Exit0
    lea eax, [ebp+V0]
    and DWORD PTR [eax], 0
    push eax
    push esi
    call LastRef
    add esp, 8
    test eax, eax
    jz Exit0
    movzx edi, WORD PTR [ebp+V0]
    lea ebx, [ebp+B1]
    push ebx
    push edi
    call RefGet
    add esp, 8
    mov al, [esi+102h]
    cmp al, [ebx+7]
    je Exit0
    movsx eax, WORD PTR [ebx+2]
    push eax
    call RefName
    pop ecx
    push 2
    push eax
    push 219h
    push PRIVATE
    push esi
    call ShowTextArg
    add esp, 20
    mov al, [esi+102h]
    mov [ebx+7], al
    push ebx
    push edi
    call RefPut
    jmp Exit1
KEYS ENDS

TABS SEGMENT BYTE PUBLIC FLAT 'CODE'

; ---- case 25, text 521: an aunt leaves the manager 100 shares of the club's arch rival (+0F4h), bought
; at 0 DM. Only with the stock market on, a free slot (or one holding the rival) and within 10 % of its
; shares. The manager's six slots (+0D8h, 10h bytes): +0 country, +2 club, +4 shares, +8 average price.
E25:
    ENTER_CASE
    mov esi, CLUB
    mov edi, MANAGER
    cmp StockOption, 0
    je Exit0
    movzx ebx, BYTE PTR [esi+0F4h]
    cmp bl, 0FFh
    je Exit0
    movzx eax, BYTE PTR [esi+103h]
    push PARTS
    push ebx
    push eax
    call LoadClub
    add esp, 12
    test eax, eax
    jz Exit0
    mov [ebp+V0], eax
    cmp BYTE PTR [eax+115h], 0      ; listed
    je c25_free
    lea ecx, [ebp+B1]
    push eax
    push ecx
    call ClubName
    lea ecx, [ebp+B2]
    push ecx
    push ebx
    movzx eax, BYTE PTR [esi+103h]
    push eax
    call StockBlock                 ; B2: the shares issued
    push PARTS
    push DWORD PTR [ebp+V0]
    call FreeClub
    add esp, 28
    lea edx, [edi+0D8h]
    xor edi, edi                    ; the first free slot
    mov al, [esi+103h]
    push 6
    pop ecx
c25_slot:
    cmp [edx], al
    jne c25_other
    cmp [edx+2], bx
    je c25_held
c25_other:
    test edi, edi
    jnz c25_next
    cmp BYTE PTR [edx], 0
    jne c25_next
    cmp WORD PTR [edx+2], 0
    jne c25_next
    mov edi, edx
c25_next:
    add edx, 10h
    loop c25_slot
    test edi, edi                   ; the free slot, none held (ecx 0)
    jz Exit0
    jmp short c25_limit
c25_held:
    mov edi, edx
    mov ecx, [edi+4]
c25_limit:
    lea eax, [ecx+100]
    imul eax, eax, 10
    cmp eax, [ebp+B2]
    jg Exit0
    cmp BYTE PTR [edi], 0
    jne c25_add
    mov al, [esi+103h]
    mov [edi], al
    mov [edi+2], bx
    and DWORD PTR [edi+4], 0
    and DWORD PTR [edi+8], 0
c25_add:
    movsx eax, WORD PTR [edi+8]
    imul eax, ecx
    lea ebx, [ecx+100]
    cdq
    idiv ebx
    mov [edi+8], ax
    mov [edi+4], ebx
    lea eax, [ebp+B1]
    push 1
    push eax
    push 209h
    push PRIVATE
    push esi
    call ShowTextArg
    jmp Exit1

c25_free:
    push PARTS
    push eax
    call FreeClub
    jmp Exit0
TABS ENDS

; ---- ASKS, the questions: entries of the game's question table (2Ch bytes each). The patch puts them behind MAIN
; at the first address the dialog reaches as an entry (table + number * 2Ch, the number a short): Q13, Q21
; and Q35 are their numbers in this order. Each: the dialog (4E34h), the question's text, the buttons'
; texts (0: none), the text the dialog shows after each button (0: none), a formatter for the question
; and one for the buttons (0: the plain text), a callback (none).
ASKS SEGMENT DWORD PUBLIC FLAT 'DATA'
Question13 DD 4E34h, 503, 113h, 114h, 0, 504, 505, 0, 0, 0, 0                    ; "Ja, klar" / "Natürlich nicht"
Question21 DD 4E34h, 514, 113h, 114h, 0, 517, 0, 0, OFFSET FormatCaller, 0, 0
Question35 DD 4E34h, 531, 1, 2, 3, 0, 0, 0, 0, OFFSET FormatPlayer, 0             ; the buttons: three players
ASKS ENDS


END
