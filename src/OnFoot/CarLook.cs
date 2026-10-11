using System;
using System.Collections.Generic;
using System.Text;
using Keystone;
using UnityEngine;
using UnityEngine.Rendering;

namespace OnFoot
{
    /// <summary>
    /// In flight: every crew car (see CarFrame) is drawn as an airport baggage tug (TugModel) instead of the stock parts it
    /// is made of. The car is still those parts and the game still drives it: only what is drawn changes. The parts are
    /// hidden (but for the kerbal and the crate it carries) and the tug's body rides on the car's root part; its four tyres
    /// follow the car's own wheels, turning with their steering, rising and falling with their suspension and rolling as
    /// they roll.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public sealed class CarLook : MonoBehaviour
    {
        float next;

#if DEV
        /// <summary>The last frames' lengths (seconds), for measuring what the tug costs.</summary>
        internal static readonly float[] Frames = new float[240];
        internal static int frame;
        /// <summary>For measuring: every tug taken off (without touching the player's setting).</summary>
        internal static bool Off;
#endif

        void Update()
        {
#if DEV
            Frames[frame++ % Frames.Length] = Time.unscaledDeltaTime;
#endif
            if (Time.unscaledTime < next) return;
            next = Time.unscaledTime + 0.5f;
            bool want = Foot.tug == null || Foot.tug.Value;
#if DEV
            want &= !Off;
#endif
            for (int i = 0; i < FlightGlobals.VesselsLoaded.Count; i++)
            {
                Vessel v = FlightGlobals.VesselsLoaded[i];
                if (v == null || v.rootPart == null) continue;
                // (its top speed, whatever it looks like)
                if (v.rootPart.GetComponent<CarCap>() == null && CarFrame.Is(v))
                {
                    try { v.rootPart.gameObject.AddComponent<CarCap>(); }
                    catch (Exception ex) { Kit.Log(Foot.Name, "could not set the crew car's top speed: " + ex); }
                }
                Tug tug = v.rootPart.GetComponent<Tug>();
                if (tug != null)
                {
                    if (!want) Destroy(tug);
                    continue;
                }
                if (want && CarFrame.Is(v) && TugModel.Ready(out _))
                {
                    try { v.rootPart.gameObject.AddComponent<Tug>(); }
                    catch (Exception ex) { Kit.Log(Foot.Name, "could not dress the crew car: " + ex); }
                }
            }
        }
    }

    /// <summary>The baggage tug drawn over one crew car: on its root part while the car is whole.</summary>
    public sealed class Tug : MonoBehaviour
    {
        // where the tyres are in the model (as Model/tug.py makes it: x right, y up, z forward, from the ground midway
        // between the axles)
        const float Track = 0.6f, AxleFront = 0.85f, AxleBack = -0.85f, TyreRadius = 0.275f;

        Part root;
        Vessel vessel;
        CarFrame frame;
        GameObject top;
        Transform interior, steering;
        TugModel.Piece steeringPiece;
        /// <summary>How many times further the steering wheel turns than the front wheels.</summary>
        const float SteeringRatio = 8f;
        readonly Transform[] tyres = new Transform[4];
        readonly Vector3[] places = new Vector3[4];
        readonly int[] follows = new int[4];          // which of the car's wheels each tyre follows
        readonly Vector3[] axles = new Vector3[4];    // each wheel's axle, in its pivot's own axes (pointing to the car's right)
        readonly Vector3[] spokes = new Vector3[4];   // a direction across each wheel's axle, in its pivot's own axes
        readonly Vector3[] lastSpoke = new Vector3[4];
        readonly float[] rest = new float[4];         // each wheel pivot's height on its part when the car was posed
        readonly float[] spin = new float[4];
        readonly float[] rolling = new float[4];      // each wheel's own rolling radius (the tyre turns as much faster as it is smaller)
        readonly List<Renderer> hidden = new List<Renderer>();
        MeshRenderer bodyRenderer;
        TugModel.Piece bodyPiece;
        MaterialPropertyBlock glow;
        List<ModuleLight> lights = new List<ModuleLight>();
        float posedAt = -1f, settledSince = -1f, nextLook;
        bool posed;

