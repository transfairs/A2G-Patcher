using System;
using System.Linq;
using Xunit;

namespace A2GPatch.Tests
{
    /// <summary>Tests for SoundFormat: the rewritten DirectSound setup and where its data goes, and refusing what it doesn't know.</summary>
    public class SoundFormatTests
    {
        private static readonly SoundFormat Format = Patch.Get<SoundFormat>();

        // The way out of the failed DirectSoundCreate, the 121 bytes and the function's end: the original exe
        // from 0x6534B1, GOG's run.exe from 0x6582E1.
        internal static readonly byte[] OriginalCode = (
            "E89A000000893560E1CA0033C05F5E83C460C20C00" +
            "A160E1CA006A0157508B08FF511885C07415E873000000893560E1CA0033C05F5E83C460C20C00" +
            "8B542470B91800000033C08D7C2408F3ABA160E1CA0089156CE1CA008D542408C70564E1CA0001000000" +
            "C7442408600000008B085250FF511085C075158B4424748B4C2434F7D81BC0F7D023C1A374E1CA00" +
            "5FB8010000005E83C460C20C00").Hex();
        internal static readonly byte[] GogCode = (
            "E89A0000008935A012CB0033C05F5E83C460C20C00" +
            "A1A012CB006A0157508B08FF511885C07415E8730000008935A012CB0033C05F5E83C460C20C00" +
            "8B542470B91800000033C08D7C2408F3ABA1A012CB008915AC12CB008D542408C705A412CB0001000000" +
            "C7442408600000008B085250FF511085C075158B4424748B4C2434F7D81BC0F7D023C1A3B412CB00" +
            "5FB8010000005E83C460C20C00").Hex();

        // The code goes behind 16 bytes of filler, then 0x300 zeros give the room in front of the stub's.
        private const int CodeAt = (int)TestExe.CodeAt + 16 + 21;

        private static byte[] Exe(byte[] code, int zeros = 0x300) =>
            TestExe.Build(_ => Enumerable.Repeat((byte)0xCC, 16).Concat(code).Concat(new byte[zeros]).ToArray());

        private static byte[] Apply(byte[] image, Site[] sites)
        {
            byte[] result = (byte[])image.Clone();
            foreach (Site site in sites)
                site.Patched.CopyTo(result, site.Offset);
            return result;
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Find_RewritesTheSetup_WithTheDataInFrontOfTheStubsRoom(bool gog)
        {
            byte[] code = gog ? GogCode : OriginalCode;
            byte[] image = Exe(code);
            Site[] sites = Format.Find(image);

            // The code section ends at 0x400 + 16 + 155 + 0x300; the helper's room on 16 bytes, the stub's and the data's in front.
            long helper = (TestExe.CodeAt + 16 + 155 + 0x300 - 177) & ~15L;
            long dataAt = helper - InterviewEffects.StubSize - SoundFormat.DataRoom;
            uint dataVa = TestExe.Base + TestExe.CodeRva + (uint)(dataAt - TestExe.CodeAt);
            Assert.Equal(new[] { dataAt, CodeAt }, sites.Select(s => s.Offset));
            Assert.All(sites, s => Assert.False(s.IsPatched));
            Assert.Equal(new byte[38], sites[0].Original);
            Assert.Equal(SoundFormat.Data, sites[0].Patched);
            Assert.Equal(code.Skip(21).Take(121), sites[1].Original);

            uint ds = BitConverter.ToUInt32(code, 7);
            uint open = BitConverter.ToUInt32(code, 21 + 73), argument = BitConverter.ToUInt32(code, 21 + 63), buffers = BitConverter.ToUInt32(code, 21 + 117);
            Assert.Equal(SoundFormat.PatchedCode(ds, open, argument, buffers, dataVa).Select(b => b.Value), sites[1].Patched);
            Assert.Equal(121, sites[1].Patched.Length);
            Assert.Equal(new byte[] { 0x6A, 0x02 }, sites[1].Patched.Skip(5).Take(2));                  // the priority level
            Assert.Equal(new byte[] { 0x75, 0xD9 }, sites[1].Patched.Skip(16).Take(2));                 // a failure: the way out in front
            Assert.Equal(dataVa, BitConverter.ToUInt32(sites[1].Patched, 84));
            Assert.Equal(dataVa + 0x14, BitConverter.ToUInt32(sites[1].Patched, 105));

            byte[] patched = Apply(image, sites);
            Site[] again = Format.Find(patched);
            Assert.All(again, s => Assert.True(s.IsPatched));
            Assert.Equal(sites.Select(s => s.Original), again.Select(s => s.Original));
            Assert.Equal(sites.Select(s => s.Patched), again.Select(s => s.Patched));
            Assert.All(Format.Patterns, p => Assert.Single(Patch.FindAll(image, p).Concat(Patch.FindAll(patched, p)).ToList()));
        }

        [Fact]
        public void Find_PutsTheData_BehindTheVirtualEndOfASectionThatIsntCode()
        {
            // The import section (0x200 bytes in the file) loaded with 0x100: room behind it.
            byte[] image = Exe(OriginalCode).With(TestExe.SectionsAt + 8, 0x100);
            Site[] sites = Format.Find(image);

            Assert.Equal(0x200 + 0x200 - SoundFormat.DataRoom, sites[0].Offset);
            Assert.Equal(TestExe.Base + TestExe.ImportRva + 0x200 - SoundFormat.DataRoom, BitConverter.ToUInt32(sites[1].Patched, 84));

            // Loaded with its whole size or none given: the code section's room as before.
            long inCode = Format.Find(Exe(OriginalCode))[0].Offset;
            Assert.Equal(inCode, Format.Find(Exe(OriginalCode).With(TestExe.SectionsAt + 8, 0))[0].Offset);
        }

        [Fact]
        public void Find_RefusesUnknownCodeAndTakenOrMissingRoom()
        {
            byte[] image = Exe(OriginalCode);
            Site[] sites = Format.Find(image);
            byte[] patched = Apply(image, sites);

            Assert.Null(Format.Find(Exe(OriginalCode.Concat(OriginalCode).ToArray())));                    // twice
            Assert.Null(Format.Find(Exe(new byte[16])));                                                     // not there
            Assert.Null(Format.Find(GameCode.Exe(OriginalCode)));                                            // no exe
            Assert.Null(Format.Find(image.With(CodeAt + 19, 0x74)));                                       // the second way out leads elsewhere
            Assert.Null(Format.Find(image.With(CodeAt + 57, 0xCAE180)));                                   // another DirectSound pointer
            Assert.Null(Format.Find(patched.With(CodeAt + 89, 0xCAE180)));
            Assert.Null(Format.Find(image.With((int)sites[0].Offset + 8, 1)));                             // the data's room taken
            Assert.Null(Format.Find(patched.With((int)sites[0].Offset, 0)));                               // patched without the data
            Assert.Null(Format.Find(Exe(OriginalCode, 0x40)));                                              // no room in front of the stub's
            Assert.Null(Format.Find(image.With(TestExe.SectionsAt + 40 + 36, 0x40000040)));                // no code section
        }
    }
}
