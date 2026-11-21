using A2GPatch;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace A2GPatch.Tests
{
    /// <summary>Builds the game's text files synthetically: RESDLL.dll with string tables and dialogs, event files, tips.</summary>
    internal static class GameTextFiles
    {
        internal const uint ImageBase = 0x10000000, ResourceRva = 0x1000;
        private const int PeAt = 0x40, OptionalAt = PeAt + 24, SectionsAt = OptionalAt + 0xE0;

        /// <summary>A DLL whose only section holds the resources (type, id, data), each in German.</summary>
        internal static byte[] Dll(IEnumerable<Tuple<int, int, byte[]>> resources, bool named = false, string sectionName = ".rsrc", uint resourceRva = ResourceRva)
        {
            byte[] section = Section(resources.ToList(), named);
            uint rawSize = (uint)((section.Length + 0x1FF) & ~0x1FF);
            byte[] image = new byte[0x400 + rawSize];
            image[0] = 0x4D;
            image[1] = 0x5A;
            Put(image, 0x3C, PeAt);
            Put(image, PeAt, 0x4550);
            Put(image, PeAt + 4, 0x14C);
            image[PeAt + 6] = 1;
            image[PeAt + 20] = 0xE0;
            Put(image, OptionalAt, 0x10B);
            Put(image, OptionalAt + 28, ImageBase);
            Put(image, OptionalAt + 32, 0x1000);
            Put(image, OptionalAt + 36, 0x200);
            Put(image, OptionalAt + 56, ResourceRva + (uint)((section.Length + 0xFFF) & ~0xFFF));
            Put(image, OptionalAt + 60, 0x400);
            Put(image, OptionalAt + 92, 16);
            Put(image, OptionalAt + 112, resourceRva);
            Put(image, OptionalAt + 116, (uint)section.Length);
            Encoding.ASCII.GetBytes(sectionName).CopyTo(image, SectionsAt);
            Put(image, SectionsAt + 8, (uint)section.Length);
            Put(image, SectionsAt + 12, ResourceRva);
            Put(image, SectionsAt + 16, rawSize);
            Put(image, SectionsAt + 20, 0x400);
            Put(image, SectionsAt + 36, 0x40000040);
            section.CopyTo(image, 0x400);
            return image;
        }

        // The resource directory (types, ids, one language each), the data entries, then the data.
        private static byte[] Section(List<Tuple<int, int, byte[]>> resources, bool named)
        {
            var types = resources.GroupBy(r => r.Item1).OrderBy(g => g.Key).ToList();
            int root = 16 + 8 * (types.Count + (named ? 1 : 0));
            int typeDirs = types.Sum(t => 16 + 8 * t.Count());
            int idDirs = resources.Count * 24, entries = resources.Count * 16;
            int dataAt = (root + typeDirs + idDirs + entries + 3) & ~3;
            int size = dataAt + resources.Sum(r => (r.Item3.Length + 3) & ~3);
            byte[] s = new byte[size];
            s[12] = (byte)(named ? 1 : 0);
            s[14] = (byte)types.Count;
            int nextType = root, nextId = root + typeDirs, nextEntry = nextId + idDirs, nextData = dataAt, rootEntry = 16;
            if (named)
            {
                Put(s, rootEntry, 0x80000000 | (uint)nextEntry);   // a name: never read
                Put(s, rootEntry + 4, 0x80000000 | (uint)root);
                rootEntry += 8;
            }
            foreach (var type in types)
            {
                Put(s, rootEntry, (uint)type.Key);
                Put(s, rootEntry + 4, 0x80000000 | (uint)nextType);
                rootEntry += 8;
                s[nextType + 14] = (byte)type.Count();
                int typeEntry = nextType + 16;
                nextType += 16 + 8 * type.Count();
                foreach (var resource in type)
                {
                    Put(s, typeEntry, (uint)resource.Item2);
                    Put(s, typeEntry + 4, 0x80000000 | (uint)nextId);
                    typeEntry += 8;
                    s[nextId + 14] = 1;
                    Put(s, nextId + 16, 1031);
                    Put(s, nextId + 20, (uint)nextEntry);
                    nextId += 24;
                    Put(s, nextEntry, ResourceRva + (uint)nextData);
                    Put(s, nextEntry + 4, (uint)resource.Item3.Length);
                    nextEntry += 16;
                    resource.Item3.CopyTo(s, nextData);
                    nextData += (resource.Item3.Length + 3) & ~3;
                }
            }
            return s;
        }

        internal static void Put(byte[] data, int at, uint value) => BitConverter.GetBytes(value).CopyTo(data, at);

        /// <summary>A block of 16 strings, the rest empty.</summary>
        internal static byte[] Strings(Dictionary<int, string> strings) =>
            Enumerable.Range(0, 16).SelectMany(i =>
            {
                string text = strings.TryGetValue(i, out string s) ? s : "";
                return BitConverter.GetBytes((ushort)text.Length).Concat(Encoding.Unicode.GetBytes(text));
            }).ToArray();

        /// <summary>A DIALOGEX with font, the caption and a static control for each text.</summary>
        internal static byte[] Dialog(string caption, params string[] controls)
        {
            List<byte> d = new List<byte>();
            d.AddRange(new byte[] { 1, 0, 0xFF, 0xFF, 0, 0, 0, 0, 0, 0, 0, 0 });
            d.AddRange(BitConverter.GetBytes(0x80C800C0u));                 // style with DS_SETFONT
            d.AddRange(BitConverter.GetBytes((ushort)controls.Length));
            d.AddRange(new byte[8]);                                        // x, y, cx, cy
            d.AddRange(new byte[] { 0, 0, 0, 0 });                          // no menu, no class
            d.AddRange(Encoding.Unicode.GetBytes(caption + "\0"));
            d.AddRange(new byte[] { 8, 0, 0, 0, 0, 1 });
            d.AddRange(Encoding.Unicode.GetBytes("MS Sans Serif\0"));
            foreach (string control in controls)
            {
                d.AddRange(new byte[(4 - d.Count % 4) % 4]);
                d.AddRange(new byte[4]);                                    // help id
                d.AddRange(BitConverter.GetBytes(0x200u));                  // WS_EX_CLIENTEDGE
                d.AddRange(BitConverter.GetBytes(0x50020301u));
                d.AddRange(new byte[8]);
                d.AddRange(BitConverter.GetBytes(0xFFFFFFFFu));             // id
                d.AddRange(new byte[] { 0xFF, 0xFF, 0x82, 0x00 });          // static
                d.AddRange(Encoding.Unicode.GetBytes(control + "\0"));
                d.AddRange(new byte[2]);                                    // no extra data
            }
            return d.ToArray();
        }

        /// <summary>
        /// The game's RESDLL.dll as far as the patches look at it: each string and dialog they change holding
        /// the texts they look for (<paramref name="change"/> may alter them), and string 1147 "DM". The dialog
        /// <paramref name="without"/> is left out.
        /// </summary>
        internal static byte[] Resources(Func<TextSource, int, string, string> change = null, int without = -1)
        {
            change = change ?? ((source, id, text) => text);
            TextChange[] changes = TextPatch.InOrder.SelectMany(p => p.Changes).ToArray();
            Dictionary<int, string> strings = changes.Where(c => c.Source == TextSource.String).SelectMany(c => c.Ids.Select(id => new { id, c.From }))
                .GroupBy(x => x.id).ToDictionary(g => g.Key, g => change(TextSource.String, g.Key, string.Join(" | ", g.Select(x => x.From).Distinct())));
            strings[1147] = "DM";
            List<Tuple<int, int, byte[]>> resources = strings.GroupBy(s => s.Key / 16 + 1)
                .Select(block => Tuple.Create(ResourceTexts.StringType, block.Key, Strings(block.ToDictionary(s => s.Key % 16, s => s.Value)))).ToList();
            resources.AddRange(changes.Where(c => c.Source == TextSource.Dialog).SelectMany(c => c.Ids.Select(id => new { id, c.From })).GroupBy(x => x.id)
                .Where(g => g.Key != without).Select(g => Tuple.Create(ResourceTexts.DialogType, g.Key, Dialog("ANSTOSS 2", g.Select(x => change(TextSource.Dialog, g.Key, x.From)).Distinct().ToArray()))));
            return Dll(resources);
        }

        /// <summary>An event file of 1000 texts, "Text n" except where a patch looks for its text.</summary>
        internal static byte[] Events(Func<int, string, string> change = null)
        {
            string[] texts = Enumerable.Range(0, 1000).Select(i => "Text " + i).ToArray();
            foreach (TextChange c in TextPatch.InOrder.SelectMany(p => p.Changes).Where(c => c.Source == TextSource.Event))
                texts[c.Ids[0]] = c.From;
            if (change != null)
                texts = texts.Select((t, i) => change(i, t)).ToArray();
            byte[] plain = TextPatch.Ansi.GetBytes(string.Concat(texts.Select(t => t + "\0")));
            byte[] data = new byte[8 + plain.Length];
            BitConverter.GetBytes(plain.Length).CopyTo(data, 0);
            BitConverter.GetBytes(texts.Length).CopyTo(data, 4);
            byte key = data[0];
            for (int i = 0; i < plain.Length; i++)
                data[8 + i] = key = (byte)(plain[i] ^ key);
            return data;
        }

        /// <summary>The tips.</summary>
        internal static byte[] Tips(string text = "Kaufen Sie für 1 Million DM.\r\nOder 300.000 DM?\r\n") => TextPatch.Ansi.GetBytes(text);

        /// <summary>Puts RESDLL.dll, both event files and the tips into <paramref name="folder"/>.</summary>
        internal static void Write(GameFolder folder, byte[] resources = null, byte[] events = null, byte[] tips = null)
        {
            File.WriteAllBytes(folder.File(TextPatch.ResourceFile), resources ?? Resources());
            Directory.CreateDirectory(folder.File("TEXTE"));
            foreach (string name in TextPatch.EventFiles)
                File.WriteAllBytes(folder.File(name), events ?? Events());
            File.WriteAllBytes(folder.File(TextPatch.TipsFile), tips ?? Tips());
        }

        /// <summary>The string <paramref name="id"/> of <paramref name="dll"/> as it is now (not the original).</summary>
        internal static string StringNow(byte[] dll, int id) => StringTable.Read(ResourceNow(dll, ResourceTexts.StringType, id / 16 + 1))[id % 16];

        /// <summary>The texts of the dialog <paramref name="id"/> of <paramref name="dll"/> as it is now.</summary>
        internal static string[] DialogNow(byte[] dll, int id) => DialogTexts.Read(ResourceNow(dll, ResourceTexts.DialogType, id)).Texts.ToArray();

        /// <summary>The current data of a resource, following the directory.</summary>
        internal static byte[] ResourceNow(byte[] dll, int type, int id)
        {
            PeFile pe = PeFile.Read(dll);
            long root = pe.FileOffset(ImageBase + pe.ResourceRva);
            long Entry(long dir, int key)
            {
                int named = BitConverter.ToUInt16(dll, (int)dir + 12), numbered = BitConverter.ToUInt16(dll, (int)dir + 14);
                for (int i = named; i < named + numbered; i++)
                {
                    if (BitConverter.ToInt32(dll, (int)dir + 16 + 8 * i) == key || key < 0)
                        return root + (BitConverter.ToUInt32(dll, (int)dir + 20 + 8 * i) & 0x7FFFFFFF);
                }
                throw new KeyNotFoundException();
            }
            long entry = Entry(Entry(Entry(root, type), id), -1);
            long data = pe.FileOffset(ImageBase + BitConverter.ToUInt32(dll, (int)entry));
            return dll.Skip((int)data).Take(BitConverter.ToInt32(dll, (int)entry + 4)).ToArray();
        }
    }

    /// <summary>Tests for the text patches: their changes, the files they read and write, and how they switch.</summary>
    // Patch101's hashes are replaced in one test (GogPatchesTests.WithTestHashes).
    [Collection("GameInstall statics")]
    public class TextPatchesTests
    {
        private static readonly TextPatch Euro = Patch.Get<Currency>(), Back = Patch.Get<CentreBack>(), Cups = Patch.Get<CupNames>();
        private static readonly TextPatch Typos = Patch.Get<TextTypos>();
        private static TextPatch[] All => TextPatch.InOrder.ToArray();

        private static GameFiles Files(GameFolder folder) => new GameFiles(folder.Path, null);

        private static void Write(GameFolder folder, GameFiles files)
        {
            foreach (KeyValuePair<string, byte[]> file in files.Changes)
                File.WriteAllBytes(folder.File(file.Key), file.Value);
        }

        [Fact]
        public void ThePatches_AreTextsWithoutFlag_InTheirOrder()
        {
            Assert.Equal(new[] { "Currency", "CentreBack", "CupNames", "LeagueNames", "GroupLetters", "ContractTerms", "TextTypos" }, All.Select(p => p.Key));
            Assert.All(All, p => Assert.Equal(0u, p.Flag));
            Assert.All(All, p => Assert.False(p.NeedsExe));
            Assert.All(All.Take(6), p => Assert.Equal(PatchGroup.Texts, p.Group));
            Assert.Equal(PatchGroup.Patches, Typos.Group);
            Assert.True(Patch.Get<NetworkPlay>().NeedsExe);
            Assert.Contains(GameInstall.FileSwitches, s => s.Key == Typos && s.Value == Patch.Get<GameBugs>());
        }

        // The game wants the event file at its length: each patch's event texts together keep it.
        [Fact]
        public void EventChanges_KeepTheLength_EachPatchOnItsOwn()
        {
            foreach (TextPatch patch in All)
            {
                TextChange[] events = patch.Changes.Where(c => c.Source == TextSource.Event).ToArray();
                Assert.Equal(events.Sum(c => TextPatch.Ansi.GetByteCount(c.From)), events.Sum(c => TextPatch.Ansi.GetByteCount(c.To)));
            }
        }

        [Fact]
        public void Replace_TakesWholeWordsOnly()
        {
            Assert.Equal("1 € und 2 €. 3 DMX", TextChange.Replace("1 DM und 2 DM. 3 DMX", " DM", " €"));
            Assert.Equal("IV, XMD, IV", TextChange.Replace("MD, XMD, MD", "MD", "IV"));
            Assert.Equal("Gruppe A, Gruppe 10", TextChange.Replace("Gruppe 1, Gruppe 10", "Gruppe 1", "Gruppe A"));
            Assert.Equal("keins", TextChange.Replace("keins", "MD", "IV"));
        }

        [Fact]
        public void Changes_GoBothWays()
        {
            TextChange words = TextChange.Strings("MD", "IV", 1), whole = TextChange.Event(1, "Manndecker", "Innenverteidiger");
            Assert.Equal("IV", words.Apply("MD", true));
            Assert.Equal("MD", words.Apply("IV", false));
            Assert.True(words.IsIn("MD/LI", false));
            Assert.False(words.IsIn("MD/LI", true));
            Assert.Equal("Innenverteidiger", whole.Apply("Manndecker", true));
            Assert.Equal("Manndecker", whole.Apply("Innenverteidiger", false));
            Assert.Equal("Torwart", whole.Apply("Torwart", true));
            Assert.True(whole.IsIn("Innenverteidiger", true));
            Assert.False(whole.IsIn("Ein Manndecker", false));
            Assert.Empty(TextChange.Tips("a", "b").Ids);
        }

        // ---- Files ----

        [Fact]
        public void EventFile_ReadsAndWritesTheTexts()
        {
            byte[] events = GameTextFiles.Events();
            string[] texts = EventFile.Read(events);
            Assert.Equal(1000, texts.Length);
            Assert.Equal("Manndecker", texts[909]);
            Assert.Equal(events, EventFile.Write(events, texts));

            Assert.Null(EventFile.Read(null));
            Assert.Null(EventFile.Read(new byte[8]));
            Assert.Null(EventFile.Read(events.Concat(new byte[] { 0 }).ToArray()));          // longer than its header says
            byte[] unended = (byte[])events.Clone();
            unended[unended.Length - 1] ^= 1;                                                   // the last text without its NUL
            Assert.Null(EventFile.Read(unended));
            byte[] count = (byte[])events.Clone();
            count[4]++;
            Assert.Null(EventFile.Read(count));
        }

        [Fact]
        public void StringTable_ReadsSixteenStrings_AndKeepsWhatFollows()
        {
            byte[] block = GameTextFiles.Strings(new Dictionary<int, string> { [3] = "Manndecker" }).Concat(new byte[] { 0, 0 }).ToArray();
            string[] strings = StringTable.Read(block);
            Assert.Equal("Manndecker", strings[3]);
            Assert.Equal(block, StringTable.Write(strings, block));
            strings[3] = "Innenverteidiger";
            Assert.Equal("Innenverteidiger", StringTable.Read(StringTable.Write(strings, block))[3]);
            Assert.Equal(new byte[] { 0, 0 }, StringTable.Write(strings, block).Skip(block.Length + 12 - 2).ToArray());

            Assert.Null(StringTable.Read(null));
            Assert.Null(StringTable.Read(new byte[30]));                                       // 15 strings
            Assert.Null(StringTable.Read(new byte[] { 5, 0, 65, 0 }));                         // shorter than its length
        }

        [Fact]
        public void DialogTexts_ChangeOnlyTheTexts_AndRealignTheItems()
        {
            byte[] dialog = GameTextFiles.Dialog("Titel", "1 DM", "MD");
            DialogTexts texts = DialogTexts.Read(dialog);
            Assert.Equal(new[] { "Titel", "1 DM", "MD" }, texts.Texts);
            Assert.Equal(dialog, texts.Write());
            texts.Change(t => TextChange.Replace(t, " DM", " €"));
            byte[] changed = texts.Write();
            Assert.Equal(new[] { "Titel", "1 €", "MD" }, DialogTexts.Read(changed).Texts);
            Assert.Equal(GameTextFiles.Dialog("Titel", "1 €", "MD"), changed);              // the second item moved up and is aligned again

            Assert.Null(DialogTexts.Read(null));
            Assert.Null(DialogTexts.Read(dialog.Take(dialog.Length - 6).ToArray()));
        }

        // A DLGTEMPLATE without font: an item numbered instead of titled, one with creation data, a class by name.
        [Fact]
        public void DialogTexts_ReadOldTemplates_WithNumbersAndCreationData()
        {
            List<byte> d = new List<byte>();
            d.AddRange(BitConverter.GetBytes(0x80C80080u));                      // no DS_SETFONT
            d.AddRange(new byte[4]);
            d.AddRange(new byte[] { 3, 0 });
            d.AddRange(new byte[8]);
            d.AddRange(new byte[] { 0xFF, 0xFF, 7, 0 });                         // menu by number
            d.AddRange(Encoding.Unicode.GetBytes("Klasse\0"));
            d.AddRange(Encoding.Unicode.GetBytes("Titel\0"));
            void Item(byte[] title, byte[] extra)
            {
                d.AddRange(new byte[(4 - d.Count % 4) % 4]);
                d.AddRange(new byte[16]);                                        // style, exstyle, x, y, cx, cy
                d.AddRange(new byte[] { 1, 0 });                                 // id
                d.AddRange(Encoding.Unicode.GetBytes("BUTTON\0"));
                d.AddRange(title);
                d.AddRange(extra);
            }
            Item(new byte[] { 0xFF, 0xFF, 5, 0 }, new byte[2]);
            Item(Encoding.Unicode.GetBytes("Festgehalt\0"), new byte[] { 2, 0, 9, 9 });   // two bytes of creation data
            Item(Encoding.Unicode.GetBytes("\0"), new byte[2]);
            byte[] dialog = d.ToArray();
            DialogTexts texts = DialogTexts.Read(dialog);
            Assert.Equal(new[] { "Titel", "Festgehalt", "" }, texts.Texts);
            Assert.Equal(dialog, texts.Write());
            Assert.Null(DialogTexts.Read(dialog.Take(dialog.Length - 1).ToArray()));

            // With a font (only its size) and no items.
            byte[] font = BitConverter.GetBytes(0x80C800C0u).Concat(new byte[14]).Concat(new byte[4])
                .Concat(Encoding.Unicode.GetBytes("Leer\0")).Concat(new byte[] { 8, 0 }).Concat(Encoding.Unicode.GetBytes("Arial\0")).ToArray();
            Assert.Equal(new[] { "Leer" }, DialogTexts.Read(font).Texts);
            Assert.Equal(font, DialogTexts.Read(font).Write());

            // Extended with extra data that runs past the end.
            byte[] extended = GameTextFiles.Dialog("Titel", "Text");
            extended[extended.Length - 2] = 8;
            Assert.Null(DialogTexts.Read(extended));
            extended[extended.Length - 2] = 0;
            extended = extended.Take(extended.Length - 2).Concat(new byte[] { 2, 0, 1, 2 }).ToArray();
            Assert.Equal(extended, DialogTexts.Read(extended).Write());
        }

        [Fact]
        public void ResourceTexts_FindTheOriginals()
        {
            byte[] dll = GameTextFiles.Dll(new[]
            {
                Tuple.Create(6, 69, GameTextFiles.Strings(new Dictionary<int, string> { [4] = "Manndecker" })),
                Tuple.Create(5, 291, GameTextFiles.Dialog("Info", "Manndecker")),
                Tuple.Create(2, 1, new byte[] { 1, 2, 3 }),
            }, named: true);
            ResourceTexts texts = ResourceTexts.Read(dll);
            Assert.Empty(texts.Applied);
            Assert.Equal("Manndecker", texts.OriginalString(1092));
            Assert.Equal(new[] { "Info", "Manndecker" }, texts.OriginalDialog(291).Texts);
            Assert.Equal(new byte[] { 1, 2, 3 }, texts.Original(2, 1));
            Assert.Null(texts.OriginalString(5000));
            Assert.Null(texts.OriginalDialog(1));
            Assert.Null(texts.Original(5, 1));
            Assert.Equal(dll, texts.Write(new string[0], new Dictionary<long, byte[]>()));

            // Moved into the section and back.
            byte[] moved = texts.Write(new[] { "CentreBack" }, new Dictionary<long, byte[]> { [ResourceTexts.Key(5, 291)] = GameTextFiles.Dialog("Info", "Innenverteidiger") });
            Assert.Equal(new[] { "Info", "Innenverteidiger" }, GameTextFiles.DialogNow(moved, 291));
            PeFile pe = PeFile.Read(moved);
            Assert.Equal(ResourceTexts.SectionName, pe.Sections.Last().Name);
            Assert.Equal(PeFile.ReadOnlyData, pe.Sections.Last().Characteristics);
            ResourceTexts again = ResourceTexts.Read(moved);
            Assert.Equal(new[] { "CentreBack" }, again.Applied);
            Assert.Equal(new[] { "Info", "Manndecker" }, again.OriginalDialog(291).Texts);
            Assert.Equal(dll, again.Write(new string[0], new Dictionary<long, byte[]>()));
            // Moved anew from the moved file: the original addresses are kept.
            byte[] twice = again.Write(new[] { "CentreBack" }, new Dictionary<long, byte[]> { [ResourceTexts.Key(5, 291)] = GameTextFiles.Dialog("Info", "IV") });
            Assert.Equal(dll, ResourceTexts.Read(twice).Write(new string[0], new Dictionary<long, byte[]>()));
        }

        [Fact]
        public void ResourceTexts_AreNull_ForOtherFiles()
        {
            byte[] dll = GameTextFiles.Dll(new[] { Tuple.Create(6, 1, GameTextFiles.Strings(new Dictionary<int, string>())) });
            Assert.Null(ResourceTexts.Read(null));
            Assert.Null(ResourceTexts.Read(new byte[] { 0x4D, 0x5A }));
            Assert.Null(ResourceTexts.Read(GameTextFiles.Dll(new Tuple<int, int, byte[]>[0], resourceRva: 0)));
            Assert.Null(ResourceTexts.Read(GameTextFiles.Dll(new Tuple<int, int, byte[]>[0], resourceRva: 0x5000)));     // outside the sections
            Assert.Null(ResourceTexts.Read(GameTextFiles.Dll(new Tuple<int, int, byte[]>[0], sectionName: ResourceTexts.SectionName)));   // our name, not our section
            byte[] broken = (byte[])dll.Clone();
            GameTextFiles.Put(broken, 0x400 + 0x14, 0x80000000 | 0x7FFF0);                   // the type's directory outside the file
            Assert.Null(ResourceTexts.Read(broken));
            byte[] entry = (byte[])dll.Clone();
            int at = 0x400 + 72;                                                               // the data entry (behind three directories): address far away
            GameTextFiles.Put(entry, at, 0x10000);
            Assert.Throws<ArgumentException>(() => ResourceTexts.Read(entry).Original(6, 1));
            GameTextFiles.Put(entry, at, GameTextFiles.ResourceRva);
            GameTextFiles.Put(entry, at + 4, 0x7FFFFFFF);                                      // too long
            Assert.Throws<ArgumentException>(() => ResourceTexts.Read(entry).Original(6, 1));
        }

        // ---- The patches ----

        [Fact]
        public void State_IsUnknown_WithoutTheGamesTexts()
        {
            using (GameFolder folder = new GameFolder())
            {
                Assert.All(All, p => Assert.Equal(FileState.Unknown, p.State(Files(folder))));
                // A string or dialog missing, or without the text a patch looks for: that patch only.
                GameTextFiles.Write(folder, GameTextFiles.Resources((source, id, text) => id == 2162 ? "%c %d Euro" : id == 291 ? "Libero" : text));
                Assert.Equal(FileState.Unknown, Euro.State(Files(folder)));
                Assert.Equal(FileState.Unknown, Back.State(Files(folder)));
                Assert.Equal(FileState.Off, Cups.State(Files(folder)));
                File.WriteAllBytes(folder.File(TextPatch.ResourceFile), GameTextFiles.Resources(without: 291));
                Assert.Equal(FileState.Unknown, Back.State(Files(folder)));
                Assert.Equal(FileState.Off, Euro.State(Files(folder)));
                byte[] withoutDialogs = GameTextFiles.Dll(new[] { Tuple.Create(6, 136, GameTextFiles.Strings(new Dictionary<int, string> { [2162 % 16] = "%c %d DM" })) });
                File.WriteAllBytes(folder.File(TextPatch.ResourceFile), withoutDialogs);
                Assert.All(All, p => Assert.Equal(FileState.Unknown, p.State(Files(folder))));
            }
        }

        [Fact]
        public void EachPatch_SwitchesItsTexts_AndBackToTheByte()
        {
            using (GameFolder folder = new GameFolder())
            {
                GameTextFiles.Write(folder);
                Dictionary<string, byte[]> originals = new[] { TextPatch.ResourceFile, TextPatch.TipsFile }.Concat(TextPatch.EventFiles).ToDictionary(f => f, f => File.ReadAllBytes(folder.File(f)));
                Assert.All(All, p => Assert.Equal(FileState.Off, p.State(Files(folder))));
                foreach (TextPatch patch in All)
                {
                    GameFiles files = Files(folder);
                    patch.Switch(files, true);
                    Assert.All(All, p => Assert.Equal(p == patch ? FileState.On : FileState.Off, p.State(files)));
                    patch.Switch(files, false);
                    Assert.All(originals, f => Assert.Equal(f.Value, files.Read(f.Key)));
                }
            }
        }

        [Fact]
        public void AllPatches_ChangeTheTexts_InAnyOrder()
        {
            using (GameFolder folder = new GameFolder())
            {
                GameTextFiles.Write(folder);
                GameFiles files = Files(folder);
                foreach (TextPatch patch in All)
                    patch.Switch(files, true);
                byte[] dll = files.Read(TextPatch.ResourceFile);
                Assert.Equal(" €", GameTextFiles.StringNow(dll, 2162));
                Assert.Equal("DM", GameTextFiles.StringNow(dll, 1147));
                Assert.Equal("IV", GameTextFiles.StringNow(dll, 1144));
                Assert.Equal("Innenverteidiger", GameTextFiles.StringNow(dll, 1174));
                // The cup names come first: the typing error's "UEFA - Pokals" is gone already.
                Assert.Equal("UEFA-Cups", GameTextFiles.StringNow(dll, 2642));
                Assert.Equal(new[] { "ANSTOSS 2", " €", "Grundgehalt", "Ausstiegsklausel" }, GameTextFiles.DialogNow(dll, 210));
                Assert.Equal(new[] { "ANSTOSS 2", "Cup der Pokalsieger", "ECL-Cup", "ECL" }, GameTextFiles.DialogNow(dll, 274));
                string[] events = EventFile.Read(files.Read(TextPatch.EventFiles[1]));
                Assert.Equal("Innenverteidiger", events[909]);
                Assert.Equal("rechter Verteidiger", events[911]);
                Assert.Equal(GameTextFiles.Events().Length, files.Read(TextPatch.EventFiles[0]).Length);
                Assert.Equal("Kaufen Sie für 1 Million €.\r\nOder 300.000 €?\r\n", TextPatch.Ansi.GetString(files.Read(TextPatch.TipsFile)));
                Write(folder, files);

                GameFiles reverse = Files(folder);
                Assert.All(All, p => Assert.Equal(FileState.On, p.State(reverse)));
                foreach (TextPatch patch in All)
                    patch.Switch(reverse, false);
                foreach (TextPatch patch in All.Reverse())
                    patch.Switch(reverse, true);
                Assert.Equal(dll, reverse.Read(TextPatch.ResourceFile));
            }
        }

        [Fact]
        public void DataFiles_InAnotherState_AreLeftOut()
        {
            using (GameFolder folder = new GameFolder())
            {
                // An event file with one of the texts changed, one too short, and tips that have both.
                GameTextFiles.Write(folder, events: GameTextFiles.Events((i, t) => i == 229 ? "anders" : t), tips: GameTextFiles.Tips("1 DM, 2 €"));
                File.WriteAllBytes(folder.File(TextPatch.EventFiles[1]), GameTextFiles.Events().Take(8).Concat(new byte[] { 1 }).ToArray());
                GameFiles files = Files(folder);
                Assert.Equal(FileState.Off, Euro.State(files));
                Euro.Switch(files, true);
                Assert.Equal(new[] { TextPatch.ResourceFile }, files.Changes.Select(c => c.Key));
                Assert.Equal(FileState.On, Euro.State(files));

                // Event files missing and no tips: the resources alone.
                foreach (string name in TextPatch.EventFiles.Concat(new[] { TextPatch.TipsFile }))
                    File.Delete(folder.File(name));
                files = Files(folder);
                Back.Switch(files, true);
                Assert.Equal(FileState.On, Back.State(files));
                // Events that hold no text of the patch's (too few texts) are left out as well.
                Write(folder, files);
                File.WriteAllBytes(folder.File(TextPatch.EventFiles[0]), ShortEvents());
                Assert.Equal(FileState.On, Back.State(Files(folder)));
            }
        }

        // An event file of 10 texts.
        private static byte[] ShortEvents()
        {
            byte[] plain = Encoding.ASCII.GetBytes(string.Concat(Enumerable.Range(0, 10).Select(i => "T\0")));
            byte[] data = new byte[8 + plain.Length];
            BitConverter.GetBytes(plain.Length).CopyTo(data, 0);
            BitConverter.GetBytes(10).CopyTo(data, 4);
            byte key = data[0];
            for (int i = 0; i < plain.Length; i++)
                data[8 + i] = key = (byte)(plain[i] ^ key);
            return data;
        }

        // On in the resources, but an event file came back (patch 1.01 rewrote it): switching on does only that file.
        [Fact]
        public void SwitchingOn_CompletesTheDataFiles()
        {
            using (GameFolder folder = new GameFolder())
            {
                GameTextFiles.Write(folder);
                GameFiles files = Files(folder);
                Back.Switch(files, true);
                Write(folder, files);
                File.WriteAllBytes(folder.File(TextPatch.EventFiles[0]), GameTextFiles.Events());
                files = Files(folder);
                Assert.Equal(FileState.Off, Back.State(files));
                Back.Switch(files, true);
                Assert.Equal(new[] { TextPatch.EventFiles[0] }, files.Changes.Select(c => c.Key));
                Assert.Equal(FileState.On, Back.State(files));
            }
        }

        // A name in the section's list this version doesn't know: nothing may be switched.
        [Fact]
        public void UnknownPatchInTheFile_MakesAllUnknown()
        {
            using (GameFolder folder = new GameFolder())
            {
                GameTextFiles.Write(folder);
                byte[] dll = File.ReadAllBytes(folder.File(TextPatch.ResourceFile));
                File.WriteAllBytes(folder.File(TextPatch.ResourceFile), ResourceTexts.Read(dll).Write(new[] { "Future" }, new Dictionary<long, byte[]>()));
                Assert.All(All, p => Assert.Equal(FileState.Unknown, p.State(Files(folder))));
            }
        }

        [Fact]
        public void PlainEvents_TakesTheChangesBack()
        {
            byte[] events = GameTextFiles.Events();
            Assert.Null(TextPatch.PlainEvents(null));
            Assert.Same(events, TextPatch.PlainEvents(events));
            byte[] other = { 1, 2, 3 }, few = ShortEvents();
            Assert.Same(other, TextPatch.PlainEvents(other));
            Assert.Same(few, TextPatch.PlainEvents(few));
            // Some texts changed, without the ones that keep the length: back to the byte, length included.
            byte[] changed = GameTextFiles.Events((i, t) => i == 909 ? "Innenverteidiger" : i == 229 ? Euro.Changes.First(c => c.Source == TextSource.Event).To : t);
            Assert.NotEqual(events.Length, changed.Length);
            Assert.Equal(events, TextPatch.PlainEvents(changed));
        }

        // Patch 1.01 knows GOG's event file by its hash, with the texts of the text patches in it too.
        [Fact]
        public void Patch101_KnowsTheEventsWithTextsChanged() => GogPatchesTests.WithTestHashes(() =>
        {
            using (GameFolder folder = new GameFolder())
            {
                GogPatchesTests.GogData(folder);
                byte[] gog = File.ReadAllBytes(folder.File(Patch101.Events));
                string[] texts = Enumerable.Range(0, 1000).Select(i => i == 10 || i == 55 ? "Text " + i + ":" : "Text " + i).ToArray();
                foreach (TextChange c in ((TextPatch)Back).Changes.Where(c => c.Source == TextSource.Event))
                    texts[c.Ids[0]] = c.From;
                byte[] plain = TextPatch.Ansi.GetBytes(string.Concat(texts.Select(t => t + "\0")));
                byte[] events = new byte[8 + plain.Length];
                BitConverter.GetBytes(plain.Length).CopyTo(events, 0);
                BitConverter.GetBytes(texts.Length).CopyTo(events, 4);
                byte key = events[0];
                for (int i = 0; i < plain.Length; i++)
                    events[8 + i] = key = (byte)(plain[i] ^ key);
                string previous = Patch101.EventsGog;
                Patch101.EventsGog = GameFiles.Sha256(events);
                try
                {
                    File.WriteAllBytes(folder.File(Patch101.Events), events);
                    GameTextFiles.Write(folder, events: events);
                    File.Delete(folder.File(Patch101.EventsShort));
                    GameFiles files = Files(folder);
                    Back.Switch(files, true);
                    Write(folder, files);
                    Assert.Equal("Innenverteidiger", EventFile.Read(File.ReadAllBytes(folder.File(Patch101.Events)))[909]);
                    files = Files(folder);
                    Patch101.SwitchData(files, true);
                    string[] v101 = EventFile.Read(files.Read(Patch101.Events));
                    Assert.Equal("Text 10.", v101[10]);
                    Assert.Equal("Innenverteidiger", v101[909]);
                    Assert.Equal(files.Read(Patch101.Events), files.Read(Patch101.EventsShort));
                }
                finally
                {
                    Patch101.EventsGog = previous;
                }
            }
        });

        [Fact]
        public void PlainResources_AndWithTextsOf_LeaveOtherFilesAsTheyAre()
        {
            byte[] other = { 1, 2, 3 };
            Assert.Same(other, TextPatch.PlainResources(other));
            Assert.Same(other, TextPatch.WithTextsOf(new byte[] { 4 }, other));
        }

        // GOG's dialog styles change the dialogs under the texts: both stay, whichever is switched first.
        [Fact]
        public void DialogStyles_AndTheTexts_KeepEachOther()
        {
            byte[] gog = GameTextFiles.Resources(), styled = DialogStyles.Restore(gog);
            byte[] Style(byte[] dll) => GameTextFiles.ResourceNow(dll, ResourceTexts.DialogType, 200).Skip(12).Take(4).ToArray();
            Assert.NotEqual(Style(gog), Style(styled));
            string[] previous = { DialogStyles.Gog, DialogStyles.Result };
            DialogStyles.Gog = GameFiles.Sha256(gog);
            DialogStyles.Result = GameFiles.Sha256(styled);
            try
            {
                using (GameFolder folder = new GameFolder())
                {
                    GameTextFiles.Write(folder, gog);
                    DialogStyles styles = Patch.All.OfType<DialogStyles>().Single();
                    GameFiles files = Files(folder);
                    Back.Switch(files, true);
                    Assert.Equal(FileState.Off, styles.State(files));

                    styles.Switch(files, true);
                    Assert.Equal(FileState.On, styles.State(files));
                    Assert.Equal(FileState.On, Back.State(files));
                    Assert.Equal(gog, files.Read(DialogStyles.Backup));
                    byte[] dll = files.Read(TextPatch.ResourceFile);
                    Assert.Equal(Style(styled), Style(dll));                                       // moved, with the style of 1999
                    Assert.Equal(new[] { "ANSTOSS 2", "IV", "Fixe Ablöse" }, GameTextFiles.DialogNow(dll, 200));

                    Back.Switch(files, false);
                    Assert.Equal(styled, files.Read(TextPatch.ResourceFile));
                    Back.Switch(files, true);
                    styles.Switch(files, false);
                    Assert.Equal(FileState.Off, styles.State(files));
                    Assert.Equal(FileState.On, Back.State(files));
                    Assert.Null(files.Read(DialogStyles.Backup));
                    Assert.Equal(Style(gog), Style(files.Read(TextPatch.ResourceFile)));
                    Back.Switch(files, false);
                    Assert.Equal(gog, files.Read(TextPatch.ResourceFile));
                }
            }
            finally
            {
                DialogStyles.Gog = previous[0];
                DialogStyles.Result = previous[1];
            }
        }

        // ---- In a game folder ----

        // The packed 2007 CD exe: the text patches go without the DLL.
        [Fact]
        public void PackedRunExe_SwitchesTheTexts_WithoutTheDll() => GameInstallTests.WithDll(GameInstallTests.OwnDll, () =>
        {
            using (GameFolder folder = new GameFolder())
            {
                File.WriteAllBytes(folder.File("run.exe"), new byte[] { 0x4D, 0x5A, 0x90 });
                GameTextFiles.Write(folder);
                byte[] original = File.ReadAllBytes(folder.File(TextPatch.ResourceFile));
                GameInstall install = GameInstall.Detect(folder.Path);
                Assert.Equal(PatchKind.Dll, install.Kind);
                Assert.True(install.CanApply(Euro));
                Assert.False(install.IsApplied(Euro));
                Assert.False(install.CanApply(Patch.Get<NetworkPlay>()));

                install.Apply(new[] { Euro, Typos });           // the typing errors follow the game's bugs, not their own tile
                Assert.False(File.Exists(folder.File(GameInstall.DllName)));
                Assert.True(install.IsApplied(Euro));
                Assert.Equal(" €", GameTextFiles.StringNow(File.ReadAllBytes(folder.File(TextPatch.ResourceFile)), 2162));
                Assert.Equal(FileState.Off, ((TextPatch)Typos).State(new GameFiles(folder.Path, null)));

                install = GameInstall.Detect(folder.Path);
                Assert.True(install.IsApplied(Euro));
                install.Apply(new[] { GameInstallTests.Bugs, Euro });
                Assert.Equal(FileState.On, ((TextPatch)Typos).State(new GameFiles(folder.Path, null)));
                Assert.True(File.Exists(folder.File(GameInstall.DllName)));

                GameInstall.Detect(folder.Path).Apply(new Patch[0]);
                Assert.Equal(original, File.ReadAllBytes(folder.File(TextPatch.ResourceFile)));
                Assert.False(File.Exists(folder.File(GameInstall.DllName)));
            }
        });

        // An exe patched on disk: the texts beside the code, the typing errors with the game's bugs.
        [Fact]
        public void ExeOnDisk_SwitchesTheTexts_WithTheCode()
        {
            using (GameFolder folder = new GameFolder())
            {
                byte[] exe = GameCode.Exe(GameCode.Arrow());
                File.WriteAllBytes(folder.File("anstoss2.exe"), exe);
                GameTextFiles.Write(folder);
                byte[] original = File.ReadAllBytes(folder.File(TextPatch.ResourceFile));
                GameInstall install = GameInstall.Detect(folder.Path);
                Assert.Equal(PatchKind.Exe, install.Kind);
                Assert.True(install.CanApply(Back));
                install.Apply(new[] { GameInstallTests.Bugs, Back });
                Assert.True(install.IsApplied(Back));
                Assert.True(install.IsApplied(GameInstallTests.Bugs));
                Assert.Equal(FileState.On, ((TextPatch)Typos).State(new GameFiles(folder.Path, null)));
                Assert.Equal("Innenverteidiger", EventFile.Read(File.ReadAllBytes(folder.File(TextPatch.EventFiles[0])))[909]);

                GameInstall.Detect(folder.Path).Apply(new Patch[0]);
                Assert.Equal(original, File.ReadAllBytes(folder.File(TextPatch.ResourceFile)));
                Assert.Equal(exe, File.ReadAllBytes(folder.File("anstoss2.exe")));
            }
        }

        [Fact]
        public void GameFiles_ReadEachFileOnce()
        {
            using (GameFolder folder = new GameFolder())
            {
                File.WriteAllBytes(folder.File("a.bin"), new byte[] { 1 });
                GameFiles files = Files(folder);
                byte[] first = files.Read("a.bin");
                File.WriteAllBytes(folder.File("a.bin"), new byte[] { 2 });
                Assert.Same(first, files.Read("A.BIN"));
                Assert.Null(files.Read("b.bin"));
                File.WriteAllBytes(folder.File("b.bin"), new byte[] { 2 });
                Assert.Null(files.Read("b.bin"));
                files.Write("a.bin", new byte[] { 3 });
                Assert.Equal(new byte[] { 3 }, files.Read("a.bin"));
            }
        }
    }
}
