using System.Collections.Generic;
using UnityEngine;

namespace LightningForge.Arcade.Game
{
    /// <summary>
    /// Procedural meshes with rounded edges.
    ///
    /// Everything in the arcade is built from primitives at runtime, and Unity's cube has
    /// perfectly sharp edges. Real dice, trays, rails and counters do not: a sharp edge
    /// catches no light along its length, so a scene made of them reads as untextured
    /// blocks no matter how the materials are set up. A small bevel gives every edge a
    /// highlight, which is most of the difference between "primitives" and "objects".
    ///
    /// Meshes are cached by their parameters, because a board asks for the same rounded
    /// counter thirty times and each Mesh is a separate allocation otherwise.
    /// </summary>
    public static class ArcadeMeshes
    {
        static readonly Dictionary<string, Mesh> Cache = new Dictionary<string, Mesh>();

        /// <summary>
        /// A box with rounded edges and corners.
        ///
        /// Every vertex of a plain box is pushed out from the nearest point on an inner,
        /// shrunken box. On a face that leaves the surface flat, along an edge it sweeps a
        /// quarter cylinder, and at a corner an eighth of a sphere, all from one expression.
        /// The direction it moved is also the normal, so the shading comes out right with
        /// no separate normal pass.
        /// </summary>
        public static Mesh RoundedBox(Vector3 size, float radius, int segments = 6)
        {
            radius = Mathf.Min(radius, Mathf.Min(size.x, Mathf.Min(size.y, size.z)) * 0.5f);
            segments = Mathf.Max(2, segments);

            string key = "box" + size + radius + segments;
            if (Cache.TryGetValue(key, out Mesh cached) && cached != null) return cached;

            Vector3 half = size * 0.5f;
            Vector3 inner = new Vector3(
                Mathf.Max(0f, half.x - radius),
                Mathf.Max(0f, half.y - radius),
                Mathf.Max(0f, half.z - radius));

            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var triangles = new List<int>();

            // Six faces, each a grid. Shared edges are duplicated between faces, which
            // costs a few vertices and buys not having to stitch them together.
            AddFace(vertices, normals, triangles, half, inner, segments, Vector3.right);
            AddFace(vertices, normals, triangles, half, inner, segments, Vector3.left);
            AddFace(vertices, normals, triangles, half, inner, segments, Vector3.up);
            AddFace(vertices, normals, triangles, half, inner, segments, Vector3.down);
            AddFace(vertices, normals, triangles, half, inner, segments, Vector3.forward);
            AddFace(vertices, normals, triangles, half, inner, segments, Vector3.back);

            var mesh = new Mesh { name = "RoundedBox" };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();

            Cache[key] = mesh;
            return mesh;
        }

        static void AddFace(List<Vector3> vertices, List<Vector3> normals, List<int> triangles,
            Vector3 half, Vector3 inner, int segments, Vector3 axis)
        {
            // A right handed frame across the face. Building it the other way round leaves
            // some faces wound backwards, which renders them inside out and unlit.
            Vector3 up = Mathf.Abs(axis.y) > 0.5f ? Vector3.forward : Vector3.up;
            Vector3 right = Vector3.Cross(up, axis).normalized;
            up = Vector3.Cross(axis, right).normalized;

            int start = vertices.Count;

            for (int y = 0; y <= segments; y++)
            {
                for (int x = 0; x <= segments; x++)
                {
                    float u = x / (float)segments * 2f - 1f;
                    float v = y / (float)segments * 2f - 1f;

                    // A point on the plain box, then pushed out from the shrunken one.
                    Vector3 point = Vector3.Scale(axis, half)
                        + Vector3.Scale(right, half) * u
                        + Vector3.Scale(up, half) * v;

                    Vector3 clamped = new Vector3(
                        Mathf.Clamp(point.x, -inner.x, inner.x),
                        Mathf.Clamp(point.y, -inner.y, inner.y),
                        Mathf.Clamp(point.z, -inner.z, inner.z));

                    Vector3 offset = point - clamped;
                    float distance = offset.magnitude;
                    Vector3 normal = distance > 0.0001f ? offset / distance : axis;
                    float radius = Mathf.Min(
                        half.x - inner.x, Mathf.Min(half.y - inner.y, half.z - inner.z));

                    vertices.Add(clamped + normal * radius);
                    normals.Add(normal);
                }
            }

            int stride = segments + 1;

            // Rather than reason about winding per face, wind one triangle and check it
            // against the face direction. Getting this wrong is invisible in the editor
            // until a surface renders black, so it is worth measuring instead of assuming.
            Vector3 a = vertices[start];
            Vector3 b = vertices[start + stride];
            Vector3 c = vertices[start + 1];
            bool flip = Vector3.Dot(Vector3.Cross(b - a, c - a), axis) < 0f;

            for (int y = 0; y < segments; y++)
            {
                for (int x = 0; x < segments; x++)
                {
                    int i = start + y * stride + x;
                    if (flip)
                    {
                        triangles.Add(i);
                        triangles.Add(i + 1);
                        triangles.Add(i + stride);

                        triangles.Add(i + 1);
                        triangles.Add(i + stride + 1);
                        triangles.Add(i + stride);
                    }
                    else
                    {
                        triangles.Add(i);
                        triangles.Add(i + stride);
                        triangles.Add(i + 1);

                        triangles.Add(i + 1);
                        triangles.Add(i + stride);
                        triangles.Add(i + stride + 1);
                    }
                }
            }
        }

