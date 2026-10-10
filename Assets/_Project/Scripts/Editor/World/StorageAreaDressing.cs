using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using static ToyFactory.Editor.World.DressingKit;
using static ToyFactory.Editor.World.RoomPieces;

namespace ToyFactory.Editor.World
{
    /// <summary>
    /// Builds the Storage Area's final dressing in <c>Env.unity</c> (room 3, x and z 21 to 41) and
    /// restyles the relay order board in <c>Interactables.unity</c>. The room gets its own look,
    /// unlike the Assembly Floor and the Painting Room: steel roof beams carrying aisle signs,
    /// high strip windows, an orange wall band, a loading dock with a roller shutter and a toy
    /// forklift, yellow aisle lines and shelf-end labels, a packing table, a roll cage, and a
    /// coloured ring and plaque at each relay so its colour matches the board. Door 3 rises to
    /// 5 m and both doors are framed. Re-running replaces the previous build.
    /// </summary>
    /// <remarks>
    /// The board keeps its RelayBoard component, collider and three order slots (the board
    /// tints the slots at runtime with the relays' colours); only its look changes, and it
    /// gains a header, numbered steps with arrows, lit sockets, an instruction and, beside it on
    /// the wall, a map of where the three relays are. Everything is clear of S2's relays,
    /// switch cage, pickups and trap, Saboteur A's and D's routes, the shelves and the two
    /// Storage cutscene cameras (the high one looks across the room at the board).
    /// </remarks>
    public static class StorageAreaDressing
    {
        const string AtlasPath = TextureFolder + "/T_Env_Signs_Storage.png";
        
        // The relays (S2): position and colour, and which wall each one stands by.
        static readonly (string name, Vector3 at, Color colour, string word)[] Relays =
        {
            ("Relay_1", new Vector3(40.2f, 0f, 33.5f), Hex("FF3B30"), "RED"),
            ("Relay_2", new Vector3(21.8f, 0f, 25.5f), Hex("34C759"), "GREEN"),
            ("Relay_3", new Vector3(27.8f, 0f, 22.2f), Hex("3A6CF4"), "BLUE")
        };

        // The shelf runs (Level/Obstacles/Storage_Shelf_N): x range, z centre, label.
        static readonly (float x0, float x1, float z, string label)[] Runs =
        {
            (22.42f, 26.71f, 37.5f, "A1"), (29.56f, 35.28f, 37.5f, "A2"), (22.42f, 26.71f, 33.5f, "B1"), (33.86f, 38.15f, 33.5f, "B2"),
            (26.70f, 32.42f, 29.5f, "C1"), (35.29f, 39.58f, 29.5f, "C2"), (23.84f, 26.70f, 25.5f, "D1"), (35.28f, 38.14f, 25.5f, "D2")
        };

        static Material _signs, _screens, _green;

        // Atlas rectangles (pixels, bottom-left origin, 1024 x 1024).
        static readonly RectInt MainSign = new RectInt(0, 832, 1024, 192);
        static readonly RectInt PaintingPlaque = new RectInt(0, 704, 512, 128);
        static readonly RectInt ControlPlaque = new RectInt(512, 704, 512, 128);
        static readonly RectInt BoardHeader = new RectInt(0, 624, 640, 80);
        static readonly RectInt BoardHint = new RectInt(640, 624, 384, 80);
        static RectInt Badge(int i) => new RectInt(i * 96, 528, 96, 96);
        static readonly RectInt Arrow = new RectInt(288, 528, 96, 96);
        static readonly RectInt MiniMap = new RectInt(768, 368, 256, 256);
        static readonly RectInt MapTitle = new RectInt(384, 528, 384, 96);
        static readonly RectInt PosterLift = new RectInt(0, 208, 256, 320);
        static readonly RectInt PosterFragile = new RectInt(256, 208, 256, 320);
        static readonly RectInt PosterCount = new RectInt(512, 208, 256, 320);
        static readonly RectInt DockSign = new RectInt(768, 272, 256, 96);
        static RectInt RelayPlaque(int i) => new RectInt(768, 192 - i * 80, 256, 80);
        static RectInt AisleSign(int i) => new RectInt(i * 384, 112, 384, 96);
        static RectInt ShelfLabel(int i) => new RectInt(i * 96, 8, 96, 96);

        static Rect _uvMapTitle, _uvMain, _uvPainting, _uvControl, _uvHeader, _uvHint, _uvArrow, _uvMap, _uvLift, _uvFragile, _uvCount, _uvDock;
        static readonly Rect[] _uvBadge = new Rect[3], _uvRelay = new Rect[3], _uvAisle = new Rect[2], _uvShelf = new Rect[8];
        static readonly string[] AisleNames = { "PLUSH TOYS", "ROBOTS & BLOCKS" };

        [MenuItem("Tools/Factory Reset/Build Storage Area Dressing")]
        static void RunFromMenu() => Debug.Log(Build());

        [MenuItem("Tools/Factory Reset/Restyle Relay Order Board")]
        static void RunBoardFromMenu() => Debug.Log(BuildBoard());

        /// <summary>Builds (or rebuilds) the room's dressing in the open Env scene and returns a report.</summary>
        public static string Build()
        {
            GameObject level = GameObject.Find("Level");
            if (level == null)
                throw new System.InvalidOperationException("Open Env.unity first: no Level object.");
            Transform dressing = level.transform.Find("Dressing");
            GameObject old = GameObject.Find("Level/Dressing/Storage");
            if (old != null)
                Object.DestroyImmediate(old);

            Load();
            PaintAtlas();
            Transform root = Group(dressing, "Storage");

            Doors(level.transform, root);
            Walls(root);
            Windows(root);
            Dock(root);
            Floor(root);
            Signs(root);
            Props(root);
            Roof(root);
            Shelves(root);

            int probesMoved = MoveProbesOutOfSolids(root);
            int mergedAway = 0;
            foreach (Transform section in root)
                if (section.name != "Shelves")   // merged shelf by shelf in Shelves()
                    mergedAway += MergeStatic(section, "Storage_" + section.name);

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(level.scene);
            var report = new StringBuilder();
            report.AppendLine($"Storage Area dressing built: moved {probesMoved} light probes out of solid props, merged {mergedAway} static parts.");
            foreach (Transform section in root)
                report.AppendLine($"  {section.name}: {section.GetComponentsInChildren<MeshRenderer>(true).Length} renderers, " +
                                  $"{Triangles(section)} triangles, {section.GetComponentsInChildren<BoxCollider>(true).Length} colliders");
            report.AppendLine($"  Total: {root.GetComponentsInChildren<MeshRenderer>(true).Length} renderers, {Triangles(root)} triangles.");
            return report.ToString();
        }

