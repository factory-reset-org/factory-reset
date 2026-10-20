using System.Collections.Generic;

namespace ToyFactory.UI
{
    /// <summary>How a line of the credits is drawn.</summary>
    public enum CreditsKind
    {
        /// <summary>The game's name.</summary>
        Title,

        /// <summary>One line under the name.</summary>
        Tagline,

        /// <summary>A module's code and title.</summary>
        Module,

        /// <summary>A wrapped paragraph.</summary>
        Paragraph,

        /// <summary>A section heading.</summary>
        Section,

        /// <summary>A heading inside a section.</summary>
        Subsection,

        /// <summary>A member's name.</summary>
        Name,

        /// <summary>A member's role.</summary>
        Role,

        /// <summary>The small line under a role.</summary>
        Detail,

        /// <summary>One credit, wrapped if it is long.</summary>
        Credit,

        /// <summary>A small note.</summary>
        Note,

        /// <summary>The last line.</summary>
        Closing
    }

    /// <summary>One line of the credits: how it is drawn and what it says.</summary>
    public readonly struct CreditsEntry
    {
        /// <summary>How the line is drawn.</summary>
        public readonly CreditsKind Kind;

        /// <summary>What the line says.</summary>
        public readonly string Text;

        /// <summary>Creates a line.</summary>
        public CreditsEntry(CreditsKind kind, string text)
        {
            Kind = kind;
            Text = text;
        }
    }

    /// <summary>One member of the group, with the role in each module.</summary>
    public readonly struct TeamMember
    {
        /// <summary>The student label the project documents use (S1 to S4).</summary>
        public readonly string Student;

        /// <summary>The name shown.</summary>
        public readonly string Name;

        /// <summary>The Graphics and Visualization role.</summary>
        public readonly string GvRole;

        /// <summary>The agent this member built for Intelligent Systems.</summary>
        public readonly string IsAgent;

        /// <summary>The agent's architecture.</summary>
        public readonly string IsMethod;

        /// <summary>Creates a member.</summary>
        public TeamMember(string student, string name, string gvRole, string isAgent, string isMethod)
        {
            Student = student;
            Name = name;
            GvRole = gvRole;
            IsAgent = isAgent;
            IsMethod = isMethod;
        }
    }

    /// <summary>
    /// What the credits say, in order: the two modules the game was made for, the group members with their
    /// role in each module, and the third-party resources that need an acknowledgement. Pure data, so the
    /// order and the facts are tested without a scene.
    /// </summary>
    /// <remarks>
    /// <para>Every fact comes from the project's own records: the roles and agents from the team table in
    /// <c>README.md</c> (the Responsibilities document it follows), the models and music from
    /// <see cref="ThirdPartyCredits"/> and the READMEs in <c>Assets/_ThirdParty</c>, the textures and sounds
    /// from <c>Docs/DesignDoc.md</c>, and the tools from the README's requirements. Only S2 to S4's names appear in
    /// the repository (as commit authors); S1 is known there only by the GitHub handle shown here.
    /// <c>CreditsContentTests</c> checks the roles against the README and the credits against the READMEs.</para>
    /// </remarks>
    public static class CreditsContent
    {
        /// <summary>The game's name.</summary>
        public const string GameName = "FACTORY RESET";

        /// <summary>One line about the game, from the README.</summary>
        public const string Tagline = "A cartoon 3D shooter set inside an abandoned toy factory";

        /// <summary>The Graphics and Visualization module.</summary>
        public const string GvModule = "SE3032 - Graphics and Visualization";

        /// <summary>The Intelligent Systems module.</summary>
        public const string IsModule = "SE3062 - Intelligent Systems";

        /// <summary>Says what the game was made for.</summary>
        public const string Intro =
            "Factory Reset was developed as an assignment project for both modules, by a group of four students.";

