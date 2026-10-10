using System.Collections.Generic;
using Keystone;
using UnityEngine;

namespace OnFoot
{
    /// <summary>
    /// The assembly building or the hangar (the game's editor scene), as a place to walk.
    ///
    /// What the game has there: the building is drawn by one camera (layer 15, its light painted on) and the craft
    /// and the ground crew by another, both on one rig that the editor's own camera script (VABCamera or SPHCamera)
    /// swings round the craft. Nothing in the building is solid: nobody was ever meant to stand in it. So for the
    /// walk each of its meshes is given a collider of the same shape, in the same place, and a floor is laid at the
    /// height of its floor; all of it is taken away again when the walk ends.
    /// </summary>
    public sealed class EditorPlace : Place
    {
        public const int BuildingLayer = 15, PartsLayer = 0, CrewLayer = 16;

        EditorFacility facility;
        Transform rig, eye;                       // what the editor's camera script moves; the camera whose view is the picture
        Vector3 rigPosWas; Quaternion rigRotWas;  // (local to the rig's parent, which is the script's pivot)
        Vector3 eyeWas; Quaternion lookWas;       // (in the world)
        float fovWas;
        Behaviour orbit;                          // the editor's camera script that was running
        readonly List<Behaviour> stopped = new List<Behaviour>();
        List<Kept> cameras;
        readonly List<CameraOffCenter> offCentre = new List<CameraOffCenter>();
        readonly List<Vector2> offCentreWas = new List<Vector2>();
        GameObject solid;                         // everything made to stand on, under one object
        readonly List<GameObject> made = new List<GameObject>();
        float floor;
        Bounds building;
        int meshes, unreadable, skipped, crew;
        double madeMs;
        bool begun;

        public float Floor => floor;
        public Bounds Building => building;

        public static EditorPlace Make(out string why)
        {
            why = null;
            if (EditorLogic.fetch == null || EditorDriver.fetch == null || EditorCamera.Instance == null) { why = "The building is not ready yet."; return null; }
            if (EditorDriver.fetch.restartingEditor) { why = "The building is still being set up."; return null; }
            if (EditorLogic.SelectedPart != null) { why = "Put down the part you are holding first."; return null; }
            // (one of the game's own windows is open over the editor, the craft browser say: they hold off picking parts while they are)
            // (the editor's own states hold the same things off and are not windows: "EditorLogic_rootPartMode" while there is no craft yet)
            foreach (KeyValuePair<string, ulong> l in InputLockManager.lockStack)
                if (l.Key != LockName && l.Key != "CameraMouseLook" && !l.Key.StartsWith("EditorLogic", System.StringComparison.Ordinal) && !l.Key.StartsWith("EditorDriver", System.StringComparison.Ordinal)
                    && (l.Value & (ulong)ControlTypes.EDITOR_PAD_PICK_PLACE) != 0) { why = "Close the window that is open first."; return null; }
            return new EditorPlace { atDoor = AtDoor };
        }

        /// <summary>The next walk made begins at the building's door (it was walked into from the space centre).</summary>
        public static bool AtDoor;
        bool atDoor;

        public override string About => (facility == EditorFacility.SPH ? "the hangar" : "the assembly building") + ": " + meshes + " of its pieces made solid (" + already + " were already)"
                                        + (unreadable > 0 ? ", " + unreadable + " could not be" : "") + (walls.Length > 0 ? "; " + walls : "") + "; " + parts + " of the craft's shapes, " + crew + " of the ground crew and " + vehicles + " of their vehicles; in " + madeMs.ToString("F0") + " ms; floor at " + floor.ToString("F2") + " m.";

        public override int Solid => (1 << BuildingLayer) | (1 << PartsLayer) | (1 << WalkLayer);

        public override bool Still =>
            begun && HighLogic.LoadedSceneIsEditor && EditorDriver.fetch != null && !EditorDriver.fetch.restartingEditor && EditorDriver.editorFacility == facility && rig != null;

