using System.Collections.Generic;

namespace ToyFactory.UI
{
    /// <summary>One third-party model that needs a credit.</summary>
    public readonly struct ModelCredit
    {
        /// <summary>The model's title on the site it came from.</summary>
        public readonly string Model;

        /// <summary>Its author.</summary>
        public readonly string Author;

        /// <summary>Its licence, as the site names it.</summary>
        public readonly string Licence;

        /// <summary>Where it was downloaded.</summary>
        public readonly string Url;

        /// <summary>Creates a credit.</summary>
        public ModelCredit(string model, string author, string licence, string url)
        {
            Model = model;
            Author = author;
            Licence = licence;
            Url = url;
        }

        /// <summary>The line shown on the title screen.</summary>
        public string Line => Model + " by " + Author + " (" + Licence + ")";
    }

    /// <summary>
    /// The credits the game must show for third-party models under CC BY 3.0, which asks for the title,
    /// the author and the licence. The CC0 models need none; they are thanked in one line. The full list,
    /// with links and the changes made, is in <c>Assets/_ThirdParty/PolyPizza/README.md</c> and
    /// <c>Assets/_ThirdParty/Kenney/README.md</c>.
    /// </summary>
    public static class ThirdPartyCredits
    {
        /// <summary>The models that need a credit, in the order they are shown.</summary>
        public static readonly IReadOnlyList<ModelCredit> Attribution = new[]
        {
            new ModelCredit("Toy robot", "Tiff Eidmann", "CC BY 3.0", "https://poly.pizza/m/5tzTpu3fNHs"),
            new ModelCredit("Concrete Barrier", "J-Toastie", "CC BY 3.0", "https://poly.pizza/m/5Cg2oW7EFn"),
            new ModelCredit("Vending Machine", "dook", "CC BY 3.0", "https://poly.pizza/m/yNG4vqWVq0"),
            new ModelCredit("Low poly vending machine", "Anastasiia Ku", "CC BY 3.0", "https://poly.pizza/m/aZcNr5-opYI")
        };

        /// <summary>A thank-you for the CC0 models, which need no credit.</summary>
        public const string CourtesyLine = "Also with thanks to Kenney, Quaternius and iPoly3D (CC0).";

        /// <summary>The heading above the credits.</summary>
        public const string Heading = "THIRD-PARTY MODELS";

        /// <summary>The lines shown under the heading: one per credit, then the thank-you.</summary>
        public static IEnumerable<string> Lines()
        {
            foreach (ModelCredit credit in Attribution)
                yield return credit.Line;
            yield return CourtesyLine;
        }
    }
}
