using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using ToyFactory.UI;

namespace ToyFactory.Tests.EditMode
{
    /// <summary>The local top-10 leaderboard: order, cap, ties, names and the JSON file.</summary>
    public class LeaderboardTests
    {
        string _directory;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "FactoryResetLeaderboard_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_directory))
                Directory.Delete(_directory, true);
        }

        string BoardPath => Path.Combine(_directory, "board.json");

        static int AddRun(Leaderboard board, string name, int score, float seconds = 600f) =>
            board.Add(name, score, ScoreRules.GradeFor(score), seconds, RunOutcome.Won, new DateTime(2026, 10, 10));

        [Test]
        public void BoardKeepsTheHighestScoreFirst()
        {
            var board = new Leaderboard(BoardPath);
            AddRun(board, "LOW", 1000);
            AddRun(board, "HIGH", 9000);
            AddRun(board, "MID", 5000);

            CollectionAssert.AreEqual(new[] { "HIGH", "MID", "LOW" }, board.Entries.Select(e => e.name).ToArray());
        }

        [Test]
        public void AddReportsTheOneBasedPlace()
        {
            var board = new Leaderboard(BoardPath);
            Assert.AreEqual(1, AddRun(board, "A", 5000));
            Assert.AreEqual(2, AddRun(board, "B", 3000));
            Assert.AreEqual(1, AddRun(board, "C", 7000));
        }

        [Test]
        public void EqualScoresGoToTheShorterRunAndThenToTheOlderEntry()
        {
            var board = new Leaderboard(BoardPath);
            AddRun(board, "SLOW", 5000, 900f);
            AddRun(board, "FAST", 5000, 600f);
            AddRun(board, "SAME", 5000, 600f);

            CollectionAssert.AreEqual(new[] { "FAST", "SAME", "SLOW" }, board.Entries.Select(e => e.name).ToArray());
        }

        [Test]
        public void BoardHoldsTenAndDropsTheWorst()
        {
            var board = new Leaderboard(BoardPath);
            for (int i = 1; i <= 12; i++)
                AddRun(board, "P" + i, i * 100);

            Assert.AreEqual(Leaderboard.Capacity, board.Entries.Count);
            Assert.AreEqual(1200, board.Entries[0].score);
            Assert.AreEqual(300, board.Entries[Leaderboard.Capacity - 1].score, "The two lowest, 100 and 200, are gone.");
        }

        [Test]
        public void ARunBelowAFullBoardDoesNotPlaceAndAddsNothing()
        {
            var board = new Leaderboard(BoardPath);
            for (int i = 1; i <= Leaderboard.Capacity; i++)
                AddRun(board, "P" + i, i * 100 + 1000);

            Assert.IsFalse(board.Qualifies(1000, 100f));
            Assert.AreEqual(0, AddRun(board, "LATE", 1000));
            Assert.AreEqual(Leaderboard.Capacity, board.Entries.Count);
            Assert.IsFalse(board.Entries.Any(e => e.name == "LATE"));
        }

        [Test]
        public void ATieWithTheLastPlaceNeedsAShorterTimeToPlace()
        {
            var board = new Leaderboard(BoardPath);
            for (int i = 1; i <= Leaderboard.Capacity; i++)
                AddRun(board, "P" + i, i * 100, 600f);

            Assert.IsFalse(board.Qualifies(100, 600f), "Same score and time as the last place: the older entry stays.");
            Assert.IsTrue(board.Qualifies(100, 500f));
        }

        [Test]
        public void ARunWithNoPointsNeverPlaces()
        {
            var board = new Leaderboard(BoardPath);

            Assert.IsFalse(board.Qualifies(0, 10f));
            Assert.AreEqual(0, AddRun(board, "ZERO", 0));
            Assert.AreEqual(0, board.Entries.Count);
        }

        [TestCase("  Bob  ", "Bob")]
        [TestCase("", Leaderboard.DefaultName)]
        [TestCase(null, Leaderboard.DefaultName)]
        [TestCase("   ", Leaderboard.DefaultName)]
        [TestCase("A very long player name", "A very long")]
        [TestCase("tab\there\nnow", "tabherenow")]
        public void NamesAreCleanedAndCut(string typed, string expected)
        {
            Assert.AreEqual(expected, Leaderboard.CleanName(typed));
        }

        [Test]
        public void SavedBoardComesBackTheSame()
        {
            var board = new Leaderboard(BoardPath);
            AddRun(board, "ONE", 9000, 700.5f);
            AddRun(board, "TWO", 4000, 810f);
            Assert.IsTrue(board.Save());

            var loaded = new Leaderboard(BoardPath);
            Assert.IsTrue(loaded.Load());

            Assert.AreEqual(2, loaded.Entries.Count);
            Assert.AreEqual("ONE", loaded.Entries[0].name);
            Assert.AreEqual(9000, loaded.Entries[0].score);
            Assert.AreEqual(700.5f, loaded.Entries[0].seconds, 1e-3f);
            Assert.AreEqual("Won", loaded.Entries[0].outcome);
            Assert.AreEqual("2026-10-10", loaded.Entries[0].date);
            Assert.AreEqual(ScoreGrade.B.ToString(), loaded.Entries[0].grade);
        }

