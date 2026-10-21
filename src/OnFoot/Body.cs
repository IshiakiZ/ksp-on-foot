using System;
using System.Collections.Generic;
using Keystone;
using UnityEngine;

namespace OnFoot
{
    /// <summary>
    /// (Since 0.3.0.) The walker's own body, seen as one sees one's own when looking down: legs and feet stepping under the eyes,
    /// arms swinging at the sides, the front of the coat. The walker is one of the ground crew, so the body is one of theirs: a copy
    /// of a crew kerbal's own model as the game made it, with the game's own walk and run, made for the walk.
    ///
    /// What is taken off the copy: whatever made it one of the crew (its walking its rounds, its weight; the crew do not make way
    /// for it, and it is not solid), what it carries in its hands (a hammer, a notepad, a pen), what it wears on its head, and its
    /// head, whose bones are shrunk to nothing so that the neck stays and closes the collar (the eyes are where the head was). It is made as
    /// tall as the walker's eyes say (a crew kerbal's eyes are some 0.57 m up), set back so that its eyes are where the walker's
    /// are (a kerbal's eyes are a little ahead of its feet), turned the way the walker faces, and walks in step: the
    /// game's walk played a cycle for every two of the walker's steps, its run when running, its stance when standing still, the
    /// stride held in the air. Walking sideways the hips turn toward the way it goes, as a person's do, up to 70 degrees; walking
    /// backward the walk is played backward. On a ladder nothing of it is drawn (it would stand on air).
    /// </summary>
    sealed class Body
    {
        const string Walking = "wkC_forward01", Running = "wkC_run01", Standing = "idle";
        const float MostTurn = 70f;

        readonly GameObject copy;
        readonly Transform head;
        readonly Animation moves;
        readonly AnimationState walking, running, standing;
        readonly float eyeAt;                         // (the copy's eyes above its feet at its own size)
        readonly Vector2 eyeAhead;                    // (and how far ahead of its feet and to its right, at its own size)
        readonly Vector3 headAt;                      // (where its head turns, from its feet, standing, at its own size)
        Vector3 upNow = Vector3.up;
        float scaleNow = 1f;
        float turned;                                 // (the hips' turn toward the way it goes, eased)
        public readonly string About;

        Body(GameObject copy, Transform head, Animation moves, float eyeAt, Vector2 eyeAhead, Vector3 headAt, string about)
        {
            this.copy = copy; this.head = head; this.moves = moves; this.eyeAt = eyeAt; this.eyeAhead = eyeAhead; this.headAt = headAt; About = about;
            walking = moves[Walking]; running = moves[Running]; standing = moves[Standing];
            foreach (AnimationState s in new[] { walking, running, standing })
            {
                if (s == null) continue;
                s.wrapMode = WrapMode.Loop;
                s.layer = 0;
                s.enabled = true;
                s.weight = 0f;
            }
            if (walking != null) walking.speed = 0f;
            if (running != null) running.speed = 0f;
            if (standing != null) { standing.speed = 1f; standing.weight = 1f; }
        }

