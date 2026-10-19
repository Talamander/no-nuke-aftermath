using System.Collections.Generic;
using UnityEngine;

namespace Talamander.NukeAftermath
{
    /// <summary>
    /// Ground fires, a smoke column from ground zero and drifting embers for one blast. Fires sit on the tree clumps
    /// the blast removed when there are any (so they burn where the forest was), otherwise on open ground in a ring
    /// around ground zero. They catch a few seconds after the flash, burn for minutes and die out one by one.
    ///
    /// Each fire is two particle systems (flames and smoke) driven by manual Emit calls; all of them share the
    /// blast's ParticleBudget. No lights are added per fire: URP here draws at most four additional lights.
    /// </summary>
    internal class GroundFires
    {
        class Fire
        {
            public Vector3 pos, front; // front: unit vector along the fire line
            public float start, end, size, flameCarry, smokeCarry;
        }

        readonly List<Fire> fires = new List<Fire>();
        readonly ParticleSystem flames, smoke, column, embers;
        readonly Vector3 groundZero;
        readonly float clearRadius, budget, startTime, burnTime;
        readonly Plugin p;
        float columnCarry, emberCarry;
        internal bool Done { get; private set; }

        internal GroundFires(Transform parent, Plugin plugin, Vector3 groundZero, float blastRadius, float clearRadius, float budget,
            List<Vector3> clearedClumps, PavedAreas paved, float now)
        {
            p = plugin;
            this.groundZero = groundZero;
            this.clearRadius = clearRadius;
            this.budget = budget;
            startTime = now;
            burnTime = 60f * p.FireMinutes.Value;

            if (p.GroundFires.Value && p.MaxFires.Value > 0)
            {
                PlaceFires(clearedClumps, blastRadius, paved);
                var flameMat = Fx.StockMaterial("fire_small1", true, 0.4f); // stock flames dimmed, they glowed neon
                flames = Fx.NewSystem("NukeAftermath_Flames", parent, Vector3.zero, flameMat, Count(1800));
                SetupFlames(flames);
                smoke = Fx.NewSystem("NukeAftermath_FireSmoke", parent, Vector3.zero, Fx.StockMaterial("smoke_background"), Count(4000));
                SetupSmoke(smoke, new float[] { 0f, 0.15f, 0.14f, 0.13f, 0.4f, 0.29f, 0.27f, 0.25f, 1f, 0.5f, 0.48f, 0.46f },
                    new float[] { 0f, 0f, 0.12f, 0.46f, 0.45f, 0.34f, 1f, 0f }, 2.2f);
            }
            if (p.SmokeColumn.Value)
            {
                column = Fx.NewSystem("NukeAftermath_SmokeColumn", parent, Vector3.zero, Fx.StockMaterial("smoke_background"), Count(900));
                SetupSmoke(column, new float[] { 0f, 0.18f, 0.17f, 0.16f, 0.5f, 0.32f, 0.31f, 0.3f, 1f, 0.5f, 0.49f, 0.48f },
                    new float[] { 0f, 0f, 0.06f, 0.6f, 0.6f, 0.4f, 1f, 0f }, 3.5f);
            }
            if (p.Embers.Value)
            {
                embers = Fx.NewSystem("NukeAftermath_Embers", parent, Vector3.zero, Fx.StockMaterial("sparks", true), Count(1500));
                SetupEmbers(embers);
            }
            Plugin.Log.LogInfo($"Ground fires: {fires.Count} fires for {burnTime / 60f:F1} min" + (p.SmokeColumn.Value ? ", smoke column" : "") + (p.Embers.Value ? ", embers." : "."));
        }

        int Count(int n) => Mathf.Max(1, Mathf.RoundToInt(n * budget));

