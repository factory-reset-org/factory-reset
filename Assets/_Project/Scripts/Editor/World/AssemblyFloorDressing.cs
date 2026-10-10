using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using ToyFactory.Runtime.World;
using static ToyFactory.Editor.World.DressingKit;
using static ToyFactory.Editor.World.RoomPieces;

namespace ToyFactory.Editor.World
{
    /// <summary>
    /// Builds the Assembly Floor's final dressing in <c>Env.unity</c> (room 1, x and z 0.5 to 20.5):
    /// the Toy-O-Matic assembler in place of the two stamping presses, taller doorways with
    /// framed openings, 1 m floor tiles, windows, pipes, signs and posters, a workbench, lockers,
    /// a parts shelf, a pallet of toy boxes and an overhead rail of hanging toys (between the
    /// ceiling light panels, which light the room: there are no hanging lamps).
    /// Re-running replaces the previous build, so the layout can be tuned and rebuilt.
    /// </summary>
    /// <remarks>
    /// Everything is placed clear of S2's gameplay objects (belts, plate, lever, dispenser, switch
    /// cage, charger, fuses, battery, the three crates and their routes), the player start, the
    /// agents' spawns, the Tracker's patrol corners and the cutscene cameras
    /// (<c>CutsceneShotPlan</c>). Solid props get one invisible box collider each, so the NavMesh
    /// and grid see simple shapes; wall dressing has no collider. After a build: rebake the
    /// NavMesh, lighting, reflection probes and occlusion, as for any level change.
    /// </remarks>
    public static class AssemblyFloorDressing
    {
        const string AtlasPath = TextureFolder + "/T_Env_Signs_Assembly.png";

        // Materials, loaded or made once per build.
        static Material _white, _grey, _blue, _red, _yellow, _purple, _chrome, _hazard, _glow, _amber;
        static Material _mint, _orange, _bubble, _plum, _signs, _screens, _window;
        static Mesh _sphere;

        // Sign atlas rectangles (pixels, bottom-left origin, 1024 x 1024).
        static readonly RectInt MainSign = new RectInt(0, 832, 1024, 192);
        static readonly RectInt PaintingPlaque = new RectInt(0, 704, 512, 128);
        static readonly RectInt ControlPlaque = new RectInt(512, 704, 512, 128);
        static readonly RectInt PosterWind = new RectInt(0, 384, 256, 320);
        static readonly RectInt PosterPaws = new RectInt(256, 384, 256, 320);
        static readonly RectInt PosterSmile = new RectInt(512, 384, 256, 320);
        static readonly RectInt ClockFace = new RectInt(768, 448, 256, 256);
        static readonly RectInt MachineLabel = new RectInt(0, 256, 512, 128);
        static readonly RectInt StatusScreen = new RectInt(512, 192, 512, 192);
        static readonly RectInt Pegboard = new RectInt(0, 0, 384, 192);
        static RectInt LockerNumber(int i) => new RectInt(384 + i * 96, 0, 96, 96);
        static readonly RectInt MachineScreen = new RectInt(768, 0, 256, 144);

        static Rect _uvScreen, _uvMain, _uvPainting, _uvControl, _uvWind, _uvPaws, _uvSmile, _uvClock, _uvLabel, _uvStatus, _uvPeg;
        static readonly Rect[] _uvLocker = new Rect[4];

        [MenuItem("Tools/Factory Reset/Build Assembly Floor Dressing")]
        static void RunFromMenu() => Debug.Log(Build());

        /// <summary>Builds (or rebuilds) the dressing in the open Env scene and returns a report.</summary>
        public static string Build()
        {
            GameObject level = GameObject.Find("Level");
            if (level == null)
                throw new System.InvalidOperationException("Open Env.unity first: no Level object.");
            Transform dressing = level.transform.Find("Dressing");

            // What this replaces: the two stamping presses, their lamps and floor outlines, and an earlier build.
            int removed = 0;
            foreach (string path in new[]
                     {
                         "Level/Obstacles/Assembly_Press_1", "Level/Obstacles/Assembly_Press_2",
                         "Level/Dressing/Assembly_PressLight_1", "Level/Dressing/Assembly_PressLight_2",
                         "Level/Dressing/PressOutlines", "Level/Dressing/Assembly"
                     })
            {
                GameObject old = GameObject.Find(path);
                if (old == null)
                    continue;
                Object.DestroyImmediate(old);
                removed++;
            }

            LoadMaterials();
            PaintAtlas();
            Transform root = Group(dressing, "Assembly");

            Floor(root);
            Doors(level.transform, root);
            Walls(root);
            Windows(root);
            Signs(root);
            Assembler(root);
            Workbench(root);
            Lockers(root);
            Pallet(root);
            Shelf(root);
            Ceiling(root);

            int probesMoved = MoveProbesOutOfSolids(root);

            // One renderer per material per section instead of one per part.
            int mergedAway = 0;
            foreach (Transform section in root)
                mergedAway += MergeStatic(section, "Assembly_" + section.name);

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(level.scene);

            var report = new StringBuilder();
            report.AppendLine($"Assembly Floor dressing built: removed {removed} old objects, moved {probesMoved} light probes out of solid props, merged {mergedAway} static parts.");
            foreach (Transform section in root)
                report.AppendLine($"  {section.name}: {section.GetComponentsInChildren<MeshRenderer>(true).Length} renderers, " +
                                  $"{Triangles(section)} triangles, {section.GetComponentsInChildren<BoxCollider>(true).Length} colliders");
            report.AppendLine($"  Total: {root.GetComponentsInChildren<MeshRenderer>(true).Length} renderers, {Triangles(root)} triangles.");
            return report.ToString();
        }

        // ---- Materials and textures --------------------------------------------------------

        static void LoadMaterials()
        {
            Load();
            _white = M.White; _grey = M.Grey; _blue = M.Blue; _red = M.Red; _yellow = M.Yellow;
            _purple = M.Purple; _chrome = M.Chrome; _hazard = M.Hazard; _glow = M.Glow; _amber = M.Amber;
            _mint = M.Mint; _orange = M.Orange; _bubble = M.Bubble; _plum = M.Plum; _window = M.Window;
            _sphere = M.Ball;
        }

