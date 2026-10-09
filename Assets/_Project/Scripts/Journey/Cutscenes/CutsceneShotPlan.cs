using System.Collections.Generic;
using UnityEngine;
using ToyFactory.Interfaces;

namespace ToyFactory.Journey.Cutscenes
{
    /// <summary>The room a shot is filmed in (the level's four 20 x 20 m rooms).</summary>
    public enum ShotRoom
    {
        Assembly,
        Painting,
        Storage,
        Control,
    }

    /// <summary>How a shot's camera is placed.</summary>
    public enum ShotFraming
    {
        /// <summary>A fixed camera: positions and look points are world coordinates.</summary>
        World,

        /// <summary>
        /// A camera that follows the Unit 047 stand-in: positions are offsets in the stand-in's
        /// frame (x right, y up, z forward), so the shot works wherever the player stands.
        /// </summary>
        FollowActor,
    }

    /// <summary>
    /// One shot of a cutscene: a slow camera move from one pose to another while the shot's
    /// lines are said. The editor's Timeline builder turns each shot into two Cinemachine
    /// cameras and a blend between them; its length comes from the shot's dialogue.
    /// </summary>
    public sealed class CutsceneShot
    {
        public string CutsceneId { get; }
        public int Index { get; }
        public string Subject { get; }
        public ShotRoom Room { get; }
        public ShotFraming Framing { get; }

        /// <summary>Camera at the start and end of the shot (world, or the stand-in's frame).</summary>
        public Vector3 From { get; }
        public Vector3 To { get; }

        /// <summary>
        /// What the camera looks at, start and end: a world point, or with
        /// <see cref="LooksAtActor"/> an offset from the stand-in's feet.
        /// </summary>
        public Vector3 LookFrom { get; }
        public Vector3 LookTo { get; }

        /// <summary>True if the camera aims at the stand-in itself (follow shots only).</summary>
        public bool LooksAtActor { get; }

        /// <summary>Vertical field of view in degrees.</summary>
        public float FieldOfView { get; }

        /// <summary>Critical signals fired as the shot starts.</summary>
        public IReadOnlyList<string> Signals { get; }

        public CutsceneShot(string cutsceneId, int index, string subject, ShotRoom room, ShotFraming framing,
            Vector3 from, Vector3 to, Vector3 lookFrom, Vector3 lookTo, float fieldOfView,
            bool looksAtActor = false, params string[] signals)
        {
            CutsceneId = cutsceneId;
            Index = index;
            Subject = subject;
            Room = room;
            Framing = framing;
            From = from;
            To = to;
            LookFrom = lookFrom;
            LookTo = lookTo;
            FieldOfView = fieldOfView;
            LooksAtActor = looksAtActor;
            Signals = signals ?? new string[0];
        }

        /// <summary>Name of the Timeline's exposed reference to this shot's start camera.</summary>
        public string FromCameraName => $"{CutsceneId}.{Index}.from";

        /// <summary>Name of the Timeline's exposed reference to this shot's end camera.</summary>
        public string ToCameraName => $"{CutsceneId}.{Index}.to";
    }

    /// <summary>
    /// The camera shots of the five cutscenes, one per shot of their dialogue scripts. The
    /// subjects follow the prototype's shot list; the positions are placed again for this
    /// level, whose rooms and layout differ from the prototype's.
    /// </summary>
    /// <remarks>
    /// Authoring data, read by the editor's Timeline builder. It lives here, not in the editor
    /// assembly, so the EditMode tests can check every camera against the level: under the
    /// 6 m ceiling with a metre to spare, inside its room, and out of the presses, pillars,
    /// shelves and cover blocks. Coordinates are world metres from <c>Docs/LevelLayout.md</c>.
    /// Agents are where they spawned when a cutscene plays (the intro) or frozen where they
    /// are, so fixed shots of an agent use its spawn point.
    /// </remarks>
    public static class CutsceneShotPlan
    {
        /// <summary>Ceiling height agreed with S1 (walls raised to 6 m).</summary>
        public const float CeilingHeight = 6f;

