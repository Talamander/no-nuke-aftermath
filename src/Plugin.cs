using System.Collections;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using NuclearOption.Effects;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Talamander.NukeAftermath
{
    /// <summary>
    /// Client-only, visual-only nuclear aftermath for Nuclear Option.
    ///
    /// Trigger: a postfix on Missile.UserCode_RpcDetonate_897349600. That is the body of the [ClientRpc] RpcDetonate,
    /// which the server sends to every observer (Mirage AlwaysVisible: every scene-ready player) and also runs on the
    /// host. Only warheads whose WeaponInfo.nuclear is true and that were armed count.
    ///
    /// Effects, all local and never networked:
    /// 1. A persistent scorch decal (the game's own GameAssets.scorchMarkDecal prefab), sized from the same blast
    ///    radius formula as Shockwave.Start, and not handed to EffectManager (which keeps only 20 effects).
    /// 2. Charred dead trunks where the stock TreeRenderer cleared trees. The stock game already removes trees in
    ///    nuclear blasts (BlastManager.AddBlast, then TreeRenderer.ClearTrees on the GPU). We read the position
    ///    buffer back once after the blast and draw our own trunk meshes at exactly the trees that disappeared.
    ///
    /// No colliders are created, no gameplay state is read back into the game, no game method is replaced.
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "talamander.no.nukeaftermath";
        public const string Name = "Nuke Aftermath";
        public const string Version = "0.1.0";

        internal static ManualLogSource Log;
        internal static Plugin Instance;

        internal ConfigEntry<bool> PluginEnabled;
        internal ConfigEntry<bool> Scorch;
        internal ConfigEntry<float> ScorchRadiusFactor;
        internal ConfigEntry<float> ScorchDrawDistance;
        internal ConfigEntry<bool> CharredTrees;
        internal ConfigEntry<int> TrunksPerClump;
        internal ConfigEntry<float> ClumpRadius;
        internal ConfigEntry<float> FlattenFraction;
        internal ConfigEntry<int> MaxCharredClumps;
        internal ConfigEntry<float> MinYieldKg;

        readonly List<GameObject> spawned = new List<GameObject>();
        readonly HashSet<long> charred = new HashSet<long>(); // renderer index << 32 | tree index
        int charredClumps;
        Material trunkMaterial;

        void Awake()
        {
            Log = Logger;
            Instance = this;
            PluginEnabled = Config.Bind("General", "Enabled", true, "Master switch. Visual only; safe to differ between players.");
            MinYieldKg = Config.Bind("General", "MinYieldKg", 1000f,
                "Ignore nuclear-flagged warheads below this yield (kg TNT). The stock BlastManager starts at 10,000 kg.");
            Scorch = Config.Bind("Scorch", "Enabled", true, "Leave a persistent scorch decal at ground zero.");
            ScorchRadiusFactor = Config.Bind("Scorch", "RadiusFactor", 0.45f,
                "Scorch radius as a fraction of the game's blast radius (13 * yieldKg^(1/3) m).");
            ScorchDrawDistance = Config.Bind("Scorch", "DrawDistance", 30000f, "Decal draw distance in metres.");
            CharredTrees = Config.Bind("Trees", "Enabled", true, "Draw charred trunks where the stock blast removed trees.");
            TrunksPerClump = Config.Bind("Trees", "TrunksPerClump", 7, "Trunks per removed tree clump (stock clumps are about 50 m wide).");
            ClumpRadius = Config.Bind("Trees", "ClumpRadius", 16f, "Spread of trunks around each removed clump, metres.");
            FlattenFraction = Config.Bind("Trees", "FlattenFraction", 0.55f,
                "Inside this fraction of the cleared radius, trunks lie flat, blown down away from ground zero.");
            MaxCharredClumps = Config.Bind("Trees", "MaxCharredClumps", 40000, "Hard cap for the whole session.");

            var harmony = new Harmony(Guid);
            harmony.Patch(AccessTools.Method(typeof(Missile), "UserCode_RpcDetonate_897349600"),
                postfix: new HarmonyMethod(typeof(Plugin), nameof(DetonatePostfix)));
            harmony.Patch(AccessTools.Method(typeof(DetailRenderer), "Start"),
                postfix: new HarmonyMethod(typeof(Plugin), nameof(DetailRendererStartPostfix)));
            Log.LogInfo($"{Name} {Version} loaded.");
        }

        // ---- hooks ----

        static void DetonatePostfix(Missile __instance, bool armed)
        {
            try
            {
                var self = Instance;
                if (self == null || !self.PluginEnabled.Value || GameManager.IsHeadless) return;
                if (!armed || __instance == null) return;
                var info = __instance.GetWeaponInfo();
                if (info == null || !info.nuclear) return;
                float yieldKg = __instance.GetYield();
                if (yieldKg < self.MinYieldKg.Value) return;
                self.OnNuke(__instance.transform.position, yieldKg);
            }
            catch (System.Exception e) { Log.LogError(e); }
        }

        static void DetailRendererStartPostfix()
        {
            // New map or mission: the stock blast map and tree buffers are rebuilt. Our objects are children of
            // Datum.origin and usually go with the map; clear the bookkeeping either way.
            if (Instance != null) Instance.ResetSession();
        }

        void ResetSession()
        {
            foreach (var go in spawned) if (go != null) Destroy(go);
            spawned.Clear();
            charred.Clear();
            charredClumps = 0;
        }

        // ---- aftermath ----

        void OnNuke(Vector3 localPos, float yieldKg)
        {
            // Same numbers as Shockwave.Start: blastRadius = (kt * 1e6)^(1/3) * 13, with kt = yieldKg * 1e-6.
            float blastRadius = Mathf.Pow(yieldKg, 0.3333f) * 13f;
            // BlastManager.AddBlast(pos, blastRadius * 0.5) * radiusMultiplier 2 * clearTreeRadiusMultiplier 0.5.
            float clearRadius = blastRadius * 0.5f;
            var global = localPos.ToGlobalPosition();
            Log.LogInfo($"Nuclear detonation {yieldKg / 1000f:F1} t at {global}; blast radius {blastRadius:F0} m, tree clear radius {clearRadius:F0} m.");

            if (Scorch.Value) SpawnScorch(localPos, blastRadius);
            if (CharredTrees.Value) StartCoroutine(CharTrees(global, clearRadius));
        }

        void SpawnScorch(Vector3 localPos, float blastRadius)
        {
            if (GameAssets.i == null || GameAssets.i.scorchMarkDecal == null || Datum.origin == null) return;
            // Shockwave.Start only puts its ground decal down when the ground is within half a blast radius.
            if (!Physics.Linecast(localPos + Vector3.up * blastRadius * 0.5f, localPos - Vector3.up * blastRadius * 0.5f,
                    out var hit, PhysicsLayers.StaticsMask))
            {
                Log.LogInfo("Airburst too high for a ground scorch.");
                return;
            }
            float r = blastRadius * ScorchRadiusFactor.Value;
            var go = Instantiate(GameAssets.i.scorchMarkDecal, Datum.origin);
            go.name = "NukeAftermath_Scorch";
            go.transform.rotation = Quaternion.LookRotation(Vector3.down);
            go.transform.position = hit.point;
            var projector = go.GetComponent<DecalProjector>();
            if (projector == null) { Destroy(go); Log.LogWarning("scorchMarkDecal has no DecalProjector."); return; }
            projector.material = new Material(projector.material);
            // Depth must cover the relief inside the scorch: a hill 300 m above ground zero still needs paint.
            projector.size = new Vector3(r * 2f, r * 2f, Mathf.Max(400f, r * 0.6f));
            projector.drawDistance = ScorchDrawDistance.Value;
            projector.fadeFactor = 1f;
            spawned.Add(go);
        }

        IEnumerator CharTrees(GlobalPosition center, float clearRadius)
        {
            // The stock DetailRenderer drains BlastManager.Blasts in LateUpdate of the frame the Shockwave starts.
            // Give it a few frames, then read back what the GPU clear left.
            for (int i = 0; i < 6; i++) yield return null;
            var detail = SceneSingleton<DetailRenderer>.i;
            if (detail == null) yield break;
            var renderers = AccessTools.Field(typeof(DetailRenderer), "treeRenderers").GetValue(detail) as TreeRenderer[];
            if (renderers == null) yield break;
            var bufferField = AccessTools.Field(typeof(TreeRenderer), "treePositionBuffer");

            for (int ri = 0; ri < renderers.Length; ri++)
            {
                var tr = renderers[ri];
                if (tr == null || tr.PositionData == null) continue;
                var buffer = bufferField.GetValue(tr) as GraphicsBuffer;
                if (buffer == null) continue;
                var request = AsyncGPUReadback.Request(buffer);
                while (!request.done) yield return null;
                if (request.hasError) { Log.LogWarning("Tree buffer readback failed."); continue; }
                var after = request.GetData<Vector3>();
                var before = tr.PositionData.GetData<Vector3>();
                BuildTrunks(ri, before, after, center, clearRadius);
            }
        }

        void BuildTrunks(int rendererIndex, NativeArray<Vector3> before, NativeArray<Vector3> after, GlobalPosition center, float clearRadius)
        {
            var c = center.AsVector3();
            float search = clearRadius * 1.25f;
            float search2 = search * search;
            int n = Mathf.Min(before.Length, after.Length);
            var removed = new List<Vector3>();
            int changedOutside = 0;
            for (int i = 0; i < n; i++)
            {
                var p = before[i];
                float dx = p.x - c.x, dz = p.z - c.z;
                bool near = dx * dx + dz * dz <= search2;
                if (after[i] == p) continue;
                if (!near) { changedOutside++; continue; }
                long key = ((long)rendererIndex << 32) | (uint)i;
                if (charred.Contains(key)) continue;
                charred.Add(key);
                removed.Add(p);
            }
            Log.LogInfo($"Renderer {rendererIndex}: {n} trees, {removed.Count} newly cleared near ground zero, {changedOutside} changed elsewhere (earlier blasts or compaction).");
            if (removed.Count == 0) return;
            int budget = MaxCharredClumps.Value - charredClumps;
            if (budget <= 0) return;
            if (removed.Count > budget) removed.RemoveRange(budget, removed.Count - budget);
            charredClumps += removed.Count;
            foreach (var go in TrunkMeshBuilder.Build(removed, c, clearRadius, TrunksPerClump.Value, ClumpRadius.Value,
                         FlattenFraction.Value, GetTrunkMaterial()))
            {
                go.transform.SetParent(Datum.origin, false);
                spawned.Add(go);
            }
        }

        Material GetTrunkMaterial()
        {
            if (trunkMaterial != null) return trunkMaterial;
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Universal Render Pipeline/Simple Lit");
            if (shader == null) { Log.LogWarning("No URP Lit shader found; trunks fall back to the default shader."); shader = Shader.Find("Standard"); }
            trunkMaterial = new Material(shader) { name = "NukeAftermath_Charcoal" };
            trunkMaterial.SetColor("_BaseColor", new Color(0.045f, 0.04f, 0.035f, 1f));
            trunkMaterial.SetFloat("_Smoothness", 0.08f);
            return trunkMaterial;
        }
    }
}