        // The signs, posters and decals, painted into one atlas (one material, one draw state).
        static void PaintAtlas()
        {
            var p = new SignPainter(1024, Plum);

            // 01 ASSEMBLY FLOOR: plum panel, mint badge.
            p.RoundRect(MainSign, 40f, Plum);
            p.RoundFrame(new RectInt(8, 840, 1008, 176), 34f, 8f, Mint);
            p.Circle(new Vector2(104f, 928f), 70f, Mint);
            p.Text("01", new RectInt(54, 888, 100, 80), Plum);
            p.Text("ASSEMBLY FLOOR", new RectInt(200, 878, 780, 100), Ink);

            // Door plaques.
            p.RoundRect(PaintingPlaque, 30f, Bubble);
            p.Polygon(new[] { new Vector2(30f, 768f), new Vector2(90f, 728f), new Vector2(90f, 808f) }, Ink);
            p.Text("PAINTING ROOM", new RectInt(108, 728, 380, 80), Ink);
            p.RoundRect(ControlPlaque, 30f, Grape);
            p.Text("CONTROL ROOM", new RectInt(548, 764, 440, 50), Ink);
            p.RoundRect(new RectInt(628, 718, 280, 40), 18f, Tomato);
            p.Text("SEALED", new RectInt(640, 722, 256, 32), Ink);

            // Poster: WIND IT UP! with a big wind-up key.
            p.RoundRect(PosterWind, 22f, Sun);
            p.RoundFrame(new RectInt(10, 394, 236, 300), 16f, 6f, Plum);
            p.Circle(new Vector2(92f, 560f), 46f, Plum);
            p.Circle(new Vector2(164f, 560f), 46f, Plum);
            p.Circle(new Vector2(92f, 560f), 20f, Sun);
            p.Circle(new Vector2(164f, 560f), 20f, Sun);
            p.Line(new Vector2(128f, 540f), new Vector2(128f, 470f), 22f, Plum);
            p.Text("WIND IT\nUP!", new RectInt(24, 610, 208, 76), Plum);
            p.Text("A WOUND TOY IS A HAPPY TOY", new RectInt(20, 414, 216, 34), Plum, false);

            // Poster: KEEP PAWS CLEAR, hazard stripes and a paw.
            p.RoundRect(PosterPaws, 22f, Ink);
            p.Stripes(new RectInt(268, 396, 232, 44), 22, Sun, Plum);
            p.Stripes(new RectInt(268, 648, 232, 44), 22, Sun, Plum);
            p.Circle(new Vector2(384f, 528f), 40f, Tomato);
            p.Circle(new Vector2(338f, 588f), 17f, Tomato);
            p.Circle(new Vector2(370f, 604f), 17f, Tomato);
            p.Circle(new Vector2(404f, 604f), 17f, Tomato);
            p.Circle(new Vector2(436f, 588f), 17f, Tomato);
            p.Text("KEEP PAWS\nCLEAR", new RectInt(276, 446, 216, 52), Plum);

            // Poster: SMILE! QUALITY TOYS, a star.
            p.RoundRect(PosterSmile, 22f, Mint);
            p.Polygon(Star(new Vector2(640f, 560f), 78f, 34f), Sun);
            p.Circle(new Vector2(622f, 572f), 8f, Plum);
            p.Circle(new Vector2(658f, 572f), 8f, Plum);
            p.Line(new Vector2(622f, 540f), new Vector2(658f, 540f), 6f, Plum);
            p.Text("SMILE!", new RectInt(536, 640, 208, 52), Plum);
            p.Text("QUALITY TOYS\nSINCE 1987", new RectInt(530, 404, 220, 64), Plum, false);

            // Clock face (the hands are separate parts that turn).
            p.Circle(new Vector2(896f, 576f), 124f, Plum);
            p.Circle(new Vector2(896f, 576f), 110f, Ink);
            for (int h = 0; h < 12; h++)
            {
                float a = h * Mathf.PI / 6f;
                var dir = new Vector2(Mathf.Sin(a), Mathf.Cos(a));
                p.Line(new Vector2(896f, 576f) + dir * (h % 3 == 0 ? 76f : 88f), new Vector2(896f, 576f) + dir * 100f,
                    h % 3 == 0 ? 10f : 6f, h % 3 == 0 ? Tomato : Plum);
            }

            // Machine label.
            p.RoundRect(MachineLabel, 26f, Plum);
            p.Text("TOY-O-MATIC", new RectInt(30, 280, 452, 80), Sun);

            // Line status screen: two lines, bars and lamps.
            p.RoundRect(StatusScreen, 24f, Plum);
            p.Text("LINE STATUS", new RectInt(540, 334, 300, 36), Mint);
            for (int line = 0; line < 2; line++)
            {
                int y = 280 - line * 66;
                p.Text(line == 0 ? "A" : "B", new RectInt(536, y, 40, 40), Ink);
                p.RoundRect(new RectInt(588, y + 6, 330, 28), 12f, Grey);
                p.RoundRect(new RectInt(588, y + 6, line == 0 ? 260 : 170, 28), 12f, line == 0 ? Mint : Sun);
                p.Circle(new Vector2(966f, y + 20f), 16f, line == 0 ? Mint : Sun);
            }

            // Pegboard with tool shapes.
            p.RoundRect(Pegboard, 16f, Orange);
            for (int y = 16; y < 190; y += 24)
                for (int x = 16; x < 380; x += 24)
                    p.Circle(new Vector2(x, y), 3f, Hex("C9781A"));
            p.Line(new Vector2(60f, 40f), new Vector2(60f, 140f), 14f, Plum);          // screwdriver
            p.RoundRect(new RectInt(48, 128, 24, 40), 8f, Tomato);
            p.Line(new Vector2(130f, 40f), new Vector2(130f, 130f), 12f, Plum);         // hammer
            p.RoundRect(new RectInt(100, 126, 64, 30), 8f, Plum);
            p.Line(new Vector2(210f, 44f), new Vector2(210f, 132f), 12f, Plum);         // wrench
            p.Ring(new Vector2(210f, 146f), 10f, 24f, Plum);
            p.Line(new Vector2(280f, 50f), new Vector2(320f, 140f), 10f, Plum);         // pliers
            p.Line(new Vector2(320f, 50f), new Vector2(280f, 140f), 10f, Plum);
            p.Circle(new Vector2(300f, 95f), 9f, Sun);

            // Locker numbers.
            for (int i = 0; i < 4; i++)
            {
                RectInt r = LockerNumber(i);
                p.Circle(r.center, 44f, Ink);
                p.Text((i + 1).ToString(), new RectInt(r.x + 22, r.y + 18, 52, 60), Plum);
            }

            // Machine screen: a teddy blueprint and a progress bar.
            p.RoundRect(MachineScreen, 14f, Hex("10241F"));
            p.Ring(new Vector2(860f, 84f), 30f, 36f, Mint);
            p.Ring(new Vector2(832f, 114f), 9f, 14f, Mint);
            p.Ring(new Vector2(888f, 114f), 9f, 14f, Mint);
            p.Circle(new Vector2(860f, 76f), 9f, Mint);
            p.Line(new Vector2(912f, 112f), new Vector2(1000f, 112f), 6f, Mint);
            p.Line(new Vector2(912f, 92f), new Vector2(980f, 92f), 6f, Mint);
            p.Line(new Vector2(912f, 72f), new Vector2(995f, 72f), 6f, Mint);
            p.RoundRect(new RectInt(784, 16, 224, 22), 10f, Hex("2D4A44"));
            p.RoundRect(new RectInt(784, 16, 150, 22), 10f, Sun);

            Texture2D atlas = p.Save(AtlasPath);
            _uvScreen = p.Uv(MachineScreen);
            _uvMain = p.Uv(MainSign); _uvPainting = p.Uv(PaintingPlaque); _uvControl = p.Uv(ControlPlaque);
            _uvWind = p.Uv(PosterWind); _uvPaws = p.Uv(PosterPaws); _uvSmile = p.Uv(PosterSmile);
            _uvClock = p.Uv(ClockFace); _uvLabel = p.Uv(MachineLabel); _uvStatus = p.Uv(StatusScreen);
            _uvPeg = p.Uv(Pegboard);
            for (int i = 0; i < 4; i++)
                _uvLocker[i] = p.Uv(LockerNumber(i));
            _signs = Plastic("Signs", Color.white, 0.55f, atlas, new Color(0.18f, 0.18f, 0.18f), atlas);
            _screens = Plastic("SignsGlow", Color.white, 0.55f, atlas, new Color(1.1f, 1.1f, 1.1f), atlas);
        }