        /// <summary>
        /// A body for the walk, under the walker's own object (so that it goes when the walk goes), or none where there are no crew
        /// to copy (an editor without them) or the copy cannot be made; why not, in Why.
        /// </summary>
        public static Body Make(Transform walker)
        {
            Why = "";
            global::SpaceCenterCrew source = Pick();
            if (source == null) { Why = "no ground crew here to take a body from"; return null; }
            var making = new GameObject("On Foot: body (being made)");
            making.SetActive(false);
            GameObject copy = null;
            try
            {
                // (made under something switched off, nothing of the copy wakes: none of the crew's own scripts run in it)
                copy = UnityEngine.Object.Instantiate(source.gameObject, making.transform, false);
                copy.name = "On Foot: the walker's body";
                // (the crew's own scripts first, then joints and shapes, then weights: one may need another while it is there)
                for (int pass = 0; pass < 3; pass++)
                    foreach (Component c in copy.GetComponentsInChildren<Component>(true))
                    {
                        if (c == null || c is Transform || c is Renderer || c is MeshFilter || c is Animation) continue;
                        bool now = pass == 0 ? c is MonoBehaviour : pass == 1 ? !(c is Rigidbody) : true;
                        if (now) UnityEngine.Object.DestroyImmediate(c);
                    }
                Animation moves = copy.GetComponent<Animation>();
                if (moves == null || moves[Walking] == null) { Why = "the crew's model has no walk of its own"; UnityEngine.Object.Destroy(making); return null; }
                Transform head = Find(copy.transform, "bn_headPivot_a01"), eye = Find(copy.transform, "jntDrv_l_eye01") ?? Find(copy.transform, "bn_headPivot_b01"),
                          eyeRight = Find(copy.transform, "jntDrv_r_eye01") ?? eye;
                if (head == null || eye == null) { Why = "the crew's model has no head where a kerbal's is"; UnityEngine.Object.Destroy(making); return null; }
                // (what is carried, and the head's own pieces, not drawn: a hand's prop is a piece on one bone of the wrist)
                int hidden = 0;
                foreach (SkinnedMeshRenderer r in copy.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    Transform root = r.rootBone;
                    bool carried = root != null && r.bones != null && r.bones.Length <= 1 && root.name.Contains("wrist");
                    bool ofHead = root != null && root.IsChildOf(head);
                    if (carried || ofHead) { r.enabled = false; hidden++; }
                }
                foreach (Renderer r in copy.GetComponentsInChildren<Renderer>(true))
                {
                    r.receiveShadows = true;
                    r.motionVectorGenerationMode = MotionVectorGenerationMode.Object;
                }
                copy.transform.SetParent(walker, false);
                copy.transform.localPosition = Vector3.zero;
                copy.transform.localRotation = Quaternion.identity;
                copy.transform.localScale = Vector3.one;
                UnityEngine.Object.Destroy(making);
                copy.SetActive(true);
                // (its eyes' height, stood as it stands in the game's own stance: the moves can be laid on only once it is switched on)
                moves.Play(Standing);
                moves.Sample();
                Vector3 eyes = 0.5f * (copy.transform.InverseTransformPoint(eye.position) + copy.transform.InverseTransformPoint(eyeRight.position));
                float eyeAt = Mathf.Max(0.2f, eyes.y);
                var eyeAhead = new Vector2(eyes.x, eyes.z);
                Vector3 headAt = copy.transform.InverseTransformPoint(head.position);
                var shownNames = new List<string>();
                foreach (Renderer r in copy.GetComponentsInChildren<Renderer>(true)) if (r.enabled) shownNames.Add(r.name);
                string about = "a body copied from " + source.name + " (eyes " + eyeAt.ToString("F2") + " m up at its own size, " + hidden + " pieces not drawn; drawn: " + string.Join(", ", shownNames.ToArray()) + ")";
                Kit.Log(Foot.Name, about);
                return new Body(copy, head, moves, eyeAt, eyeAhead, headAt, about);
            }
            catch (Exception ex)
            {
                Why = "the body could not be made: " + ex.Message;
                Kit.Log(Foot.Name, Why);
                if (copy != null) UnityEngine.Object.Destroy(copy);
                UnityEngine.Object.Destroy(making);
                return null;
            }
        }

        public static string Why = "";

        /// <summary>One of the crew to copy: one with the game's walk, one of those in work clothes rather than a lab coat if there are any.</summary>
        static global::SpaceCenterCrew Pick()
        {
            global::SpaceCenterCrew best = null;
            int bestScore = -1;
            foreach (global::SpaceCenterCrew one in UnityEngine.Object.FindObjectsOfType<global::SpaceCenterCrew>())
            {
                if (one == null) continue;
                Animation a = one.GetComponent<Animation>();
                if (a == null || a[Walking] == null) continue;
                string path = one.transform.parent != null ? one.transform.parent.name + "/" + one.name : one.name;
                int score = path.IndexOf("Scientist", StringComparison.OrdinalIgnoreCase) >= 0 ? 1 : 2;
                if (score > bestScore) { best = one; bestScore = score; }
            }
            return best;
        }

        static Transform Find(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform c in t) { Transform f = Find(c, name); if (f != null) return f; }
            return null;
        }

