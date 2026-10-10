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
    /// Builds the Painting Room's final dressing in <c>Env.unity</c> (room 2, x 21 to 41, z 0.5 to
    /// 20.5): the paint tanks refitted (no more drip balls; paint runs, a gauge and a valve on
    /// each, and tank 5, which carries no target, rebuilt as a mixing vat fed by four paint
    /// pipes), door 2 raised to 5 m with framed openings on both of this room's doors, paint
    /// splats and a paw-print trail in place of the round puddles, porthole windows, a
    /// three-colour paint stripe and paint drips down the walls, a colour-wheel mural, signs and
    /// posters, paint tins and drums, a drying rack of freshly painted toys, a cleaning sink and
    /// two extractor fans in the ceiling. It differs from the Assembly Floor on purpose (round
    /// windows, no corner columns, a stripe instead of a rail, paint lines from the ceiling
    /// instead of wall pipes), and the ceiling light panels light the room (no hanging lamps).
    /// Re-running replaces the previous build.
    /// </summary>
    /// <remarks>
    /// The middle of the room is left to the Guard fight: new solid props stand within a metre
    /// of the walls, clear of S2's targets, terminal, switch cage, crates, trap, pickups and
    /// slippery zones, the Guard's and Saboteur C's routes, and the cutscene cameras. The
    /// splats stay on S2's slippery zones (they are what makes those zones read as slippery).
    /// After a build: rebake the NavMesh, lighting, reflection probes and occlusion.
    /// </remarks>
    public static class PaintingRoomDressing
    {
        const string AtlasPath = TextureFolder + "/T_Env_Signs_Painting.png";

        static readonly (Vector3 centre, string band)[] Tanks =
        {
            (new Vector3(24.6f, 0f, 17.5f), "Blue"), (new Vector3(37.4f, 0f, 17.5f), "Red"), (new Vector3(27.4f, 0f, 11.5f), "Yellow"),
            (new Vector3(38.9f, 0f, 5.5f), "Bubble"), (new Vector3(23.1f, 0f, 1.5f), "Yellow")
        };
        static readonly Vector3 RoomCentre = new Vector3(31f, 0f, 10.5f);
        const float TankRadius = 0.8f;   // the tank body's radius between 0.24 and 1.92 m

        static Material _signs, _screens;

        // Sign atlas rectangles (pixels, bottom-left origin, 1024 x 1024).
        static readonly RectInt MainSign = new RectInt(0, 832, 1024, 192);
        static readonly RectInt AssemblyPlaque = new RectInt(0, 704, 512, 128);
        static readonly RectInt StoragePlaque = new RectInt(512, 704, 512, 128);
        static readonly RectInt PosterWet = new RectInt(0, 384, 256, 320);
        static readonly RectInt PosterSwatches = new RectInt(256, 384, 256, 320);
        static readonly RectInt PosterLines = new RectInt(512, 384, 256, 320);
        static readonly RectInt MixerLabel = new RectInt(768, 608, 256, 96);
        static readonly RectInt GaugeFace = new RectInt(800, 416, 160, 160);
        static readonly RectInt ColourWheel = new RectInt(0, 0, 384, 384);
        static readonly RectInt LevelsScreen = new RectInt(384, 64, 640, 288);

        static Rect _uvMain, _uvAssembly, _uvStorage, _uvWet, _uvSwatches, _uvLines, _uvMixer, _uvGauge, _uvWheel, _uvLevels;

        [MenuItem("Tools/Factory Reset/Build Painting Room Dressing")]
        static void RunFromMenu() => Debug.Log(Build());

        /// <summary>Builds (or rebuilds) the dressing in the open Env scene and returns a report.</summary>
        public static string Build()
        {
            GameObject level = GameObject.Find("Level");
            if (level == null)
                throw new System.InvalidOperationException("Open Env.unity first: no Level object.");
            Transform dressing = level.transform.Find("Dressing");

            // What this replaces: the round puddles and an earlier build. The tanks are refitted in place.
            int removed = 0;
            foreach (string path in new[] { "Level/Dressing/PaintPuddles", "Level/Dressing/Painting" })
            {
                GameObject old = GameObject.Find(path);
                if (old == null)
                    continue;
                Object.DestroyImmediate(old);
                removed++;
            }

            Load();
            PaintAtlas();
            Transform root = Group(dressing, "Painting");

            int refitted = RefitTanks(level.transform);
            Floor(root);
            Doors(level.transform, root);
            Walls(root);
            Windows(root);
            Signs(root);
            Tins(root);
            Rack(root);
            Sink(root);
            Ceiling(root);

            int probesMoved = MoveProbesOutOfSolids(root);

            // One renderer per material per section (and per tank) instead of one per part.
            int mergedAway = 0;
            foreach (Transform section in root)
                mergedAway += MergeStatic(section, "Painting_" + section.name);
            for (int i = 1; i <= 5; i++)
            {
                Transform detail = level.transform.Find($"Obstacles/Painting_Tank_{i}/Detail");
                if (detail != null)
                    mergedAway += MergeStatic(detail, $"Painting_Tank{i}");
            }

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(level.scene);

            var report = new StringBuilder();
            report.AppendLine($"Painting Room dressing built: removed {removed} old groups, refitted {refitted} tanks, moved {probesMoved} light probes out of solid props, merged {mergedAway} static parts.");
            foreach (Transform section in root)
                report.AppendLine($"  {section.name}: {section.GetComponentsInChildren<MeshRenderer>(true).Length} renderers, " +
                                  $"{Triangles(section)} triangles, {section.GetComponentsInChildren<BoxCollider>(true).Length} colliders");
            int tankTriangles = 0, tankRenderers = 0;
            for (int i = 1; i <= 5; i++)
            {
                Transform detail = level.transform.Find($"Obstacles/Painting_Tank_{i}/Detail");
                if (detail == null) continue;
                tankTriangles += Triangles(detail);
                tankRenderers += detail.GetComponentsInChildren<MeshRenderer>(true).Length;
            }
            report.AppendLine($"  Tank details: {tankRenderers} renderers, {tankTriangles} triangles");
            report.AppendLine($"  Total: {root.GetComponentsInChildren<MeshRenderer>(true).Length + tankRenderers} renderers, {Triangles(root) + tankTriangles} triangles.");
            return report.ToString();
        }

        // ---- Atlas -------------------------------------------------------------------------

        static void PaintAtlas()
        {
            var p = new SignPainter(1024, Plum);

            // 02 PAINTING ROOM: plum panel, bubble-pink badge.
            p.RoundRect(MainSign, 40f, Plum);
            p.RoundFrame(new RectInt(8, 840, 1008, 176), 34f, 8f, Bubble);
            p.Circle(new Vector2(104f, 928f), 70f, Bubble);
            p.Text("02", new RectInt(54, 888, 100, 80), Ink);
            p.Text("PAINTING ROOM", new RectInt(200, 878, 780, 100), Ink);

            // Door plaques (the arrows point at the door from where each plaque hangs).
            p.RoundRect(AssemblyPlaque, 30f, Mint);
            p.Text("ASSEMBLY FLOOR", new RectInt(24, 728, 380, 80), Plum);
            p.Polygon(new[] { new Vector2(482f, 768f), new Vector2(422f, 728f), new Vector2(422f, 808f) }, Plum);
            p.RoundRect(StoragePlaque, 30f, Orange);
            p.Polygon(new[] { new Vector2(542f, 768f), new Vector2(602f, 728f), new Vector2(602f, 808f) }, Plum);
            p.Text("STORAGE AREA", new RectInt(620, 728, 370, 80), Plum);

            // Poster: WET PAINT! with drips.
            p.RoundRect(PosterWet, 22f, Sun);
            p.RoundRect(new RectInt(10, 640, 236, 54), 14f, Bubble);
            for (int i = 0; i < 5; i++)
            {
                float x = 32f + i * 48f, length = 30f + (i * 37 % 50);
                p.Line(new Vector2(x, 650f), new Vector2(x, 650f - length), 16f, Bubble);
                p.Circle(new Vector2(x, 650f - length), 11f, Bubble);
            }
            p.Text("WET\nPAINT!", new RectInt(24, 430, 208, 130), Plum);

            // Poster: SWATCHES, a grid of the palette.
            p.RoundRect(PosterSwatches, 22f, Ink);
            Color[] swatches = { Tomato, Sun, Mint, Cobalt, Bubble, Orange, Grape, Plum, Grey, Hex("FF8A80"), Hex("FFE082"), Hex("A5F2DC") };
            for (int i = 0; i < 12; i++)
            {
                int col = i % 3, row = i / 3;
                p.RoundRect(new RectInt(282 + col * 70, 560 - row * 46, 58, 38), 8f, swatches[i]);
            }
            p.Text("SWATCHES", new RectInt(276, 620, 216, 60), Plum);
            p.Text("MIX WITH CARE", new RectInt(276, 400, 216, 28), Plum, false);

            // Poster: STAY IN THE LINES! with a paint roller.
            p.RoundRect(PosterLines, 22f, Grape);
            p.RoundRect(new RectInt(560, 560, 160, 56), 24f, Bubble);
            p.Line(new Vector2(640f, 560f), new Vector2(640f, 500f), 10f, Ink);
            p.Line(new Vector2(640f, 500f), new Vector2(600f, 460f), 14f, Plum);
            p.Text("STAY IN\nTHE LINES!", new RectInt(530, 620, 220, 76), Ink);
            p.Text("PAINT CREW ONLY", new RectInt(536, 404, 208, 28), Ink, false);

            // Mixer label and gauge face.
            p.RoundRect(MixerLabel, 22f, Plum);
            p.Text("MIXER", new RectInt(800, 626, 192, 60), Sun);
            Vector2 gc = GaugeFace.center;
            p.Circle(gc, 78f, Plum);
            p.Circle(gc, 66f, Ink);
            for (int i = 0; i <= 8; i++)
            {
                float a = Mathf.Lerp(-120f, 120f, i / 8f) * Mathf.Deg2Rad;
                var dir = new Vector2(Mathf.Sin(a), Mathf.Cos(a));
                p.Line(gc + dir * 46f, gc + dir * 58f, 5f, i > 6 ? Tomato : Plum);
            }
            p.Line(gc, gc + new Vector2(Mathf.Sin(0.7f), Mathf.Cos(0.7f)) * 48f, 6f, Tomato);
            p.Circle(gc, 8f, Plum);

            // Colour wheel mural: six palette segments round a white hub.
            Vector2 wc = ColourWheel.center;
            p.RoundRect(ColourWheel, 40f, Ink);
            Color[] wheel = { Tomato, Orange, Sun, Mint, Cobalt, Grape };
            for (int s = 0; s < 6; s++)
            {
                var wedge = new List<Vector2> { wc };
                for (int k = 0; k <= 12; k++)
                {
                    float a = (s * 60f + k * 5f) * Mathf.Deg2Rad;
                    wedge.Add(wc + new Vector2(Mathf.Sin(a), Mathf.Cos(a)) * 170f);
                }
                p.Polygon(wedge.ToArray(), wheel[s]);
            }
            p.Circle(wc, 62f, Ink);
            p.Ring(wc, 168f, 178f, Plum);
            p.Text("COLOUR", new RectInt((int)wc.x - 52, (int)wc.y - 20, 104, 40), Plum);

            // Paint levels screen: four tanks, four bars.
            p.RoundRect(LevelsScreen, 24f, Plum);
            p.Text("PAINT LEVELS", new RectInt(420, 296, 360, 40), Bubble);
            Color[] levels = { Cobalt, Tomato, Sun, Bubble };
            float[] fill = { 0.8f, 0.45f, 0.65f, 0.3f };
            for (int i = 0; i < 4; i++)
            {
                int x = 430 + i * 145;
                p.RoundRect(new RectInt(x, 90, 100, 190), 14f, Grey);
                p.RoundRect(new RectInt(x, 90, 100, (int)(190 * fill[i])), 14f, levels[i]);
            }

            Texture2D atlas = p.Save(AtlasPath);
            _uvMain = p.Uv(MainSign); _uvAssembly = p.Uv(AssemblyPlaque); _uvStorage = p.Uv(StoragePlaque);
            _uvWet = p.Uv(PosterWet); _uvSwatches = p.Uv(PosterSwatches); _uvLines = p.Uv(PosterLines);
            _uvMixer = p.Uv(MixerLabel); _uvGauge = p.Uv(GaugeFace); _uvWheel = p.Uv(ColourWheel); _uvLevels = p.Uv(LevelsScreen);
            _signs = Plastic("SignsPainting", Color.white, 0.55f, atlas, new Color(0.18f, 0.18f, 0.18f), atlas);
            _screens = Plastic("SignsPaintingGlow", Color.white, 0.55f, atlas, new Color(1.1f, 1.1f, 1.1f), atlas);
        }

        static Material Band(string name) => name switch
        {
            "Blue" => M.Blue, "Red" => M.Red, "Yellow" => M.Yellow, "Bubble" => M.Bubble, _ => M.Mint
        };

        // ---- Tanks -------------------------------------------------------------------------

        // The tanks stay where they are (the Guard's cover, the targets' poles); their round paint
        // drips go, and each gets paint runs, a gauge and a valve facing the middle of the room.
        // Tank 5 carries no target, so it becomes a mixing vat fed by the paint pipes.
        static int RefitTanks(Transform level)
        {
            int refitted = 0;
            for (int i = 0; i < Tanks.Length; i++)
            {
                Transform tank = level.Find($"Obstacles/Painting_Tank_{i + 1}");
                if (tank == null)
                    continue;
                foreach (string old in new[] { "PaintDrip", "Detail" })
                {
                    Transform child = tank.Find(old);
                    if (child != null)
                        Object.DestroyImmediate(child.gameObject);
                }
                Transform detail = Group(tank, "Detail", tank.position);
                Vector3 c = tank.position;
                Vector3 facing = RoomCentre - c;
                facing.y = 0f;
                facing.Normalize();
                Quaternion toward = Quaternion.LookRotation(facing, Vector3.up);
                Material band = Band(Tanks[i].band);

                // Paint runs down the sides, each ending in a drop.
                float[] angles = { -45f, 45f, 120f };   // clear of the front (gauge, label, valve), the sight glass (-90) and the ladder (180)
                float[] lengths = { 0.6f, 0.95f, 0.4f };
                for (int r = 0; r < 3; r++)
                {
                    Vector3 dir = Quaternion.Euler(0f, angles[r], 0f) * facing;
                    Vector3 top = c + dir * (TankRadius + 0.005f) + Vector3.up * 1.92f;
                    Part(detail, "PaintRun", Cylinder(0.045f, lengths[r]), band, top - Vector3.up * lengths[r], Quaternion.identity);
                    Part(detail, "PaintDrop", M.Ball, band, top - Vector3.up * lengths[r] + dir * 0.01f, toward,
                        scale: new Vector3(0.13f, 0.17f, 0.13f));
                }

                // Gauge at chest height, valve and wheel near the foot.
                Vector3 gauge = c + facing * (TankRadius + 0.02f) + Vector3.up * 1.35f;
                Part(detail, "Gauge_Rim", Ring(0.12f, 0.16f, 0.05f, 20), M.Plum, gauge - facing * 0.03f, toward * Quaternion.Euler(90f, 0f, 0f));
                Part(detail, "Gauge", Quad("Gauge", new Vector2(0.26f, 0.26f), _uvGauge), _signs, gauge + facing * 0.015f, toward);
                Vector3 valve = c + facing * TankRadius + Vector3.up * 0.42f;
                Part(detail, "Valve_Pipe", Cylinder(0.06f, 0.24f), M.Chrome, valve - facing * 0.05f, toward * Quaternion.Euler(90f, 0f, 0f));
                Part(detail, "Valve_Wheel", Ring(0.08f, 0.15f, 0.04f, 16), M.Red, valve + facing * 0.2f, toward * Quaternion.Euler(90f, 0f, 0f));

                DetailTank(tank, detail, c, facing, band);

                if (i == 4)
                    MakeMixer(tank, detail, c, facing, toward);
                refitted++;
            }
            return refitted;
        }

        // The tank as a more detailed machine: a footed base ring in place of the plain plinth,
        // two steel hoops, a sight glass on the side showing the paint level, and a ladder up the
        // back to the lid. Size and collider are unchanged (the Guard's cover, the targets' poles).
        static void DetailTank(Transform tank, Transform detail, Vector3 c, Vector3 facing, Material band)
        {
            Transform plinth = tank.Find("Plinth");
            if (plinth != null)
                plinth.gameObject.SetActive(false);
            Part(detail, "Base", Lathe("TankBase", new[]
            {
                new Vector2(0f, 0f), new Vector2(0.92f, 0f), new Vector2(0.95f, 0.04f), new Vector2(0.95f, 0.12f),
                new Vector2(0.86f, 0.16f), new Vector2(0f, 0.16f)
            }, 28), M.Grey, c, Quaternion.identity);
            for (int f = 0; f < 4; f++)
            {
                Vector3 dir = Quaternion.Euler(0f, 45f + f * 90f, 0f) * facing;
                Quaternion along = Quaternion.LookRotation(dir, Vector3.up);
                Box(detail, "Foot", new Vector3(0.26f, 0.1f, 0.34f), M.Grey, c + dir * 0.9f + Vector3.up * 0.05f, rotation: along);
                Part(detail, "Foot_Bolt", Cylinder(0.04f, 0.03f), M.Chrome, c + dir * 0.98f + Vector3.up * 0.1f, Quaternion.identity);
            }
            foreach (float y in new[] { 0.28f, 1.8f })
                Part(detail, "Hoop", Cylinder(0.835f, 0.08f, 28), M.Grey, c + Vector3.up * y, Quaternion.identity);

            // Sight glass on the side: a grey frame, a dark glass, and the paint level inside it.
            Vector3 side = Quaternion.Euler(0f, -90f, 0f) * facing;
            Quaternion sideways = Quaternion.LookRotation(side, Vector3.up);
            Box(detail, "SightGlass_Frame", new Vector3(0.2f, 1.15f, 0.08f), M.Grey, c + side * 0.81f + Vector3.up * 1.0f, rotation: sideways);
            Box(detail, "SightGlass", new Vector3(0.11f, 1.0f, 0.03f), M.Plum, c + side * 0.855f + Vector3.up * 1.0f, rotation: sideways);
            Box(detail, "SightGlass_Paint", new Vector3(0.11f, 0.55f, 0.035f), band, c + side * 0.86f + Vector3.up * 0.775f, rotation: sideways);

            // Ladder up the back.
            Vector3 back = -facing, across = Vector3.Cross(Vector3.up, back);
            Quaternion facingBack = Quaternion.LookRotation(back, Vector3.up);
            foreach (float offset in new[] { -0.18f, 0.18f })
            {
                Part(detail, "Ladder_Rail", Cylinder(0.025f, 2.2f, 8), M.Chrome, c + back * 0.9f + across * offset + Vector3.up * 0.16f, Quaternion.identity);
                foreach (float y in new[] { 0.6f, 2.0f })
                    Box(detail, "Ladder_Standoff", new Vector3(0.04f, 0.04f, 0.12f), M.Chrome, c + back * 0.85f + across * offset + Vector3.up * y, rotation: facingBack);
            }
            for (int r = 0; r < 7; r++)
                Box(detail, "Ladder_Rung", new Vector3(0.36f, 0.035f, 0.035f), M.Chrome, c + back * 0.9f + Vector3.up * (0.45f + r * 0.3f), rotation: facingBack);
        }

        static void MakeMixer(Transform tank, Transform detail, Vector3 c, Vector3 facing, Quaternion toward)
        {
            Transform bar = tank.Find("Lid/LidBar") ?? tank.Find("LidBar");
            if (bar != null)
                bar.gameObject.SetActive(false);
            // A bridge across the lid carrying the stirrer's motor (the lid itself turns underneath).
            Box(detail, "Mixer_Bridge", new Vector3(1.5f, 0.1f, 0.18f), M.Grey, c + Vector3.up * 2.42f, rotation: toward * Quaternion.Euler(0f, 90f, 0f));
            Box(detail, "Mixer_Motor", new Vector3(0.5f, 0.38f, 0.5f), M.Blue, c + Vector3.up * 2.66f);
            Part(detail, "Mixer_Cap", Cylinder(0.14f, 0.1f), M.Chrome, c + Vector3.up * 2.85f, Quaternion.identity);
            Part(detail, "Mixer_Label", Quad("Sign_Mixer", new Vector2(0.8f, 0.3f), _uvMixer), _signs,
                c + facing * (TankRadius + 0.02f) + Vector3.up * 1.0f, toward);
        }

        // ---- Floor: paint splats and a paw-print trail -------------------------------------

        static void Floor(Transform root)
        {
            Transform floor = Group(root, "Floor");

            // Splats where the round puddles were, on S2's three slippery zones.
            (Vector3 at, float size, Material paint)[] splats =
            {
                (new Vector3(28.9f, 0f, 15.6f), 2.5f, M.Blue), (new Vector3(30.4f, 0f, 15.2f), 1.9f, M.Yellow),
                (new Vector3(34.7f, 0f, 11.7f), 1.8f, M.Red), (new Vector3(36.1f, 0f, 11.2f), 2.6f, M.Purple),
                (new Vector3(24.5f, 0f, 7.6f), 2.5f, M.Mint), (new Vector3(26.0f, 0f, 7.2f), 1.9f, M.Blue),
                (new Vector3(24.8f, 0f, 5.4f), 2.2f, M.Yellow), (new Vector3(26.2f, 0f, 5.8f), 2.6f, M.Bubble)
            };
            for (int i = 0; i < splats.Length; i++)
            {
                var s = splats[i];
                float lift = 0.004f + i * 0.0015f;   // overlapping splats never fight for the same depth
                Part(floor, "Splat", SplatMesh(i % 3), s.paint, s.at + Vector3.up * lift, Quaternion.Euler(0f, i * 67f, 0f),
                    scale: new Vector3(s.size * 0.5f, 1f, s.size * 0.5f));
                var random = new System.Random(31 + i);
                for (int d = 0; d < 4; d++)
                {
                    float a = (float)random.NextDouble() * Mathf.PI * 2f, r = s.size * (0.55f + 0.2f * (float)random.NextDouble());
                    Part(floor, "Droplet", Ring(0.01f, 0.07f, 0.006f, 12), s.paint,
                        s.at + new Vector3(Mathf.Cos(a) * r, lift, Mathf.Sin(a) * r), Quaternion.identity,
                        scale: Vector3.one * (0.7f + (float)random.NextDouble()));
                }
            }

            // A paint-pawed toy walked out of the red splat towards the drying rack.
            for (int step = 0; step < 6; step++)
            {
                var at = new Vector3(35.6f + step * 0.65f, 0.004f, 12.3f + step * 0.32f + (step % 2 == 0 ? 0.12f : -0.12f));
                Quaternion heading = Quaternion.Euler(0f, 64f, 0f);
                float fade = 1f - step * 0.1f;
                Part(floor, "Paw_Pad", Ring(0.01f, 0.09f, 0.006f, 16), M.Red, at, heading, scale: new Vector3(fade, 1f, fade * 0.85f));
                for (int t = 0; t < 4; t++)
                {
                    Vector3 toe = heading * new Vector3(-0.09f + t * 0.06f, 0f, 0.11f + (t == 1 || t == 2 ? 0.03f : 0f));
                    Part(floor, "Paw_Toe", Ring(0.01f, 0.032f, 0.006f, 10), M.Red, at + toe * fade, Quaternion.identity, scale: Vector3.one * fade);
                }
            }
        }

        // A flat paint splat of radius about 1: a wobbly outline with a few lobes, rounded at the rim.
        static Mesh SplatMesh(int variant)
        {
            return Custom($"Splat_{variant}", () =>
            {
                const int points = 48;
                const float height = 0.008f;
                var random = new System.Random(7 + variant * 13);
                float p1 = (float)random.NextDouble() * 6.28f, p2 = (float)random.NextDouble() * 6.28f;
                int lobes = 5 + variant;
                var vertices = new List<Vector3> { new Vector3(0f, height, 0f) };
                var normals = new List<Vector3> { Vector3.up };
                var triangles = new List<int>();
                for (int i = 0; i < points; i++)
                {
                    float a = i * Mathf.PI * 2f / points;
                    float r = 0.82f + 0.12f * Mathf.Sin(3f * a + p1) + 0.07f * Mathf.Sin(lobes * a + p2)
                              + (i % (points / lobes) == 0 ? 0.14f : 0f);
                    var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    vertices.Add(dir * (r - 0.02f) + Vector3.up * height);   // top rim
                    normals.Add(Vector3.up);
                    vertices.Add(dir * r);                                   // floor rim
                    normals.Add((dir + Vector3.up).normalized);
                }
                for (int i = 0; i < points; i++)
                {
                    int top = 1 + i * 2, next = 1 + ((i + 1) % points) * 2;
                    triangles.Add(0); triangles.Add(next); triangles.Add(top);                 // top fan (clockwise from above)
                    triangles.Add(top); triangles.Add(next); triangles.Add(top + 1);           // rounded rim
                    triangles.Add(top + 1); triangles.Add(next); triangles.Add(next + 1);
                }
                var mesh = new Mesh { name = $"Splat_{variant}" };
                mesh.SetVertices(vertices);
                mesh.SetNormals(normals);
                var uvs = new List<Vector2>();
                foreach (Vector3 v in vertices)
                    uvs.Add(new Vector2(v.x, v.z));
                mesh.SetUVs(0, uvs);
                mesh.SetTriangles(triangles, 0);
                mesh.RecalculateBounds();
                mesh.RecalculateTangents();
                return mesh;
            });
        }

        // ---- Doors -------------------------------------------------------------------------

        static void Doors(Transform level, Transform root)
        {
            Transform doors = Group(root, "Doors");
            // Door 2 (north wall, to Storage, open): taller opening, a white lintel at 5-6 m and a frame on this side.
            Lintel(level, "Walls/Lintel_Door2", new Vector3(3f, 1f, 0.5f), new Vector3(31f, 5.5f, 20.75f));
            Frame(doors, "Door2", new Vector3(31f, 0f, 20.34f), alongZ: false, inward: Vector3.back);
            Box(doors, "Door2_OpenStrip", new Vector3(2.4f, 0.12f, 0.04f), M.Glow, new Vector3(31f, 5.2f, 20.16f));
            // Door 1 (west wall, from the Assembly Floor): its frame on the Painting side.
            Frame(doors, "Door1", new Vector3(21.16f, 0f, 10.5f), alongZ: true, inward: Vector3.right);
            Box(doors, "Door1_OpenStrip", new Vector3(0.04f, 0.12f, 2.4f), M.Glow, new Vector3(21.34f, 5.2f, 10.5f));
        }

        // ---- Walls -------------------------------------------------------------------------

        static void Walls(Transform root)
        {
            Transform walls = Group(root, "Walls");

            // A three-colour paint stripe round the room (pink, yellow, mint), broken at the doors.
            (float y, Material material)[] stripes = { (1.49f, M.Bubble), (1.58f, M.Yellow), (1.67f, M.Mint) };
            foreach (var stripe in stripes)
            {
                Box(walls, "Stripe_S", new Vector3(20f, 0.07f, 0.04f), stripe.material, new Vector3(31f, stripe.y, 0.52f));
                Box(walls, "Stripe_E", new Vector3(0.04f, 0.07f, 20f), stripe.material, new Vector3(40.98f, stripe.y, 10.5f));
                Box(walls, "Stripe_W1", new Vector3(0.04f, 0.07f, 8.1f), stripe.material, new Vector3(21.02f, stripe.y, 4.55f));
                Box(walls, "Stripe_W2", new Vector3(0.04f, 0.07f, 7.9f), stripe.material, new Vector3(21.02f, stripe.y, 16.45f));
                Box(walls, "Stripe_N1", new Vector3(8.1f, 0.07f, 0.04f), stripe.material, new Vector3(25.05f, stripe.y, 20.48f));
                Box(walls, "Stripe_N2", new Vector3(7.9f, 0.07f, 0.04f), stripe.material, new Vector3(36.95f, stripe.y, 20.48f));
            }

            // Paint drips running down from the top band, each ending in a drop (clear of the
            // signs, posters, windows and gears).
            (Vector3 top, Vector3 alongWall, Vector3 outward, float length, Material material)[] drips =
            {
                (new Vector3(25.2f, 5.7f, 0.5f), Vector3.right, Vector3.forward, 1.2f, M.Bubble),
                (new Vector3(29.3f, 5.7f, 0.5f), Vector3.right, Vector3.forward, 0.7f, M.Mint),
                (new Vector3(33.2f, 5.7f, 0.5f), Vector3.right, Vector3.forward, 1.5f, M.Yellow),
                (new Vector3(38.0f, 5.7f, 0.5f), Vector3.right, Vector3.forward, 0.9f, M.Blue),
                (new Vector3(41f, 5.7f, 5.0f), Vector3.forward, Vector3.left, 1.4f, M.Purple),
                (new Vector3(41f, 5.7f, 7.8f), Vector3.forward, Vector3.left, 0.8f, M.Bubble),
                (new Vector3(41f, 5.7f, 19.3f), Vector3.forward, Vector3.left, 1.1f, M.Yellow),
                (new Vector3(26.0f, 5.7f, 20.5f), Vector3.right, Vector3.back, 1.3f, M.Mint),
                (new Vector3(36.8f, 5.7f, 20.5f), Vector3.right, Vector3.back, 0.9f, M.Red),
                (new Vector3(21f, 5.7f, 2.2f), Vector3.forward, Vector3.right, 1.0f, M.Blue),
                (new Vector3(21f, 5.7f, 7.9f), Vector3.forward, Vector3.right, 1.2f, M.Yellow),
                (new Vector3(21f, 5.7f, 19.5f), Vector3.forward, Vector3.right, 0.8f, M.Bubble),
            };
            foreach (var drip in drips)
            {
                Quaternion onWall = Quaternion.LookRotation(drip.outward, Vector3.up);
                Vector3 centre = drip.top + drip.outward * 0.03f - Vector3.up * drip.length * 0.5f;
                Box(walls, "Drip", new Vector3(0.11f, drip.length, 0.03f), drip.material, centre, rotation: onWall);
                Part(walls, "Drip_Drop", M.Ball, drip.material, drip.top + drip.outward * 0.035f - Vector3.up * drip.length,
                    onWall, scale: new Vector3(0.16f, 0.2f, 0.06f));
            }

            // Four paint lines come straight down from a manifold in the ceiling into the mixing
            // vat (tank 5), each with a shut-off wheel, clamped to the wall.
            Transform pipes = Group(walls, "PaintLines");
            (Material material, float x)[] lines = { (M.Bubble, 22.7f), (M.Yellow, 22.95f), (M.Blue, 23.2f), (M.Mint, 23.45f) };
            const float wallZ = 0.75f, vatY = 2.75f;
            Box(pipes, "Manifold", new Vector3(1.1f, 0.3f, 0.36f), M.Grey, new Vector3(23.07f, 5.8f, wallZ));
            foreach (var line in lines)
            {
                Part(pipes, "Line_Down", Cylinder(0.065f, 5.65f - vatY), line.material, new Vector3(line.x, vatY, wallZ), Quaternion.identity);
                Part(pipes, "Line_Into_Vat", Cylinder(0.065f, 0.4f), line.material, new Vector3(line.x, vatY, wallZ), AlongZ);
                Part(pipes, "Joint", M.Ball, M.Chrome, new Vector3(line.x, vatY, wallZ), Quaternion.identity, scale: Vector3.one * 0.18f);
                Part(pipes, "Nozzle", Cylinder(0.08f, 0.08f), M.Chrome, new Vector3(line.x, vatY, wallZ + 0.4f), AlongZ);
                Part(pipes, "ShutOff", Ring(0.05f, 0.1f, 0.03f, 16), M.Red, new Vector3(line.x, 4.2f, wallZ + 0.07f), AlongZ);
            }
            foreach (float y in new[] { 3.5f, 5.0f })
                Box(pipes, "Clamp", new Vector3(1.0f, 0.08f, 0.3f), M.Grey, new Vector3(23.07f, y, 0.66f));
        }

        // ---- Windows -----------------------------------------------------------------------

        static void Windows(Transform root)
        {
            Transform windows = Group(root, "Windows");
            // Round portholes, unlike the Assembly Floor's square windows.
            foreach (float x in new[] { 27.1f, 35.6f })
                Porthole(windows, new Vector3(x, 3.4f, 0.5f), FacingSouth, 0.75f);
            foreach (float z in new[] { 2.6f, 10.5f, 16.2f })
                Porthole(windows, new Vector3(41f, 3.4f, z), FacingWest, 0.75f);
        }

        // ---- Signs and posters -------------------------------------------------------------

        static void Signs(Transform root)
        {
            Transform signs = Group(root, "Signs");

            // The room name high on the east wall, facing the door in from the Assembly Floor.
            Box(signs, "MainSign_Back", new Vector3(0.06f, 1.22f, 5.8f), M.Plum, new Vector3(40.97f, 5.0f, 15.4f));
            Part(signs, "MainSign", Quad("Sign_Painting_Main", new Vector2(5.6f, 1.05f), _uvMain), _signs, new Vector3(40.935f, 5.0f, 15.4f), FacingWest);

            // Door plaques.
            Part(signs, "AssemblyPlaque", Quad("Sign_ToAssembly", new Vector2(2f, 0.5f), _uvAssembly), _signs, new Vector3(21.02f, 3.3f, 7.45f), FacingEast);
            Part(signs, "StoragePlaque", Quad("Sign_ToStorage", new Vector2(2f, 0.5f), _uvStorage), _signs, new Vector3(34.3f, 3.3f, 20.48f), FacingNorth);

            // The colour-wheel mural on the west wall, south of the door.
            Part(signs, "ColourWheel", Quad("Mural_ColourWheel", new Vector2(2.8f, 2.8f), _uvWheel), _signs, new Vector3(21.02f, 3.7f, 4.6f), FacingEast);

            // Posters.
            Part(signs, "Poster_Wet", Quad("Poster_Wet", new Vector2(1.2f, 1.5f), _uvWet), _signs, new Vector3(22.4f, 2.95f, 20.48f), FacingNorth);
            Part(signs, "Poster_Swatches", Quad("Poster_Swatches", new Vector2(1.2f, 1.5f), _uvSwatches), _signs, new Vector3(27.8f, 2.95f, 20.48f), FacingNorth);
            Part(signs, "Poster_Lines", Quad("Poster_Lines", new Vector2(1.2f, 1.5f), _uvLines), _signs, new Vector3(21.02f, 2.95f, 13.8f), FacingEast);

            // Paint levels screen on the north wall, east of door 2.
            Box(signs, "Levels_Back", new Vector3(2.1f, 1.0f, 0.06f), M.Plum, new Vector3(39.1f, 3.3f, 20.47f));
            Part(signs, "LevelsScreen", Quad("Screen_Levels", new Vector2(2.0f, 0.9f), _uvLevels), _screens, new Vector3(39.1f, 3.3f, 20.435f), FacingNorth);
        }

        // ---- Floor props -------------------------------------------------------------------

        // Stacked paint tins and three drums along the south wall, under the windows.
        static void Tins(Transform root)
        {
            Transform tins = Group(root, "PaintTins");
            Material[] colours = { M.Bubble, M.Yellow, M.Blue, M.Mint, M.Red, M.Purple, M.Orange };
            Blocker(tins, "Solid_Tins", new Vector3(27f, 0.55f, 0.92f), new Vector3(3.0f, 1.1f, 0.74f));
            // Three layers, each shifted half a tin: 6 x 2, then 4, then 2.
            int n = 0;
            (int count, int rows, float x0, float z0)[] layers = { (12, 2, 25.75f, 0.74f), (4, 1, 25.94f, 0.92f), (2, 1, 26.13f, 0.92f) };
            for (int layer = 0; layer < layers.Length; layer++)
                for (int i = 0; i < layers[layer].count; i++)
                {
                    int column = i / layers[layer].rows, row = i % layers[layer].rows;
                    var at = new Vector3(layers[layer].x0 + column * 0.38f, layer * 0.34f, layers[layer].z0 + row * 0.36f);
                    Material colour = colours[n++ % colours.Length];
                    Part(tins, "Tin", Cylinder(0.17f, 0.34f, 12), M.White, at, Quaternion.identity);
                    Part(tins, "Tin_Band", Cylinder(0.175f, 0.12f, 12), colour, at + Vector3.up * 0.11f, Quaternion.identity);
                    Part(tins, "Tin_Lid", Ring(0.04f, 0.16f, 0.025f, 14), colour, at + Vector3.up * 0.34f, Quaternion.identity);
                }

            Blocker(tins, "Solid_Drums", new Vector3(35.3f, 0.48f, 0.92f), new Vector3(2.4f, 0.96f, 0.74f));
            Material[] drums = { M.Bubble, M.Mint, M.Yellow };
            for (int i = 0; i < 3; i++)
            {
                var at = new Vector3(34.5f + i * 0.78f, 0f, 0.92f);
                Part(tins, "Drum", Cylinder(0.32f, 0.9f), drums[i], at, Quaternion.identity);
                Part(tins, "Drum_Hoop", Cylinder(0.33f, 0.06f), M.Grey, at + Vector3.up * 0.28f, Quaternion.identity);
                Part(tins, "Drum_Hoop", Cylinder(0.33f, 0.06f), M.Grey, at + Vector3.up * 0.62f, Quaternion.identity);
                Part(tins, "Drum_Bung", Cylinder(0.05f, 0.04f), M.Chrome, at + new Vector3(0.14f, 0.9f, 0f), Quaternion.identity);
            }
        }

        // A drying rack of freshly painted toys on the east wall, north of the switch.
        static void Rack(Transform root)
        {
            Transform rack = Group(root, "DryingRack");
            const float x = 40.65f, z = 14.1f;
            Blocker(rack, "Solid", new Vector3(x, 1.0f, z), new Vector3(0.7f, 2.0f, 3.0f));
            foreach (float side in new[] { -1.47f, 1.47f })
                Box(rack, "Post", new Vector3(0.7f, 2.0f, 0.06f), M.Grey, new Vector3(x, 1.0f, z + side));
            foreach (float y in new[] { 0.6f, 1.2f, 1.8f })
                Box(rack, "Shelf", new Vector3(0.7f, 0.04f, 3.0f), M.White, new Vector3(x, y, z));
            string[] kinds = { "Teddy", "Robot", "Duck" };
            Material[] paints = { M.Bubble, M.Mint, M.Blue, M.Yellow, M.Purple, M.Red, M.Orange, M.Bubble, M.Mint };
            for (int shelf = 0; shelf < 3; shelf++)
                for (int i = 0; i < 3; i++)
                {
                    int k = shelf * 3 + i;
                    float lift = kinds[k % 3] == "Robot" ? 0.065f : kinds[k % 3] == "Teddy" ? 0.09f : 0.05f;
                    Toy(rack, kinds[k % 3], new Vector3(x - 0.05f, 0.62f + shelf * 0.6f + lift, z - 1f + i * 1f), 0.8f,
                        Quaternion.Euler(0f, -90f + (k * 23 % 40) - 20f, 0f), paint: paints[k]);
                }
        }

        // A cleaning sink with brushes on the west wall, north of the door, and spray guns above it.
        // Kept north of z 16 so SaboteurDoorSceneTests' gap through this wall at z 15-16 stays open.
        static void Sink(Transform root)
        {
            Transform sink = Group(root, "Sink");
            const float x = 21.36f, z = 17.4f;   // clear of z 15-16: SaboteurDoorSceneTests opens a gap beside door 1 there
            Blocker(sink, "Solid", new Vector3(x, 0.5f, z), new Vector3(0.72f, 1.0f, 1.8f));
            Box(sink, "Cabinet", new Vector3(0.7f, 0.86f, 1.8f), M.Blue, new Vector3(x, 0.43f, z));
            Box(sink, "Top", new Vector3(0.74f, 0.08f, 1.84f), M.White, new Vector3(x, 0.9f, z));
            Box(sink, "Basin", new Vector3(0.46f, 0.04f, 0.7f), M.Grey, new Vector3(x + 0.04f, 0.95f, z - 0.35f));
            Part(sink, "Tap", Cylinder(0.03f, 0.3f), M.Chrome, new Vector3(21.1f, 0.94f, z - 0.35f), Quaternion.identity);
            Box(sink, "Tap_Spout", new Vector3(0.22f, 0.04f, 0.04f), M.Chrome, new Vector3(21.2f, 1.22f, z - 0.35f));
            Part(sink, "Brush_Pot", Cylinder(0.1f, 0.2f), M.Orange, new Vector3(x, 0.94f, z + 0.4f), Quaternion.identity);
            Material[] tips = { M.Bubble, M.Mint, M.Yellow, M.Blue };
            for (int i = 0; i < 4; i++)
            {
                float a = i * Mathf.PI / 2f;
                var at = new Vector3(x + Mathf.Cos(a) * 0.04f, 1.0f, z + 0.4f + Mathf.Sin(a) * 0.04f);
                Quaternion lean = Quaternion.Euler(Mathf.Sin(a) * 10f, 0f, -Mathf.Cos(a) * 10f);
                Part(sink, "Brush", Cylinder(0.012f, 0.3f), M.Orange, at, lean);
                Part(sink, "Brush_Tip", Cylinder(0.025f, 0.07f), tips[i], at + lean * Vector3.up * 0.3f, lean);
            }
            // Spray guns hanging on hooks above the sink.
            for (int i = 0; i < 3; i++)
            {
                var hook = new Vector3(21.06f, 2.2f, z - 0.6f + i * 0.6f);
                Box(sink, "Hook", new Vector3(0.12f, 0.03f, 0.03f), M.Chrome, hook);
                Box(sink, "SprayGun_Body", new Vector3(0.1f, 0.12f, 0.26f), i == 1 ? M.Mint : M.Bubble, hook + new Vector3(0.08f, -0.12f, 0f));
                Box(sink, "SprayGun_Grip", new Vector3(0.07f, 0.16f, 0.06f), M.Plum, hook + new Vector3(0.08f, -0.24f, -0.06f));
                Part(sink, "SprayGun_Can", Cylinder(0.05f, 0.12f), M.Chrome, hook + new Vector3(0.08f, -0.06f, 0.06f), Quaternion.identity);
            }
        }

        // ---- Ceiling: extractor fans -------------------------------------------------------

        // Two extractor fans set into the ceiling between the light panels (x 23.3-25.7,
        // 29.8-32.2, 36.3-38.7 and z 3.6-4.4, 10.1-10.9, 16.6-17.4), drawing off paint fumes.
        static void Ceiling(Transform root)
        {
            Transform ceiling = Group(root, "Ceiling");
            foreach (Vector2 at in new[] { new Vector2(27.75f, 13.75f), new Vector2(34.25f, 7.25f) })
            {
                var centre = new Vector3(at.x, 5.88f, at.y);
                Part(ceiling, "Extractor_Housing", Ring(0.62f, 0.78f, 0.12f, 32), M.Grey, centre, Quaternion.identity);
                Part(ceiling, "Extractor_Back", Ring(0.01f, 0.64f, 0.02f, 32), M.Plum, centre + Vector3.up * 0.1f, Quaternion.identity);
                Transform rotor = Group(ceiling, "Extractor_Rotor", centre + Vector3.up * 0.05f);
                rotor.gameObject.AddComponent<DressingSpinner>().Configure(Vector3.up, 120f);
                Part(rotor, "Hub", Cylinder(0.12f, 0.06f), M.Chrome, centre + Vector3.up * 0.02f, Quaternion.identity, moving: true);
                for (int b = 0; b < 5; b++)
                    Box(rotor, "Blade", new Vector3(0.5f, 0.02f, 0.16f), M.White,
                        centre + Vector3.up * 0.05f + Quaternion.Euler(0f, b * 72f, 0f) * Vector3.right * 0.33f,
                        rotation: Quaternion.Euler(0f, b * 72f, 12f), moving: true);
            }
        }
    }
}