        // ---- Floor -------------------------------------------------------------------------

        static void Floor(Transform root)
        {
            Transform floor = Group(root, "Floor");

            // 1 m tiles, the size of S2's crates, with the joins on whole metres. The floor's top
            // face runs its UVs from x = z = 20.75 back to 0, so the 4-tile texture repeats every
            // 4 m (tiling 20.75 / 4) and is offset a quarter tile to land the joins on whole metres.
            Material tiles = Mat("FloorTiled_Assembly");
            foreach (string property in new[] { "_BaseMap", "_MainTex" })
            {
                tiles.SetTextureScale(property, new Vector2(20.75f / 4f, 20.75f / 4f));
                tiles.SetTextureOffset(property, new Vector2(0.0625f, 0.0625f));
            }
            EditorUtility.SetDirty(tiles);

            // Hazard edging along both belts (painted lines, no collider).
            Stripe(floor, "BeltEdge_Bottom_S", 10.3f, 6.62f, 17.5f, true);
            Stripe(floor, "BeltEdge_Bottom_N", 10.3f, 8.38f, 17.5f, true);
            Stripe(floor, "BeltEdge_Top_S", 9.2f, 16.62f, 11.4f, true);
            Stripe(floor, "BeltEdge_Top_N", 9.2f, 18.38f, 11.4f, true);

            // Unit 047's start pad: a dark disc with a glowing mint ring.
            Part(floor, "StartPad", Ring(0.01f, 0.62f, 0.012f), _plum, new Vector3(10.5f, 0f, 3f), Quaternion.identity);
            Part(floor, "StartPad_Glow", Ring(0.66f, 0.84f, 0.016f), _glow, new Vector3(10.5f, 0f, 3f), Quaternion.identity);

            // A big painted gear medallion in the open middle of the room (paint only: the
            // Tracker's patrol lane and the crates' routes cross it).
            var medallion = new Vector3(13.5f, 0f, 12.2f);
            Part(floor, "Medallion_Ring", Ring(1.45f, 1.62f, 0.012f), _mint, medallion, Quaternion.identity);
            Part(floor, "Medallion_Disc", Ring(0.01f, 1.4f, 0.008f), _white, medallion, Quaternion.identity);
            Part(floor, "Medallion_Gear", SavedGear("Gear_Floor_16", 1.15f, 0.92f, 0.32f, 16, 0.014f), _plum, medallion + Vector3.up * 0.009f,
                Quaternion.Euler(-90f, 0f, 0f));
            Part(floor, "Medallion_Hub", Ring(0.01f, 0.2f, 0.022f), _yellow, medallion, Quaternion.identity);

            // Painted chevrons from the start pad towards the belts and the tasks.
            foreach (float cz in new[] { 4.4f, 5.0f, 5.6f })
                foreach (float side in new[] { -1f, 1f })
                    Box(floor, "Chevron", new Vector3(0.5f, 0.012f, 0.1f), _yellow, new Vector3(10.5f + side * 0.17f, 0.006f, cz),
                        rotation: Quaternion.Euler(0f, side * 35f, 0f));

            // A painted sun-yellow ring round the control switch, so the goal reads from across the room.
            Part(floor, "SwitchRing", Ring(1.0f, 1.14f, 0.012f), _yellow, new Vector3(1.5f, 0f, 4f), Quaternion.identity);
        }

        // ---- Doors -------------------------------------------------------------------------

