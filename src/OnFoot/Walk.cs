using System;
using Keystone;
using UnityEngine;

namespace OnFoot
{
    /// <summary>What the player asks of the body this frame: along and across (each -1 to 1), running, jumping.</summary>
    internal struct Driven { public float Along, Across; public bool Run, Jump; }

    /// <summary>
    /// One walk: a body the size of a kerbal that the keys move about and the mouse turns, and the game's camera at
    /// its eyes. The body is Unity's own character controller (a capsule that slides along whatever it meets, climbs
    /// steps and slopes, and falls), so it is stopped by the building made solid for the walk (see Place) and by the
    /// craft's own parts, and it can stand on anything solid: a floor, a stair, a gantry, a wing.
    /// </summary>
    public sealed class Walk : MonoBehaviour
    {
        /// <summary>The body: a kerbal of the ground crew is 0.9 to 1 m tall, and half a metre across.</summary>
        public const float Radius = 0.25f, Height = 1.0f;
        const float Step = 0.3f, Slope = 50f, Jump = 0.5f, Ease = 0.7f, EaseOut = 0.45f;

        Place place;
        CharacterController body;
        float heading, pitch;
        Vector3 going;                            // across the ground, metres a second (in the world)
        float rising;                             // up, metres a second
        float stride, bobbing, dip;               // how far through a step; how much the head moves now; how far it has dropped on landing
        bool grounded = true;
        bool paused;                              // Keystone's window is open: the pointer is free for it
        float settled;                            // (the mouse is not read until then)
        float clock, began, ending = -1f;           // the walk's own clock (seconds since it began), when it began, and when it began to end
        Vector3 fromEye, leaveEye; Quaternion fromLook, leaveLook; float fromFov, leaveFov;
        Vector3 lastFeet;
        float lowest;
        bool finished;
        string readout = "";

        public string Readout => readout;
        public Place Place => place;

        /// <summary>Set off: the place takes the camera over and says where to stand, and a body is made there.</summary>
        public static Walk Start(Place place)
        {
            place.Begin();
            if (!place.Start(out Vector3 feet, out float heading, out float pitch))
                throw new InvalidOperationException("there is no room to stand anywhere near");
            var holder = new GameObject("On Foot: walker");
            holder.layer = BodyLayer(place.Solid);
            holder.transform.position = feet;
            var body = holder.AddComponent<CharacterController>();
            body.radius = Radius; body.height = Height; body.center = new Vector3(0f, Height * 0.5f, 0f);
            body.stepOffset = Step; body.slopeLimit = Slope; body.skinWidth = 0.03f; body.minMoveDistance = 0f;
            var walk = holder.AddComponent<Walk>();
            walk.place = place; walk.body = body;
            walk.heading = heading; walk.pitch = pitch;
            walk.began = 0f;
            walk.settled = 0.25f;
            place.Was(out walk.fromEye, out walk.fromLook, out walk.fromFov);
            walk.lastFeet = feet;
            walk.lowest = feet.y - 250f;
            Kit.Log(Foot.Name, "set off at " + feet.ToString("F2") + " facing " + heading.ToString("F0") + "°, the body on layer " + holder.layer + "; " + place.About);
            return walk;
        }

        /// <summary>A layer for the body that the place's solid layers stop (the EVA kerbals' own, where it does: it is what a kerbal on foot is).</summary>
        static int BodyLayer(int solid)
        {
            int[] tried = { 17, 19, 16, 2, 1 };
            foreach (int layer in tried)
            {
                bool all = true;
                for (int other = 0; other < 32; other++)
                    if ((solid & (1 << other)) != 0 && Physics.GetIgnoreLayerCollision(layer, other)) { all = false; break; }
                if (all) return layer;
            }
            return 0;
        }

        /// <summary>
        /// Stop. Now: everything is handed back at once (the scene is going). Otherwise the view first rises back to where the
        /// game's camera was, and everything is handed back when it gets there.
        /// </summary>
        public void Finish(bool now = false)
        {
            if (finished) return;
            if (!now && place != null && place.Still && ending < 0f)
            {
                ending = clock;
                return;
            }
            finished = true;
            try { place?.End(); }
            catch (Exception ex) { Kit.Log(Foot.Name, "putting things back: " + ex); }
            if (Foot.Current == this) Foot.Current = null;
            if (this != null && gameObject != null) Destroy(gameObject);
        }

        public bool Ending => ending >= 0f;

        void OnDestroy()
        {
            // (taken away by something else, a development build's reload say: things are handed back all the same)
            if (!finished) Finish(true);
        }

