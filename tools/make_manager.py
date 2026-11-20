#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-only
"""Assembles tools/manager_events.asm (the manager's private life) into ManagerCode.cs and dll/managercode.h.

    python tools/make_manager.py

Needs ml.exe of Visual Studio's x86 C++ tools, found through vswhere like dll/build.cmd does, or
given in the environment variable MASM_EXE (ml.exe itself reads the variable ML as options).

The object file's segments become blocks (MAIN, ASKS, KEYS, TABS), each with the fields to fill
in: an address in a block or of one of the game's symbols (absolute), or the distance to one
(relative, a call or jump). ManagerEvents (Patches.cs) and msvfw32.dll put MAIN and behind it the
ASKS (the questions) into the first of three functions of the game that nothing refers to, KEYS and TABS into the
other two, and fill in the fields with the addresses of the build (tools/manager_builds.py; msvfw32.dll
knows only the 2007 CD release's). This script links each build once to check that
the blocks fit. Not part of the build: both files are committed, run this after changing
manager_events.asm or manager_builds.py.
"""
import base64
import os
import struct
import subprocess
import sys
import tempfile

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(ROOT, "tools"))
from manager_builds import BUILDS  # noqa: E402

SOURCE = os.path.join(ROOT, "tools", "manager_events.asm")
TARGET = os.path.join(ROOT, "ManagerCode.cs")
HEADER = os.path.join(ROOT, "dll", "managercode.h")
BLOCKS = ["MAIN", "ASKS", "KEYS", "TABS"]
REGION = [0, 0, 1, 2]                       # the function each block goes into
QUESTIONS = ["Q13", "Q21", "Q35"]           # the questions' numbers, in the order of the ASKS block
QUESTION_SIZE = 0x2C
ENTRIES = [(13, "E13"), (21, "E21"), (25, "E25"), (34, "E34"), (35, "E35"), (39, "E39")]
DIR32, REL32 = 6, 0x14
FILL = 0xCC                                 # int3 in the rest of each function


def find_ml():
    if os.environ.get("MASM_EXE"):
        return os.path.normpath(os.environ["MASM_EXE"])
    vswhere = os.path.join(os.environ.get("ProgramFiles(x86)", r"C:\Program Files (x86)"),
                           "Microsoft Visual Studio", "Installer", "vswhere.exe")
    out = subprocess.run([vswhere, "-latest", "-products", "*", "-requires",
                          "Microsoft.VisualStudio.Component.VC.Tools.x86.x64", "-property", "installationPath"],
                         check=True, capture_output=True, text=True).stdout.strip()
    tools = os.path.join(out, "VC", "Tools", "MSVC")
    version = sorted(os.listdir(tools))[-1]
    return os.path.join(tools, version, "bin", "Hostx86", "x86", "ml.exe")


def read_coff(path):
    """Sections (name, data, relocations (offset, symbol index, type)) and symbols of a COFF object."""
    d = open(path, "rb").read()
    _, count, _, symbols_at, symbol_count, optional, _ = struct.unpack_from("<HHIIIHH", d, 0)
    strings = symbols_at + symbol_count * 18

    def name(raw):
        if raw[:4] == b"\0\0\0\0":
            at = strings + struct.unpack_from("<I", raw, 4)[0]
            return d[at:d.index(b"\0", at)].decode()
        return raw.rstrip(b"\0").decode()

    sections = []
    for i in range(count):
        at = 20 + optional + i * 40
        size, raw, relocations_at, _, relocation_count = struct.unpack_from("<IIIIH", d, at + 16)
        relocations = [struct.unpack_from("<IIH", d, relocations_at + k * 10) for k in range(relocation_count)]
        sections.append((name(d[at:at + 8]), d[raw:raw + size], relocations))
    symbols = {}
    i = 0
    while i < symbol_count:
        at = symbols_at + i * 18
        value, section, _, kind, aux = struct.unpack_from("<IhHBB", d, at + 8)
        symbols[i] = (name(d[at:at + 8]), value, section, kind)
        i += 1 + aux
    return sections, symbols


