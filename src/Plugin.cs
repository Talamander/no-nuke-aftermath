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

namespace Talamander.NukeAftermath
{
    /// <summary>
    /// Client-only, visual-only nuclear blast and aftermath effects for Nuclear Option.
    ///
    /// Trigger: a postfix on Missile.UserCode_RpcDetonate_897349600. That is the body of the [ClientRpc] RpcDetonate,
    /// which the server sends to every observer and also runs on the host. Only armed warheads whose WeaponInfo.nuclear
    /// is true count. The stock explosion prefab has just been instantiated at that point (its Start has not run), so
    /// its visual curves can still be changed before they are used.
    ///
    /// Everything here is local and never networked. No colliders are created, no damage or gameplay value is read or
    /// changed, and no game method is replaced. The only stock calls with side effects are visual: an extra
    /// BlastManager.AddBlast (the tree-clearing and ground-tint map) and CameraStateManager.ShakeCamera.
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "talamander.no.nukeaftermath";
        public const string Name = "Nuke Aftermath";
        public const string Version = "0.2.2";

        internal static ManualLogSource Log;
        internal static Plugin Instance;

        // General
        internal ConfigEntry<bool> PluginEnabled;
        internal ConfigEntry<float> MinYieldKg;
        // Explosion
        internal ConfigEntry<bool> BiggerCloud;
        internal ConfigEntry<float> CloudScale;
        internal ConfigEntry<float> CloudLifetimeScale;
        internal ConfigEntry<bool> Flash;
        internal ConfigEntry<float> FlashBrightness;
        internal ConfigEntry<bool> ScreenFlash;
        internal ConfigEntry<bool> BaseSurge;
        internal ConfigEntry<bool> CondensationRing;
        internal ConfigEntry<bool> LingeringCloud;
        internal ConfigEntry<float> LingeringCloudMinutes;
        internal ConfigEntry<bool> CameraShake;
        internal ConfigEntry<float> ShakeStrength;
        // Aftermath
        internal ConfigEntry<bool> Burn;
        internal ConfigEntry<float> BurnRadiusFactor;
        internal ConfigEntry<float> BurnDrawDistance;
        internal ConfigEntry<float> PavedBurn;
        internal ConfigEntry<bool> ExtraTreeClear;
        internal ConfigEntry<float> TreeClearFactor;
        internal ConfigEntry<bool> CharredTrees;
        internal ConfigEntry<int> TrunksPerClump;
        internal ConfigEntry<float> ClumpRadius;
        internal ConfigEntry<float> FlattenFraction;
        internal ConfigEntry<int> MaxTrunksPerBlast;
        internal ConfigEntry<bool> GroundFires;
        internal ConfigEntry<int> MaxFires;
        internal ConfigEntry<float> FireMinutes;
        internal ConfigEntry<bool> SmokeColumn;
        internal ConfigEntry<bool> Embers;
        // Performance
        internal ConfigEntry<float> ParticleBudget;
        internal ConfigEntry<int> MaxActiveBlasts;

        readonly List<GameObject> persistent = new List<GameObject>(); // burns and trunks: stay for the mission
        readonly List<GameObject> activeBlasts = new List<GameObject>(); // particle effects: trimmed oldest first
        readonly HashSet<long> charred = new HashSet<long>(); // renderer index << 32 | tree index
        int nukesThisSession;
        bool vrChecked, vrActive;

        void Awake()
        {
            // BepInEx 5 can be set up to keep its manager object in the scene (HideManagerGameObject = false, the
            // default). The game then destroys it, and this plugin with it, on the first scene load. Keep it alive.
            if ((gameObject.hideFlags & HideFlags.DontSave) == 0) gameObject.hideFlags |= HideFlags.HideAndDontSave;

            Log = Logger;
            Instance = this;
            BindConfig();

            var harmony = new Harmony(Guid);
            harmony.Patch(AccessTools.Method(typeof(Missile), "UserCode_RpcDetonate_897349600"),
                postfix: new HarmonyMethod(typeof(Plugin), nameof(DetonatePostfix)));
            harmony.Patch(AccessTools.Method(typeof(DetailRenderer), "Start"),
                postfix: new HarmonyMethod(typeof(Plugin), nameof(DetailRendererStartPostfix)));
            harmony.Patch(AccessTools.Method(typeof(MushroomCloud), "Start"),
                postfix: new HarmonyMethod(typeof(StockBlastTweaks), nameof(StockBlastTweaks.MushroomStartPostfix)));
            Log.LogInfo($"{Name} {Version} loaded.");
        }

