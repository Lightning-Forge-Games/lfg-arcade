using System.Collections.Generic;
using LightningForge.Chess.Core;
using LightningForge.Chess.Game;
using UnityEditor;
using UnityEngine;

namespace LightningForge.Chess.EditorTools
{
    /// <summary>
    /// Builds chess piece meshes procedurally and bakes them to assets and prefabs.
    ///
    /// Every piece except the knight is a surface of revolution, which is how real chess
    /// pieces are made: turned on a lathe from a single profile. Rook crenellations, the
    /// king's cross and the knight's head are added as extra parts and merged in.
    /// </summary>
    public static class ChessPieceMeshGenerator
    {
        const string MeshFolder = "Assets/Models/Pieces";
        const string PrefabFolder = "Assets/Prefabs/Pieces";
        const int Segments = 48;

        [MenuItem("Tools/Chess/Generate Piece Models")]
        public static void GenerateAllMenu()
        {
            Debug.Log(GenerateAll());
        }

        public static string GenerateAll()
        {
            EnsureFolder("Assets/Models");
            EnsureFolder(MeshFolder);
            EnsureFolder("Assets/Prefabs");
            EnsureFolder(PrefabFolder);

            Material white = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/PieceWhite.mat");
            Material black = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/PieceBlack.mat");
            if (white == null || black == null) return "ERROR: piece materials missing";

            var types = new[]
            {
                PieceType.Pawn, PieceType.Knight, PieceType.Bishop,
                PieceType.Rook, PieceType.Queen, PieceType.King
            };

            var log = new System.Text.StringBuilder();

            foreach (PieceType type in types)
            {
                Mesh mesh = BuildMesh(type);
                mesh.name = type.ToString();
                string meshPath = MeshFolder + "/" + type + ".asset";

                Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
                if (existing != null)
                {
                    existing.Clear();
                    existing.vertices = mesh.vertices;
                    existing.triangles = mesh.triangles;
                    existing.normals = mesh.normals;
                    existing.RecalculateBounds();
                    EditorUtility.SetDirty(existing);
                    mesh = existing;
                }
                else
                {
                    AssetDatabase.CreateAsset(mesh, meshPath);
                }

                CreatePrefab(type, PieceColor.White, mesh, white);
                CreatePrefab(type, PieceColor.Black, mesh, black);

                log.Append(type).Append(" verts=").Append(mesh.vertexCount)
                   .Append(" height=").Append(mesh.bounds.max.y.ToString("0.00")).Append("\n");
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return log.ToString();
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/');
            AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
        }

        static void CreatePrefab(PieceType type, PieceColor color, Mesh mesh, Material material)
        {
            string path = PrefabFolder + "/" + color + "_" + type + ".prefab";

            var go = new GameObject(color + "_" + type);
            var filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;

            // A capsule approximates the silhouette closely enough for picking and is far
            // cheaper than a convex mesh collider.
            Bounds bounds = mesh.bounds;
            var collider = go.AddComponent<CapsuleCollider>();
            collider.direction = 1;
            collider.height = bounds.size.y;
            collider.radius = Mathf.Max(bounds.extents.x, bounds.extents.z) * 0.85f;
            collider.center = new Vector3(0f, bounds.size.y * 0.5f, 0f);

            PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
        }

        static Mesh BuildMesh(PieceType type)
        {
            switch (type)
            {
                case PieceType.Pawn: return Lathe(PawnProfile());
                case PieceType.Bishop: return BuildBishop();
                case PieceType.Rook: return BuildRook();
                case PieceType.Queen: return BuildQueen();
                case PieceType.King: return BuildKing();
                case PieceType.Knight: return BuildKnight();
                default: return Lathe(PawnProfile());
            }
        }

        // ---------------------------------------------------------------- profiles

        /// <summary>Shared foot: a wide disc easing into the stem. Keeps the set consistent.</summary>
        static void AddBase(List<Vector2> p, float radius, float top)
        {
            p.Add(new Vector2(0f, 0f));
            p.Add(new Vector2(radius, 0f));
            p.Add(new Vector2(radius, top * 0.28f));
            p.Add(new Vector2(radius * 0.92f, top * 0.42f));
            p.Add(new Vector2(radius * 0.60f, top * 0.72f));
            p.Add(new Vector2(radius * 0.44f, top));
        }

        static Vector2[] PawnProfile()
        {
            var p = new List<Vector2>();
            AddBase(p, 0.33f, 0.16f);
            p.Add(new Vector2(0.13f, 0.26f));
            p.Add(new Vector2(0.12f, 0.32f));
            p.Add(new Vector2(0.19f, 0.36f));   // collar
            p.Add(new Vector2(0.19f, 0.39f));
            p.Add(new Vector2(0.12f, 0.42f));
            p.Add(new Vector2(0.17f, 0.48f));   // head
            p.Add(new Vector2(0.18f, 0.54f));
            p.Add(new Vector2(0.13f, 0.60f));
            p.Add(new Vector2(0.05f, 0.64f));
            p.Add(new Vector2(0f, 0.65f));
            return p.ToArray();
        }

        static Mesh BuildBishop()
        {
            var p = new List<Vector2>();
            AddBase(p, 0.34f, 0.18f);
            p.Add(new Vector2(0.13f, 0.30f));
            p.Add(new Vector2(0.11f, 0.44f));
            p.Add(new Vector2(0.20f, 0.50f));   // collar
            p.Add(new Vector2(0.20f, 0.53f));
            p.Add(new Vector2(0.13f, 0.57f));
            p.Add(new Vector2(0.17f, 0.66f));   // mitre
            p.Add(new Vector2(0.16f, 0.76f));
            p.Add(new Vector2(0.09f, 0.84f));
            p.Add(new Vector2(0.05f, 0.86f));
            p.Add(new Vector2(0.055f, 0.90f));  // finial
            p.Add(new Vector2(0.03f, 0.93f));
            p.Add(new Vector2(0f, 0.94f));
            return Lathe(p.ToArray());
        }

        static Mesh BuildRook()
        {
            var p = new List<Vector2>();
            AddBase(p, 0.34f, 0.18f);
            p.Add(new Vector2(0.17f, 0.30f));
            p.Add(new Vector2(0.155f, 0.46f));
            p.Add(new Vector2(0.205f, 0.52f));  // flared top
            p.Add(new Vector2(0.205f, 0.60f));
            p.Add(new Vector2(0.165f, 0.60f));  // inner lip
            p.Add(new Vector2(0.165f, 0.50f));
            p.Add(new Vector2(0f, 0.50f));      // floor of the tower
            Mesh body = Lathe(p.ToArray());

            // Eight slim merlons around the rim. Four fat ones read as a box, not a turret.
            var parts = new List<CombineInstance> { Instance(body, Matrix4x4.identity) };
            Mesh block = Box(0.040f, 0.055f, 0.026f);
            for (int i = 0; i < 8; i++)
            {
                float angle = i * 45f;
                Quaternion rot = Quaternion.Euler(0f, angle, 0f);
                Vector3 pos = rot * new Vector3(0f, 0.635f, 0.185f);
                parts.Add(Instance(block, Matrix4x4.TRS(pos, rot, Vector3.one)));
            }
            return Combine(parts);
        }

        static Mesh BuildQueen()
        {
            var p = new List<Vector2>();
            AddBase(p, 0.36f, 0.20f);
            p.Add(new Vector2(0.15f, 0.34f));
            p.Add(new Vector2(0.12f, 0.56f));
            p.Add(new Vector2(0.22f, 0.63f));   // collar
            p.Add(new Vector2(0.22f, 0.67f));
            p.Add(new Vector2(0.15f, 0.71f));
            p.Add(new Vector2(0.24f, 0.84f));   // crown flare
            p.Add(new Vector2(0.25f, 0.92f));
            p.Add(new Vector2(0.20f, 0.92f));
            p.Add(new Vector2(0.19f, 0.86f));   // crown hollow
            p.Add(new Vector2(0.09f, 0.88f));
            p.Add(new Vector2(0.08f, 0.96f));   // finial ball
            p.Add(new Vector2(0.06f, 1.00f));
            p.Add(new Vector2(0f, 1.02f));
            Mesh body = Lathe(p.ToArray());

            var parts = new List<CombineInstance> { Instance(body, Matrix4x4.identity) };
            Mesh point = Box(0.05f, 0.06f, 0.05f);
            for (int i = 0; i < 8; i++)
            {
                Quaternion rot = Quaternion.Euler(0f, i * 45f, 0f);
                Vector3 pos = rot * new Vector3(0f, 0.945f, 0.225f);
                parts.Add(Instance(point, Matrix4x4.TRS(pos, rot, Vector3.one)));
            }
            return Combine(parts);
        }

        static Mesh BuildKing()
        {
            var p = new List<Vector2>();
            AddBase(p, 0.37f, 0.21f);
            p.Add(new Vector2(0.16f, 0.36f));
            p.Add(new Vector2(0.13f, 0.62f));
            p.Add(new Vector2(0.23f, 0.70f));   // collar
            p.Add(new Vector2(0.23f, 0.74f));
            p.Add(new Vector2(0.16f, 0.78f));
            p.Add(new Vector2(0.23f, 0.92f));   // crown
            p.Add(new Vector2(0.24f, 1.00f));
            p.Add(new Vector2(0.19f, 1.00f));
            p.Add(new Vector2(0.18f, 0.94f));
            p.Add(new Vector2(0.08f, 0.97f));
            p.Add(new Vector2(0.07f, 1.04f));
            p.Add(new Vector2(0f, 1.05f));
            Mesh body = Lathe(p.ToArray());

            // The cross that identifies the king.
            var parts = new List<CombineInstance>
            {
                Instance(body, Matrix4x4.identity),
                Instance(Box(0.035f, 0.13f, 0.035f), Matrix4x4.Translate(new Vector3(0f, 1.11f, 0f))),
                Instance(Box(0.09f, 0.035f, 0.035f), Matrix4x4.Translate(new Vector3(0f, 1.12f, 0f)))
            };
            return Combine(parts);
        }

        /// <summary>
        /// The one piece that cannot be turned on a lathe. A lathed plinth carries a head
        /// built by extruding a side-on horse silhouette, which is how most stylised sets
        /// do it. Stacked boxes were tried first and read as an angular blob: the knight
        /// is recognised entirely by its profile, so the profile has to be the input.
        /// </summary>
        static Mesh BuildKnight()
        {
            var p = new List<Vector2>();
            AddBase(p, 0.34f, 0.18f);
            p.Add(new Vector2(0.17f, 0.28f));
            p.Add(new Vector2(0.19f, 0.34f));
            p.Add(new Vector2(0.19f, 0.38f));
            p.Add(new Vector2(0f, 0.38f));
            Mesh plinth = Lathe(p.ToArray());

            // Silhouette in the YZ plane: +z is forward (the way the knight faces), y is up.
            // Wound clockwise starting at the base of the throat.
            Vector2[] outline =
            {
                new Vector2(0.15f, 0.36f),   // throat, bottom front
                new Vector2(0.11f, 0.54f),   // throat curve
                new Vector2(0.15f, 0.66f),   // jaw
                new Vector2(0.30f, 0.71f),   // muzzle underside
                new Vector2(0.35f, 0.79f),   // nose tip
                new Vector2(0.29f, 0.85f),   // bridge of the nose
                new Vector2(0.13f, 0.88f),   // forehead dip
                new Vector2(0.08f, 1.00f),   // ear, front edge
                new Vector2(0.01f, 0.89f),   // notch behind the ear
                new Vector2(-0.07f, 0.97f),  // mane tuft
                new Vector2(-0.17f, 0.84f),  // crest
                new Vector2(-0.22f, 0.64f),  // back of the neck
                new Vector2(-0.18f, 0.46f),
                new Vector2(-0.13f, 0.36f)   // neck, bottom back
            };

            var parts = new List<CombineInstance>
            {
                Instance(plinth, Matrix4x4.identity),
                Instance(ExtrudeProfile(outline, 0.105f), Matrix4x4.identity)
            };
            return Combine(parts);
        }

        // ---------------------------------------------------------------- primitives

        /// <summary>Revolves a profile around the Y axis. Profile x is radius, y is height.</summary>
        static Mesh Lathe(Vector2[] profile)
        {
            int rings = profile.Length;
            int cols = Segments + 1;   // duplicate seam column so UVs do not wrap

            var vertices = new Vector3[rings * cols];
            var uvs = new Vector2[rings * cols];

            for (int r = 0; r < rings; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    float t = (float)c / Segments;
                    float angle = t * Mathf.PI * 2f;
                    float radius = profile[r].x;
                    vertices[r * cols + c] = new Vector3(
                        Mathf.Cos(angle) * radius, profile[r].y, Mathf.Sin(angle) * radius);
                    uvs[r * cols + c] = new Vector2(t, profile[r].y);
                }
            }

            var triangles = new List<int>((rings - 1) * Segments * 6);
            for (int r = 0; r < rings - 1; r++)
            {
                for (int c = 0; c < Segments; c++)
                {
                    int a = r * cols + c;
                    int b = a + 1;
                    int d = (r + 1) * cols + c;
                    int e = d + 1;

                    triangles.Add(a); triangles.Add(d); triangles.Add(b);
                    triangles.Add(b); triangles.Add(d); triangles.Add(e);
                }
            }

            var mesh = new Mesh();
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = triangles.ToArray();
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// Extrudes a closed 2D outline along X. Outline points are (z, y): z forward, y up.
        /// The outline may be concave, so the caps are ear-clipped rather than fanned.
        /// </summary>
        static Mesh ExtrudeProfile(Vector2[] outline, float halfWidth)
        {
            // Normalise winding so the cap and wall orientations below are deterministic.
            var poly = new List<Vector2>(outline);
            if (SignedArea(poly) < 0f) poly.Reverse();

            int n = poly.Count;
            var verts = new List<Vector3>();
            var tris = new List<int>();

            // Caps and walls get their own vertices. Sharing them makes RecalculateNormals
            // average a flat cap with a perpendicular wall, which smears the whole piece
            // into a rounded shell instead of a crisp extrusion.
            for (int i = 0; i < n; i++) verts.Add(new Vector3(halfWidth, poly[i].y, poly[i].x));
            for (int i = 0; i < n; i++) verts.Add(new Vector3(-halfWidth, poly[i].y, poly[i].x));

            List<int> cap = Triangulate(poly);

            // +X cap, wound so it faces outward.
            for (int i = 0; i < cap.Count; i += 3)
            {
                tris.Add(cap[i + 2]); tris.Add(cap[i + 1]); tris.Add(cap[i]);
            }

            // -X cap, opposite winding.
            for (int i = 0; i < cap.Count; i += 3)
            {
                tris.Add(n + cap[i]); tris.Add(n + cap[i + 1]); tris.Add(n + cap[i + 2]);
            }

            // Side walls, four fresh vertices per edge so every crease stays hard.
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                int b = verts.Count;
                verts.Add(new Vector3(halfWidth, poly[i].y, poly[i].x));
                verts.Add(new Vector3(halfWidth, poly[j].y, poly[j].x));
                verts.Add(new Vector3(-halfWidth, poly[j].y, poly[j].x));
                verts.Add(new Vector3(-halfWidth, poly[i].y, poly[i].x));

                tris.Add(b); tris.Add(b + 1); tris.Add(b + 2);
                tris.Add(b); tris.Add(b + 2); tris.Add(b + 3);
            }

            var mesh = new Mesh();
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        static float SignedArea(List<Vector2> poly)
        {
            float area = 0f;
            for (int i = 0; i < poly.Count; i++)
            {
                Vector2 a = poly[i];
                Vector2 b = poly[(i + 1) % poly.Count];
                area += a.x * b.y - b.x * a.y;
            }
            return area * 0.5f;
        }

        /// <summary>Ear clipping for a simple polygon wound counter clockwise.</summary>
        static List<int> Triangulate(List<Vector2> poly)
        {
            var result = new List<int>();
            int n = poly.Count;
            if (n < 3) return result;

            var remaining = new List<int>(n);
            for (int i = 0; i < n; i++) remaining.Add(i);

            int guard = n * n;
            while (remaining.Count > 2 && guard-- > 0)
            {
                bool clipped = false;
                for (int i = 0; i < remaining.Count; i++)
                {
                    int ia = remaining[(i + remaining.Count - 1) % remaining.Count];
                    int ib = remaining[i];
                    int ic = remaining[(i + 1) % remaining.Count];

                    Vector2 a = poly[ia], b = poly[ib], c = poly[ic];
                    if (Cross(b - a, c - b) <= 0f) continue;   // reflex, not an ear

                    bool contains = false;
                    for (int k = 0; k < remaining.Count; k++)
                    {
                        int idx = remaining[k];
                        if (idx == ia || idx == ib || idx == ic) continue;
                        if (PointInTriangle(poly[idx], a, b, c)) { contains = true; break; }
                    }
                    if (contains) continue;

                    result.Add(ia); result.Add(ib); result.Add(ic);
                    remaining.RemoveAt(i);
                    clipped = true;
                    break;
                }
                if (!clipped) break;   // degenerate outline; emit what we have
            }

            return result;
        }

        static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

        static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = Cross(b - a, p - a);
            float d2 = Cross(c - b, p - b);
            float d3 = Cross(a - c, p - c);
            bool hasNeg = d1 < 0f || d2 < 0f || d3 < 0f;
            bool hasPos = d1 > 0f || d2 > 0f || d3 > 0f;
            return !(hasNeg && hasPos);
        }

