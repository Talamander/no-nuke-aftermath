using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace Talamander.NukeAftermath
{
    /// <summary>
    /// The runtime-built effects of one nuclear blast. Lives on a root object under Datum.origin; every particle system
    /// simulates in Datum.origin space, so emit positions are the game's global (map) coordinates and stay put through
    /// floating-origin shifts.
    ///
    /// Timeline (seconds after detonation, game time, so it pauses with the game):
    ///   0      flash light pulse and screen flash
    ///   0.2    base surge: a ring of dust racing out along the ground
    ///   0.5    condensation ring around the fireball
    ///   d/340  extra cockpit rumble when the blast wave reaches the camera
    ///   2+     ground fires catch (see GroundFires), embers
    ///   20+    lingering cloud: puffs shed from the stock cap and stem that drift with the wind for minutes
    ///   25+    smoke column from ground zero
    /// </summary>
    internal class BlastFx : MonoBehaviour
    {
        Plugin p;
        Vector3 burst;      // datum (global) coordinates
        Vector3 groundZero; // datum coordinates
        bool groundHit;
        float blastRadius, clearRadius, budget, time;
        MushroomCloud cloud;
        Transform cloudMesh;
        AnimationCurve cloudSize;

        Light flashLight;
        float flashPeak;
        bool flashOnScreen;

        ParticleSystem surge, surgeSkirt, ring, capRemnant, stemRemnant;
        float nextCapPuff, nextStemPuff, skirtCarry;
        bool surgeDone, ringDone;

        float shakeAt = -1f, shakeAmp;
        bool shakeArmed;

        GroundFires fires;
        float lingerEnd;

        readonly Queue<System.Action> pendingBuilds = new Queue<System.Action>();

        internal void Init(Plugin plugin, Vector3 localBurst, Vector3 localGroundZero, bool hasGround, float blastRadius,
            float clearRadius, MushroomCloud stockCloud)
        {
            p = plugin;
            burst = localBurst.ToGlobalPosition().AsVector3();
            groundZero = localGroundZero.ToGlobalPosition().AsVector3();
            groundHit = hasGround;
            this.blastRadius = blastRadius;
            this.clearRadius = clearRadius;
            budget = p.ParticleBudget.Value;
            cloud = stockCloud;
            if (cloud != null)
            {
                var c = Traverse.Create(cloud).Field("cloud").GetValue();
                if (c != null)
                {
                    var mr = Traverse.Create(c).Field<MeshRenderer>("meshRenderer").Value;
                    cloudMesh = mr != null ? mr.transform : null;
                    cloudSize = Traverse.Create(c).Field<AnimationCurve>("horizontalSizeOverTime").Value;
                }
            }
            lingerEnd = 60f * p.LingeringCloudMinutes.Value;

            if (p.Flash.Value) StartFlash(localBurst);
            // The particle systems are built one per frame over the next frames, not on the blast frame, which is
            // already the game's heaviest. Their first particles are due at 0.2 s or later.
            if (p.BaseSurge.Value && groundHit) pendingBuilds.Enqueue(BuildSurge);
            if (p.CondensationRing.Value) pendingBuilds.Enqueue(BuildRing);
            if (p.LingeringCloud.Value && cloudMesh != null) pendingBuilds.Enqueue(BuildRemnants);
            if (p.CameraShake.Value && !p.InVR) shakeArmed = true;
        }

        internal void StartFires(List<Vector3> clearedClumps)
        {
            if (!groundHit) return;
            if (!p.GroundFires.Value && !p.SmokeColumn.Value && !p.Embers.Value) return;
            if (fires != null) return;
            fires = new GroundFires(transform, p, groundZero, blastRadius, clearRadius, budget, clearedClumps, time);
        }

        // ---- flash ----

        void StartFlash(Vector3 localBurst)
        {
            var go = new GameObject("NukeAftermath_FlashLight");
            go.transform.SetParent(transform, false);
            go.transform.position = localBurst + Vector3.up * blastRadius * 0.25f;
            flashLight = go.AddComponent<Light>();
            flashLight.type = LightType.Point;
            flashLight.range = blastRadius * 9f;
            flashLight.color = new Color(1f, 0.9f, 0.76f);
            flashLight.shadows = LightShadows.None;
            flashLight.renderMode = LightRenderMode.ForcePixel;
            // Scaled with the blast area; 1.5 kt gives 2.5e7 (the stock fireball light is 5e7 with a 4 s tail).
            flashPeak = 2.5e7f * p.FlashBrightness.Value * Mathf.Pow(blastRadius / 1487f, 2f);
            flashLight.intensity = flashPeak;
            flashOnScreen = p.ScreenFlash.Value && !p.InVR;
        }

        void UpdateFlash()
        {
            if (flashLight == null) return;
            // Sharp peak, fast fall, then a warm tail that lights the cloud and ground for a few seconds.
            float k = time < 0.05f ? time / 0.05f : 0.75f * Mathf.Exp(-(time - 0.05f) / 0.45f) + 0.25f * Mathf.Exp(-(time - 0.05f) / 3.5f);
            flashLight.intensity = flashPeak * k;
            flashLight.color = Color.Lerp(new Color(1f, 0.95f, 0.88f), new Color(1f, 0.55f, 0.22f), Mathf.Clamp01(time / 4f));
            if (time > 14f) { Destroy(flashLight.gameObject); flashLight = null; }
        }

        void OnGUI()
        {
            if (!flashOnScreen || time > 2f || Event.current.type != EventType.Repaint) return;
            var cam = Camera.main;
            if (cam == null) return;
            var camGlobal = cam.transform.position.ToGlobalPosition().AsVector3();
            var to = burst - camGlobal;
            float d = Mathf.Max(1f, to.magnitude);
            float facing = Mathf.Clamp01((Vector3.Dot(cam.transform.forward, to / d) + 0.2f) / 1.2f);
            float near = Mathf.Clamp01(blastRadius * 10f / d);
            float env = time < 0.04f ? time / 0.04f : Mathf.Exp(-(time - 0.04f) / 0.32f);
            float a = 0.85f * env * near * Mathf.Lerp(0.3f, 1f, facing);
            if (a < 0.004f) return;
            var old = GUI.color;
            GUI.color = new Color(1f, 0.97f, 0.9f, a);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = old;
        }

        // ---- base surge ----

        void BuildSurge()
        {
            var mat = Fx.StockMaterial("smoke_midground_turbulent");
            surge = Fx.NewSystem("NukeAftermath_BaseSurge", transform, Vector3.zero, mat, Count(1200));
            SetupDust(surge, new Color(0.66f, 0.6f, 0.5f), 3.2f, 0.11f);
            surgeSkirt = Fx.NewSystem("NukeAftermath_DustSkirt", transform, Vector3.zero, mat, Count(700));
            SetupDust(surgeSkirt, new Color(0.47f, 0.42f, 0.36f), 2.6f, 0.05f);
        }

        static void SetupDust(ParticleSystem ps, Color tint, float grow, float drag)
        {
            var main = ps.main;
            main.startColor = tint;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = Fx.Gradient(new float[] { 0f, 1f, 1f, 1f, 1f, 0.92f, 0.9f, 0.88f },
                new float[] { 0f, 0f, 0.03f, 0.62f, 0.35f, 0.38f, 0.75f, 0.1f, 1f, 0f });
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, Fx.Curve(0f, 0.35f, 0.25f, 0.7f, 1f, 1f));
            size.separateAxes = false;
            main.startSizeMultiplier = 1f;
            var limit = ps.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.drag = drag;
            limit.multiplyDragByParticleSize = false;
            limit.multiplyDragByParticleVelocity = false;
            limit.limit = 10000f;
            var rot = ps.rotationOverLifetime;
            rot.enabled = true;
            rot.z = new ParticleSystem.MinMaxCurve(-0.08f, 0.08f);
            ps.Play();
            _ = grow;
        }

        void UpdateSurge(float dt)
        {
            if (surge == null) return;
            var wind = Fx.Wind(transform.position) * 0.4f;
            if (!surgeDone && time > 0.2f)
            {
                surgeDone = true;
                // Ring of dust racing outwards along the ground; slowed by drag so it stalls near the blast radius.
                int n = Count(1000);
                for (int i = 0; i < n; i++)
                {
                    float ang = Random.value * Mathf.PI * 2f;
                    var dir = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
                    float r0 = blastRadius * Random.Range(0.08f, 0.2f);
                    var pos = groundZero + dir * r0 + Vector3.up * Random.Range(10f, 45f);
                    var vel = dir * Random.Range(130f, 210f) + Vector3.up * Random.Range(2f, 14f) + wind;
                    // Gone by about half a minute: as the ring stalls and thins out, late puffs read as a row of separate blobs.
                    Emit(surge, pos, vel, Random.Range(170f, 300f) * Scale, Random.Range(20f, 32f), Random.Range(0.85f, 1.1f));
                }
            }
            // Billowing skirt around the base for the first seconds.
            if (time < 7f && surgeSkirt != null)
            {
                skirtCarry += dt * 90f * budget;
                while (skirtCarry >= 1f)
                {
                    skirtCarry -= 1f;
                    float ang = Random.value * Mathf.PI * 2f;
                    var dir = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
                    var pos = groundZero + dir * blastRadius * Random.Range(0.05f, 0.25f) + Vector3.up * Random.Range(10f, 50f);
                    var vel = dir * Random.Range(20f, 60f) + Vector3.up * Random.Range(3f, 10f) + wind;
                    Emit(surgeSkirt, pos, vel, Random.Range(200f, 320f) * Scale, Random.Range(25f, 40f), Random.Range(0.75f, 1.05f));
                }
            }
        }

        // ---- condensation ring ----

        void BuildRing()
        {
            var mat = Fx.StockMaterial("steam");
            ring = Fx.NewSystem("NukeAftermath_CondensationRing", transform, Vector3.zero, mat, Count(1000));
            var main = ring.main;
            main.startColor = Color.white;
            var col = ring.colorOverLifetime;
            col.enabled = true;
            col.color = Fx.Gradient(new float[] { 0f, 1f, 1f, 1f, 1f, 0.95f, 0.95f, 0.97f },
                new float[] { 0f, 0f, 0.12f, 0.26f, 0.5f, 0.16f, 1f, 0f });
            var size = ring.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, Fx.Curve(0f, 0.5f, 1f, 1f));
            var limit = ring.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.drag = 0.9f;
            limit.multiplyDragByParticleSize = false;
            limit.multiplyDragByParticleVelocity = false;
            limit.limit = 10000f;
            ring.Play();
        }

        void UpdateRing()
        {
            if (ring == null || ringDone || time < 0.45f) return;
            ringDone = true;
            int n = Count(1400);
            float h = burst.y + blastRadius * 0.06f;
            for (int i = 0; i < n; i++)
            {
                float ang = Random.value * Mathf.PI * 2f;
                var dir = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
                bool dome = false; // a Wilson dome made of puffs read as dots; ring only
                Vector3 pos, vel;
                if (dome)
                {
                    // A thin shell over the fireball, the Wilson cloud.
                    float el = Random.Range(10f, 70f) * Mathf.Deg2Rad;
                    var d3 = new Vector3(dir.x * Mathf.Cos(el), Mathf.Sin(el), dir.z * Mathf.Cos(el));
                    pos = burst + d3 * blastRadius * 0.16f;
                    vel = d3 * Random.Range(180f, 260f);
                }
                else
                {
                    // A thin, translucent disc spreading out rather than one fat donut of puffs.
                    pos = new Vector3(burst.x, h, burst.z) + dir * blastRadius * Random.Range(0.08f, 0.2f) + Vector3.up * Random.Range(-8f, 8f);
                    vel = dir * Random.Range(120f, 340f) + Vector3.up * Random.Range(-3f, 8f);
                }
                Emit(ring, pos, vel, Random.Range(45f, 80f) * Scale, Random.Range(1.8f, 3.4f), 1f);
            }
        }

        // ---- lingering cloud ----

        void BuildRemnants()
        {
            capRemnant = Fx.NewSystem("NukeAftermath_CloudRemnant", transform, Vector3.zero, Fx.StockMaterial("smoke_background"), Count(420));
            SetupRemnant(capRemnant, new float[] { 0f, 1f, 0.98f, 0.96f, 1f, 0.84f, 0.82f, 0.8f },
                new float[] { 0f, 0f, 0.15f, 0.42f, 0.6f, 0.33f, 1f, 0f }, 1.8f);
            stemRemnant = Fx.NewSystem("NukeAftermath_StemRemnant", transform, Vector3.zero, Fx.StockMaterial("smoke_background"), Count(420));
            SetupRemnant(stemRemnant, new float[] { 0f, 0.62f, 0.58f, 0.55f, 1f, 0.72f, 0.7f, 0.68f },
                new float[] { 0f, 0f, 0.12f, 0.4f, 0.6f, 0.3f, 1f, 0f }, 2.2f);
            nextCapPuff = 22f;
            nextStemPuff = 30f;
        }

        static void SetupRemnant(ParticleSystem ps, float[] colors, float[] alphas, float grow)
        {
            var main = ps.main;
            main.startColor = Color.white;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = Fx.Gradient(colors, alphas);
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, Fx.Curve(0f, 1f / grow, 0.4f, 0.75f, 1f, 1f));
            var rot = ps.rotationOverLifetime;
            rot.enabled = true;
            rot.z = new ParticleSystem.MinMaxCurve(-0.02f, 0.02f);
            ps.Play();
        }

        void UpdateRemnants()
        {
            if (capRemnant == null || cloudMesh == null) return;
            float puffLife = lingerEnd;
            var capPos = cloudMesh.position.ToGlobalPosition().AsVector3();
            var wind = Fx.Wind(cloudMesh.position);
            float capSize = cloudSize != null ? cloudSize.Evaluate(time) : 200f;
            if (time >= nextCapPuff && time < 200f)
            {
                nextCapPuff = time + 1.0f / budget;
                var off = Random.insideUnitSphere;
                off.y *= 0.35f;
                var pos = capPos + off * capSize * 0.35f;
                var vel = wind * 0.85f + Vector3.up * Random.Range(0f, 1.5f) + new Vector3(off.x, 0f, off.z) * 2f;
                // Final size; puffs start at about half of it and spread into a soft drifting sheet.
                Emit(capRemnant, pos, vel, capSize * Random.Range(1.1f, 1.6f), puffLife * Random.Range(0.75f, 1f), Random.Range(0.9f, 1.05f));
            }
            if (time >= nextStemPuff && time < 220f)
            {
                nextStemPuff = time + 0.7f / budget;
                // Kept off the ground: low puffs this size lie on the terrain as a flat grey sheet.
                float f = Mathf.Pow(Random.value, 0.8f) * 0.7f + 0.22f;
                var pos = Vector3.Lerp(groundZero, capPos, f) + Random.insideUnitSphere * capSize * 0.12f;
                var vel = Fx.Wind(cloudMesh.position) * Mathf.Lerp(0.5f, 0.9f, f) + Vector3.up * Random.Range(0.5f, 2.5f);
                Emit(stemRemnant, pos, vel, capSize * Random.Range(0.6f, 0.9f), puffLife * Random.Range(0.55f, 0.85f), Random.Range(0.85f, 1.05f));
            }
        }

        // ---- shake ----

        void UpdateShake()
        {
            if (!shakeArmed) return;
            var cam = SceneSingleton<CameraStateManager>.i;
            if (cam == null) return;
            if (shakeAt < 0f)
            {
                var d = Vector3.Distance(cam.transform.position.ToGlobalPosition().AsVector3(), burst);
                if (340f * time < d) return;
                shakeAt = time;
                shakeAmp = p.ShakeStrength.Value * Mathf.Clamp01(blastRadius * 2.5f / Mathf.Max(d, 1f)) * 0.35f;
                if (shakeAmp < 0.01f) { shakeArmed = false; return; }
            }
            float since = time - shakeAt;
            if (since > 3f) { shakeArmed = false; return; }
            // A rumble that dies away: low-frequency first, a little high-frequency rattle on top.
            float k = Mathf.Exp(-since / 0.9f) * Time.deltaTime * 6f;
            cam.ShakeCamera(shakeAmp * k, shakeAmp * 0.4f * k);
        }

        // ---- frame ----

        void Update()
        {
            float dt = Time.deltaTime;
            time += dt;
            try
            {
                if (pendingBuilds.Count > 0) pendingBuilds.Dequeue()();
                UpdateFlash();
                UpdateSurge(dt);
                UpdateRing();
                UpdateRemnants();
                UpdateShake();
                fires?.Update(time, dt);
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogError(e);
                enabled = false;
            }
            if (time > lingerEnd + 30f && (fires == null || fires.Done) && time > 120f) Destroy(gameObject);
        }

        void OnDestroy()
        {
            fires?.Dispose();
        }

        // ---- helpers ----

        float Scale => Mathf.Pow(blastRadius / 1487f, 0.8f);

        int Count(int n) => Mathf.Max(1, Mathf.RoundToInt(n * budget));

        internal static void Emit(ParticleSystem ps, Vector3 pos, Vector3 vel, float size, float life, float shade)
        {
            var e = new ParticleSystem.EmitParams
            {
                position = pos,
                velocity = vel,
                startSize = size,
                startLifetime = life,
                rotation = Random.Range(0f, 360f),
                startColor = new Color(shade, shade, shade, 1f) * ps.main.startColor.color,
                applyShapeToPosition = false,
            };
            ps.Emit(e, 1);
        }
    }
}
