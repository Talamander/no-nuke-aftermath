using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Talamander.NukeAftermath
{
    /// <summary>
    /// Procedural charred trunks, merged into one mesh per 512 m cell so a blast costs a few draw calls.
    /// Nothing comes from game assets. No colliders.
    ///
    /// Coordinates: tree positions are the game's global (map-centred) positions, the same frame as TreeRenderer
    /// PositionData. Each cell object is meant to be parented to Datum.origin with its local position at the cell
    /// centre, so the floating origin moves it with the map.
    /// </summary>
    internal static class TrunkMeshBuilder
    {
        const float CellSize = 512f;
        const int Sides = 5;

        public static List<GameObject> Build(List<Vector3> clumps, Vector3 groundZero, float clearRadius, int trunksPerClump,
            float clumpRadius, float flattenFraction, Material material)
        {
            var cells = new Dictionary<(int, int), List<Vector3>>();
            foreach (var p in clumps)
            {
                var key = (Mathf.FloorToInt(p.x / CellSize), Mathf.FloorToInt(p.z / CellSize));
                if (!cells.TryGetValue(key, out var list)) cells[key] = list = new List<Vector3>();
                list.Add(p);
            }

            var result = new List<GameObject>();
            foreach (var kv in cells)
            {
                var origin = new Vector3((kv.Key.Item1 + 0.5f) * CellSize, 0f, (kv.Key.Item2 + 0.5f) * CellSize);
                origin.y = kv.Value[0].y;
                var verts = new List<Vector3>();
                var normals = new List<Vector3>();
                var tris = new List<int>();
                foreach (var p in kv.Value)
                {
                    // Deterministic per tree, so every client with the mod draws the same trunks.
                    var rng = new System.Random(Hash(p));
                    float d = Vector2.Distance(new Vector2(p.x, p.z), new Vector2(groundZero.x, groundZero.z));
                    var away = new Vector3(p.x - groundZero.x, 0f, p.z - groundZero.z);
                    away = away.sqrMagnitude > 1f ? away.normalized : Vector3.forward;
                    bool flattened = d < clearRadius * flattenFraction;
                    for (int t = 0; t < trunksPerClump; t++)
                    {
                        float ang = (float)rng.NextDouble() * Mathf.PI * 2f;
                        float rad = Mathf.Sqrt((float)rng.NextDouble()) * clumpRadius;
                        var basePos = p + new Vector3(Mathf.Cos(ang) * rad, 0f, Mathf.Sin(ang) * rad) - origin;
                        float height = Mathf.Lerp(6f, 22f, (float)rng.NextDouble());
                        float radius = Mathf.Lerp(0.18f, 0.45f, (float)rng.NextDouble());
                        Vector3 dir;
                        if (flattened || rng.NextDouble() < 0.15)
                        {
                            // Blown down, pointing away from ground zero with some scatter, lying on the ground.
                            var jitter = Quaternion.Euler(0f, Mathf.Lerp(-25f, 25f, (float)rng.NextDouble()), 0f);
                            dir = (jitter * away + Vector3.up * 0.05f).normalized;
                            basePos.y += radius;
                            height *= 0.8f;
                        }
                        else
                        {
                            // Standing snag: leaning away from the blast, sometimes snapped short.
                            float lean = Mathf.Lerp(3f, 18f, (float)rng.NextDouble());
                            dir = Quaternion.AngleAxis(lean, Vector3.Cross(Vector3.up, away)) * Vector3.up;
                            if (rng.NextDouble() < 0.35) height *= 0.35f;
                            basePos.y -= 0.5f; // sink a little so slopes do not show a gap
                        }
                        AddTrunk(verts, normals, tris, basePos, dir, height, radius);
                    }
                }

                var mesh = new Mesh { name = "NukeAftermath_Trunks", indexFormat = IndexFormat.UInt32 };
                mesh.SetVertices(verts);
                mesh.SetNormals(normals);
                mesh.SetTriangles(tris, 0);
                mesh.RecalculateBounds();
                mesh.UploadMeshData(true);
                var go = new GameObject("NukeAftermath_TrunkCell");
                go.transform.localPosition = origin;
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = material;
                mr.shadowCastingMode = ShadowCastingMode.On;
                mr.receiveShadows = true;
                result.Add(go);
            }
            return result;
        }

        static void AddTrunk(List<Vector3> v, List<Vector3> n, List<int> t, Vector3 basePos, Vector3 dir, float height, float radius)
        {
            var rot = Quaternion.FromToRotation(Vector3.up, dir);
            float topRadius = radius * 0.35f;
            var top = basePos + dir * height;
            for (int s = 0; s < Sides; s++)
            {
                float a0 = s * Mathf.PI * 2f / Sides, a1 = (s + 1) * Mathf.PI * 2f / Sides;
                var o0 = rot * new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0));
                var o1 = rot * new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
                int i = v.Count;
                v.Add(basePos + o0 * radius); v.Add(basePos + o1 * radius);
                v.Add(top + o1 * topRadius); v.Add(top + o0 * topRadius);
                var normal = (o0 + o1).normalized;
                n.Add(normal); n.Add(normal); n.Add(normal); n.Add(normal);
                t.Add(i); t.Add(i + 2); t.Add(i + 1);
                t.Add(i); t.Add(i + 3); t.Add(i + 2);
            }
        }

        static int Hash(Vector3 p)
        {
            unchecked
            {
                int h = (int)(p.x * 10f) * 73856093;
                h ^= (int)(p.z * 10f) * 19349663;
                h ^= (int)(p.y * 10f) * 83492791;
                return h;
            }
        }
    }
}