        /// <summary>Space every camera keeps under the ceiling.</summary>
        public const float CeilingClearance = 1f;

        /// <summary>Gap between the end of one shot's lines and the start of the next shot.</summary>
        public const float ShotPadding = 0.25f;

        static readonly CutsceneShot[] Shots =
        {
            // ---- Intro: the factory wakes up, Factory OS calls each kind of toy online,
            // Unit 047 fails its quality check, Pip calls in. Agents are at their spawns.
            new CutsceneShot("intro", 0, "Wide high shot across the Assembly Floor", ShotRoom.Assembly, ShotFraming.World,
                new Vector3(18.5f, 4.6f, 2f), new Vector3(17f, 4.3f, 3.5f), new Vector3(5f, 0.5f, 16f), new Vector3(5f, 0.5f, 14.5f), 60f),
            new CutsceneShot("intro", 1, "Close-up on the Tracker at its spawn", ShotRoom.Assembly, ShotFraming.World,
                new Vector3(7.6f, 1.2f, 14.6f), new Vector3(6.8f, 1.05f, 14.4f), new Vector3(4f, 0.7f, 14f), new Vector3(4f, 0.7f, 14f), 50f),
            new CutsceneShot("intro", 2, "Low shot of the Guard from in front, at its spawn", ShotRoom.Painting, ShotFraming.World,
                new Vector3(31.8f, 1.7f, 11.9f), new Vector3(31.4f, 1.6f, 12.6f), new Vector3(31f, 1.5f, 15f), new Vector3(31f, 1.6f, 15f), 50f),
            new CutsceneShot("intro", 3, "Saboteur B at its spawn by the pressure plate", ShotRoom.Assembly, ShotFraming.World,
                new Vector3(13.8f, 2.6f, 16.2f), new Vector3(14.4f, 2.3f, 16.6f), new Vector3(17f, 1f, 17f), new Vector3(17f, 1f, 17f), 55f),
            new CutsceneShot("intro", 4, "Push-in on Unit 047's face", ShotRoom.Assembly, ShotFraming.FollowActor,
                new Vector3(0f, 2f, 4.6f), new Vector3(0f, 1.9f, 3.4f), new Vector3(0f, 1.85f, 0f), new Vector3(0f, 1.85f, 0f), 45f,
                looksAtActor: true),
            new CutsceneShot("intro", 5, "Beside Unit 047, down the Assembly Floor to the belts", ShotRoom.Assembly, ShotFraming.FollowActor,
                new Vector3(-2.5f, 2.6f, -1.5f), new Vector3(-2f, 2.4f, -1f), new Vector3(10f, 1f, 16f), new Vector3(10f, 1f, 16f), 60f),

            // ---- Chapter 2: switch one is back; the Painting Room, its targets and terminal.
            new CutsceneShot("ch2", 0, "The restored Assembly switch", ShotRoom.Assembly, ShotFraming.World,
                new Vector3(5f, 2.2f, 7f), new Vector3(4.6f, 2.4f, 6.5f), new Vector3(1.5f, 1.2f, 4f), new Vector3(1.5f, 1.1f, 4f), 55f),
            new CutsceneShot("ch2", 1, "High pan across the Painting Room to the Guard's ground", ShotRoom.Painting, ShotFraming.World,
                new Vector3(24.5f, 4.6f, 3f), new Vector3(26.5f, 4.3f, 5f), new Vector3(31f, 1.4f, 15f), new Vector3(31f, 1.4f, 15f), 60f),
            new CutsceneShot("ch2", 2, "Pan from a spinning target down to the colour terminal", ShotRoom.Painting, ShotFraming.World,
                new Vector3(35.5f, 2.6f, 5f), new Vector3(33.8f, 2.4f, 7.2f), new Vector3(37.4f, 3.55f, 17.5f), new Vector3(31f, 1.4f, 10.5f), 55f),

            // ---- Chapter 3: the Captain wakes, the Control Room unlocks, the Storage vault.
            new CutsceneShot("ch3", 0, "Looking up at the Captain through the server row as it wakes", ShotRoom.Control, ShotFraming.World,
                new Vector3(10.5f, 1.2f, 32.3f), new Vector3(10.5f, 1.3f, 32.8f), new Vector3(10.5f, 1.6f, 36f), new Vector3(10.5f, 1.8f, 36f), 58f,
                signals: CutsceneSignals.CaptainWake),
            new CutsceneShot("ch3", 1, "The Storage-Control door unlocking, from the Storage side", ShotRoom.Storage, ShotFraming.World,
                new Vector3(23f, 3.2f, 26.5f), new Vector3(22.6f, 2.7f, 27.8f), new Vector3(20.75f, 2f, 31f), new Vector3(20.75f, 2f, 31f), 55f,
                signals: CutsceneSignals.ControlRoomUnlock),
            new CutsceneShot("ch3", 2, "High pan over the Storage shelves to the relay board", ShotRoom.Storage, ShotFraming.World,
                new Vector3(24f, 4.6f, 23.5f), new Vector3(27.5f, 4.8f, 28f), new Vector3(36f, 3.3f, 40.5f), new Vector3(36f, 3.3f, 40.5f), 60f),

            // ---- Chapter 4: the cores.
            new CutsceneShot("ch4", 0, "Wide over the three power cores", ShotRoom.Control, ShotFraming.World,
                new Vector3(2.2f, 4.4f, 22.3f), new Vector3(5.5f, 4.6f, 23.2f), new Vector3(10.5f, 3f, 33.5f), new Vector3(10.5f, 3f, 33.5f), 60f,
                signals: CutsceneSignals.CoreShieldsDown),

            // ---- Ending: behind Unit 047 at the console, then a crane back as the factory sleeps.
            // The console stands in the middle of the Control Room, 10 m from every wall, so the
            // crane ends at most 6 m behind 047 to stay inside the room.
            new CutsceneShot("ending", 0, "Behind Unit 047, looking at the console", ShotRoom.Control, ShotFraming.FollowActor,
                new Vector3(0.6f, 2.2f, -3f), new Vector3(0.7f, 2.5f, -3.6f), new Vector3(10.5f, 1.2f, 31f), new Vector3(10.5f, 1.2f, 31f), 55f,
                signals: CutsceneSignals.FactoryShutdown),
            new CutsceneShot("ending", 1, "Crane back and up from Unit 047", ShotRoom.Control, ShotFraming.FollowActor,
                new Vector3(0.7f, 2.5f, -3.6f), new Vector3(1f, 4.4f, -6f), new Vector3(0f, 1.2f, 0f), new Vector3(0f, 1f, 0f), 60f,
                looksAtActor: true),
        };

