using System.Collections.Generic;
using UnityEngine;

namespace Talamander.NukeAftermath
{
    /// <summary>
    /// Shared helpers for the runtime-built effects: stock particle materials (looked up by name among the materials
    /// the game already loaded, then cloned), particle system setup, curves and gradients.
    /// Nothing here is copied from the game's files; the materials are referenced at runtime only.
    /// </summary>
    internal static class Fx
    {
        static readonly Dictionary<string, Material> cache = new Dictionary<string, Material>();
        static Texture2D softDot;

        /// <summary>Every stock material the effects use, with the unlit flag and emission scale each one is used with.</summary>
        static readonly (string name, bool unlit, float emission)[] Used =
        {
            ("smoke_background", false, 1f), ("smoke_midground_turbulent", false, 1f), ("steam", false, 1f),
            ("fire_small1", true, 0.4f), ("sparks", true, 1f),
        };

        /// <summary>
        /// Clones the stock materials at session start with one scan of the loaded materials, so the blast frame does
        /// not search all materials once per effect. Materials not loaded yet are left for the first blast to find.
        /// </summary>
        internal static void Prewarm()
        {
            var byName = new Dictionary<string, Material>();
            foreach (var candidate in Resources.FindObjectsOfTypeAll<Material>())
                if (candidate != null && !byName.ContainsKey(candidate.name)) byName[candidate.name] = candidate;
            int n = 0;
            foreach (var u in Used)
                if (byName.TryGetValue(u.name, out var found)) { StockMaterial(u.name, u.unlit, u.emission, found); n++; }
            Plugin.Log.LogInfo($"Effect materials ready: {n} of {Used.Length} stock materials found.");
        }

        /// <summary>
        /// Clone of a stock material by exact name, or a plain URP particle material as a fallback. Clones are cached
        /// per name and emission scale, so a tweak is applied once and never compounds over several blasts.
        /// </summary>
        internal static Material StockMaterial(string name, bool unlit = false, float emission = 1f, Material found = null)
        {
            string key = emission == 1f ? name : name + "#" + emission.ToString("0.###");
            if (cache.TryGetValue(key, out var m) && m != null) return m;
            if (found == null)
                foreach (var candidate in Resources.FindObjectsOfTypeAll<Material>())
                {
                    if (candidate != null && candidate.name == name) { found = candidate; break; }
                }
            if (found != null)
            {
                m = new Material(found) { name = "NukeAftermath_" + name };
                if (emission != 1f && m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", m.GetColor("_EmissionColor") * emission);
                if (!unlit)
                {
                    // Our smoke particles are hundreds of metres across: blend them softly into the ground over
                    // tens of metres and fade them out as the camera flies into them, so they never cut a hard
                    // edge across the terrain or fill the screen with a flat grey sheet.
                    if (m.HasProperty("_SoftParticleFadeParams")) m.SetVector("_SoftParticleFadeParams", new Vector4(0f, 1f / 60f, 0f, 0f));
                    if (m.HasProperty("_CameraFadeParams")) m.SetVector("_CameraFadeParams", new Vector4(20f, 1f / 160f, 0f, 0f));
                    m.EnableKeyword("_SOFTPARTICLES_ON");
                    m.EnableKeyword("_FADING_ON");
                }
            }
            else
            {
                Plugin.Log.LogWarning($"Stock material '{name}' not loaded; using a plain particle material.");
                var shader = Shader.Find(unlit ? "Universal Render Pipeline/Particles/Unlit" : "Universal Render Pipeline/Particles/Lit")
                             ?? Shader.Find("Universal Render Pipeline/Particles/Unlit");
                m = new Material(shader) { name = "NukeAftermath_fallback_" + name };
                m.SetTexture("_BaseMap", SoftDot());
                m.SetFloat("_Surface", 1f);
                m.renderQueue = 3000;
            }
            cache[key] = m;
            return m;
        }

        internal static void ClearCache()
        {
            foreach (var m in cache.Values) if (m != null) Object.Destroy(m);
            cache.Clear();
        }

        static Texture2D SoftDot()
        {
            if (softDot != null) return softDot;
            const int n = 64;
            softDot = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "NukeAftermath_SoftDot", wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                px[y * n + x] = new Color32(255, 255, 255, (byte)(a * a * 255));
            }
            softDot.SetPixels32(px);
            softDot.Apply(false, true);
            return softDot;
        }