        // ---- Atlas -------------------------------------------------------------------------

        static void PaintAtlas()
        {
            var p = new SignPainter(1024, Plum);

            p.RoundRect(MainSign, 40f, Plum);
            p.RoundFrame(new RectInt(8, 840, 1008, 176), 34f, 8f, Orange);
            p.Circle(new Vector2(104f, 928f), 70f, Orange);
            p.Text("03", new RectInt(54, 888, 100, 80), Plum);
            p.Text("STORAGE AREA", new RectInt(200, 878, 780, 100), Ink);

            // Door plaques (arrows point at the door from where each plaque hangs).
            p.RoundRect(PaintingPlaque, 30f, Bubble);
            p.Text("PAINTING ROOM", new RectInt(24, 728, 380, 80), Ink);
            p.Polygon(new[] { new Vector2(482f, 768f), new Vector2(422f, 728f), new Vector2(422f, 808f) }, Ink);
            p.RoundRect(ControlPlaque, 30f, Grape);
            p.Text("CONTROL ROOM", new RectInt(530, 764, 400, 50), Ink);
            p.Polygon(new[] { new Vector2(1000f, 768f), new Vector2(946f, 732f), new Vector2(946f, 804f) }, Ink);
            p.RoundRect(new RectInt(600, 716, 260, 42), 18f, Tomato);
            p.Text("SEALED", new RectInt(612, 720, 236, 34), Ink);

            // Board header, hint, step badges and the arrow.
            p.RoundRect(BoardHeader, 26f, Orange);
            p.Text("RELAY ORDER", new RectInt(40, 636, 560, 56), Plum);
            p.RoundRect(BoardHint, 20f, Plum);
            p.Text("USE THE RELAYS\nIN THIS ORDER", new RectInt(660, 632, 344, 64), Ink, false);
            for (int i = 0; i < 3; i++)
            {
                RectInt b = Badge(i);
                p.Circle(b.center, 44f, Sun);
                p.Circle(b.center, 36f, Plum);
                p.Text((i + 1).ToString(), new RectInt(b.x + 24, b.y + 20, 48, 56), Sun);
            }
            p.Polygon(new[] { new Vector2(306f, 548f), new Vector2(366f, 576f), new Vector2(306f, 604f), new Vector2(322f, 576f) }, Sun);

            // Map of the room: where the three relays are, and where the board is.
            p.RoundRect(MiniMap, 18f, Plum);
            p.RoundFrame(new RectInt(778, 378, 236, 236), 10f, 4f, Ink);
            Vector2 MapPoint(float x, float z) => new Vector2(MiniMap.x + 18f + (x - 21f) / 20f * 220f, MiniMap.y + 18f + (z - 21f) / 20f * 220f);
            foreach (var run in Runs)
            {
                Vector2 a = MapPoint(run.x0, run.z - 0.5f), b = MapPoint(run.x1, run.z + 0.5f);
                p.RoundRect(new RectInt((int)a.x, (int)a.y, (int)(b.x - a.x), (int)(b.y - a.y)), 2f, Grey);
            }
            foreach (var relay in Relays)
            {
                Vector2 at = MapPoint(relay.at.x, relay.at.z);
                p.Circle(at, 15f, Ink);
                p.Circle(at, 11f, relay.colour);
            }
            Vector2 you = MapPoint(36f, 40.2f);
            p.Polygon(Star(you, 14f, 6f), Sun);
            p.RoundRect(MapTitle, 22f, Orange);
            p.Text("WHERE ARE\nTHE RELAYS?", new RectInt(MapTitle.x + 24, MapTitle.y + 10, MapTitle.width - 48, 76), Plum);

            // Posters.
            p.RoundRect(PosterLift, 22f, Orange);
            p.RoundRect(new RectInt(70, 330, 116, 90), 10f, Ink);
            p.Line(new Vector2(128f, 430f), new Vector2(128f, 470f), 10f, Plum);
            p.Polygon(new[] { new Vector2(100f, 460f), new Vector2(156f, 460f), new Vector2(128f, 492f) }, Plum);
            p.Text("LIFT WITH\nCARE", new RectInt(20, 238, 216, 80), Plum);
            p.RoundRect(PosterFragile, 22f, Ink);
            p.Text("FRAGILE", new RectInt(276, 452, 216, 56), Tomato);
            p.Polygon(new[] { new Vector2(336f, 430f), new Vector2(432f, 430f), new Vector2(408f, 360f), new Vector2(360f, 360f) }, Tomato);
            p.Line(new Vector2(384f, 360f), new Vector2(384f, 310f), 10f, Tomato);
            p.Line(new Vector2(354f, 300f), new Vector2(414f, 300f), 10f, Tomato);
            p.Text("TOYS INSIDE", new RectInt(276, 226, 216, 40), Plum, false);
            p.RoundRect(PosterCount, 22f, Cobalt);
            p.Text("COUNT\nEVERY\nTOY!", new RectInt(532, 300, 216, 200), Ink);
            p.Text("STOCK CHECK FRIDAYS", new RectInt(532, 226, 216, 30), Ink, false);

            // Dock sign, relay plaques, aisle signs, shelf labels.
            p.RoundRect(DockSign, 18f, Sun);
            p.Text("DOCK 3", new RectInt(800, 284, 192, 72), Plum);
            for (int i = 0; i < 3; i++)
            {
                RectInt r = RelayPlaque(i);
                p.RoundRect(new RectInt(r.x + 4, r.y + 4, r.width - 8, r.height - 8), 18f, Ink);
                p.Circle(new Vector2(r.x + 44f, r.y + 40f), 26f, Relays[i].colour);
                p.Text(Relays[i].word + " RELAY", new RectInt(r.x + 80, r.y + 16, 164, 48), Plum);
            }
            for (int i = 0; i < 2; i++)
            {
                RectInt r = AisleSign(i);
                p.RoundRect(new RectInt(r.x + 4, r.y + 4, r.width - 8, r.height - 8), 20f, Orange);
                p.Text(AisleNames[i], new RectInt(r.x + 24, r.y + 18, r.width - 48, 60), Plum);
            }
            for (int i = 0; i < Runs.Length; i++)
            {
                RectInt r = ShelfLabel(i);
                p.RoundRect(new RectInt(r.x + 4, r.y + 4, 88, 88), 14f, Sun);
                p.Text(Runs[i].label, new RectInt(r.x + 14, r.y + 22, 68, 52), Plum);
            }

            Texture2D atlas = p.Save(AtlasPath);
            _uvMain = p.Uv(MainSign); _uvPainting = p.Uv(PaintingPlaque); _uvControl = p.Uv(ControlPlaque);
            _uvHeader = p.Uv(BoardHeader); _uvHint = p.Uv(BoardHint); _uvArrow = p.Uv(Arrow); _uvMap = p.Uv(MiniMap); _uvMapTitle = p.Uv(MapTitle);
            _uvLift = p.Uv(PosterLift); _uvFragile = p.Uv(PosterFragile); _uvCount = p.Uv(PosterCount); _uvDock = p.Uv(DockSign);
            for (int i = 0; i < 3; i++) { _uvBadge[i] = p.Uv(Badge(i)); _uvRelay[i] = p.Uv(RelayPlaque(i)); }
            for (int i = 0; i < 2; i++) _uvAisle[i] = p.Uv(AisleSign(i));
            for (int i = 0; i < Runs.Length; i++) _uvShelf[i] = p.Uv(ShelfLabel(i));
            _signs = Plastic("SignsStorage", Color.white, 0.55f, atlas, new Color(0.18f, 0.18f, 0.18f), atlas);
            _screens = Plastic("SignsStorageGlow", Color.white, 0.55f, atlas, new Color(0.9f, 0.9f, 0.9f), atlas);
            // The green relay is S2's pure green; a matching marker reads better than the palette's mint.
            _green = Plastic("RelayGreen", Relays[1].colour, 0.8f);
        }