        /// <summary>
        /// An open topped tube: an outer wall, an inner wall, a rim joining them and a solid
        /// base. Used for the dice cup, where a solid cylinder reads as a mug and the gap
        /// down the middle is what makes it look like something dice come out of.
        /// </summary>
        public static Mesh Tube(float outerRadius, float innerRadius, float height, int segments = 40)
        {
            segments = Mathf.Max(8, segments);
            string key = "tube" + outerRadius + innerRadius + height + segments;
            if (Cache.TryGetValue(key, out Mesh cached) && cached != null) return cached;

            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var triangles = new List<int>();

            float top = height * 0.5f;
            float bottom = -height * 0.5f;
            float floor = bottom + Mathf.Min(height * 0.14f, 0.06f);

            for (int i = 0; i <= segments; i++)
            {
                float angle = i / (float)segments * Mathf.PI * 2f;
                var outward = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));

                // Outer wall, normals pointing out.
                vertices.Add(outward * outerRadius + Vector3.up * top);
                normals.Add(outward);
                vertices.Add(outward * outerRadius + Vector3.up * bottom);
                normals.Add(outward);

                // Inner wall, normals pointing in.
                vertices.Add(outward * innerRadius + Vector3.up * top);
                normals.Add(-outward);
                vertices.Add(outward * innerRadius + Vector3.up * floor);
                normals.Add(-outward);

                // The rim, and the floor inside.
                vertices.Add(outward * outerRadius + Vector3.up * top);
                normals.Add(Vector3.up);
                vertices.Add(outward * innerRadius + Vector3.up * top);
                normals.Add(Vector3.up);
                vertices.Add(outward * innerRadius + Vector3.up * floor);
                normals.Add(Vector3.up);

                // The underside.
                vertices.Add(outward * outerRadius + Vector3.up * bottom);
                normals.Add(Vector3.down);
            }

            const int ring = 8;
            int centreFloor = vertices.Count;
            vertices.Add(Vector3.up * floor);
            normals.Add(Vector3.up);
            int centreBottom = vertices.Count;
            vertices.Add(Vector3.up * bottom);
            normals.Add(Vector3.down);

            for (int i = 0; i < segments; i++)
            {
                int a = i * ring;
                int b = (i + 1) * ring;

                Quad(triangles, a + 0, a + 1, b + 0, b + 1);       // outer wall
                Quad(triangles, b + 2, b + 3, a + 2, a + 3);       // inner wall
                Quad(triangles, a + 4, a + 5, b + 4, b + 5);       // rim

                // Floor inside and the flat underside.
                triangles.Add(a + 6); triangles.Add(centreFloor); triangles.Add(b + 6);
                triangles.Add(b + 7); triangles.Add(centreBottom); triangles.Add(a + 7);
            }

            var mesh = new Mesh { name = "Tube" };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();