        void PlaceFires(List<Vector3> clumps, float blastRadius, PavedAreas paved)
        {
            int max = p.MaxFires.Value;
            var rng = new System.Random(Mathf.RoundToInt(groundZero.x * 7f + groundZero.z * 13f));
            var chosen = new List<Vector3>();
            if (clumps != null && clumps.Count > 0)
            {
                // Spread fires out: one per angular sector, each at a random cleared clump in that sector.
                int sectors = Mathf.Max(6, max);
                var bySector = new List<Vector3>[sectors];
                foreach (var c in clumps)
                {
                    float dx = c.x - groundZero.x, dz = c.z - groundZero.z;
                    float d = Mathf.Sqrt(dx * dx + dz * dz);
                    if (d < clearRadius * 0.12f) continue; // nothing left to burn right under the fireball
                    int s = Mathf.Clamp(Mathf.FloorToInt((Mathf.Atan2(dz, dx) / (Mathf.PI * 2f) + 0.5f) * sectors), 0, sectors - 1);
                    (bySector[s] ??= new List<Vector3>()).Add(c);
                }
                for (int pass = 0; pass < 3 && chosen.Count < max; pass++)
                    for (int s = 0; s < sectors && chosen.Count < max; s++)
                        if (bySector[s] != null && bySector[s].Count > pass)
                            chosen.Add(bySector[s][rng.Next(bySector[s].Count)]);
            }
            // Open ground (fields burn too): top up in a ring around ground zero.
            int open = Mathf.Max(max / 3, max - chosen.Count);
            for (int i = 0; i < open && chosen.Count < max + max / 3; i++)
            {
                float ang = (float)rng.NextDouble() * Mathf.PI * 2f;
                float r = Mathf.Lerp(0.25f, 0.95f, Mathf.Sqrt((float)rng.NextDouble())) * blastRadius * 0.6f;
                var g = groundZero + new Vector3(Mathf.Cos(ang) * r, 0f, Mathf.Sin(ang) * r);
                var local = g + Datum.origin.position; // datum space to world
                g.y = Fx.GroundY(local, groundZero.y + Datum.origin.position.y) - Datum.origin.position.y;
                chosen.Add(g);
            }
            // Fire lines are about 90 m long and their smoke hangs low: keep them well off runways and taxiways.
            if (paved != null && paved.Any) chosen.RemoveAll(c => paved.Distance(c) < 70f);
            foreach (var c in chosen)
            {
                var away = new Vector3(c.x - groundZero.x, 0f, c.z - groundZero.z);
                if (away.sqrMagnitude < 1f) away = Vector3.forward;
                // Fire lines run roughly across the blast direction, with scatter.
                var front = Quaternion.Euler(0f, 90f + Mathf.Lerp(-35f, 35f, (float)rng.NextDouble()), 0f) * away.normalized;
                fires.Add(new Fire
                {
                    pos = c,
                    front = front,
                    start = startTime + 1.5f + (float)rng.NextDouble() * 8f,
                    end = startTime + burnTime * Mathf.Lerp(0.55f, 1f, (float)rng.NextDouble()),
                    size = Mathf.Lerp(0.7f, 1.4f, (float)rng.NextDouble()),
                });
            }
        }

        static void SetupFlames(ParticleSystem ps)
        {
            var main = ps.main;
            main.startColor = Color.white;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = Fx.Gradient(new float[] { 0f, 1f, 1f, 1f, 1f, 1f, 0.8f, 0.6f },
                new float[] { 0f, 0f, 0.15f, 1f, 0.6f, 0.7f, 1f, 0f });
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, Fx.Curve(0f, 0.5f, 0.4f, 1f, 1f, 0.6f));
            var rot = ps.rotationOverLifetime;
            rot.enabled = true;
            rot.z = new ParticleSystem.MinMaxCurve(-1f, 1f);
            ps.Play();
        }