        /// <summary>The tug on a vessel, if it has one.</summary>
        public static Tug On(Vessel v) => v != null && v.rootPart != null ? v.rootPart.GetComponent<Tug>() : null;

        void Start()
        {
            root = GetComponent<Part>();
            vessel = root != null ? root.vessel : null;
            frame = CarFrame.Of(vessel);
            if (frame == null) { Destroy(this); return; }
            Build();
            Pose();
            Hide();
            Kit.Log(Foot.Name, "the crew car '" + vessel.vesselName + "' is drawn as a baggage tug");
        }

        void Build()
        {
            top = new GameObject("OnFoot tug");
            top.transform.SetParent(root.transform, false);
            top.layer = root.gameObject.layer;
            bodyPiece = TugModel.Get("body");
            bodyRenderer = Draw("body", top.transform);
            Draw("deck", top.transform);
            interior = Draw("interior", top.transform).transform;
            steeringPiece = TugModel.Get("steering");
            steering = Draw("steering", interior).transform;
            steering.localPosition = steeringPiece.Position;
            steering.localRotation = steeringPiece.Rotation;
            for (int i = 0; i < 4; i++)
            {
                places[i] = new Vector3((i & 1) == 0 ? -Track : Track, TyreRadius, i < 2 ? AxleFront : AxleBack);
                tyres[i] = Draw("tyre", top.transform).transform;
                tyres[i].name = "tyre " + ((i & 1) == 0 ? "left " : "right ") + (i < 2 ? "front" : "back");
            }
            glow = new MaterialPropertyBlock();
        }

        MeshRenderer Draw(string piece, Transform parent)
        {
            TugModel.Piece p = TugModel.Get(piece);
            var go = new GameObject(piece);
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = p.Mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterials = p.Materials;
            r.shadowCastingMode = ShadowCastingMode.On;
            r.receiveShadows = true;
            return r;
        }

        /// <summary>
        /// The tug put where the car is: its frame worked out from where its wheels are now (best when it stands still on
        /// the ground: the tyres then touch the ground), fixed on its root part from then on.
        /// </summary>
        public void Pose()
        {
            frame.Work();
            top.transform.position = frame.Origin;
            top.transform.rotation = frame.Rotation;
            interior.localPosition = frame.Local(frame.SeatPivot.position);
            // (which wheel each tyre follows: the one on its side and at its end of the car)
            for (int i = 0; i < 4; i++)
            {
                int best = -1; float bestScore = float.MinValue;
                for (int w = 0; w < frame.Pivots.Count; w++)
                {
                    Vector3 at = frame.Local(frame.Pivots[w].position);
                    float score = at.x * places[i].x + at.z * places[i].z;
                    if (score > bestScore) { bestScore = score; best = w; }
                }
                follows[i] = best;
                Transform pivot = frame.Pivots[best];
                // (its axle: whichever of the pivot's own axes lies along the car's right; a spoke: another of them)
                Vector3[] own = { Vector3.right, Vector3.up, Vector3.forward };
                int axle = 0; float along = 0f;
                for (int k = 0; k < 3; k++)
                {
                    float d = Vector3.Dot(pivot.TransformDirection(own[k]), frame.Right);
                    if (Mathf.Abs(d) > Mathf.Abs(along)) { along = d; axle = k; }
                }
                axles[i] = own[axle] * Mathf.Sign(along);
                spokes[i] = own[(axle + 1) % 3];
                lastSpoke[i] = Spoke(i);
                rest[i] = Lift(i);
                rolling[i] = CarFrame.RollingRadius(frame.Wheels[best]);
            }
            lights.Clear();
            Restore();
            foreach (Part p in vessel.parts)
            {
                ModuleLight l = p.FindModuleImplementing<ModuleLight>();
                if (l == null) continue;
                lights.Add(l);
                // (the lamps' own lights, moved to the tug's headlamps: the stock lamps are hidden inside its nose, and point up)
                if (l.lights == null) continue;
                bool right = top.transform.InverseTransformPoint(p.transform.position).x >= 0f;
                foreach (Light b in l.lights)
                    if (b != null) beams.Add(new Beam { Light = b, Right = right, Position = b.transform.localPosition, Rotation = b.transform.localRotation });
            }
            posedAt = Time.time;
            LateUpdate();
        }

