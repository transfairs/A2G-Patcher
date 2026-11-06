/* SPDX-License-Identifier: GPL-3.0-only */
/* The changes of patch 1.01 for run.exe of the GOG version (2007 release, built from the 1.00 source).
 *
 * Found by comparing run.exe with the 1.01 program and the 1999 CD release (which contains 1.01);
 * written anew here from what the changes do. tools/make_v101.py assembles this file at the
 * address of a new section of run.exe (0xcbd000) into V101Code.cs; A2G-Patch adds the section and
 * makes the places listed in Patch101 (GogPatches.cs) jump here. All other addresses are those of
 * run.exe. rand(n) is 0x64ad00 (cdecl, 0 <= result < n).
 */
.intel_syntax noprefix
.code32
.text
.globl _start
_start:

.equ RAND, 0x64ad00

/* --- 0x412dd0: player lists with counts per position -------------------------------------------
 * f(team, short count, record *out): for each of count entries, a list of players is fetched and
 * those not left out (0x5a7650) are written to out with a count per position. 1.00 wrote each player to the slot of its list index and
 * stored the length of the whole list, leaving gaps and a wrong count when players were left out.
 * 1.01 writes them one after the other and stores how many there are. Replaces the function. */
.equ P1, 0xa24          /* team (after 4 pushes and 0xa10 bytes of locals) */
.equ P2, 0xa28          /* short count */
.equ P3, 0xa2c          /* records, 0x274 bytes each */
.equ T, 0xa00
.equ N, 0xa04
.equ K, 0xa08
.globl fix_412dd0
fix_412dd0:
    push ebp
    push ebx
    push esi
    push edi
    sub esp, 0xa10                      /* [esp]: list (short count, ids from +0xa) */
    mov dword ptr [esp+T], 0
1:  movsx eax, word ptr [esp+P2]
    movsx ecx, word ptr [esp+T]
    cmp ecx, eax
    jge 9f
    mov ecx, [esp+T]
    mov edx, [esp+P1]
    lea eax, [esp]
    push ecx
    push edx
    push eax
    call 0x5830b0                       /* fill the list */
    add esp, 0xc
    mov eax, [esp+T]
    imul eax, eax, 0x274
    mov ebx, [esp+P3]
    add ebx, eax                        /* record */
    mov eax, [esp+P1]
    mov [ebx], ax
    mov dword ptr [esp+N], 0
    mov dword ptr [esp+K], 0
2:  movsx eax, word ptr [esp]
    movsx ecx, word ptr [esp+K]
    cmp ecx, eax
    jge 8f
    movsx esi, word ptr [esp+ecx*2+0xa] /* player id */
    push esi
    push dword ptr [esp+P1+4]
    call 0x5a7650                       /* left out? */
    add esp, 8
    test ax, ax
    jne 7f
    push 0xffff
    push esi
    push dword ptr [esp+P1+8]
    call 0x587da0                       /* player */
    add esp, 0xc
    mov esi, eax
    mov eax, [esp+N]
    imul eax, eax, 0x1a
    lea edi, [ebx+eax]                  /* next free entry, not the list index */
    movzx ax, byte ptr [esi+0x102]
    mov [edi+4], ax
    mov ax, [esi+0xe6]
    mov [edi+6], ax
    xor ecx, ecx
3:  cmp cx, word ptr [esi+0xe6]
    jge 4f
    movsx eax, cx
    mov eax, [esi+eax*4+0x990]
    movzx eax, byte ptr [eax+0x20]
    inc word ptr [edi+eax*2+8]
    inc ecx
    jmp 3b
4:  push 0xffff
    push esi
    call 0x586fe0                       /* release the player */
    add esp, 8
    inc dword ptr [esp+N]
7:  inc dword ptr [esp+K]
    jmp 2b
8:  mov eax, [esp+N]
    mov [ebx+2], ax                     /* count of the entries written */
    inc dword ptr [esp+T]
    jmp 1b
9:  add esp, 0xa10
    pop edi
    pop esi
    pop ebx
    pop ebp
    ret

/* --- 0x415da0: message 0xc7 only if word [object+8] > 47 (two places) ----------------------------
 * 1.01 adds this condition before the message. At the first place the object is in ebx, at the
 * second ebx has been reused and the object is the function's second argument at [esp+0x18]. */
.globl fix_415f96
fix_415f96:                             /* replaces: je 0x415fcb / mov cl,[esi+4] */
    je 1f
    cmp word ptr [ebx+8], 0x2f
    jle 1f
    mov cl, [esi+4]
    jmp 0x415f9b
1:  jmp 0x415fcb

.globl fix_4160bd
fix_4160bd:                             /* replaces: je 0x4160f1 / mov al,[esi] / push 1 */
    je 1f
    mov eax, [esp+0x18]
    cmp word ptr [eax+8], 0x2f
    jle 1f
    mov al, [esi]
    push 1
    jmp 0x4160c3
1:  jmp 0x4160f1