        static Material RelayMaterial(int i) => i == 0 ? M.Red : i == 1 ? _green : M.Blue;

        // ---- Doors -------------------------------------------------------------------------

        static void Doors(Transform level, Transform root)
        {
            Transform doors = Group(root, "Doors");
            // Door 2 (south wall, from the Painting Room): its frame on the Storage side.
            Frame(doors, "Door2", new Vector3(31f, 0f, 21.16f), alongZ: false, inward: Vector3.forward);
            Box(doors, "Door2_OpenStrip", new Vector3(2.4f, 0.12f, 0.04f), M.Glow, new Vector3(31f, 5.2f, 21.34f));
            // Door 3 (west wall, to the sealed Control Room): 5 m tall with a white lintel, framed,
            // its alarm beacon on this side moved onto the middle of the frame's header.
            Lintel(level, "Walls/Lintel_Door3", new Vector3(0.5f, 1f, 3f), new Vector3(20.75f, 5.5f, 31f));
            Frame(doors, "Door3", new Vector3(21.16f, 0f, 31f), alongZ: true, inward: Vector3.right);
            Move("Lighting/ControlAlarms/AlarmDoor3/Beacon_B", new Vector3(21.44f, 5.2f, 31f));
            Move("Lighting/ControlAlarms/AlarmDoor3/Backplate_B", new Vector3(21.34f, 5.2f, 31f));
            Move("Lighting/ControlAlarms/AlarmDoor3/Beacon_A", new Vector3(20.38f, 5.5f, 31f));
            Move("Lighting/ControlAlarms/AlarmDoor3/Backplate_A", new Vector3(20.47f, 5.5f, 31f));
            Move("Lighting/ControlAlarms/AlarmDoor3/AlarmLight", new Vector3(20.75f, 4.6f, 31f));
        }

        // ---- Walls: an orange band ---------------------------------------------------------

        static void Walls(Transform root)
        {
            Transform walls = Group(root, "Walls");
            Material band = M.Orange;
            Box(walls, "Band_S1", new Vector3(8.1f, 0.2f, 0.04f), band, new Vector3(25.05f, 1.35f, 21.02f));
            Box(walls, "Band_S2", new Vector3(8.1f, 0.2f, 0.04f), band, new Vector3(36.95f, 1.35f, 21.02f));
            Box(walls, "Band_W1", new Vector3(0.04f, 0.2f, 8.1f), band, new Vector3(21.02f, 1.35f, 25.05f));
            Box(walls, "Band_W2", new Vector3(0.04f, 0.2f, 8.1f), band, new Vector3(21.02f, 1.35f, 36.95f));
            Box(walls, "Band_N", new Vector3(20f, 0.2f, 0.04f), band, new Vector3(31f, 1.35f, 40.98f));
            Box(walls, "Band_E", new Vector3(0.04f, 0.2f, 13.6f), band, new Vector3(40.98f, 1.35f, 34.2f));
            // Wall brackets carrying the roof beams' ends (kept high, clear of the door 3 cutscene shot).
            foreach (float z in new[] { 27.75f, 34.25f })
                foreach (float x in new[] { 21.1f, 40.9f })
                {
                    Box(walls, "Bracket", new Vector3(0.2f, 0.8f, 0.34f), M.Grey, new Vector3(x, 5.3f, z));
                    Box(walls, "Bracket_Plate", new Vector3(0.04f, 1.0f, 0.5f), M.Grey, new Vector3(x < 31f ? 21.02f : 40.98f, 5.25f, z));
                }
        }

        // ---- Windows: high strips ----------------------------------------------------------

        static void Windows(Transform root)
        {
            Transform windows = Group(root, "Windows");
            StripWindow(windows, new Vector3(30.9f, 4.95f, 41f), FacingNorth, 3f);
            StripWindow(windows, new Vector3(39.2f, 4.95f, 41f), FacingNorth, 2.4f);
            StripWindow(windows, new Vector3(41f, 4.95f, 30.8f), FacingWest, 3f);
            StripWindow(windows, new Vector3(41f, 4.95f, 37.6f), FacingWest, 3f);
        }

