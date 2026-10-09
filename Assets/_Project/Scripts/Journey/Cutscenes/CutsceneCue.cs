using System.Collections.Generic;
using UnityEngine;

namespace ToyFactory.Journey.Cutscenes
{
    /// <summary>What a cutscene cue does.</summary>
    public enum CutsceneCueKind
    {
        /// <summary>The factory alarm sounds.</summary>
        Alarm,

        /// <summary>A comic word pops up in the scene.</summary>
        Pop,
    }

    /// <summary>The comic words the cutscenes use (pre-rendered sprites in Textures/FX).</summary>
    public enum ComicWord
    {
        /// <summary>"?!" over the Tracker as it powers on.</summary>
        Alert,

        /// <summary>"ATTEN-HUT!" over the Guard at its post.</summary>
        Attention,

        /// <summary>A red "DEFECTIVE!" as Unit 047 fails its quality check.</summary>
        Defective,

        /// <summary>A mint "SHUTDOWN" as Factory OS shuts down.</summary>
        Shutdown,
    }

    /// <summary>
    /// One cue: in this cutscene's shot, as this line starts, sound the alarm or pop a word
    /// up. The editor's Timeline builder turns each cue into a <see cref="CutsceneCueMarker"/>.
    /// </summary>
    public sealed class CutsceneCue
    {
        public string CutsceneId { get; }
        public int Shot { get; }

        /// <summary>The line of the shot it lands on (0 = as the shot starts).</summary>
        public int AtLine { get; }

        public CutsceneCueKind Kind { get; }
        public ComicWord Word { get; }

        /// <summary>True: <see cref="Point"/> is an offset above the Unit 047 stand-in. False: a world point.</summary>
        public bool OnActor { get; }

        /// <summary>Where a word pops up (world, or offset from the stand-in's feet).</summary>
        public Vector3 Point { get; }

        CutsceneCue(string cutsceneId, int shot, int atLine, CutsceneCueKind kind, ComicWord word, bool onActor, Vector3 point)
        {
            CutsceneId = cutsceneId;
            Shot = shot;
            AtLine = atLine;
            Kind = kind;
            Word = word;
            OnActor = onActor;
            Point = point;
        }

        public static CutsceneCue Alarm(string cutsceneId, int shot) =>
            new CutsceneCue(cutsceneId, shot, 0, CutsceneCueKind.Alarm, default, false, Vector3.zero);

        public static CutsceneCue PopAt(string cutsceneId, int shot, int atLine, ComicWord word, Vector3 world) =>
            new CutsceneCue(cutsceneId, shot, atLine, CutsceneCueKind.Pop, word, false, world);

        public static CutsceneCue PopOnActor(string cutsceneId, int shot, int atLine, ComicWord word, Vector3 offset) =>
            new CutsceneCue(cutsceneId, shot, atLine, CutsceneCueKind.Pop, word, true, offset);
    }

    /// <summary>
    /// The cutscenes' sound and comic cues, from the shot lists in Docs/Story.md. Authoring
    /// data for the Timeline builder, like <see cref="CutsceneShotPlan"/>, so tests can check it.
    /// </summary>
    /// <remarks>
    /// Words over agents use their spawn points, where the intro films them (the agents are at
    /// their spawns in the intro). "ATTEN-HUT!" for the Guard comes from the prototype; the
    /// story's shot list only describes the Guard at its post.
    /// </remarks>
    public static class CutsceneCuePlan
    {
        static readonly CutsceneCue[] Cues =
        {
            CutsceneCue.Alarm("intro", 0),                                                          // the dark assembly line
            CutsceneCue.PopAt("intro", 1, 0, ComicWord.Alert, new Vector3(4f, 2.1f, 14f)),         // the Tracker powers on
            CutsceneCue.PopAt("intro", 2, 0, ComicWord.Attention, new Vector3(31f, 3.1f, 15f)),    // the Guard at its post
            CutsceneCue.PopOnActor("intro", 4, 1, ComicWord.Defective, new Vector3(0f, 2.8f, 0f)), // "Product status: DEFECTIVE."
            CutsceneCue.Alarm("ch2", 1),                                                            // Painting line breached
            CutsceneCue.Alarm("ch3", 0),                                                            // waking the Captain
            CutsceneCue.Alarm("ch4", 0),                                                            // core defence protocol
            CutsceneCue.PopAt("ending", 0, 0, ComicWord.Shutdown, new Vector3(10.5f, 2.8f, 31f)), // over the console
        };

        /// <summary>Every cue of every cutscene.</summary>
        public static IReadOnlyList<CutsceneCue> All => Cues;

        /// <summary>The cues of one shot, in the order they were authored.</summary>
        public static List<CutsceneCue> For(string cutsceneId, int shot)
        {
            var cues = new List<CutsceneCue>();
            foreach (CutsceneCue cue in Cues)
                if (cue.CutsceneId == cutsceneId && cue.Shot == shot)
                    cues.Add(cue);
            return cues;
        }
    }
}
