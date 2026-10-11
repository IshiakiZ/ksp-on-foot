using System.Collections.Generic;
using System.Text;
using Keystone;
using UnityEngine;
using Motor = ModuleWheels.ModuleWheelMotor;

namespace OnFoot
{
    /// <summary>
    /// The crew car's top speed (the page's setting). Its wheels' motors are given a torque curve of their own: the stock one
    /// up to 85% of the top speed, falling away smoothly past that and giving nothing from 3% over it. The motors still work
    /// the game's way (no force of ours, no speed clamped): the car pulls up to its top speed on the level and holds there,
    /// downhill it can roll faster, and the brakes work as ever. The stock curves go back when the setting is at its top
    /// (100 km/h: the wheels' own limit, about 95 km/h), or when the car stops being a crew car.
    /// </summary>
    public sealed class CarCap : MonoBehaviour
    {
        Vessel vessel;
        float applied = -1f, next;
        readonly Dictionary<Motor, FloatCurve> stock = new Dictionary<Motor, FloatCurve>();

        public static CarCap On(Vessel v) => v != null && v.rootPart != null ? v.rootPart.GetComponent<CarCap>() : null;

        void Start()
        {
            Part root = GetComponent<Part>();
            vessel = root != null ? root.vessel : null;
            if (vessel == null) Destroy(this);
        }

        void Update()
        {
            if (Time.unscaledTime < next) return;
            next = Time.unscaledTime + 0.5f;
            if (vessel == null || vessel.rootPart == null || vessel.rootPart.gameObject != gameObject || !CarFrame.Is(vessel)) { Destroy(this); return; }
            float want = Foot.TopSpeed;
            bool fresh = false;
            foreach (Motor m in Motors()) if (!stock.ContainsKey(m)) { fresh = true; break; }
            if (fresh || Mathf.Abs(want - applied) > 0.01f) Apply(want);
        }

        IEnumerable<Motor> Motors()
        {
            foreach (Part p in vessel.parts)
            {
                if (p == null) continue;
                foreach (PartModule pm in p.Modules)
                    if (pm is Motor m) yield return m;
            }
        }

        void Apply(float cap)
        {
            var motors = new List<Motor>(Motors());
            foreach (Motor m in motors)
            {
                if (!stock.ContainsKey(m)) stock[m] = m.torqueCurve;
                // (the curve is looked up against the rim speed of the wheel's radius as written; the wheel rolls on that radius
                // made as large as the part is drawn, 1.25 times for the RoveMax, so it goes that much faster than the curve says)
                float grown = Mathf.Max(0.1f, m.part.rescaleFactor);
                m.torqueCurve = cap > 0f ? Capped(stock[m], cap / grown) : stock[m];
            }
            if (applied != cap) Kit.Log(Foot.Name, "the crew car's top speed: " + (cap > 0f ? (cap * 3.6f).ToString("F0") + " km/h" : "the wheels' own") + " (" + motors.Count + " wheel motors)");
            applied = cap;
        }

        /// <summary>A wheel motor's torque curve (pull against the wheel's speed in m/s) that gives out at `cap`.</summary>
        internal static FloatCurve Capped(FloatCurve original, float cap)
        {
            var curve = new FloatCurve();
            float[] at = { 0f, 0.15f, 0.3f, 0.45f, 0.6f, 0.75f, 0.85f, 0.9f, 0.94f, 0.97f, 1f, 1.03f, 1.1f };
            var values = new float[at.Length];
            for (int i = 0; i < at.Length; i++) values[i] = Mathf.Max(0f, original.Evaluate(at[i] * cap)) * Taper(at[i]);
            for (int i = 0; i < at.Length; i++)
            {
                // (the slopes from the neighbours, so that the curve passes smoothly through the points and never dips below nought)
                float slope = i == 0 ? (values[1] - values[0]) / ((at[1] - at[0]) * cap)
                            : i == at.Length - 1 ? 0f
                            : (values[i + 1] - values[i - 1]) / ((at[i + 1] - at[i - 1]) * cap);
                if (values[i] <= 0f || (i + 1 < at.Length && values[i + 1] <= 0f)) slope = 0f;
                curve.Add(at[i] * cap, values[i], slope, slope);
            }
            return curve;
        }

        static float Taper(float x) => x <= 0.85f ? 1f : x >= 1.03f ? 0f : 1f - Mathf.SmoothStep(0f, 1f, (x - 0.85f) / 0.18f);

        void OnDestroy()
        {
            foreach (KeyValuePair<Motor, FloatCurve> kv in stock)
                if (kv.Key != null) kv.Key.torqueCurve = kv.Value;
            stock.Clear();
        }

#if DEV
        public string Report()
        {
            var say = new StringBuilder("top speed ").Append(applied > 0f ? (applied * 3.6f).ToString("F1") + " km/h" : "the wheels' own").Append(":");
            foreach (Motor m in Motors())
            {
                say.Append(" [pull at 0, 6, 8, 8.3, 9 m/s on the ground: ");
                float g = Mathf.Max(0.1f, m.part.rescaleFactor);
                foreach (float v in new[] { 0f, 6f, 8f, 8.3f, 9f }) say.Append(m.torqueCurve.Evaluate(v / g).ToString("F3")).Append(" ");
                say.Append("; output ").Append(m.driveOutput.ToString("F0")).Append("%]");
            }
            return say.ToString();
        }
#endif
    }
}
