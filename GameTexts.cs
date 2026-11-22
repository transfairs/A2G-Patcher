using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace A2GPatch
{
    /// <summary>Where a text the <see cref="TextPatch"/>es change is kept.</summary>
    internal enum TextSource
    {
        /// <summary>A string of RESDLL.dll's string tables, by its id.</summary>
        String,
        /// <summary>A dialog of RESDLL.dll, by its id: its caption and the texts of its controls.</summary>
        Dialog,
        /// <summary>A text of the event files, by its number; replaced whole.</summary>
        Event,
        /// <summary>The tips file, as a whole.</summary>
        Tips,
    }

    /// <summary>
    /// One change of a <see cref="TextPatch"/>: in the texts <see cref="Ids"/> of <see cref="Source"/>,
    /// <see cref="From"/> becomes <see cref="To"/>. In strings, dialogs and tips every occurrence as a whole
    /// word (no letter or digit right before or after it where it starts or ends with one); event texts are replaced
    /// whole, as the game wants the event file at its length to the byte.
    /// </summary>
    internal sealed class TextChange
    {
        /// <summary>Where the texts are.</summary>
        public readonly TextSource Source;
        /// <summary>The texts: string ids, dialog ids or event numbers; none for the tips.</summary>
        public readonly int[] Ids;
        /// <summary>The text as the game has it, and as the patch makes it.</summary>
        public readonly string From, To;

        private TextChange(TextSource source, string from, string to, int[] ids)
        {
            this.Source = source;
            this.From = from;
            this.To = to;
            this.Ids = ids;
        }

        /// <summary>A change in the strings <paramref name="ids"/>.</summary>
        public static TextChange Strings(string from, string to, params int[] ids) => new TextChange(TextSource.String, from, to, ids);
        /// <summary>A change in the dialogs <paramref name="ids"/>.</summary>
        public static TextChange Dialogs(string from, string to, params int[] ids) => new TextChange(TextSource.Dialog, from, to, ids);
        /// <summary>The event text <paramref name="id"/> replaced whole.</summary>
        public static TextChange Event(int id, string from, string to) => new TextChange(TextSource.Event, from, to, new[] { id });
        /// <summary>A change in the tips.</summary>
        public static TextChange Tips(string from, string to) => new TextChange(TextSource.Tips, from, to, new int[0]);

        /// <summary><paramref name="text"/> changed (<paramref name="on"/>) or changed back.</summary>
        public string Apply(string text, bool on)
        {
            string from = on ? this.From : this.To, to = on ? this.To : this.From;
            return this.Source == TextSource.Event ? (text == from ? to : text) : Replace(text, from, to);
        }

        /// <summary>Whether <paramref name="text"/> holds the text as the game has it (<paramref name="on"/> false) or as changed.</summary>
        public bool IsIn(string text, bool on)
        {
            string wanted = on ? this.To : this.From;
            return this.Source == TextSource.Event ? text == wanted : Positions(text, wanted).Any();
        }

        /// <summary><paramref name="text"/> with every whole-word <paramref name="from"/> replaced by <paramref name="to"/>.</summary>
        internal static string Replace(string text, string from, string to)
        {
            StringBuilder result = new StringBuilder();
            int done = 0;
            foreach (int at in Positions(text, from))
            {
                result.Append(text, done, at - done).Append(to);
                done = at + from.Length;
            }
            return result.Append(text, done, text.Length - done).ToString();
        }

        // Where whole-word "word" starts in text, not overlapping (letters and digits make up words).
        private static IEnumerable<int> Positions(string text, string word)
        {
            for (int at = text.IndexOf(word, StringComparison.Ordinal); at >= 0; at = text.IndexOf(word, at + 1, StringComparison.Ordinal))
            {
                int end = at + word.Length;
                if ((!char.IsLetterOrDigit(word[0]) || at == 0 || !char.IsLetterOrDigit(text[at - 1]))
                    && (!char.IsLetterOrDigit(word[word.Length - 1]) || end == text.Length || !char.IsLetterOrDigit(text[end])))
                {
                    yield return at;
                    at = end - 1;
                }
            }
        }
    }

    /// <summary>
    /// A change to the game's texts: the strings and dialogs in RESDLL.dll, the event texts and the tips.
    /// Needs no code of the exe, so it is offered for every release, the packed 2007 CD exe included.
    /// RESDLL.dll is never changed in place (a longer text wouldn't fit): the string tables and dialogs the
    /// patches change go into a section of their own at the end of the file (see <see cref="ResourceTexts"/>),
    /// built anew from the original for the patches that are on, in the order of <see cref="InOrder"/>.
    /// Only texts change; styles and positions stay, the button frames the game draws among them.
    /// </summary>
    internal abstract class TextPatch : FilePatch
    {
        /// <summary>The game's resources.</summary>
        internal const string ResourceFile = "RESDLL.dll";
        /// <summary>The tips, read line by line.</summary>
        internal static readonly string TipsFile = Path.Combine("TEXTE", "tips.txt");
        /// <summary>The event files the releases read (the 1999 exe EreigGld.saq, GOG's EreigGold.saq; patch 1.01 writes the first).</summary>
        internal static readonly string[] EventFiles = { Patch101.EventsShort, Patch101.Events };

        /// <summary>The game's texts outside the resources are Windows-1252, € included.</summary>
        internal static readonly Encoding Ansi = Encoding.GetEncoding(1252);

        /// <inheritdoc/>
        public override PatchGroup Group => PatchGroup.Texts;
        /// <inheritdoc/>
        internal override bool NeedsExe => false;

        /// <summary>The patch's name in the list RESDLL.dll keeps of the patches applied.</summary>
        internal string Key => this.GetType().Name;

        /// <summary>The changes, made in this order.</summary>
        internal abstract TextChange[] Changes { get; }

        /// <summary>
        /// Every text patch in the order their changes are made, each on the texts as those before it left them:
        /// the switches, then the game's bugs.
        /// </summary>
        internal static IEnumerable<TextPatch> InOrder => Patch.All.Concat(Patch.All.OfType<GameBugs>().SelectMany(b => b.Members)).OfType<TextPatch>();

        private IEnumerable<TextChange> In(TextSource source) => this.Changes.Where(c => c.Source == source);

        /// <inheritdoc/>
        public override FileState State(GameFiles files)
        {
            ResourceTexts texts = ResourceTexts.Read(files.Read(ResourceFile));
            if (texts == null || !texts.Applied.All(key => InOrder.Any(p => p.Key == key)) || !this.Fits(texts))
                return FileState.Unknown;
            return texts.Applied.Contains(this.Key) && this.Data(files).All(d => d.IsOn) ? FileState.On : FileState.Off;
        }

        /// <inheritdoc/>
        public override void Switch(GameFiles files, bool on)
        {
            ResourceTexts texts = ResourceTexts.Read(files.Read(ResourceFile));
            if (texts.Applied.Contains(this.Key) != on)
            {
                HashSet<string> keys = new HashSet<string>(texts.Applied);
                if (on)
                    keys.Add(this.Key);
                else
                    keys.Remove(this.Key);
                files.Write(ResourceFile, Build(texts, keys));
            }
            foreach (DataFile file in this.Data(files).Where(d => d.IsOn != on))
                files.Write(file.Name, on ? file.On : file.Off);
        }

        // Whether every string and dialog the patch changes is in the original resources, with its text.
        private bool Fits(ResourceTexts texts) =>
            this.In(TextSource.String).All(c => c.Ids.All(id => texts.OriginalString(id) is string text && c.IsIn(text, false)))
            && this.In(TextSource.Dialog).All(c => c.Ids.All(id => texts.OriginalDialog(id) is DialogTexts dialog && dialog.Texts.Any(t => c.IsIn(t, false))));

        // RESDLL.dll with the changes of the patches in keys, made on the original.
        private static byte[] Build(ResourceTexts texts, HashSet<string> keys)
        {
            TextPatch[] patches = InOrder.Where(p => keys.Contains(p.Key)).ToArray();
            Dictionary<long, byte[]> changed = new Dictionary<long, byte[]>();
            IEnumerable<TextChange> strings = patches.SelectMany(p => p.In(TextSource.String));
            foreach (int block in strings.SelectMany(c => c.Ids).Select(id => id / 16 + 1).Distinct())
            {
                byte[] original = texts.Original(ResourceTexts.StringType, block);
                string[] table = StringTable.Read(original);
                foreach (TextChange change in strings)
                    foreach (int id in change.Ids.Where(i => i / 16 + 1 == block))
                        table[id % 16] = change.Apply(table[id % 16], true);
                changed[ResourceTexts.Key(ResourceTexts.StringType, block)] = StringTable.Write(table, original);
            }
            IEnumerable<TextChange> dialogs = patches.SelectMany(p => p.In(TextSource.Dialog));
            foreach (int id in dialogs.SelectMany(c => c.Ids).Distinct())
            {
                DialogTexts dialog = texts.OriginalDialog(id);
                foreach (TextChange change in dialogs.Where(c => c.Ids.Contains(id)))
                    dialog.Change(text => change.Apply(text, true));
                changed[ResourceTexts.Key(ResourceTexts.DialogType, id)] = dialog.Write();
            }
            return texts.Write(patches.Select(p => p.Key), changed);
        }

        // A file outside the resources the patch changes: whether it is on, and both versions.
        private sealed class DataFile
        {
            public string Name;
            public bool IsOn;
            public byte[] Off, On;
        }

        // The event files and the tips as far as the patch changes them; files that hold neither the game's
        // texts nor the patch's are left out.
        private IEnumerable<DataFile> Data(GameFiles files)
        {
            TextChange[] events = this.In(TextSource.Event).ToArray();
            foreach (string name in events.Length == 0 ? new string[0] : EventFiles)
            {
                byte[] bytes = files.Read(name);
                string[] texts = EventFile.Read(bytes);
                foreach (bool on in new[] { false, true })
                {
                    if (texts != null && events.All(c => c.Ids[0] < texts.Length && c.IsIn(texts[c.Ids[0]], on)))
                        yield return new DataFile { Name = name, IsOn = on, Off = on ? ChangeEvents(bytes, events, false) : bytes, On = on ? bytes : ChangeEvents(bytes, events, true) };
                }
            }
            TextChange[] tips = this.In(TextSource.Tips).ToArray();
            string tipsText = tips.Length == 0 || files.Read(TipsFile) == null ? null : Ansi.GetString(files.Read(TipsFile));
            foreach (bool on in new[] { false, true })
            {
                if (tipsText != null && tips.All(c => c.IsIn(tipsText, on) && !c.IsIn(tipsText, !on)))
                {
                    string changed = tips.Aggregate(tipsText, (text, c) => c.Apply(text, !on));
                    yield return new DataFile { Name = TipsFile, IsOn = on, Off = on ? Ansi.GetBytes(changed) : files.Read(TipsFile), On = on ? files.Read(TipsFile) : Ansi.GetBytes(changed) };
                }
            }
        }

        // The event file with the changes made (on) or taken back.
        private static byte[] ChangeEvents(byte[] bytes, IEnumerable<TextChange> changes, bool on)
        {
            string[] texts = EventFile.Read(bytes);
            foreach (TextChange change in changes)
                texts[change.Ids[0]] = change.Apply(texts[change.Ids[0]], on);
            return EventFile.Write(bytes, texts);
        }

        /// <summary>
        /// RESDLL.dll without the text patches' changes (the file itself if it isn't readable): how
        /// <see cref="DialogStyles"/> knows the file by its hash.
        /// </summary>
        internal static byte[] PlainResources(byte[] dll) => ResourceTexts.Read(dll)?.Write(new string[0], new Dictionary<long, byte[]>()) ?? dll;

        /// <summary>
        /// <paramref name="plain"/>, RESDLL.dll without text changes, with those of the text patches applied in
        /// <paramref name="dll"/>: when <see cref="DialogStyles"/> changes the dialogs under them.
        /// </summary>
        internal static byte[] WithTextsOf(byte[] dll, byte[] plain)
        {
            List<string> applied = ResourceTexts.Read(dll)?.Applied;
            return applied == null || applied.Count == 0 ? plain : Build(ResourceTexts.Read(plain), new HashSet<string>(applied));
        }

        /// <summary>
        /// The event file with every text patch's changes taken back (the file itself if none is in it, or
        /// it isn't an event file): how patch 1.01 knows the file by its hash.
        /// </summary>
        internal static byte[] PlainEvents(byte[] bytes)
        {
            string[] texts = EventFile.Read(bytes);
            TextChange[] changes = texts == null ? new TextChange[0]
                : InOrder.SelectMany(p => p.In(TextSource.Event)).Where(c => c.Ids[0] < texts.Length && c.IsIn(texts[c.Ids[0]], true)).ToArray();
            return changes.Length == 0 ? bytes : ChangeEvents(bytes, changes, false);
        }
    }

    /// <summary>
    /// An event file (TEXTE\*.saq): the length of the text block and the number of texts (both 32 bit), then the
    /// block, each byte XORed with the byte before it as stored (the first with the length's low byte). The
    /// texts follow each other, each ended by a NUL. The game reads only a block of exactly its length.
    /// </summary>
    internal static class EventFile
    {
        /// <summary>The texts of <paramref name="bytes"/>; null if it isn't an event file.</summary>
        public static string[] Read(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 9 || BitConverter.ToInt32(bytes, 0) != bytes.Length - 8)
                return null;
            byte[] plain = new byte[bytes.Length - 8];
            byte key = bytes[0];
            for (int i = 0; i < plain.Length; i++)
            {
                plain[i] = (byte)(key ^ bytes[8 + i]);
                key = bytes[8 + i];
            }
            // Every text ended by its NUL, as many as the header says.
            string[] texts = TextPatch.Ansi.GetString(plain).Split('\0');
            return plain[plain.Length - 1] == 0 && texts.Length == BitConverter.ToInt32(bytes, 4) + 1 ? texts.Take(texts.Length - 1).ToArray() : null;
        }

        /// <summary><paramref name="texts"/> as an event file, the number of texts as in <paramref name="original"/>.</summary>
        public static byte[] Write(byte[] original, string[] texts)
        {
            byte[] plain = TextPatch.Ansi.GetBytes(string.Concat(texts.Select(t => t + "\0").ToArray()));
            byte[] result = new byte[8 + plain.Length];
            BitConverter.GetBytes(plain.Length).CopyTo(result, 0);
            Array.Copy(original, 4, result, 4, 4);
            byte key = result[0];
            for (int i = 0; i < plain.Length; i++)
                result[8 + i] = key = (byte)(plain[i] ^ key);
            return result;
        }
    }

    /// <summary>A block of a string table: 16 strings, each its length in UTF-16 units and the units.</summary>
    internal static class StringTable
    {
        /// <summary>The 16 strings of the block <paramref name="data"/>; null if it isn't one.</summary>
        public static string[] Read(byte[] data)
        {
            string[] strings = new string[16];
            int at = 0;
            for (int i = 0; i < 16; i++)
            {
                if (data == null || at + 2 > data.Length)
                    return null;
                int length = BitConverter.ToUInt16(data, at);
                if (at + 2 + 2 * length > data.Length)
                    return null;
                strings[i] = Encoding.Unicode.GetString(data, at + 2, 2 * length);
                at += 2 + 2 * length;
            }
            return strings;
        }

        /// <summary>The block holding <paramref name="strings"/>; what <paramref name="original"/> has behind its strings stays.</summary>
        public static byte[] Write(string[] strings, byte[] original)
        {
            List<byte> data = new List<byte>();
            int end = 0;
            for (int i = 0; i < 16; i++)
            {
                data.AddRange(BitConverter.GetBytes((ushort)strings[i].Length));
                data.AddRange(Encoding.Unicode.GetBytes(strings[i]));
                end += 2 + 2 * BitConverter.ToUInt16(original, end);
            }
            return data.Concat(original.Skip(end)).ToArray();
        }
    }

    /// <summary>
    /// A dialog template (DLGTEMPLATE or DLGTEMPLATEEX with its items) split into the texts that may change
    /// (the caption and the controls' texts) and the bytes around them, which stay as they are. Written
    /// back, each item starts on a 4-byte boundary again, padded with zeros.
    /// </summary>
    internal sealed class DialogTexts
    {
        // Raw bytes (byte[]), a text (string) or the start of an item (Item).
        private readonly List<object> parts = new List<object>();
        private static readonly object Item = new object();

        private DialogTexts() { }

        /// <summary>The caption and the controls' texts.</summary>
        public IEnumerable<string> Texts => this.parts.OfType<string>();

        /// <summary>The dialog <paramref name="data"/>; null if it isn't one.</summary>
        public static DialogTexts Read(byte[] data)
        {
            try
            {
                return data == null ? null : Parse(data);
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        private static DialogTexts Parse(byte[] data)
        {
            DialogTexts dialog = new DialogTexts();
            bool extended = U16(data, 0) == 1 && U16(data, 2) == 0xFFFF;
            uint style = BitConverter.ToUInt32(data, extended ? 12 : 0);
            int items = U16(data, extended ? 16 : 8), at = extended ? 26 : 18, done = 0;
            at = SkipNameOrNumber(data, SkipNameOrNumber(data, at));       // menu, class
            dialog.Raw(data, ref done, at);
            dialog.Text(data, ref done, ref at);                            // caption
            if ((style & 0x40) != 0)                                        // DS_SETFONT: size (weight, italic, charset), face
                at = SkipText(data, at + (extended ? 6 : 2));
            for (int i = 0; i < items; i++)
            {
                dialog.Raw(data, ref done, at);
                done = at = Align(at);
                dialog.parts.Add(Item);
                at = SkipNameOrNumber(data, at + (extended ? 24 : 18));     // fixed part, class
                dialog.Raw(data, ref done, at);
                if (U16(data, at) == 0xFFFF)
                    at += 4;                                                // a resource number, no text
                else
                    dialog.Text(data, ref done, ref at);
                at += 2 + U16(data, at);                                    // creation data: its size, then the bytes
            }
            if (at > data.Length)
                throw new ArgumentException("Dialog ends early.");
            dialog.Raw(data, ref done, data.Length);
            return dialog;
        }

        // The bytes from done to end as they are.
        private void Raw(byte[] data, ref int done, int end)
        {
            this.parts.Add(data.Skip(done).Take(end - done).ToArray());
            done = end;
        }

        // The text at at (done = at).
        private void Text(byte[] data, ref int done, ref int at)
        {
            int end = SkipText(data, at);
            this.parts.Add(Encoding.Unicode.GetString(data, at, end - at - 2));
            done = at = end;
        }

        /// <summary>Changes every text by <paramref name="change"/>.</summary>
        public void Change(Func<string, string> change)
        {
            for (int i = 0; i < this.parts.Count; i++)
            {
                if (this.parts[i] is string text)
                    this.parts[i] = change(text);
            }
        }

        /// <summary>The dialog template.</summary>
        public byte[] Write()
        {
            List<byte> data = new List<byte>();
            foreach (object part in this.parts)
            {
                if (part == Item)
                    data.AddRange(new byte[Align(data.Count) - data.Count]);
                else if (part is string text)
                    data.AddRange(Encoding.Unicode.GetBytes(text + "\0"));
                else
                    data.AddRange((byte[])part);
            }
            return data.ToArray();
        }

        private static int Align(int at) => (at + 3) & ~3;

        private static int U16(byte[] data, int at) =>
            at + 2 <= data.Length ? BitConverter.ToUInt16(data, at) : throw new ArgumentException("Dialog ends early.");

        // Behind the name or number (FFFF and a word) at at.
        private static int SkipNameOrNumber(byte[] data, int at) => U16(data, at) == 0xFFFF ? at + 4 : SkipText(data, at);

        // Behind the NUL that ends the text at at.
        private static int SkipText(byte[] data, int at)
        {
            while (U16(data, at) != 0)
                at += 2;
            return at + 2;
        }
    }

    /// <summary>
    /// The string tables and dialogs of RESDLL.dll, the game's resources, and the section the text patches
    /// add to it: ".a2gtxt", holding "A2GPatchTexts\0", the number of patches applied and their names (each
    /// ended by a NUL), the number of resources moved and for each where its data entry is in the file and the
    /// address and size it had, then the moved resources' data. The resource directory points there. Without
    /// the section and with the entries back, the file is the original to the byte.
    /// </summary>
    internal sealed class ResourceTexts
    {
        /// <summary>The section's name.</summary>
        internal const string SectionName = ".a2gtxt";
        /// <summary>At the start of the section.</summary>
        internal static readonly byte[] Marker = Encoding.ASCII.GetBytes("A2GPatchTexts\0");
        /// <summary>Resource types: string table blocks, dialogs.</summary>
        internal const int StringType = 6, DialogType = 5;

        private readonly byte[] file;
        private readonly PeFile pe;
        // Per resource (type and id, see Key): where its data entry is in the file.
        private readonly Dictionary<long, long> entries = new Dictionary<long, long>();
        // Per data entry moved into the section: the address and size it had.
        private readonly Dictionary<long, uint[]> moved = new Dictionary<long, uint[]>();

        /// <summary>The names of the patches applied (<see cref="TextPatch.Key"/>).</summary>
        public List<string> Applied { get; } = new List<string>();

        private ResourceTexts(byte[] file, PeFile pe)
        {
            this.file = file;
            this.pe = pe;
        }

        /// <summary>The key of the resource <paramref name="id"/> of <paramref name="type"/>.</summary>
        internal static long Key(int type, int id) => (long)type << 32 | (uint)id;

        /// <summary>The resources of <paramref name="file"/>; null if it isn't a DLL with resources or its text section isn't A2G-Patch's.</summary>
        public static ResourceTexts Read(byte[] file)
        {
            PeFile pe = file == null ? null : PeFile.Read(file);
            if (pe == null || pe.ResourceRva == 0)
                return null;
            ResourceTexts texts = new ResourceTexts(file, pe);
            try
            {
                return texts.Parse() ? texts : null;
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        private bool Parse()
        {
            long root = this.Offset(this.pe.ResourceRva);
            foreach (KeyValuePair<int, long> type in this.Directory(root, root))
            {
                foreach (KeyValuePair<int, long> id in this.Directory(root, type.Value))
                {
                    // The first language; RESDLL.dll has only German.
                    foreach (KeyValuePair<int, long> language in this.Directory(root, id.Value).Take(1))
                        this.entries[Key(type.Key, id.Key)] = language.Value;
                }
            }
            PeFile.Section last = this.pe.Sections.Last();
            if (last.Name != SectionName)
                return true;
            long at = last.Raw;
            if (!this.Bytes(at, Marker.Length).SequenceEqual(Marker))
                return false;
            at += Marker.Length;
            for (uint count = this.U32(ref at); count > 0; count--)
            {
                StringBuilder name = new StringBuilder();
                for (byte c = this.Bytes(at++, 1)[0]; c != 0; c = this.Bytes(at++, 1)[0])
                    name.Append((char)c);
                this.Applied.Add(name.ToString());
            }
            for (uint count = this.U32(ref at); count > 0; count--)
            {
                long entry = this.U32(ref at);
                this.moved[entry] = new[] { this.U32(ref at), this.U32(ref at) };
            }
            return true;
        }

        // The numbered entries of the resource directory at "at": id and where the subdirectory or data entry is.
        private IEnumerable<KeyValuePair<int, long>> Directory(long root, long at)
        {
            long next = at + 16;
            int named = BitConverter.ToUInt16(this.Bytes(at + 12, 2), 0), numbered = BitConverter.ToUInt16(this.Bytes(at + 14, 2), 0);
            next += 8 * named;
            for (int i = 0; i < numbered; i++, next += 8)
                yield return new KeyValuePair<int, long>((int)this.U32(next), root + (this.U32(next + 4) & 0x7FFFFFFF));
        }

        private byte[] Bytes(long at, int count)
        {
            if (at < 0 || count < 0 || at + count > this.file.Length)
                throw new ArgumentException("Outside the file.");
            byte[] bytes = new byte[count];
            Array.Copy(this.file, at, bytes, 0, count);
            return bytes;
        }

        private uint U32(long at) => BitConverter.ToUInt32(this.Bytes(at, 4), 0);

        private uint U32(ref long at)
        {
            uint value = this.U32(at);
            at += 4;
            return value;
        }

        // Where the address rva lies in the file.
        private long Offset(uint rva)
        {
            long at = this.pe.FileOffset(this.pe.ImageBase + rva);
            return at >= 0 ? at : throw new ArgumentException("Outside the sections.");
        }

        /// <summary>The original data of the resource <paramref name="id"/> of <paramref name="type"/>; null if there is none.</summary>
        public byte[] Original(int type, int id)
        {
            if (!this.entries.TryGetValue(Key(type, id), out long entry))
                return null;
            uint[] was = this.OriginalEntry(entry);
            return this.Bytes(this.Offset(was[0]), (int)was[1]);
        }

        // The address and size the data entry at "entry" has in the original file.
        private uint[] OriginalEntry(long entry) => this.moved.TryGetValue(entry, out uint[] was) ? was : new[] { this.U32(entry), this.U32(entry + 4) };

        /// <summary>The original string <paramref name="id"/>; null if there is none.</summary>
        public string OriginalString(int id) => StringTable.Read(this.Original(StringType, id / 16 + 1))?[id % 16];

        /// <summary>The original dialog <paramref name="id"/>; null if there is none.</summary>
        public DialogTexts OriginalDialog(int id) => DialogTexts.Read(this.Original(DialogType, id));

        /// <summary>
        /// The file with the patches <paramref name="applied"/> listed and the resources <paramref name="changed"/>
        /// (by <see cref="Key"/>) moved into the section; the original file if both are empty.
        /// </summary>
        public byte[] Write(IEnumerable<string> applied, Dictionary<long, byte[]> changed)
        {
            byte[] original = (byte[])this.file.Clone();
            foreach (KeyValuePair<long, uint[]> entry in this.moved)
            {
                PeFile.Put(original, entry.Key, entry.Value[0]);
                PeFile.Put(original, entry.Key + 4, entry.Value[1]);
            }
            if (this.pe.Sections.Last().Name == SectionName)
                original = PeFile.RemoveLastSection(original);
            string[] names = applied.ToArray();
            if (names.Length == 0 && changed.Count == 0)
                return original;

            uint va = PeFile.Read(original).SizeOfImage;
            List<byte> data = new List<byte>(Marker);
            data.AddRange(BitConverter.GetBytes(names.Length));
            foreach (string name in names)
                data.AddRange(Encoding.ASCII.GetBytes(name + "\0"));
            data.AddRange(BitConverter.GetBytes(changed.Count));
            List<KeyValuePair<long, byte[]>> resources = changed.Select(c => new KeyValuePair<long, byte[]>(this.entries[c.Key], c.Value)).ToList();
            foreach (KeyValuePair<long, byte[]> resource in resources)
            {
                data.AddRange(BitConverter.GetBytes((uint)resource.Key));
                foreach (uint value in this.OriginalEntry(resource.Key))   // address, size
                    data.AddRange(BitConverter.GetBytes(value));
            }
            List<uint> at = new List<uint>();
            foreach (KeyValuePair<long, byte[]> resource in resources)
            {
                data.AddRange(new byte[(4 - data.Count % 4) % 4]);
                at.Add(va + (uint)data.Count);
                data.AddRange(resource.Value);
            }
            byte[] result = PeFile.AddSection(original, SectionName, data.ToArray(), PeFile.ReadOnlyData);
            for (int i = 0; i < resources.Count; i++)
            {
                PeFile.Put(result, resources[i].Key, at[i]);
                PeFile.Put(result, resources[i].Key + 4, (uint)resources[i].Value.Length);
            }
            return result;
        }
    }
}