        // A long, low window high on the wall, with mullions every metre.
        static void StripWindow(Transform parent, Vector3 face, Quaternion facing, float width)
        {
            Vector3 normal = facing * Vector3.forward, right = facing * Vector3.right;
            Transform window = Group(parent, "StripWindow", face);
            Part(window, "Pane", Quad($"Window_Strip_{width:0.#}", new Vector2(width, 0.5f), new Rect(0f, 0.3f, 1f, 0.45f)), M.Window, face + normal * 0.02f, facing);
            Quaternion along = Quaternion.LookRotation(normal, Vector3.up);
            Box(window, "Frame_Top", new Vector3(width + 0.2f, 0.1f, 0.1f), M.Grey, face + normal * 0.05f + Vector3.up * 0.3f, rotation: along);
            Box(window, "Frame_Bottom", new Vector3(width + 0.2f, 0.1f, 0.14f), M.Grey, face + normal * 0.07f - Vector3.up * 0.3f, rotation: along);
            int bays = Mathf.Max(1, Mathf.RoundToInt(width));
            for (int i = 0; i <= bays; i++)
            {
                float t = i / (float)bays - 0.5f;
                Box(window, "Mullion", new Vector3(0.08f, 0.6f, 0.08f), M.Grey, face + normal * 0.05f + right * (t * width), rotation: along);
            }
        }

        // ---- The loading dock --------------------------------------------------------------

        static void Dock(Transform root)
        {
            Transform dock = Group(root, "Dock");
            // A closed roller shutter on the east wall (z 22-27), framed in hazard stripes.
            const float x = 40.96f, z = 24.5f;
            Box(dock, "Shutter", new Vector3(0.06f, 4f, 4.6f), M.Grey, new Vector3(x, 2f, z));
            for (int i = 0; i < 20; i++)
                Box(dock, "Shutter_Slat", new Vector3(0.03f, 0.04f, 4.5f), M.White, new Vector3(x - 0.04f, 0.12f + i * 0.2f, z));
            Box(dock, "Shutter_Box", new Vector3(0.4f, 0.4f, 5.0f), M.Plum, new Vector3(40.8f, 4.2f, z));
            Box(dock, "Frame_S", new Vector3(0.1f, 4.4f, 0.24f), M.Hazard, new Vector3(40.94f, 2.2f, z - 2.42f));
            Box(dock, "Frame_N", new Vector3(0.1f, 4.4f, 0.24f), M.Hazard, new Vector3(40.94f, 2.2f, z + 2.42f));
            Box(dock, "Handle", new Vector3(0.08f, 0.06f, 0.5f), M.Chrome, new Vector3(40.9f, 0.5f, z));
            Part(dock, "DockSign", Quad("Sign_Dock", new Vector2(1.6f, 0.6f), _uvDock), _signs, new Vector3(40.97f, 4.9f, z), FacingWest);
            foreach (float side in new[] { -2.8f, 2.8f })
            {
                Box(dock, "DockLamp_Hood", new Vector3(0.3f, 0.18f, 0.3f), M.Grey, new Vector3(40.82f, 4.0f, z + side));
                Box(dock, "DockLamp", new Vector3(0.2f, 0.06f, 0.2f), M.Amber, new Vector3(40.82f, 3.89f, z + side));
            }
            Box(dock, "Floor_Hazard", new Vector3(1.0f, 0.01f, 4.6f), M.Hazard, new Vector3(40.45f, 0.005f, z));
        }

        // ---- Floor: aisle lines, relay rings -----------------------------------------------

        static void Floor(Transform root)
        {
            Transform floor = Group(root, "Floor");
            foreach (var run in Runs)
                foreach (float side in new[] { -0.65f, 0.65f })
                    Box(floor, "AisleLine", new Vector3(run.x1 - run.x0, 0.01f, 0.08f), M.Yellow, new Vector3((run.x0 + run.x1) * 0.5f, 0.005f, run.z + side));
            for (int i = 0; i < Relays.Length; i++)
            {
                Part(floor, "RelayRing", Ring(0.55f, 0.72f, 0.014f), RelayMaterial(i), Relays[i].at, Quaternion.identity);
                Part(floor, "RelayRing_Glow", Ring(0.76f, 0.8f, 0.016f), M.Glow, Relays[i].at, Quaternion.identity);
            }
        }

        // ---- Signs and posters -------------------------------------------------------------

        static void Signs(Transform root)
        {
            Transform signs = Group(root, "Signs");
            // The room name on the north wall, seen through door 2.
            Box(signs, "MainSign_Back", new Vector3(5.8f, 1.22f, 0.06f), M.Plum, new Vector3(25.3f, 4.1f, 40.97f));
            Part(signs, "MainSign", Quad("Sign_Storage_Main", new Vector2(5.6f, 1.05f), _uvMain), _signs, new Vector3(25.3f, 4.1f, 40.935f), FacingNorth);
            // Door plaques: beside door 2 on the south wall, beside door 3 on the west wall.
            Part(signs, "PaintingPlaque", Quad("Sign_Storage_ToPainting", new Vector2(2f, 0.5f), _uvPainting), _signs, new Vector3(34.6f, 3.3f, 21.02f), FacingSouth);
            Part(signs, "ControlPlaque", Quad("Sign_Storage_ToControl", new Vector2(1.6f, 0.4f), _uvControl), _signs, new Vector3(21.02f, 3.4f, 26.5f), FacingEast);
            // Relay plaques above each relay, on its wall.
            Part(signs, "RelayPlaque_Red", Quad("Sign_Relay_Red", new Vector2(1.4f, 0.44f), _uvRelay[0]), _signs, new Vector3(40.97f, 2.3f, 33.5f), FacingWest);
            Part(signs, "RelayPlaque_Green", Quad("Sign_Relay_Green", new Vector2(1.4f, 0.44f), _uvRelay[1]), _signs, new Vector3(21.03f, 2.3f, 25.5f), FacingEast);
            Part(signs, "RelayPlaque_Blue", Quad("Sign_Relay_Blue", new Vector2(1.4f, 0.44f), _uvRelay[2]), _signs, new Vector3(27.8f, 2.3f, 21.03f), FacingSouth);
            // Map of the relays beside the order board.
            Box(signs, "Map_Back", new Vector3(1.3f, 1.78f, 0.06f), M.Orange, new Vector3(33.4f, 3.99f, 40.97f));
            Part(signs, "Map_Title", Quad("Map_Relays_Title", new Vector2(1.16f, 0.29f), _uvMapTitle), _signs, new Vector3(33.4f, 4.66f, 40.935f), FacingNorth);
            Part(signs, "Map", Quad("Map_Relays", new Vector2(1.16f, 1.16f), _uvMap), _screens, new Vector3(33.4f, 3.75f, 40.935f), FacingNorth);
            // Posters.
            Part(signs, "Poster_Lift", Quad("Poster_Lift", new Vector2(1.2f, 1.5f), _uvLift), _signs, new Vector3(21.02f, 2.95f, 37.2f), FacingEast);
            Part(signs, "Poster_Fragile", Quad("Poster_Fragile", new Vector2(1.2f, 1.5f), _uvFragile), _signs, new Vector3(38f, 2.95f, 21.02f), FacingSouth);
            Part(signs, "Poster_Count", Quad("Poster_Count", new Vector2(1.2f, 1.5f), _uvCount), _signs, new Vector3(40.97f, 2.95f, 39.4f), FacingWest);
            // Labels on the west end of each shelf run.
            for (int i = 0; i < Runs.Length; i++)
                Part(signs, "ShelfLabel", Quad($"Label_Shelf_{Runs[i].label}", new Vector2(0.36f, 0.36f), _uvShelf[i]), _signs,
                    new Vector3(Runs[i].x0 - 0.02f, 2.7f, Runs[i].z), FacingWest);
        }

