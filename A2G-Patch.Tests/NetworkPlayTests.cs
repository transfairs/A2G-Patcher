using A2GPatch;
using System;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace A2GPatch.Tests
{
    /// <summary>Tests for <see cref="NetworkPlay"/>: where it is offered, and the DLL and ini it writes.</summary>
    // Replaces GameInstall.DllBytes, which GameInstallTests and PatchFormTests use as well.
    [Collection("GameInstall statics")]
    public class NetworkPlayTests
    {
        private static readonly NetworkPlay Network = Patch.All.OfType<NetworkPlay>().Single();

        // An exe image holding each of the network layer's places once (wildcards as 0).
        private static byte[] Exe(int skip = -1)
        {
            byte[] image = new byte[0x400];
            int at = 0x10;
            for (int i = 0; i < NetworkPlay.Places.Length; i++)
            {
                if (i == skip)
                    continue;
                foreach (byte? b in NetworkPlay.Places[i])
                    image[at++] = b ?? 0;
                at += 0x10;
            }
            return image;
        }

        // A stand-in for A2G-Patch's msvfw32.dll: some bytes, the marker, flags all set.
        private static byte[] OurDll() => new byte[] { 1, 2, 3 }.Concat(GameInstall.DllMarker).Concat(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 7 }).ToArray();

        private static byte[] Ini(string text) => Encoding.ASCII.GetBytes(text);
        private static string Text(byte[] bytes) => Encoding.ASCII.GetString(bytes);

        private static void WithDll(byte[] dll, Action body)
        {
            Func<byte[]> previous = GameInstall.DllBytes;
            GameInstall.DllBytes = () => dll;
            try
            {
                body();
            }
            finally
            {
                GameInstall.DllBytes = previous;
            }
        }

        [Fact]
        public void IsAnExtraWithoutFlag()
        {
            Assert.Equal("Netzwerkspiel", Network.Title);
            Assert.Equal(PatchGroup.Extras, Network.Group);
            Assert.Equal(0u, Network.Flag);
            Assert.NotEmpty(Network.Name);
        }

        [Fact]
        public void State_IsUnknown_WithoutAllPlaces_OrWithoutExe()
        {
            using (GameFolder folder = new GameFolder())
            {
                Assert.Equal(FileState.Unknown, Network.State(new GameFiles(folder.Path, null)));
                for (int i = 0; i < NetworkPlay.Places.Length; i++)
                    Assert.Equal(FileState.Unknown, Network.State(new GameFiles(folder.Path, Exe(skip: i))));
                byte[] twice = Exe();
                NetworkPlay.Places[0].Select(b => b ?? 0).ToArray().CopyTo(twice, 0x300);
                Assert.Equal(FileState.Unknown, Network.State(new GameFiles(folder.Path, twice)));
            }
        }

        [Fact]
        public void State_FollowsTheDllAndTheIni()
        {
            using (GameFolder folder = new GameFolder())
            {
                GameFiles files = new GameFiles(folder.Path, Exe());
                Assert.Equal(FileState.Off, Network.State(files));

                files.Write(GameInstall.DllName, OurDll());
                Assert.Equal(FileState.Off, Network.State(files));
                files.Write(NetworkPlay.IniName, Ini("[A2GNet]\r\nEnabled=0\r\n"));
                Assert.Equal(FileState.Off, Network.State(files));
                files.Write(NetworkPlay.IniName, Ini("[A2GNet]\r\nMode=host\r\nEnabled = 1\r\n"));
                Assert.Equal(FileState.On, Network.State(files));

                files.Write(GameInstall.DllName, new byte[] { 1, 2, 3 });     // someone else's msvfw32.dll
                Assert.Equal(FileState.Unknown, Network.State(files));
                files.Write(GameInstall.DllName, null);
                Assert.Equal(FileState.Off, Network.State(files));
            }
        }

        [Fact]
        public void IsEnabled_ReadsTheKeyAsANumber()
        {
            Assert.False(NetworkPlay.IsEnabled(null));
            Assert.False(NetworkPlay.IsEnabled(Ini("[A2GNet]\nMode=host\n")));
            Assert.False(NetworkPlay.IsEnabled(Ini("Enabled=x")));
            Assert.False(NetworkPlay.IsEnabled(Ini("EnabledLog=1")));
            Assert.False(NetworkPlay.IsEnabled(Ini("Enabled")));
            Assert.True(NetworkPlay.IsEnabled(Ini("  enabled=2\n")));
        }

        [Fact]
        public void SetEnabled_ChangesOnlyTheKey()
        {
            Assert.Equal("[A2GNet]\r\nEnabled=1\r\n", Text(NetworkPlay.SetEnabled(null, true)));
            Assert.Equal("[A2GNet]\r\nEnabled=0\r\nHost=x\r\n", Text(NetworkPlay.SetEnabled(Ini("[A2GNet]\r\nEnabled=1\r\nHost=x\r\n"), false)));
            Assert.Equal("[a2gnet]\r\nEnabled=1\r\nHost=x\r\n", Text(NetworkPlay.SetEnabled(Ini("[a2gnet]\nHost=x"), true)));
            Assert.Equal("[Other]\r\nA=1\r\n[A2GNet]\r\nEnabled=1\r\n", Text(NetworkPlay.SetEnabled(Ini("[Other]\r\nA=1\r\n"), true)));
        }

        [Fact]
        public void SwitchOn_WritesTheDllWithoutPatchFlags_AndEnablesTheIni()
        {
            using (GameFolder folder = new GameFolder())
            {
                File.WriteAllBytes(folder.File(NetworkPlay.IniName), Ini("[A2GNet]\r\nEnabled=0\r\nFirstName=Gerd\r\n"));
                GameFiles files = new GameFiles(folder.Path, Exe());
                WithDll(OurDll(), () => Network.Switch(files, true));

                byte[] dll = files.Read(GameInstall.DllName);
                int marker = GameInstall.MarkerAt(dll);
                Assert.Equal(new byte[4], dll.Skip(marker + GameInstall.DllMarker.Length).Take(GameInstall.FlagsBytes));
                Assert.Equal(7, dll[dll.Length - 1]);
                Assert.Equal("[A2GNet]\r\nEnabled=1\r\nFirstName=Gerd\r\n", Text(files.Read(NetworkPlay.IniName)));
                Assert.Equal(FileState.On, Network.State(files));
            }
        }

        [Fact]
        public void SwitchOn_FailsWithoutAnEmbeddedDll()
        {
            using (GameFolder folder = new GameFolder())
            {
                GameFiles files = new GameFiles(folder.Path, Exe());
                WithDll(null, () => Assert.Throws<InvalidOperationException>(() => Network.Switch(files, true)));
                WithDll(new byte[] { 1, 2 }, () => Assert.Throws<InvalidOperationException>(() => Network.Switch(files, true)));
                Assert.Empty(files.Changes);
            }
        }

        [Fact]
        public void SwitchOff_RemovesTheDll_AndKeepsTheSettings()
        {
            using (GameFolder folder = new GameFolder())
            {
                File.WriteAllBytes(folder.File(GameInstall.DllName), OurDll());
                File.WriteAllBytes(folder.File(NetworkPlay.IniName), Ini("[A2GNet]\r\nEnabled=1\r\nHost=10.0.0.2\r\n"));
                GameFiles files = new GameFiles(folder.Path, Exe());
                Network.Switch(files, false);

                Assert.Null(files.Read(GameInstall.DllName));
                Assert.Equal("[A2GNet]\r\nEnabled=0\r\nHost=10.0.0.2\r\n", Text(files.Read(NetworkPlay.IniName)));

                GameFiles empty = new GameFiles(new GameFolder().Path, Exe());
                Network.Switch(empty, false);
                Assert.Empty(empty.Changes);
            }
        }
    }
}