        public override void Begin()
        {
            facility = EditorDriver.editorFacility;
            EditorCamera camera = EditorCamera.Instance;
            rig = camera.transform;
            eye = camera.cam != null ? camera.cam.transform : rig;
            rigPosWas = rig.localPosition; rigRotWas = rig.localRotation;
            eyeWas = eye.position; lookWas = eye.rotation;
            fovWas = camera.cam != null ? camera.cam.fieldOfView : 60f;

            // ---- the editor's own camera script stops moving the rig (it is the one of the two that is running)
            orbit = null;
            var vab = rig.GetComponent<VABCamera>();
            var sph = rig.GetComponent<SPHCamera>();
            if (vab != null && vab.enabled) orbit = vab;
            else if (sph != null && sph.enabled) orbit = sph;
            if (orbit != null) orbit.enabled = false;

            // ---- the cameras on the rig: their reach and angle, and the shift that leaves room for the parts list (there is no list while walking)
            var cams = new List<Camera>(rig.GetComponentsInChildren<Camera>());
            cameras = Keep(cams);
            foreach (Camera c in cams)
            {
                if (c.nearClipPlane > 0.05f) c.nearClipPlane = 0.05f;
                var shift = c.GetComponent<CameraOffCenter>();
                if (shift != null) { offCentre.Add(shift); offCentreWas.Add(new Vector2(shift.x, shift.y)); }
                var keeper = c.GetComponent<EditorCameraOffset>();
                if (keeper != null && keeper.enabled) { keeper.enabled = false; stopped.Add(keeper); }
            }

            // ---- the player's controls: the editor's keys and clicks held off (W, A, S, D would turn a part; the mouse would pick one)
            InputLockManager.SetControlLock(ControlTypes.EDITOR_LOCK | ControlTypes.EDITOR_UI | ControlTypes.EDITOR_CREW, LockName);
            locked = true;
            TakePointer();
            pointerTaken = true;
            HideInterface();
            begun = true;

            MakeSolid();
        }

        bool locked, pointerTaken;

        /// <summary>One step of putting things back: whatever goes wrong in it, the others are still done.</summary>
        static void Try(string what, System.Action step)
        {
            try { step(); }
            catch (System.Exception ex) { Kit.Log(Foot.Name, "putting back " + what + ": " + ex.Message); }
        }

        public override void Look(Vector3 at, Quaternion facing, float fieldOfView, float walking)
        {
            if (rig == null) return;
            // (the rig is placed so that the camera that makes the picture is where the eye is, whatever its offset on the rig)
            Quaternion inner = Quaternion.Inverse(rig.rotation) * eye.rotation;
            Vector3 offset = rig.InverseTransformPoint(eye.position);
            rig.rotation = facing * Quaternion.Inverse(inner);
            rig.position = at - rig.rotation * Vector3.Scale(offset, rig.lossyScale);
            if (cameras != null)
                foreach (Kept k in cameras) if (k.Cam != null) k.Cam.fieldOfView = fieldOfView;
            // (the picture is shifted sideways for the parts list while the game has the camera; for walking it is not: eased between the two)
            for (int i = 0; i < offCentre.Count; i++) if (offCentre[i] != null) { offCentre[i].x = offCentreWas[i].x * (1f - walking); offCentre[i].y = offCentreWas[i].y * (1f - walking); }
        }

        public override void Was(out Vector3 at, out Quaternion facing, out float fieldOfView)
        {
            at = eyeWas; facing = lookWas; fieldOfView = fovWas;
        }

        public override void End()
        {
            bool same = HighLogic.LoadedSceneIsEditor && EditorDriver.editorFacility == facility && EditorDriver.fetch != null && !EditorDriver.fetch.restartingEditor;
            Try("what was made solid", () =>
            {
                foreach (GameObject g in made) if (g != null) Object.Destroy(g);
                made.Clear();
                if (solid != null) Object.Destroy(solid);
                solid = null;
            });
            Try("the cameras", () =>
            {
                for (int i = 0; i < offCentre.Count; i++) if (offCentre[i] != null) { offCentre[i].x = offCentreWas[i].x; offCentre[i].y = offCentreWas[i].y; }
                offCentre.Clear(); offCentreWas.Clear();
                foreach (Behaviour b in stopped) if (b != null) b.enabled = true;
                stopped.Clear();
                PutBack(cameras);
                cameras = null;
            });
            // The rig goes back where the editor's script had it, and the script takes it on from there. (If the editor has gone over to
            // the other building meanwhile, its own script for that one has the rig already: it is left to it.)
            Try("the editor's camera", () =>
            {
                if (rig != null && same)
                {
                    rig.localPosition = rigPosWas; rig.localRotation = rigRotWas;
                    if (orbit != null) orbit.enabled = true;
                }
                orbit = null;
            });
            Try("the controls", () => { if (locked) InputLockManager.RemoveControlLock(LockName); locked = false; });
            Try("the pointer", () => { if (pointerTaken) GivePointer(); pointerTaken = false; });
            Try("the interface", ShowInterface);
            begun = false;
        }

