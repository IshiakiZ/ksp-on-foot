using System;
using Keystone;
using UnityEngine;

#if !DEV
[assembly: KSPAssembly("OnFoot", 0, 2)]
[assembly: KSPAssemblyDependency("Keystone", 0, 4)]
#endif

namespace OnFoot
{
    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    public sealed class FootAtStart : MonoBehaviour
    {
        void Awake() { Foot.Setup(); Destroy(gameObject); }
    }

    /// <summary>
    /// On Foot: walking, in first person and at a kerbal's height, where the game lets nobody walk: the floor of
    /// the assembly building and of the hangar, among the ground crew and up to the craft being built.
    ///
    /// The game's own camera is taken over while walking and handed back exactly as it was afterwards; nothing
    /// of the game's is changed for good. The building is made solid for the walk from its own meshes (they are
    /// drawn, but nothing in the game ever needed to stand on them), with a floor laid where it has none.
    /// </summary>
    public static class Foot
    {
        public const string Version = "0.2.1";
        internal const string Name = "On Foot";

        internal static Mod mod;
        internal static Slider walk, run, look, fov, bob, eye;
        internal static Toggle invert, hint;

        public static void Setup()
        {
            if (mod != null && Kit.Find(Name) == mod) return;
            mod = Kit.Register(Name, Version, "Walk the floor of the assembly building and the hangar in first person, at a kerbal's height: among the ground crew, and up to the craft being built.");
            mod.Heading("Walking");
            walk = mod.Slider("walk", "Walking speed", 1.6f, 0.5f, 4f, " m/s", 1, "How fast you walk. (A kerbal on the ground crew walks at 0.7 m/s.)");
            run = mod.Slider("run", "Running speed", 4.5f, 1f, 10f, " m/s", 1, "How fast you go with the run key (the game's EVA run key, Shift unless you have changed it) held.");
            eye = mod.Slider("eye", "Eye height", 0.8f, 0.5f, 2f, " m", 2, "How high your eyes are above the floor. The ground crew are 0.9 to 1 m tall, and a kerbal's eyes are 0.8 m up; a person's would be 1.6 m.");
            mod.Heading("Looking");
            look = mod.Slider("look", "Mouse speed", 1f, 0.2f, 4f, "", 1, "How far the view turns for a move of the mouse.");
            invert = mod.Toggle("invert", "Mouse up looks down", false, "Pushing the mouse away tilts the view down, as in a flight simulator.");
            fov = mod.Slider("fov", "Field of view", 60f, 35f, 95f, "°", 0, "How much you see, from the top of the picture to the bottom.");
            bob = mod.Slider("bob", "Head movement", 0.5f, 0f, 1f, "", 2, "How much your head rises and falls with each step. None at 0.");
            hint = mod.Toggle("hint", "Show the keys on starting", true, "For a few seconds after you set off, the keys are shown at the foot of the picture.");
            mod.Heading("The crew car");
            tug = mod.Toggle("tug", "Looks like a baggage tug", true, "The crew car is drawn as an airport baggage tractor. Off, it shows the stock rover parts it is made of (it drives the same either way).");
            topSpeed = mod.Slider("speed", "Top speed", 30f, 10f, 100f, " km/h", 0, "How fast the crew car can drive itself on the level: its wheels' motors give less and less pull near this speed and none past it (a real baggage tug does about 25 km/h). It is not held there: downhill it can roll faster, and the brakes work as ever. At 100, the stock rover wheels' own limit (about 95 km/h).");
        }

        internal static Toggle tug;
        internal static Slider topSpeed;

        /// <summary>The crew car's top speed in metres a second, or nought for the stock wheels' own.</summary>
        internal static float TopSpeed => topSpeed == null || topSpeed.Value >= 99.5f ? 0f : topSpeed.Value / 3.6f;

        /// <summary>The walk under way, if there is one.</summary>
        internal static Walk Current;

        /// <summary>What a key press would do now, for the window: why walking cannot start here, or nothing if it can.</summary>
        internal static string WhyNot;

        /// <summary>When the player walked into a building from the space centre (a walk begins at its door once it is ready), or below nought.</summary>
        internal static float WalkInAt = -1f;

