using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace A2GPatch.Tests
{
    /// <summary>Tests for the quiz file and the switches that bring its questions up to date and add new ones.</summary>
    public class QuizPatchesTests
    {
        private static readonly QuizAnswers Quiz = Patch.Get<QuizAnswers>();
        private static readonly QuizQuestions More = Patch.Get<QuizQuestions>();

        // A quiz with every question the switch changes, as the game has them (on false) or as changed, spread over
        // the three levels, and one more question in each level that stays.
        private static byte[] QuizBytes(bool on)
        {
            List<QuizQuestion> questions = QuizAnswers.List.Select((c, i) => new QuizQuestion
            {
                Level = i % 3,
                Question = (on ? c.To : c.From)[0],
                Answers = (on ? c.To : c.From)[1],
            }).ToList();
            for (int level = 0; level < 3; level++)
                questions.Add(new QuizQuestion { Level = level, Question = "Frage " + level, Answers = "richtig,falsch,falsch,falsch" });
            return QuizFile.Write(questions);
        }

        private static GameFiles FilesWith(GameFolder folder, byte[] quiz)
        {
            Directory.CreateDirectory(folder.File("TEXTE"));
            if (quiz != null)
                File.WriteAllBytes(folder.File(QuizPatch.File), quiz);
            return new GameFiles(folder.Path, null);
        }

        [Fact]
        public void TheSwitches_AreTextsWithoutFlag_ForEveryRelease()
        {
            foreach (QuizPatch patch in new QuizPatch[] { Quiz, More })
            {
                Assert.Equal(PatchGroup.Texts, patch.Group);
                Assert.Equal(0u, patch.Flag);
                Assert.False(patch.NeedsExe);
                Assert.Contains(GameInstall.FileSwitches, s => s.Key == patch && s.Value == patch);
            }
            Assert.Equal("Quizfragen", Quiz.Title);
            Assert.Equal("Neue Quizfragen", More.Title);
            Assert.Same(QuizAnswers.List, Quiz.Changes);
            Assert.Same(QuizQuestions.List, More.Changes);
        }

        // The game takes the first answer as the right one and two of the next three; it copies the answers into a
        // buffer of 500 bytes.
        [Fact]
        public void Changes_KeepFourAnswers_AndFitTheGamesBuffer()
        {
            foreach (QuizChange change in QuizAnswers.List.Concat(QuizQuestions.List))
            {
                Assert.True(change.To[1].Split(',').Length >= 4, change.To[0]);
                Assert.True(TextPatch.Ansi.GetByteCount(change.To[1]) < 500, change.To[0]);
                Assert.NotEqual(change.From, change.To);
            }
            // The new ones: four answers each, in every level.
            Assert.Equal(150, QuizQuestions.List.Length);
            Assert.All(QuizQuestions.List, c => Assert.Null(c.From));
            Assert.All(QuizQuestions.List, c => Assert.Equal(4, c.To[1].Split(',').Length));
            Assert.Equal(new[] { 46, 59, 45 }, Enumerable.Range(0, 3).Select(l => QuizQuestions.List.Count(c => c.Level == l)));
            // Each question as the game has it and as changed, and each new one, is found only once.
            string[] texts = QuizAnswers.List.SelectMany(c => new[] { c.From, c.To }).Concat(QuizQuestions.List.Select(c => c.To))
                .Select(t => t[0] + "|" + t[1]).ToArray();
            Assert.Equal(texts.Length, texts.Distinct().Count());
            string[] questions = QuizQuestions.List.Select(c => c.To[0]).ToArray();
            Assert.Equal(questions.Length, questions.Distinct().Count());
        }

        [Fact]
        public void File_ReadsAndWritesBack_ToTheByte()
        {
            byte[] bytes = QuizBytes(false);
            List<QuizQuestion> questions = QuizFile.Read(bytes);
            Assert.Equal(QuizAnswers.List.Length + 3, questions.Count);
            Assert.Equal(new[] { 0, 1, 2 }, questions.Select(q => q.Level).Distinct());
            Assert.Equal(questions.OrderBy(q => q.Level), questions);
            Assert.Equal(bytes, QuizFile.Write(questions));
            // The header: questions per level, texts, block length; the texts inverted.
            Assert.Equal(questions.Count(q => q.Level == 1), BitConverter.ToInt32(bytes, 4));
            Assert.Equal(2 * questions.Count, BitConverter.ToInt32(bytes, 12));
            Assert.Equal(bytes.Length - 20 - 8 * questions.Count, BitConverter.ToInt32(bytes, 16));
            Assert.Equal((byte)~(byte)QuizAnswers.List[0].From[0][0], bytes[20 + 8 * questions.Count]);
        }

        [Fact]
        public void File_IsNotRead_WhenItDoesNotHoldWhatItsHeaderSays()
        {
            byte[] good = QuizBytes(false);
            Assert.Null(QuizFile.Read(null));
            Assert.Null(QuizFile.Read(new byte[19]));
            Assert.Null(QuizFile.Read(Changed(good, b => b[0]++)));                    // a question more than texts
            Assert.Null(QuizFile.Read(good.Take(good.Length - 1).ToArray()));          // shorter than the block
            Assert.Null(QuizFile.Read(Changed(good, b => b[b.Length - 1] = 0)));       // a NUL too few
            Assert.Null(QuizFile.Read(Changed(good, b => b[b.Length - 2] = 0xFF)));    // a NUL too many
            Assert.Null(QuizFile.Read(Changed(good, b =>                              // the NUL a byte early: something behind the last text
            {
                b[b.Length - 2] = 0xFF;
                b[b.Length - 1] = 0;
            })));
            Assert.Null(QuizFile.Read(Changed(good, b => b[24]++)));                   // the second text not behind the first
            Assert.Empty(QuizFile.Read(new byte[20]));
        }

        private static byte[] Changed(byte[] bytes, Action<byte[]> change)
        {
            byte[] copy = (byte[])bytes.Clone();
            change(copy);
            return copy;
        }

        [Fact]
        public void State_FollowsTheQuestions()
        {
            using (GameFolder folder = new GameFolder())
                Assert.Equal(FileState.Unknown, Quiz.State(FilesWith(folder, null)));
            using (GameFolder folder = new GameFolder())
                Assert.Equal(FileState.Off, Quiz.State(FilesWith(folder, QuizBytes(false))));
            using (GameFolder folder = new GameFolder())
                Assert.Equal(FileState.On, Quiz.State(FilesWith(folder, QuizBytes(true))));
            // One question neither as the game has it nor as changed.
            List<QuizQuestion> questions = QuizFile.Read(QuizBytes(false));
            questions[0].Answers = "anders,falsch,falsch,falsch";
            using (GameFolder folder = new GameFolder())
                Assert.Equal(FileState.Unknown, Quiz.State(FilesWith(folder, QuizFile.Write(questions))));
        }

        [Fact]
        public void Switch_ChangesTheQuestions_AndBackToTheByte()
        {
            byte[] off = QuizBytes(false), on = QuizBytes(true);
            using (GameFolder folder = new GameFolder())
            {
                GameFiles files = FilesWith(folder, off);
                Quiz.Switch(files, true);
                Assert.Equal(on, files.Read(QuizPatch.File));
                Quiz.Switch(files, false);
                Assert.Equal(off, files.Read(QuizPatch.File));
            }
            // Nothing is written where the questions are as wanted already.
            using (GameFolder folder = new GameFolder())
            {
                GameFiles files = FilesWith(folder, on);
                Quiz.Switch(files, true);
                Assert.Empty(files.Changes);
            }
        }

        [Fact]
        public void NewQuestions_GoToTheEndOfTheirLevel_AndAwayAgain()
        {
            byte[] off = QuizBytes(false);
            using (GameFolder folder = new GameFolder())
            {
                GameFiles files = FilesWith(folder, off);
                Assert.Equal(FileState.Off, More.State(files));
                More.Switch(files, true);
                List<QuizQuestion> questions = QuizFile.Read(files.Read(QuizPatch.File));
                Assert.Equal(QuizAnswers.List.Length + 3 + 150, questions.Count);
                // Behind the questions of level 1 that were there, before those of level 2.
                QuizQuestion first = questions.Single(q => q.Question == QuizQuestions.List[0].To[0]);
                Assert.Equal(0, first.Level);
                Assert.Equal("Frage 0", questions[questions.IndexOf(first) - 1].Question);
                Assert.Equal(FileState.On, More.State(files));
                Assert.Equal(FileState.Off, Quiz.State(files));
                More.Switch(files, false);
                Assert.Equal(off, files.Read(QuizPatch.File));
            }
            // Some of them there: neither on nor off.
            List<QuizQuestion> some = QuizFile.Read(off);
            some.Add(new QuizQuestion { Level = 1, Question = QuizQuestions.List[100].To[0], Answers = QuizQuestions.List[100].To[1] });
            using (GameFolder folder = new GameFolder())
                Assert.Equal(FileState.Unknown, More.State(FilesWith(folder, QuizFile.Write(some))));
        }

        [Fact]
        public void BothSwitches_ChangeTheFileIndependently()
        {
            byte[] off = QuizBytes(false);
            using (GameFolder folder = new GameFolder())
            {
                GameFiles files = FilesWith(folder, off);
                More.Switch(files, true);
                Quiz.Switch(files, true);
                Assert.Equal(FileState.On, Quiz.State(files));
                Assert.Equal(FileState.On, More.State(files));
                More.Switch(files, false);
                Assert.Equal(QuizBytes(true), files.Read(QuizPatch.File));
                More.Switch(files, true);
                Quiz.Switch(files, false);
                Assert.Equal(FileState.Off, Quiz.State(files));
                Assert.Equal(FileState.On, More.State(files));
                More.Switch(files, false);
                Assert.Equal(off, files.Read(QuizPatch.File));
            }
        }
    }
}
