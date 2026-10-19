using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Talamander.NukeAftermath
{
    /// <summary>
    /// Procedural charred trunks, merged into one mesh per 512 m cell so a blast costs a few draw calls.
    /// Nothing comes from game assets. No colliders.
    ///
    /// Inner zone: trunks blown flat, pointing away from ground zero, some broken in two. Toward the edge of the
    /// cleared area more and more of them stand as burnt snags, leaning away from the blast, many snapped off, with
    /// stubs of branches. A small bark atlas (four tones from jet-black charcoal to grey ash) breaks up the colour.
    ///
    /// Coordinates: tree positions are the game's global (map-centred) positions, the same frame as TreeRenderer
    /// PositionData. Cells are built lazily, one per enumeration step, so the caller can spread the work over frames. Each cell object is parented to Datum.origin with its local position at the cell centre, so the
    /// floating origin moves it with the map. Trunk bases are dropped onto the terrain with a short raycast.
    /// </summary>
    internal static class TrunkMeshBuilder
    {
        const float CellSize = 512f;
        const int Tones = 4;
        static Material material;

        public static IEnumerable<GameObject> Build(List<Vector3> clumps, Vector3 groundZero, float clearRadius, int trunksPerClump,
            float clumpRadius, float flattenFraction, Material material, PavedAreas paved = null)
        {
            var cells = new Dictionary<(int, int), List<Vector3>>();
            foreach (var p in clumps)
            {
                var key = (Mathf.FloorToInt(p.x / CellSize), Mathf.FloorToInt(p.z / CellSize));
                if (!cells.TryGetValue(key, out var list)) cells[key] = list = new List<Vector3>();
                list.Add(p);
            }

            var origin0 = Datum.origin != null ? Datum.origin.position : Vector3.zero; // global -> world offset
            foreach (var kv in cells)
            {
                var origin = new Vector3((kv.Key.Item1 + 0.5f) * CellSize, kv.Value[0].y, (kv.Key.Item2 + 0.5f) * CellSize);
                var m = new MeshData();
                foreach (var p in kv.Value)
                {
                    // Deterministic per tree, so every client with the mod draws the same trunks.
                    var rng = new System.Random(Hash(p));
                    float d = Vector2.Distance(new Vector2(p.x, p.z), new Vector2(groundZero.x, groundZero.z));
                    var away = new Vector3(p.x - groundZero.x, 0f, p.z - groundZero.z);
                    away = away.sqrMagnitude > 1f ? away.normalized : Vector3.forward;
                    float rel = d / Mathf.Max(1f, clearRadius);
                    // Chance of standing: none in the inner zone, rising to most of them at the edge.
                    float standChance = Mathf.Clamp01((rel - flattenFraction) / Mathf.Max(0.05f, 1f - flattenFraction)) * 0.85f;
                    for (int t = 0; t < trunksPerClump; t++)
                    {
                        float ang = (float)rng.NextDouble() * Mathf.PI * 2f;
                        float rad = Mathf.Sqrt((float)rng.NextDouble()) * clumpRadius;
                        var g = p + new Vector3(Mathf.Cos(ang) * rad, 0f, Mathf.Sin(ang) * rad);
                        // A blown-down trunk is up to 26 m long: none within that of a runway or taxiway.
                        if (paved != null && paved.Any && paved.Distance(g) < 26f) continue;
                        g.y = Ground(g, p.y, origin0);
                        var basePos = g - origin;
                        float height = Mathf.Lerp(9f, 26f, (float)rng.NextDouble());
                        float radius = Mathf.Lerp(0.28f, 0.7f, (float)rng.NextDouble());
                        int tone = rng.NextDouble() < 0.55 ? 0 : rng.Next(1, Tones);
                        if (rng.NextDouble() >= standChance)
                        {
                            // Blown down, pointing away from ground zero with some scatter, lying on the ground.
                            var jitter = Quaternion.Euler(0f, Mathf.Lerp(-28f, 28f, (float)rng.NextDouble()), 0f);
                            var dir = (jitter * away + Vector3.up * 0.04f).normalized;
                            basePos.y += radius * 0.7f;
                            if (rng.NextDouble() < 0.3)
                            {
                                // Broken in two: a second short piece a little further on, skewed.
                                float h1 = height * Mathf.Lerp(0.35f, 0.6f, (float)rng.NextDouble());
                                m.AddTrunk(basePos, dir, h1, radius, 0.75f, 5, tone);
                                var dir2 = (Quaternion.Euler(0f, Mathf.Lerp(-35f, 35f, (float)rng.NextDouble()), 0f) * dir).normalized;
                                m.AddTrunk(basePos + dir * (h1 + 1.5f), dir2, height - h1, radius * 0.75f, 0.35f, 5, tone);
                            }
                            else m.AddTrunk(basePos, dir, height * 0.85f, radius, 0.3f, 5, tone);
                        }
                        else
                        {
                            // Standing snag: leaning away from the blast, often snapped short, with branch stubs.
                            float lean = Mathf.Lerp(2f, 16f, (float)rng.NextDouble()) * (1.2f - rel * 0.5f);
                            var dir = Quaternion.AngleAxis(lean, Vector3.Cross(Vector3.up, away)) * Vector3.up;
                            bool snapped = rng.NextDouble() < 0.4;
                            if (snapped) height *= Mathf.Lerp(0.25f, 0.55f, (float)rng.NextDouble());
                            basePos.y -= 0.6f; // sink a little so slopes do not show a gap
                            m.AddTrunk(basePos, dir, height, radius * 1.1f, snapped ? 0.7f : 0.18f, 6, tone);
                            int stubs = snapped ? rng.Next(0, 2) : rng.Next(2, 5);
                            for (int s = 0; s < stubs; s++)
                            {
                                float at = Mathf.Lerp(0.45f, 0.92f, (float)rng.NextDouble()) * height;
                                float sa = (float)rng.NextDouble() * 360f;
                                // Branches survive mostly on the lee side, angled up.
                                var side = Quaternion.AngleAxis(sa, Vector3.up) * Vector3.forward;
                                side = (side + away * 0.8f).normalized;
                                var bdir = (side + Vector3.up * Mathf.Lerp(0.3f, 1f, (float)rng.NextDouble())).normalized;
                                float blen = Mathf.Lerp(1.5f, 5f, (float)rng.NextDouble()) * (1f - at / height * 0.4f);
                                m.AddTrunk(basePos + dir * at, bdir, blen, radius * 0.32f, 0.3f, 4, tone);
                            }
                        }
                    }
                }

                var mesh = new Mesh { name = "NukeAftermath_Trunks", indexFormat = IndexFormat.UInt32 };
                mesh.SetVertices(m.v);
                mesh.SetNormals(m.n);
                mesh.SetUVs(0, m.uv);
                mesh.SetTriangles(m.t, 0);
                mesh.RecalculateBounds();
                mesh.RecalculateTangents();
                mesh.UploadMeshData(true);
                var go = new GameObject("NukeAftermath_TrunkCell");
                go.transform.localPosition = origin;
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = material;
                mr.shadowCastingMode = ShadowCastingMode.On;
                mr.receiveShadows = true;
                yield return go; // built lazily, one cell per step
            }
        }

        static float Ground(Vector3 global, float fallback, Vector3 origin0)
        {
            var world = global + origin0;
            world.y = fallback + origin0.y;
            if (Physics.Raycast(world + Vector3.up * 80f, Vector3.down, out var hit, 200f, PhysicsLayers.StaticsMask))
                return hit.point.y - origin0.y;
            return fallback;
        }

        class MeshData
        {
            public readonly List<Vector3> v = new List<Vector3>(), n = new List<Vector3>();
            public readonly List<Vector2> uv = new List<Vector2>();
            public readonly List<int> t = new List<int>();

            public void AddTrunk(Vector3 basePos, Vector3 dir, float height, float radius, float taper, int sides, int tone)
            {
                var rot = Quaternion.FromToRotation(Vector3.up, dir);
                float topRadius = radius * taper;
                var top = basePos + dir * height;
                float u0 = (float)tone / Tones, uw = 1f / Tones;
                float vTop = height / 6f;
                int start = v.Count;
                for (int s = 0; s <= sides; s++)
                {
                    float a = s * Mathf.PI * 2f / sides;
                    var o = rot * new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    float u = u0 + uw * (0.05f + 0.9f * s / sides);
                    v.Add(basePos + o * radius); n.Add(o); uv.Add(new Vector2(u, 0f));
                    v.Add(top + o * topRadius); n.Add(o); uv.Add(new Vector2(u, vTop));
                }
                for (int s = 0; s < sides; s++)
                {
                    int i = start + s * 2;
                    t.Add(i); t.Add(i + 1); t.Add(i + 3);
                    t.Add(i); t.Add(i + 3); t.Add(i + 2);
                }
                // Cap the broken top so snapped trunks do not show as hollow tubes.
                if (taper > 0.25f)
                {
                    int c = v.Count;
                    v.Add(top + dir * topRadius * 0.6f); n.Add(dir); uv.Add(new Vector2(u0 + uw * 0.5f, vTop));
                    for (int s = 0; s < sides; s++)
                    {
                        int i = start + s * 2 + 1;
                        t.Add(c); t.Add(i + 2); t.Add(i);
                    }
                }
            }
        }

        internal static Material Material()
        {
            if (material != null) return material;
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Universal Render Pipeline/Simple Lit");
            if (shader == null) { Plugin.Log.LogWarning("No URP Lit shader found; trunks fall back to the default shader."); shader = Shader.Find("Standard"); }
            material = new Material(shader) { name = "NukeAftermath_Charcoal" };
            var tex = BarkAtlas();
            material.SetTexture("_BaseMap", tex);
            material.mainTexture = tex;
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Smoothness", 0.12f);
            material.SetFloat("_Metallic", 0f);
            return material;
        }

        /// <summary>Four bark tones side by side: charcoal, black-brown, grey ash, scorched bark. Vertical streaks and cracks.</summary>
        static Texture2D BarkAtlas()
        {
            const int w = 256, h = 256;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, true) { name = "NukeAftermath_Bark", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear };
            var tones = new[]
            {
                new Color(0.035f, 0.032f, 0.03f),
                new Color(0.07f, 0.05f, 0.038f),
                new Color(0.2f, 0.195f, 0.19f),
                new Color(0.12f, 0.075f, 0.045f),
            };
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int tone = x * Tones / w;
                float lx = (x % (w / Tones)) / (float)(w / Tones);
                float streak = Mathf.PerlinNoise(lx * 9f + tone * 10f, y / (float)h * 1.5f);
                float crack = Mathf.PerlinNoise(lx * 22f + 3f, y / (float)h * 26f + tone * 5f);
                float k = 0.75f + 0.5f * streak;
                if (crack > 0.68f) k *= 0.45f; // charcoal cracks
                var c = tones[tone] * k;
                if (tone == 2 && streak < 0.35f) c = Color.Lerp(c, tones[0], 0.6f); // ash flaking off charcoal
                px[y * w + x] = new Color(c.r, c.g, c.b, 1f);
            }
            tex.SetPixels32(px);
            tex.Apply(true, true);
            return tex;
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
