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

        /// <summary>The line shown on the credits screen.</summary>
        public string Line => Model + " by " + Author + " (" + Licence + ")";
    }

    /// <summary>
    /// The credits the game must show for third-party models under CC BY 3.0, which asks for the title,
    /// the author and the licence, and for Eric Matyas's music, whose terms ask for his name and site in
    /// the game's credits. They are shown on the credits screen (<see cref="CreditsContent"/>). The CC0 models and
    /// music need none; they are thanked in one line each. The full
    /// lists, with links and the changes made, are in <c>Assets/_ThirdParty/PolyPizza/README.md</c>,
    /// <c>Assets/_ThirdParty/Kenney/README.md</c> and <c>Assets/_ThirdParty/Music/README.md</c>.
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

        /// <summary>
        /// The credit Eric Matyas asks for in the game's credits ("Music by Eric Matyas,
        /// www.soundimage.org"), naming his tracks because other composers' music plays too.
        /// The tracks are listed in <c>Assets/_ThirdParty/Music/README.md</c>.
        /// </summary>
        public const string MusicLine = "Music by Eric Matyas, www.soundimage.org:";

        /// <summary>His tracks in the game, on the line under <see cref="MusicLine"/>.</summary>
        public const string MusicTracksLine = "Creepy Action, They're Closing In, Night Stalker";

        /// <summary>A thank-you for the CC0 music, which needs no credit.</summary>
        public const string MusicCourtesyLine = "Also music by epb9000, MintoDog, cynicmusic and Kistol (CC0).";

        /// <summary>The heading above the credits.</summary>
        public const string Heading = "THIRD-PARTY MODELS AND MUSIC";

        /// <summary>
        /// The lines shown under the heading: one per model credit and the models' thank-you,
        /// then the music credit, its tracks and the music's thank-you.
        /// </summary>
        public static IEnumerable<string> Lines()
        {
            foreach (ModelCredit credit in Attribution)
                yield return credit.Line;
            yield return CourtesyLine;
            yield return MusicLine;
            yield return MusicTracksLine;
            yield return MusicCourtesyLine;
        }
    }
}
