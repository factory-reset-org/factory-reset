using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using ToyFactory.UI;

namespace ToyFactory.Tests.EditMode
{
    /// <summary>
    /// The credits say what the project's own records say, in the order the screen promises: the two modules,
    /// the members under each with their role, then the third-party resources.
    /// </summary>
    public class CreditsContentTests
    {
        static string ProjectFile(params string[] parts) =>
            File.ReadAllText(Path.GetFullPath(Path.Combine(new[] { Application.dataPath, ".." }.Concat(parts).ToArray())));

        static string ThirdParty(string pack) =>
            File.ReadAllText(Path.Combine(Application.dataPath, "_ThirdParty", pack, "README.md"));

        static string[] Texts() => CreditsContent.Build().Select(e => e.Text).ToArray();

        static int IndexOf(CreditsKind kind, string text) =>
            Array.FindIndex(CreditsContent.Build().ToArray(), e => e.Kind == kind && e.Text == text);

        [Test]
        public void TheCreditsStartWithTheGameAndTheTwoModulesAndEndWithAThankYou()
        {
            CreditsEntry[] entries = CreditsContent.Build().ToArray();
            Assert.AreEqual(CreditsKind.Title, entries[0].Kind);
            Assert.AreEqual(CreditsContent.GameName, entries[0].Text);

            int gv = Array.FindIndex(entries, e => e.Kind == CreditsKind.Module && e.Text == CreditsContent.GvModule);
            int iis = Array.FindIndex(entries, e => e.Kind == CreditsKind.Module && e.Text == CreditsContent.IsModule);
            int intro = Array.FindIndex(entries, e => e.Kind == CreditsKind.Paragraph);
            Assert.That(gv, Is.GreaterThan(0).And.LessThan(iis));
            Assert.That(intro, Is.GreaterThan(iis));
            StringAssert.Contains("assignment project for both modules", entries[intro].Text);

            Assert.AreEqual(CreditsKind.Closing, entries[entries.Length - 1].Kind);
            Assert.AreEqual(CreditsContent.ThankYou, entries[entries.Length - 1].Text);
        }

        [Test]
        public void TheModulesComeBeforeTheMembersAndTheMembersBeforeTheThirdPartyResources()
        {
            int gvSection = IndexOf(CreditsKind.Section, CreditsContent.GvModule);
            int isSection = IndexOf(CreditsKind.Section, CreditsContent.IsModule);
            int thirdParty = IndexOf(CreditsKind.Section, "Third-party resources");
            int intro = Array.FindIndex(CreditsContent.Build().ToArray(), e => e.Kind == CreditsKind.Paragraph);

            Assert.That(gvSection, Is.GreaterThan(intro));
            Assert.That(isSection, Is.GreaterThan(gvSection));
            Assert.That(thirdParty, Is.GreaterThan(isSection));
        }

        [Test]
        public void EveryMemberIsListedUnderBothModulesWithTheirRoleThere()
        {
            CreditsEntry[] entries = CreditsContent.Build().ToArray();
            int gvSection = IndexOf(CreditsKind.Section, CreditsContent.GvModule);
            int isSection = IndexOf(CreditsKind.Section, CreditsContent.IsModule);
            int thirdParty = IndexOf(CreditsKind.Section, "Third-party resources");

            foreach (TeamMember member in CreditsContent.Members)
            {
                int nameInGv = Array.FindIndex(entries, gvSection, isSection - gvSection, e => e.Kind == CreditsKind.Name && e.Text == member.Name);
                Assert.GreaterOrEqual(nameInGv, 0, member.Name + " under Graphics and Visualization");
                Assert.AreEqual(member.GvRole, entries[nameInGv + 1].Text);
                Assert.AreEqual(CreditsKind.Role, entries[nameInGv + 1].Kind);

                int nameInIs = Array.FindIndex(entries, isSection, thirdParty - isSection, e => e.Kind == CreditsKind.Name && e.Text == member.Name);
                Assert.GreaterOrEqual(nameInIs, 0, member.Name + " under Intelligent Systems");
                Assert.AreEqual(member.IsAgent + " developer", entries[nameInIs + 1].Text);
                Assert.AreEqual(member.IsMethod, entries[nameInIs + 2].Text);
            }

            Assert.AreEqual(4, CreditsContent.Members.Count);
            CollectionAssert.AllItemsAreUnique(CreditsContent.Members.Select(m => m.Name));
        }

