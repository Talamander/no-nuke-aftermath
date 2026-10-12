using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace Talamander.NukeAftermath
{
    /// <summary>
    /// Changes the visual settings of the stock explosion prefab instance (MushroomCloud, its fireball light and lens
    /// flare) right after Warhead.Detonate instantiates it and before its Start runs. Instantiate gives every instance
    /// its own copies of the serialized curves, so the prefab and other explosions are not touched.
    ///
    /// Nothing here feeds back into the game: MushroomCloud is a purely visual component. The Shockwave component,
    /// which does damage, is never touched.
    /// </summary>
    internal static class StockBlastTweaks
    {
        // MushroomCloud instance id -> lifetime scale to apply in its Start postfix.
        static readonly Dictionary<int, float> pendingLifetime = new Dictionary<int, float>();

        internal static MushroomCloud FindFreshCloud(Vector3 localPos)
        {
            MushroomCloud best = null;
            float bestD = 2000f * 2000f;
            foreach (var mc in Object.FindObjectsOfType<MushroomCloud>())
            {
                // Start has not run yet: blastScale is still 0.
                if (Traverse.Create(mc).Field<float>("blastScale").Value != 0f) continue;
                float d = (mc.transform.position - localPos).sqrMagnitude;
                if (d < bestD) { bestD = d; best = mc; }
            }
            if (best == null) Plugin.Log.LogWarning("No fresh stock MushroomCloud found near the detonation; stock cloud left as is.");
            return best;
        }

        internal static void Apply(MushroomCloud mc, Plugin p)
        {
            var t = Traverse.Create(mc);
            float s = p.BiggerCloud.Value ? p.CloudScale.Value : 1f;
            float life = p.BiggerCloud.Value ? p.CloudLifetimeScale.Value : 1f;

            if (p.BiggerCloud.Value)
            {
                // blastScale = (yield / 1e6)^(1/3) drives the stem spread, base cloud height, cloud lift and the
                // ground-burst factor. Scaling the yield by s^3 scales all of those by s.
                mc.yield *= s * s * s;

                var cloud = t.Field("cloud").GetValue();
                if (cloud != null)
                {
                    var c = Traverse.Create(cloud);
                    Fx.ScaleValues(c.Field<AnimationCurve>("horizontalSizeOverTime").Value, s);
                    Fx.ScaleValues(c.Field<AnimationCurve>("riseRateOverTime").Value, Mathf.Sqrt(s));
                    Fx.ScaleValues(c.Field<AnimationCurve>("particleRateOverTime").Value, 1.6f);
                    // The cap glows for longer.
                    Fx.ScaleTimes(OptionalValue<AnimationCurve>(c.Field("emitOverTime")), 1.6f);
                    Fx.ScaleTimes(OptionalValue<AnimationCurve>(c.Field("emitRoughnessOverTime")), 1.6f);
                }

                var fireball = OptionalValue<object>(t.Field("fireball"));
                if (fireball != null)
                {
                    var f = Traverse.Create(fireball);
                    var size = f.Field<AnimationCurve>("sizeOverTime").Value;
                    var emit = f.Field<AnimationCurve>("emitOverTime").Value;
                    if (size != null && emit != null && size.length > 0 && emit.length > 0)
                    {
                        // Stock: grows to its full size in 0.4 s and is gone. Bigger, and it hangs for 1.4 s.
                        float full = size.Evaluate(size.keys[size.length - 1].time) * s;
                        float peak = emit.keys[0].value;
                        f.Field<AnimationCurve>("sizeOverTime").Value = Fx.Curve(0f, 0f, 0.2f, full * 0.7f, 0.6f, full, 1.4f, full * 1.12f);
                        f.Field<AnimationCurve>("emitOverTime").Value = Fx.Curve(0f, peak * 1.4f, 0.3f, peak * 0.9f, 0.8f, peak * 0.3f, 1.4f, 0f);
                    }
                }

                var baseCloud = OptionalValue<object>(t.Field("baseCloud"));
                if (baseCloud != null)
                {
                    var b = Traverse.Create(baseCloud);
                    Fx.ScaleValues(OptionalValue<AnimationCurve>(b.Field("radiusOverTime")), s * 1.25f);
                    Fx.ScaleValues(OptionalValue<AnimationCurve>(b.Field("startSizeOverTime")), s);
                    Fx.ScaleValues(OptionalValue<AnimationCurve>(b.Field("rateOverTime")), 1.5f);
                }

                var stem = OptionalValue<object>(t.Field("stem"));
                if (stem != null)
                {
                    var st = Traverse.Create(stem);
                    Fx.ScaleValues(OptionalValue<AnimationCurve>(st.Field("startSizeOverTime")), s);
                    Fx.ScaleValues(OptionalValue<AnimationCurve>(st.Field("rateOverTime")), 1.4f);
                }

                var lens = t.Field<AnimationCurve>("lensFlareIntensity").Value;
                if (lens != null) { Fx.ScaleValues(lens, 2f); Fx.ScaleTimes(lens, 2f); }

                // Keep the explosion object long enough for the longer cloud.
                var del = mc.GetComponent<TimedDeletion>();
                if (del != null)
                {
                    var d = Traverse.Create(del).Field<float>("deleteDelay");
                    float before = d.Value;
                    d.Value = Mathf.Max(before, before * life);
                    Plugin.Log.LogInfo($"Stock cloud: size x{s:F2}, lifetime x{life:F2}, deletion {before:F0} s -> {d.Value:F0} s.");
                }
                if (life > 1f) pendingLifetime[mc.GetInstanceID()] = life;
            }

            if (p.Flash.Value)
            {
                foreach (var anim in mc.GetComponentsInChildren<LightAnimation>(true))
                {
                    var a = Traverse.Create(anim);
                    a.Field<float>("lightIntensity").Value *= 1.8f;
                    Fx.ScaleTimes(a.Field<AnimationCurve>("intensityCurve").Value, 2.2f);
                }
            }
        }

        /// <summary>Stretches the stock cloud timelines once MushroomCloud.Start has read the particle durations.</summary>
        internal static void MushroomStartPostfix(MushroomCloud __instance)
        {
            try
            {
                if (__instance == null || !pendingLifetime.TryGetValue(__instance.GetInstanceID(), out float life)) return;
                pendingLifetime.Remove(__instance.GetInstanceID());
                var t = Traverse.Create(__instance);
                Stretch(t.Field("cloud").GetValue(), "cloudParticles", life);
                // The base cloud hugs the ground: kept at its stock lifetime, because stretched it lingers as a flat
                // sheet over the terrain. Both low systems get a wide soft-particle fade for the same reason.
                Soften(OptionalValue<object>(t.Field("baseCloud")), "system");
                var stem = OptionalValue<object>(t.Field("stem"));
                Stretch(stem, "system", life);
                Soften(stem, "system");
            }
            catch (System.Exception e) { Plugin.Log.LogError(e); }
        }

        static void Stretch(object part, string systemField, float life)
        {
            if (part == null) return;
            var tr = Traverse.Create(part);
            var lifetime = tr.Field<float>("lifetime");
            lifetime.Value *= life;
            var ps = tr.Field<ParticleSystem>(systemField).Value;
            if (ps == null) return;
            // A non-looping system stops emitting at its duration; lengthen it to match the stretched timeline.
            ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear); // called at time 0: nothing to lose
            var main = ps.main;
            main.duration = lifetime.Value;
            ps.Play(false);
        }

        static void Soften(object part, string systemField)
        {
            if (part == null) return;
            var ps = Traverse.Create(part).Field<ParticleSystem>(systemField).Value;
            var r = ps != null ? ps.GetComponent<ParticleSystemRenderer>() : null;
            if (r == null || r.sharedMaterial == null) return;
            var m = r.material; // per-instance copy; the shared stock material is left alone
            if (m.HasProperty("_SoftParticleFadeParams")) m.SetVector("_SoftParticleFadeParams", new Vector4(0f, 1f / 150f, 0f, 0f));
            m.EnableKeyword("_SOFTPARTICLES_ON");
        }

        static T OptionalValue<T>(Traverse optional) where T : class
        {
            if (optional == null) return null;
            var boxed = optional.GetValue();
            if (boxed == null) return null;
            var o = Traverse.Create(boxed);
            if (!o.Field<bool>("enabled").Value) return null;
            return o.Field("value").GetValue() as T;
        }
    }
}
