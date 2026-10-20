using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ToyFactory.UI;

namespace ToyFactory.Tests.EditMode
{
    /// <summary>The HUD's collapsible parts: what Tab and H remember, which row the folded panel keeps, and the words on screen.</summary>
    public class HudSectionsTests
    {
        sealed class Store
        {
            public readonly Dictionary<string, int> Values = new Dictionary<string, int>();

            public HudSections Open() => new HudSections(key => Values.TryGetValue(key, out int v) ? v : 0, (key, v) => Values[key] = v);
        }

        static HudTaskRow Row(string id, bool done = false) => new HudTaskRow(id, id, done);

        // ---- What is remembered

        [Test]
        public void EverythingStartsOpenAndShown()
        {
            HudSections sections = new Store().Open();
            Assert.IsFalse(sections.ObjectivesCollapsed);
            Assert.IsFalse(sections.HintsHidden);
        }

        [Test]
        public void TabAndHToggleTheirOwnPartOnly()
        {
            HudSections sections = new Store().Open();

            sections.ToggleObjectives();
            Assert.IsTrue(sections.ObjectivesCollapsed);
            Assert.IsFalse(sections.HintsHidden);

            sections.ToggleHints();
            Assert.IsTrue(sections.HintsHidden);

            sections.ToggleObjectives();
            Assert.IsFalse(sections.ObjectivesCollapsed);
            Assert.IsTrue(sections.HintsHidden);
        }

        [Test]
        public void TheChoicesComeBackTheNextRun()
        {
            var store = new Store();
            HudSections first = store.Open();
            first.ToggleObjectives();
            first.ToggleHints();

            HudSections next = store.Open();
            Assert.IsTrue(next.ObjectivesCollapsed);
            Assert.IsTrue(next.HintsHidden);

            next.ToggleHints();
            Assert.IsFalse(store.Open().HintsHidden);
            Assert.IsTrue(store.Open().ObjectivesCollapsed, "Opening the hints leaves the panel as it was.");
        }

        [Test]
        public void ChoicesAreStoredUnderTheirOwnKeysAsZeroOrOne()
        {
            var store = new Store();
            HudSections sections = store.Open();
            sections.ToggleObjectives();

            Assert.AreEqual(1, store.Values[HudSections.ObjectivesKey]);
            sections.ToggleObjectives();
            Assert.AreEqual(0, store.Values[HudSections.ObjectivesKey]);
            Assert.IsFalse(store.Values.ContainsKey(HudSections.HintsKey), "Nothing is written for a part that was not touched.");
        }

        // ---- The row the folded panel keeps

        [Test]
        public void TheFoldedPanelKeepsTheCurrentTask()
        {
            var rows = new List<HudTaskRow> { Row("a", done: true), Row("b"), Row("c") };
            Assert.AreEqual(2, HudSections.CompactRow(rows, "c"));
        }

        [Test]
        public void WithNoCurrentTaskItKeepsTheFirstOneNotDone()
        {
            var rows = new List<HudTaskRow> { Row("a", done: true), Row("b"), Row("c") };
            Assert.AreEqual(1, HudSections.CompactRow(rows, null));
            Assert.AreEqual(1, HudSections.CompactRow(rows, "not a task"));
        }

        [Test]
        public void ACurrentTaskThatIsAlreadyDoneIsNotKept()
        {
            var rows = new List<HudTaskRow> { Row("a", done: true), Row("b") };
            Assert.AreEqual(1, HudSections.CompactRow(rows, "a"));
        }

        [Test]
        public void WhenEverythingIsDoneItKeepsTheLastRow()
        {
            var rows = new List<HudTaskRow> { Row("a", done: true), Row("b", done: true) };
            Assert.AreEqual(1, HudSections.CompactRow(rows, null));
        }

        [Test]
        public void NoRowsMeansNoRowIsKept()
        {
            Assert.AreEqual(-1, HudSections.CompactRow(new List<HudTaskRow>(), "a"));
            Assert.AreEqual(-1, HudSections.CompactRow(null, "a"));
        }

        // ---- The words

        [TestCase(0, 6, "0 of 6 DONE")]
        [TestCase(5, 6, "5 of 6 DONE")]
        [TestCase(0, 0, "")]
        public void TheSummaryCountsTheTasksDone(int done, int total, string expected)
        {
            Assert.AreEqual(expected, HudSections.Summary(done, total));
        }

        [Test]
        public void TheChipSaysWhatTabWillDo()
        {
            Assert.AreEqual("TAB  LESS", HudSections.ObjectivesChip(false));
            Assert.AreEqual("TAB  MORE", HudSections.ObjectivesChip(true));
        }

        [Test]
        public void TheKeyHintsAndTheTitleScreenTeachTabAndH()
        {
            StringAssert.Contains("Tab", HudMath.KeyHints);
            StringAssert.Contains("H", HudMath.KeyHints);
            Assert.IsTrue(TitleView.Controls.Any(c => c[0] == "Tab"), "Tab is on the title screen's controls.");
            Assert.IsTrue(TitleView.Controls.Any(c => c[0] == "H"), "H is on the title screen's controls.");
        }
    }
}
