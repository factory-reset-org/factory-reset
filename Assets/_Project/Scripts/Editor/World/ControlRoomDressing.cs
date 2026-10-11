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
    /// Builds the Control Room's final dressing in <c>Env.unity</c> (room 4, x 0.5 to 20.5, z 21
    /// to 41): Factory OS's lair, the darkest room in the factory. The twelve server racks in the
    /// middle are rebuilt as detailed cabinets on their old footprints and colliders; bundles of
    /// cable leave each rack, drop through floor ports into grated, red-lit channels and run to
    /// banks of server cabinets built into the walls, where they rise again. A mainframe stands
    /// under a wall of screens showing Factory OS's status. There are no windows;
    /// the walls are clad dark, the ceiling lights are dimmed and cooled, and red light comes from
    /// the cables and channels, violet from the strips and screens. Re-running replaces the previous build.
    /// </summary>
    /// <remarks>
    /// Everything stays clear of S2's console, cores, traps and pickups, the Captain's spawn and
    /// the Control cutscene cameras (the Captain's wake shot looks through the gap in the middle
    /// server row, which the cables leave clear). The three screens keep their objects, which
    /// <c>LightingState</c> dims at the shutdown; the status lights blink with
    /// <see cref="DressingBlinker"/> and go dark at the shutdown too.
    /// </remarks>
    public static class ControlRoomDressing
    {
        const string AtlasPath = TextureFolder + "/T_Env_Signs_Control.png";
        const float CabinetWidth = 1.2f, CabinetDepth = 0.4f, CabinetHeight = 3.2f;

        static readonly Color Night = Hex("0B0910"), Panel = Hex("231E31"), Slot = Hex("110E18"),
            Bay = Hex("3A3450"), Edge = Hex("5B5277"), Blood = Hex("E0203A"), Ember = Hex("FF5A3C");

        static Material _signs, _screens, _shell, _trim, _black, _wall, _red, _violet, _cable, _cableGlow, _panel;

        // Atlas rectangles (pixels, bottom-left origin, 1024 x 1024).
        static readonly RectInt MainSign = new RectInt(0, 944, 1024, 80);
        static readonly RectInt StatusScreen = new RectInt(0, 688, 512, 256);
        static readonly RectInt GraphScreen = new RectInt(512, 688, 512, 256);
        static readonly RectInt CoreScreen = new RectInt(0, 432, 512, 256);
        static readonly RectInt VentPanel = new RectInt(512, 432, 256, 256);
        static readonly RectInt Logo = new RectInt(768, 432, 256, 256);
        static readonly RectInt UnitStorage = new RectInt(0, 236, 512, 196);
        static readonly RectInt UnitFans = new RectInt(512, 236, 512, 196);
        static readonly RectInt UnitDrives = new RectInt(0, 138, 512, 98);
        static readonly RectInt Trench = new RectInt(512, 138, 256, 98);
        static readonly RectInt Danger = new RectInt(768, 138, 256, 98);
        static readonly RectInt UnitVent = new RectInt(0, 90, 512, 48);
        static readonly RectInt UnitSwitch = new RectInt(512, 90, 512, 48);
        static RectInt Plaque(int i) => new RectInt(i * 256, 6, 256, 80);

        static Rect _uvMain, _uvStatus, _uvGraph, _uvCores, _uvVent, _uvLogo, _uvTrench, _uvDanger;
        static readonly Rect[] _uvUnit = new Rect[5], _uvPlaque = new Rect[4];

        // Server units: atlas picture and height (m). 0 vent 1U, 1 switch 1U, 2 drives 2U, 3 storage 4U, 4 fans 4U.
        static readonly float[] UnitHeight = { 0.1f, 0.1f, 0.2f, 0.4f, 0.4f };
        static readonly int[] RackFront = { 2, 0, 3, 1, 2, 0, 3, 1, 2, 0 };
        static readonly int[] RackRear = { 4, 1, 4, 0, 4, 1 };
        static readonly int[] CabinetFront = { 1, 2, 2, 0, 3, 1, 2, 0, 3, 1, 2, 0 };

        // Cable bundles, one tube list per material; LED quads per blinker group.
        static readonly List<(Vector3 a, Vector3 b, Vector3 c, Vector3 d)> _cables = new List<(Vector3, Vector3, Vector3, Vector3)>();
        static readonly List<(Vector3 a, Vector3 b, Vector3 c, Vector3 d)> _glowCables = new List<(Vector3, Vector3, Vector3, Vector3)>();

        [MenuItem("Tools/Factory Reset/Build Control Room Dressing")]
        static void RunFromMenu() => Debug.Log(Build());

        /// <summary>Builds (or rebuilds) the room's dressing in the open Env scene and returns a report.</summary>
        public static string Build()
        {
            GameObject level = GameObject.Find("Level");
            if (level == null)
                throw new System.InvalidOperationException("Open Env.unity first: no Level object.");
            Transform dressing = level.transform.Find("Dressing");
            foreach (string old in new[] { "Level/Dressing/Control", "Level/Dressing/ServerRacks" })
            {
                GameObject previous = GameObject.Find(old);
                if (previous != null)
                    Object.DestroyImmediate(previous);
            }

            Load();
            Materials();
            PaintAtlas();
            _cables.Clear();
            _glowCables.Clear();
            Transform root = Group(dressing, "Control");

            Mood();
            Doors(root);
            Walls(root);
            var leds = Group(root, "Leds");
            Servers(root, leds);
            Banks(root, leds);
            Mainframe(root, leds);
            Screens(root);
            Signs(root);
            Cables(root);

            int probesMoved = MoveProbesOutOfSolids(root);
            int mergedAway = 0;
            foreach (Transform section in root)
            {
                if (section == leds)
                    continue;
                // The racks merge per row and the cabinets per bank, not per room, so occlusion
                // culling can leave out the ones out of view (a room-wide mesh is drawn whole
                // whenever any of it shows, for example through an open doorway).
                if (section.name == "Servers" || section.name == "Banks")
                    foreach (Transform part in section)
                        mergedAway += MergeStatic(part, $"Control_{section.name}_{part.name}");
                else
                    mergedAway += MergeStatic(section, "Control_" + section.name);
            }

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(level.scene);
            var report = new StringBuilder();
            report.AppendLine($"Control Room dressing built: moved {probesMoved} light probes out of solid props, merged {mergedAway} static parts.");
            foreach (Transform section in root)
                report.AppendLine($"  {section.name}: {section.GetComponentsInChildren<MeshRenderer>(true).Length} renderers, " +
                                  $"{Triangles(section)} triangles, {section.GetComponentsInChildren<BoxCollider>(true).Length} colliders");
            report.AppendLine($"  Total: {root.GetComponentsInChildren<MeshRenderer>(true).Length} renderers, {Triangles(root)} triangles.");
            return report.ToString();
        }

        // ---- Materials and mood ------------------------------------------------------------

        static void Materials()
        {
            _shell = Plastic("ServerShell", Hex("1C1828"), 0.8f);
            _trim = Plastic("ServerTrim", Hex("3A3450"), 0.85f);
            _black = Plastic("ServerBlack", Night, 0.6f);
            _wall = Plastic("ControlWall", Hex("241E34"), 0.65f);
            _red = Plastic("GlowRed", Blood, 0.6f, null, new Color(2.4f, 0.18f, 0.28f));
            _violet = Plastic("GlowViolet", Grape, 0.6f, null, new Color(0.6f, 0.36f, 1.6f));
            _cable = Plastic("Cable", Hex("17141F"), 0.45f);
            _cableGlow = Plastic("CableGlow", Hex("A01628"), 0.5f, null, new Color(1.1f, 0.06f, 0.14f));
            _panel = Plastic("CeilingPanelControl", Hex("C9BDFF"), 0.9f, null, new Color(0.75f, 0.68f, 1.35f));
        }

        // The dark look: a dark floor whose grid glows violet, dimmed cool ceiling lights, a violet glow over the mainframe.
        static void Mood()
        {
            Material floor = Mat("FloorTiled_Control");
            floor.SetColor("_BaseColor", Hex("4A4260"));
            floor.EnableKeyword("_EMISSION");
            floor.SetColor("_EmissionColor", new Color(0.16f, 0.07f, 0.3f));
            EditorUtility.SetDirty(floor);

            GameObject panels = GameObject.Find("Panels_Control");
            if (panels != null)
                foreach (MeshRenderer panel in panels.GetComponentsInChildren<MeshRenderer>())
                    panel.sharedMaterial = _panel;

            GameObject ceiling = GameObject.Find("Lighting/CeilingLights_Control");
            if (ceiling != null)
                foreach (Light light in ceiling.GetComponentsInChildren<Light>())
                {
                    light.color = Hex("9C8EFF");
                    light.intensity = 9f;
                }
            GameObject fill = GameObject.Find("Lighting/RoomFills_Baked/Fill_Control");
            if (fill != null)
            {
                var light = fill.GetComponent<Light>();
                light.color = Hex("5A4A8A");
                light.intensity = 0.5f;
            }
            GameObject fills = GameObject.Find("Lighting/RoomFills_Baked");
            if (fills != null)
            {
                Transform mainframeGlow = fills.transform.Find("Glow_Mainframe") ?? fills.transform.Find("Glow_FactoryEye");
                if (mainframeGlow == null)
                {
                    mainframeGlow = new GameObject("Glow_Mainframe").transform;
                    mainframeGlow.SetParent(fills.transform, false);
                    var added = mainframeGlow.gameObject.AddComponent<Light>();
                    added.type = LightType.Point;
                    added.lightmapBakeType = LightmapBakeType.Baked;
                    added.shadows = LightShadows.Soft;
                }
                mainframeGlow.name = "Glow_Mainframe";
                mainframeGlow.position = new Vector3(10.5f, 3.2f, 39.4f);
                var glow = mainframeGlow.GetComponent<Light>();
                glow.color = Grape;
                glow.intensity = 1.6f;
                glow.range = 9f;
            }
        }

        // ---- Atlas -------------------------------------------------------------------------

        static void PaintAtlas()
        {
            var p = new SignPainter(1024, Night);

            // Room sign.
            p.RoundRect(MainSign, 30f, Night);
            p.RoundFrame(new RectInt(6, 950, 1012, 68), 26f, 5f, Blood);
            p.Circle(new Vector2(70f, 984f), 30f, Blood);
            p.Text("04", new RectInt(44, 964, 52, 42), Night);
            p.Text("CONTROL ROOM", new RectInt(140, 956, 760, 58), Ink);

            // The mainframe's status screen: the Factory OS gear, its name and three busy bars.
            p.RoundRect(StatusScreen, 0f, Hex("0A0812"));
            for (int y = StatusScreen.y; y < StatusScreen.yMax; y += 6)
                p.RoundRect(new RectInt(StatusScreen.x, y, StatusScreen.width, 2), 0f, Hex("120E1E"));
            Emblem(p, new Vector2(StatusScreen.x + 120f, StatusScreen.y + 128f), 92f);
            p.Text("FACTORY OS", new RectInt(StatusScreen.x + 236, StatusScreen.y + 150, 256, 56), Ink);
            p.Text("MAINFRAME ONLINE", new RectInt(StatusScreen.x + 236, StatusScreen.y + 112, 256, 30), Grape, false);
            for (int i = 0; i < 3; i++)
            {
                p.RoundRect(new RectInt(StatusScreen.x + 240, StatusScreen.y + 76 - i * 22, 240, 12), 6f, Hex("1E1438"));
                p.RoundRect(new RectInt(StatusScreen.x + 240, StatusScreen.y + 76 - i * 22, 120 + i * 50, 12), 6f, i == 2 ? Blood : Grape);
            }

            // System load graphs.
            p.RoundRect(GraphScreen, 0f, Hex("08050C"));
            for (int x = GraphScreen.x + 32; x < GraphScreen.xMax; x += 48)
                p.RoundRect(new RectInt(x, GraphScreen.y + 20, 2, 180), 0f, Hex("2A0E1C"));
            for (int y = GraphScreen.y + 20; y < GraphScreen.y + 200; y += 36)
                p.RoundRect(new RectInt(GraphScreen.x + 20, y, 472, 2), 0f, Hex("2A0E1C"));
            int[] bars = { 60, 90, 75, 120, 140, 110, 160, 150, 175 };
            for (int i = 0; i < bars.Length; i++)
                p.RoundRect(new RectInt(GraphScreen.x + 34 + i * 50, GraphScreen.y + 22, 30, bars[i]), 4f, i == bars.Length - 1 ? Blood : Grape);
            Vector2 last = Vector2.zero;
            for (int i = 0; i < 10; i++)
            {
                var point = new Vector2(GraphScreen.x + 30 + i * 50, GraphScreen.y + 60 + Mathf.Sin(i * 1.3f) * 30f + i * 12f);
                if (i > 0)
                    p.Line(last, point, 5f, Ink);
                last = point;
            }
            p.Text("SYSTEM LOAD", new RectInt(GraphScreen.x + 40, GraphScreen.y + 206, 432, 42), Ink);

            // Power cores.
            p.RoundRect(CoreScreen, 0f, Hex("08050C"));
            p.Text("POWER CORES", new RectInt(CoreScreen.x + 96, CoreScreen.y + 206, 320, 40), Grape);
            for (int i = 0; i < 3; i++)
            {
                var c = new Vector2(CoreScreen.x + 96 + i * 160, CoreScreen.y + 128);
                p.Circle(c, 52f, Hex("1E1438"));
                p.Ring(c, 40f, 50f, Grape);
                p.Circle(c, 26f, Hex("B9A6FF"));
                p.RoundRect(new RectInt((int)c.x - 50, CoreScreen.y + 30, 100, 14), 6f, Hex("1E1438"));
                p.RoundRect(new RectInt((int)c.x - 50, CoreScreen.y + 30, 70 + i * 12, 14), 6f, Grape);
                p.Text($"CORE {i + 1}", new RectInt((int)c.x - 50, CoreScreen.y + 48, 100, 22), Ink, false);
            }

            // Side panel vents and the Factory OS logo.
            p.RoundRect(VentPanel, 10f, Panel);
            p.RoundFrame(new RectInt(VentPanel.x + 6, VentPanel.y + 6, 244, 244), 8f, 4f, Edge);
            for (int y = VentPanel.y + 24; y < VentPanel.yMax - 20; y += 16)
            {
                p.RoundRect(new RectInt(VentPanel.x + 28, y, 88, 7), 3f, Slot);
                p.RoundRect(new RectInt(VentPanel.x + 140, y, 88, 7), 3f, Slot);
            }
            p.RoundRect(Logo, 24f, Panel);
            Emblem(p, new Vector2(Logo.x + 128f, Logo.y + 150f), 80f);
            p.Text("FACTORY OS", new RectInt(Logo.x + 24, Logo.y + 18, 208, 36), Ink);

            // Server unit fronts.
            Unit(p, UnitVent, 0);
            Unit(p, UnitSwitch, 1);
            Unit(p, UnitDrives, 2);
            Unit(p, UnitStorage, 3);
            Unit(p, UnitFans, 4);

            // Floor channel: a grate over a dark trench of cables, one of them lit red.
            p.RoundRect(Trench, 0f, Hex("0D0A14"));
            Color[] cables = { Hex("2A2536"), Hex("3A2A44"), Blood, Hex("221E2C"), Hex("5A1C2E") };
            for (int i = 0; i < cables.Length; i++)
                p.RoundRect(new RectInt(Trench.x, Trench.y + 14 + i * 15, Trench.width, 9), 4f, cables[i]);
            for (int x = Trench.x + 10; x < Trench.xMax; x += 32)
                p.RoundRect(new RectInt(x, Trench.y, 6, Trench.height), 2f, Hex("4A4460"));
            p.RoundRect(new RectInt(Trench.x, Trench.y, Trench.width, 7), 0f, Edge);
            p.RoundRect(new RectInt(Trench.x, Trench.yMax - 7, Trench.width, 7), 0f, Edge);

            // Danger label.
            p.RoundRect(Danger, 10f, Sun);
            p.RoundFrame(new RectInt(Danger.x + 5, Danger.y + 5, 246, 88), 8f, 5f, Night);
            p.Polygon(new[] { new Vector2(Danger.x + 52f, Danger.y + 78f), new Vector2(Danger.x + 18f, Danger.y + 20f), new Vector2(Danger.x + 86f, Danger.y + 20f) }, Night);
            p.Text("!", new RectInt(Danger.x + 40, Danger.y + 24, 24, 40), Sun);
            p.Text("DANGER\nHIGH VOLTAGE", new RectInt(Danger.x + 96, Danger.y + 14, 150, 70), Night);

            // Plaques: storage and assembly doors (arrows to the doors), two warnings.
            string[] plaques = { "STORAGE AREA", "ASSEMBLY FLOOR", "AUTHORISED\nUNITS ONLY", "DO NOT\nUNPLUG" };
            for (int i = 0; i < 4; i++)
            {
                RectInt r = Plaque(i);
                p.RoundRect(new RectInt(r.x + 4, r.y + 4, r.width - 8, r.height - 8), 18f, Hex("1A1426"));
                p.RoundFrame(new RectInt(r.x + 8, r.y + 8, r.width - 16, r.height - 16), 14f, 3f, Blood);
                bool arrow = i < 2;
                p.Text(plaques[i], new RectInt(r.x + 18, r.y + 14, arrow ? 180 : 220, 52), i < 2 ? Ink : Blood);
                if (arrow)
                    p.Polygon(new[] { new Vector2(r.x + 238f, r.y + 40f), new Vector2(r.x + 206f, r.y + 20f), new Vector2(r.x + 206f, r.y + 60f) }, Blood);
            }

            Texture2D atlas = p.Save(AtlasPath);
            _uvMain = p.Uv(MainSign); _uvStatus = p.Uv(StatusScreen); _uvGraph = p.Uv(GraphScreen); _uvCores = p.Uv(CoreScreen);
            _uvVent = p.Uv(VentPanel); _uvLogo = p.Uv(Logo); _uvTrench = p.Uv(Trench); _uvDanger = p.Uv(Danger);
            _uvUnit[0] = p.Uv(UnitVent); _uvUnit[1] = p.Uv(UnitSwitch); _uvUnit[2] = p.Uv(UnitDrives);
            _uvUnit[3] = p.Uv(UnitStorage); _uvUnit[4] = p.Uv(UnitFans);
            for (int i = 0; i < 4; i++)
                _uvPlaque[i] = p.Uv(Plaque(i));
            _signs = Plastic("SignsControl", Color.white, 0.6f, atlas, new Color(0.16f, 0.16f, 0.16f), atlas);
            _screens = Plastic("SignsControlGlow", Color.white, 0.5f, atlas, new Color(1.1f, 1.1f, 1.1f), atlas);
        }

        // The Factory OS emblem: a violet gear of radius r round a dark hub with a small red light.
        static void Emblem(SignPainter p, Vector2 c, float r)
        {
            var teeth = new List<Vector2>();
            for (int i = 0; i < 40; i++)
            {
                float a = i * Mathf.PI * 2f / 40f;
                float radius = (i / 2) % 2 == 0 ? r : r * 0.8f;   // ten teeth, two points each
                teeth.Add(c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius);
            }
            p.Polygon(teeth.ToArray(), Grape);
            p.Circle(c, r * 0.62f, Hex("1A1426"));
            p.Ring(c, r * 0.4f, r * 0.48f, Grape);
            p.Circle(c, r * 0.16f, Blood);
        }

        // One server unit front: a dark faceplate with ear screws and its own detail.
        static void Unit(SignPainter p, RectInt r, int kind)
        {
            p.RoundRect(r, 4f, Panel);
            p.RoundFrame(new RectInt(r.x + 2, r.y + 2, r.width - 4, r.height - 4), 4f, 2f, Edge);
            p.Circle(new Vector2(r.x + 10f, r.center.y), 4f, Edge);
            p.Circle(new Vector2(r.xMax - 10f, r.center.y), 4f, Edge);
            switch (kind)
            {
                case 0:   // vented blank with a status window on the right
                    for (int x = r.x + 26; x < r.x + 390; x += 14)
                        p.RoundRect(new RectInt(x, r.y + 12, 8, r.height - 24), 3f, Slot);
                    p.RoundRect(new RectInt(r.x + 404, r.y + 10, 88, r.height - 20), 4f, Slot);
                    break;
                case 1:   // network switch: two rows of ports
                    for (int row = 0; row < 2; row++)
                        for (int x = r.x + 30; x < r.x + 440; x += 26)
                        {
                            p.RoundRect(new RectInt(x, r.y + 6 + row * 18, 20, 14), 2f, Bay);
                            p.RoundRect(new RectInt(x + 4, r.y + 8 + row * 18, 12, 9), 1f, Slot);
                        }
                    break;
                case 2:   // four drive bays
                    for (int i = 0; i < 4; i++)
                    {
                        int x = r.x + 22 + i * 120;
                        p.RoundRect(new RectInt(x, r.y + 10, 108, r.height - 20), 5f, Bay);
                        for (int y = r.y + 40; y < r.yMax - 16; y += 8)
                            p.RoundRect(new RectInt(x + 8, y, 92, 3), 1f, Slot);
                        p.RoundRect(new RectInt(x + 8, r.y + 16, 60, 10), 3f, Slot);
                    }
                    break;
                case 3:   // storage: three rows of six bays
                    for (int row = 0; row < 3; row++)
                        for (int i = 0; i < 6; i++)
                        {
                            int x = r.x + 24 + i * 78, y = r.y + 12 + row * 60;
                            p.RoundRect(new RectInt(x, y, 70, 54), 4f, Bay);
                            p.RoundRect(new RectInt(x + 6, y + 6, 40, 6), 2f, Slot);
                            for (int s = y + 20; s < y + 50; s += 7)
                                p.RoundRect(new RectInt(x + 6, s, 58, 3), 1f, Slot);
                        }
                    break;
                default:  // rear: four fans
                    for (int i = 0; i < 4; i++)
                    {
                        var c = new Vector2(r.x + 70f + i * 124f, r.center.y);
                        p.Circle(c, 56f, Slot);
                        p.Ring(c, 50f, 56f, Bay);
                        for (int b = 0; b < 5; b++)
                        {
                            float a = b * Mathf.PI * 2f / 5f;
                            p.Line(c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 12f, c + new Vector2(Mathf.Cos(a + 0.7f), Mathf.Sin(a + 0.7f)) * 46f, 9f, Bay);
                        }
                        p.Circle(c, 14f, Edge);
                    }
                    break;
            }
        }

        // ---- Doors -------------------------------------------------------------------------

        static void Doors(Transform root)
        {
            Transform doors = Group(root, "Doors");
            Frame(doors, "Door4", new Vector3(10.5f, 0f, 21.16f), alongZ: false, inward: Vector3.forward);
            Frame(doors, "Door3", new Vector3(20.34f, 0f, 31f), alongZ: true, inward: Vector3.left);
            Box(doors, "Door4_Strip", new Vector3(2.4f, 0.08f, 0.04f), _red, new Vector3(10.5f, 4.96f, 21.34f));
            Box(doors, "Door3_Strip", new Vector3(0.04f, 0.08f, 2.4f), _red, new Vector3(20.16f, 4.96f, 31f));
            // The alarm beacons on this side sit just above the new headers.
            Move("Lighting/ControlAlarms/AlarmDoor4/Beacon_B", new Vector3(10.5f, 5.62f, 21.12f));
            Move("Lighting/ControlAlarms/AlarmDoor4/Backplate_B", new Vector3(10.5f, 5.62f, 21.03f));
            Move("Lighting/ControlAlarms/AlarmDoor3/Beacon_A", new Vector3(20.38f, 5.62f, 31f));
            Move("Lighting/ControlAlarms/AlarmDoor3/Backplate_A", new Vector3(20.47f, 5.62f, 31f));
        }

        // ---- Walls: dark cladding and a red strip ------------------------------------------

        static void Walls(Transform root)
        {
            Transform walls = Group(root, "Walls");
            (Vector3 centre, Vector3 size)[] cladding =
            {
                (new Vector3(0.517f, 3.15f, 31f), new Vector3(0.03f, 5.1f, 20f)),
                (new Vector3(10.5f, 3.15f, 40.983f), new Vector3(20f, 5.1f, 0.03f)),
                (new Vector3(4.75f, 3.15f, 21.017f), new Vector3(8.5f, 5.1f, 0.03f)),
                (new Vector3(16.25f, 3.15f, 21.017f), new Vector3(8.5f, 5.1f, 0.03f)),
                (new Vector3(20.483f, 3.15f, 25.25f), new Vector3(0.03f, 5.1f, 8.5f)),
                (new Vector3(20.483f, 3.15f, 36.75f), new Vector3(0.03f, 5.1f, 8.5f)),
            };
            foreach (var (centre, size) in cladding)
            {
                Box(walls, "Cladding", size, _wall, centre);
                // A violet strip just above the skirting, the length of the cladding.
                bool alongX = size.x > size.z;
                Vector3 into = alongX ? (centre.z < 31f ? Vector3.forward : Vector3.back) : (centre.x < 10.5f ? Vector3.right : Vector3.left);
                Box(walls, "Strip", alongX ? new Vector3(size.x, 0.035f, 0.02f) : new Vector3(0.02f, 0.035f, size.z), _violet,
                    new Vector3(centre.x, 0.68f, centre.z) + into * 0.02f);
                // Seams every 2.5 m.
                float length = alongX ? size.x : size.z;
                for (float s = -length * 0.5f + 2.5f; s < length * 0.5f - 0.5f; s += 2.5f)
                    Box(walls, "Seam", alongX ? new Vector3(0.03f, 5.1f, 0.02f) : new Vector3(0.02f, 5.1f, 0.03f), _trim,
                        centre + (alongX ? Vector3.right : Vector3.forward) * s + into * 0.02f);
            }
        }

        // ---- The middle racks ---------------------------------------------------------------

        static void Servers(Transform root, Transform leds)
        {
            Transform servers = Group(root, "Servers");
            Transform floor = Group(root, "Floor");
            var cores = new List<Vector3>();
            for (int i = 1; i <= 3; i++)
            {
                GameObject anchor = GameObject.Find($"Anchor_ch4.core.{i}");
                if (anchor != null)
                    cores.Add(anchor.transform.position);
            }

            Mesh[] rackLeds = RackLeds();
            bool trunkBuilt = false;
            for (int n = 1; n <= 12; n++)
            {
                GameObject rack = GameObject.Find($"Level/Obstacles/Control_Server_{n}");
                if (rack == null)
                    continue;
                // The obstacle keeps its collider; its body becomes the cabinet shell.
                Transform cap = rack.transform.Find("Cap");
                if (cap != null)
                    Object.DestroyImmediate(cap.gameObject);
                rack.GetComponent<MeshFilter>().sharedMesh = RoundBox(new Vector3(1.24f, 2.7f, 1.44f));
                rack.GetComponent<MeshRenderer>().sharedMaterial = _shell;
                var c = new Vector3(rack.transform.position.x, 0f, rack.transform.position.z);
                Transform row = servers.Find($"Row_{c.z:0}") ?? Group(servers, $"Row_{c.z:0}");
                RackDetail(row, c, n);

                // Its status lights blink in four groups.
                Transform lights = Group(leds, $"Rack_{n}", c);
                var renderers = new List<Renderer>();
                Material[] colours = { _red, _red, _violet, M.Amber };
                for (int g = 0; g < rackLeds.Length; g++)
                    renderers.Add(Part(lights, $"Leds_{g}", rackLeds[g], colours[g], c, Quaternion.identity, moving: true).GetComponent<Renderer>());
                lights.gameObject.AddComponent<DressingBlinker>().Configure(renderers.ToArray(), 0.22f, 100 + n);

                // A rack under a core gets a cradle that beams power up to it.
                foreach (Vector3 core in cores)
                    if (Mathf.Abs(core.x - c.x) < 0.2f && Mathf.Abs(core.z - c.z) < 0.2f)
                    {
                        Part(row, "Cradle", Ring(0.2f, 0.34f, 0.05f, 24), _violet, c + Vector3.up * 2.9f, Quaternion.identity);
                        Part(row, "Cradle_Beam", Cylinder(0.05f, 0.42f), _violet, c + Vector3.up * 2.9f, Quaternion.identity);
                        foreach (float a in new[] { 45f, 135f, 225f, 315f })
                        {
                            Vector3 o = Quaternion.Euler(0f, a, 0f) * Vector3.forward * 0.4f;
                            Box(row, "Cradle_Prong", new Vector3(0.05f, 0.28f, 0.05f), _trim, c + o + Vector3.up * 3.04f,
                                rotation: Quaternion.LookRotation(o) * Quaternion.Euler(-20f, 0f, 0f));
                        }
                    }

                // Cables: the corner racks send theirs to the nearer north or south wall, the
                // outer rack of each row pair to its side wall, the inner one to the console.
                float dx = c.x - 10.5f;
                if (Mathf.Abs(dx) > 5.5f)
                {
                    float sz = c.z > 31f ? 1f : -1f;
                    float wallFront = sz > 0f ? 41f - CabinetDepth : 21f + CabinetDepth;
                    Vector3 port = new Vector3(c.x, 0f, c.z + sz * 1.05f);
                    for (int k = 0; k < 4; k++)
                    {
                        float o = -0.27f + 0.18f * k;
                        float y = 0.2f + 0.05f * (k % 2);
                        AddCable(k, new Vector3(c.x + o, y, c.z + sz * 0.68f), new Vector3(c.x + o, y, c.z + sz * 0.95f),
                            new Vector3(c.x + o * 0.6f, 0.2f, c.z + sz * 1.05f), new Vector3(c.x + o * 0.4f, -0.08f, c.z + sz * 1.05f));
                    }
                    Port(floor, port, alongX: true);
                    Channel(floor, port, new Vector3(c.x, 0f, wallFront + sz * -0.2f));
                    Rise(floor, new Vector3(c.x, 0f, wallFront - sz * 0.2f), sz > 0f ? Vector3.forward : Vector3.back);
                }
                else
                {
                    bool outer = Mathf.Abs(dx) > 3f;
                    float sx = outer ? Mathf.Sign(dx) : -Mathf.Sign(dx);
                    Vector3 port = new Vector3(c.x + sx * 1.0f, 0f, c.z);
                    for (int k = 0; k < 4; k++)
                    {
                        float o = -0.27f + 0.18f * k;
                        float y = 0.45f + 0.08f * (k % 2);
                        AddCable(k, new Vector3(c.x + sx * 0.6f, y, c.z + o), new Vector3(c.x + sx * 0.95f, y, c.z + o),
                            new Vector3(c.x + sx * 1.0f, 0.25f, c.z + o * 0.6f), new Vector3(c.x + sx * 1.0f, -0.08f, c.z + o * 0.4f));
                    }
                    Port(floor, port, alongX: false);
                    if (outer)
                    {
                        float wallFront = sx > 0f ? 20.5f - CabinetDepth : 0.5f + CabinetDepth;
                        Channel(floor, port, new Vector3(wallFront - sx * 0.2f, 0f, c.z));
                        Rise(floor, new Vector3(wallFront - sx * 0.2f, 0f, c.z), sx > 0f ? Vector3.right : Vector3.left);
                    }
                    else
                    {
                        Channel(floor, port, new Vector3(10.5f - sx * 0.2f, 0f, c.z));
                        if (!trunkBuilt)
                        {
                            // The trunk to the console, between the two middle rows.
                            Channel(floor, new Vector3(10.5f, 0f, 27.8f), new Vector3(10.5f, 0f, 34.2f));
                            trunkBuilt = true;
                        }
                    }
                }
            }
        }

        // The detail on one rack: plinth, crown, framed fronts of server units, rear fans, vented sides.
        static void RackDetail(Transform parent, Vector3 c, int n)
        {
            Box(parent, "Plinth", new Vector3(1.34f, 0.12f, 1.54f), _black, c + Vector3.up * 0.06f);
            Box(parent, "Crown", new Vector3(1.36f, 0.1f, 1.56f), _trim, c + Vector3.up * 2.85f);
            Box(parent, "Crown_Lip", new Vector3(1.3f, 0.04f, 1.5f), _black, c + Vector3.up * 2.92f);
            foreach (float s in new[] { -1f, 1f })
            {
                Quaternion facing = s > 0f ? FacingSouth : FacingNorth;   // a quad facing +Z or -Z
                float face = 0.73f;
                int[] units = s > 0f ? RackFront : RackRear;
                // Frame round the front.
                foreach (float x in new[] { -0.6f, 0.6f })
                    Box(parent, "Frame_Post", new Vector3(0.08f, 2.62f, 0.05f), _trim, c + new Vector3(x, 1.45f, s * (face + 0.02f)));
                Box(parent, "Frame_Top", new Vector3(1.28f, 0.08f, 0.05f), _trim, c + new Vector3(0f, 2.72f, s * (face + 0.02f)));
                Box(parent, "Frame_Bottom", new Vector3(1.28f, 0.06f, 0.05f), _trim, c + new Vector3(0f, 0.2f, s * (face + 0.02f)));
                Box(parent, "Glow_Strip", new Vector3(1.1f, 0.025f, 0.02f), _red, c + new Vector3(0f, 0.13f, s * 0.78f));
                Box(parent, "Handle", new Vector3(0.03f, 0.5f, 0.04f), M.Chrome, c + new Vector3(s * 0.6f, 1.45f, s * (face + 0.06f)));
                // The units, stacked from the bottom.
                StackUnits(parent, units, 0.28f, 2.66f, 1.04f, c, new Vector3(0f, 0f, s * (face + 0.005f)), facing, null);
                // Vented sides with the logo and a danger label.
                Quaternion side = s > 0f ? FacingEast : FacingWest;
                Vector3 sideFace = c + new Vector3(s * 0.625f, 0f, 0f);
                Part(parent, "Side_Vent", Quad("Control_RackVent", new Vector2(1.3f, 1.7f), _uvVent), _signs, sideFace + Vector3.up * 1.2f, side);
                Part(parent, "Side_Logo", Quad("Control_RackLogo", new Vector2(0.56f, 0.56f), _uvLogo), _signs, sideFace + Vector3.up * 2.4f + side * Vector3.forward * 0.002f, side);
                Part(parent, "Side_Danger", Quad("Control_RackDanger", new Vector2(0.52f, 0.2f), _uvDanger), _signs, sideFace + Vector3.up * 0.5f + side * Vector3.forward * 0.004f, side);
            }
        }

        // Stacks unit quads from y0 to y1 on a face at 'origin + offset' facing 'facing', and adds each
        // unit's status lights (in face-local positions) to 'leds' when given.
        static void StackUnits(Transform parent, int[] sequence, float y0, float y1, float width, Vector3 origin, Vector3 offset, Quaternion facing,
            List<(Vector3 position, Vector3 normal)> leds)
        {
            float y = y0;
            Vector3 right = facing * Vector3.right, normal = facing * Vector3.forward;
            for (int i = 0; ; i++)
            {
                int kind = sequence[i % sequence.Length];
                float h = UnitHeight[kind];
                if (y + h > y1)
                    break;
                Vector3 centre = origin + offset + Vector3.up * (y + h * 0.5f);
                Part(parent, "Unit", Quad($"Control_Unit_{kind}_{width:0.##}", new Vector2(width, h), _uvUnit[kind]), _signs, centre, facing);
                if (leds != null)
                    foreach (Vector2 l in UnitLeds(kind, width))
                        leds.Add((centre + right * l.x + Vector3.up * l.y + normal * 0.004f, normal));
                y += h + 0.02f;
            }
        }

        // Where a unit's status lights are, in the unit's own plane (x across, y up), for a unit 'width' wide.
        static IEnumerable<Vector2> UnitLeds(int kind, float width)
        {
            float k = width / 1.04f;
            switch (kind)
            {
                case 0: yield return new Vector2(0.4f * k, 0f); yield return new Vector2(0.45f * k, 0f); break;
                case 1: for (int i = 0; i < 6; i++) yield return new Vector2((-0.44f + i * 0.08f) * k, 0.03f); break;
                case 2: foreach (float x in new[] { -0.36f, -0.12f, 0.12f, 0.36f }) yield return new Vector2((x + 0.07f) * k, -0.06f); break;
                case 3: for (int i = 0; i < 6; i++) yield return new Vector2((-0.4f + i * 0.157f) * k, -0.15f); break;
                default: yield return new Vector2(0.47f * k, 0.15f); break;
            }
        }

        // The rack status lights, in rack-local space (floor centre), split into four blink groups; shared by all racks.
        static Mesh[] RackLeds()
        {
            var all = new List<(Vector3 position, Vector3 normal)>();
            var scratch = new GameObject("scratch").transform;
            foreach (float s in new[] { -1f, 1f })
                StackUnits(scratch, s > 0f ? RackFront : RackRear, 0.28f, 2.66f, 1.04f, Vector3.zero, new Vector3(0f, 0f, s * 0.735f),
                    s > 0f ? FacingSouth : FacingNorth, all);
            Object.DestroyImmediate(scratch.gameObject);
            return LedGroups("Control_RackLeds", all, 4, 7);
        }

        static Mesh[] LedGroups(string name, List<(Vector3 position, Vector3 normal)> leds, int groups, int seed)
        {
            var random = new System.Random(seed);
            var lists = new List<(Vector3, Vector3)>[groups];
            for (int g = 0; g < groups; g++)
                lists[g] = new List<(Vector3, Vector3)>();
            foreach (var led in leds)
                lists[random.Next(groups)].Add(led);
            var meshes = new Mesh[groups];
            for (int g = 0; g < groups; g++)
            {
                var list = lists[g];
                meshes[g] = Custom($"{name}_{g}", () =>
                {
                    var vertices = new List<Vector3>();
                    var normals = new List<Vector3>();
                    var triangles = new List<int>();
                    const float h = 0.013f;
                    foreach (var (p, n) in list)
                    {
                        Vector3 right = Vector3.Cross(Vector3.up, n).normalized, up = Vector3.Cross(n, right);
                        int b = vertices.Count;
                        vertices.Add(p + right * h - up * h); vertices.Add(p - right * h - up * h);
                        vertices.Add(p - right * h + up * h); vertices.Add(p + right * h + up * h);
                        for (int i = 0; i < 4; i++)
                            normals.Add(n);
                        triangles.AddRange(new[] { b, b + 2, b + 1, b, b + 3, b + 2 });
                    }
                    var mesh = new Mesh { name = $"{name}_{g}" };
                    mesh.SetVertices(vertices);
                    mesh.SetNormals(normals);
                    mesh.SetUVs(0, new List<Vector2>(new Vector2[vertices.Count]));
                    mesh.SetTriangles(triangles, 0);
                    mesh.RecalculateBounds();
                    return mesh;
                });
            }
            return meshes;
        }

        // ---- Floor: ports and channels ------------------------------------------------------

        // A rubber-lipped floor port where a cable bundle goes through the floor.
        static void Port(Transform floor, Vector3 at, bool alongX)
        {
            Vector3 size = alongX ? new Vector3(0.8f, 0.03f, 0.32f) : new Vector3(0.32f, 0.03f, 0.8f);
            Box(floor, "Port", size, _trim, at + Vector3.up * 0.012f);
            Box(floor, "Port_Hole", size - new Vector3(0.08f, -0.004f, 0.08f), _black, at + Vector3.up * 0.014f);
        }

        // A grated, red-lit channel in the floor from a to b (axis-aligned), made of 1 m tiles with steel edges.
        static void Channel(Transform floor, Vector3 a, Vector3 b)
        {
            Vector3 d = b - a;
            float length = d.magnitude;
            if (length < 0.1f)
                return;
            bool alongX = Mathf.Abs(d.x) > Mathf.Abs(d.z);
            Vector3 dir = d / length;
            int tiles = Mathf.Max(1, Mathf.RoundToInt(length));
            float tile = length / tiles;
            Quaternion flat = Quaternion.Euler(-90f, alongX ? 0f : 90f, 0f);
            Mesh quad = Quad($"Control_Channel_{tile:0.##}", new Vector2(tile, 0.4f), _uvTrench);
            for (int i = 0; i < tiles; i++)
                Part(floor, "Channel", quad, _screens, a + dir * (tile * (i + 0.5f)) + Vector3.up * 0.006f, flat);
            Vector3 side = alongX ? Vector3.forward : Vector3.right;
            Vector3 middle = (a + b) * 0.5f + Vector3.up * 0.012f;
            Vector3 rail = alongX ? new Vector3(length, 0.024f, 0.03f) : new Vector3(0.03f, 0.024f, length);
            Box(floor, "Channel_Edge", rail, _trim, middle + side * 0.215f);
            Box(floor, "Channel_Edge", rail, _trim, middle - side * 0.215f);
        }

        // Cables coming up out of the floor at a wall bank and into its plinth; 'toWall' points at the wall.
        static void Rise(Transform floor, Vector3 at, Vector3 toWall)
        {
            Vector3 along = Vector3.Cross(Vector3.up, toWall);
            Port(floor, at, alongX: Mathf.Abs(along.x) > 0.5f);
            for (int k = 0; k < 4; k++)
            {
                float o = -0.27f + 0.18f * k;
                AddCable(k, at + along * o * 0.4f - Vector3.up * 0.08f, at + along * o * 0.5f + Vector3.up * 0.32f,
                    at + toWall * 0.12f + along * o + Vector3.up * 0.2f, at + toWall * 0.32f + along * o + Vector3.up * 0.14f);
            }
        }

        static void AddCable(int k, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            if (k == 2)
                _glowCables.Add((a, b, c, d));
            else
                _cables.Add((a, b, c, d));
        }

        // ---- Cables --------------------------------------------------------------------------

        static void Cables(Transform root)
        {
            Transform cables = Group(root, "Cables");
            Part(cables, "Cables", Tubes("Control_Cables", _cables, 0.035f), _cable, Vector3.zero, Quaternion.identity);
            Part(cables, "Cables_Lit", Tubes("Control_CablesLit", _glowCables, 0.03f), _cableGlow, Vector3.zero, Quaternion.identity);
        }

        // One mesh of round tubes, each along a cubic Bezier curve (8 sides, 10 segments).
        static Mesh Tubes(string name, List<(Vector3 a, Vector3 b, Vector3 c, Vector3 d)> curves, float radius)
        {
            return Custom(name, () =>
            {
                const int sides = 8, segments = 10;
                var vertices = new List<Vector3>();
                var normals = new List<Vector3>();
                var uvs = new List<Vector2>();
                var triangles = new List<int>();
                foreach (var (p0, p1, p2, p3) in curves)
                {
                    Vector3 Point(float t) => (1 - t) * (1 - t) * (1 - t) * p0 + 3 * (1 - t) * (1 - t) * t * p1 + 3 * (1 - t) * t * t * p2 + t * t * t * p3;
                    Vector3 normal = Vector3.zero;
                    int start = vertices.Count;
                    for (int i = 0; i <= segments; i++)
                    {
                        float t = i / (float)segments;
                        Vector3 p = Point(t);
                        Vector3 tangent = (Point(Mathf.Min(1f, t + 0.02f)) - Point(Mathf.Max(0f, t - 0.02f))).normalized;
                        if (i == 0)
                        {
                            normal = Vector3.Cross(tangent, Mathf.Abs(tangent.y) > 0.9f ? Vector3.right : Vector3.up).normalized;
                        }
                        normal = (normal - Vector3.Dot(normal, tangent) * tangent).normalized;
                        Vector3 binormal = Vector3.Cross(tangent, normal);
                        for (int s = 0; s < sides; s++)
                        {
                            float angle = s * Mathf.PI * 2f / sides;
                            Vector3 dir = Mathf.Cos(angle) * normal + Mathf.Sin(angle) * binormal;
                            vertices.Add(p + dir * radius);
                            normals.Add(dir);
                            uvs.Add(new Vector2(s / (float)sides, t));
                        }
                    }
                    for (int i = 0; i < segments; i++)
                        for (int s = 0; s < sides; s++)
                        {
                            int a = start + i * sides + s, b = a + sides;
                            int c = start + i * sides + (s + 1) % sides, d = c + sides;
                            triangles.AddRange(new[] { a, c, b, c, d, b });
                        }
                }
                var mesh = new Mesh { name = name };
                if (vertices.Count > 65000)
                    mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                mesh.SetVertices(vertices);
                mesh.SetNormals(normals);
                mesh.SetUVs(0, uvs);
                mesh.SetTriangles(triangles, 0);
                mesh.RecalculateBounds();
                return mesh;
            });
        }

        // ---- Wall banks: server cabinets built into the walls -------------------------------

        // Each bank: its centre on the wall face, the facing into the room, and its cabinet count.
        static readonly (string name, Vector3 centre, Quaternion facing, int cabinets)[] BankList =
        {
            ("West", new Vector3(0.5f, 0f, 31f), FacingEast, 10),
            ("EastSouth", new Vector3(20.5f, 0f, 25.6f), FacingWest, 5),
            ("EastNorth", new Vector3(20.5f, 0f, 36.4f), FacingWest, 5),
            ("SouthWest", new Vector3(4f, 0f, 21f), FacingSouth, 5),
            ("SouthEast", new Vector3(17f, 0f, 21f), FacingSouth, 5),
            ("NorthWest", new Vector3(4f, 0f, 41f), FacingNorth, 5),
            ("NorthEast", new Vector3(17f, 0f, 41f), FacingNorth, 5),
        };

        static void Banks(Transform root, Transform leds)
        {
            Transform banks = Group(root, "Banks");
            for (int b = 0; b < BankList.Length; b++)
            {
                var (name, centre, facing, count) = BankList[b];
                var lights = new List<(Vector3, Vector3)>();
                Bank(Group(banks, name), centre, facing, count, b, lights);
                Transform group = Group(leds, $"Bank_{name}", centre);
                Mesh[] meshes = LedGroups($"Control_BankLeds_{name}", lights, 6, 20 + b);
                Material[] colours = { _red, _red, _red, _violet, _violet, M.Amber };
                var renderers = new List<Renderer>();
                for (int g = 0; g < meshes.Length; g++)
                    renderers.Add(Part(group, $"Leds_{g}", meshes[g], colours[g], Vector3.zero, Quaternion.identity, moving: true).GetComponent<Renderer>());
                group.gameObject.AddComponent<DressingBlinker>().Configure(renderers.ToArray(), 0.18f, 200 + b);
            }
        }

        // A bank of cabinets against the wall at 'centre', facing into the room, under a header with a violet strip.
        static void Bank(Transform parent, Vector3 centre, Quaternion facing, int count, int seed, List<(Vector3, Vector3)> lights)
        {
            Vector3 into = facing * Vector3.forward, along = facing * Vector3.right;
            float length = count * CabinetWidth;
            for (int i = 0; i < count; i++)
            {
                float a = (i + 0.5f) * CabinetWidth - length * 0.5f;
                Cabinet(parent, centre + along * a, facing, (i + seed) % 3 == 2, lights);
            }
            Box(parent, "Header", new Vector3(length + 0.1f, 0.4f, CabinetDepth + 0.08f), _shell, centre + into * (CabinetDepth + 0.08f) * 0.5f + Vector3.up * (CabinetHeight + 0.2f), rotation: facing);
            Box(parent, "Header_Strip", new Vector3(length, 0.03f, 0.02f), _violet, centre + into * (CabinetDepth + 0.09f) + Vector3.up * (CabinetHeight + 0.02f), rotation: facing);
            foreach (float s in new[] { -1f, 1f })
                Box(parent, "Pilaster", new Vector3(0.12f, CabinetHeight + 0.4f, CabinetDepth + 0.1f), _trim,
                    centre + along * s * (length * 0.5f + 0.06f) + into * (CabinetDepth + 0.1f) * 0.5f + Vector3.up * (CabinetHeight + 0.4f) * 0.5f, rotation: facing);
            Vector3 size = facing * new Vector3(length + 0.36f, CabinetHeight + 0.4f, CabinetDepth + 0.1f);
            Blocker(parent, "Bank_Solid", centre + into * (CabinetDepth + 0.1f) * 0.5f + Vector3.up * (CabinetHeight + 0.4f) * 0.5f,
                new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z)));
        }

        // One wall cabinet: shell, plinth, frame, and either open server units or a vented door.
        static void Cabinet(Transform parent, Vector3 foot, Quaternion facing, bool door, List<(Vector3, Vector3)> lights)
        {
            Vector3 into = facing * Vector3.forward, along = facing * Vector3.right;
            Box(parent, "Cabinet", new Vector3(CabinetWidth - 0.04f, CabinetHeight, CabinetDepth), _shell, foot + into * CabinetDepth * 0.5f + Vector3.up * CabinetHeight * 0.5f, rotation: facing);
            Box(parent, "Cabinet_Plinth", new Vector3(CabinetWidth - 0.02f, 0.25f, CabinetDepth + 0.02f), _black, foot + into * (CabinetDepth + 0.02f) * 0.5f + Vector3.up * 0.125f, rotation: facing);
            Vector3 front = foot + into * (CabinetDepth + 0.01f);
            foreach (float s in new[] { -1f, 1f })
                Box(parent, "Cabinet_Post", new Vector3(0.07f, CabinetHeight - 0.25f, 0.04f), _trim, front + along * s * (CabinetWidth * 0.5f - 0.06f) + Vector3.up * (0.25f + (CabinetHeight - 0.25f) * 0.5f), rotation: facing);
            Box(parent, "Cabinet_Top", new Vector3(CabinetWidth - 0.08f, 0.07f, 0.04f), _trim, front + Vector3.up * (CabinetHeight - 0.05f), rotation: facing);
            if (door)
            {
                Part(parent, "Door", Quad("Control_CabinetDoor", new Vector2(CabinetWidth - 0.2f, 2.7f), _uvVent), _signs, front + into * 0.008f + Vector3.up * 1.68f, facing);
                Part(parent, "Door_Logo", Quad("Control_CabinetLogo", new Vector2(0.5f, 0.5f), _uvLogo), _signs, front + into * 0.012f + Vector3.up * 2.55f, facing);
                Box(parent, "Door_Handle", new Vector3(0.03f, 0.5f, 0.04f), M.Chrome, front + into * 0.03f + along * (CabinetWidth * 0.5f - 0.16f) + Vector3.up * 1.6f, rotation: facing);
                lights.Add((front + into * 0.014f + along * 0.38f + Vector3.up * 0.42f, into));
                lights.Add((front + into * 0.014f + along * 0.42f + Vector3.up * 0.42f, into));
            }
            else
                StackUnits(parent, CabinetFront, 0.3f, CabinetHeight - 0.12f, CabinetWidth - 0.2f, front, into * 0.006f, facing, lights);
        }

        // ---- Mainframe and screens ------------------------------------------------------------

        static void Mainframe(Transform root, Transform leds)
        {
            Transform frame = Group(root, "Mainframe");
            Quaternion facing = FacingNorth;
            Vector3 into = Vector3.back, along = facing * Vector3.right;
            var foot = new Vector3(10.5f, 0f, 41f);
            const float depth = 0.5f, height = 3.2f, width = 3.6f;
            Box(frame, "Body", new Vector3(width, height, depth), _shell, foot + into * depth * 0.5f + Vector3.up * height * 0.5f, rotation: facing);
            Box(frame, "Plinth", new Vector3(width + 0.04f, 0.25f, depth + 0.04f), _black, foot + into * (depth + 0.04f) * 0.5f + Vector3.up * 0.125f, rotation: facing);
            Box(frame, "Header", new Vector3(width + 0.2f, 0.36f, depth + 0.1f), _trim, foot + into * (depth + 0.1f) * 0.5f + Vector3.up * (height + 0.18f), rotation: facing);
            Box(frame, "Header_Strip", new Vector3(width, 0.03f, 0.02f), _violet, foot + into * (depth + 0.11f) + Vector3.up * (height + 0.0f), rotation: facing);
            Vector3 front = foot + into * (depth + 0.01f);
            var lights = new List<(Vector3, Vector3)>();
            foreach (float s in new[] { -1f, 1f })
            {
                // Side columns: two tape reels over a vented panel.
                Vector3 column = front + along * s * 1.2f;
                foreach (float y in new[] { 2.45f, 1.6f })
                {
                    Part(frame, "Reel_Window", RoundBox(new Vector3(0.9f, 0.78f, 0.02f)), _black, column + Vector3.up * y, facing);
                    Part(frame, "Reel", Ring(0.1f, 0.3f, 0.03f, 28), _trim, column + Vector3.up * y + into * 0.02f, facing * Quaternion.Euler(90f, 0f, 0f));
                    Part(frame, "Reel_Rim", Ring(0.3f, 0.33f, 0.035f, 28), _violet, column + Vector3.up * y + into * 0.02f, facing * Quaternion.Euler(90f, 0f, 0f));
                    Part(frame, "Reel_Hub", Cylinder(0.08f, 0.05f), _trim, column + Vector3.up * y + into * 0.01f, facing * Quaternion.Euler(90f, 0f, 0f));
                }
                Part(frame, "Vent", Quad("Control_MainframeVent", new Vector2(0.9f, 0.8f), _uvVent), _signs, column + into * 0.008f + Vector3.up * 0.75f, facing);
                Box(frame, "Divider", new Vector3(0.06f, height - 0.25f, 0.05f), _trim, front + along * s * 0.6f + Vector3.up * (0.25f + (height - 0.25f) * 0.5f), rotation: facing);
            }
            // Centre column: a red core slot between racks of drives, and a warning.
            Box(frame, "Core_Slot", new Vector3(0.12f, 2.2f, 0.03f), _red, front + into * 0.01f + Vector3.up * 1.95f, rotation: facing);
            StackUnits(frame, new[] { 2, 1, 2 }, 2.3f, 3.1f, 0.5f, front + along * -0.31f, into * 0.006f, facing, lights);
            StackUnits(frame, new[] { 2, 1, 2 }, 2.3f, 3.1f, 0.5f, front + along * 0.31f, into * 0.006f, facing, lights);
            StackUnits(frame, new[] { 2, 0, 2, 1 }, 0.95f, 1.75f, 0.5f, front + along * -0.31f, into * 0.006f, facing, lights);
            StackUnits(frame, new[] { 2, 0, 2, 1 }, 0.95f, 1.75f, 0.5f, front + along * 0.31f, into * 0.006f, facing, lights);
            Part(frame, "Warning", Quad("Control_Plaque_Unplug", new Vector2(1.1f, 0.34f), _uvPlaque[3]), _signs, front + into * 0.012f + Vector3.up * 0.55f, facing);
            Blocker(frame, "Mainframe_Solid", foot + into * (depth + 0.1f) * 0.5f + Vector3.up * (height + 0.36f) * 0.5f, new Vector3(width + 0.2f, height + 0.36f, depth + 0.1f));

            Transform group = Group(leds, "Mainframe", foot);
            Mesh[] meshes = LedGroups("Control_MainframeLeds", lights, 4, 31);
            Material[] colours = { _red, _red, _violet, M.Amber };
            var renderers = new List<Renderer>();
            for (int g = 0; g < meshes.Length; g++)
                renderers.Add(Part(group, $"Leds_{g}", meshes[g], colours[g], Vector3.zero, Quaternion.identity, moving: true).GetComponent<Renderer>());
            group.gameObject.AddComponent<DressingBlinker>().Configure(renderers.ToArray(), 0.15f, 300);
        }

        // The three screens high on the north wall: graphs, the mainframe's status over the mainframe, the cores.
        // They keep their objects (LightingState dims them at the shutdown); only their look changes.
        static void Screens(Transform root)
        {
            Transform mounts = Group(root, "Screens");
            (string name, float x, Vector2 size, Rect uv)[] screens =
            {
                ("Control_Screen_1", 4f, new Vector2(3f, 1.5f), _uvGraph),
                ("Control_Screen_2", 10.5f, new Vector2(3.2f, 1.6f), _uvStatus),
                ("Control_Screen_3", 17f, new Vector2(3f, 1.5f), _uvCores),
            };
            foreach (var (name, x, size, uv) in screens)
            {
                GameObject screen = GameObject.Find(name);
                if (screen == null)
                    continue;
                float y = name.EndsWith("2") ? 4.48f : 4.4f;
                screen.transform.SetPositionAndRotation(new Vector3(x, y, 40.9f), FacingNorth);
                screen.transform.localScale = Vector3.one;
                screen.GetComponent<MeshFilter>().sharedMesh = Quad($"Control_{name}", size, uv);
                screen.GetComponent<MeshRenderer>().sharedMaterial = _screens;
                Box(mounts, "Bezel", new Vector3(size.x + 0.16f, size.y + 0.16f, 0.06f), _black, new Vector3(x, y, 40.94f));
                Box(mounts, "Bezel_Rim", new Vector3(size.x + 0.22f, 0.04f, 0.08f), _trim, new Vector3(x, y - size.y * 0.5f - 0.1f, 40.93f));
            }
        }

        // ---- Signs -------------------------------------------------------------------------

        static void Signs(Transform root)
        {
            Transform signs = Group(root, "Signs");
            // The room name on the west wall, seen from door 3.
            Box(signs, "MainSign_Back", new Vector3(0.05f, 0.56f, 5.8f), _black, new Vector3(0.55f, 4.45f, 31f));
            Part(signs, "MainSign", Quad("Control_Sign_Main", new Vector2(5.6f, 0.44f), _uvMain), _screens, new Vector3(0.58f, 4.45f, 31f), FacingEast);
            Part(signs, "Plaque_Storage", Quad("Control_Plaque_Storage", new Vector2(1.4f, 0.44f), _uvPlaque[0]), _signs, new Vector3(20.45f, 4.2f, 35.2f), FacingWest);
            Part(signs, "Plaque_Assembly", Quad("Control_Plaque_Assembly", new Vector2(1.4f, 0.44f), _uvPlaque[1]), _signs, new Vector3(13.2f, 2.8f, 21.045f), FacingSouth);
            Part(signs, "Plaque_Authorised", Quad("Control_Plaque_Authorised", new Vector2(1.3f, 0.41f), _uvPlaque[2]), _signs, new Vector3(7.8f, 2.8f, 21.045f), FacingSouth);
        }
    }
}