        // ---- Props -------------------------------------------------------------------------

        static void Props(Transform root)
        {
            Transform props = Group(root, "Props");

            // A toy forklift parked by the dock, forks towards the room, a boxed pallet raised on them.
            var c = new Vector3(39.4f, 0f, 22.4f);
            Blocker(props, "Forklift_Solid", c + new Vector3(-0.2f, 0.9f, 0f), new Vector3(2.6f, 1.8f, 1.2f));
            Box(props, "Forklift_Body", new Vector3(1.4f, 0.6f, 1.1f), M.Yellow, c + new Vector3(0.2f, 0.55f, 0f));
            Box(props, "Forklift_Weight", new Vector3(0.4f, 0.7f, 1.1f), M.Orange, c + new Vector3(0.95f, 0.6f, 0f));
            Box(props, "Forklift_Seat", new Vector3(0.4f, 0.3f, 0.5f), M.Plum, c + new Vector3(0.35f, 1.0f, 0f));
            foreach (Vector2 post in new[] { new Vector2(-0.35f, -0.48f), new Vector2(-0.35f, 0.48f), new Vector2(0.75f, -0.48f), new Vector2(0.75f, 0.48f) })
                Box(props, "Forklift_Post", new Vector3(0.06f, 1.1f, 0.06f), M.Grey, c + new Vector3(post.x, 1.4f, post.y));
            Box(props, "Forklift_Roof", new Vector3(1.3f, 0.08f, 1.12f), M.Yellow, c + new Vector3(0.2f, 1.98f, 0f));
            foreach (Vector2 wheel in new[] { new Vector2(-0.3f, -0.6f), new Vector2(-0.3f, 0.6f), new Vector2(0.7f, -0.6f), new Vector2(0.7f, 0.6f) })
                Part(props, "Forklift_Wheel", Cylinder(0.24f, 0.18f), M.Plum, c + new Vector3(wheel.x, 0.24f, wheel.y - 0.09f), AlongZ);
            foreach (float side in new[] { -0.3f, 0.3f })
            {
                Box(props, "Forklift_Mast", new Vector3(0.08f, 1.8f, 0.08f), M.Grey, c + new Vector3(-0.55f, 0.95f, side));
                Box(props, "Forklift_Fork", new Vector3(0.9f, 0.05f, 0.1f), M.Chrome, c + new Vector3(-1.0f, 0.42f, side));
            }
            Box(props, "Forklift_Pallet", new Vector3(0.9f, 0.12f, 0.9f), M.Orange, c + new Vector3(-1.0f, 0.51f, 0f));
            Box(props, "Forklift_Load", new Vector3(0.8f, 0.6f, 0.8f), M.Yellow, c + new Vector3(-1.0f, 0.87f, 0f));
            Box(props, "Forklift_Load_Tape", new Vector3(0.82f, 0.08f, 0.82f), M.White, c + new Vector3(-1.0f, 0.95f, 0f));

            // Wrapped pallets in the dock bay.
            for (int i = 0; i < 2; i++)
            {
                var at = new Vector3(40.2f, 0f, 25.0f + i * 1.3f);
                Blocker(props, "Pallet_Solid", at + Vector3.up * 0.7f, new Vector3(1.1f, 1.4f, 1.1f));
                Box(props, "Pallet", new Vector3(1.1f, 0.14f, 1.1f), M.Orange, at + Vector3.up * 0.07f);
                Box(props, "Pallet_Stack", new Vector3(1.0f, 1.1f, 1.0f), i == 0 ? M.Yellow : M.Orange, at + Vector3.up * 0.7f);
                Box(props, "Pallet_Wrap", new Vector3(1.04f, 0.9f, 1.04f), M.White, at + Vector3.up * 0.75f);
                Box(props, "Pallet_Strap", new Vector3(1.06f, 0.08f, 1.06f), M.Blue, at + Vector3.up * 0.95f);
            }

            // Packing table on the south wall, west of relay 3.
            var t = new Vector3(24f, 0f, 21.45f);
            Blocker(props, "Table_Solid", t + Vector3.up * 0.5f, new Vector3(2.8f, 1.0f, 0.8f));
            Box(props, "Table_Top", new Vector3(2.8f, 0.06f, 0.8f), M.White, t + Vector3.up * 0.93f);
            foreach (float side in new[] { -1.3f, 1.3f })
                Box(props, "Table_Leg", new Vector3(0.12f, 0.9f, 0.7f), M.Grey, t + new Vector3(side, 0.45f, 0f));
            Box(props, "Table_Box", new Vector3(0.5f, 0.4f, 0.45f), M.Orange, t + new Vector3(-0.8f, 1.16f, 0f));
            Box(props, "Table_Box", new Vector3(0.4f, 0.3f, 0.35f), M.Orange, t + new Vector3(-0.2f, 1.11f, 0.1f));
            Box(props, "Table_Tape", new Vector3(0.2f, 0.12f, 0.1f), M.Red, t + new Vector3(0.3f, 1.02f, 0f));
            Box(props, "Scale", new Vector3(0.45f, 0.08f, 0.4f), M.Grey, t + new Vector3(0.9f, 1.0f, 0f));
            Box(props, "Scale_Display", new Vector3(0.2f, 0.1f, 0.03f), M.Glow, t + new Vector3(0.9f, 1.06f, 0.21f));
            Toy(props, "Teddy", t + new Vector3(0.9f, 1.12f, 0f), 0.6f, Quaternion.Euler(0f, 180f, 0f));

            // A roll cage of boxes on the south wall, east of door 2.
            var r = new Vector3(34.7f, 0f, 21.55f);
            Blocker(props, "Cage_Solid", r + Vector3.up * 0.85f, new Vector3(1.3f, 1.7f, 0.85f));
            Box(props, "Cage_Base", new Vector3(1.3f, 0.08f, 0.85f), M.Grey, r + Vector3.up * 0.18f);
            foreach (Vector2 corner in new[] { new Vector2(-0.62f, -0.4f), new Vector2(0.62f, -0.4f), new Vector2(-0.62f, 0.4f), new Vector2(0.62f, 0.4f) })
            {
                Part(props, "Cage_Wheel", Cylinder(0.07f, 0.06f), M.Plum, r + new Vector3(corner.x, 0.07f, corner.y - 0.03f), AlongZ);
                Part(props, "Cage_Bar", Cylinder(0.02f, 1.5f), M.Chrome, r + new Vector3(corner.x, 0.2f, corner.y), Quaternion.identity);
            }
            foreach (float y in new[] { 0.7f, 1.2f, 1.7f })
            {
                Box(props, "Cage_Rail", new Vector3(1.26f, 0.03f, 0.03f), M.Chrome, r + new Vector3(0f, y, -0.4f));
                Box(props, "Cage_Rail", new Vector3(1.26f, 0.03f, 0.03f), M.Chrome, r + new Vector3(0f, y, 0.4f));
            }
            Box(props, "Cage_Box", new Vector3(0.55f, 0.5f, 0.6f), M.Orange, r + new Vector3(-0.3f, 0.47f, 0f));
            Box(props, "Cage_Box", new Vector3(0.5f, 0.45f, 0.55f), M.Yellow, r + new Vector3(0.32f, 0.45f, 0f));
            Box(props, "Cage_Box", new Vector3(0.5f, 0.4f, 0.5f), M.Orange, r + new Vector3(0f, 0.92f, 0f));
        }