        /// <summary>
        /// Every frame, after the walker has moved: where its feet are, which way it faces and which way it is going (across the
        /// ground, m/s), how far through its stride it is (radians: π a step), how much it is walking (0 standing to 1), how much
        /// of that is running, whether it is on the ground, on a ladder, and how high its eyes are.
        /// </summary>
        public void Pose(Vector3 feet, Vector3 up, float heading, Vector3 going, float stride, float walkingNow, float runningNow, bool grounded, bool climbing, float eyes, float dt)
        {
            if (copy == null) return;
            bool show = !climbing && (Foot.body == null || Foot.body.Value);
            if (copy.activeSelf != show) copy.SetActive(show);
            if (!show) return;

            // ---- the way it goes against the way it faces: the hips turned toward it, or the walk played backward
            Quaternion facing = Quaternion.AngleAxis(heading, up);
            Vector3 ahead = facing * Vector3.forward;
            float across = going.magnitude, wanted = 0f;
            bool back = false;
            if (across > 0.15f)
            {
                float toward = Vector3.SignedAngle(ahead, going, up);
                if (Mathf.Abs(toward) > 110f) { back = true; toward = Mathf.DeltaAngle(180f, toward); }
                wanted = Mathf.Clamp(toward, -MostTurn, MostTurn);
            }
            turned = Mathf.LerpAngle(turned, wanted, 1f - Mathf.Exp(-8f * dt));

            // ---- its size, place and turn: as tall as the walker's eyes say
            float scale = Mathf.Clamp(eyes / eyeAt, 0.5f, 4f);
            scaleNow = scale; upNow = up;
            Transform t = copy.transform;
            t.localScale = Vector3.one * scale;
            t.rotation = Quaternion.AngleAxis(turned, up) * facing;
            // (set back so that its eyes are where the walker's are, over the feet: they are a little ahead of a kerbal's own feet)
            t.position = feet - facing * new Vector3(eyeAhead.x * scale, 0f, eyeAhead.y * scale);
            // ---- the head shrunk away (after the moves are laid on, which set the bones each frame: see Late)
            pendingHead = true;

            // ---- its moves: a cycle of the walk (or the run) for two of the walker's steps; standing when still
            float phase = Mathf.Repeat(stride / (2f * Mathf.PI), 1f);
            if (back) phase = 1f - phase;
            float move = grounded ? Mathf.Clamp01(walkingNow) : Mathf.Max(Mathf.Clamp01(walkingNow), 0.6f);
            float run = Mathf.Clamp01(runningNow);
            float wWalk = move * (1f - run), wRun = running != null ? move * run : 0f, wStand = 1f - move;
            if (running == null) wWalk = move;
            float sum = Mathf.Max(1e-4f, wWalk + wRun + wStand);
            if (walking != null) { walking.weight = wWalk / sum; walking.normalizedTime = phase; }
            if (running != null) { running.weight = wRun / sum; running.normalizedTime = phase; }
            if (standing != null) standing.weight = wStand / sum;
        }

        bool pendingHead;

        /// <summary>
        /// After the game has laid the moves on the bones this frame: the head's bones shrunk to nothing, so that the neck closes the
        /// collar. (Not drawn at all, the head's mesh left the collar an open hole under the eyes, the neck being part of it. What is
        /// left where the neck meets the head lies behind and below the eyes, out of any view down.)
        /// </summary>
        public void Late()
        {
            if (!pendingHead || head == null) return;
            // (the head kept over the place it has standing, under the walker's eyes, however the moves lean the body: a runner
            // leans forward, its head ahead of its feet; left as the moves put it, the neck came forward under the eyes)
            Transform t = copy.transform;
            Vector3 standing = t.position + t.rotation * (headAt * scaleNow), lean = head.position - standing;
            lean -= upNow * Vector3.Dot(lean, upNow);
            t.position -= lean;
            head.localScale = Vector3.one * 0.001f;
            pendingHead = false;
        }

        public void Off()
        {
            if (copy != null) UnityEngine.Object.Destroy(copy);
        }

        public bool Shown => copy != null && copy.activeSelf;
    }
}