/* --- 0x557e30 ------------------------------------------------------------------------------------
 * 1.01 clears the block of counters at 0xc24fe4 (0x567ee0) on entry; the counter at +0x5e grows by one instead of
 * by the value at +0x60 (that part is patched in place by a2patch.py). */
.globl fix_557e3f
fix_557e3f:                             /* replaces: or byte ptr [esi+0xae],1 */
    or byte ptr [esi+0xae], 1
    call 0x567ee0
    jmp 0x557e46

/* --- 0x568ca0: overflow of five counters ---------------------------------------------------------
 * f(object, short index, int amount), thiscall (ecx = table, 0x50 bytes per entry), ret 0xc.
 * 1.01: the five counters of the entry must stay below 1,000,000, so the amount is cut to
 * 999,999 - sum; nothing happens if it is negative. Otherwise as 1.00. Replaces the function. */
.globl fix_568ca0
fix_568ca0:
    push ebx
    push ebp
    push esi
    push edi
    sub esp, 8                          /* [esp]: double */
    mov ebx, ecx
    movsx edi, word ptr [esp+0x20]      /* index */
    lea esi, [edi+edi*4]
    shl esi, 4
    add esi, ebx                        /* entry */
    mov eax, [esi+0xc]
    add eax, [esi+0x10]
    add eax, [esi+0x14]
    add eax, [esi+0x18]
    add eax, [esi+0x1c]
    mov ebp, [esp+0x24]                 /* amount */
    lea ecx, [eax+ebp]
    cmp ecx, 1000000
    jl 1f
    mov ebp, 999999
    sub ebp, eax
1:  test ebp, ebp
    jl 9f
    mov [esp+0x24], ebp
    movsx eax, word ptr [esi+2]
    imul esi, eax, 28
    fild dword ptr [esi+0x6c0f40]
    fimul dword ptr [esp+0x24]
    fmul dword ptr [0x6c1fc0]
    fmul qword ptr [0x69fef8]
    fstp qword ptr [esp]
    push ebp
    call 0x568700
    add esp, 4
    mov ecx, 100
    sub ecx, eax
    mov [esp+0x20], ecx
    fild dword ptr [esp+0x20]
    fmul qword ptr [esp]
    fmul qword ptr [0x69fef8]
    call 0x65b4e0                       /* float -> int */
    mov ecx, [esp+0x1c]                 /* object */
    add [ecx+0x47c], eax
    movsx edx, word ptr [esi+0x6c0f50]
    lea eax, [edi+edi*4]
    lea ecx, [edx+eax*4]
    add [ebx+ecx*4+0xc], ebp
9:  add esp, 8
    pop edi
    pop esi
    pop ebp
    pop ebx
    ret 0xc

/* --- 0x5a0b70: result of a dialog ----------------------------------------------------------------
 * 1.01 passes one more value into the dialog's data, tells the dialog (flag at +0x16a34, which the
 * dialog code of run.exe already handles) to return its result and stores that in the team (ebx): +0x14d4 word, +0x14d8 dword, +0x14d6 word,
 * +0x14dc dword; without a result (+0x14d4 = 0) the second pair moves to the first. */
.globl fix_5a0bf7
fix_5a0bf7:                             /* replaces: push 0 / lea ecx,[esp+0x378] */
    mov ax, [esp+0x4a]
    mov [esp+0x82], ax
    push 0
    lea ecx, [esp+0x378]
    jmp 0x5a0c00

.globl fix_5a0f95
fix_5a0f95:                             /* replaces: call 0x66dfb0 (DoModal) */
    mov dword ptr [esp+0x19490], 1
    call 0x66dfb0
    mov cx, [esp+0x1949c]
    mov [ebx+0x14d4], cx
    mov edx, [esp+0x1949e]
    mov [ebx+0x14d8], edx
    mov ax, [esp+0x194a2]
    mov [ebx+0x14d6], ax
    mov ecx, [esp+0x194a4]
    mov [ebx+0x14dc], ecx
    cmp word ptr [ebx+0x14d4], 0
    jne 1f
    mov [ebx+0x14d4], ax
    mov [ebx+0x14d8], ecx
    mov word ptr [ebx+0x14d6], 0
    mov byte ptr [ebx+0x14dc], 0
1:  jmp 0x5a0f9a

/* --- 0x5a2690: second position of a new player --------------------------------------------------
 * After the alternative position (40 %), 1.01 gives a field player a second position in 20 % of
 * the cases: position 4/5/6/7 -> 7/6/5/4, positions 2, 3 and 8 to 10 a random one from 1 to 10;
 * it is cleared if it equals the main or the alternative position. */
.globl fix_5a2836
fix_5a2836:                             /* replaces: the 14 bytes setting the alternative position */
    xor ecx, ecx
    mov cl, [esi+0x20]
    mov dl, [ecx+0x6c5a58]
    mov [esi+0x21], dl
    push 100
    call RAND
    add esp, 4
    cmp eax, 20
    jae 9f
    movzx eax, byte ptr [esi+0x20]
    sub eax, 2
    cmp eax, 8
    ja 5f
    cmp eax, 2
    je 1f
    cmp eax, 3
    je 2f
    cmp eax, 4
    je 3f
    cmp eax, 5
    je 4f
    push 10
    call RAND
    add esp, 4
    inc al
    mov [esi+0x22], al
    jmp 5f