        // ---- Roof: steel beams with aisle signs --------------------------------------------

        // Two steel beams across the room between the light panels' rows (z 24.1-24.9,
        // 30.6-31.4, 37.1-37.9), each carrying a double-sided aisle sign between the panels'
        // columns. The signs hang no lower than 4.9 m, above the high cutscene camera's view of the board.
        static void Roof(Transform root)
        {
            Transform roof = Group(root, "Roof");
            (float z, float signX)[] beams = { (27.75f, 34.25f), (34.25f, 27.75f) };
            for (int i = 0; i < beams.Length; i++)
            {
                float z = beams[i].z;
                Box(roof, "Beam", new Vector3(20f, 0.26f, 0.12f), M.Grey, new Vector3(31f, 5.8f, z));
                Box(roof, "Beam_Flange", new Vector3(20f, 0.04f, 0.3f), M.Grey, new Vector3(31f, 5.69f, z));
                var sign = new Vector3(beams[i].signX, 5.15f, z);
                foreach (float side in new[] { -0.4f, 0.4f })
                    Part(roof, "Sign_Chain", Cylinder(0.015f, 0.5f), M.Chrome, sign + new Vector3(side, 0.18f, 0f), Quaternion.identity);
                Box(roof, "Sign_Board", new Vector3(1.7f, 0.5f, 0.06f), M.Orange, sign);
                Part(roof, "Sign_Front", Quad($"Sign_Aisle_{i}", new Vector2(1.6f, 0.4f), _uvAisle[i]), _signs, sign + Vector3.back * 0.035f, FacingNorth);
                Part(roof, "Sign_Back", Quad($"Sign_Aisle_{i}", new Vector2(1.6f, 0.4f), _uvAisle[i]), _signs, sign + Vector3.forward * 0.035f, FacingSouth);
            }
        }

        // ---- Shelves: stocked with toys ----------------------------------------------------

        // The shelf runs held placeholder blocks; they now hold teddies, robots, ducks and taped
        // toy boxes on the lower two boards and boxed overstock on the third, a row each side of
        // the spine, facing the aisle. The teddies and ducks are low-poly versions of the room
        // toys (8-sided balls), as there are a few hundred items. Each shelf's stock is merged on
        // its own and stays lit by light probes (not lightmaps), as the old toys were, so the
        // lightmaps do not grow.
        static void Shelves(Transform root)
        {
            Transform shelves = Group(root, "Shelves");
            var random = new System.Random(31);
            // The toy meshes are combined once here, not once per item.
            _teddy = ShelfTeddy();
            _duck = ShelfDuck();
            GameObject robot = Toy(shelves, "Robot", Vector3.zero, 1f, Quaternion.identity);
            _robot = robot.GetComponent<MeshFilter>().sharedMesh;
            Object.DestroyImmediate(robot);
            for (int n = 1; n <= Runs.Length; n++)
            {
                GameObject run = GameObject.Find($"Level/Obstacles/Storage_Shelf_{n}");
                Transform stock = run != null ? run.transform.Find("Dressing") : null;
                if (stock == null)
                    continue;
                var boards = new List<float>();
                var placeholders = new List<GameObject>();
                foreach (Transform child in stock)
                {
                    if (child.name.StartsWith("Toy_"))
                        placeholders.Add(child.gameObject);
                    else if (child.name.StartsWith("Board_"))
                        boards.Add(child.GetComponent<Renderer>().bounds.max.y);
                }
                foreach (GameObject placeholder in placeholders)
                    Object.DestroyImmediate(placeholder);
                boards.Sort();

                Bounds body = run.GetComponent<Collider>().bounds;
                Transform shelf = Group(shelves, $"Shelf_{n}");
                for (int b = 0; b < Mathf.Min(3, boards.Count); b++)
                    foreach (float side in new[] { -1f, 1f })
                    {
                        float x = body.min.x + 0.42f;
                        while (x < body.max.x - 0.3f)
                        {
                            var at = new Vector3(x + (float)(random.NextDouble() - 0.5) * 0.08f, boards[b], body.center.z + side * 0.25f);
                            Quaternion facing = Quaternion.Euler(0f, (side > 0f ? 0f : 180f) + (float)(random.NextDouble() - 0.5) * 40f, 0f);
                            int roll = random.Next(10);
                            StockItem(shelf, b == 2 ? 5 + roll % 5 : roll, random, at, facing);
                            x += 0.62f + (float)random.NextDouble() * 0.16f;
                        }
                    }
                MergeStatic(shelf, $"Storage_Shelf_{n}");
                foreach (MeshRenderer merged in shelf.GetComponentsInChildren<MeshRenderer>())
                {
                    GameObjectUtility.SetStaticEditorFlags(merged.gameObject, StaticEditorFlags.BatchingStatic |
                        StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic);
                    merged.receiveGI = ReceiveGI.LightProbes;
                }
            }
        }

