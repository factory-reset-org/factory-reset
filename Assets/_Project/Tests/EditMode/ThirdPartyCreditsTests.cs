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
        public void TheLinesAreOnePerCreditThenTheThankYou()
        {
            string[] lines = ThirdPartyCredits.Lines().ToArray();
            Assert.AreEqual(ThirdPartyCredits.Attribution.Count + 1, lines.Length);
            Assert.AreEqual(ThirdPartyCredits.CourtesyLine, lines[lines.Length - 1]);
        }
    }
}