        /// <summary>Axis-aligned box centred on the origin, given half extents.</summary>
        static Mesh Box(float hx, float hy, float hz)
        {
            var mesh = new Mesh();
            Vector3[] c =
            {
                new Vector3(-hx, -hy, -hz), new Vector3(hx, -hy, -hz),
                new Vector3(hx, hy, -hz), new Vector3(-hx, hy, -hz),
                new Vector3(-hx, -hy, hz), new Vector3(hx, -hy, hz),
                new Vector3(hx, hy, hz), new Vector3(-hx, hy, hz)
            };

            var verts = new List<Vector3>();
            var tris = new List<int>();
            int[][] faces =
            {
                new[] { 0, 3, 2, 1 },   // back
                new[] { 5, 6, 7, 4 },   // front
                new[] { 4, 7, 3, 0 },   // left
                new[] { 1, 2, 6, 5 },   // right
                new[] { 3, 7, 6, 2 },   // top
                new[] { 4, 0, 1, 5 }    // bottom
            };

            foreach (int[] f in faces)
            {
                int b = verts.Count;
                verts.Add(c[f[0]]); verts.Add(c[f[1]]); verts.Add(c[f[2]]); verts.Add(c[f[3]]);
                tris.Add(b); tris.Add(b + 1); tris.Add(b + 2);
                tris.Add(b); tris.Add(b + 2); tris.Add(b + 3);
            }

            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        static CombineInstance Instance(Mesh mesh, Matrix4x4 transform)
        {
            var ci = new CombineInstance();
            ci.mesh = mesh;
            ci.transform = transform;
            return ci;
        }

        static Mesh Combine(List<CombineInstance> parts)
        {
            var mesh = new Mesh();
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.CombineMeshes(parts.ToArray(), true, true);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
