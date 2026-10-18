using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Talamander.NukeAftermath
{
    /// <summary>
    /// The ground burn: the game's own scorch decal prefab (GameAssets.scorchMarkDecal, a URP DecalProjector with the
    /// "crater" shader graph) with its textures swapped for a procedural burn generated here: a black core, a charred
    /// streaky ring with radial blast streaks and ash patches, and a singed, ragged, feathered edge. A flat normal map
    /// replaces the crater normals, which would read as a bowl stretched over two kilometres.
    /// </summary>
    internal static class BurnDecal
    {
        const int Size = 1024;
        static Texture2D burn, flatNormal;

        internal static GameObject Spawn(Vector3 localGroundZero, float radius, float drawDistance)
        {
            if (GameAssets.i == null || GameAssets.i.scorchMarkDecal == null || Datum.origin == null) return null;
            var go = Object.Instantiate(GameAssets.i.scorchMarkDecal, Datum.origin);
            go.name = "NukeAftermath_Burn";
            // Random spin so two blasts never look stamped from the same texture.
            go.transform.rotation = Quaternion.AngleAxis(Random.Range(0f, 360f), Vector3.up) * Quaternion.LookRotation(Vector3.down);
            go.transform.position = localGroundZero;
            var projector = go.GetComponent<DecalProjector>();
            if (projector == null) { Object.Destroy(go); Plugin.Log.LogWarning("scorchMarkDecal has no DecalProjector."); return null; }
            var mat = new Material(projector.material) { name = "NukeAftermath_BurnMat" };
            int swapped = 0;
            foreach (var prop in mat.GetTexturePropertyNames())
            {
                var tex = mat.GetTexture(prop);
                if (tex == null) continue;
                if (tex.name == "crater_b") { mat.SetTexture(prop, BurnTexture()); swapped++; }
                else if (tex.name == "crater_n") { mat.SetTexture(prop, FlatNormal()); swapped++; }
            }
            if (swapped < 2) Plugin.Log.LogWarning($"Burn: only {swapped} of 2 crater textures found to replace; the burn may look like a stretched crater.");
            projector.material = mat;
            // Depth covers the relief inside the burn: hills well above or below ground zero still get paint.
            // On mountain maps (Ironjaw Sound fjords) the relief is far taller than 0.7 r, so measure it.
            float relief = Relief(localGroundZero, radius);
            float depth = Mathf.Max(500f, radius * 0.7f, 2f * (relief + 100f));
            projector.size = new Vector3(radius * 2f, radius * 2f, depth);
            projector.pivot = Vector3.zero;
            projector.drawDistance = drawDistance;
            projector.fadeFactor = 1f;
            Plugin.Log.LogInfo($"Burn: radius {radius:F0} m, relief {relief:F0} m, depth {depth:F0} m.");
            return go;
        }

        // Largest height difference between ground zero and the ground inside the burn, from a ring of raycasts.
        static float Relief(Vector3 gz, float radius)
        {
            float max = 0f;
            for (int ring = 1; ring <= 4; ring++)
            {
                float r = radius * ring / 4f;
                for (int i = 0; i < 16; i++)
                {
                    float a = (i + ring * 0.5f) * Mathf.PI / 8f;
                    var top = gz + new Vector3(Mathf.Cos(a) * r, 6000f, Mathf.Sin(a) * r);
                    if (Physics.Raycast(top, Vector3.down, out var hit, 12000f, PhysicsLayers.StaticsMask))
                        max = Mathf.Max(max, Mathf.Abs(hit.point.y - gz.y));
                }
            }
            return Mathf.Min(max, 4000f);
        }

        static Texture2D FlatNormal()
        {
            if (flatNormal != null) return flatNormal;
            flatNormal = new Texture2D(4, 4, TextureFormat.RGBA32, false, true) { name = "NukeAftermath_FlatNormal" };
            var px = new Color32[16];
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(128, 128, 255, 255);
            flatNormal.SetPixels32(px);
            flatNormal.Apply(false, true);
            return flatNormal;
        }

        static float Fbm(float x, float y, int octaves)
        {
            float sum = 0f, amp = 0.5f, norm = 0f;
            for (int o = 0; o < octaves; o++)
            {
                sum += amp * Mathf.PerlinNoise(x, y);
                norm += amp;
                x *= 2.03f; y *= 2.03f; amp *= 0.5f;
            }
            return sum / norm;
        }

        static float Smooth(float e0, float e1, float x)
        {
            float t = Mathf.Clamp01((x - e0) / (e1 - e0));
            return t * t * (3f - 2f * t);
        }

        internal static Texture2D BurnTexture()
        {
            if (burn != null) return burn;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            burn = new Texture2D(Size, Size, TextureFormat.RGBA32, true) { name = "NukeAftermath_BurnTex", wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[Size * Size];
            const float ox = 37.1f, oy = 91.7f;
            for (int y = 0; y < Size; y++)
            {
                float v = (y + 0.5f) / Size * 2f - 1f;
                for (int x = 0; x < Size; x++)
                {
                    float u = (x + 0.5f) / Size * 2f - 1f;
                    float r = Mathf.Sqrt(u * u + v * v);
                    if (r > 1f) { px[y * Size + x] = new Color32(0, 0, 0, 0); continue; }
                    float cs = r > 1e-4f ? u / r : 1f, sn = r > 1e-4f ? v / r : 0f;
                    // Seamless angular noise: sample on a circle.
                    float ang = Fbm(cs * 2.2f + ox, sn * 2.2f + oy, 3);
                    float streak = Fbm(cs * 18f + ox, sn * 18f + oy + r * 1.2f, 2); // radial blast streaks
                    float blot = Fbm(u * 5f + oy, v * 5f + ox, 5);
                    float fine = Fbm(u * 38f + 11f, v * 38f + 7f, 3);

                    float edge = 0.8f + (ang - 0.5f) * 0.32f + (blot - 0.5f) * 0.14f; // ragged outer edge
                    float core = 0.2f + (ang - 0.5f) * 0.12f + (blot - 0.5f) * 0.1f;
                    float ring = 0.52f + (blot - 0.5f) * 0.18f;

                    // Colour, from the black core outwards (sRGB values).
                    Color c;
                    float coreBlack = 0.03f + fine * 0.03f;
                    Color black = new Color(coreBlack, coreBlack * 0.95f, coreBlack * 0.9f);
                    float charV = 0.085f + (streak - 0.5f) * 0.11f + (fine - 0.5f) * 0.05f;
                    Color charred = new Color(charV * 1.08f, charV * 0.98f, charV * 0.9f);
                    Color singed = new Color(0.2f, 0.15f, 0.1f) * (0.8f + 0.4f * fine);
                    float ash = Smooth(0.62f, 0.72f, Fbm(u * 9f + 3f, v * 9f + 5f, 3)) * Smooth(0.9f, 0.4f, r / edge);
                    c = Color.Lerp(black, charred, Smooth(core - 0.06f, core + 0.08f, r));
                    c = Color.Lerp(c, singed, Smooth(ring, edge * 0.97f, r) * 0.85f);
                    c = Color.Lerp(c, new Color(0.27f, 0.26f, 0.25f), ash * 0.65f); // grey ash drifts

                    // Coverage: solid inside, then a feathered, patchy edge with unburnt holes.
                    float a = Smooth(edge, edge - 0.22f, r);
                    float patch = Smooth(0.36f, 0.62f, Fbm(u * 7f + 51f, v * 7f + 17f, 4));
                    float outer = Smooth(edge - 0.4f, edge, r);
                    a *= Mathf.Lerp(1f, patch, outer);
                    a *= Mathf.Lerp(1f, 0.6f + 0.4f * streak, outer * 0.8f);
                    a = Mathf.Clamp01(a * 1.05f);
                    px[y * Size + x] = new Color(c.r, c.g, c.b, a);
                }
            }
            burn.SetPixels32(px);
            burn.Apply(true, true);
            Plugin.Log.LogInfo($"Burn texture generated in {sw.ElapsedMilliseconds} ms.");
            return burn;
        }
    }
}