def assemble():
    """The blocks (code with the fields zeroed), their fields (offset, relative, target, addend), the
    symbols (the game's, then the questions' numbers) and the labels of each block."""
    with tempfile.TemporaryDirectory() as tmp:
        obj = os.path.join(tmp, "m.obj")
        subprocess.run([find_ml(), "/nologo", "/c", "/coff", "/Cp", "/Fo" + obj, SOURCE], check=True)
        sections, symbols = read_coff(obj)
    names = [s[0] for s in sections]
    game = list(next(iter(BUILDS.values()))["symbols"])
    symbol_names = game + QUESTIONS
    blocks, fields, labels = [], [], []
    for block in BLOCKS:
        index = names.index(block)
        data = bytearray(sections[index][1])
        own = []
        for offset, symbol, kind in sections[index][2]:
            name, value, section, _ = symbols[symbol]
            addend = struct.unpack_from("<i", data, offset)[0]
            if section > 0:                 # a label in one of the blocks
                target, addend = BLOCKS.index(names[section - 1]), addend + value
            else:
                target = len(BLOCKS) + symbol_names.index(name)
            if kind not in (DIR32, REL32):
                sys.exit("unexpected relocation type %d at %s+%x" % (kind, block, offset))
            struct.pack_into("<i", data, offset, 0)
            own.append((offset, 1 if kind == REL32 else 0, target, addend))
        blocks.append(bytes(data))
        fields.append(sorted(own))
        labels.append({n: v for n, v, s, k in symbols.values() if s == index + 1 and not n.startswith((".", "$"))})
    return blocks, fields, symbol_names, labels


def link(blocks, fields, build):
    """The three functions' new bytes for a build, and where each block went."""
    regions = build["regions"]
    sizes = [len(base64.b64decode(r[1])) for r in regions]
    main_end = regions[0][0] + len(blocks[0])
    slot = main_end + (build["questions"] - main_end) % QUESTION_SIZE
    bases = [regions[0][0], slot, regions[1][0], regions[2][0]]
    first = (slot - build["questions"]) // QUESTION_SIZE
    values = list(build["symbols"].values()) + [first, first + 1, first + 2]
    out = [bytearray([FILL] * n) for n in sizes]
    for b, (data, own) in enumerate(zip(blocks, fields)):
        data = bytearray(data)
        for offset, relative, target, addend in own:
            value = bases[target] if target < len(BLOCKS) else values[target - len(BLOCKS)]
            value += addend
            if relative:
                value -= bases[b] + offset + 4
            struct.pack_into("<I", data, offset, value & 0xFFFFFFFF)
        r = REGION[b]
        at = bases[b] - regions[r][0]
        if at + len(data) > sizes[r]:
            sys.exit("%s doesn't fit into function %d (%d bytes at %d of %d)" % (BLOCKS[b], r, len(data), at, sizes[r]))
        out[r][at:at + len(data)] = data
    return [bytes(o) for o in out], bases