        // ---- the building made solid

        /// <summary>The layer of what is made solid only for the walk and is not the building: the craft's parts, the crew. (Nothing in the editor looks for things on it.)</summary>
        public const int WalkLayer = 19;

        /// <summary>The editor's own scenery: the building, and the space centre round it that is seen through its doors. Not the planet's own local scenery, which the scene also holds, far off.</summary>
        static bool Ours(Transform t) => t.root.name != "localSpace" && t.position.sqrMagnitude < 20000f * 20000f;

        /// <summary>Whether something already has a collider of its own that stops things (the space centre's ground, buildings and pad have).</summary>
        static bool Stops(GameObject g)
        {
            foreach (Collider c in g.GetComponents<Collider>()) if (c.enabled && !c.isTrigger) return true;
            return false;
        }

        void MakeSolid()
        {
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            solid = new GameObject("On Foot: the building made solid");
            meshes = unreadable = skipped = already = 0;
            var shell = new List<Renderer>();
            bool some = false;
            foreach (MeshFilter mf in Object.FindObjectsOfType<MeshFilter>())
            {
                if (mf.gameObject.layer != BuildingLayer || !mf.gameObject.activeInHierarchy || !Ours(mf.transform)) continue;
                Renderer r = mf.GetComponent<Renderer>();
                Mesh m = mf.sharedMesh;
                if (r == null || m == null || !r.enabled) continue;
                // (the room: what of the building stands round the middle of the floor, the outside's ground and the other buildings left out)
                Vector3 c = r.bounds.center, size = r.bounds.size;
                if (c.x * c.x + c.z * c.z < 100f * 100f && size.x < 200f && size.z < 200f)
                {
                    if (!some) { building = r.bounds; some = true; } else building.Encapsulate(r.bounds);
                }
                // (pools of light laid on the floor, glows, glass: drawn after everything solid, and nothing to stand on)
                Material mat = r.sharedMaterial;
                if (mat == null || mat.renderQueue > 2450) { skipped++; continue; }
                if (Stops(mf.gameObject)) { already++; continue; }
                if (!m.isReadable)
                {
                    // (a piece the game combined with others when it was built: its shape cannot be read. If it is a room's shell, its walls are found by looking at them, below.)
                    if ((size.x > 15f && size.z > 15f && size.y > 8f) || r.isPartOfStaticBatch) shell.Add(r);
                    else unreadable++;
                    continue;
                }
                var piece = new GameObject("On Foot: solid " + mf.name) { layer = BuildingLayer };
                piece.transform.SetParent(mf.transform, false);
                var collider = piece.AddComponent<MeshCollider>();
                collider.sharedMesh = m;
                made.Add(piece);
                meshes++;
            }

            // ---- the floor: at the height of the sheet the craft's shadow is thrown on, else the bottom of the space a craft is built in
            floor = 0f;
            bool found = false;
            foreach (Renderer r in Object.FindObjectsOfType<Renderer>())
            {
                Material m = r.sharedMaterial;
                if (m == null || m.shader == null || !m.shader.name.Contains("Shadow Receiver")) continue;
                floor = r.bounds.center.y; found = true;
                break;
            }
            if (!found && EditorBounds.Instance != null) floor = EditorBounds.Instance.constructionBounds.min.y;
            if (!some) building = new Bounds(new Vector3(0f, floor + 25f, 0f), new Vector3(100f, 50f, 100f));

            // ---- the shell's walls, where it could not be made solid from its shape
            walls = "";
            hasShell = shell.Count > 0;
            if (hasShell) { shellRoom = shell[0].bounds; foreach (Renderer r in shell) shellRoom.Encapsulate(r.bounds); }
            if (shell.Count > 0)
            {
                try { walls = Shell.Walls(shell, floor, solid.transform, BuildingLayer, made); }
                catch (System.Exception ex) { walls = "the walls could not be found (" + ex.Message + ")"; Kit.Log(Foot.Name, walls + ": " + ex); }
            }

            // (a slab under the room and a little round it, its top a hair under the floor that is drawn: where the drawn floor is solid it is stood on, elsewhere this is)
            var slab = new GameObject("On Foot: floor") { layer = BuildingLayer };
            slab.transform.SetParent(solid.transform, false);
            var box = slab.AddComponent<BoxCollider>();
            box.size = new Vector3(building.size.x + 30f, 2f, building.size.z + 30f);
            box.center = new Vector3(building.center.x, floor - 0.02f - 1f, building.center.z);

            // ---- the craft. In the editor its parts' shapes are only "triggers" (things pass through them: that is what lets a part be
            // dragged through others); for the walk each gets a solid copy, on a layer of the walk's own.
            parts = 0;
            if (EditorLogic.fetch != null && EditorLogic.fetch.ship != null)
                foreach (Part p in EditorLogic.fetch.ship.parts)
                {
                    if (p == null) continue;
                    foreach (Collider c in p.GetComponentsInChildren<Collider>())
                    {
                        if (!c.enabled || !c.isTrigger || c.gameObject.layer != PartsLayer || c is SphereCollider) continue;     // (spheres: the attachment nodes' markers)
                        string n = c.name.ToLowerInvariant();
                        if (c.tag == "Ladder" || c.tag == "Airlock" || n.Contains("airlock") || n.Contains("ladder")) continue;
                        GameObject copy = null;
                        if (c is MeshCollider mc && mc.sharedMesh != null)
                        {
                            copy = new GameObject("On Foot: solid part") { layer = WalkLayer };
                            copy.transform.SetParent(c.transform, false);
                            var m = copy.AddComponent<MeshCollider>(); m.sharedMesh = mc.sharedMesh; m.convex = mc.convex;
                        }
                        else if (c is BoxCollider bc)
                        {
                            copy = new GameObject("On Foot: solid part") { layer = WalkLayer };
                            copy.transform.SetParent(c.transform, false);
                            var b = copy.AddComponent<BoxCollider>(); b.center = bc.center; b.size = bc.size;
                        }
                        else if (c is CapsuleCollider cc)
                        {
                            copy = new GameObject("On Foot: solid part") { layer = WalkLayer };
                            copy.transform.SetParent(c.transform, false);
                            var k = copy.AddComponent<CapsuleCollider>(); k.center = cc.center; k.radius = cc.radius; k.height = cc.height; k.direction = cc.direction;
                        }
                        if (copy == null) continue;
                        made.Add(copy);
                        parts++;
                    }
                }

            // ---- the ground crew: each as solid as a kerbal standing there (a capsule that goes about with them). They do not know
            // anyone is walking among them: one who walks into the walker pushes the walker aside.
            crew = 0;
            foreach (global::SpaceCenterCrew one in Object.FindObjectsOfType<global::SpaceCenterCrew>())
            {
                Bounds b = new Bounds(); bool any = false;
                foreach (Renderer r in one.GetComponentsInChildren<Renderer>())
                {
                    if (!r.enabled) continue;
                    if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
                }
                if (!any || b.size.y < 0.3f || b.size.y > 3f) continue;
                // (upright in the world and at the world's scale whatever the model's own axes are; from the floor under the kerbal to
                // the top of its head, as wide as a kerbal's body and not its arms. It turns with the kerbal about the upright, which
                // leaves an upright capsule as it is.)
                var piece = new GameObject("On Foot: solid crew") { layer = WalkLayer };
                piece.transform.SetParent(one.transform, true);
                piece.transform.position = new Vector3(b.center.x, b.min.y, b.center.z);
                piece.transform.rotation = Quaternion.identity;
                Vector3 scale = one.transform.lossyScale;
                float s = Mathf.Max(1e-4f, (Mathf.Abs(scale.x) + Mathf.Abs(scale.y) + Mathf.Abs(scale.z)) / 3f);
                piece.transform.localScale = Vector3.one / s;
                var capsule = piece.AddComponent<CapsuleCollider>();
                capsule.direction = 1;
                capsule.height = b.size.y;
                capsule.radius = Mathf.Min(0.24f, 0.5f * Mathf.Min(b.size.x, b.size.z));
                capsule.center = new Vector3(0f, 0.5f * b.size.y, 0f);
                made.Add(piece);
                crew++;
            }

            // ---- the crew's trucks and carts (big pieces on the crew's layer that are not kerbals): a box each, round what is drawn,
            // going about with it
            vehicles = 0;
            var kerbals = new HashSet<Transform>();
            foreach (global::SpaceCenterCrew one in Object.FindObjectsOfType<global::SpaceCenterCrew>()) kerbals.Add(one.transform);
            foreach (Renderer r in Object.FindObjectsOfType<Renderer>())
            {
                if (r.gameObject.layer != CrewLayer || !r.enabled || !Ours(r.transform)) continue;
                Vector3 size = r.bounds.size;
                if (Mathf.Max(size.x, size.z) < 1.4f || size.y < 0.3f || size.x > 20f || size.z > 20f) continue;
                bool kerbal = false;
                for (Transform t = r.transform; t != null; t = t.parent) if (kerbals.Contains(t)) { kerbal = true; break; }
                if (kerbal) continue;
                var piece = new GameObject("On Foot: solid vehicle") { layer = WalkLayer };
                piece.transform.SetParent(r.transform, true);
                piece.transform.position = r.bounds.center;
                piece.transform.rotation = Quaternion.identity;
                Vector3 scale = r.transform.lossyScale;
                piece.transform.localScale = new Vector3(1f / Mathf.Max(1e-4f, Mathf.Abs(scale.x)), 1f / Mathf.Max(1e-4f, Mathf.Abs(scale.y)), 1f / Mathf.Max(1e-4f, Mathf.Abs(scale.z)));
                var hull = piece.AddComponent<BoxCollider>();
                hull.size = size;
                made.Add(piece);
                vehicles++;
            }

            madeMs = (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            Kit.Log(Foot.Name, About);
        }

        int vehicles;

        int already, parts;
        string walls = "";
        bool hasShell;
        Bounds shellRoom;

        // ---- where to set off

        public override bool Start(out Vector3 feet, out float heading, out float pitch)
        {
            Vector3 forward = lookWas * Vector3.forward;
            heading = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
            pitch = 0f;
            if (atDoor && Door(out feet, out heading)) { atDoor = false; return true; }
            atDoor = false;
            // Straight down from where the game's camera was, onto the first thing that can be stood on; if the body does not fit there,
            // the nearest place round about where it does; failing all that, the middle of the floor.
            Vector3 from = eyeWas;
            from.y = Mathf.Min(from.y, building.max.y - 1f);
            for (int ring = 0; ring <= 12; ring++)
            {
                int around = ring == 0 ? 1 : 8 + ring * 4;
                for (int k = 0; k < around; k++)
                {
                    float angle = (k + 0.5f * ring) / around * Mathf.PI * 2f, far = ring * 2.5f;
                    Vector3 here = from + new Vector3(Mathf.Sin(angle) * far, 0f, Mathf.Cos(angle) * far);
                    if (Ground(here, out feet) && Fits(feet)) return true;
                }
            }
            feet = new Vector3(building.center.x, floor, building.center.z);
            return Fits(feet);
        }

        /// <summary>
        /// Just inside the building's main doorway, facing in: for a walk that came in from the space centre. The doorway is found as
        /// the way out: from the middle of the floor, at a kerbal's waist, lines are drawn all round against the building made solid;
        /// the widest spread of them that get out of the room is the doorway. A few steps in from where they leave the room.
        /// </summary>
        bool Door(out Vector3 feet, out float heading)
        {
            feet = Vector3.zero; heading = 0f;
            Vector3 middle = new Vector3(building.center.x, floor + 0.6f, building.center.z);
            float reach = Mathf.Max(building.extents.x, building.extents.z) * 1.5f + 20f;
            const int around = 360;
            var gets = new bool[around];
            int any = 0;
            for (int i = 0; i < around; i++)
            {
                float a = i * Mathf.PI * 2f / around;
                gets[i] = !Physics.Raycast(middle, new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)), reach, 1 << BuildingLayer, QueryTriggerInteraction.Ignore);
                if (gets[i]) any++;
            }
            if (any == 0 || any == around) return false;
            // (the longest run of ways out, going round the circle from a way that is shut)
            int shut = System.Array.IndexOf(gets, false), best = 0, bestAt = 0, run = 0, runAt = 0;
            for (int k = 1; k <= around; k++)
            {
                int i = (shut + k) % around;
                if (gets[i]) { if (run == 0) runAt = k; run++; if (run > best) { best = run; bestAt = runAt; } }
                else run = 0;
            }
            float mid = (shut + bestAt + 0.5f * (best - 1)) * Mathf.PI * 2f / around;
            Vector3 outward = new Vector3(Mathf.Sin(mid), 0f, Mathf.Cos(mid));
            // (where the building ends that way: the shell's outline, where the walls were found by looking at the shell; otherwise
            // the first point going out from which a line straight up no longer meets a roof)
            float plane = 0f;
            if (hasShell)
            {
                if (Mathf.Abs(outward.x) > 1e-3f) plane = ((outward.x > 0f ? shellRoom.max.x : shellRoom.min.x) - middle.x) / outward.x;
                if (Mathf.Abs(outward.z) > 1e-3f) { float z = ((outward.z > 0f ? shellRoom.max.z : shellRoom.min.z) - middle.z) / outward.z; plane = plane > 0f ? Mathf.Min(plane, z) : z; }
            }
            else
                for (float d = 2f; d < reach; d += 1f)
                    if (!Physics.Raycast(middle + outward * d, Vector3.up, 200f, 1 << BuildingLayer, QueryTriggerInteraction.Ignore)) { plane = d; break; }
            if (plane < 2f) return false;
            // (a few steps outside it, looking in, so that one walks in through the door; failing room there, just inside)
            // (and with the way in clear for a few metres: a truck may be parked in the doorway; failing that, any spot that fits)
            Vector3 along = Vector3.Cross(Vector3.up, outward);
            for (int pass = 0; pass < 2; pass++)
            foreach (float off in new[] { 4f, 6f, 2.5f, -2f, -4f })
            foreach (float side in new[] { 0f, 3f, -3f, 6f, -6f, 9f, -9f })
                if (Ground(middle + outward * (plane + off) + along * side + Vector3.up * 2f, out feet) && Fits(feet)
                    && (pass == 1 || !Physics.SphereCast(feet + Vector3.up * 0.6f, 0.3f, -outward, out RaycastHit _, 7f, Solid, QueryTriggerInteraction.Ignore)))
                {
                    heading = Mathf.Atan2(-outward.x, -outward.z) * Mathf.Rad2Deg;
                    Kit.Log(Foot.Name, "walked in by the doorway " + (best * 360f / around).ToString("F0") + "° wide, " + plane.ToString("F1") + " m from the middle; standing " + off.ToString("F1") + " m outside it, looking " + heading.ToString("F0") + "°");
                    return true;
                }
            return false;
        }

