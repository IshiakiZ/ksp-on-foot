using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace OnFoot
{
    /// <summary>
    /// The crew car as the game has it in flight: which vessel is one, and its own frame of reference, worked out from where its
    /// parts are: up from the plane of its four wheels (towards the seat), forward from its crate (at the back) to its seat (in
    /// front), the origin midway between its wheels at the height of the ground under them. The body drawn over the car (see
    /// CarLook) is placed in that frame.
    /// </summary>
    public sealed class CarFrame
    {
        public const string Body = "roverBody.v2", Seat = "seatExternalCmd", Crate = "smallCargoContainer", WheelPart = "roverWheel1", Lamp = "spotLight1.v2";
        /// <summary>The stock wheel's own radius (RoveMax M1), as its part says.</summary>
        public const float WheelRadius = 0.378f;

        public Vessel Vessel;
        public Part Root, SeatPart, CratePart;
        public readonly List<Part> Wheels = new List<Part>();
        public readonly List<Transform> Pivots = new List<Transform>();
        public Transform SeatPivot;
        public Vector3 Origin, Up, Forward, Right;               // (in the world, when worked out)

        /// <summary>Whether a vessel is a crew car: the parts the car is made of, under the name it goes out with.</summary>
        public static bool Is(Vessel v)
        {
            if (v == null || !v.loaded || v.rootPart == null || v.rootPart.partInfo == null || v.rootPart.partInfo.name != Body) return false;
            if (v.vesselName == null || v.vesselName.IndexOf("Crew Car", System.StringComparison.OrdinalIgnoreCase) < 0) return false;
            int wheels = 0; bool seat = false, crate = false;
            foreach (Part p in v.parts)
            {
                if (p == null || p.partInfo == null) continue;
                if (p.partInfo.name == WheelPart) wheels++;
                else if (p.partInfo.name == Seat) seat = true;
                else if (p.partInfo.name == Crate) crate = true;
            }
            return wheels == 4 && seat && crate;
        }

        /// <summary>The frame of a crew car, or nothing if it cannot be worked out (a wheel gone, say).</summary>
        public static CarFrame Of(Vessel v)
        {
            if (!Is(v)) return null;
            var f = new CarFrame { Vessel = v, Root = v.rootPart };
            foreach (Part p in v.parts)
            {
                if (p.partInfo.name == WheelPart)
                {
                    Transform pivot = p.FindModelTransform("WheelPivot");
                    if (pivot == null) return null;
                    f.Wheels.Add(p); f.Pivots.Add(pivot);
                }
                else if (p.partInfo.name == Seat) { f.SeatPart = p; f.SeatPivot = p.FindModelTransform("seatPivot") ?? p.transform; }
                else if (p.partInfo.name == Crate) f.CratePart = p;
            }
            if (f.Pivots.Count != 4 || f.SeatPart == null || f.CratePart == null) return null;
            f.Work();
            return f;
        }

        /// <summary>Work the frame out from where the parts are now.</summary>
        public void Work()
        {
            Vector3 c = Vector3.zero;
            foreach (Transform t in Pivots) c += t.position;
            c /= Pivots.Count;
            // (the plane of the wheels: its normal turned towards the seat)
            Vector3 n = Vector3.zero;
            // (whatever order the wheels come in: the biggest cross product of pairs of their offsets from the middle)
            for (int i = 0; i < 4; i++)
                for (int j = 0; j < 4; j++)
                {
                    Vector3 cr = Vector3.Cross(Pivots[i].position - c, Pivots[j].position - c);
                    if (Vector3.Dot(cr, SeatPivot.position - c) < 0f) cr = -cr;
                    if (cr.sqrMagnitude > n.sqrMagnitude) n = cr;
                }
            Up = n.normalized;
            Vector3 along = Vector3.ProjectOnPlane(SeatPivot.position - CratePart.transform.position, Up);
            Forward = along.normalized;
            Right = Vector3.Cross(Up, Forward);
            Origin = c - Up * WheelRadius;
        }

        /// <summary>
        /// The radius a wheel part rolls on: its wheel module's radius made as large as the part is drawn (the game sizes the
        /// wheel's collider so, but works out its motor's rim speed from the radius alone): 0.4725 m for a RoveMax M1.
        /// </summary>
        public static float RollingRadius(Part wheel)
        {
            ModuleWheelBase w = wheel != null ? wheel.FindModuleImplementing<ModuleWheelBase>() : null;
            return w != null ? w.radius * wheel.rescaleFactor : WheelRadius;
        }

        /// <summary>A point of the world in the car's frame (x right, y up, z forward, from the origin).</summary>
        public Vector3 Local(Vector3 world)
        {
            Vector3 d = world - Origin;
            return new Vector3(Vector3.Dot(d, Right), Vector3.Dot(d, Up), Vector3.Dot(d, Forward));
        }

        public Quaternion Rotation => Quaternion.LookRotation(Forward, Up);

#if DEV
        /// <summary>For the development build: where everything of the car is, in its own frame.</summary>
        public string Report()
        {
            var say = new StringBuilder();
            say.Append("crew car '").Append(Vessel.vesselName).Append("': up ").Append(Up.ToString("F3")).Append(" forward ").Append(Forward.ToString("F3"))
               .Append("; root forward (its up) ").Append(Root.transform.up.ToString("F3")).Append(" = ").Append(Vector3.Dot(Root.transform.up, Forward).ToString("F2")).Append(" along the car\n");
            for (int i = 0; i < Pivots.Count; i++)
            {
                Transform t = Pivots[i];
                say.Append("wheel ").Append(i).Append(" pivot at ").Append(Local(t.position).ToString("F3"));
                // (which of its own axes lies along the car's right: that is its axle)
                Vector3[] axes = { t.right, t.up, t.forward };
                string[] names = { "x", "y", "z" };
                for (int k = 0; k < 3; k++) say.Append(" ").Append(names[k]).Append("·right ").Append(Vector3.Dot(axes[k], Right).ToString("F2"));
                ModuleWheels.ModuleWheelSteering steer = Wheels[i].FindModuleImplementing<ModuleWheels.ModuleWheelSteering>();
                say.Append(" steering ").Append(steer != null && steer.steeringEnabled);
                // (how far below the pivot the ground really is, and the wheel's own radius as the game rolls it)
                RaycastHit hit;
                if (Physics.Raycast(t.position, -Up, out hit, 3f, 1 << 15, QueryTriggerInteraction.Ignore))
                    say.Append(" ground ").Append(hit.distance.ToString("F3")).Append(" below");
                say.Append(" rolling radius ").Append(RollingRadius(Wheels[i]).ToString("F3")).Append("\n");
            }
            say.Append("seat pivot at ").Append(Local(SeatPivot.position).ToString("F3")).Append(", seat part at ").Append(Local(SeatPart.transform.position).ToString("F3")).Append("\n");
            say.Append("crate at ").Append(Local(CratePart.transform.position).ToString("F3")).Append(", from ").Append(Lowest(CratePart).ToString("F3")).Append(" to ").Append(Highest(CratePart).ToString("F3")).Append(" above the ground\n");
            say.Append("body (RoveMate) at ").Append(Local(Root.transform.position).ToString("F3")).Append(", from ").Append(Lowest(Root).ToString("F3")).Append(" to ").Append(Highest(Root).ToString("F3")).Append(" above the ground\n");
            foreach (Part p in Vessel.parts)
                foreach (Light l in p.GetComponentsInChildren<Light>(true))
                    say.Append("light on ").Append(p.partInfo.title).Append(" at ").Append(Local(l.transform.position).ToString("F3")).Append(" shining ").Append(new Vector3(Vector3.Dot(l.transform.forward, Right), Vector3.Dot(l.transform.forward, Up), Vector3.Dot(l.transform.forward, Forward)).ToString("F2")).Append("\n");
            say.Append("centre of mass at ").Append(Local(Vessel.CoM).ToString("F3")).Append("; mass ").Append(Vessel.totalMass.ToString("F3")).Append(" t; parts ").Append(Vessel.parts.Count);
            foreach (Part p in Vessel.parts)
            {
                Bounds e = Extent(p);
                say.Append("\n  part ").Append(p.partInfo.name).Append(" at ").Append(Local(p.transform.position).ToString("F3"))
                   .Append(" from ").Append(e.min.ToString("F3")).Append(" to ").Append(e.max.ToString("F3"));
                // (a seated kerbal's hands, head and feet, from the bones of its skeleton)
                if (!p.isKerbalEVA()) continue;
                int n = 0;
                foreach (Transform t in p.GetComponentsInChildren<Transform>(true))
                {
                    string low = t.name.ToLowerInvariant();
                    if (!(low.Contains("hand") || low.Contains("wrist") || low.Contains("palm") || low.Contains("head") || low.Contains("helmet") || low.Contains("foot") || low.Contains("ankle") || low.Contains("knee") || low.Contains("pelvis") || low.Contains("hip"))) continue;
                    if (++n > 30) break;
                    say.Append("\n    bone ").Append(t.name).Append(" at ").Append(Local(t.position).ToString("F3"));
                }
            }
            return say.ToString();
        }

        /// <summary>The box (in the car's frame) round everything a part draws (its meshes' corners; a kerbal's skinned meshes too).</summary>
        Bounds Extent(Part p)
        {
            bool any = false;
            var box = new Bounds();
            foreach (Renderer r in p.GetComponentsInChildren<Renderer>())
            {
                if (!r.gameObject.activeInHierarchy || r is ParticleSystemRenderer || Ours(r.transform)) continue;
                Mesh mesh = r is SkinnedMeshRenderer s ? s.sharedMesh : r.GetComponent<MeshFilter>()?.sharedMesh;
                if (mesh == null) continue;
                Bounds b = r is SkinnedMeshRenderer sk ? sk.localBounds : mesh.bounds;
                Transform t = r is SkinnedMeshRenderer sm && sm.rootBone != null ? sm.rootBone : r.transform;
                for (int k = 0; k < 8; k++)
                {
                    Vector3 corner = b.center + Vector3.Scale(b.extents, new Vector3((k & 1) == 0 ? -1 : 1, (k & 2) == 0 ? -1 : 1, (k & 4) == 0 ? -1 : 1));
                    Vector3 at = Local(t.TransformPoint(corner));
                    if (!any) { box = new Bounds(at, Vector3.zero); any = true; }
                    else box.Encapsulate(at);
                }
            }
            return box;
        }

        /// <summary>Whether a transform is the tug's (drawn over the car by CarLook), not the part's own.</summary>
        static bool Ours(Transform t)
        {
            for (; t != null; t = t.parent) if (t.name == "OnFoot tug") return true;
            return false;
        }

        /// <summary>How high above the ground the lowest point of a part's own meshes is (its corners of the box round each mesh).</summary>
        float Lowest(Part p) => Heights(p).x;
        float Highest(Part p) => Heights(p).y;

        Vector2 Heights(Part p)
        {
            float lo = float.MaxValue, hi = float.MinValue;
            foreach (MeshFilter mf in p.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null) continue;
                Bounds b = mf.sharedMesh.bounds;
                for (int k = 0; k < 8; k++)
                {
                    Vector3 corner = b.center + Vector3.Scale(b.extents, new Vector3((k & 1) == 0 ? -1 : 1, (k & 2) == 0 ? -1 : 1, (k & 4) == 0 ? -1 : 1));
                    float h = Local(mf.transform.TransformPoint(corner)).y;
                    lo = Mathf.Min(lo, h); hi = Mathf.Max(hi, h);
                }
            }
            return new Vector2(lo, hi);
        }
#endif
    }
}