        void Update()
        {
            if (finished) return;
            if (place == null || !place.Still) { Finish(true); return; }
            // (a clock of the walk's own: the same frame by frame whether the game runs freely or a recording steps it)
            float dt = Time.captureFramerate > 0 ? 1f / Time.captureFramerate : Mathf.Clamp(Time.unscaledDeltaTime > 0f ? Time.unscaledDeltaTime : Time.deltaTime, 0f, 0.1f);
            clock += dt;
            if (ending >= 0f) return;

            // ---- while Keystone's window is open over the walk, the pointer is the window's, and the walker stands still
            bool window = Host.Instance != null && Host.Instance.Open;
            if (window != paused)
            {
                paused = window;
                place.Pointer(!paused);
                settled = clock + 0.15f;
            }

            // ---- looking (not in the first moments after the pointer is taken: the jump it makes to the middle of the screen is not a move of the mouse)
            if (!paused && clock > settled)
            {
                float speed = 2.5f * Foot.look.Value;
                heading += Input.GetAxisRaw("Mouse X") * speed;
                pitch += Input.GetAxisRaw("Mouse Y") * speed * (Foot.invert.Value ? 1f : -1f);
                heading = Mathf.Repeat(heading, 360f);
                pitch = Mathf.Clamp(pitch, -85f, 85f);
            }

            // ---- walking: the game's own EVA keys (W, A, S, D, Shift to run, Space to jump unless the player has changed them)
            Driven drive = paused ? new Driven() : Drive();
            Vector3 up = place.Up;
            Quaternion facing = Quaternion.AngleAxis(heading, up);
            Vector3 wish = facing * new Vector3(drive.Across, 0f, drive.Along);
            if (wish.sqrMagnitude > 1f) wish.Normalize();
            float pace = drive.Run ? Foot.run.Value : Foot.walk.Value;
            // (quick to start and stop on the ground, as legs are; next to no say in the air)
            float grip = grounded ? 12f : 1.2f;
            going = Vector3.Lerp(going, wish * pace, 1f - Mathf.Exp(-grip * dt));

            if (grounded)
            {
                rising = -2f;                                                 // (held to the ground, down steps and slopes)
                if (drive.Jump) rising = Mathf.Sqrt(2f * place.Gravity * Jump);
            }
            else rising = Mathf.Max(rising - place.Gravity * dt, -40f);

            float fall = rising;
            CollisionFlags hit = body.Move((going + up * rising) * dt);
            bool wasGrounded = grounded;
            grounded = (hit & CollisionFlags.Below) != 0 || (body.isGrounded && rising <= 0f);
            if ((hit & CollisionFlags.Above) != 0 && rising > 0f) rising = 0f;
            if (grounded && !wasGrounded && fall < -2.5f) dip = Mathf.Min(0.16f, -fall * 0.018f);
            if (grounded && rising < -2f) rising = -2f;

            // ---- the head: a little rise and fall with each step, and the knees' give on landing
            Vector3 feet = transform.position;
            Vector3 moved = feet - lastFeet; moved -= up * Vector3.Dot(moved, up);
            lastFeet = feet;
            float across = moved.magnitude;
            float strideLength = drive.Run ? 0.95f : 0.55f;
            if (grounded && across > 0.0005f) stride += across / strideLength * Mathf.PI;
            float wanted = grounded ? Mathf.Clamp01(going.magnitude / Mathf.Max(0.1f, Foot.walk.Value)) : 0f;
            bobbing = Mathf.Lerp(bobbing, wanted, 1f - Mathf.Exp(-6f * dt));
            dip = Mathf.Lerp(dip, 0f, 1f - Mathf.Exp(-7f * dt));

            // ---- fallen out of the world (through a gap nothing covers): back to where the walk began
            if (feet.y < lowest && place.Start(out Vector3 again, out float _, out float _))
            {
                body.enabled = false; transform.position = again; body.enabled = true;
                going = Vector3.zero; rising = 0f; lastFeet = again;
            }
            readout = "Pace " + going.magnitude.ToString("F1") + " m/s" + (grounded ? "" : ", in the air") + ". " + Foot.KeyName + " or Esc stops, and the game's camera comes back where it was.";
        }

        static Driven Drive()
        {
            var d = new Driven();
#if DEV
            if (Scripted.Active) return Scripted.Now();
#endif
            if (GameSettings.EVA_forward.GetKey(true)) d.Along += 1f;
            if (GameSettings.EVA_back.GetKey(true)) d.Along -= 1f;
            if (GameSettings.EVA_right.GetKey(true)) d.Across += 1f;
            if (GameSettings.EVA_left.GetKey(true)) d.Across -= 1f;
            d.Run = GameSettings.EVA_Run.GetKey(true);
            d.Jump = GameSettings.EVA_Jump.GetKeyDown(true);
            return d;
        }