        /// <summary>The first floor of the building (anything of it facing up) straight under a point. (Not the craft: a walk does not begin on top of a rocket.)</summary>
        bool Ground(Vector3 above, out Vector3 feet)
        {
            feet = above;
            RaycastHit[] hits = Physics.RaycastAll(above, Vector3.down, Mathf.Max(5f, above.y - floor + 5f), 1 << BuildingLayer, QueryTriggerInteraction.Ignore);
            // (the floor itself if it is there; failing that, the first thing to stand on going down: a gantry, a stair)
            float best = float.MaxValue, nearFloor = float.MaxValue;
            bool any = false, onFloor = false;
            Vector3 floorAt = above;
            foreach (RaycastHit h in hits)
            {
                if (h.normal.y < 0.7f) continue;
                float off = Mathf.Abs(h.point.y - floor);
                if (off < 1f && off < nearFloor) { nearFloor = off; floorAt = h.point; onFloor = true; }
                if (h.distance < best) { best = h.distance; feet = h.point; any = true; }
            }
            if (onFloor) feet = floorAt;
            return any;
        }

        bool Fits(Vector3 feet)
        {
            float radius = Walk.Radius, high = Walk.Height;
            Vector3 low = feet + Vector3.up * (radius + 0.05f), top = feet + Vector3.up * (high - radius);
            return !Physics.CheckCapsule(low, top, radius, Solid, QueryTriggerInteraction.Ignore);
        }
    }
}