        [Test]
        public void SaveCreatesTheFolderAndLeavesNoTemporaryFile()
        {
            string nested = Path.Combine(_directory, "deeper", "board.json");
            var board = new Leaderboard(nested);
            AddRun(board, "ONE", 100);

            Assert.IsTrue(board.Save());
            Assert.IsTrue(File.Exists(nested));
            Assert.IsFalse(File.Exists(nested + ".tmp"));
        }

        [Test]
        public void SavingAgainReplacesTheOldFile()
        {
            var board = new Leaderboard(BoardPath);
            AddRun(board, "ONE", 100);
            board.Save();
            AddRun(board, "TWO", 200);
            Assert.IsTrue(board.Save());

            var loaded = new Leaderboard(BoardPath);
            loaded.Load();
            Assert.AreEqual(2, loaded.Entries.Count);
        }

        [Test]
        public void MissingFileGivesAnEmptyBoard()
        {
            var board = new Leaderboard(BoardPath);

            Assert.IsFalse(board.Load());
            Assert.AreEqual(0, board.Entries.Count);
        }

        [TestCase("")]
        [TestCase("   ")]
        [TestCase("not json at all")]
        [TestCase("{\"entries\": [ {\"name\": ")]
        [TestCase("[1, 2, 3]")]
        [TestCase("null")]
        public void CorruptFileGivesAnEmptyBoardWithoutThrowing(string content)
        {
            File.WriteAllText(BoardPath, content);
            var board = new Leaderboard(BoardPath);
            AddRun(board, "STALE", 100);

            bool read = true;
            Assert.DoesNotThrow(() => read = board.Load());

            Assert.AreEqual(0, board.Entries.Count, "Whatever was in memory is replaced.");
            Assert.IsFalse(read);
        }

        [Test]
        public void HandEditedFileIsCleanedSortedAndCut()
        {
            string json = "{\"version\":1,\"entries\":["
                + "{\"name\":\"\",\"score\":-5,\"grade\":\"\",\"seconds\":-1,\"outcome\":\"?\",\"date\":\"\"},"
                + "{\"name\":\"BEST\",\"score\":9000,\"grade\":\"B\",\"seconds\":10,\"outcome\":\"Won\",\"date\":\"2026-10-10\"}"
                + "]}";
            File.WriteAllText(BoardPath, json);
            var board = new Leaderboard(BoardPath);

            Assert.IsTrue(board.Load());

            Assert.AreEqual("BEST", board.Entries[0].name);
            LeaderboardEntry bad = board.Entries[1];
            Assert.AreEqual(Leaderboard.DefaultName, bad.name);
            Assert.AreEqual(0, bad.score);
            Assert.AreEqual(0f, bad.seconds);
            Assert.AreEqual("Recalled", bad.outcome);
            Assert.AreEqual(ScoreGrade.D.ToString(), bad.grade);
        }

        [Test]
        public void FileWithMoreThanTenRowsIsCutToTen()
        {
            var board = new Leaderboard(BoardPath);
            for (int i = 1; i <= Leaderboard.Capacity; i++)
                AddRun(board, "P" + i, i * 100);
            board.Save();

            string text = File.ReadAllText(BoardPath);
            string extra = "{\"name\":\"EXTRA\",\"score\":99999,\"grade\":\"S\",\"seconds\":1,\"outcome\":\"Won\",\"date\":\"\"},";
            File.WriteAllText(BoardPath, text.Replace("\"entries\": [", "\"entries\": [" + extra));

            var loaded = new Leaderboard(BoardPath);
            loaded.Load();

            Assert.AreEqual(Leaderboard.Capacity, loaded.Entries.Count);
            Assert.AreEqual("EXTRA", loaded.Entries[0].name);
            Assert.AreEqual(200, loaded.Entries[Leaderboard.Capacity - 1].score);
        }

        [Test]
        public void UnwritablePathReportsFailureWithoutThrowing()
        {
            // A file where a folder is needed makes the path impossible to create.
            string blocker = Path.Combine(_directory, "blocker");
            File.WriteAllText(blocker, "x");
            var board = new Leaderboard(Path.Combine(blocker, "board.json"));
            AddRun(board, "ONE", 100);

            bool saved = true;
            Assert.DoesNotThrow(() => saved = board.Save());
            Assert.IsFalse(saved);
            Assert.AreEqual(1, board.Entries.Count, "The in-memory board is unharmed.");
        }
    }
}