        void BindConfig()
        {
            PluginEnabled = Config.Bind("General", "Enabled", true, "Master switch. Visual only; safe to differ between players.");
            MinYieldKg = Config.Bind("General", "MinYieldKg", 1000f,
                "Ignore nuclear-flagged warheads below this yield (kg TNT).");

            BiggerCloud = Config.Bind("Explosion", "BiggerCloud", true, "Make the stock fireball, mushroom cloud, stem and base cloud bigger and longer-lasting.");
            CloudScale = Config.Bind("Explosion", "CloudScale", 1.6f, new ConfigDescription("Size multiplier for the stock fireball and mushroom cloud.", new AcceptableValueRange<float>(1f, 3f)));
            CloudLifetimeScale = Config.Bind("Explosion", "CloudLifetimeScale", 1.6f, new ConfigDescription("How much longer the stock cloud, stem and base cloud last.", new AcceptableValueRange<float>(1f, 3f)));
            Flash = Config.Bind("Explosion", "Flash", true, "Brighter, longer flash, plus a light pulse that lights the ground and nearby clouds.");
            FlashBrightness = Config.Bind("Explosion", "FlashBrightness", 1f, new ConfigDescription("Multiplier for the extra flash light.", new AcceptableValueRange<float>(0f, 3f)));
            ScreenFlash = Config.Bind("Explosion", "ScreenFlash", true, "Brief white-out of the screen when the flash is in view. Always off in VR.");
            BaseSurge = Config.Bind("Explosion", "BaseSurge", true, "Ring of dust racing out along the ground from ground zero.");
            CondensationRing = Config.Bind("Explosion", "CondensationRing", true, "Short-lived white condensation ring around the fireball.");
            LingeringCloud = Config.Bind("Explosion", "LingeringCloud", true, "Leave a drifting remnant of the mushroom cloud and stem for several minutes.");
            LingeringCloudMinutes = Config.Bind("Explosion", "LingeringCloudMinutes", 7f, new ConfigDescription("How long the remnant cloud lasts.", new AcceptableValueRange<float>(1f, 20f)));
            CameraShake = Config.Bind("Explosion", "CameraShake", true, "Extra cockpit camera rumble when the blast wave arrives, scaled by distance. Always off in VR.");
            ShakeStrength = Config.Bind("Explosion", "ShakeStrength", 1f, new ConfigDescription("Multiplier for the extra rumble.", new AcceptableValueRange<float>(0f, 2f)));

            Burn = Config.Bind("Aftermath", "Burn", true, "Leave a large burn on the ground: black core, charred ring, feathered edge.");
            BurnRadiusFactor = Config.Bind("Aftermath", "BurnRadiusFactor", 0.7f, new ConfigDescription("Burn radius as a fraction of the game's blast radius (13 * yieldKg^(1/3) m).", new AcceptableValueRange<float>(0.2f, 1.5f)));
            BurnDrawDistance = Config.Bind("Aftermath", "BurnDrawDistance", 30000f, "Burn draw distance in metres.");
            PavedBurn = Config.Bind("Aftermath", "PavedBurn", 0.3f, new ConfigDescription(
                "How dark the burn gets on runways and taxiways, as a fraction of the full burn. 0 leaves them clean, 1 burns them like the ground around them. Fires and charred trunks are never put on them.",
                new AcceptableValueRange<float>(0f, 1f)));
            ExtraTreeClear = Config.Bind("Aftermath", "ExtraTreeClear", true, "Clear trees further out than the stock blast does (visual only).");
            TreeClearFactor = Config.Bind("Aftermath", "TreeClearFactor", 0.75f, new ConfigDescription("Tree clearing radius as a fraction of the blast radius. Stock is 0.5.", new AcceptableValueRange<float>(0.5f, 1.2f)));
            CharredTrees = Config.Bind("Aftermath", "CharredTrees", true, "Draw charred trunks where the blast removed trees.");
            TrunksPerClump = Config.Bind("Aftermath", "TrunksPerClump", 16, new ConfigDescription("Trunks per removed tree clump (stock clumps are about 50 m wide).", new AcceptableValueRange<int>(1, 40)));
            ClumpRadius = Config.Bind("Aftermath", "ClumpRadius", 22f, "Spread of trunks around each removed clump, metres.");
            FlattenFraction = Config.Bind("Aftermath", "FlattenFraction", 0.6f,
                "Inside this fraction of the cleared radius, trunks lie flat, blown down away from ground zero. Further out they stand as burnt snags.");
            MaxTrunksPerBlast = Config.Bind("Aftermath", "MaxTrunksPerBlast", 45000, "Performance cap: trunks drawn for one blast. Clumps share it evenly.");
            GroundFires = Config.Bind("Aftermath", "GroundFires", true, "Scattered ground fires with smoke columns in the burnt area.");
            MaxFires = Config.Bind("Aftermath", "MaxFires", 14, new ConfigDescription("Fires per blast.", new AcceptableValueRange<int>(0, 60)));
            FireMinutes = Config.Bind("Aftermath", "FireMinutes", 6f, new ConfigDescription("How long fires burn before they die out.", new AcceptableValueRange<float>(1f, 30f)));
            SmokeColumn = Config.Bind("Aftermath", "SmokeColumn", true, "A tall smoke column rising from ground zero after the stem fades.");
            Embers = Config.Bind("Aftermath", "Embers", true, "Glowing embers drifting over the burnt area for the first few minutes.");

            ParticleBudget = Config.Bind("Performance", "ParticleBudget", 1f, new ConfigDescription("Scales the number of particles in every effect this mod adds.", new AcceptableValueRange<float>(0.1f, 2f)));
            MaxActiveBlasts = Config.Bind("Performance", "MaxActiveBlasts", 3, new ConfigDescription("Blasts with live smoke and fire at once. The oldest is removed first; burns and trunks stay.", new AcceptableValueRange<int>(1, 10)));
        }