def csharp(blocks, fields, symbols, entries):
    def b64(data, indent):
        text = base64.b64encode(data).decode()
        lines = [text[i:i + 100] for i in range(0, len(text), 100)]
        return ("\" +\n" + indent + "\"").join(lines)

    def ints(values):
        return ", ".join("0x%X" % v if v >= 0 else "-0x%X" % -v for v in values)

    out = ["// Generated by tools/make_manager.py from tools/manager_events.asm and tools/manager_builds.py. Do not edit.",
           "using System;", "", "namespace A2GPatch", "{",
           "    /// <summary>The code of <see cref=\"ManagerEvents\"/> and the builds of the game it knows.</summary>",
           "    internal static class ManagerCode", "    {",
           "        /// <summary>The blocks: MAIN, ASKS (the questions), KEYS, TABS (fields zeroed).</summary>",
           "        internal static readonly byte[][] Blocks =", "        {"]
    for data in blocks:
        out.append("            Convert.FromBase64String(\"%s\")," % b64(data, "                "))
    out += ["        };", "",
            "        /// <summary>",
            "        /// Each block's fields, four numbers each: the offset, 1 for a distance (call, jump) or 0 for an",
            "        /// address, the target (a block, or the number of blocks plus a symbol) and what to add to it.",
            "        /// </summary>",
            "        internal static readonly int[][] Fields =", "        {"]
    for own in fields:
        out.append("            new[] { %s }," % ints([v for f in own for v in f]))
    out += ["        };", "",
            "        /// <summary>The symbols: the game's (<see cref=\"ManagerBuild.Symbols\"/>), then the numbers of the three questions.</summary>",
            "        internal static readonly string[] Symbols = { %s };" % ", ".join("\"%s\"" % s for s in symbols), "",
            "        /// <summary>The cases the patch takes over, and where their code starts: block and offset.</summary>",
            "        internal static readonly int[] Cases = { %s };" % ", ".join(str(c) for c, _ in ENTRIES),
            "        /// <summary>Where the code of each of <see cref=\"Cases\"/> starts: block and offset.</summary>",
            "        internal static readonly int[][] Entries = { %s };" % ", ".join("new[] { %d, 0x%X }" % e for e in entries), "",
            "        /// <summary>The builds of the game the patch knows.</summary>",
            "        internal static readonly ManagerBuild[] Builds =", "        {"]
    for key, build in BUILDS.items():
        out += ["            new ManagerBuild",
                "            {",
                "                Switch = 0x%X, Table = 0x%X, Questions = 0x%X, KeyState = 0x%X," % (build["switch"], build["table"], build["questions"], build["keystate"]),
                "                Cases = new uint[] { %s }," % ", ".join("0x%X" % build["cases"][c] for c, _ in ENTRIES),
                "                Regions = new uint[] { %s }," % ", ".join("0x%X" % r[0] for r in build["regions"]),
                "                Originals = new[]",
                "                {"]
        for _, data in build["regions"]:
            out.append("                    Convert.FromBase64String(\"%s\")," % b64(base64.b64decode(data), "                        "))
        out += ["                },",
                "                Symbols = new uint[] { %s }," % ", ".join("0x%X" % v for v in build["symbols"].values()),
                "            },"]
    out += ["        };", "    }", "", "    /// <summary>A build of the game for <see cref=\"ManagerEvents\"/>.</summary>",
            "    internal sealed class ManagerBuild", "    {",
            "        /// <summary>Where the switch of the private life starts (movsx eax,word [esp+7F4] / dec eax / cmp eax,26), its jump table, the question table.</summary>",
            "        public uint Switch, Table, Questions;",
            "        /// <summary>The import address table entry of GetAsyncKeyState, which tells GOG's build from the 2007 CD release's.</summary>",
            "        public uint KeyState;",
            "        /// <summary>The original targets of <see cref=\"ManagerCode.Cases\"/> in the jump table.</summary>",
            "        public uint[] Cases;",
            "        /// <summary>The three functions nothing refers to: where they start and their original bytes.</summary>",
            "        public uint[] Regions;",
            "        /// <summary>The original bytes of <see cref=\"Regions\"/>.</summary>",
            "        public byte[][] Originals;",
            "        /// <summary>The addresses of the game's symbols, in the order of <see cref=\"ManagerCode.Symbols\"/>.</summary>",
            "        public uint[] Symbols;",
            "    }", "}", ""]
    return "\n".join(out)


