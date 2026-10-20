using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using ToyFactory.UI;

namespace ToyFactory.Tests.EditMode
{
    /// <summary>
    /// The CC BY 3.0 models need their title, author and licence shown in the game, not only in the READMEs.
    /// These tests keep the title screen's credits and the READMEs saying the same thing.
    /// </summary>
    public class ThirdPartyCreditsTests
    {
        static string Readme(string pack) =>
            File.ReadAllText(Path.Combine(Application.dataPath, "_ThirdParty", pack, "README.md"));

        [Test]
        public void EveryCreditNamesItsModelAuthorAndLicence()
        {
            foreach (ModelCredit credit in ThirdPartyCredits.Attribution)
            {
                Assert.IsNotEmpty(credit.Model);
                Assert.IsNotEmpty(credit.Author, credit.Model);
                StringAssert.StartsWith("CC BY", credit.Licence, credit.Model);
                StringAssert.StartsWith("https://", credit.Url, credit.Model);
                StringAssert.Contains(credit.Author, credit.Line);
                StringAssert.Contains(credit.Model, credit.Line);
                StringAssert.Contains(credit.Licence, credit.Line);
            }
        }

        [Test]
        public void EveryCreditInTheGameIsInThePolyPizzaReadmeWithTheSameLink()
        {
            string readme = Readme("PolyPizza");
            foreach (ModelCredit credit in ThirdPartyCredits.Attribution)
            {
                StringAssert.Contains(credit.Author, readme, credit.Model);
                StringAssert.Contains(credit.Url, readme, credit.Model);
            }
        }

        [Test]
        public void EveryCcByModelInTheReadmeIsCreditedInTheGame()
        {
            // Rows of the README's credits table that carry the CC BY licence.
            string[] rows = Readme("PolyPizza").Split('\n').Where(l => l.StartsWith("| `") && l.Contains("CC BY 3.0")).ToArray();
            Assert.AreEqual(ThirdPartyCredits.Attribution.Count, rows.Length, "One credit in the game per CC BY row in the README.");
            foreach (string row in rows)
                Assert.IsTrue(ThirdPartyCredits.Attribution.Any(c => row.Contains(c.Author)), row);
        }

        [Test]
        public void TheLinesAreOnePerCreditThenTheThankYouThenTheMusic()
        {
            string[] lines = ThirdPartyCredits.Lines().ToArray();
            int models = ThirdPartyCredits.Attribution.Count;
            Assert.AreEqual(models + 4, lines.Length);
            Assert.AreEqual(ThirdPartyCredits.CourtesyLine, lines[models]);
            Assert.AreEqual(ThirdPartyCredits.MusicLine, lines[models + 1]);
            Assert.AreEqual(ThirdPartyCredits.MusicTracksLine, lines[models + 2]);
            Assert.AreEqual(ThirdPartyCredits.MusicCourtesyLine, lines[models + 3]);
        }

        [Test]
        public void TheMusicCreditNamesEricMatyasHisSiteAndEveryOneOfHisTracksInTheReadme()
        {
            // His terms: "Music by Eric Matyas" and "www.soundimage.org" in the game's credits, with
            // the titles of his tracks when other composers' music is used too.
            StringAssert.Contains("Music by Eric Matyas", ThirdPartyCredits.MusicLine);
            StringAssert.Contains("www.soundimage.org", ThirdPartyCredits.MusicLine);
            string[] rows = Readme("Music").Split('\n').Where(l => l.StartsWith("| ") && l.Contains("| Eric Matyas |")).ToArray();
            Assert.IsNotEmpty(rows, "The music README lists his tracks.");
            foreach (string row in rows)
            {
                string title = row.Split('|')[3].Trim().Replace(" (looping)", "").Replace(" (v2)", "");
                StringAssert.Contains(title, ThirdPartyCredits.MusicTracksLine, "His track " + title + " is named in the game.");
            }

            // Every other composer in the README is CC0 and thanked.
            foreach (string row in Readme("Music").Split('\n').Where(l => l.StartsWith("| ") && l.Contains("| CC0 1.0 |")))
                StringAssert.Contains(row.Split('|')[4].Trim(), ThirdPartyCredits.MusicCourtesyLine);
        }
    }
}