1:  mov byte ptr [esi+0x22], 7
    jmp 5f
2:  mov byte ptr [esi+0x22], 6
    jmp 5f
3:  mov byte ptr [esi+0x22], 5
    jmp 5f
4:  mov byte ptr [esi+0x22], 4
5:  mov al, [esi+0x22]
    cmp al, [esi+0x20]
    jne 6f
    mov byte ptr [esi+0x22], 0
6:  mov al, [esi+0x22]
    cmp al, [esi+0x21]
    jne 9f
    mov byte ptr [esi+0x22], 0
9:  jmp 0x5a2844

/* --- 0x5a2fc0: traits of a new player -----------------------------------------------------------
 * f(player). For goalkeepers (type 1) 1.00 always set exactly one positive trait and skipped the
 * rest. 1.01 treats them like field players, with 5 instead of 11 possible traits: a positive one
 * in 30 %, a negative one in 10 % of the cases, then the common part. Replaces the function. */
.globl fix_5a2fc0
fix_5a2fc0:
    push esi
    mov esi, [esp+8]
    push edi
    xor edi, edi
    mov al, [esi+0x20]
    mov [esi+0x28], di                  /* positive traits */
    mov [esi+0x2a], di                  /* negative traits */
    mov [esi+0x2c], di
    cmp al, 1
    jne 2f
    push 100                            /* goalkeeper */
    call RAND
    add esp, 4
    cmp eax, 30
    jae 1f
    push 5
    call RAND
    add esp, 4
    mov ecx, eax
    mov edx, 1
    shl edx, cl
    or [esi+0x28], dx
1:  push 100
    call RAND
    add esp, 4
    cmp eax, 10
    jae 5f
    push 5
    jmp 4f
2:  push 100                            /* field player */
    call RAND
    add esp, 4
    cmp eax, 30
    jae 3f
    push 11
    call RAND
    add esp, 4
    mov ecx, eax
    mov edx, 1
    shl edx, cl
    or [esi+0x28], dx
3:  push 100
    call RAND
    add esp, 4
    cmp eax, 10
    jae 31f
    push 11
    call RAND
    add esp, 4
    mov ecx, eax
    mov edx, 1
    shl edx, cl
    or [esi+0x2a], dx
31: push 100
    call RAND
    add esp, 4
    cmp eax, 10
    jae 5f
    push 11
4:  call RAND
    add esp, 4
    mov ecx, eax
    mov edx, 1
    shl edx, cl
    or [esi+0x2a], dx
5:  mov ax, [esi+0x28]                  /* common part */
    not ax
    and [esi+0x2a], ax
    push 100
    call RAND
    add esp, 4
    cmp eax, 30
    jae 6f
    push 4
    call RAND
    add esp, 4
    mov ecx, eax
    inc ecx
    mov edx, 1
    shl edx, cl
    or [esi+0x2c], dx
6:  mov [esi+0x2e], di
    push 100
    call RAND
    add esp, 4
    cmp ax, 10
    jge 7f
    or byte ptr [esi+0x2f], 1
    pop edi
    pop esi
    ret
7:  cmp ax, 20
    jge 8f
    or byte ptr [esi+0x2f], 2
8:  pop edi
    pop esi
    ret

/* --- 0x5bd540: reordering the human managers -------------------------------------------------
 * f(short *order): copies the data blocks of the managers (count at 0x8297a6) and writes them back
 * in the order given by order[1..count]. 1.00 leaves out the block at 0x7afd70 (0x160 bytes per
 * manager), so it stays with the old order. 1.01 reorders it as well. Wraps the function: the
 * block is saved, the original runs, then the block is written back in the new order. */
.globl fix_5bd540
fix_5bd540:
    push esi
    push edi
    push ecx
    mov esi, 0x7afd70
    mov edi, offset saved_7afd70
    mov ecx, 0x160
    rep movsd
    pop ecx
    pop edi
    pop esi
    push dword ptr [esp+4]              /* order */
    call 1f                             /* the original function */
    add esp, 4
    push ebx
    push esi
    push edi
    mov ebx, [esp+0x10]                 /* order */
    xor edx, edx                        /* manager index - 1 */
2:  movsx eax, word ptr [0x8297a6]
    cmp edx, eax
    jge 3f
    movsx eax, word ptr [ebx+edx*2+2]   /* order[index] */
    dec eax
    imul esi, eax, 0x160
    add esi, offset saved_7afd70
    imul edi, edx, 0x160
    add edi, 0x7afd70
    mov ecx, 0x58
    rep movsd
    inc edx
    jmp 2b
3:  pop edi
    pop esi
    pop ebx
    ret
1:  mov eax, 0x9f6c                     /* replaced: mov eax,0x9f6c */
    jmp 0x5bd545

    .balign 4
saved_7afd70:
    .space 0x580
