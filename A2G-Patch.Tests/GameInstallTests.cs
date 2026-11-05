using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Xunit;

namespace A2GPatch.Tests
{
    /// <summary>A temporary game folder, deleted again (read-only flags included) on dispose.</summary>
    internal sealed class GameFolder : IDisposable
    {
        public readonly string Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "a2g-patch-" + Guid.NewGuid().ToString("N"));

        public GameFolder() => Directory.CreateDirectory(this.Path);

        public string File(string name) => System.IO.Path.Combine(this.Path, name);

        public void Dispose()
        {
            foreach (string file in Directory.GetFiles(this.Path))
                System.IO.File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(this.Path, true);
        }
    }

    /// <summary>Tests for GameInstall: how the patches apply in a folder, and applying and taking them back.</summary>
    // Shares the replaceable statics (DllBytes, IsGameRunning) with PatchFormTests, so they mustn't run in parallel.
    [Collection("GameInstall statics")]
    public class GameInstallTests
    {
        internal static readonly Patch Goals = Patch.All[0];
        internal static readonly Patch Arrow = Patch.All[1];
        // Stands for A2G-Patch's msvfw32.dll: the marker with the flags byte after it.
        internal static readonly byte[] OwnDll = Encoding.ASCII.GetBytes("MZ...A2GPatchFlags\0\x03...");

        // Runs body with DllBytes returning dll.
        internal static void WithDll(byte[] dll, Action body)
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

        private static byte Flags(GameFolder folder)
        {
            byte[] dll = File.ReadAllBytes(folder.File(GameInstall.DllName));
            return dll[GameInstall.MarkerAt(dll) + GameInstall.DllMarker.Length];
        }

        [Theory]
        [InlineData("anstoss2.exe")]
        [InlineData("run.exe")]
        public void Exe_IsPatchedPerPatch_WithABackup_AndRestoredExactly(string name)
        {
            using (GameFolder folder = new GameFolder())
            {
                byte[] original = GameCode.Both();
                File.WriteAllBytes(folder.File(name), original);
                GameInstall install = GameInstall.Detect(folder.Path);
                Assert.Equal(PatchKind.Exe, install.Kind);
                Assert.Equal(folder.File(name), install.ExePath);
                Assert.True(install.CanApply(Goals) && install.CanApply(Arrow));
                Assert.False(install.IsApplied(Goals) || install.IsApplied(Arrow));

                install.Apply(new[] { Arrow });
                Assert.Equal(original, File.ReadAllBytes(folder.File(name) + GameInstall.BackupSuffix));
                GameInstall arrowOnly = GameInstall.Detect(folder.Path);
                Assert.True(arrowOnly.IsApplied(Arrow));
                Assert.False(arrowOnly.IsApplied(Goals));

                arrowOnly.Apply(new[] { Arrow, Goals });
                Assert.True(GameInstall.Detect(folder.Path).IsApplied(Goals));
                // The same choice again changes nothing.
                arrowOnly.Apply(new[] { Goals, Arrow });

                GameInstall.Detect(folder.Path).Apply(new Patch[0]);
                Assert.Equal(original, File.ReadAllBytes(folder.File(name)));
                Assert.False(File.Exists(folder.File(name) + GameInstall.BackupSuffix));
            }
        }

        [Fact]
        public void Exe_WithOnlyOnePatchsCode_OffersOnlyThatOne()
        {
            using (GameFolder folder = new GameFolder())
            {
                File.WriteAllBytes(folder.File("run.exe"), GameCode.Exe(GameCode.Arrow()));
                GameInstall install = GameInstall.Detect(folder.Path);

                Assert.Equal(PatchKind.Exe, install.Kind);
                Assert.True(install.CanApply(Arrow));
                Assert.False(install.CanApply(Goals));
                Assert.False(install.IsApplied(Goals));
            }
        }

        // An older backup that doesn't match the restored exe (e.g. the exe was updated since) is kept.
        [Fact]
        public void Restore_KeepsABackupThatDiffersFromTheExe()
        {
            using (GameFolder folder = new GameFolder())
            {
                File.WriteAllBytes(folder.File("anstoss2.exe"), GameCode.Both());
                File.WriteAllBytes(folder.File("anstoss2.exe" + GameInstall.BackupSuffix), new byte[] { 1, 2, 3 });

                GameInstall.Detect(folder.Path).Apply(new[] { Goals });
                Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(folder.File("anstoss2.exe" + GameInstall.BackupSuffix)));
                GameInstall.Detect(folder.Path).Apply(new Patch[0]);

                Assert.True(File.Exists(folder.File("anstoss2.exe" + GameInstall.BackupSuffix)));
            }
        }

        // The 2007 CD: anstoss2.exe is the launcher, run.exe packed - neither holds a pattern.
        [Fact]
        public void PackedRunExe_GetsTheDllWithTheChosenFlags_AndLosesItAgain() => WithDll(OwnDll, () =>
        {
            using (GameFolder folder = new GameFolder())
            {
                File.WriteAllBytes(folder.File("anstoss2.exe"), new byte[] { 0x4D, 0x5A });
                File.WriteAllBytes(folder.File("run.exe"), new byte[] { 0x4D, 0x5A, 0x90 });
                GameInstall install = GameInstall.Detect(folder.Path);
                Assert.Equal(PatchKind.Dll, install.Kind);
                Assert.Equal(folder.File("run.exe"), install.ExePath);
                Assert.True(install.CanApply(Goals) && install.CanApply(Arrow));
                Assert.False(install.ForeignDll);

                install.Apply(new[] { Arrow });
                Assert.Equal(2, Flags(folder));
                GameInstall arrowOnly = GameInstall.Detect(folder.Path);
                Assert.True(arrowOnly.IsApplied(Arrow));
                Assert.False(arrowOnly.IsApplied(Goals));
                // The same choice again changes nothing.
                arrowOnly.Apply(new[] { Arrow });

                arrowOnly.Apply(new[] { Arrow, Goals });
                Assert.Equal(3, Flags(folder));

                GameInstall.Detect(folder.Path).Apply(new Patch[0]);
                Assert.False(File.Exists(folder.File(GameInstall.DllName)));
                Assert.Equal(new byte[] { 0x4D, 0x5A, 0x90 }, File.ReadAllBytes(folder.File("run.exe")));
                // The embedded DLL itself stays as built.
                Assert.Equal(3, OwnDll[GameInstall.MarkerAt(OwnDll) + GameInstall.DllMarker.Length]);
            }
        });

        [Fact]
        public void SomeoneElsesDll_IsNeitherOverwrittenNorDeleted()
        {
            using (GameFolder folder = new GameFolder())
            {
                File.WriteAllBytes(folder.File("run.exe"), new byte[] { 0x4D, 0x5A });
                File.WriteAllBytes(folder.File(GameInstall.DllName), new byte[] { 0x4D, 0x5A, 1 });
                GameInstall install = GameInstall.Detect(folder.Path);

                Assert.True(install.ForeignDll);
                Assert.False(install.CanApply(Goals));
                Assert.False(install.IsApplied(Goals));
                Assert.Throws<InvalidOperationException>(() => install.Apply(new[] { Goals }));
                Assert.Equal(new byte[] { 0x4D, 0x5A, 1 }, File.ReadAllBytes(folder.File(GameInstall.DllName)));
            }
        }

        [Theory]
        [InlineData(null)]
        [InlineData(new byte[] { 0x4D, 0x5A })]
        public void ABuildWithoutTheDll_SaysSo(byte[] dll) => WithDll(dll, () =>
        {
            using (GameFolder folder = new GameFolder())
            {
                File.WriteAllBytes(folder.File("run.exe"), new byte[] { 0x4D, 0x5A });

                Assert.Throws<InvalidOperationException>(() => GameInstall.Detect(folder.Path).Apply(new[] { Goals }));
                Assert.False(File.Exists(folder.File(GameInstall.DllName)));
            }
        });

        // A msvfw32.dll of an older A2G-Patch is replaced on applying, even with the same choice; the
        // same version only differing in its flags is not "outdated".
        [Fact]
        public void DllOfAnotherVersion_IsReplaced_EvenWithTheSameChoice() => WithDll(OwnDll, () =>
        {
            using (GameFolder folder = new GameFolder())
            {
                byte[] older = Encoding.ASCII.GetBytes("MZ.,.A2GPatchFlags\0\x02...");
                File.WriteAllBytes(folder.File("run.exe"), new byte[] { 0x4D, 0x5A });
                File.WriteAllBytes(folder.File(GameInstall.DllName), older);
                GameInstall install = GameInstall.Detect(folder.Path);
                Assert.True(install.DllOutdated);
                Assert.True(install.IsApplied(Arrow));
                Assert.Equal("run.exe ist gepackt. Die msvfw32.dll im Ordner stammt von einer anderen Version von A2G-Patch; „Übernehmen“ ersetzt sie.", PatchForm.StatusText(install));

                install.Apply(new[] { Arrow });

                byte[] expected = (byte[])OwnDll.Clone();
                expected[GameInstall.MarkerAt(expected) + GameInstall.DllMarker.Length] = 2;
                Assert.Equal(expected, File.ReadAllBytes(folder.File(GameInstall.DllName)));
                Assert.False(install.DllOutdated);
                Assert.False(GameInstall.Detect(folder.Path).DllOutdated);

                // Taking everything back still deletes an outdated one.
                File.WriteAllBytes(folder.File(GameInstall.DllName), older);
                GameInstall.Detect(folder.Path).Apply(new Patch[0]);
                Assert.False(File.Exists(folder.File(GameInstall.DllName)));
            }
        });

        [Fact]
        public void DllOfAnotherLength_OrWithoutAnEmbeddedOne_IsJudgedCorrectly()
        {
            using (GameFolder folder = new GameFolder())
            {
                File.WriteAllBytes(folder.File("run.exe"), new byte[] { 0x4D, 0x5A });
                File.WriteAllBytes(folder.File(GameInstall.DllName), Encoding.ASCII.GetBytes("MZ...A2GPatchFlags\0\x03....."));
                WithDll(OwnDll, () => Assert.True(GameInstall.Detect(folder.Path).DllOutdated));
                WithDll(null, () => Assert.False(GameInstall.Detect(folder.Path).DllOutdated));
                File.WriteAllBytes(folder.File(GameInstall.DllName), Encoding.ASCII.GetBytes("MZ...A2GPatchFlags\0\x01..."));
                WithDll(OwnDll, () => Assert.False(GameInstall.Detect(folder.Path).DllOutdated));
            }
        }

        // Built with dll\msvfw32.dll present (as in the release), the exe carries it, all patches on.
        [Fact]
        public void EmbeddedDll_IsOurs_WithAllPatchesOn()
        {
            byte[] dll = GameInstall.DllBytes();
            if (dll != null)
                Assert.Equal(Patch.All.Aggregate(0, (f, p) => f | p.Flag), dll[GameInstall.MarkerAt(dll) + GameInstall.DllMarker.Length]);
        }

        [Fact]
        public void MarkerAt_NeedsExactlyOneMarkerWithAFlagsByte()
        {
            byte[] marker = GameInstall.DllMarker;
            Assert.Equal(1, GameInstall.MarkerAt(new byte[] { 0 }.Concat(marker).Concat(new byte[] { 3 }).ToArray()));
            Assert.Equal(-1, GameInstall.MarkerAt(marker));
            Assert.Equal(-1, GameInstall.MarkerAt(marker.Concat(new byte[] { 3 }).Concat(marker).Concat(new byte[] { 3 }).ToArray()));
        }

        public static IEnumerable<object[]> UnknownFolders => new[]
        {
            new object[] { null },                                                        // no exe at all
            new object[] { GameCode.Exe(GameCode.Goals(), GameCode.Goals(), GameCode.Arrow(), GameCode.Arrow()) }, // ambiguous
            new object[] { GameCode.Exe(GoalLimits.LimitPattern) },                       // a pattern, but not the code
        };

        [Theory]
        [MemberData(nameof(UnknownFolders))]
        public void UnknownCode_IsNotTouched(byte[] runExe)
        {
            using (GameFolder folder = new GameFolder())
            {
                // anstoss2.exe with ambiguous code: not patchable, so run.exe decides.
                File.WriteAllBytes(folder.File("anstoss2.exe"), GameCode.Exe(GameCode.Goals(), GameCode.Goals()));
                if (runExe != null)
                    File.WriteAllBytes(folder.File("run.exe"), runExe);
                else
                    File.Delete(folder.File("anstoss2.exe"));
                GameInstall install = GameInstall.Detect(folder.Path);

                Assert.Equal(PatchKind.None, install.Kind);
                Assert.Null(install.ExePath);
                Assert.False(install.CanApply(Goals));
                Assert.Throws<InvalidOperationException>(() => install.Apply(new[] { Goals }));
            }
        }

        [Fact]
        public void CandidateFolders_AreExistingFoldersInOrderWithoutDuplicates()
        {
            using (GameFolder cd = new GameFolder())
            using (GameFolder gog = new GameFolder())
            {
                Dictionary<string, string> registry = new Dictionary<string, string>
                {
                    [@"SOFTWARE\Ascaron\ANSTOSS 2\1.00|InstalledTo"] = cd.Path + "\\",
                    [@"SOFTWARE\GOG.com\Games\1808817480|path"] = gog.Path,
                };
                Func<string, string, string> read = (key, value) => registry.TryGetValue(key + "|" + value, out string folder) ? folder : null;

                // The registry's CD folder (with a trailing backslash) is the program's own, the GOG one was given in other case.
                Assert.Equal(new[] { gog.Path.ToUpperInvariant(), cd.Path }, GameInstall.CandidateFolders(gog.Path.ToUpperInvariant(), cd.Path, read));
                Assert.Equal(new[] { cd.Path, gog.Path }, GameInstall.CandidateFolders(null, @"C:\gibt-es-nicht-a2g", read));
                Assert.Empty(GameInstall.CandidateFolders("", null, (k, v) => null));
            }
        }

        [Fact]
        public void ReadRegistry_WithoutTheKey_IsNull()
        {
            Assert.Null(GameInstall.ReadRegistry(@"SOFTWARE\A2G-Patch-Test-gibt-es-nicht", "path"));
            Assert.Null(GameInstall.ReadRegistry(@"SOFTWARE\Microsoft\Windows\CurrentVersion", "a2g-gibt-es-nicht"));
        }

        [Fact]
        public void IsGameRunning_IsFalseForAFolderWithoutAGame()
        {
            using (GameFolder folder = new GameFolder())
            {
                Assert.False(GameInstall.IsGameRunning(folder.Path));
                // This test host itself runs, but isn't a game exe.
                Assert.False(GameInstall.IsGameRunning(AppDomain.CurrentDomain.BaseDirectory));
            }
        }

        // A 32-bit ping copied as run.exe stands for the game (this test host is 32-bit as well, so it can
        // look into it); a running copy under another name doesn't count.
        [Theory]
        [InlineData("run.exe", true)]
        [InlineData("ping.exe", false)]
        public void IsGameRunning_FindsAGameExeRunningFromTheFolder(string name, bool expected)
        {
            using (GameFolder folder = new GameFolder())
            {
                File.Copy(Path.Combine(Environment.GetEnvironmentVariable("SystemRoot"), @"SysWOW64\ping.exe"), folder.File(name));
                ProcessStartInfo start = new ProcessStartInfo(folder.File(name), "-n 60 127.0.0.1") { CreateNoWindow = true, UseShellExecute = false };
                using (Process game = Process.Start(start))
                {
                    try
                    {
                        // Until the process has loaded its main module it can't be looked into.
                        bool running = false;
                        for (int i = 0; i < 50 && !running; i++)
                        {
                            running = GameInstall.IsGameRunning(folder.Path + "\\");
                            if (expected && !running)
                                Thread.Sleep(100);
                        }
                        Assert.Equal(expected, running);
                    }
                    finally
                    {
                        game.Kill();
                        game.WaitForExit();
                    }
                }
            }
        }

        [Fact]
        public void ReadResource_WithoutTheResource_IsNull()
        {
            Assert.Null(GameInstall.ReadResource("A2GPatch.gibt-es-nicht"));
        }
    }
}
