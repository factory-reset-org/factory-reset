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
        static Material _wood, _card, _lockerBlue, _lockerMint, _wallPanel, _ceilingTiles;

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
                         "Level/Dressing/PressOutlines", "Level/Dressing/Assembly",
                         // The room's six wall gears turned for no reason; their job is now the belt drive's.
                         "Level/Dressing/WallGears/Gear_1", "Level/Dressing/WallGears/Gear_2", "Level/Dressing/WallGears/Gear_3",
                         "Level/Dressing/WallGears/Gear_4", "Level/Dressing/WallGears/Gear_5", "Level/Dressing/WallGears/Gear_6"
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
            Surfaces(level.transform);
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
            BeltDrive(root);
            Ceiling(root);

            string outside = OutsideDressing.Build();
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
            report.AppendLine(outside);
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

            // Materials made of something other than plastic, with their surface textures.
            _wood = Plastic("Wood", Hex("C8894B"), 0.55f);
            PhotoTextures.Apply(_wood, PhotoTextures.Kind.Wood);
            _card = Plastic("Cardboard", Hex("C9A271"), 0.4f);
            PhotoTextures.Apply(_card, PhotoTextures.Kind.Cardboard);
            _lockerBlue = Plastic("LockerBlue", Cobalt, 0.75f);
            PhotoTextures.Apply(_lockerBlue, PhotoTextures.Kind.Metal);
            _lockerMint = Plastic("LockerMint", Mint, 0.75f);
            PhotoTextures.Apply(_lockerMint, PhotoTextures.Kind.Metal);
            _wallPanel = Plastic("WallPanel", Hex("E8ECF2"), 0.6f);
            PhotoTextures.Apply(_wallPanel, PhotoTextures.Kind.WallPanel);
            _ceilingTiles = Plastic("CeilingTiles", Hex("2B2450"), 0.5f);
            PhotoTextures.Apply(_ceilingTiles, PhotoTextures.Kind.Ceiling, new Vector2(20.75f / 3.85f, 20.75f / 3.85f));
        }

        // Panelled walls everywhere (the walls run through every room), 1 m floor tiles and an
        // acoustic tile ceiling in this room.
        static void Surfaces(Transform level)
        {
            foreach (MeshRenderer wall in level.Find("Walls").GetComponentsInChildren<MeshRenderer>())
                wall.sharedMaterial = _wallPanel;

            // 1 m tiles, the size of S2's crates, with the joins on whole metres. The floor's top
            // face runs its UVs from x = z = 20.75 back to 0; the photo holds 8 x 8 tiles, scaled
            // to 8 m, so it repeats 20.75 / 8 times and is offset 0.25 m (1/32) to land the joins.
            Material tiles = Mat("FloorTiled_Assembly");
            PhotoTextures.Apply(tiles, PhotoTextures.Kind.FloorTile, new Vector2(20.75f / 8f, 20.75f / 8f));
            tiles.SetTextureOffset("_BaseMap", new Vector2(0.03125f, 0.03125f));
            EditorUtility.SetDirty(tiles);

            // Three times the lightmap detail on this floor, so the sun's patches through the
            // windows have sharp edges and show the window bars.
            Transform floorObject = level.Find("Floors/Floor_Assembly");
            if (floorObject != null)
            {
                var lightmapped = new SerializedObject(floorObject.GetComponent<MeshRenderer>());
                lightmapped.FindProperty("m_ScaleInLightmap").floatValue = 3f;
                lightmapped.ApplyModifiedPropertiesWithoutUndo();
            }

            Transform ceiling = level.Find("Ceilings/Ceiling_Assembly");
            if (ceiling != null)
                ceiling.GetComponent<MeshRenderer>().sharedMaterial = _ceilingTiles;
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

            // Hazard edging along both belts (painted lines, no collider).
            Stripe(floor, "BeltEdge_Bottom_S", 10.3f, 6.62f, 17.5f, true);
            Stripe(floor, "BeltEdge_Bottom_N", 10.3f, 8.38f, 17.5f, true);
            Stripe(floor, "BeltEdge_Top_S", 9.2f, 16.62f, 11.4f, true);
            Stripe(floor, "BeltEdge_Top_N", 9.2f, 18.38f, 11.4f, true);

            // Unit 047's start pad: a dark disc with a glowing mint ring.
            Part(floor, "StartPad", Ring(0.01f, 0.62f, 0.012f), _plum, new Vector3(10.5f, 0f, 3f), Quaternion.identity);
            Part(floor, "StartPad_Glow", Ring(0.66f, 0.84f, 0.016f), _glow, new Vector3(10.5f, 0f, 3f), Quaternion.identity);

            // A painted walkway from the start pad to the Painting Room door: two white lines and
            // mint arrows along the south wall and up the east wall, past the charger and the
            // bottom belt's end, so the way on reads at a glance (paint only).
            Transform walk = Group(floor, "Walkway");
            const float laneZ = 3.4f, laneX = 19.85f, half = 0.6f, laneStart = 11.6f, laneEnd = 9.2f;
            Box(walk, "Line", new Vector3(laneX + half - laneStart, 0.012f, 0.08f), _white, new Vector3((laneStart + laneX + half) * 0.5f, 0.006f, laneZ - half));
            Box(walk, "Line", new Vector3(laneX - half - laneStart, 0.012f, 0.08f), _white, new Vector3((laneStart + laneX - half) * 0.5f, 0.006f, laneZ + half));
            Box(walk, "Line", new Vector3(0.08f, 0.012f, laneEnd - (laneZ - half)), _white, new Vector3(laneX + half, 0.006f, (laneZ - half + laneEnd) * 0.5f));
            Box(walk, "Line", new Vector3(0.08f, 0.012f, laneEnd - (laneZ + half)), _white, new Vector3(laneX - half, 0.006f, (laneZ + half + laneEnd) * 0.5f));
            foreach (float x in new[] { 13.6f, 15.6f, 17.6f })
                FloorArrow(walk, new Vector3(x, 0.007f, laneZ), Vector3.right);
            foreach (float z in new[] { 5.6f, 7.6f })
                FloorArrow(walk, new Vector3(laneX, 0.007f, z), Vector3.forward);
            FloorArrow(walk, new Vector3(laneX, 0.007f, 10.3f), Vector3.right);   // into door 1

            // Painted chevrons from the start pad towards the belts and the tasks.
            foreach (float cz in new[] { 4.4f, 5.0f, 5.6f })
                foreach (float side in new[] { -1f, 1f })
                    Box(floor, "Chevron", new Vector3(0.5f, 0.012f, 0.1f), _yellow, new Vector3(10.5f + side * 0.17f, 0.006f, cz),
                        rotation: Quaternion.Euler(0f, side * 35f, 0f));

            // A painted sun-yellow ring round the control switch, so the goal reads from across the room.
            Part(floor, "SwitchRing", Ring(1.0f, 1.14f, 0.012f), _yellow, new Vector3(1.5f, 0f, 4f), Quaternion.identity);
        }

        // A painted arrow (two strokes) pointing along 'direction' on the floor.
        static void FloorArrow(Transform parent, Vector3 at, Vector3 direction)
        {
            Vector3 side = Vector3.Cross(Vector3.up, direction);
            Vector3 tip = at + direction * 0.22f;
            foreach (float s in new[] { -1f, 1f })
            {
                Vector3 tail = at - direction * 0.12f + side * s * 0.28f;
                Vector3 stroke = tip - tail;
                Box(parent, "Arrow", new Vector3(0.11f, 0.012f, stroke.magnitude + 0.08f), _mint, (tip + tail) * 0.5f,
                    rotation: Quaternion.LookRotation(stroke));
            }
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

        // Real windows: openings cut through the outside walls (the walls keep their colliders),
        // glass in the frames, and the sky, the sun and the backdrop outside (OutsideDressing).
        static readonly float[] WestWindows = { 2.7f, 9.9f, 15.9f }, SouthWindows = { 9.35f, 11.65f };
        const float WindowY = 3.4f;

        static void Windows(Transform root)
        {
            Transform windows = Group(root, "Windows");
            // West wall (outside): behind the switch, behind the assembler, above the lever.
            foreach (float z in WestWindows)
                Window(windows, new Vector3(0.5f, WindowY, z), FacingEast, open: true);
            // South wall (outside): a double window behind the start pad.
            foreach (float x in SouthWindows)
                Window(windows, new Vector3(x, WindowY, 0.5f), FacingSouth, open: true);

            var west = new List<(float, float, float, float)>();
            foreach (float z in WestWindows)
                west.Add((z - 1f, z + 1f, WindowY - 0.7f, WindowY + 0.7f));
            var south = new List<(float, float, float, float)>();
            foreach (float x in SouthWindows)
                south.Add((x - 1f, x + 1f, WindowY - 0.7f, WindowY + 0.7f));
            WallCutter.Cut(GameObject.Find("Level/Walls/Wall_West"), "Assembly", west);
            WallCutter.Cut(GameObject.Find("Level/Walls/Wall_South"), "Assembly", south);
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

            // South wall: the tool pegboard above the repair bench, where the tools are used.
            Part(signs, "Pegboard", Quad("Pegboard", new Vector2(1.8f, 0.9f), _uvPeg), _signs, new Vector3(5.2f, 2.2f, 0.535f), FacingSouth);
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

            // Hopper on four chrome legs, heaped with the parts it feeds the stamper: wheels, robot
            // heads, teddy heads and arms.
            const float hx = 3.2f;
            foreach (Vector2 leg in new[] { new Vector2(-0.45f, -0.45f), new Vector2(0.45f, -0.45f), new Vector2(-0.45f, 0.45f), new Vector2(0.45f, 0.45f) })
                Part(a, "Hopper_Leg", Cylinder(0.05f, 2.05f), _chrome, new Vector3(hx + leg.x, 0.3f, z + leg.y), Quaternion.identity);
            Mesh funnel = Lathe("Hopper", new[]
            {
                new Vector2(0f, 0f), new Vector2(0.14f, 0f), new Vector2(0.17f, 0.08f), new Vector2(0.2f, 0.3f),
                new Vector2(0.64f, 0.8f), new Vector2(0.7f, 0.88f), new Vector2(0.72f, 1f), new Vector2(0.66f, 1.06f), new Vector2(0f, 1.06f)
            }, 28);
            Part(a, "Hopper", funnel, _blue, new Vector3(hx, 1.55f, z), Quaternion.identity);
            var random = new System.Random(47);
            for (int i = 0; i < 12; i++)
            {
                float angle = i * 2.4f, radius = 0.1f + 0.4f * (i / 11f);
                var turn = Quaternion.Euler((float)random.NextDouble() * 50f - 25f, (float)random.NextDouble() * 360f, (float)random.NextDouble() * 50f - 25f);
                ToyPart(a, i, new Vector3(hx + Mathf.Cos(angle) * radius, 2.66f + 0.1f * (1f - i / 11f), z + Mathf.Sin(angle) * radius), turn, 1.1f);
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

        // A repair bench: a robot toy held in a vise under a lamp, its spare head and a screwdriver
        // beside it, three tidy trays of parts, and the tool pegboard on the wall above (Signs).
        static void Workbench(Transform root)
        {
            Transform bench = Group(root, "Workbench");
            const float z = 0.95f;
            Blocker(bench, "Solid", new Vector3(5.2f, 0.5f, z), new Vector3(3.2f, 1f, 0.8f));
            Box(bench, "Top", new Vector3(3.2f, 0.08f, 0.8f), _wood, new Vector3(5.2f, 0.92f, z));
            foreach (float x in new[] { 4.0f, 6.4f })
                Box(bench, "Cabinet", new Vector3(0.72f, 0.86f, 0.7f), _blue, new Vector3(x, 0.44f, z));
            foreach (float x in new[] { 4.0f, 6.4f })
                Box(bench, "Drawer_Handle", new Vector3(0.3f, 0.04f, 0.04f), _chrome, new Vector3(x, 0.66f, 1.32f));

            // The vise, and the robot clamped in it.
            Box(bench, "Vise_Base", new Vector3(0.4f, 0.08f, 0.26f), _grey, new Vector3(5.0f, 1.0f, 1.0f));
            foreach (float side in new[] { -0.16f, 0.16f })
                Box(bench, "Vise_Jaw", new Vector3(0.06f, 0.16f, 0.24f), _grey, new Vector3(5.0f + side, 1.12f, 1.0f));
            Part(bench, "Vise_Screw", Cylinder(0.02f, 0.3f), _chrome, new Vector3(5.19f, 1.12f, 1.0f), AlongX);
            Box(bench, "Vise_Handle", new Vector3(0.03f, 0.03f, 0.24f), _red, new Vector3(5.49f, 1.12f, 1.0f));
            Toy(bench, "Robot", new Vector3(5.0f, 1.16f, 1.0f), 0.8f, Quaternion.identity);

            // Its spare head, and a screwdriver.
            ToyPart(bench, 1, new Vector3(5.75f, 1.05f, 0.9f), Quaternion.Euler(0f, -20f, 0f), 0.9f);
            Part(bench, "Screwdriver_Handle", Cylinder(0.025f, 0.11f), _red, new Vector3(4.4f, 0.985f, 1.12f), AlongX);
            Part(bench, "Screwdriver_Shaft", Cylinder(0.008f, 0.15f), _chrome, new Vector3(4.51f, 0.985f, 1.12f), AlongX);

            // Three trays of parts at the east end: wheels, teddy heads, arms.
            Material[] trays = { _red, _yellow, _blue };
            int[] kinds = { 0, 2, 3 };
            for (int i = 0; i < 3; i++)
            {
                var tray = new Vector3(6.05f + i * 0.3f, 0.99f, 0.85f);
                Box(bench, "Tray", new Vector3(0.26f, 0.06f, 0.36f), trays[i], tray);
                for (int k = 0; k < 2; k++)
                    ToyPart(bench, kinds[i], tray + new Vector3(0f, 0.07f, -0.08f + k * 0.16f), Quaternion.Euler(0f, 90f, 0f), 0.6f);
            }

            // A lamp at the west end, bent over the vise.
            Part(bench, "Lamp_Base", Cylinder(0.08f, 0.04f), _grey, new Vector3(4.05f, 0.96f, 0.8f), Quaternion.identity);
            Box(bench, "Lamp_Arm", new Vector3(0.04f, 0.55f, 0.04f), _chrome, new Vector3(4.19f, 1.22f, 0.85f), rotation: Quaternion.Euler(0f, 0f, -30f));
            Part(bench, "Lamp_Shade", Lathe("LampSmall", new[] { new Vector2(0f, 0.12f), new Vector2(0.04f, 0.12f), new Vector2(0.13f, 0f), new Vector2(0.11f, 0f) }, 20),
                _yellow, new Vector3(4.42f, 1.44f, 0.9f), Quaternion.Euler(0f, 0f, 35f));
        }

        // Recognisable toy parts for the hopper, the bins and the bench: 0 a wheel, 1 a robot head
        // with a glowing visor, 2 a teddy head, 3 an arm with its hand.
        static void ToyPart(Transform parent, int kind, Vector3 at, Quaternion turn, float scale = 1f)
        {
            switch (kind % 4)
            {
                case 0:
                    Part(parent, "Part_Wheel", Cylinder(0.11f * scale, 0.07f * scale), _plum, at - turn * Vector3.right * 0.035f * scale, turn * AlongX);
                    Part(parent, "Part_Hub", Cylinder(0.045f * scale, 0.076f * scale), _chrome, at - turn * Vector3.right * 0.038f * scale, turn * AlongX);
                    break;
                case 1:
                    Box(parent, "Part_RobotHead", new Vector3(0.2f, 0.17f, 0.17f) * scale, _white, at, rotation: turn);
                    Box(parent, "Part_Visor", new Vector3(0.15f, 0.045f, 0.02f) * scale, _glow, at + turn * new Vector3(0f, 0.01f, 0.086f) * scale, rotation: turn);
                    break;
                case 2:
                    Part(parent, "Part_TeddyHead", _sphere, _orange, at, turn, scale: Vector3.one * 0.2f * scale);
                    foreach (float side in new[] { -1f, 1f })
                        Part(parent, "Part_Ear", _sphere, _orange, at + turn * new Vector3(side * 0.075f, 0.08f, 0f) * scale, turn, scale: Vector3.one * 0.07f * scale);
                    Part(parent, "Part_Muzzle", _sphere, _white, at + turn * new Vector3(0f, -0.02f, 0.085f) * scale, turn, scale: Vector3.one * 0.07f * scale);
                    break;
                default:
                    Box(parent, "Part_Arm", new Vector3(0.07f, 0.07f, 0.24f) * scale, _yellow, at, rotation: turn);
                    Part(parent, "Part_Hand", _sphere, _yellow, at + turn * new Vector3(0f, 0f, 0.13f) * scale, turn, scale: Vector3.one * 0.09f * scale);
                    break;
            }
        }

        // A cardboard toy box with its brand-coloured band and a strip of tape over the top.
        static void ToyBox(Transform parent, Vector3 centre, Vector3 size, Material band)
        {
            Box(parent, "ToyBox", size, _card, centre);
            Box(parent, "ToyBox_Band", new Vector3(size.x + 0.01f, size.y * 0.24f, size.z + 0.01f), band, centre);
            Box(parent, "ToyBox_Tape", new Vector3(0.09f, size.y + 0.012f, size.z + 0.012f), _white, centre);
        }

        static void Lockers(Transform root)
        {
            Transform lockers = Group(root, "Lockers");
            Blocker(lockers, "Solid", new Vector3(14.4f, 0.95f, 0.8f), new Vector3(3.6f, 1.9f, 0.5f));
            Material[] doors = { _lockerBlue, _lockerMint, _lockerBlue, _lockerMint };
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

        // A wooden pallet of taped cardboard toy boxes, squarely stacked, waiting to ship.
        static void Pallet(Transform root)
        {
            Transform pallet = Group(root, "Pallet");
            Vector3 centre = new Vector3(1.8f, 0f, 1.6f);
            Blocker(pallet, "Solid", centre + Vector3.up * 0.62f, new Vector3(1.4f, 1.24f, 1.2f));
            foreach (float x in new[] { -0.6f, 0f, 0.6f })
                Box(pallet, "Runner", new Vector3(0.12f, 0.1f, 1.2f), _wood, centre + new Vector3(x, 0.05f, 0f));
            for (int i = 0; i < 5; i++)
                Box(pallet, "Board", new Vector3(1.4f, 0.03f, 0.2f), _wood, centre + new Vector3(0f, 0.115f, -0.5f + i * 0.25f));
            Material[] bands = { _red, _blue, _mint, _yellow };
            var size = new Vector3(0.62f, 0.52f, 0.56f);
            for (int i = 0; i < 4; i++)
                ToyBox(pallet, centre + new Vector3(i % 2 == 0 ? -0.33f : 0.33f, 0.13f + size.y * 0.5f, i < 2 ? -0.29f : 0.29f), size, bands[i]);
            for (int i = 0; i < 2; i++)
                ToyBox(pallet, centre + new Vector3(i == 0 ? -0.33f : 0.33f, 0.13f + size.y * 1.5f, 0f), size, bands[(i + 2) % 4]);
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
                    for (int k = 0; k < 3; k++)   // one kind of part per bin, peeking over the rim
                        ToyPart(shelf, i + row * 3, new Vector3(x - 0.1f, y + 0.17f, bz - 0.14f + k * 0.14f), FacingWest, 0.7f);
                }
            for (int i = 0; i < 2; i++)
                ToyBox(shelf, new Vector3(x - 0.02f, 0.38f, z - 0.45f + i * 0.9f), new Vector3(0.45f, 0.5f, 0.85f), i % 2 == 0 ? _orange : _red);
        }

        const float ShelfZ = 17.6f;

        // The belt drive: a gearbox on the west wall at the bottom belt's start, a motor on top, its
        // two gears turning behind a hazard-striped guard, and a chain guard down to the drum the
        // belt runs over. It is why the belt moves. No collider: it stands within the wall's 0.55 m
        // clearance, and the drum is ankle-high at the belt's end.
        static void BeltDrive(Transform root)
        {
            Transform drive = Group(root, "BeltDrive");
            const float z = 7.5f, face = 0.87f;
            Box(drive, "Housing", new Vector3(0.36f, 1.5f, 1.7f), _grey, new Vector3(0.68f, 1.15f, z));
            Box(drive, "Housing_Face", new Vector3(0.02f, 1.3f, 1.5f), _plum, new Vector3(face, 1.15f, z));
            foreach (float y in new[] { 0.46f, 1.84f })
                Box(drive, "Guard", new Vector3(0.05f, 0.08f, 1.6f), _hazard, new Vector3(face + 0.02f, y, z));
            foreach (float dz in new[] { -0.78f, 0.78f })
                Box(drive, "Guard", new Vector3(0.05f, 1.46f, 0.08f), _hazard, new Vector3(face + 0.02f, 1.15f, z + dz));
            Part(drive, "Motor", Cylinder(0.2f, 0.7f), _blue, new Vector3(0.72f, 2.1f, z - 0.35f), AlongZ);
            Part(drive, "Motor_Cap", Cylinder(0.12f, 0.06f), _chrome, new Vector3(0.72f, 2.1f, z + 0.35f), AlongZ);

            // Two meshing gears: the motor turns the big one, the small one drives the chain.
            var big = new Vector3(face + 0.04f, 1.35f, z - 0.25f);
            var small = new Vector3(face + 0.04f, 0.83f, z + 0.29f);
            Gear(drive, "Gear_Big", SavedGear("Gear_Drive_14", 0.5f, 0.42f, 0.08f, 14, 0.06f), _yellow, big, 40f);
            Gear(drive, "Gear_Small", SavedGear("Gear_Drive_9", 0.33f, 0.26f, 0.06f, 9, 0.06f), _red, small, -40f * 14f / 9f);

            // The drum at the belt's west end, turning with it, and the chain guard down to it.
            var drum = new Vector3(1.38f, 0.11f, 6.75f);
            Transform drumSpin = Group(drive, "Drum", drum + new Vector3(0f, 0f, 0.75f));
            Part(drumSpin, "Drum", Cylinder(0.11f, 1.5f), _chrome, drum, AlongZ, moving: true);
            drumSpin.gameObject.AddComponent<DressingSpinner>().Configure(Vector3.forward, -120f);
            Vector3 top = small + Vector3.right * 0.04f, bottom = new Vector3(1.38f, 0.2f, small.z);
            Box(drive, "ChainGuard", new Vector3(0.1f, Vector3.Distance(top, bottom) + 0.1f, 0.12f), _yellow, (top + bottom) * 0.5f,
                rotation: Quaternion.FromToRotation(Vector3.up, top - bottom));
        }

        // A gear facing into the room from the west wall, turning about its centre (moving, so not merged).
        static void Gear(Transform parent, string name, Mesh mesh, Material material, Vector3 centre, float degreesPerSecond)
        {
            Transform pivot = Group(parent, name, centre);
            Part(pivot, name, mesh, material, centre, FacingEast, moving: true);
            Part(pivot, name + "_Hub", Cylinder(0.06f, 0.08f), _chrome, centre, Quaternion.Euler(0f, 0f, -90f), moving: true);
            pivot.gameObject.AddComponent<DressingSpinner>().Configure(Vector3.right, degreesPerSecond);
        }

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
