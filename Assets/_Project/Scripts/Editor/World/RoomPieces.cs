using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using ToyFactory.Runtime.World;
using static ToyFactory.Editor.World.DressingKit;

namespace ToyFactory.Editor.World
{
    /// <summary>
    /// Pieces shared by the room dressing builders: the palette materials, door frames and
    /// lintels, windows with the daylight view, clock hands, hazard stripes, saved gears, the
    /// small combined toys, and the light-probe fix. Call <see cref="Load"/> before building.
    /// </summary>
    public static class RoomPieces
    {
        public const string WindowPath = TextureFolder + "/T_Env_WindowSky.png";

        public static readonly Quaternion FacingSouth = Quaternion.identity;          // a quad's front faces +Z
        public static readonly Quaternion FacingEast = Quaternion.Euler(0f, 90f, 0f);
        public static readonly Quaternion FacingNorth = Quaternion.Euler(0f, 180f, 0f);
        public static readonly Quaternion FacingWest = Quaternion.Euler(0f, -90f, 0f);
        public static readonly Quaternion AlongZ = Quaternion.Euler(90f, 0f, 0f);       // a cylinder's +Y to +Z
        public static readonly Quaternion AlongX = Quaternion.Euler(0f, 0f, -90f);      // a cylinder's +Y to +X

        /// <summary>The palette materials and the small sphere, filled by <see cref="Load"/>.</summary>
        public static class M
        {
            public static Material White, Grey, Blue, Red, Yellow, Purple, Chrome, Hazard, Glow, Amber;
            public static Material Mint, Orange, Bubble, Plum, Window;
            public static Mesh Ball;
        }

        /// <summary>
        /// A round porthole window of <paramref name="radius"/> on the wall face at
        /// <paramref name="face"/>, facing into the room along <paramref name="facing"/>: a thick
        /// grey ring with eight chrome bolts round a round pane showing the daylight view.
        /// </summary>
        public static void Porthole(Transform parent, Vector3 face, Quaternion facing, float radius)
        {
            Vector3 normal = facing * Vector3.forward;
            Transform porthole = Group(parent, "Porthole", face);
            Quaternion axisOut = facing * Quaternion.Euler(90f, 0f, 0f);   // a lathe's +Y along the wall normal
            Mesh pane = Custom($"Disc_Window_r{radius:0.##}", () =>
            {
                const int points = 32;
                var vertices = new List<Vector3> { Vector3.zero };
                var uvs = new List<Vector2> { new Vector2(0.5f, 0.5f) };
                var triangles = new List<int>();
                for (int i = 0; i < points; i++)
                {
                    float a = i * Mathf.PI * 2f / points;
                    var p = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                    vertices.Add(new Vector3(p.x * radius, p.y * radius, 0f));
                    uvs.Add(new Vector2(0.5f - p.x * 0.5f, 0.5f + p.y * 0.5f));   // mirrored, like Quad, so it reads from +Z
                }
                for (int i = 0; i < points; i++)
                {
                    triangles.Add(0);
                    triangles.Add(1 + i);
                    triangles.Add(1 + (i + 1) % points);
                }
                var disc = new Mesh { name = "Disc_Window" };
                disc.SetVertices(vertices);
                var normals = new List<Vector3>();
                foreach (Vector3 _ in vertices)
                    normals.Add(Vector3.forward);
                disc.SetNormals(normals);
                disc.SetUVs(0, uvs);
                disc.SetTriangles(triangles, 0);
                disc.RecalculateBounds();
                disc.RecalculateTangents();
                return disc;
            });
            Part(porthole, "Pane", pane, M.Window, face + normal * 0.02f, facing);
            Part(porthole, "Frame", Ring(radius - 0.02f, radius + 0.16f, 0.14f, 32), M.Grey, face, axisOut);
            Part(porthole, "Frame_Lip", Ring(radius - 0.06f, radius + 0.02f, 0.06f, 32), M.White, face + normal * 0.12f, axisOut);
            for (int b = 0; b < 8; b++)
            {
                float a = b * Mathf.PI / 4f + Mathf.PI / 8f;
                Vector3 offset = facing * new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * (radius + 0.08f);
                Part(porthole, "Bolt", Cylinder(0.035f, 0.05f), M.Chrome, face + offset + normal * 0.13f, axisOut);
            }
        }