        /// <summary>A stopped particle system on a new child object, simulated in the floating-origin frame.</summary>
        internal static ParticleSystem NewSystem(string name, Transform parent, Vector3 localPos, Material material, int maxParticles)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.maxParticles = Mathf.Max(1, maxParticles);
            // Datum.origin moves on floating-origin shifts; custom space keeps the particles glued to the world.
            main.simulationSpace = ParticleSystemSimulationSpace.Custom;
            main.customSimulationSpace = Datum.origin != null ? Datum.origin : parent;
            main.scalingMode = ParticleSystemScalingMode.Local;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;
            var shape = ps.shape;
            shape.enabled = false;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = material;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.sortMode = ParticleSystemSortMode.Distance;
            r.minParticleSize = 0f;
            r.maxParticleSize = 100f; // screen fraction; large clouds must not be clamped
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return ps;
        }

        internal static AnimationCurve Curve(params float[] timeValuePairs)
        {
            var keys = new Keyframe[timeValuePairs.Length / 2];
            for (int i = 0; i < keys.Length; i++) keys[i] = new Keyframe(timeValuePairs[i * 2], timeValuePairs[i * 2 + 1]);
            var c = new AnimationCurve(keys);
            for (int i = 0; i < keys.Length; i++) c.SmoothTangents(i, 0f);
            return c;
        }

        /// <summary>Gradient from (time, r, g, b) colour keys and (time, alpha) alpha keys.</summary>
        internal static Gradient Gradient(float[] colorKeys, float[] alphaKeys)
        {
            var ck = new GradientColorKey[colorKeys.Length / 4];
            for (int i = 0; i < ck.Length; i++)
                ck[i] = new GradientColorKey(new Color(colorKeys[i * 4 + 1], colorKeys[i * 4 + 2], colorKeys[i * 4 + 3]), colorKeys[i * 4]);
            var ak = new GradientAlphaKey[alphaKeys.Length / 2];
            for (int i = 0; i < ak.Length; i++) ak[i] = new GradientAlphaKey(alphaKeys[i * 2 + 1], alphaKeys[i * 2]);
            var g = new UnityEngine.Gradient();
            g.SetKeys(ck, ak);
            return g;
        }

        /// <summary>Scales the values (not the times) of every key in place.</summary>
        internal static void ScaleValues(AnimationCurve c, float k)
        {
            if (c == null) return;
            var keys = c.keys;
            for (int i = 0; i < keys.Length; i++) { keys[i].value *= k; keys[i].inTangent *= k; keys[i].outTangent *= k; }
            c.keys = keys;
        }

        /// <summary>Scales the times of every key in place (a slower curve).</summary>
        internal static void ScaleTimes(AnimationCurve c, float k)
        {
            if (c == null || k <= 0f) return;
            var keys = c.keys;
            for (int i = 0; i < keys.Length; i++) { keys[i].time *= k; keys[i].inTangent /= k; keys[i].outTangent /= k; }
            c.keys = keys;
        }

        internal static float GroundY(Vector3 localPos, float fallback)
        {
            if (Physics.Raycast(localPos + Vector3.up * 3000f, Vector3.down, out var hit, 8000f, PhysicsLayers.StaticsMask))
                return hit.point.y;
            return fallback;
        }

        internal static Vector3 Wind(Vector3 localPos)
        {
            var level = NetworkSceneSingleton<LevelInfo>.i;
            if (level == null) return Vector3.zero;
            var w = level.GetWind(localPos.ToGlobalPosition());
            w.y = 0f;
            return w;
        }
    }
}