        /// <summary>Whether a walk in from the space centre is still to begin (it is forgotten after a minute: the click may not have led in).</summary>
        internal static bool WalkIn => WalkInAt >= 0f && Time.unscaledTime - WalkInAt < 60f;

        internal static string KeyName => (Application.platform == RuntimePlatform.OSXPlayer ? "Option" : "Alt") + "-W";
    }

    /// <summary>
    /// In every scene: the key that sets off on foot and stops again, and the page in Keystone's window. Where
    /// there is somewhere to walk (see Place.Here), a press of the key begins a walk there.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.EveryScene, false)]
    public sealed class Feet : MonoBehaviour
    {
        void Awake()
        {
            Foot.Setup();
            Foot.mod.Panel = Panel;
            GameEvents.onGameSceneLoadRequested.Add(Leaving);
        }

        void OnDestroy()
        {
            GameEvents.onGameSceneLoadRequested.Remove(Leaving);
            if (Foot.mod != null && Foot.mod.Panel == Panel) Foot.mod.Panel = null;
            Stop(true);
        }

        /// <summary>The game is about to go to another scene: everything is handed back before it does.</summary>
        void Leaving(GameScenes to) => Stop(true);

        void Update()
        {
            if (HighLogic.LoadedScene == GameScenes.LOADING || HighLogic.LoadedScene == GameScenes.LOADINGBUFFER) return;
            bool typing = GUIUtility.keyboardControl != 0;
            bool toggle = !typing && GameSettings.MODIFIER_KEY.GetKey() && Input.GetKeyDown(KeyCode.W);
            if (Foot.Current != null)
            {
                if (!Foot.Current.Ending && (toggle || Input.GetKeyDown(KeyCode.Escape))) Stop();
                return;
            }
            // (walked in from the space centre: on foot as soon as the building is ready, at its door)
            if (Foot.WalkIn && HighLogic.LoadedSceneIsEditor)
            {
                if (EditorPlace.Make(out string _) != null && Time.unscaledTime > walkInAfter)
                {
                    Foot.WalkInAt = -1f;
                    EditorPlace.AtDoor = true;
                    Begin();
                    EditorPlace.AtDoor = false;
                }
                return;
            }
            if (toggle)
            {
                if (HighLogic.LoadedScene == GameScenes.SPACECENTER) WalkInto();
                else Begin();
            }
        }

        float walkInAfter;

        void Start()
        {
            // (the building needs a moment after the editor is up: its crew and its scenery settle in)
            walkInAfter = Time.unscaledTime + 1.5f;
            if (!HighLogic.LoadedSceneIsEditor) Foot.WalkInAt = -1f;
        }

        /// <summary>
        /// From the space centre: into the building under the pointer on foot, if it is one that has an inside to walk in (the
        /// assembly building or the hangar). It is opened as a click on it opens it, and the walk begins at its door.
        /// </summary>
        static void WalkInto()
        {
            SpaceCenterBuilding over = Hovered();
            if (over == null) { Say("Point at the assembly building or the hangar, and press " + Foot.KeyName + " to walk in."); return; }
            if (!(over is VehicleAssemblyBuilding) && !(over is SpacePlaneHangarBuilding))
            {
                Say("There is no inside to walk in there: only the assembly building and the hangar have one.");
                return;
            }
            System.Reflection.MethodInfo click = Reach.Method(typeof(SpaceCenterBuilding), "OnClicked");
            if (click == null) return;
            Foot.WalkInAt = Time.unscaledTime;
            try { click.Invoke(over, null); }
            catch (Exception ex) { Foot.WalkInAt = -1f; Kit.Log(Foot.Name, "could not open the building: " + ex.Message); }
            // (if the click did not lead into the editor, say a building that is closed in this game, the walk in is forgotten at the next scene)
        }

#if DEV
        /// <summary>For the development build: walk into a building as Option-W over it does (arg: vab or sph), without the pointer.</summary>
        public static string WalkIntoNamed(string which)
        {
            foreach (SpaceCenterBuilding b in FindObjectsOfType<SpaceCenterBuilding>())
                if ((which == "vab" && b is VehicleAssemblyBuilding) || (which == "sph" && b is SpacePlaneHangarBuilding))
                {
                    Foot.WalkInAt = Time.unscaledTime;
                    Reach.Method(typeof(SpaceCenterBuilding), "OnClicked")?.Invoke(b, null);
                    return "walking into " + b.name;
                }
            return "no such building here";
        }
#endif