        static void SetupSmoke(ParticleSystem ps, float[] colors, float[] alphas, float grow)
        {
            var main = ps.main;
            main.startColor = Color.white;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = Fx.Gradient(colors, alphas);
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, Fx.Curve(0f, 1f / grow, 0.5f, 0.7f, 1f, 1f));
            var limit = ps.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.drag = 0.04f;
            limit.multiplyDragByParticleSize = false;
            limit.multiplyDragByParticleVelocity = false;
            limit.limit = 10000f;
            var rot = ps.rotationOverLifetime;
            rot.enabled = true;
            rot.z = new ParticleSystem.MinMaxCurve(-0.1f, 0.1f);
            ps.Play();
        }

        static void SetupEmbers(ParticleSystem ps)
        {
            var main = ps.main;
            main.startColor = new Color(1f, 0.55f, 0.18f, 1f);
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = Fx.Gradient(new float[] { 0f, 1f, 0.8f, 0.5f, 1f, 1f, 0.3f, 0.1f },
                new float[] { 0f, 0f, 0.1f, 1f, 0.7f, 0.8f, 1f, 0f });
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 3f;
            noise.frequency = 0.08f;
            noise.scrollSpeed = 0.3f;
            ps.Play();
        }

        internal void Update(float time, float dt)
        {
            var origin = Datum.origin.position;
            var wind = Fx.Wind(groundZero + origin);
            float life = time - startTime;
            int burning = 0;
            foreach (var f in fires)
            {
                if (time < f.start || time > f.end) continue;
                burning++;
                float age = time - f.start, left = f.end - time;
                // Catch quickly, roar, then die down over the last part of the burn.
                float env = Mathf.Clamp01(age / 12f) * Mathf.Clamp01(left / (0.35f * (f.end - f.start)));
                if (flames != null)
                {
                    // A ragged line of small flames rather than one big blob.
                    f.flameCarry += dt * 18f * budget * env * f.size;
                    while (f.flameCarry >= 1f)
                    {
                        f.flameCarry -= 1f;
                        float along = (Random.value + Random.value - 1f) * 45f * f.size;
                        var side = Vector3.Cross(Vector3.up, f.front) * Random.Range(-6f, 6f);
                        var pos = f.pos + f.front * along + side + Vector3.up * Random.Range(0.5f, 3f);
                        BlastFx.Emit(flames, pos, Vector3.up * Random.Range(3f, 6f) + wind * 0.25f, Random.Range(6f, 14f) * Mathf.Lerp(0.5f, 1f, env),
                            Random.Range(0.9f, 1.8f), 1f);
                    }
                }
                if (smoke != null)
                {
                    // Dense enough that the puffs overlap into one continuous plume.
                    f.smokeCarry += dt * 2.5f * budget * Mathf.Max(env, 0.2f) * f.size;
                    while (f.smokeCarry >= 1f)
                    {
                        f.smokeCarry -= 1f;
                        float along = (Random.value + Random.value - 1f) * 35f * f.size;
                        var pos = f.pos + f.front * along + Vector3.up * Random.Range(6f, 16f);
                        var vel = Vector3.up * Random.Range(7f, 11f) * Mathf.Lerp(0.6f, 1f, env) + wind * 0.9f;
                        // Final size; the puff starts at a fifth of it (size over lifetime).
                        BlastFx.Emit(smoke, pos, vel, Random.Range(150f, 230f) * f.size, Random.Range(50f, 75f), Mathf.Lerp(1.2f, 0.8f, env));
                    }
                }
            }

            // Smoke column from ground zero: starts as the stock stem thins, fades over the burn time.
            if (column != null && life > 20f && life < burnTime)
            {
                float env = Mathf.Clamp01((life - 20f) / 20f) * Mathf.Clamp01((burnTime - life) / (burnTime * 0.4f));
                columnCarry += dt * 1.8f * budget * env;
                while (columnCarry >= 1f)
                {
                    columnCarry -= 1f;
                    var off = Random.insideUnitCircle * clearRadius * 0.08f;
                    var pos = groundZero + new Vector3(off.x, Random.Range(20f, 60f), off.y);
                    BlastFx.Emit(column, pos, Vector3.up * Random.Range(14f, 20f) + wind * 0.75f, Random.Range(380f, 520f),
                        Random.Range(100f, 140f), Random.Range(0.85f, 1.1f));
                }
            }

            // Embers over the burnt ground for the first three minutes.
            if (embers != null && life > 2f && life < 180f)
            {
                float env = Mathf.Clamp01((180f - life) / 90f);
                emberCarry += dt * 110f * budget * env;
                while (emberCarry >= 1f)
                {
                    emberCarry -= 1f;
                    var off = Random.insideUnitCircle * clearRadius * 0.85f;
                    var pos = groundZero + new Vector3(off.x, Random.Range(1f, 25f), off.y);
                    var vel = Vector3.up * Random.Range(2f, 9f) + wind * 0.9f + new Vector3(Random.Range(-2f, 2f), 0f, Random.Range(-2f, 2f));
                    BlastFx.Emit(embers, pos, vel, Random.Range(0.45f, 1.3f), Random.Range(2.5f, 5f), Random.Range(0.6f, 1f));
                }
            }

            Done = life > burnTime + 130f;
            _ = burning;
        }

        internal void Dispose() { }
    }
}