        sealed class Beam { public Light Light; public bool Right; public Vector3 Position; public Quaternion Rotation; }
        readonly List<Beam> beams = new List<Beam>();

        /// <summary>The lamps' lights put back where their parts have them.</summary>
        void Restore()
        {
            foreach (Beam b in beams)
                if (b.Light != null) { b.Light.transform.localPosition = b.Position; b.Light.transform.localRotation = b.Rotation; }
            beams.Clear();
        }

        /// <summary>How far a wheel's pivot is up its part (the suspension's travel), in the car's frame.</summary>
        float Lift(int i)
        {
            Transform pivot = frame.Pivots[follows[i]];
            return Vector3.Dot(pivot.position - frame.Wheels[follows[i]].transform.position, top.transform.up);
        }

        /// <summary>A wheel's spoke direction in the tug's frame, square to its axle.</summary>
        Vector3 Spoke(int i)
        {
            Transform pivot = frame.Pivots[follows[i]];
            Vector3 axle = top.transform.InverseTransformDirection(pivot.TransformDirection(axles[i]));
            Vector3 spoke = top.transform.InverseTransformDirection(pivot.TransformDirection(spokes[i]));
            return Vector3.ProjectOnPlane(spoke, axle).normalized;
        }

        /// <summary>Every renderer of the car's parts out of sight, but the kerbals' and the crate's (and ours).</summary>
        void Hide()
        {
            foreach (Part p in vessel.parts)
            {
                if (p == null || p.partInfo == null || p.isKerbalEVA() || p.partInfo.name == CarFrame.Crate) continue;
                foreach (Renderer r in p.GetComponentsInChildren<Renderer>(true))
                {
                    if (r == null || r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
                    if (top != null && r.transform.IsChildOf(top.transform)) continue;
                    if (!r.enabled) continue;
                    r.enabled = false;
                    hidden.Add(r);
                }
            }
        }

        void Update()
        {
            if (Time.unscaledTime < nextLook) return;
            nextLook = Time.unscaledTime + 0.5f;
            if (vessel == null || root == null || !CarFrame.Is(vessel) || vessel.rootPart != root || CarFrame.Of(vessel) == null) { Destroy(this); return; }
            // (whenever it has stood still on the ground for a second with its wheels settled elsewhere than when it was posed, posed
            // again from there: the first pose may have been in the air as the car was put down; a driver getting in weighs it down)
            bool still = (vessel.Landed || vessel.situation == Vessel.Situations.PRELAUNCH || vessel.situation == Vessel.Situations.LANDED) && vessel.srfSpeed < 0.2;
            if (!still) settledSince = -1f;
            else if (settledSince < 0f) settledSince = Time.time;
            if (still && Time.time - settledSince > 1f && (!posed || Drift() > 0.01f)) { posed = true; Pose(); }
            Hide();         // (anything the game has shown again since)
        }

        /// <summary>How far, on average, the wheels have moved on their suspension since the car was posed.</summary>
        float Drift()
        {
            float sum = 0f;
            for (int i = 0; i < 4; i++) sum += Mathf.Abs(Lift(i) - rest[i]);
            return sum / 4f;
        }

#if DEV
        /// <summary>How long the tugs' work each frame has taken, all told, and over how many frames.</summary>
        internal static readonly System.Diagnostics.Stopwatch Work = new System.Diagnostics.Stopwatch();
        internal static long Worked;
#endif

        void LateUpdate()
        {
            if (top == null || frame == null) return;
#if DEV
            Work.Start();
            try { Follow(); } finally { Work.Stop(); Worked++; }
        }

        void Follow()
        {
#endif
            float steered = 0f;
            for (int i = 0; i < 4; i++)
            {
                Transform pivot = frame.Pivots[follows[i]];
                if (pivot == null) continue;
                // steering: where its axle points now, turned about the car's up
                Vector3 axle = top.transform.InverseTransformDirection(pivot.TransformDirection(axles[i]));
                float steer = Mathf.Atan2(-axle.z, axle.x) * Mathf.Rad2Deg;
                if (i < 2) steered += steer * 0.5f;
                // rolling: how far its spokes have turned about its axle since the last frame (scaled up for the smaller tyre)
                Vector3 now = Spoke(i);
                float turned = Vector3.SignedAngle(lastSpoke[i], now, Vector3.ProjectOnPlane(axle, Vector3.up).normalized);
                lastSpoke[i] = now;
                if (Mathf.Abs(turned) < 90f) spin[i] = Mathf.Repeat(spin[i] + turned * (rolling[i] / TyreRadius), 360f);
                // the suspension: as far up or down as the wheel has moved on its part since the car was posed
                float lift = Lift(i) - rest[i];
                Transform t = tyres[i];
                t.localPosition = places[i] + new Vector3(0f, Mathf.Clamp(lift, -0.2f, 0.2f), 0f);
                t.localRotation = Quaternion.Euler(0f, steer, 0f) * Quaternion.AngleAxis(spin[i], Vector3.right) * (places[i].x < 0f ? Quaternion.Euler(0f, 180f, 0f) : Quaternion.identity);
            }
            // the steering wheel, turned about its column as far as the front wheels are turned, many times over (to the left,
            // anticlockwise as the driver sees it)
            if (steering != null) steering.localRotation = steeringPiece.Rotation * Quaternion.AngleAxis(-steered * SteeringRatio, Vector3.forward);
            Lamps();
            // the lamps' lights from the headlamps, shining ahead and a little down
            foreach (Beam b in beams)
            {
                if (b.Light == null) continue;
                b.Light.transform.position = top.transform.TransformPoint(new Vector3(b.Right ? 0.46f : -0.46f, 0.52f, 1.54f));
                b.Light.transform.rotation = top.transform.rotation * Quaternion.Euler(6f, 0f, 0f);
            }
        }

        /// <summary>The lamps: the headlamps and the tail lamps with the car's lights, the tail lamps brighter with the brakes on,
        /// the beacon turning while the lights are on.</summary>
        void Lamps()
        {
            if (bodyRenderer == null || bodyPiece == null) return;
            bool on = false;
            foreach (ModuleLight l in lights) if (l != null && l.isOn) { on = true; break; }
            bool brakes = vessel.ActionGroups[KSPActionGroup.Brakes];
            Set("headlamp", on ? new Color(1.6f, 1.55f, 1.4f) : Color.black);
            Set("taillamp", brakes ? new Color(1.8f, 0.12f, 0.06f) : on ? new Color(0.7f, 0.05f, 0.03f) : Color.black);
            float beat = Mathf.Repeat(Time.time * 1.3f, 1f);
            float flash = on ? Mathf.Pow(Mathf.Max(0f, Mathf.Sin(beat * Mathf.PI * 2f)), 6f) : 0f;
            Set("beacon", new Color(1.6f, 0.75f, 0.08f) * flash);
        }

        void Set(string lamp, Color colour)
        {
            if (!bodyPiece.Lamps.TryGetValue(lamp, out int index)) return;
            glow.Clear();
            glow.SetColor("_EmissiveColor", colour);
            bodyRenderer.SetPropertyBlock(glow, index);
        }

        void OnDestroy()
        {
            foreach (Renderer r in hidden) if (r != null) r.enabled = true;
            Restore();
            hidden.Clear();
            if (top != null) Destroy(top);
        }

#if DEV
        public string Report()
        {
            var say = new StringBuilder();
            say.Append("tug on '").Append(vessel.vesselName).Append("': posed ").Append(posed ? "settled" : "at once").Append(" ").Append((Time.time - posedAt).ToString("F0")).Append(" s ago; ")
               .Append(hidden.Count).Append(" stock renderers hidden\n");
            say.Append("frame origin ").Append(frame.Origin.ToString("F3")).Append(" up ").Append(frame.Up.ToString("F3")).Append("; interior at ").Append(interior.localPosition.ToString("F3")).Append("\n");
            for (int i = 0; i < 4; i++)
            {
                Vector3 axle = top.transform.InverseTransformDirection(frame.Pivots[follows[i]].TransformDirection(axles[i]));
                say.Append(tyres[i].name).Append(" follows wheel ").Append(follows[i]).Append(" at ").Append(top.transform.InverseTransformPoint(frame.Pivots[follows[i]].position).ToString("F3"))
                   .Append("; steer ").Append((Mathf.Atan2(-axle.z, axle.x) * Mathf.Rad2Deg).ToString("F1")).Append(" lift ").Append((Lift(i) - rest[i]).ToString("F3"))
                   .Append(" spin ").Append(spin[i].ToString("F0")).Append("\n");
            }
            say.Append("lamps ").Append(lights.Count).Append(" (lights moved to the headlamps ").Append(beams.Count).Append(", on ");
            foreach (Beam b in beams) if (b.Light != null) say.Append(b.Light.enabled ? "y" : "n").Append(b.Light.shadows != LightShadows.None ? "s" : "");
            say.Append("), brakes ").Append(vessel.ActionGroups[KSPActionGroup.Brakes]).Append("\n");
            // what is drawn: the tug's pieces against the stock parts' renderers it hides (draws: one for each submesh)
            int ourTris = 0, ourDraws = 0, theirTris = 0, theirDraws = 0;
            foreach (MeshFilter mf in top.GetComponentsInChildren<MeshFilter>())
                if (mf.sharedMesh != null) for (int s = 0; s < mf.sharedMesh.subMeshCount; s++) { ourTris += (int)mf.sharedMesh.GetIndexCount(s) / 3; ourDraws++; }
            foreach (Renderer r in hidden)
            {
                if (r == null) continue;
                Mesh m = r is SkinnedMeshRenderer sk ? sk.sharedMesh : r.GetComponent<MeshFilter>()?.sharedMesh;
                if (m != null) for (int s = 0; s < m.subMeshCount; s++) { theirTris += (int)m.GetIndexCount(s) / 3; theirDraws++; }
            }
            say.Append("drawn: the tug ").Append(ourTris).Append(" triangles in ").Append(ourDraws).Append(" draws; the stock parts hidden ").Append(theirTris).Append(" triangles in ").Append(theirDraws).Append(" draws\n");
            say.Append("top active ").Append(top.activeInHierarchy).Append(" at ").Append((top.transform.position - vessel.CoM).ToString("F2")).Append(" from the CoM, scale ").Append(top.transform.lossyScale.ToString("F2")).Append("\n");
            foreach (MeshRenderer r in top.GetComponentsInChildren<MeshRenderer>(true))
            {
                MeshFilter mf = r.GetComponent<MeshFilter>();
                say.Append("  ").Append(r.name).Append(" enabled ").Append(r.enabled).Append(" visible ").Append(r.isVisible).Append(" layer ").Append(r.gameObject.layer)
                   .Append(" bounds ").Append((r.bounds.center - vessel.CoM).ToString("F2")).Append(" size ").Append(r.bounds.size.ToString("F2"))
                   .Append(" mesh ").Append(mf != null && mf.sharedMesh != null ? mf.sharedMesh.vertexCount + " verts, " + mf.sharedMesh.subMeshCount + " subs, bounds " + mf.sharedMesh.bounds.size.ToString("F2") : "none")
                   .Append(" materials");
                foreach (Material m in r.sharedMaterials) say.Append(" ").Append(m == null ? "null" : m.shader.name + "/" + m.renderQueue + (m.mainTexture != null ? "/tex " + m.mainTexture.width : "/no tex"));
                say.Append("\n");
            }
            return say.ToString();
        }
#endif
    }
}