        [Test]
        public void TheRolesAreTheOnesInTheReadmeTeamTable()
        {
            string[] rows = ProjectFile("README.md").Split('\n').Where(l => l.Length > 4 && l.StartsWith("| S") && char.IsDigit(l[3])).ToArray();
            Assert.AreEqual(CreditsContent.Members.Count, rows.Length, "One README row per member.");

            foreach (TeamMember member in CreditsContent.Members)
            {
                string[] cells = rows.First(r => r.StartsWith("| " + member.Student + " |")).Split('|').Select(c => c.Trim()).ToArray();
                Assert.AreEqual(member.GvRole, cells[2], member.Student + " Graphics and Visualization role");
                StringAssert.StartsWith(member.IsAgent, cells[3], member.Student + " agent");
                Assert.AreEqual(member.IsMethod, cells[4], member.Student + " architecture");
            }
        }

        [Test]
        public void SOneAndSFourAreCreditedByTheirFullNames()
        {
            // The credits show full names; the CODEOWNERS file keeps S1's GitHub handle, which is not a credit.
            Assert.AreEqual("Onel De Silva", CreditsContent.Members[0].Name);
            Assert.AreEqual("Omira Kularatne", CreditsContent.Members[3].Name);
            StringAssert.Contains("@OnelDJ", ProjectFile(".github", "CODEOWNERS"));
        }

        [Test]
        public void EveryThirdPartyCreditTheGameMustShowIsInTheCredits()
        {
            string[] texts = Texts();
            foreach (string line in ThirdPartyCredits.Lines())
                CollectionAssert.Contains(texts, line);
        }

        [Test]
        public void EveryMusicComposerInTheMusicReadmeIsInTheCredits()
        {
            string all = string.Join("\n", Texts());
            foreach (string row in ThirdParty("Music").Split('\n').Where(l => l.StartsWith("| ") && (l.Contains("| CC0 1.0 |") || l.Contains("| Eric Matyas |"))))
                StringAssert.Contains(row.Split('|')[4].Trim(), all, row);
        }

        [Test]
        public void TheKenneyPacksAndThePolyPizzaAuthorsAreInTheCredits()
        {
            string all = string.Join("\n", Texts());
            foreach (string row in ThirdParty("Kenney").Split('\n').Where(l => l.StartsWith("| ") && l.Contains("kenney.nl/assets/")))
                StringAssert.Contains(row.Split('|')[1].Trim(), all, row);

            // The CC0 authors of the Poly Pizza props are thanked in the courtesy line.
            foreach (string row in ThirdParty("PolyPizza").Split('\n').Where(l => l.StartsWith("| `") && l.Contains("CC0 1.0")))
                StringAssert.Contains(row.Split('|')[3].Trim(), all, row);
        }

        [Test]
        public void TheToolsLineMatchesTheProjectsUnityVersionAndTheReadmesBlender()
        {
            string version = ProjectFile("ProjectSettings", "ProjectVersion.txt");
            string unity = version.Split('\n').First(l => l.StartsWith("m_EditorVersion:")).Split(':')[1].Trim();
            StringAssert.Contains(unity, CreditsContent.ToolsLine);
            StringAssert.Contains("Blender **5.2.0 LTS**", ProjectFile("README.md"), "The README names the Blender version, in bold.");
            StringAssert.Contains("Blender 5.2.0 LTS", CreditsContent.ToolsLine);
        }

        [Test]
        public void EveryLineHasTextAndTheContentIsBuiltTheSameEachTime()
        {
            foreach (CreditsEntry entry in CreditsContent.Build())
                Assert.IsFalse(string.IsNullOrWhiteSpace(entry.Text), entry.Kind.ToString());

            CollectionAssert.AreEqual(Texts(), Texts());
        }
    }
}