        static void Doors(Transform level, Transform root)
        {
            Transform doors = Group(root, "Doors");

            // Taller openings: the lintels shrink to the top metre (5 to 6 m) and become wall white.
            // S2's sliding panels are 4 m tall; they need to grow to 5 m to fill the openings.
            Lintel(level, "Walls/Lintel_Door1", new Vector3(0.5f, 1f, 3f), new Vector3(20.75f, 5.5f, 10.5f));
            Lintel(level, "Walls/Lintel_Door4", new Vector3(3f, 1f, 0.5f), new Vector3(10.5f, 5.5f, 20.75f));

            // Door 1 (east wall, to the Painting Room, open): a plum-grey frame with hazard feet and a mint "open" strip.
            Frame(doors, "Door1", new Vector3(20.34f, 0f, 10.5f), alongZ: true);
            Box(doors, "Door1_OpenStrip", new Vector3(0.04f, 0.12f, 2.4f), _glow, new Vector3(20.16f, 5.2f, 10.5f));

            // Door 4 (north wall, to the sealed Control Room): the same frame. Its red alarm beacon
            // moves onto the middle of the new header, its light up with it.
            Frame(doors, "Door4", new Vector3(10.5f, 0f, 20.34f), alongZ: false);
            Move("Lighting/ControlAlarms/AlarmDoor4/Beacon_A", new Vector3(10.5f, 5.2f, 20.06f));
            Move("Lighting/ControlAlarms/AlarmDoor4/Backplate_A", new Vector3(10.5f, 5.2f, 20.16f));
            Move("Lighting/ControlAlarms/AlarmDoor4/Beacon_B", new Vector3(10.5f, 5.5f, 21.12f));
            Move("Lighting/ControlAlarms/AlarmDoor4/Backplate_B", new Vector3(10.5f, 5.5f, 21.03f));
            Move("Lighting/ControlAlarms/AlarmDoor4/AlarmLight", new Vector3(10.5f, 4.6f, 20.75f));
        }

        // ---- Walls -------------------------------------------------------------------------

        static void Walls(Transform root)
        {
            Transform walls = Group(root, "Walls");

            // A mint rail round the room at 1.6 m, broken at the doors.
            Box(walls, "Rail_S", new Vector3(20f, 0.12f, 0.05f), _mint, new Vector3(10.5f, 1.61f, 0.525f));
            Box(walls, "Rail_W", new Vector3(0.05f, 0.12f, 20f), _mint, new Vector3(0.525f, 1.61f, 10.5f));
            Box(walls, "Rail_N1", new Vector3(8.1f, 0.12f, 0.05f), _mint, new Vector3(4.55f, 1.61f, 20.475f));
            Box(walls, "Rail_N2", new Vector3(7.9f, 0.12f, 0.05f), _mint, new Vector3(16.55f, 1.61f, 20.475f));
            Box(walls, "Rail_E1", new Vector3(0.05f, 0.12f, 8.1f), _mint, new Vector3(20.475f, 1.61f, 4.55f));
            Box(walls, "Rail_E2", new Vector3(0.05f, 0.12f, 7.9f), _mint, new Vector3(20.475f, 1.61f, 16.55f));

            // Rounded corner columns.
            foreach (Vector2 corner in new[] { new Vector2(0.75f, 0.75f), new Vector2(0.75f, 20.25f), new Vector2(20.25f, 0.75f), new Vector2(20.25f, 20.25f) })
                Box(walls, "CornerColumn", new Vector3(0.5f, 5.1f, 0.5f), _grey, new Vector3(corner.x, 3.15f, corner.y), solid: true);

            // Pipes: three colours along the west wall and on along the north wall, rising into the
            // ceiling at both ends, with a blue drop to a valve box near the lever.
            Transform pipes = Group(walls, "Pipes");
            (float y, Material material)[] runs = { (5.05f, _red), (5.3f, _yellow), (5.55f, _blue) };
            const float wallX = 0.75f, wallZ = 20.25f;
            foreach (var run in runs)
            {
                Part(pipes, "Pipe_W", Cylinder(0.07f, 19.25f), run.material, new Vector3(wallX, run.y, 1f), AlongZ);
                Part(pipes, "Pipe_N", Cylinder(0.07f, 7.55f), run.material, new Vector3(wallX, run.y, wallZ), AlongX);
                Part(pipes, "Pipe_Up_S", Cylinder(0.07f, 6f - run.y), run.material, new Vector3(wallX, run.y, 1f), Quaternion.identity);
                Part(pipes, "Pipe_Up_E", Cylinder(0.07f, 6f - run.y), run.material, new Vector3(8.3f, run.y, wallZ), Quaternion.identity);
                foreach (Vector3 joint in new[] { new Vector3(wallX, run.y, 1f), new Vector3(wallX, run.y, wallZ), new Vector3(8.3f, run.y, wallZ) })
                    Part(pipes, "Joint", _sphere, _chrome, joint, Quaternion.identity, scale: Vector3.one * 0.2f);
            }
            foreach (float z in new[] { 3f, 7f, 11f, 15f, 19f })
                Box(pipes, "Bracket", new Vector3(0.34f, 0.75f, 0.12f), _grey, new Vector3(0.66f, 5.3f, z));
            foreach (float x in new[] { 3f, 6.5f })
                Box(pipes, "Bracket", new Vector3(0.12f, 0.75f, 0.34f), _grey, new Vector3(x, 5.3f, 20.34f));
            Part(pipes, "Drop", Cylinder(0.07f, 3.9f), _blue, new Vector3(wallX, 1.65f, 13.8f), Quaternion.identity);
            Part(pipes, "Joint", _sphere, _chrome, new Vector3(wallX, 5.55f, 13.8f), Quaternion.identity, scale: Vector3.one * 0.2f);
            Box(pipes, "ValveBox", new Vector3(0.36f, 0.5f, 0.42f), _grey, new Vector3(0.7f, 1.42f, 13.8f));
            Part(pipes, "ValveWheel", Ring(0.1f, 0.18f, 0.04f), _red, new Vector3(0.9f, 1.42f, 13.8f), Quaternion.Euler(0f, 0f, -90f));

            // Fire extinguisher by the west wall.
            Part(walls, "Extinguisher", Cylinder(0.12f, 0.6f), _red, new Vector3(0.72f, 0.65f, 9.3f), Quaternion.identity);
            Part(walls, "Extinguisher_Top", Cylinder(0.05f, 0.12f), _chrome, new Vector3(0.72f, 1.25f, 9.3f), Quaternion.identity);
            Box(walls, "Extinguisher_Hook", new Vector3(0.1f, 0.1f, 0.3f), _grey, new Vector3(0.56f, 1.05f, 9.3f));

            // A wall fan above the charger, turning.
            Part(walls, "Fan_Housing", Ring(0.52f, 0.68f, 0.14f), _grey, new Vector3(20.5f, 4.2f, 4.6f), Quaternion.Euler(0f, 0f, 90f));
            Transform hub = Part(walls, "Fan_Hub", _sphere, _chrome, new Vector3(20.42f, 4.2f, 4.6f), Quaternion.Euler(0f, -90f, 0f),
                moving: true, scale: Vector3.one * 0.18f).transform;
            var spinner = hub.gameObject.AddComponent<DressingSpinner>();
            spinner.Configure(Vector3.forward, 140f);
            for (int i = 0; i < 4; i++)
            {
                GameObject blade = Part(hub, "Blade", RoundBox(new Vector3(0.16f, 0.5f, 0.03f)), _white, hub.position,
                    hub.rotation * Quaternion.Euler(0f, 0f, i * 90f) * Quaternion.Euler(0f, 18f, 0f), moving: true);
                blade.transform.position = hub.position + blade.transform.up * 0.3f;
                blade.transform.localScale = new Vector3(1f / 0.18f, 1f / 0.18f, 1f / 0.18f);
            }
        }