def c_header(blocks, fields, symbols, entries):
    build = BUILDS["2007"]

    def bytes_c(data):
        rows = [", ".join("0x%02X" % b for b in data[i:i + 20]) for i in range(0, len(data), 20)]
        return "{\n    " + ",\n    ".join(rows) + " }"

    out = ["/* Generated by tools/make_manager.py from tools/manager_events.asm and tools/manager_builds.py. Do not edit. */",
           "/* The code of the manager events (ManagerCode.cs) and the 2007 CD release's build (GOG's addresses, its own imports). */", ""]
    for name, data in zip(BLOCKS, blocks):
        out.append("static const BYTE kManager%s[%d] = %s;" % (name.title(), len(data), bytes_c(data)))
    out.append("static const BYTE *const kManagerBlocks[%d] = { %s };" % (len(blocks), ", ".join("kManager%s" % n.title() for n in BLOCKS)))
    out.append("static const WORD kManagerBlockSizes[%d] = { %s };" % (len(blocks), ", ".join(str(len(b)) for b in blocks)))
    out.append("/* Each block's fields: offset, 1 for a distance or 0 for an address, target (a block, or %d plus a symbol), addend. */" % len(BLOCKS))
    for name, own in zip(BLOCKS, fields):
        values = [v for f in own for v in f]
        out.append("static const int kManagerFields%s[%d] = { %s };" % (name.title(), len(values), ", ".join(str(v) for v in values)))
    out.append("static const int *const kManagerFields[%d] = { %s };" % (len(BLOCKS), ", ".join("kManagerFields%s" % n.title() for n in BLOCKS)))
    out.append("static const WORD kManagerFieldCounts[%d] = { %s };" % (len(BLOCKS), ", ".join(str(len(f)) for f in fields)))
    out.append("/* The game's symbols (%s), then the three questions' numbers, filled in at the start. */" % ", ".join(symbols[:-3]))
    out.append("static const DWORD kManagerSymbols[%d] = { %s };" % (len(symbols) - 3, ", ".join("0x%X" % v for v in build["symbols"].values())))
    out.append("static const BYTE kManagerCases[%d] = { %s };" % (len(ENTRIES), ", ".join(str(c) for c, _ in ENTRIES)))
    out.append("static const WORD kManagerEntries[%d][2] = { %s };" % (len(entries), ", ".join("{ %d, 0x%X }" % e for e in entries)))
    out.append("static const DWORD kManagerCaseTargets[%d] = { %s };" % (len(ENTRIES), ", ".join("0x%X" % build["cases"][c] for c, _ in ENTRIES)))
    out.append("#define MANAGER_SWITCH 0x%X" % build["switch"])
    out.append("#define MANAGER_TABLE 0x%X" % build["table"])
    out.append("#define MANAGER_QUESTIONS 0x%X" % build["questions"])
    out.append("static const DWORD kManagerRegions[3] = { %s };" % ", ".join("0x%X" % r[0] for r in build["regions"]))
    originals = [base64.b64decode(r[1]) for r in build["regions"]]
    for i, data in enumerate(originals):
        out.append("static const BYTE kManagerOriginal%d[%d] = %s;" % (i, len(data), bytes_c(data)))
    out.append("static const BYTE *const kManagerOriginals[3] = { kManagerOriginal0, kManagerOriginal1, kManagerOriginal2 };")
    out.append("static const WORD kManagerRegionSizes[3] = { %s };" % ", ".join(str(len(d)) for d in originals))
    return "\n".join(out) + "\n"


def main():
    blocks, fields, symbols, labels = assemble()
    entries = []
    for case, label in ENTRIES:
        block = next(i for i, own in enumerate(labels) if label in own)
        entries.append((block, labels[block][label]))
    q = labels[BLOCKS.index("ASKS")]
    if [q.get("Question13"), q.get("Question21"), q.get("Question35")] != [0, QUESTION_SIZE, 2 * QUESTION_SIZE] or len(blocks[1]) != 3 * QUESTION_SIZE:
        sys.exit("ASKS must hold Question13, Question21, Question35 in this order")
    for key, build in BUILDS.items():
        regions, bases = link(blocks, fields, build)
        print("%s: MAIN %d, questions at 0x%X (number %d), KEYS %d of %d, TABS %d of %d bytes" % (
            key, len(blocks[0]), bases[1], (bases[1] - build["questions"]) // QUESTION_SIZE,
            len(blocks[2]), len(regions[1]), len(blocks[3]), len(regions[2])))
    with open(TARGET, "w", encoding="utf-8", newline="\n") as f:
        f.write(csharp(blocks, fields, symbols, entries))
    with open(HEADER, "w", encoding="utf-8", newline="\n") as f:
        f.write(c_header(blocks, fields, symbols, entries))
    print("wrote", TARGET, "and", HEADER)


if __name__ == "__main__":
    main()