        /// <summary>Loads or makes the palette materials and paints the window view.</summary>
        public static void Load()
        {
            M.White = Mat("PlasticWhite");
            M.Grey = Mat("PlasticGrey");
            M.Blue = Mat("PlasticBlue");
            M.Red = Mat("PlasticRed");
            M.Yellow = Mat("PlasticYellow");
            M.Purple = Mat("PlasticPurple");
            M.Chrome = Mat("Chrome");
            M.Hazard = Mat("HazardStripe");
            M.Glow = Mat("GlowCircuit");
            M.Amber = Mat("GlowAmber");
            M.Mint = Plastic("PlasticMint", Mint, 0.8f);
            M.Orange = Plastic("PlasticOrange", Orange, 0.8f);
            M.Bubble = Plastic("PlasticBubble", Bubble, 0.8f);
            M.Plum = Plastic("PlasticPlum", Plum, 0.7f);
            M.Ball = Sphere();

            // Window view: sky gradient, a sun and a row of distant sheds.
            var w = new SignPainter(256, Hex("BFE9FF"));
            for (int y = 0; y < 256; y++)
            {
                float t = y / 255f;
                w.RoundRect(new RectInt(0, y, 256, 1), 0f, Color.Lerp(Hex("FFE7B8"), Hex("8FD3FF"), t));
            }
            w.Circle(new Vector2(190f, 170f), 26f, Hex("FFF4CF"));
            Color shed = Hex("7E8FB5");
            w.RoundRect(new RectInt(0, 0, 256, 60), 0f, shed);
            w.Polygon(new[] { new Vector2(10f, 60f), new Vector2(50f, 92f), new Vector2(90f, 60f) }, shed);
            w.Polygon(new[] { new Vector2(90f, 60f), new Vector2(130f, 92f), new Vector2(170f, 60f) }, shed);
            w.RoundRect(new RectInt(196, 60, 22, 90), 4f, shed);
            w.RoundRect(new RectInt(190, 146, 34, 10), 4f, Tomato);
            Texture2D sky = w.Save(WindowPath);
            M.Window = Plastic("Window", Color.white, 0.9f, sky, new Color(0.9f, 0.9f, 0.9f), sky);
        }