        static System.Reflection.FieldInfo hoverField;

        /// <summary>The space centre's building under the pointer, as the game itself marks it, or nothing.</summary>
        static SpaceCenterBuilding Hovered()
        {
            if (hoverField == null) hoverField = Reach.Field(typeof(SpaceCenterBuilding), "hover");
            if (hoverField == null) return null;
            foreach (SpaceCenterBuilding b in FindObjectsOfType<SpaceCenterBuilding>())
                if (b != null && hoverField.GetValue(b) is bool on && on) return b;
            return null;
        }

        internal static void Begin()
        {
            if (Foot.Current != null) return;
            Place place = Place.Here(out string why);
            Foot.WhyNot = why;
            if (place == null)
            {
                if (why != null) Say(why);
                return;
            }
            try { Foot.Current = Walk.Start(place); }
            catch (Exception ex)
            {
                Kit.Log(Foot.Name, "could not set off: " + ex);
                try { place.End(); } catch (Exception again) { Kit.Log(Foot.Name, "and could not put things back: " + again); }
                Foot.Current = null;
                Say("Could not set off on foot here (" + ex.Message + ").");
            }
        }

        /// <summary>Stop walking: at once (the scene is going), or with the view rising back to the game's camera first.</summary>
        internal static void Stop(bool now = false)
        {
            Walk walk = Foot.Current;
            if (walk == null) return;
            if (now) Foot.Current = null;
            walk.Finish(now);
        }

        static void Say(string text)
        {
            try { ScreenMessages.PostScreenMessage(text, 4f, ScreenMessageStyle.UPPER_CENTER); } catch { }
        }

        void Panel()
        {
            GUILayout.Space(4f);
            bool walking = Foot.Current != null;
            string place = Place.Describe();
            GUILayout.Label("<size=11><color=#b0d8ff>" + (walking ? "Walking. " + Foot.Current.Readout
                : place != null ? place + " " + Foot.KeyName + " sets off on foot, and stops again; so does Esc."
                : HighLogic.LoadedScene == GameScenes.SPACECENTER ? "Point at the assembly building or the hangar and press " + Foot.KeyName + " to walk in at its door."
                : "There is nowhere to walk in this scene: walking is in the assembly building and the hangar.") + "</color></size>", Host.Rich);
            if (place != null && !walking && GUILayout.Button("Set off on foot"))
            {
                // (the window is in the way of the view, and the pointer is wanted for looking about: it is closed first)
                if (Host.Instance != null) Host.Instance.Open = false;
                Begin();
            }
            if (!walking && !string.IsNullOrEmpty(Foot.WhyNot)) GUILayout.Label("<size=11><color=#ffc080>" + Foot.WhyNot + "</color></size>", Host.Rich);

            // ---- the crew car, from the space centre
            if (HighLogic.LoadedScene == GameScenes.SPACECENTER)
            {
                GUILayout.Space(6f);
                GUILayout.Label("<b>The crew car</b>", Host.Rich);
                GUILayout.Label("<size=11>A small rover of the game's own parts, with a seat, a storage crate and two lamps: put on the runway with a kerbal at the wheel, and driven with the rover keys.</size>", Host.Small);
                if (Time.unscaledTime > carCheckedAt) { carWhyNot = Car.WhyNot(); carCheckedAt = Time.unscaledTime + 1f; }
                GUI.enabled = carWhyNot == null;
                if (GUILayout.Button("Drive the crew car"))
                {
                    try { Car.Drive(); if (Host.Instance != null) Host.Instance.Open = false; }
                    catch (Exception ex) { carWhyNot = ex.Message; Kit.Log(Foot.Name, "the crew car: " + ex); }
                }
                GUI.enabled = true;
                if (carWhyNot != null) GUILayout.Label("<size=11><color=#ffc080>" + carWhyNot + "</color></size>", Host.Rich);
            }
        }

        string carWhyNot;
        float carCheckedAt;
    }
}