        /// <summary>The group, in the order of the README's team table.</summary>
        public static readonly IReadOnlyList<TeamMember> Members = new[]
        {
            new TeamMember("S1", "Onel De Silva", "World Builder", "Tracker Toy", "Greedy Best-First Search + hierarchical FSM"),
            new TeamMember("S2", "Dhananjaya Kularatne", "Systems Engineer", "Guard Bot", "Tactical A* + cover evaluation"),
            new TeamMember("S3", "Gayan Rajasinghe", "Core Developer", "Saboteur Squad",
                "Utility AI with response curves and squad target claims"),
            new TeamMember("S4", "Omira Kularatne", "Agent Controller (movement, animation, cutscenes)", "Captain Bot",
                "Goal prediction + Dijkstra fields + A* intercept")
        };

        /// <summary>The photographic surface textures, from the design document.</summary>
        public const string TexturesLine = "Photographic surface textures from ambientCG (CC0).";

        /// <summary>The sounds, from the design document: they are made in code.</summary>
        public const string SoundsLine = "Sound effects are synthesised in code, so there are no third-party sound files.";

        /// <summary>The Kenney packs the props started from, from <c>Assets/_ThirdParty/Kenney/README.md</c>.</summary>
        public const string KenneyLine = "Kenney's Factory Kit, Blaster Kit and Platformer Kit (CC0), at kenney.nl.";

        /// <summary>The tools, from the README's requirements and <c>ProjectSettings/ProjectVersion.txt</c>.</summary>
        public const string ToolsLine = "Made with Unity 6000.6.2f1 and Blender 5.2.0 LTS.";

        /// <summary>The last line.</summary>
        public const string ThankYou = "Thank you for playing";

        /// <summary>The credits, from the first line to the last.</summary>
        public static IReadOnlyList<CreditsEntry> Build()
        {
            var list = new List<CreditsEntry>
            {
                new CreditsEntry(CreditsKind.Title, GameName),
                new CreditsEntry(CreditsKind.Tagline, Tagline),
                new CreditsEntry(CreditsKind.Module, GvModule),
                new CreditsEntry(CreditsKind.Module, IsModule),
                new CreditsEntry(CreditsKind.Paragraph, Intro),

                new CreditsEntry(CreditsKind.Section, GvModule),
            };

            foreach (TeamMember member in Members)
            {
                list.Add(new CreditsEntry(CreditsKind.Name, member.Name));
                list.Add(new CreditsEntry(CreditsKind.Role, member.GvRole));
            }

            list.Add(new CreditsEntry(CreditsKind.Section, IsModule));
            foreach (TeamMember member in Members)
            {
                list.Add(new CreditsEntry(CreditsKind.Name, member.Name));
                list.Add(new CreditsEntry(CreditsKind.Role, member.IsAgent + " developer"));
                list.Add(new CreditsEntry(CreditsKind.Detail, member.IsMethod));
            }

            list.Add(new CreditsEntry(CreditsKind.Section, "Third-party resources"));

            list.Add(new CreditsEntry(CreditsKind.Subsection, "Models"));
            foreach (ModelCredit credit in ThirdPartyCredits.Attribution)
                list.Add(new CreditsEntry(CreditsKind.Credit, credit.Line));
            list.Add(new CreditsEntry(CreditsKind.Credit, ThirdPartyCredits.CourtesyLine));
            list.Add(new CreditsEntry(CreditsKind.Note, KenneyLine));

            list.Add(new CreditsEntry(CreditsKind.Subsection, "Music"));
            list.Add(new CreditsEntry(CreditsKind.Credit, ThirdPartyCredits.MusicLine));
            list.Add(new CreditsEntry(CreditsKind.Credit, ThirdPartyCredits.MusicTracksLine));
            list.Add(new CreditsEntry(CreditsKind.Credit, ThirdPartyCredits.MusicCourtesyLine));

            list.Add(new CreditsEntry(CreditsKind.Subsection, "Textures"));
            list.Add(new CreditsEntry(CreditsKind.Credit, TexturesLine));

            list.Add(new CreditsEntry(CreditsKind.Subsection, "Sound"));
            list.Add(new CreditsEntry(CreditsKind.Credit, SoundsLine));

            list.Add(new CreditsEntry(CreditsKind.Subsection, "Tools"));
            list.Add(new CreditsEntry(CreditsKind.Credit, ToolsLine));

            list.Add(new CreditsEntry(CreditsKind.Closing, ThankYou));
            return list;
        }
    }
}