        /// <summary>Furthest a follow camera may be from Unit 047 on the ground, so a shot taken at the console stays inside the Control Room.</summary>
        public const float MaxFollowDistance = 7f;

        /// <summary>Every shot of every cutscene.</summary>
        public static IReadOnlyList<CutsceneShot> All => Shots;

        /// <summary>The shots of one cutscene, in order.</summary>
        public static List<CutsceneShot> For(string cutsceneId)
        {
            var shots = new List<CutsceneShot>();
            foreach (CutsceneShot shot in Shots)
                if (shot.CutsceneId == cutsceneId)
                    shots.Add(shot);
            shots.Sort((a, b) => a.Index.CompareTo(b.Index));
            return shots;
        }

        /// <summary>
        /// The room's floor area inside its walls, as x/z bounds (from <c>Docs/LevelLayout.md</c>:
        /// four 20 x 20 m rooms in a 2 x 2 ring, 0.5 m walls).
        /// </summary>
        public static Rect Interior(ShotRoom room)
        {
            switch (room)
            {
                case ShotRoom.Assembly: return Rect.MinMaxRect(0.5f, 0.5f, 20.5f, 20.5f);
                case ShotRoom.Painting: return Rect.MinMaxRect(21f, 0.5f, 41f, 20.5f);
                case ShotRoom.Storage:  return Rect.MinMaxRect(21f, 21f, 41f, 41f);
                default:                return Rect.MinMaxRect(0.5f, 21f, 20.5f, 41f);
            }
        }
    }
}