        void LateUpdate()
        {
            if (finished || place == null) return;
            Vector3 up = place.Up;
            float strength = Foot.bob.Value * bobbing;
            float rise = (Mathf.Abs(Mathf.Sin(stride)) - 0.5f) * 0.03f * strength - dip;
            float sway = Mathf.Sin(stride * 0.5f) * 0.012f * strength;
            Quaternion look = Quaternion.AngleAxis(heading, up) * Quaternion.AngleAxis(pitch, Vector3.right);
            Vector3 eye = transform.position + up * (Foot.eye.Value + rise) + look * Vector3.right * sway;
            float fov = Foot.fov.Value;

            if (ending >= 0f)
            {
                // ---- rising back to where the game's camera was, then everything handed back
                if (leaveFov <= 0f) { leaveEye = eye; leaveLook = look; leaveFov = fov; }
                float s = Smooth((clock - ending) / EaseOut);
                place.Look(Vector3.Lerp(leaveEye, fromEye, s), Quaternion.Slerp(leaveLook, fromLook, s), Mathf.Lerp(leaveFov, fromFov, s), 1f - s);
                if (s >= 1f) Finish(true);
                return;
            }
            // ---- coming down from the game's camera to the eyes, at first
            float e = Smooth((clock - began) / Ease);
            if (e < 1f) place.Look(Vector3.Lerp(fromEye, eye, e), Quaternion.Slerp(fromLook, look, e), Mathf.Lerp(fromFov, fov, e), e);
            else place.Look(eye, look, fov, 1f);
        }

        static float Smooth(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }

        static GUIStyle words;
        string keys;

        /// <summary>A key binding's key, as a player would name it.</summary>
        static string Key(KeyBinding binding)
        {
            string name = binding != null ? binding.name : "?";
            switch (name)
            {
                case "LeftShift": case "RightShift": return "Shift";
                case "LeftControl": case "RightControl": return "Ctrl";
                case "LeftAlt": case "RightAlt": return Application.platform == RuntimePlatform.OSXPlayer ? "Option" : "Alt";
                case "None": return "?";
                default: return name.Length > 5 && name.StartsWith("Alpha", StringComparison.Ordinal) ? name.Substring(5) : name;
            }
        }

        void OnGUI()
        {
            if (finished || ending >= 0f || !Foot.hint.Value || Event.current.type != EventType.Repaint) return;
            float age = clock - began, shown = Mathf.Clamp01((age - 0.5f) / 0.5f) * Mathf.Clamp01((7.5f - age) / 1.5f);
            if (shown <= 0f) return;
            if (words == null) words = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 13, wordWrap = false, richText = false };
            words.fontSize = Mathf.Max(11, Mathf.RoundToInt(13f * Screen.height / 720f));
            if (keys == null)
                keys = Key(GameSettings.EVA_forward) + " " + Key(GameSettings.EVA_left) + " " + Key(GameSettings.EVA_back) + " " + Key(GameSettings.EVA_right) + " to walk     " + Key(GameSettings.EVA_Run) + " to run     "
                       + Key(GameSettings.EVA_Jump) + " to jump     " + Foot.KeyName + " or Esc to stop";
            string text = keys;
            var where = new Rect(0f, Screen.height * 0.9f, Screen.width, 30f);
            Color was = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.6f * shown);
            GUI.Label(new Rect(where.x + 1f, where.y + 1f, where.width, where.height), text, words);
            GUI.color = new Color(1f, 1f, 1f, 0.85f * shown);
            GUI.Label(where, text, words);
            GUI.color = was;
        }

#if DEV
        /// <summary>For the development build: what the walk is doing, in a line.</summary>
        public string Stats() =>
            "at " + transform.position.ToString("F2") + ", heading " + heading.ToString("F0") + "°, pitch " + pitch.ToString("F0") + "°, going " + going.ToString("F2") + ", rising " + rising.ToString("F2")
            + (grounded ? ", on the ground" : ", in the air") + (ending >= 0f ? ", stopping" : "") + "; " + place.About;

        /// <summary>For the development build: turn the view to a heading and pitch.</summary>
        public void Face(float h, float p) { heading = Mathf.Repeat(h, 360f); pitch = Mathf.Clamp(p, -85f, 85f); }
        public float Heading => heading;
        public float Clock => clock;
        public float Pitch => pitch;

        /// <summary>For the development build: put the body somewhere at once.</summary>
        public void Put(Vector3 feet) { body.enabled = false; transform.position = feet; body.enabled = true; lastFeet = feet; going = Vector3.zero; rising = 0f; }
#endif
    }
}
