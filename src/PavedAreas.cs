using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace Talamander.NukeAftermath
{
    /// <summary>
    /// Runways and taxiways near a blast, as flat strips in global (map) coordinates. The burn is kept light on them,
    /// and no fires or trunks are put on them, so pilots can still see where to taxi and take off.
    ///
    /// Source: every registered airbase (FactionRegistry.airbaseLookup). Runways come with their own width. Taxiways
    /// are the airbase taxi network, a set of polylines without a width, so they get TaxiwayWidth. Airbases on moving
    /// ships (runways with a Rigidbody) are skipped: the burn is static and never reaches a deck at sea anyway.
    /// </summary>
    internal sealed class PavedAreas
    {
        const float TaxiwayWidth = 30f; // taxiway plus shoulders
        const float DefaultRunwayWidth = 45f;

        struct Strip
        {
            public Vector2 a, b; // XZ, global
            public float halfWidth;
        }

        readonly List<Strip> strips = new List<Strip>();
        internal int RunwayCount { get; private set; }
        internal int TaxiwayCount { get; private set; }
        internal bool Any => strips.Count > 0;

        /// <summary>Strips within <paramref name="radius"/> of <paramref name="center"/> (global).</summary>
        internal static PavedAreas Near(Vector3 center, float radius)
        {
            var p = new PavedAreas();
            var c = new Vector2(center.x, center.z);
            try
            {
                foreach (var ab in FactionRegistry.airbaseLookup.Values)
                {
                    if (ab == null) continue;
                    if (ab.runways != null)
                        foreach (var rw in ab.runways)
                        {
                            if (rw == null || rw.Start == null || rw.End == null) continue;
                            if (Traverse.Create(rw).Field<Rigidbody>("rb").Value != null) continue;
                            float w = rw.GetWidth();
                            if (p.Add(c, radius, rw.Start.GlobalPosition().AsVector3(), rw.End.GlobalPosition().AsVector3(), w > 1f ? w : DefaultRunwayWidth))
                                p.RunwayCount++;
                        }
                    var taxi = ab.GetTaxiNetwork();
                    if (taxi?.roads == null) continue;
                    foreach (var road in taxi.roads)
                    {
                        if (road?.points == null) continue;
                        bool added = false;
                        for (int i = 1; i < road.points.Count; i++)
                            added |= p.Add(c, radius, road.points[i - 1].AsVector3(), road.points[i].AsVector3(), TaxiwayWidth);
                        if (added) p.TaxiwayCount++;
                    }
                }
            }
            catch (System.Exception e) { Plugin.Log.LogWarning("Runway lookup failed, the burn covers them: " + e.Message); }
            return p;
        }

        bool Add(Vector2 c, float radius, Vector3 a, Vector3 b, float width)
        {
            var s = new Strip { a = new Vector2(a.x, a.z), b = new Vector2(b.x, b.z), halfWidth = width * 0.5f };
            if (SegmentDistance(c, s.a, s.b) > radius + s.halfWidth) return false;
            strips.Add(s);
            return true;
        }

        /// <summary>Distance in metres from a global position to the nearest strip edge; negative inside a strip.</summary>
        internal float Distance(Vector3 global)
        {
            var q = new Vector2(global.x, global.z);
            float best = float.MaxValue;
            foreach (var s in strips) best = Mathf.Min(best, SegmentDistance(q, s.a, s.b) - s.halfWidth);
            return best;
        }

        /// <summary>
        /// Multiplies <paramref name="keep"/> (size x size texels spanning 2 * radius around <paramref name="center"/>,
        /// texel u along <paramref name="uAxis"/>, v along <paramref name="vAxis"/>) by <paramref name="paved"/> on the
        /// strips, with a soft edge <paramref name="feather"/> metres wide. Only the texels near each strip are visited.
        /// </summary>
        internal void Mask(float[] keep, int size, Vector3 center, Vector2 uAxis, Vector2 vAxis, float radius, float paved, float feather)
        {
            float texel = 2f * radius / size;
            var c = new Vector2(center.x, center.z);
            foreach (var s in strips)
            {
                // Strip ends and reach in texel space.
                Vector2 ta = ToTexel(s.a), tb = ToTexel(s.b);
                float reach = (s.halfWidth + feather) / texel + 1f;
                int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(ta.x, tb.x) - reach)), x1 = Mathf.Min(size - 1, Mathf.CeilToInt(Mathf.Max(ta.x, tb.x) + reach));
                int y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(ta.y, tb.y) - reach)), y1 = Mathf.Min(size - 1, Mathf.CeilToInt(Mathf.Max(ta.y, tb.y) + reach));
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        // Distance in texel space times texel size: the axes are orthonormal.
                        float d = SegmentDistance(new Vector2(x + 0.5f, y + 0.5f), ta, tb) * texel - s.halfWidth;
                        if (d >= feather) continue;
                        float t = Mathf.Clamp01(d / feather);
                        float f = Mathf.Lerp(paved, 1f, t * t * (3f - 2f * t));
                        int i = y * size + x;
                        if (f < keep[i]) keep[i] = f;
                    }
            }

            Vector2 ToTexel(Vector2 g)
            {
                var d = g - c;
                return new Vector2((Vector2.Dot(d, uAxis) / (2f * radius) + 0.5f) * size, (Vector2.Dot(d, vAxis) / (2f * radius) + 0.5f) * size);
            }
        }

        static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            float len2 = ab.sqrMagnitude;
            float t = len2 > 1e-6f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2) : 0f;
            return Vector2.Distance(p, a + ab * t);
        }
    }
}