        // ---- Windows -----------------------------------------------------------------------

        static void Windows(Transform root)
        {
            Transform windows = Group(root, "Windows");
            // West wall (outside): behind the switch, behind the assembler, above the lever.
            foreach (float z in new[] { 2.7f, 9.9f, 15.9f })
                Window(windows, new Vector3(0.5f, 3.4f, z), FacingEast);
            // South wall (outside): a double window behind the start pad.
            foreach (float x in new[] { 9.35f, 11.65f })
                Window(windows, new Vector3(x, 3.4f, 0.5f), FacingSouth);
        }

        // ---- Signs, posters, clock ---------------------------------------------------------

        static void Signs(Transform root)
        {
            Transform signs = Group(root, "Signs");

            // North wall, seen from the start pad: the room name left of door 4, its plaque right of it.
            Box(signs, "MainSign_Back", new Vector3(5.8f, 1.22f, 0.06f), _plum, new Vector3(5f, 4f, 20.47f));
            Part(signs, "MainSign", Quad("Sign_Main", new Vector2(5.6f, 1.05f), _uvMain), _signs, new Vector3(5f, 4f, 20.435f), FacingNorth);
            Part(signs, "ControlPlaque", Quad("Sign_Control", new Vector2(2f, 0.5f), _uvControl), _signs, new Vector3(13.8f, 3.3f, 20.48f), FacingNorth);
            Part(signs, "Poster_Wind", Quad("Poster_Wind", new Vector2(1.2f, 1.5f), _uvWind), _signs, new Vector3(17.4f, 2.9f, 20.48f), FacingNorth);
            Part(signs, "Poster_Paws", Quad("Poster_Paws", new Vector2(1.2f, 1.5f), _uvPaws), _signs, new Vector3(19.15f, 2.9f, 20.48f), FacingNorth);

            // Clock with turning hands (a slow minute hand, a slower hour hand).
            Part(signs, "Clock_Rim", Ring(0.5f, 0.6f, 0.08f), _plum, new Vector3(15.6f, 4.3f, 20.5f), Quaternion.Euler(-90f, 0f, 0f));
            Part(signs, "Clock_Face", Quad("Clock", new Vector2(1.08f, 1.08f), _uvClock), _signs, new Vector3(15.6f, 4.3f, 20.47f), FacingNorth);
            Hand(signs, "Clock_Minute", new Vector3(15.6f, 4.3f, 20.44f), 0.4f, 0.03f, 6f, _plum);
            Hand(signs, "Clock_Hour", new Vector3(15.6f, 4.3f, 20.45f), 0.26f, 0.04f, 0.5f, _red);

            // East wall: the Painting Room plaque beside door 1, the line status screen above the shelf.
            Part(signs, "PaintingPlaque", Quad("Sign_Painting", new Vector2(2f, 0.5f), _uvPainting), _signs, new Vector3(20.48f, 3.3f, 7.3f), FacingWest);
            Box(signs, "Status_Back", new Vector3(0.06f, 0.82f, 2f), _plum, new Vector3(20.47f, 3.05f, ShelfZ));
            Part(signs, "StatusScreen", Quad("Sign_Status", new Vector2(1.8f, 0.675f), _uvStatus), _screens, new Vector3(20.435f, 3.05f, ShelfZ), FacingWest);

            // South wall: a poster by the corner, seen on the way back from the switch.
            Part(signs, "Poster_Smile", Quad("Poster_Smile", new Vector2(1.2f, 1.5f), _uvSmile), _signs, new Vector3(18.8f, 2.9f, 0.52f), FacingSouth);

            // West wall: a pegboard of tools between the battery and the valve.
            Part(signs, "Pegboard", Quad("Pegboard", new Vector2(1.8f, 0.9f), _uvPeg), _signs, new Vector3(0.56f, 2.2f, 12.2f), FacingEast);
        }

        // ---- The Toy-O-Matic assembler ------------------------------------------------------