        static Mesh _teddy, _duck, _robot;

        // One item standing on a board at 'at' (the board's top), by 'kind' 0-9: a teddy (0-1),
        // a robot (2), a duck (3-4) or a taped toy box (5-7 wide, 8-9 tall), in mixed palette colours.
        static void StockItem(Transform shelf, int kind, System.Random random, Vector3 at, Quaternion facing)
        {
            Material Pick(params Material[] options) => options[random.Next(options.Length)];
            if (kind <= 1)
                Part(shelf, "Teddy", _teddy, new[] { Pick(M.Orange, M.Bubble, M.Mint, M.Purple), M.White, M.Plum }, at + Vector3.up * 0.125f, facing, scale: Vector3.one * 1.15f);
            else if (kind == 2)
                Part(shelf, "Robot", _robot, new[] { M.Blue, M.White, M.Glow, M.Chrome }, at + Vector3.up * 0.105f, facing, scale: Vector3.one * 1.3f);
            else if (kind <= 4)
                Part(shelf, "Duck", _duck, new[] { Pick(M.Yellow, M.Yellow, M.Bubble, M.Red), M.Orange, M.Plum }, at + Vector3.up * 0.075f, facing, scale: Vector3.one * 1.2f);
            else
            {
                bool tall = kind >= 8;
                var size = tall ? new Vector3(0.3f, 0.46f, 0.28f) : new Vector3(0.42f, 0.32f, 0.3f);
                Box(shelf, "ToyBox", size, Pick(M.Orange, M.Yellow, M.Mint, M.Bubble, M.Purple, M.Red), at + Vector3.up * size.y * 0.5f, rotation: facing);
                Box(shelf, "ToyBox_Tape", new Vector3(size.x + 0.01f, 0.06f, size.z + 0.01f), M.White, at + Vector3.up * size.y * 0.62f, rotation: facing);
                Part(shelf, "ToyBox_Label", Quad($"Label_ToyBox_{(tall ? "Tall" : "Wide")}", new Vector2(size.x * 0.45f, size.y * 0.3f), new Rect(0f, 0f, 1f, 1f)),
                    Pick(M.Plum, M.White), at + Vector3.up * size.y * 0.32f + facing * Vector3.forward * (size.z * 0.5f + 0.003f), facing);
            }
        }

        // A sphere of radius 0.5 with 8 sides and 5 rings (about 64 triangles), for the shelf toys.
        static Mesh LowBall()
        {
            var profile = new Vector2[6];
            for (int i = 0; i <= 5; i++)
            {
                float a = Mathf.Lerp(-90f, 90f, i / 5f) * Mathf.Deg2Rad;
                profile[i] = new Vector2(i == 0 || i == 5 ? 0f : Mathf.Cos(a) * 0.5f, Mathf.Sin(a) * 0.5f);
            }
            return Lathe("Sphere8x5", profile, 8);
        }

        static Matrix4x4 At(float x, float y, float z, float size) => Matrix4x4.TRS(new Vector3(x, y, z), Quaternion.identity, Vector3.one * size);

        // The room teddy (RoomPieces.Toy) in low poly.
        static Mesh ShelfTeddy()
        {
            Mesh ball = LowBall();
            return Combine("Toy_Teddy_Shelf", new (Mesh, Matrix4x4, int)[]
            {
                (ball, At(0f, 0.04f, 0f, 0.3f), 0), (ball, At(0f, 0.27f, 0f, 0.26f), 0),
                (ball, At(-0.1f, 0.38f, 0f, 0.1f), 0), (ball, At(0.1f, 0.38f, 0f, 0.1f), 0),
                (ball, At(0f, 0.24f, 0.11f, 0.1f), 1), (ball, At(0f, 0.27f, 0.155f, 0.04f), 2),
                (ball, At(-0.05f, 0.31f, 0.115f, 0.035f), 2), (ball, At(0.05f, 0.31f, 0.115f, 0.035f), 2),
                (ball, At(-0.15f, 0.06f, 0.03f, 0.11f), 0), (ball, At(0.15f, 0.06f, 0.03f, 0.11f), 0),
            }, 3);
        }

        // The room duck (RoomPieces.Toy) in low poly.
        static Mesh ShelfDuck()
        {
            Mesh ball = LowBall();
            return Combine("Toy_Duck_Shelf", new (Mesh, Matrix4x4, int)[]
            {
                (ball, Matrix4x4.TRS(new Vector3(0f, 0.05f, 0f), Quaternion.identity, new Vector3(0.28f, 0.22f, 0.34f)), 0),
                (ball, At(0f, 0.22f, 0.1f, 0.19f), 0),
                (RoundBox(new Vector3(0.1f, 0.035f, 0.09f)), At(0f, 0.2f, 0.21f, 1f), 1),
                (ball, At(-0.05f, 0.26f, 0.18f, 0.03f), 2), (ball, At(0.05f, 0.26f, 0.18f, 0.03f), 2),
            }, 3);
        }

