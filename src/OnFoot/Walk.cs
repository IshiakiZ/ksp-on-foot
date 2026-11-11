using System;
using Keystone;
using System.Globalization;
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
        // (as installed: the page's fine details may set others)
        const float Step = 0.3f, Slope = 50f, Jump = 0.5f, Ease = 0.7f, EaseOut = 0.45f;

        Place place;
        CharacterController body;
        float heading, pitch;
        Vector3 going;                            // across the ground, metres a second (in the world)
        float rising;                             // up, metres a second
        float stride, bobbing, dip;               // how far through a step; how much the head moves now; how far it has dropped on landing
        float running, dipAt = -10f;              // (since 0.3.0) how much the gait is a run (eased); when the last landing was
        bool grounded = true;
        bool paused;                              // Keystone's window is open: the pointer is free for it
        float settled;                            // (the mouse is not read until then)
        float clock, began, ending = -1f;           // the walk's own clock (seconds since it began), when it began, and when it began to end
        Vector3 fromEye, leaveEye; Quaternion fromLook, leaveLook; float fromFov, leaveFov;
        Vector3 lastFeet;
        float lowest;
        bool finished;
        string readout = "";
        // (since 0.3.0: footsteps, see Steps)
        AudioSource feetSound;
        int footfalls;
        Steps.Floor floorNow;
        float floorLooked = -10f;
        // (since 0.3.0: ladders, see Climb)
        Collider ladder;
        float ladderLooked = -10f, climbed;
        // (since 0.3.0: the walker's own legs and feet, see Body)
        Body own;

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
            body.stepOffset = Foot.Of(Foot.stepUp, Step); body.slopeLimit = Foot.Of(Foot.slope, Slope); body.skinWidth = 0.03f; body.minMoveDistance = 0f;
            var walk = holder.AddComponent<Walk>();
            walk.place = place; walk.body = body;
            Crowd.Join(holder, holder.transform);
            walk.feetSound = holder.AddComponent<AudioSource>();
            walk.feetSound.spatialBlend = 0f; walk.feetSound.playOnAwake = false; walk.feetSound.dopplerLevel = 0f;
            Hush.Add(walk.feetSound);
            walk.heading = heading; walk.pitch = pitch;
            walk.began = 0f;
            walk.settled = 0.25f;
            place.Was(out walk.fromEye, out walk.fromLook, out walk.fromFov);
            walk.lastFeet = feet;
            walk.lowest = feet.y - 250f;
            walk.own = Body.Make(holder.transform);
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

            // ---- (since 0.3.0) a ladder walked into, facing it, is climbed: see Climb
            if (ladder == null && !paused && drive.Along > 0.5f && clock - ladderLooked > 0.1f) { ladderLooked = clock; ladder = LadderAhead(facing * Vector3.forward, up); if (ladder != null) climbed = 0f; }
            if (ladder != null && Climb(drive, dt, up)) { going = Vector3.zero; grounded = false; own?.Pose(transform.position, up, heading, going, stride, 0f, 0f, false, true, Foot.eye.Value, dt); Say(); return; }

            if (grounded)
            {
                rising = -2f;                                                 // (held to the ground, down steps and slopes)
                if (drive.Jump) rising = Mathf.Sqrt(2f * place.Gravity * Foot.Of(Foot.jump, Jump));
            }
            else rising = Mathf.Max(rising - place.Gravity * dt, -40f);

            float fall = rising;
            // (the page's highest step and steepest slope, as they are now: they may be changed during a walk)
            body.stepOffset = Foot.Of(Foot.stepUp, Step); body.slopeLimit = Foot.Of(Foot.slope, Slope);
            CollisionFlags hit = body.Move((going + up * rising) * dt);
            bool wasGrounded = grounded;
            grounded = (hit & CollisionFlags.Below) != 0 || (body.isGrounded && rising <= 0f);
            if ((hit & CollisionFlags.Above) != 0 && rising > 0f) rising = 0f;
            if (grounded && !wasGrounded && fall < -2.5f)
            {
                dip = Mathf.Min(0.16f, -fall * 0.018f); dipAt = clock;
                Steps.Play(feetSound, Steps.Under(transform.position, up, place.Solid), false, Mathf.Clamp01(-fall / 8f));
            }
            if (grounded && rising < -2f) rising = -2f;

            // ---- the head: a little rise and fall with each step, and the knees' give on landing
            Vector3 feet = transform.position;
            Vector3 moved = feet - lastFeet; moved -= up * Vector3.Dot(moved, up);
            lastFeet = feet;
            float across = moved.magnitude;
            float strideLength = drive.Run ? 0.95f : 0.55f;
            if (grounded && across > 0.0005f) stride += across / strideLength * Mathf.PI;
            // (a footfall at every half of the head's round: what is underfoot looked at four times a second at the most)
            int footfall = Mathf.FloorToInt(stride / Mathf.PI);
            if (footfall != footfalls)
            {
                footfalls = footfall;
                if (grounded)
                {
                    if (clock - floorLooked > 0.25f) { floorNow = Steps.Under(feet, up, place.Solid); floorLooked = clock; }
                    Steps.Play(feetSound, floorNow, drive.Run, 0f);
                }
            }
            float wanted = grounded ? Mathf.Clamp01(going.magnitude / Mathf.Max(0.1f, Foot.walk.Value)) : 0f;
            bobbing = Mathf.Lerp(bobbing, wanted, 1f - Mathf.Exp(-6f * dt));
            running = Mathf.Lerp(running, drive.Run && grounded ? 1f : grounded ? 0f : running, 1f - Mathf.Exp(-5f * dt));

            // ---- (since 0.3.0) the walker's own body, in step
            own?.Pose(feet, up, heading, going, stride, bobbing, running, grounded, false, Foot.eye.Value, dt);

            // ---- fallen out of the world (through a gap nothing covers): back to where the walk began
            if (feet.y < lowest && place.Start(out Vector3 again, out float _, out float _))
            {
                body.enabled = false; transform.position = again; body.enabled = true;
                going = Vector3.zero; rising = 0f; lastFeet = again;
            }
            Say();
        }

        void Say() => readout = (ladder != null ? "On a ladder: forward climbs, back goes down, jump lets go. " : "Pace " + going.magnitude.ToString("F1") + " m/s" + (grounded ? "" : ", in the air") + ". ")
                                    + Foot.KeyName + " or Esc stops, and the game's camera comes back where it was.";

        /// <summary>
        /// (Since 0.3.0.) A ladder within reach in front: one the game itself lets a kerbal on EVA climb (its shape tagged "Ladder"): a
        /// space centre's on its towers and tanks, a craft's own. Nothing if none, or if it is not in front (within 70° of the way the
        /// walker faces).
        /// </summary>
        Collider LadderAhead(Vector3 ahead, Vector3 up)
        {
            Vector3 feet = transform.position;
            int found = Physics.OverlapCapsuleNonAlloc(feet + up * 0.25f, feet + up * (Height - 0.25f), Radius + 0.35f, near, ~0, QueryTriggerInteraction.Collide);
            Collider best = null; float bestD = float.MaxValue;
            for (int i = 0; i < found; i++)
            {
                Collider c = near[i];
                if (c == null || !c.enabled || !c.CompareTag("Ladder")) continue;
                Vector3 to = c.ClosestPoint(feet + up * (0.5f * Height)) - feet; to -= up * Vector3.Dot(to, up);
                float d = to.magnitude;
                if (d > 1e-3f && Vector3.Dot(to / d, ahead) < 0.34f) continue;
                if (d < bestD) { bestD = d; best = c; }
            }
            return best;
        }

        static readonly Collider[] near = new Collider[32];

        /// <summary>
        /// (Since 0.3.0.) Up or down the ladder being climbed, at a kerbal's pace on a ladder (0.7 m/s, 1.2 with the run key; the game's
        /// EVA kerbals go about so), held a hand's breadth off it, a rung's clank every 0.3 m; off the top, a step forward onto what is
        /// there; at its foot, going down, back on the floor; the jump key lets go. False when the climb is over (then the walk goes
        /// on as usual this frame).
        /// </summary>
        bool Climb(Driven drive, float dt, Vector3 up)
        {
            if (ladder == null || !ladder.enabled || !ladder.gameObject.activeInHierarchy || drive.Jump) { ladder = null; return false; }
            Vector3 feet = transform.position;
            Bounds b = ladder.bounds;
            float top = Vector3.Dot(b.center, up) + Mathf.Abs(b.extents.x * up.x) + Mathf.Abs(b.extents.y * up.y) + Mathf.Abs(b.extents.z * up.z);
            float bottom = 2f * Vector3.Dot(b.center, up) - top;
            float h = Vector3.Dot(feet, up);
            float climb = drive.Along * (drive.Run ? 1.2f : 0.7f);
            // (at its foot, going down: on the floor again)
            if (climb < 0f && h <= bottom + 0.05f) { ladder = null; return false; }
            Vector3 mid = feet + up * (0.5f * Height);
            Vector3 onIt = ladder.ClosestPoint(mid), away = mid - onIt; away -= up * Vector3.Dot(away, up);
            float off = away.magnitude;
            Vector3 outward = off > 1e-3f ? away / off : -(Quaternion.AngleAxis(heading, up) * Vector3.forward);
            // (off the top: over it and a step forward, onto whatever is there; the walk then finds its feet)
            if (climb > 0f && h >= top - 0.15f)
            {
                body.Move(up * 0.35f - outward * 0.55f);
                ladder = null; rising = 0f; lastFeet = transform.position;
                return false;
            }
            // (held a hand's breadth off it: the body's own radius and ten centimetres)
            Vector3 hold = outward * ((Radius + 0.1f) - off);
            body.Move(up * (climb * dt) + hold * Mathf.Min(1f, 8f * dt));
            float risen = Vector3.Dot(transform.position - feet, up);
            climbed += Mathf.Abs(risen);
            if (climbed >= 0.3f) { climbed -= 0.3f; Steps.Play(feetSound, Steps.Floor.Grating, false, 0f); }
            lastFeet = transform.position; rising = 0f;
            return true;
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
            own?.Late();
            Vector3 up = place.Up;
            // (Since 0.3.0, as a person's head moves, scaled to the eyes' height: a person's eyes are 1.6 m up, a kerbal's 0.8.
            // Walking, the body vaults over the standing leg: the head is lowest as a heel strikes, both feet on the ground, and
            // highest as it passes over the foot, 4 to 5 cm between for a person; and it sways 2 to 4 cm towards the standing leg,
            // once a stride. Running is a bounce on a spring: lowest at mid-stance as the leg takes the weight, highest in the
            // flight between steps, some 8 cm between. A landing bends the knees and springs back with a little overshoot.
            // Nothing of it turns the view: the eyes keep on what they look at, as a person's do.)
            float strength = Foot.bob.Value * bobbing, size = Foot.eye.Value / 1.6f;
            float step = stride / Mathf.PI, phase = step - Mathf.Floor(step);                         // (0 as a foot strikes)
            float walkRise = -Mathf.Cos(2f * Mathf.PI * phase) * 0.0225f;                              // (4.5 cm between)
            float runRise = -Mathf.Cos(2f * Mathf.PI * (phase - 0.3f)) * 0.04f;                        // (8 cm between)
            // (the landing's give: down at once by as much as the fall was hard, back up past where it was by an eighth of that a
            // third of a second later, and still in a second; the depths are for a kerbal's eyes, 0.8 m up)
            float since = clock - dipAt;
            float give = since < 1.2f ? -dip * Mathf.Exp(-6f * since) * Mathf.Cos(9f * since) : 0f;
            float rise = Mathf.Lerp(walkRise, runRise, running) * size * strength + give * size * 2f;
            float sway = Mathf.Sin(stride * 0.5f) * Mathf.Lerp(0.015f, 0.01f, running) * size * strength;
            Quaternion look = Quaternion.AngleAxis(heading, up) * Quaternion.AngleAxis(pitch, Vector3.right);
            Vector3 eye = transform.position + up * (Foot.eye.Value + rise) + look * Vector3.right * sway;
#if DEV
            Trace(rise, sway);
#endif
            float fov = Foot.fov.Value;

            if (ending >= 0f)
            {
                // ---- rising back to where the game's camera was, then everything handed back
                if (leaveFov <= 0f) { leaveEye = eye; leaveLook = look; leaveFov = fov; }
                float s = Smooth((clock - ending) / Foot.Of(Foot.leaves, EaseOut));
                place.Look(Vector3.Lerp(leaveEye, fromEye, s), Quaternion.Slerp(leaveLook, fromLook, s), Mathf.Lerp(leaveFov, fromFov, s), 1f - s);
                if (s >= 1f) Finish(true);
                return;
            }
            // ---- coming down from the game's camera to the eyes, at first
            float e = Smooth((clock - began) / Foot.Of(Foot.comes, Ease));
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

        /// <summary>For the development build: the walker's own body (14.2), what it was copied from and whether it is drawn.</summary>
        public string BodySaid => own != null ? own.About + (own.Shown ? ", drawn" : ", not drawn") : "no body: " + Body.Why;

        /// <summary>For the development build: turn the view to a heading and pitch.</summary>
        public void Face(float h, float p) { heading = Mathf.Repeat(h, 360f); pitch = Mathf.Clamp(p, -85f, 85f); }
        public float Heading => heading;
        public float Clock => clock;
        public float Pitch => pitch;

        /// <summary>For the development build: put the body somewhere at once.</summary>
        public void Put(Vector3 feet) { body.enabled = false; transform.position = feet; body.enabled = true; lastFeet = feet; going = Vector3.zero; rising = 0f; }

        // (for the development build: the eyes' height above the feet and their sway, frame by frame, with the footfalls, while traced)
        internal static System.Text.StringBuilder Traced;
        internal void Trace(float rise, float sway)
        {
            if (Traced == null || Traced.Length > 400000) return;
            Traced.Append(clock.ToString("F3", CultureInfo.InvariantCulture)).Append(' ').Append((rise * 100f).ToString("F2", CultureInfo.InvariantCulture)).Append(' ')
                  .Append((sway * 100f).ToString("F2", CultureInfo.InvariantCulture)).Append(' ').Append(footfalls).Append(' ').Append(grounded ? 1 : 0).Append(' ')
                  .Append(running.ToString("F2", CultureInfo.InvariantCulture)).Append('\n');
        }
#endif
    }
}