        // Where the presses stood (x 2.45 to 6.65, z 10.4 to 12.6): a parts hopper feeds a stamper,
        // and a robot arm on the main body passes the toys on. Same footprint and height class as
        // the presses, so it is the same cover from the Tracker.
        static void Assembler(Transform root)
        {
            Transform a = Group(root, "Assembler");
            const float z = 11.5f;

            Box(a, "Base", new Vector3(4.2f, 0.3f, 2.2f), _grey, new Vector3(4.55f, 0.15f, z));
            Blocker(a, "Solid", new Vector3(4.55f, 1.3f, z), new Vector3(4.2f, 2.6f, 2.2f));
            Stripe(a, "Outline_S", 4.55f, 10.19f, 4.7f, true);
            Stripe(a, "Outline_N", 4.55f, 12.81f, 4.7f, true);
            Stripe(a, "Outline_W", 2.21f, z, 2.74f, false);
            Stripe(a, "Outline_E", 6.89f, z, 2.74f, false);

            // Hopper on four chrome legs, heaped with coloured parts.
            const float hx = 3.2f;
            foreach (Vector2 leg in new[] { new Vector2(-0.45f, -0.45f), new Vector2(0.45f, -0.45f), new Vector2(-0.45f, 0.45f), new Vector2(0.45f, 0.45f) })
                Part(a, "Hopper_Leg", Cylinder(0.05f, 2.05f), _chrome, new Vector3(hx + leg.x, 0.3f, z + leg.y), Quaternion.identity);
            Mesh funnel = Lathe("Hopper", new[]
            {
                new Vector2(0f, 0f), new Vector2(0.14f, 0f), new Vector2(0.17f, 0.08f), new Vector2(0.2f, 0.3f),
                new Vector2(0.64f, 0.8f), new Vector2(0.7f, 0.88f), new Vector2(0.72f, 1f), new Vector2(0.66f, 1.06f), new Vector2(0f, 1.06f)
            }, 28);
            Part(a, "Hopper", funnel, _blue, new Vector3(hx, 1.55f, z), Quaternion.identity);
            Material[] gumballs = { _red, _yellow, _mint, _purple, _bubble, _orange };
            var random = new System.Random(47);
            for (int i = 0; i < 11; i++)
            {
                float angle = i * 2.4f, radius = 0.12f + 0.42f * (i / 10f);
                float size = 0.22f + 0.08f * (float)random.NextDouble();
                Part(a, "Part", _sphere, gumballs[i % gumballs.Length],
                    new Vector3(hx + Mathf.Cos(angle) * radius, 2.62f + size * 0.25f + (i < 4 ? 0.08f : 0f), z + Mathf.Sin(angle) * radius),
                    Quaternion.identity, scale: Vector3.one * size);
            }
            Box(a, "Chute", new Vector3(0.7f, 0.06f, 0.3f), _chrome, new Vector3(3.7f, 1.3f, z), rotation: Quaternion.Euler(0f, 0f, -22f));

            // Stamper: a platform, two posts, a crossbar, and a head that stamps the toy below it.
            const float sx = 4.45f;
            Box(a, "Stamper_Platform", new Vector3(0.7f, 0.5f, 0.7f), _grey, new Vector3(sx, 0.55f, z));
            foreach (float side in new[] { -0.32f, 0.32f })
                Part(a, "Stamper_Post", Cylinder(0.05f, 1.6f), _chrome, new Vector3(sx + side, 0.8f, z), Quaternion.identity);
            Box(a, "Stamper_Bar", new Vector3(0.8f, 0.18f, 0.2f), _red, new Vector3(sx, 2.42f, z));
            Transform stamper = Group(a, "Stamper", new Vector3(sx, 0f, z));
            GameObject head = Box(stamper, "Stamper_Head", new Vector3(0.45f, 0.3f, 0.45f), _red, new Vector3(sx, 1.75f, z), moving: true);
            head.AddComponent<DressingPiston>().Configure(1.27f, 1.75f, 0.6f, 0.2f);
            Toy(a, "Teddy", new Vector3(sx, 0.88f, z), 0.7f, Quaternion.Euler(0f, 200f, 0f));

            // Main body with a mint band, a screen and its name.
            const float bx = 5.85f;
            Box(a, "Body", new Vector3(1.6f, 1.5f, 1.9f), _white, new Vector3(bx, 1.05f, z));
            Box(a, "Body_Band", new Vector3(1.64f, 0.18f, 1.94f), _mint, new Vector3(bx, 1.5f, z));
            Box(a, "Screen_Bezel", new Vector3(1f, 0.6f, 0.04f), _plum, new Vector3(bx, 1.0f, 10.54f));
            Part(a, "Screen", Quad("Screen_Assembler", new Vector2(0.86f, 0.48f), _uvScreen), _screens, new Vector3(bx, 1.0f, 10.515f), FacingNorth);
            Part(a, "Label", Quad("Sign_Assembler", new Vector2(1.2f, 0.3f), _uvLabel), _signs, new Vector3(bx, 0.5f, 10.535f), FacingNorth);

            // Robot arm on a turntable, slowly turning, holding a robot toy.
            Part(a, "Arm_Turret", Cylinder(0.32f, 0.2f), _blue, new Vector3(bx, 1.8f, z), Quaternion.identity);
            Transform arm = Group(a, "Arm", new Vector3(bx, 2f, z));
            arm.gameObject.AddComponent<DressingSpinner>().Configure(Vector3.up, 18f);
            Part(arm, "Shoulder", _sphere, _chrome, new Vector3(bx, 2.08f, z), Quaternion.identity, moving: true, scale: Vector3.one * 0.34f);
            Box(arm, "UpperArm", new Vector3(0.2f, 0.9f, 0.2f), _yellow, new Vector3(bx, 2.55f, z), moving: true);
            Part(arm, "Elbow", _sphere, _chrome, new Vector3(bx, 3.0f, z), Quaternion.identity, moving: true, scale: Vector3.one * 0.26f);
            Box(arm, "Forearm", new Vector3(0.16f, 0.16f, 0.8f), _yellow, new Vector3(bx, 3.0f, z + 0.42f), moving: true);
            Box(arm, "Claw_L", new Vector3(0.04f, 0.22f, 0.06f), _chrome, new Vector3(bx - 0.08f, 2.86f, z + 0.84f), moving: true);
            Box(arm, "Claw_R", new Vector3(0.04f, 0.22f, 0.06f), _chrome, new Vector3(bx + 0.08f, 2.86f, z + 0.84f), moving: true);
            Toy(arm, "Robot", new Vector3(bx, 2.6f, z + 0.84f), 0.6f, Quaternion.identity, moving: true);

            // Control podium with blinking buttons.
            Box(a, "Podium", new Vector3(0.7f, 0.9f, 0.35f), _grey, new Vector3(3.9f, 0.75f, 10.62f));
            Box(a, "Podium_Panel", new Vector3(0.72f, 0.06f, 0.42f), _plum, new Vector3(3.9f, 1.22f, 10.58f), rotation: Quaternion.Euler(-25f, 0f, 0f));
            Material[] lamps = { _glow, _amber, _red, _glow, _yellow };
            var buttons = new List<Renderer>();
            for (int i = 0; i < 5; i++)
                buttons.Add(Part(a, "Button", _sphere, lamps[i], new Vector3(3.66f + i * 0.12f, 1.27f, 10.56f), Quaternion.identity,
                    scale: Vector3.one * 0.07f).GetComponent<Renderer>());
            a.gameObject.AddComponent<DressingBlinker>().Configure(buttons.ToArray(), 0.3f, 11);
        }