        // ---- The relay order board (Interactables.unity) -----------------------------------

        /// <summary>
        /// Restyles S2's relay order board in the open Interactables scene: a dark rounded panel
        /// in an orange frame, a RELAY ORDER header, lit sockets round the three slots (which the
        /// board still tints with the relays' colours), numbered step badges with arrows, and an
        /// instruction line. The board's component, collider and slots are kept.
        /// </summary>
        public static string BuildBoard()
        {
            // RelayBoard lives in S2's scripts (Assembly-CSharp), out of this assembly's reach: find it by name.
            GameObject boardObject = GameObject.Find("RelayBoard");
            Component board = boardObject != null ? boardObject.GetComponent("RelayBoard") : null;
            if (board == null)
                throw new System.InvalidOperationException("Open Interactables.unity first: no RelayBoard.");
            Load();
            PaintAtlas();
            Transform b = board.transform;
            Transform oldDisplay = b.Find("Display");
            if (oldDisplay != null)
                Object.DestroyImmediate(oldDisplay.gameObject);

            var serialized = new SerializedObject(board);
            SerializedProperty slots = serialized.FindProperty("orderSlots");
            var slotRenderers = new List<Renderer>();
            for (int i = 0; i < slots.arraySize; i++)
                if (slots.GetArrayElementAtIndex(i).objectReferenceValue is Renderer slot)
                    slotRenderers.Add(slot);

            // The board was a scaled cube: give it a rounded mesh at unit scale, keeping the
            // slots where they are (they are its children) and the collider the same size.
            var collider = board.GetComponent<BoxCollider>();
            Vector3 size = Vector3.Scale(collider.size, b.lossyScale);
            if (b.localScale != Vector3.one)
            {
                var children = new List<Transform>();
                foreach (Transform child in b)
                    children.Add(child);
                foreach (Transform child in children)
                    child.SetParent(null, true);
                b.localScale = Vector3.one;
                foreach (Transform child in children)
                    child.SetParent(b, true);
            }
            board.GetComponent<MeshFilter>().sharedMesh = RoundBox(size);
            board.GetComponent<MeshRenderer>().sharedMaterial = M.Plum;
            collider.center = Vector3.zero;
            collider.size = size;

            // Rounded slots in clean white plastic: the board tints them with the relays' colours.
            foreach (Renderer slot in slotRenderers)
            {
                Vector3 slotSize = Vector3.Scale(slot.GetComponent<MeshFilter>().sharedMesh.bounds.size, slot.transform.lossyScale);
                slot.transform.SetParent(null, true);
                slot.transform.localScale = Vector3.one;
                slot.transform.SetParent(b, true);
                slot.GetComponent<MeshFilter>().sharedMesh = RoundBox(slotSize);
                slot.sharedMaterial = M.White;
            }

            // The display: everything sits on an orange backplate behind the board, facing the room.
            Vector3 c = b.position;
            Quaternion facing = b.rotation * FacingNorth;            // the board is on the north wall
            Vector3 front = facing * Vector3.forward, right = facing * Vector3.right;
            float back = size.z * 0.5f;                              // the board's back face
            Transform display = Group(b, "Display", c);
            Vector3 plate = c - front * (back + 0.04f);              // the backplate's centre
            Vector3 onPlate = plate + front * 0.035f;                // just in front of it
            Part(display, "Backplate", RoundBox(new Vector3(3.3f, 2.6f, 0.06f)), M.Orange, plate - Vector3.up * 0.1f, facing, moving: true);
            Part(display, "Backplate_Inner", RoundBox(new Vector3(3.1f, 2.4f, 0.02f)), M.Plum, plate - Vector3.up * 0.1f + front * 0.035f, facing, moving: true);
            Part(display, "Header", Quad("Board_Header", new Vector2(2.6f, 0.33f), _uvHeader), _signs, onPlate + front * 0.02f + Vector3.up * 0.85f, facing, moving: true);
            Part(display, "Hint", Quad("Board_Hint", new Vector2(1.5f, 0.31f), _uvHint), _signs, onPlate + front * 0.02f - Vector3.up * 1.1f, facing, moving: true);

            Vector3 slotFront = front * 0.05f;
            for (int i = 0; i < slotRenderers.Count; i++)
            {
                Vector3 s = slotRenderers[i].transform.position;
                // A chrome socket round each slot, a numbered badge under it, an arrow to the next.
                Part(display, "Socket", RoundBox(new Vector3(0.5f, 0.5f, 0.03f)), M.Chrome, s - front * 0.015f, facing, moving: true);
                Part(display, "Badge", Quad($"Board_Badge_{i + 1}", new Vector2(0.34f, 0.34f), _uvBadge[i]), _signs,
                    new Vector3(s.x, c.y - size.y * 0.5f - 0.23f, onPlate.z) + front * 0.02f, facing, moving: true);
                if (i < slotRenderers.Count - 1)
                {
                    Vector3 next = slotRenderers[i + 1].transform.position;
                    Part(display, "Arrow", Quad("Board_Arrow", new Vector2(0.2f, 0.2f), _uvArrow), _signs,
                        (s + next) * 0.5f + slotFront, facing, moving: true);
                }
            }
            // Two small lamps on arms over the board, lighting it.
            foreach (float side in new[] { -1.2f, 1.2f })
            {
                Vector3 lamp = plate + Vector3.up * 1.35f + right * side + front * 0.3f;
                Part(display, "Lamp_Arm", RoundBox(new Vector3(0.05f, 0.05f, 0.34f)), M.Grey, lamp - front * 0.17f, facing, moving: true);
                Part(display, "Lamp_Hood", RoundBox(new Vector3(0.3f, 0.12f, 0.2f)), M.Grey, lamp, facing, moving: true);
                Part(display, "Lamp_Light", RoundBox(new Vector3(0.22f, 0.03f, 0.12f)), M.Amber, lamp - Vector3.up * 0.07f, facing, moving: true);
            }

            EditorUtility.SetDirty(board);
            EditorSceneManager.MarkSceneDirty(board.gameObject.scene);
            AssetDatabase.SaveAssets();
            return $"Relay board restyled: {slotRenderers.Count} slots kept, {display.GetComponentsInChildren<MeshRenderer>().Length} display parts.";
        }
    }
}