        /// <summary>True when a VR headset is active or a VR mod is loaded. Screen flash and shake are skipped then.</summary>
        internal bool InVR
        {
            get
            {
                if (vrChecked) return vrActive;
                vrChecked = true;
                try
                {
                    var t = System.Type.GetType("UnityEngine.XR.XRSettings, UnityEngine.VRModule");
                    if (t != null)
                    {
                        if (t.GetProperty("enabled")?.GetValue(null) is bool e && e) vrActive = true;
                        if (t.GetProperty("isDeviceActive")?.GetValue(null) is bool a && a) vrActive = true;
                    }
                    foreach (var info in BepInEx.Bootstrap.Chainloader.PluginInfos.Values)
                    {
                        var m = info.Metadata;
                        if (m == null || m.GUID == Guid) continue;
                        if (m.Name.IndexOf("VR", System.StringComparison.Ordinal) >= 0 || m.GUID.IndexOf(".vr", System.StringComparison.OrdinalIgnoreCase) >= 0)
                            vrActive = true;
                    }
                }
                catch (System.Exception e) { Log.LogWarning("VR check failed: " + e.Message); }
                if (vrActive) Log.LogInfo("VR detected: screen flash and camera shake are off.");
                return vrActive;
            }
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
            // New map or mission: the stock blast map and tree buffers are rebuilt.
            if (Instance != null) Instance.ResetSession();
        }

        void ResetSession()
        {
            int had = persistent.Count + activeBlasts.Count;
            foreach (var go in persistent) if (go != null) Destroy(go);
            foreach (var go in activeBlasts) if (go != null) Destroy(go);
            persistent.Clear();
            activeBlasts.Clear();
            charred.Clear();
            nukesThisSession = 0;
            if (PluginEnabled.Value && Burn.Value) BurnDecal.BurnTexture(); // generate once now, not on the blast frame
            if (PluginEnabled.Value)
            {
                Fx.Prewarm();
                if (CharredTrees.Value) TrunkMeshBuilder.Material(); // bark atlas, also once per session
            }
            Log.LogInfo($"{Name} {Version}: new map session, plugin alive" + (had > 0 ? $" (cleared {had} objects from the last one)." : "."));
        }

        // ---- per blast ----