            Cache[key] = mesh;
            return mesh;
        }

        static void Quad(List<int> triangles, int a, int b, int c, int d)
        {
            triangles.Add(a); triangles.Add(b); triangles.Add(c);
            triangles.Add(c); triangles.Add(b); triangles.Add(d);
        }

        /// <summary>
        /// A round counter with a filleted rim, for draughts pieces, Connect 4 discs and
        /// backgammon checkers.
        ///
        /// Built by revolving a profile rather than rounding a box, because a rounded box
        /// with a square footprint is a rounded box: the counters came out as cushions the
        /// first time this was attempted that way.
        /// </summary>
        public static Mesh Counter(float radius, float thickness, int segments = 34)
        {
            string key = "counter" + radius + thickness + segments;
            if (Cache.TryGetValue(key, out Mesh cached) && cached != null) return cached;

            float half = thickness * 0.5f;
            float fillet = Mathf.Min(half * 0.85f, radius * 0.3f);
            var profile = new List<(Vector2 point, Vector2 normal)>();

            // Up the outside from the underside to the top, rounding both rims. The profile
            // runs bottom to top so the revolved triangles all wind the same way.
            profile.Add((new Vector2(0f, -half), new Vector2(0f, -1f)));
            profile.Add((new Vector2(radius - fillet, -half), new Vector2(0f, -1f)));

            const int arc = 4;
            for (int i = 1; i <= arc; i++)
            {
                float a = i / (float)arc * Mathf.PI * 0.5f;
                profile.Add((
                    new Vector2(radius - fillet + Mathf.Sin(a) * fillet, -half + (1f - Mathf.Cos(a)) * fillet),
                    new Vector2(Mathf.Sin(a), -Mathf.Cos(a))));
            }
            for (int i = 0; i <= arc; i++)
            {
                float a = i / (float)arc * Mathf.PI * 0.5f;
                profile.Add((
                    new Vector2(radius - fillet + Mathf.Cos(a) * fillet, half - (1f - Mathf.Sin(a)) * fillet),
                    new Vector2(Mathf.Cos(a), Mathf.Sin(a))));
            }

            profile.Add((new Vector2(radius - fillet, half), new Vector2(0f, 1f)));
            profile.Add((new Vector2(0f, half), new Vector2(0f, 1f)));

            Mesh mesh = Lathe(profile, segments);
            mesh.name = "Counter";
            Cache[key] = mesh;
            return mesh;
        }

        /// <summary>Revolves a profile of (radius, height) points around the Y axis.</summary>
        static Mesh Lathe(List<(Vector2 point, Vector2 normal)> profile, int segments)
        {
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var triangles = new List<int>();

            for (int i = 0; i <= segments; i++)
            {
                float angle = i / (float)segments * Mathf.PI * 2f;
                float cos = Mathf.Cos(angle);
                float sin = Mathf.Sin(angle);

                foreach (var step in profile)
                {
                    vertices.Add(new Vector3(cos * step.point.x, step.point.y, sin * step.point.x));
                    normals.Add(new Vector3(cos * step.normal.x, step.normal.y, sin * step.normal.x).normalized);
                }
            }

            int stride = profile.Count;
            for (int i = 0; i < segments; i++)
            {
                for (int j = 0; j < stride - 1; j++)
                {
                    int a = i * stride + j;
                    int b = (i + 1) * stride + j;

                    triangles.Add(a);
                    triangles.Add(a + 1);
                    triangles.Add(b);

                    triangles.Add(b);
                    triangles.Add(a + 1);
                    triangles.Add(b + 1);
                }
            }

            var mesh = new Mesh();
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// A rounded box as a ready made object, since every game builds its board out of
        /// them and each was growing its own slightly different copy of this.
        ///
        /// The mesh carries the real size and the transform stays unscaled, because scaling
        /// a unit cube would stretch the bevel differently on every face. The collider is
        /// resized to match and stays a plain box: physics does not care about a bevel.
        /// </summary>
        public static GameObject Box(Transform parent, string name, Vector3 localPosition,
            Vector3 size, float bevel, Material material, bool collide)
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.transform.SetParent(parent, false);
            box.transform.localPosition = localPosition;
            box.transform.localScale = Vector3.one;
            ApplyMesh(box, RoundedBox(size, bevel, 5));

            if (collide) box.GetComponent<BoxCollider>().size = size;
            else Object.Destroy(box.GetComponent<Collider>());

            if (material != null) box.GetComponent<MeshRenderer>().sharedMaterial = material;
            return box;
        }

        /// <summary>
        /// Replaces a primitive's mesh, keeping its collider. Colliders stay as the simple
        /// primitive shape on purpose: physics does not care about a bevel, and a mesh
        /// collider would cost far more for a difference nobody can feel.
        /// </summary>
        public static void ApplyMesh(GameObject target, Mesh mesh)
        {
            var filter = target.GetComponent<MeshFilter>();
            if (filter != null) filter.sharedMesh = mesh;
        }
    }
}