        // ---- Floor props along the walls ---------------------------------------------------

        static void Workbench(Transform root)
        {
            Transform bench = Group(root, "Workbench");
            const float z = 0.95f;
            Blocker(bench, "Solid", new Vector3(5.2f, 0.5f, z), new Vector3(3.2f, 1f, 0.8f));
            Box(bench, "Top", new Vector3(3.2f, 0.08f, 0.8f), _orange, new Vector3(5.2f, 0.92f, z));
            foreach (float x in new[] { 4.0f, 6.4f })
                Box(bench, "Cabinet", new Vector3(0.72f, 0.86f, 0.7f), _blue, new Vector3(x, 0.44f, z));
            foreach (float x in new[] { 4.0f, 6.4f })
                Box(bench, "Drawer_Handle", new Vector3(0.3f, 0.04f, 0.04f), _chrome, new Vector3(x, 0.66f, 1.32f));
            // On the bench: a vise, a half-built robot toy, loose parts, a lamp.
            Box(bench, "Vise", new Vector3(0.3f, 0.2f, 0.24f), _grey, new Vector3(3.9f, 1.06f, 1.05f));
            Toy(bench, "Robot", new Vector3(5.0f, 1.02f, 1.0f), 0.7f, Quaternion.Euler(0f, 160f, 0f));
            Toy(bench, "Duck", new Vector3(5.6f, 1.0f, 1.05f), 0.6f, Quaternion.Euler(0f, 210f, 0f));
            Material[] colours = { _red, _yellow, _mint, _purple };
            for (int i = 0; i < 4; i++)
                Part(bench, "Part", _sphere, colours[i], new Vector3(6.0f + i * 0.13f, 1.01f, 0.8f + (i % 2) * 0.1f), Quaternion.identity, scale: Vector3.one * 0.1f);
            Part(bench, "Lamp_Base", Cylinder(0.1f, 0.04f), _grey, new Vector3(6.6f, 0.96f, 0.75f), Quaternion.identity);
            Box(bench, "Lamp_Arm", new Vector3(0.04f, 0.5f, 0.04f), _chrome, new Vector3(6.6f, 1.2f, 0.82f), rotation: Quaternion.Euler(18f, 0f, 0f));
            Part(bench, "Lamp_Shade", Lathe("LampSmall", new[] { new Vector2(0f, 0.12f), new Vector2(0.04f, 0.12f), new Vector2(0.13f, 0f), new Vector2(0.11f, 0f) }, 20),
                _yellow, new Vector3(6.6f, 1.36f, 0.95f), Quaternion.Euler(30f, 0f, 0f));
        }

        static void Lockers(Transform root)
        {
            Transform lockers = Group(root, "Lockers");
            Blocker(lockers, "Solid", new Vector3(14.4f, 0.95f, 0.8f), new Vector3(3.6f, 1.9f, 0.5f));
            Material[] doors = { _blue, _mint, _blue, _mint };
            for (int i = 0; i < 4; i++)
            {
                float x = 12.95f + i * 0.9f;
                Box(lockers, "Locker", new Vector3(0.86f, 1.9f, 0.5f), doors[i], new Vector3(x, 0.95f, 0.8f));
                for (int v = 0; v < 3; v++)
                    Box(lockers, "Vent", new Vector3(0.44f, 0.035f, 0.02f), _plum, new Vector3(x, 1.62f - v * 0.07f, 1.055f));
                Box(lockers, "Handle", new Vector3(0.04f, 0.18f, 0.04f), _chrome, new Vector3(x + 0.3f, 1.0f, 1.065f));
                Part(lockers, "Number", Quad($"Locker_{i + 1}", new Vector2(0.2f, 0.2f), _uvLocker[i]), _signs, new Vector3(x, 1.32f, 1.056f), FacingSouth);
            }
        }

        static void Pallet(Transform root)
        {
            Transform pallet = Group(root, "Pallet");
            Vector3 centre = new Vector3(1.8f, 0f, 1.6f);
            Blocker(pallet, "Solid", centre + Vector3.up * 0.7f, new Vector3(1.4f, 1.4f, 1.2f));
            Box(pallet, "Pallet", new Vector3(1.4f, 0.14f, 1.2f), _orange, centre + Vector3.up * 0.07f);
            Material[] boxes = { _yellow, _orange, _orange, _yellow };
            for (int i = 0; i < 4; i++)
            {
                Vector3 c = centre + new Vector3(i % 2 == 0 ? -0.33f : 0.33f, 0.4f, i < 2 ? -0.29f : 0.29f);
                Box(pallet, "ToyBox", new Vector3(0.62f, 0.52f, 0.56f), boxes[i], c);
                Box(pallet, "Tape", new Vector3(0.64f, 0.08f, 0.58f), _white, c + Vector3.up * 0.05f);
            }
            Box(pallet, "ToyBox", new Vector3(0.62f, 0.52f, 0.56f), _yellow, centre + new Vector3(-0.3f, 0.93f, -0.05f), rotation: Quaternion.Euler(0f, 14f, 0f));
            Box(pallet, "ToyBox", new Vector3(0.5f, 0.42f, 0.46f), _orange, centre + new Vector3(0.32f, 0.88f, 0.12f), rotation: Quaternion.Euler(0f, -9f, 0f));
            Toy(pallet, "Teddy", centre + new Vector3(0.3f, 1.18f, 0.1f), 0.8f, Quaternion.Euler(0f, 40f, 0f));
        }