        // A light probe inside a solid prop samples darkness and makes the moving parts near it
        // too dark. Each one is pushed out through the nearest face of the prop, 0.15 m clear.
        public static int MoveProbesOutOfSolids(Transform root)
        {
            var group = Object.FindFirstObjectByType<LightProbeGroup>();
            if (group == null)
                return 0;
            var solids = new List<Bounds>();
            foreach (BoxCollider box in root.GetComponentsInChildren<BoxCollider>(true))
                solids.Add(box.bounds);
            Vector3[] positions = group.probePositions;
            int moved = 0;
            for (int i = 0; i < positions.Length; i++)
            {
                Vector3 world = group.transform.TransformPoint(positions[i]);
                foreach (Bounds solid in solids)
                {
                    if (!solid.Contains(world))
                        continue;
                    // Out through whichever side face is nearest (never down through the floor).
                    float toMinX = world.x - solid.min.x, toMaxX = solid.max.x - world.x;
                    float toMinZ = world.z - solid.min.z, toMaxZ = solid.max.z - world.z;
                    float nearest = Mathf.Min(Mathf.Min(toMinX, toMaxX), Mathf.Min(toMinZ, toMaxZ));
                    if (nearest == toMinX) world.x = solid.min.x - 0.15f;
                    else if (nearest == toMaxX) world.x = solid.max.x + 0.15f;
                    else if (nearest == toMinZ) world.z = solid.min.z - 0.15f;
                    else world.z = solid.max.z + 0.15f;
                    positions[i] = group.transform.InverseTransformPoint(world);
                    moved++;
                    break;
                }
            }
            if (moved > 0)
            {
                Undo.RecordObject(group, "Move light probes out of solids");
                group.probePositions = positions;
                EditorUtility.SetDirty(group);
            }
            return moved;
        }
        public static Vector2[] Star(Vector2 centre, float outer, float inner)
        {
            var points = new Vector2[10];
            for (int i = 0; i < 10; i++)
            {
                float a = i * Mathf.PI / 5f;
                float r = i % 2 == 0 ? outer : inner;
                points[i] = centre + new Vector2(Mathf.Sin(a), Mathf.Cos(a)) * r;
            }
            return points;
        }
        // A toothed disc from GearMesh, saved once like the wall gears.
        public static Mesh SavedGear(string name, float tip, float root, float hole, int teeth, float thickness)
        {
            string path = $"{MeshFolder}/{name}.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh != null)
                return mesh;
            mesh = GearMesh.Create(tip, root, hole, teeth, thickness);
            mesh.name = name;
            Unwrapping.GenerateSecondaryUVSet(mesh);
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }
        public static void Stripe(Transform parent, string name, float x, float z, float length, bool alongX) =>
            Box(parent, name, alongX ? new Vector3(length, 0.012f, 0.12f) : new Vector3(0.12f, 0.012f, length),
                M.Hazard, new Vector3(x, 0.006f, z));
        public static void Lintel(Transform level, string path, Vector3 size, Vector3 centre)
        {
            Transform lintel = level.Find(path);
            lintel.position = centre;
            lintel.GetComponent<MeshFilter>().sharedMesh = RoundBox(size);
            lintel.GetComponent<MeshRenderer>().sharedMaterial = M.White;
            var box = lintel.GetComponent<BoxCollider>();
            if (box != null)
            {
                box.center = Vector3.zero;
                box.size = size;
            }
        }
        // Two posts and a header round a 3 m opening centred at 'centre', on the face of the wall towards
        // 'inward' (by default -X for an opening along Z, -Z otherwise); alongZ when the opening runs along Z.
        public static void Frame(Transform parent, string name, Vector3 centre, bool alongZ, Vector3? inward = null)
        {
            Vector3 along = alongZ ? Vector3.forward : Vector3.right;
            Vector3 PostSize() => alongZ ? new Vector3(0.32f, 5.4f, 0.4f) : new Vector3(0.4f, 5.4f, 0.32f);
            Vector3 PlateSize() => alongZ ? new Vector3(0.02f, 1f, 0.3f) : new Vector3(0.3f, 1f, 0.02f);
            Vector3 into = inward ?? (alongZ ? Vector3.left : Vector3.back);   // the room the frame faces
            foreach (float side in new[] { -1.7f, 1.7f })
            {
                Vector3 post = centre + along * side;
                Box(parent, $"{name}_Post", PostSize(), M.Grey, post + Vector3.up * 2.7f, solid: true);
                Box(parent, $"{name}_Hazard", PlateSize(), M.Hazard, post + Vector3.up * 1.1f + into * 0.175f);
            }
            Box(parent, $"{name}_Header", alongZ ? new Vector3(0.32f, 0.4f, 3.8f) : new Vector3(3.8f, 0.4f, 0.32f),
                M.Grey, centre + Vector3.up * 5.2f);
        }
        public static void Move(string path, Vector3 position)
        {
            GameObject target = GameObject.Find(path);
            if (target != null)
                target.transform.position = position;
        }
        // A 2.2 x 1.6 m window on the wall face at 'face', facing into the room along 'facing'.
        public static void Window(Transform parent, Vector3 face, Quaternion facing)
        {
            Vector3 normal = facing * Vector3.forward, right = facing * Vector3.right;
            bool sideWall = Mathf.Abs(normal.x) > 0.5f;   // an east or west wall: the window runs along Z
            Transform window = Group(parent, "Window", face);
            Part(window, "Pane", Quad("Window_2x1.4", new Vector2(2f, 1.4f), new Rect(0f, 0f, 1f, 1f)), M.Window, face + normal * 0.02f, facing);
            Vector3 Horizontal(float w) => sideWall ? new Vector3(0.1f, 0.12f, w) : new Vector3(w, 0.12f, 0.1f);
            Vector3 Vertical(float h) => sideWall ? new Vector3(0.1f, h, 0.12f) : new Vector3(0.12f, h, 0.1f);
            Box(window, "Frame_Top", Horizontal(2.2f), M.Grey, face + normal * 0.05f + Vector3.up * 0.76f);
            Box(window, "Frame_Bottom", Horizontal(2.2f), M.Grey, face + normal * 0.05f - Vector3.up * 0.76f);
            Box(window, "Frame_Left", Vertical(1.64f), M.Grey, face + normal * 0.05f - right * 1.04f);
            Box(window, "Frame_Right", Vertical(1.64f), M.Grey, face + normal * 0.05f + right * 1.04f);
            Box(window, "Mullion", Vertical(1.4f) - (sideWall ? new Vector3(0.04f, 0f, 0.06f) : new Vector3(0.06f, 0f, 0.04f)),
                M.White, face + normal * 0.04f);
            Box(window, "Transom", Horizontal(2f) - (sideWall ? new Vector3(0.04f, 0.06f, 0f) : new Vector3(0f, 0.06f, 0.04f)),
                M.White, face + normal * 0.04f + Vector3.up * 0.12f);
            Box(window, "Sill", sideWall ? new Vector3(0.2f, 0.08f, 2.4f) : new Vector3(2.4f, 0.08f, 0.2f),
                M.White, face + normal * 0.1f - Vector3.up * 0.86f);
        }
        public static void Hand(Transform parent, string name, Vector3 pivot, float length, float width, float degreesPerSecond, Material material)
        {
            Transform hand = Group(parent, name, pivot);
            hand.rotation = FacingNorth;
            Part(hand, name + "_Arm", RoundBox(new Vector3(width, length, 0.015f)), material, pivot + Vector3.up * length * 0.45f, FacingNorth, moving: true);
            hand.gameObject.AddComponent<DressingSpinner>().Configure(Vector3.forward, degreesPerSecond);
        }
        public static GameObject Toy(Transform parent, string kind, Vector3 position, float size, Quaternion rotation,
            bool moving = false, Material paint = null)
        {
            Mesh mesh;
            Material[] materials;
            Mesh box(float x, float y, float z) => RoundBox(new Vector3(x, y, z));
            Matrix4x4 At(Vector3 p, float s) => Matrix4x4.TRS(p, Quaternion.identity, Vector3.one * s);
            Matrix4x4 AtScaled(Vector3 p, Vector3 s) => Matrix4x4.TRS(p, Quaternion.identity, s);
            switch (kind)
            {
                case "Teddy":
                    materials = new[] { M.Orange, M.White, M.Plum };
                    mesh = Combine("Toy_Teddy", new (Mesh, Matrix4x4, int)[]
                    {
                        (M.Ball, At(new Vector3(0f, 0.04f, 0f), 0.3f), 0),          // body
                        (M.Ball, At(new Vector3(0f, 0.27f, 0f), 0.26f), 0),         // head
                        (M.Ball, At(new Vector3(-0.1f, 0.38f, 0f), 0.1f), 0),       // ears
                        (M.Ball, At(new Vector3(0.1f, 0.38f, 0f), 0.1f), 0),
                        (M.Ball, At(new Vector3(0f, 0.24f, 0.11f), 0.1f), 1),       // muzzle
                        (M.Ball, At(new Vector3(0f, 0.27f, 0.155f), 0.04f), 2),     // nose
                        (M.Ball, At(new Vector3(-0.05f, 0.31f, 0.115f), 0.035f), 2),// eyes
                        (M.Ball, At(new Vector3(0.05f, 0.31f, 0.115f), 0.035f), 2),
                        (M.Ball, At(new Vector3(-0.15f, 0.06f, 0.03f), 0.11f), 0), // paws
                        (M.Ball, At(new Vector3(0.15f, 0.06f, 0.03f), 0.11f), 0),
                    }, 3);
                    break;
                case "Robot":
                    materials = new[] { M.Blue, M.White, M.Glow, M.Chrome };
                    mesh = Combine("Toy_Robot", new (Mesh, Matrix4x4, int)[]
                    {
                        (box(0.22f, 0.24f, 0.16f), At(new Vector3(0f, 0.04f, 0f), 1f), 0),
                        (box(0.2f, 0.15f, 0.15f), At(new Vector3(0f, 0.25f, 0f), 1f), 1),
                        (box(0.15f, 0.05f, 0.02f), At(new Vector3(0f, 0.26f, 0.075f), 1f), 2),
                        (Cylinder(0.012f, 0.1f), At(new Vector3(0f, 0.32f, 0f), 1f), 3),
                        (M.Ball, At(new Vector3(0f, 0.43f, 0f), 0.05f), 2),
                        (box(0.06f, 0.18f, 0.06f), At(new Vector3(-0.15f, 0.03f, 0f), 1f), 3),
                        (box(0.06f, 0.18f, 0.06f), At(new Vector3(0.15f, 0.03f, 0f), 1f), 3),
                    }, 4);
                    break;
                default:
                    materials = new[] { M.Yellow, M.Orange, M.Plum };
                    mesh = Combine("Toy_Duck", new (Mesh, Matrix4x4, int)[]
                    {
                        (M.Ball, AtScaled(new Vector3(0f, 0.05f, 0f), new Vector3(0.28f, 0.22f, 0.34f)), 0),
                        (M.Ball, At(new Vector3(0f, 0.22f, 0.1f), 0.19f), 0),
                        (box(0.1f, 0.035f, 0.09f), At(new Vector3(0f, 0.2f, 0.21f), 1f), 1),
                        (M.Ball, At(new Vector3(-0.05f, 0.26f, 0.18f), 0.03f), 2),
                        (M.Ball, At(new Vector3(0.05f, 0.26f, 0.18f), 0.03f), 2),
                    }, 3);
                    break;
            }
            if (paint != null)
                materials[0] = paint;   // a freshly painted toy: the body in the paint colour
            return Part(parent, kind, mesh, materials, position, rotation, moving: moving, scale: Vector3.one * size);
        }
    }
}
