#if DEV
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OnFoot
{
    /// <summary>
    /// For the development build: the walk driven by a script instead of the keyboard (the bridge cannot press keys).
    /// A script is steps separated by spaces, each some keys and a time: "w2" walks forward two seconds, "wr3" runs
    /// forward three, "d1.5" sidesteps right, "wj0.4" walks and jumps, "-1" stands still a second, "t90" turns 90° right at once,
    /// "p-20" tilts the view 20° down at once.
    /// </summary>
    public static class Scripted
    {
        struct Step { public float Along, Across, Time, Turn, Tilt; public bool Run, Jump; }
        static readonly List<Step> steps = new List<Step>();
        static int at;
        static float stepBegan;
        static bool jumped;

        public static bool Active => at < steps.Count;

        /// <summary>(the walk's own clock, so that a script keeps step with the walk however the frames are timed)</summary>
        static float Now0 => Foot.Current != null ? Foot.Current.Clock : Time.unscaledTime;

        internal static void Load(string script)
        {
            steps.Clear(); at = 0; stepBegan = Now0; jumped = false;
            foreach (string word in (script ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var s = new Step();
                int i = 0;
                if (word[0] == 't' || word[0] == 'p')
                {
                    float.TryParse(word.Substring(1), NumberStyles.Float, CultureInfo.InvariantCulture, out float by);
                    if (word[0] == 't') s.Turn = by; else s.Tilt = by;
                    steps.Add(s);
                    continue;
                }
                for (; i < word.Length && char.IsLetter(word[i]); i++)
                    switch (word[i])
                    {
                        case 'w': s.Along += 1f; break;
                        case 's': s.Along -= 1f; break;
                        case 'a': s.Across -= 1f; break;
                        case 'd': s.Across += 1f; break;
                        case 'r': s.Run = true; break;
                        case 'j': s.Jump = true; break;
                    }
                if (i < word.Length && word[i] == '-') i++;
                float.TryParse(word.Substring(i), NumberStyles.Float, CultureInfo.InvariantCulture, out s.Time);
                steps.Add(s);
            }
        }

        internal static Driven Now()
        {
            var d = new Driven();
            while (at < steps.Count)
            {
                Step s = steps[at];
                if (s.Turn != 0f || s.Tilt != 0f)
                {
                    if (Foot.Current != null) Foot.Current.Face(Foot.Current.Heading + s.Turn, Foot.Current.Pitch + s.Tilt);
                    at++; stepBegan = Now0; jumped = false;
                    continue;
                }
                if (Now0 - stepBegan >= s.Time) { at++; stepBegan = Now0; jumped = false; continue; }
                d.Along = s.Along; d.Across = s.Across; d.Run = s.Run;
                if (s.Jump && !jumped) { d.Jump = true; jumped = true; }
                return d;
            }
            return d;
        }
    }

    /// <summary>For the development build only: looking into the scenes (through the bridge's dev_call) and driving a walk.</summary>
    public static class Probe
    {
        static string Path(Transform t)
        {
            var parts = new List<string>();
            for (int i = 0; t != null && i < 7; i++, t = t.parent) parts.Insert(0, t.name);
            return string.Join("/", parts.ToArray());
        }

        static string V(Vector3 v) => v.ToString("F2");
        static float F(string text, float otherwise = 0f) => float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float f) ? f : otherwise;

        // ---- the walk

        public static string Go()
        {
            Feet.Begin();
            return Foot.Current != null ? "walking: " + Foot.Current.Stats() : "not walking: " + Foot.WhyNot;
        }

        public static string Stop() { Feet.Stop(); return "stopping"; }
        public static string StopNow() { Feet.Stop(true); return "stopped"; }
        public static string Stats() => Foot.Current != null ? Foot.Current.Stats() : "not walking" + (string.IsNullOrEmpty(Foot.WhyNot) ? "" : ": " + Foot.WhyNot);

        /// <summary>arg "heading pitch" in degrees.</summary>
        public static string Face(string arg)
        {
            if (Foot.Current == null) return "not walking";
            string[] p = arg.Split(' ');
            Foot.Current.Face(F(p[0]), p.Length > 1 ? F(p[1]) : 0f);
            return Foot.Current.Stats();
        }

        /// <summary>arg "x y z": put the body's feet there.</summary>
        public static string Put(string arg)
        {
            if (Foot.Current == null) return "not walking";
            string[] p = arg.Split(' ');
            Foot.Current.Put(new Vector3(F(p[0]), F(p[1]), F(p[2])));
            return Foot.Current.Stats();
        }

        public static string Script(string arg) { Scripted.Load(arg); return "script loaded: " + arg; }

        /// <summary>Turn to face the craft in the editor (arg: how far up it to look, 0 its foot to 1 its top; 0.3 if not given).</summary>
        public static string FaceCraft() => FaceCraft("0.3");

        public static string FaceCraft(string arg)
        {
            if (Foot.Current == null) return "not walking";
            if (EditorLogic.fetch == null || EditorLogic.fetch.ship == null || EditorLogic.fetch.ship.parts.Count == 0) return "no craft";
            Bounds all = new Bounds(); bool some = false;
            foreach (Part p in EditorLogic.fetch.ship.parts) foreach (Renderer r in p.GetComponentsInChildren<Renderer>()) { if (!some) { all = r.bounds; some = true; } else all.Encapsulate(r.bounds); }
            // (from the floor up: a craft on launch clamps reaches far below the floor, where the clamps would meet the ground at the pad)
            float bottom = Foot.Current.Place is EditorPlace ep ? Mathf.Max(all.min.y, ep.Floor) : all.min.y;
            Vector3 target = new Vector3(all.center.x, bottom + (all.max.y - bottom) * F(arg, 0.3f), all.center.z);
            Vector3 eye = Foot.Current.transform.position + Vector3.up * Foot.eye.Value;
            Vector3 to = target - eye;
            float heading = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg, pitch = -Mathf.Atan2(to.y, new Vector2(to.x, to.z).magnitude) * Mathf.Rad2Deg;
            Foot.Current.Face(heading, pitch);
            return "facing the craft " + new Vector2(to.x, to.z).magnitude.ToString("F1") + " m away: " + Foot.Current.Stats();
        }

        /// <summary>The controls held off just now, the pointer, and whether the interface is showing.</summary>
        public static string Locks()
        {
            var say = new StringBuilder();
            foreach (KeyValuePair<string, ulong> l in InputLockManager.lockStack) say.Append(l.Key).Append("=").Append((ControlTypes)l.Value).Append("; ");
            say.Append("\npointer ").Append(Cursor.lockState).Append(Cursor.visible ? " shown" : " hidden").Append("; interface ").Append(KSP.UI.UIMasterController.Instance != null && KSP.UI.UIMasterController.Instance.IsUIShowing ? "showing" : "hidden")
               .Append("; walking ").Append(Foot.Current != null).Append("; scene ").Append(HighLogic.LoadedScene);
            if (EditorCamera.Instance != null)
            {
                Transform rig = EditorCamera.Instance.transform;
                var vab = rig.GetComponent<VABCamera>(); var sph = rig.GetComponent<SPHCamera>();
                say.Append("; editor camera scripts: VAB ").Append(vab != null && vab.enabled).Append(", SPH ").Append(sph != null && sph.enabled).Append("; rig local ").Append(V(rig.localPosition)).Append(" ").Append(rig.localRotation.eulerAngles.ToString("F1"));
                foreach (Camera c in rig.GetComponentsInChildren<Camera>()) say.Append("; ").Append(c.name).Append(" near ").Append(c.nearClipPlane.ToString("F2")).Append(" fov ").Append(c.fieldOfView.ToString("F1"));
                foreach (CameraOffCenter o in rig.GetComponentsInChildren<CameraOffCenter>()) say.Append("; off centre ").Append(o.x.ToString("F3"));
            }
            int ours = 0;
            foreach (GameObject g in UnityEngine.Object.FindObjectsOfType<GameObject>()) if (g.name.StartsWith("On Foot", StringComparison.Ordinal)) ours++;
            say.Append("; objects of ours left: ").Append(ours);
            return say.ToString();
        }

        /// <summary>Which way is up where the scene's camera is (the planet's up, against the world's y), and what is under it.</summary>
        public static string Up()
        {
            Camera cam = Camera.main;
            if (cam == null) foreach (Camera c in Camera.allCameras) if (c.targetTexture == null && (c.cullingMask & (1 << 15)) != 0) { cam = c; break; }
            if (cam == null) return "no camera";
            CelestialBody home = FlightGlobals.GetHomeBody();
            var say = new StringBuilder();
            say.Append("camera ").Append(cam.name).Append(" at ").Append(V(cam.transform.position));
            if (home != null)
            {
                Vector3 up = ((Vector3d)cam.transform.position - home.position).normalized;
                say.Append("; ").Append(home.bodyName).Append(" centre ").Append(((Vector3)home.position).ToString("F0")).Append(", up there ").Append(up.ToString("F3")).Append(", ").Append(Vector3.Angle(up, Vector3.up).ToString("F1")).Append("° from the world's y")
                   .Append("; altitude ").Append(home.GetAltitude(cam.transform.position).ToString("F1"));
                foreach (RaycastHit h in Physics.RaycastAll(cam.transform.position + up * 5f, -up, 3000f, ~0, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance).Take(6))
                    say.Append("\n  below: ").Append(h.distance.ToString("F1")).Append(" m ").Append(Path(h.collider.transform)).Append(" layer ").Append(h.collider.gameObject.layer).Append(" ").Append(h.collider.GetType().Name);
            }
            return say.ToString();
        }

        /// <summary>The colliders under one top object (arg: its name), with where they are.</summary>
        public static string CollidersUnder(string root)
        {
            var say = new StringBuilder();
            foreach (Collider c in UnityEngine.Object.FindObjectsOfType<Collider>())
            {
                if (c.transform.root.name != root) continue;
                say.Append(Path(c.transform)).Append(" ").Append(c.GetType().Name).Append(c.isTrigger ? " trigger" : "").Append(c.enabled ? "" : " off").Append(" layer ").Append(c.gameObject.layer)
                   .Append(" at ").Append(V(c.bounds.center)).Append(" size ").Append(V(c.bounds.size)).Append("\n");
            }
            return say.Length > 0 ? say.ToString() : "none under " + root;
        }

        /// <summary>
        /// What is solid at a walker's waist round the walk (arg: "half-width cell", metres; 60 and 1.5 if not given), as a map seen
        /// from above with +x to the right and +z up: # solid, . free, @ the walker, ~ only the floor slab under it (no drawn floor).
        /// </summary>
        public static string Map(string arg)
        {
            if (Foot.Current == null) return "not walking";
            string[] p = (arg ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            float half = p.Length > 0 ? F(p[0], 60f) : 60f, cell = p.Length > 1 ? F(p[1], 1.5f) : 1.5f;
            Vector3 at = Foot.Current.transform.position;
            int solid = Foot.Current.Place.Solid;
            float centreX = p.Length > 2 ? F(p[2], at.x) : at.x, centreZ = p.Length > 3 ? F(p[3], at.z) : at.z;
            var say = new StringBuilder();
            for (float z = centreZ + half; z >= centreZ - half; z -= cell)
            {
                for (float x = centreX - half; x <= centreX + half; x += cell)
                {
                    if (Mathf.Abs(x - at.x) < cell * 0.5f && Mathf.Abs(z - at.z) < cell * 0.5f) { say.Append('@'); continue; }
                    var c = new Vector3(x, at.y + 0.5f, z);
                    if (Physics.CheckBox(c, new Vector3(cell * 0.45f, 0.3f, cell * 0.45f), Quaternion.identity, solid, QueryTriggerInteraction.Ignore)) { say.Append('#'); continue; }
                    // (what is under it: the building's own floor or ground, or only the walk's slab)
                    bool ground = Physics.Raycast(c, Vector3.down, out RaycastHit h, 30f, solid, QueryTriggerInteraction.Ignore);
                    say.Append(!ground ? ' ' : h.collider.name == "On Foot: floor" ? '~' : '.');
                }
                say.Append('\n');
            }
            return say.ToString();
        }

        /// <summary>
        /// The space centre's buildings' screens (the menus a click on Mission Control, the Astronaut Complex, Administration or R&D opens):
        /// what each holds, without opening any. Any 3D in them (meshes, cameras) is said; and the biggest pictures they show.
        /// </summary>
        public static string Screens()
        {
            var say = new StringBuilder();
            foreach (MonoBehaviour b in UnityEngine.Object.FindObjectsOfType<MonoBehaviour>())
            {
                if (b == null || !b.GetType().Name.EndsWith("SceneSpawner", StringComparison.Ordinal)) continue;
                foreach (System.Reflection.FieldInfo f in b.GetType().GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic))
                {
                    if (!typeof(Component).IsAssignableFrom(f.FieldType)) continue;
                    var prefab = f.GetValue(b) as Component;
                    if (prefab == null) continue;
                    GameObject g = prefab.gameObject;
                    say.Append(b.GetType().Name).Append(".").Append(f.Name).Append(" = ").Append(g.name).Append(": ");
                    var kinds = new Dictionary<string, int>();
                    foreach (Component k in g.GetComponentsInChildren<Component>(true))
                    {
                        if (k == null) continue;
                        string n = k.GetType().Name;
                        if (n == "RectTransform" || n == "Transform" || n == "CanvasRenderer") continue;
                        kinds[n] = (kinds.TryGetValue(n, out int c) ? c : 0) + 1;
                    }
                    foreach (var k in kinds.OrderByDescending(k => k.Value).Take(14)) say.Append(k.Key).Append(" ").Append(k.Value).Append(", ");
                    say.Append("\n");
                    foreach (Renderer r in g.GetComponentsInChildren<Renderer>(true)) say.Append("   3D: ").Append(Path(r.transform)).Append(" ").Append(r.GetType().Name).Append("\n");
                    foreach (Camera c in g.GetComponentsInChildren<Camera>(true)) say.Append("   CAMERA: ").Append(Path(c.transform)).Append("\n");
                    // (the pictures the screen shows, largest first: a room painted as a picture shows here)
                    var pictures = new List<KeyValuePair<string, int>>();
                    foreach (UnityEngine.UI.Image i in g.GetComponentsInChildren<UnityEngine.UI.Image>(true))
                        if (i.sprite != null && i.sprite.texture != null) pictures.Add(new KeyValuePair<string, int>(Path(i.transform) + " '" + i.sprite.texture.name + "' " + i.sprite.texture.width + "x" + i.sprite.texture.height, i.sprite.texture.width * i.sprite.texture.height));
                    foreach (UnityEngine.UI.RawImage i in g.GetComponentsInChildren<UnityEngine.UI.RawImage>(true))
                        if (i.texture != null) pictures.Add(new KeyValuePair<string, int>(Path(i.transform) + " raw '" + i.texture.name + "' " + i.texture.width + "x" + i.texture.height, i.texture.width * i.texture.height));
                    foreach (var pic in pictures.OrderByDescending(x => x.Value).Take(4)) say.Append("   PICTURE: ").Append(pic.Key).Append("\n");
                }
            }
            return say.Length > 0 ? say.ToString() : "no screens here (this is for the space centre)";
        }

        /// <summary>Lines at 0.4, 0.8 and 1.2 m from 15 m out towards the craft's middle, from 16 ways round: the first solid thing each meets.</summary>
        public static string RaysToCraft()
        {
            if (Foot.Current == null || EditorLogic.fetch == null || EditorLogic.fetch.ship == null) return "not walking in the editor";
            Bounds all = new Bounds(); bool some = false;
            foreach (Part p in EditorLogic.fetch.ship.parts) foreach (Renderer r in p.GetComponentsInChildren<Renderer>()) { if (!some) { all = r.bounds; some = true; } else all.Encapsulate(r.bounds); }
            var say = new StringBuilder();
            int solid = Foot.Current.Place.Solid, crafts = 0;
            for (int k = 0; k < 16; k++)
                foreach (float h in new[] { all.min.y - 0.05f + 0.33f * all.size.y, all.min.y - 0.05f + 0.5f * all.size.y, all.min.y - 0.05f + 0.67f * all.size.y })
                {
                    float a = k * Mathf.PI / 8f;
                    Vector3 to = new Vector3(all.center.x, 0.05f + h, all.center.z), from = to + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * 15f;
                    if (Physics.Raycast(from, to - from, out RaycastHit hit, 15f, solid, QueryTriggerInteraction.Ignore))
                    {
                        if (hit.collider.gameObject.layer == EditorPlace.WalkLayer && hit.collider.name.Contains("part")) crafts++;
                        say.Append(k).Append("@").Append(h.ToString("F1")).Append(": ").Append((15f - hit.distance).ToString("F1")).Append(" m from the middle, ").Append(hit.collider.name).Append(" (").Append(hit.collider.transform.parent != null ? hit.collider.transform.parent.name : "").Append(") layer ").Append(hit.collider.gameObject.layer).Append("\n");
                    }
                    else say.Append(k).Append("@").Append(h.ToString("F1")).Append(": nothing\n");
                }
            return crafts + " of 48 lines stopped by the craft\n" + say;
        }

        /// <summary>The crew car: why it cannot go out now, or "ready".</summary>
        public static string CarWhyNot() => Car.WhyNot() ?? "ready";

        /// <summary>The crew car put on the runway, as the window's button does it.</summary>
        public static string CarDrive()
        {
            string why = Car.WhyNot();
            if (why != null) return "not sent: " + why;
            Car.Drive();
            return "the crew car is going out to the " + Car.Site;
        }

        /// <summary>The editor's own launch, as its green button does it.</summary>
        public static string Launch()
        {
            if (EditorLogic.fetch == null) return "not in the editor";
            EditorLogic.fetch.launchVessel();
            return "launching";
        }

        /// <summary>The game's own prefabs whose names have any of the words given (arg, words separated by spaces; all of them if none).</summary>
        public static string Prefabs(string arg)
        {
            AssetBase assets = UnityEngine.Object.FindObjectOfType<AssetBase>();
            if (assets == null || assets.prefabs == null) return "no asset base";
            string[] words = (arg ?? "").ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var names = new List<string>();
            foreach (GameObject g in assets.prefabs)
            {
                if (g == null) continue;
                string low = g.name.ToLowerInvariant();
                if (words.Length == 0 || words.Any(w => low.Contains(w))) names.Add(g.name);
            }
            return names.Count + ": " + string.Join(", ", names.ToArray());
        }

        /// <summary>
        /// One of the game's instructor prefabs (arg: its name, e.g. Instructor_Gene) made for a moment, as Mission Control or the R&D
        /// building makes it, and photographed from its own camera and from further back (PNGs in the temporary folder, or arg2 after
        /// a space); then taken away again. What its pieces are is said.
        /// </summary>
        public static string Set(string arg)
        {
            string[] p = arg.Split(new[] { ' ' }, 2);
            GameObject prefab = AssetBase.GetPrefab(p[0]);
            if (prefab == null) return "no prefab " + p[0];
            GameObject made = UnityEngine.Object.Instantiate(prefab);
            try
            {
                made.transform.position = Vector3.zero;
                foreach (Camera c in made.GetComponentsInChildren<Camera>(true)) c.enabled = false;
                var say = new StringBuilder();
                var k = made.GetComponentInChildren<KerbalInstructorBase>(true);
                Bounds all = new Bounds(made.transform.position, Vector3.zero);
                var renderers = made.GetComponentsInChildren<Renderer>(true);
                foreach (Renderer r in renderers) all.Encapsulate(r.bounds);
                say.Append(p[0]).Append(": ").Append(renderers.Length).Append(" pieces spanning ").Append(V(all.size)).Append(" round ").Append(V(all.center)).Append("\n");
                foreach (Renderer r in renderers.OrderByDescending(r => r.bounds.size.sqrMagnitude).Take(25))
                    say.Append("   ").Append(Path(r.transform)).Append(" ").Append(r.GetType().Name).Append(" layer ").Append(r.gameObject.layer).Append(" size ").Append(V(r.bounds.size)).Append(" at ").Append(V(r.bounds.center))
                       .Append(r.sharedMaterial != null && r.sharedMaterial.shader != null ? " " + r.sharedMaterial.shader.name : "").Append("\n");
                foreach (Light l in made.GetComponentsInChildren<Light>(true)) say.Append("   LIGHT ").Append(Path(l.transform)).Append(" ").Append(l.type).Append(" mask ").Append(l.cullingMask).Append("\n");
                foreach (Collider c in made.GetComponentsInChildren<Collider>(true)) say.Append("   COLLIDER ").Append(Path(c.transform)).Append(" ").Append(c.GetType().Name).Append("\n");
                Camera cam = k != null ? k.instructorCamera : made.GetComponentInChildren<Camera>(true);
                if (cam != null)
                {
                    say.Append("   CAMERA ").Append(Path(cam.transform)).Append(" at ").Append(V(cam.transform.position)).Append(" looking ").Append(V(cam.transform.forward)).Append(" fov ").Append(cam.fieldOfView.ToString("F1")).Append(" layers ").Append(cam.cullingMask).Append(" clear ").Append(cam.clearFlags).Append("\n");
                    string folder = p.Length > 1 ? p[1] : System.IO.Path.GetTempPath();
                    var rt = new RenderTexture(768, 768, 24, RenderTextureFormat.ARGB32);
                    var read = new Texture2D(768, 768, TextureFormat.RGB24, false);
                    Vector3 pos = cam.transform.position; float fov = cam.fieldOfView;
                    try
                    {
                        for (int pass = 0; pass < 3; pass++)
                        {
                            if (pass == 1) { cam.transform.position = pos - cam.transform.forward * 1.5f; cam.fieldOfView = Mathf.Min(90f, fov * 2f); }
                            if (pass == 2) { cam.transform.position = pos - cam.transform.forward * 4f + Vector3.up * 1f; cam.fieldOfView = 100f; }
                            cam.targetTexture = rt;
                            cam.Render();
                            RenderTexture.active = rt;
                            read.ReadPixels(new Rect(0, 0, 768, 768), 0, 0, false);
                            RenderTexture.active = null;
                            string file = System.IO.Path.Combine(folder, "set_" + p[0] + "_" + pass + ".png");
                            System.IO.File.WriteAllBytes(file, ImageConversion.EncodeToPNG(read));
                            say.Append("   picture ").Append(file).Append("\n");
                        }
                    }
                    finally
                    {
                        cam.targetTexture = null; cam.transform.position = pos; cam.fieldOfView = fov;
                        RenderTexture.active = null; rt.Release(); UnityEngine.Object.Destroy(read);
                    }
                }
                return say.ToString();
            }
            finally { UnityEngine.Object.DestroyImmediate(made); }
        }

        // ---- looking into a scene

        /// <summary>Every camera: where, what it draws, how far, what else is on it.</summary>
        public static string Cameras()
        {
            var say = new StringBuilder();
            foreach (Camera c in Resources.FindObjectsOfTypeAll<Camera>())
            {
                if (!c.gameObject.scene.IsValid()) continue;
                var layers = new List<int>();
                for (int l = 0; l < 32; l++) if ((c.cullingMask & (1 << l)) != 0) layers.Add(l);
                say.Append(c.enabled && c.gameObject.activeInHierarchy ? "CAMERA " : "camera (off) ").Append(Path(c.transform)).Append(" depth ").Append(c.depth).Append(" layers ").Append(string.Join(",", layers.Select(l => l.ToString()).ToArray()))
                   .Append(" near ").Append(c.nearClipPlane.ToString("F2")).Append(" far ").Append(c.farClipPlane.ToString("F0")).Append(" fov ").Append(c.fieldOfView.ToString("F1"))
                   .Append(" clear ").Append(c.clearFlags).Append(c.targetTexture != null ? " into " + c.targetTexture.name : "").Append(" at ").Append(V(c.transform.position))
                   .Append(" components: ").Append(string.Join(", ", c.GetComponents<Component>().Select(k => k.GetType().Name + (k is Behaviour b && !b.enabled ? "(off)" : "")).ToArray())).Append("\n");
            }
            if (EditorCamera.Instance != null)
            {
                Transform rig = EditorCamera.Instance.transform;
                say.Append("EDITOR CAMERA rig ").Append(Path(rig)).Append(" local ").Append(V(rig.localPosition)).Append(" cam ").Append(EditorCamera.Instance.cam != null ? Path(EditorCamera.Instance.cam.transform) : "none")
                   .Append(" components: ").Append(string.Join(", ", rig.GetComponents<Component>().Select(k => k.GetType().Name + (k is Behaviour b && !b.enabled ? "(off)" : "")).ToArray())).Append("\n");
            }
            return say.ToString();
        }

        /// <summary>What is loaded: the scenes and their top objects; the editor's bounds.</summary>
        public static string Scenery()
        {
            var say = new StringBuilder();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene s = SceneManager.GetSceneAt(i);
                say.Append("SCENE ").Append(s.name).Append(s.isLoaded ? "" : " (not loaded)").Append(": ");
                if (s.isLoaded) foreach (GameObject g in s.GetRootGameObjects()) say.Append(g.name).Append(g.activeSelf ? "" : "(off)").Append("[").Append(g.transform.childCount).Append("] ");
                say.Append("\n");
            }
            if (EditorBounds.Instance != null)
            {
                EditorBounds b = EditorBounds.Instance;
                say.Append("EDITOR BOUNDS construction ").Append(V(b.constructionBounds.center)).Append(" size ").Append(V(b.constructionBounds.size)).Append("; camera ").Append(V(b.cameraOffsetBounds.center)).Append(" size ").Append(V(b.cameraOffsetBounds.size))
                   .Append("; spawn ").Append(V(b.rootPartSpawnPoint)).Append("; scenery centre ").Append(b.sceneryCenter != null ? V(b.sceneryCenter.position) : "none").Append("; camera distances ").Append(b.cameraMinDistance).Append(" to ").Append(b.cameraMaxDistance).Append("\n");
            }
            say.Append("facility ").Append(EditorDriver.editorFacility).Append("; time scale ").Append(Time.timeScale).Append("; gravity ").Append(V(Physics.gravity)).Append("; auto sync ").Append(Physics.autoSyncTransforms).Append("; backfaces ").Append(Physics.queriesHitBackfaces);
            return say.ToString();
        }

        /// <summary>Every collider, summed up by the top object it is under and its layer; then the largest.</summary>
        public static string Colliders()
        {
            var say = new StringBuilder();
            var groups = new Dictionary<string, int>();
            var all = UnityEngine.Object.FindObjectsOfType<Collider>();
            foreach (Collider c in all)
            {
                string key = c.transform.root.name + " layer " + c.gameObject.layer + " " + c.GetType().Name + (c.isTrigger ? " trigger" : "") + (c.enabled ? "" : " off");
                groups[key] = (groups.TryGetValue(key, out int n) ? n : 0) + 1;
            }
            foreach (var g in groups.OrderByDescending(k => k.Value)) say.Append(g.Value).Append(" x ").Append(g.Key).Append("\n");
            say.Append("LARGEST:\n");
            foreach (Collider c in all.OrderByDescending(c => c.bounds.size.x * c.bounds.size.z).Take(25))
                say.Append(Path(c.transform)).Append(" ").Append(c.GetType().Name).Append(c.isTrigger ? " trigger" : "").Append(" layer ").Append(c.gameObject.layer).Append(" bounds ").Append(V(c.bounds.center)).Append(" size ").Append(V(c.bounds.size)).Append("\n");
            return say.ToString();
        }

        /// <summary>The building's meshes (layer 15, or the layer given): how many, whether their shapes can be read, how big.</summary>
        public static string Meshes() => Meshes("15");

        public static string Meshes(string arg)
        {
            int layer = (int)F(arg, 15f);
            var say = new StringBuilder();
            int count = 0, readable = 0, batched = 0, tris = 0, transparent = 0;
            var big = new List<MeshFilter>();
            foreach (MeshFilter mf in UnityEngine.Object.FindObjectsOfType<MeshFilter>())
            {
                if (mf.gameObject.layer != layer) continue;
                Renderer r = mf.GetComponent<Renderer>();
                Mesh m = mf.sharedMesh;
                if (m == null) continue;
                count++;
                if (m.isReadable) { readable++; tris += m.triangles.Length / 3; }
                if (r != null && r.isPartOfStaticBatch) batched++;
                if (r != null && r.sharedMaterial != null && r.sharedMaterial.renderQueue > 2450) transparent++;
                big.Add(mf);
            }
            say.Append(count).Append(" meshes on layer ").Append(layer).Append(", ").Append(readable).Append(" readable (").Append(tris).Append(" triangles), ").Append(batched).Append(" statically batched, ").Append(transparent).Append(" drawn see-through\n");
            foreach (MeshFilter mf in big.OrderByDescending(m => { Renderer r = m.GetComponent<Renderer>(); return r != null ? r.bounds.size.sqrMagnitude : 0f; }).Take(40))
            {
                Renderer r = mf.GetComponent<Renderer>();
                Mesh m = mf.sharedMesh;
                say.Append(Path(mf.transform)).Append(" mesh '").Append(m.name).Append("' ").Append(m.isReadable ? "readable " + m.vertexCount + " verts" : "UNREADABLE").Append(r != null && r.isPartOfStaticBatch ? " batched" : "")
                   .Append(r != null ? " bounds " + V(r.bounds.center) + " size " + V(r.bounds.size) + " shader " + (r.sharedMaterial != null && r.sharedMaterial.shader != null ? r.sharedMaterial.shader.name + " q" + r.sharedMaterial.renderQueue : "none") + (r.enabled ? "" : " (off)") : "").Append("\n");
            }
            return say.ToString();
        }

        /// <summary>The ground crew (the game's GroundCrew, and everything on the crew's layer): how tall, where.</summary>
        public static string Crew()
        {
            var say = new StringBuilder();
            foreach (global::SpaceCenterCrew c in UnityEngine.Object.FindObjectsOfType<global::SpaceCenterCrew>())
            {
                Bounds b = new Bounds(c.transform.position, Vector3.zero);
                foreach (Renderer r in c.GetComponentsInChildren<Renderer>()) b.Encapsulate(r.bounds);
                Collider col = c.GetComponentInChildren<Collider>();
                say.Append(c.GetType().Name).Append(" ").Append(Path(c.transform)).Append(" at ").Append(V(c.transform.position)).Append(" tall ").Append(b.size.y.ToString("F2")).Append(" wide ").Append(b.size.x.ToString("F2")).Append("x").Append(b.size.z.ToString("F2"))
                   .Append(" waypoints ").Append(c.waypoints != null ? c.waypoints.Length : 0).Append(col != null ? " collider " + col.GetType().Name + " layer " + col.gameObject.layer : "").Append("\n");
            }
            var roots = new Dictionary<string, int>();
            foreach (Renderer r in UnityEngine.Object.FindObjectsOfType<Renderer>()) if (r.gameObject.layer == EditorPlace.CrewLayer) { string k = r.transform.root.name; roots[k] = (roots.TryGetValue(k, out int n) ? n : 0) + 1; }
            foreach (var k in roots) say.Append("crew-layer renderers under ").Append(k.Key).Append(": ").Append(k.Value).Append("\n");
            return say.ToString();
        }

        /// <summary>The layers by name, and which of a few collide with which.</summary>
        public static string Layers()
        {
            var say = new StringBuilder();
            for (int l = 0; l < 32; l++) { string n = LayerMask.LayerToName(l); if (!string.IsNullOrEmpty(n)) say.Append(l).Append(" ").Append(n).Append("; "); }
            say.Append("\n");
            int[] some = { 0, 15, 16, 17, 19, 21, 26, 27, 28 };
            foreach (int a in some)
            {
                say.Append(a).Append(" collides with:");
                for (int b = 0; b < 32; b++) if (!Physics.GetIgnoreLayerCollision(a, b)) say.Append(" ").Append(b);
                say.Append("\n");
            }
            return say.ToString();
        }

        /// <summary>The craft in the editor: its parts' colliders and bodies.</summary>
        public static string Parts()
        {
            if (EditorLogic.fetch == null || EditorLogic.fetch.ship == null) return "no craft";
            var say = new StringBuilder();
            var kinds = new Dictionary<string, int>();
            foreach (Part p in EditorLogic.fetch.ship.parts)
                foreach (Collider c in p.GetComponentsInChildren<Collider>(true))
                {
                    string k = "layer " + c.gameObject.layer + " " + c.GetType().Name + (c.isTrigger ? " trigger" : "") + (c.enabled && c.gameObject.activeInHierarchy ? "" : " off");
                    kinds[k] = (kinds.TryGetValue(k, out int n) ? n : 0) + 1;
                }
            foreach (var k in kinds) say.Append(k.Value).Append(" x ").Append(k.Key).Append("\n");
            int bodies = 0, kinematic = 0;
            foreach (Part p in EditorLogic.fetch.ship.parts) { Rigidbody rb = p.GetComponent<Rigidbody>(); if (rb != null) { bodies++; if (rb.isKinematic) kinematic++; } }
            Bounds all = new Bounds();
            bool some = false;
            foreach (Part p in EditorLogic.fetch.ship.parts) foreach (Renderer r in p.GetComponentsInChildren<Renderer>()) { if (!some) { all = r.bounds; some = true; } else all.Encapsulate(r.bounds); }
            say.Append(EditorLogic.fetch.ship.parts.Count).Append(" parts, ").Append(bodies).Append(" with rigid bodies (").Append(kinematic).Append(" kinematic); the craft spans ").Append(V(all.center)).Append(" size ").Append(V(all.size));
            return say.ToString();
        }

        /// <summary>Lines straight down over a grid (arg "x0 z0 x1 z1 step top"), naming the first thing each hits on any layer.</summary>
        public static string Rays(string arg)
        {
            string[] p = arg.Split(' ');
            float x0 = F(p[0], -50), z0 = F(p.Length > 1 ? p[1] : "", -50), x1 = F(p.Length > 2 ? p[2] : "", 50), z1 = F(p.Length > 3 ? p[3] : "", 50), step = F(p.Length > 4 ? p[4] : "", 10), top = F(p.Length > 5 ? p[5] : "", 80);
            var say = new StringBuilder();
            for (float z = z0; z <= z1 + 0.01f; z += step)
            {
                for (float x = x0; x <= x1 + 0.01f; x += step)
                {
                    if (Physics.Raycast(new Vector3(x, top, z), Vector3.down, out RaycastHit h, top + 100f, ~0, QueryTriggerInteraction.Ignore))
                        say.Append(h.point.y.ToString("F1")).Append("[").Append(h.collider.gameObject.layer).Append("] ");
                    else say.Append("  -   ");
                }
                say.Append("\n");
            }
            return say.ToString();
        }

        /// <summary>The camera in the editor put somewhere without walking (arg "x y z heading pitch"), for a picture; "back" gives it back.</summary>
        public static string Eye(string arg)
        {
            if (EditorCamera.Instance == null) return "not in the editor";
            Transform rig = EditorCamera.Instance.transform;
            var vab = rig.GetComponent<VABCamera>(); var sph = rig.GetComponent<SPHCamera>();
            if (arg == "back")
            {
                if (EditorDriver.editorFacility == EditorFacility.SPH) sph.enabled = true; else vab.enabled = true;
                return "given back";
            }
            vab.enabled = false; sph.enabled = false;
            string[] p = arg.Split(' ');
            rig.position = new Vector3(F(p[0]), F(p[1]), F(p[2]));
            rig.rotation = Quaternion.Euler(p.Length > 4 ? F(p[4]) : 0f, p.Length > 3 ? F(p[3]) : 0f, 0f);
            return "camera at " + V(rig.position);
        }

        // ---- the space centre's buildings

        /// <summary>Open one of the space centre's buildings as a click would (arg: mc, admin, rd, ac) and say what 3D things it brings.</summary>
        public static string Open(string arg)
        {
            SpaceCenterBuilding which = null;
            foreach (SpaceCenterBuilding b in UnityEngine.Object.FindObjectsOfType<SpaceCenterBuilding>())
            {
                string kind = b.GetType().Name;
                if ((arg == "mc" && kind == "MissionControlBuilding") || (arg == "admin" && kind == "AdministrationFacility") || (arg == "rd" && kind == "RnDBuilding") || (arg == "ac" && kind == "AstronautComplexFacility")) { which = b; break; }
            }
            if (which == null)
            {
                var names = new List<string>();
                foreach (SpaceCenterBuilding b in UnityEngine.Object.FindObjectsOfType<SpaceCenterBuilding>()) names.Add(b.GetType().Name + " '" + b.name + "'");
                return "no such building here; there are: " + string.Join(", ", names.ToArray());
            }
            typeof(SpaceCenterBuilding).GetMethod("OnClicked", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)?.Invoke(which, null);
            return "opened " + which.GetType().Name;
        }

        /// <summary>Close what Open opened (arg as for Open).</summary>
        public static string Close(string arg)
        {
            switch (arg)
            {
                case "mc": GameEvents.onGUIMissionControlDespawn.Fire(); break;
                case "admin": GameEvents.onGUIAdministrationFacilityDespawn.Fire(); break;
                case "rd": GameEvents.onGUIRnDComplexDespawn.Fire(); break;
                case "ac": GameEvents.onGUIAstronautComplexDespawn.Fire(); break;
                default: return "mc, admin, rd or ac";
            }
            return "closed " + arg;
        }

        /// <summary>The kerbals the buildings show (Gene, Wernher, Mortimer...): their sets, the cameras that draw them, their pieces.</summary>
        public static string Instructors()
        {
            var say = new StringBuilder();
            foreach (KerbalInstructorBase k in UnityEngine.Object.FindObjectsOfType<KerbalInstructorBase>())
            {
                Transform root = k.transform.root;
                Bounds all = new Bounds(root.position, Vector3.zero);
                var renderers = root.GetComponentsInChildren<Renderer>();
                foreach (Renderer r in renderers) all.Encapsulate(r.bounds);
                say.Append("INSTRUCTOR ").Append(k.CharacterName).Append(" ").Append(Path(k.transform)).Append(" root at ").Append(V(root.position)).Append(" spans ").Append(V(all.center)).Append(" size ").Append(V(all.size))
                   .Append(" camera ").Append(k.instructorCamera != null ? Path(k.instructorCamera.transform) + " at " + V(k.instructorCamera.transform.position) + " fov " + k.instructorCamera.fieldOfView.ToString("F1") + " layers " + k.instructorCamera.cullingMask : "none").Append("\n");
                foreach (Renderer r in renderers.OrderByDescending(r => r.bounds.size.sqrMagnitude).Take(30))
                    say.Append("   ").Append(Path(r.transform)).Append(" ").Append(r.GetType().Name).Append(" layer ").Append(r.gameObject.layer).Append(" size ").Append(V(r.bounds.size)).Append(" at ").Append(V(r.bounds.center)).Append("\n");
                foreach (Light l in root.GetComponentsInChildren<Light>()) say.Append("   LIGHT ").Append(l.name).Append(" ").Append(l.type).Append("\n");
                foreach (Collider c in root.GetComponentsInChildren<Collider>()) say.Append("   COLLIDER ").Append(Path(c.transform)).Append(" ").Append(c.GetType().Name).Append("\n");
            }
            if (say.Length == 0) say.Append("none");
            return say.ToString();
        }

        /// <summary>
        /// A picture of each instructor's set from its own camera (arg "back"): as the game frames it, and from twice as far back with a
        /// wider lens, to see how much of a room there is. PNGs in the folder given (arg: folder), else the system's temporary folder.
        /// </summary>
        public static string Sets(string folder)
        {
            if (string.IsNullOrEmpty(folder)) folder = System.IO.Path.GetTempPath();
            var say = new StringBuilder();
            int n = 0;
            foreach (KerbalInstructorBase k in UnityEngine.Object.FindObjectsOfType<KerbalInstructorBase>())
            {
                Camera cam = k.instructorCamera;
                if (cam == null) continue;
                var rt = new RenderTexture(1024, 1024, 24, RenderTextureFormat.ARGB32);
                var read = new Texture2D(1024, 1024, TextureFormat.RGB24, false);
                RenderTexture before = cam.targetTexture;
                Vector3 pos = cam.transform.position; float fov = cam.fieldOfView;
                try
                {
                    for (int pass = 0; pass < 3; pass++)
                    {
                        cam.targetTexture = rt;
                        if (pass == 1) { cam.transform.position = pos - cam.transform.forward * 2f; cam.fieldOfView = Mathf.Min(100f, fov * 2f); }
                        if (pass == 2) { cam.transform.position = pos - cam.transform.forward * 5f; cam.fieldOfView = 90f; }
                        cam.Render();
                        RenderTexture.active = rt;
                        read.ReadPixels(new Rect(0, 0, 1024, 1024), 0, 0, false);
                        RenderTexture.active = null;
                        string file = System.IO.Path.Combine(folder, "set_" + (k.CharacterName ?? "x").Replace(" ", "_") + "_" + n + "_" + pass + ".png");
                        System.IO.File.WriteAllBytes(file, ImageConversion.EncodeToPNG(read));
                        say.Append(file).Append("\n");
                    }
                }
                finally
                {
                    cam.transform.position = pos; cam.fieldOfView = fov; cam.targetTexture = before;
                    RenderTexture.active = null;
                    rt.Release(); UnityEngine.Object.Destroy(read);
                }
                n++;
            }
            return say.Length > 0 ? say.ToString() : "no instructors";
        }
    }
}
#endif