        // North end of the east wall (z 16.6 to 18.6), clear of fuse 3 and of z 15-16, where
        // SaboteurDoorSceneTests opens a second gap through the wall beside door 1.
        static void Shelf(Transform root)
        {
            Transform shelf = Group(root, "PartsShelf");
            const float x = 20.2f, z = ShelfZ, length = 2f;
            Blocker(shelf, "Solid", new Vector3(x, 1.05f, z), new Vector3(0.55f, 2.1f, length));
            foreach (float side in new[] { -0.97f, 0.97f })
                Box(shelf, "Side", new Vector3(0.55f, 2.1f, 0.06f), _grey, new Vector3(x, 1.05f, z + side));
            foreach (float y in new[] { 0.1f, 0.75f, 1.4f, 2.05f })
                Box(shelf, "Shelf", new Vector3(0.55f, 0.05f, length), _grey, new Vector3(x, y, z));
            Material[] bins = { _red, _yellow, _mint, _blue, _purple, _bubble };
            for (int row = 0; row < 2; row++)
                for (int i = 0; i < 3; i++)
                {
                    float y = 0.93f + row * 0.65f, bz = z - 0.62f + i * 0.62f;
                    Box(shelf, "Bin", new Vector3(0.45f, 0.3f, 0.5f), bins[(i + row * 3) % bins.Length], new Vector3(x - 0.02f, y, bz));
                    Part(shelf, "Bin_Parts", _sphere, bins[(i + row * 3 + 2) % bins.Length], new Vector3(x - 0.05f, y + 0.12f, bz), Quaternion.identity, scale: new Vector3(0.3f, 0.12f, 0.34f));
                }
            for (int i = 0; i < 2; i++)
                Box(shelf, "ToyBox", new Vector3(0.45f, 0.5f, 0.85f), i % 2 == 0 ? _orange : _yellow, new Vector3(x - 0.02f, 0.38f, z - 0.45f + i * 0.9f));
        }

        const float ShelfZ = 17.6f;

        // ---- Ceiling: the toy rail --------------------------------------------------------

        static void Ceiling(Transform root)
        {
            Transform ceiling = Group(root, "Ceiling");

            // The rail: a rounded rectangle between the belts, hung from the ceiling. Every beam and
            // hanger runs between the light panels (x 2.8-5.2, 9.3-11.7, 15.8-18.2 and z 3.6-4.4,
            // 10.1-10.9, 16.6-17.4), never under one.
            const float y = 5.35f, x0 = 6f, x1 = 15.2f, z0 = 11.6f, z1 = 16f;
            Transform rail = Group(ceiling, "ToyRail");
            Box(rail, "Beam_S", new Vector3(x1 - x0 - 0.2f, 0.14f, 0.14f), _grey, new Vector3((x0 + x1) * 0.5f, y, z0));
            Box(rail, "Beam_N", new Vector3(x1 - x0 - 0.2f, 0.14f, 0.14f), _grey, new Vector3((x0 + x1) * 0.5f, y, z1));
            Box(rail, "Beam_W", new Vector3(0.14f, 0.14f, z1 - z0 - 0.2f), _grey, new Vector3(x0, y, (z0 + z1) * 0.5f));
            Box(rail, "Beam_E", new Vector3(0.14f, 0.14f, z1 - z0 - 0.2f), _grey, new Vector3(x1, y, (z0 + z1) * 0.5f));
            foreach (Vector2 corner in new[] { new Vector2(x0, z0), new Vector2(x1, z0), new Vector2(x1, z1), new Vector2(x0, z1) })
            {
                Part(rail, "Corner", _sphere, _chrome, new Vector3(corner.x, y, corner.y), Quaternion.identity, scale: Vector3.one * 0.24f);
                Part(rail, "Hanger", Cylinder(0.035f, 0.58f), _chrome, new Vector3(corner.x, y + 0.05f, corner.y), Quaternion.identity);
            }
            foreach (float hx in new[] { 9f, 12.4f })
                foreach (float hz in new[] { z0, z1 })
                    Part(rail, "Hanger", Cylinder(0.035f, 0.58f), _chrome, new Vector3(hx, y + 0.05f, hz), Quaternion.identity);

            const float carry = 5.25f;
            var loop = new[]
            {
                new Vector3(x0 + 0.4f, carry, z0), new Vector3(x1 - 0.4f, carry, z0), new Vector3(x1, carry, z0 + 0.4f), new Vector3(x1, carry, z1 - 0.4f),
                new Vector3(x1 - 0.4f, carry, z1), new Vector3(x0 + 0.4f, carry, z1), new Vector3(x0, carry, z1 - 0.4f), new Vector3(x0, carry, z0 + 0.4f)
            };
            string[] toys = { "Teddy", "Robot", "Duck" };
            var carriers = new List<Transform>();
            for (int i = 0; i < 12; i++)
            {
                Transform carrier = Group(rail, "Carrier");
                Box(carrier, "Trolley", new Vector3(0.24f, 0.12f, 0.3f), _grey, new Vector3(0f, 0.06f, 0f), moving: true).transform.localPosition = new Vector3(0f, 0.06f, 0f);
                Part(carrier, "Rod", Cylinder(0.022f, 0.6f), _chrome, Vector3.zero, Quaternion.identity, moving: true).transform.localPosition = new Vector3(0f, -0.6f, 0f);
                Toy(carrier, toys[i % toys.Length], Vector3.zero, 1.3f, Quaternion.identity, moving: true).transform.localPosition = new Vector3(0f, -1.02f, 0f);
                carriers.Add(carrier);
            }
            rail.gameObject.AddComponent<DressingRail>().Configure(loop, carriers.ToArray(), 0.45f);
        }

        // ---- Toys: small combined meshes, one renderer each --------------------------------

    }
}