        void OnNuke(Vector3 localPos, float yieldKg)
        {
            if (Datum.origin == null) return;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            // Same numbers as Shockwave.Start: blastRadius = (yieldKg)^(1/3) * 13.
            float blastRadius = Mathf.Pow(yieldKg, 0.3333f) * 13f;
            float stockClear = blastRadius * 0.5f; // AddBlast(r * 0.5) * radiusMultiplier 2 * clearTreeRadiusMultiplier 0.5
            float clearRadius = stockClear;
            var global = localPos.ToGlobalPosition();
            nukesThisSession++;

            if (ExtraTreeClear.Value && TreeClearFactor.Value > 0.5f && SceneSingleton<BlastManager>.i != null)
            {
                clearRadius = blastRadius * TreeClearFactor.Value;
                // Queued like the stock call; the DetailRenderer drains the queue in its next LateUpdate.
                SceneSingleton<BlastManager>.i.AddBlast(global, clearRadius);
            }
            Log.LogInfo($"Nuclear detonation {yieldKg / 1000f:F1} t at {global}; blast radius {blastRadius:F0} m, tree clear radius {clearRadius:F0} m (stock {stockClear:F0} m). Blast {nukesThisSession} this session.");

            float groundY = Fx.GroundY(localPos, localPos.y - 100000f);
            bool groundHit = Physics.Linecast(localPos + Vector3.up * blastRadius * 0.5f, localPos - Vector3.up * blastRadius * 0.5f,
                out var hit, PhysicsLayers.StaticsMask);
            var groundZero = groundHit ? hit.point : new Vector3(localPos.x, groundY, localPos.z);

            MushroomCloud cloud = null;
            if (BiggerCloud.Value || Flash.Value || LingeringCloud.Value)
                cloud = StockBlastTweaks.FindFreshCloud(localPos);
            if (cloud != null) StockBlastTweaks.Apply(cloud, this);

            // Root for the particle effects of this blast, parented to the floating origin.
            var root = new GameObject("NukeAftermath_Blast");
            root.transform.SetParent(Datum.origin, false);
            root.transform.position = localPos;
            var blast = root.AddComponent<BlastFx>();
            blast.Init(this, localPos, groundZero, groundHit, blastRadius, clearRadius, cloud);
            activeBlasts.Add(root);
            TrimBlasts();

            // Runways and taxiways the burn, fires or trunks could reach.
            var paved = groundHit ? PavedAreas.Near(global.AsVector3(), blastRadius * Mathf.Max(BurnRadiusFactor.Value, TreeClearFactor.Value, 0.6f)) : null;
            if (paved != null && paved.Any) Log.LogInfo($"Paved strips near the blast: {paved.RunwayCount} runways, {paved.TaxiwayCount} taxiways.");

            if (Burn.Value && groundHit) persistent.Add(BurnDecal.Spawn(groundZero, blastRadius * BurnRadiusFactor.Value, BurnDrawDistance.Value, paved, PavedBurn.Value));
            else if (Burn.Value) Log.LogInfo("Airburst too high for a ground burn.");

            if (groundHit && (CharredTrees.Value || GroundFires.Value))
                StartCoroutine(AfterTreeClear(global, clearRadius, blast, paved));
            else if (groundHit) blast.StartFires(null, paved);
            Log.LogInfo($"Blast frame work: {sw.Elapsed.TotalMilliseconds:F1} ms.");
        }

        void TrimBlasts()
        {
            activeBlasts.RemoveAll(go => go == null);
            while (activeBlasts.Count > MaxActiveBlasts.Value)
            {
                Destroy(activeBlasts[0]);
                activeBlasts.RemoveAt(0);
            }
        }

        IEnumerator AfterTreeClear(GlobalPosition center, float clearRadius, BlastFx blast, PavedAreas paved)
        {
            // Shockwave.Start queues the stock blast on the next frame; the DetailRenderer clears trees in its
            // LateUpdate after that. Give it a few frames, then read back what the GPU clear left.
            for (int i = 0; i < 10; i++) yield return null;
            var removed = new List<Vector3>();
            var detail = SceneSingleton<DetailRenderer>.i;
            var renderers = detail != null ? AccessTools.Field(typeof(DetailRenderer), "treeRenderers").GetValue(detail) as TreeRenderer[] : null;
            if (renderers != null)
            {
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
                    CollectRemoved(ri, tr.PositionData.GetData<Vector3>(), request.GetData<Vector3>(), center, clearRadius, removed);
                }
            }
            if (blast != null && GroundFires.Value) blast.StartFires(removed, paved);
            if (!CharredTrees.Value || removed.Count == 0) yield break;

            int perClump = Mathf.Clamp(Mathf.Min(TrunksPerClump.Value, MaxTrunksPerBlast.Value / Mathf.Max(1, removed.Count)), 1, 40);
            var material = TrunkMeshBuilder.Material();
            foreach (var go in TrunkMeshBuilder.Build(removed, center.AsVector3(), clearRadius, perClump, ClumpRadius.Value, FlattenFraction.Value, material, paved))
            {
                go.transform.SetParent(Datum.origin, false);
                persistent.Add(go);
                yield return null; // one 512 m cell per frame
            }
            Log.LogInfo($"Charred trees: {removed.Count} clumps, {perClump} trunks each.");
        }

        void CollectRemoved(int rendererIndex, NativeArray<Vector3> before, NativeArray<Vector3> after, GlobalPosition center, float clearRadius, List<Vector3> removed)
        {
            var c = center.AsVector3();
            float search = clearRadius * 1.15f;
            float search2 = search * search;
            int n = Mathf.Min(before.Length, after.Length);
            int found = 0, changedOutside = 0;
            for (int i = 0; i < n; i++)
            {
                var p = before[i];
                if (after[i] == p) continue;
                float dx = p.x - c.x, dz = p.z - c.z;
                if (dx * dx + dz * dz > search2) { changedOutside++; continue; }
                long key = ((long)rendererIndex << 32) | (uint)i;
                if (!charred.Add(key)) continue;
                removed.Add(p);
                found++;
            }
            Log.LogInfo($"Renderer {rendererIndex}: {n} trees, {found} newly cleared near ground zero, {changedOutside} changed elsewhere.");
        }
    }
}
