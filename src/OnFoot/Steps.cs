using UnityEngine;

namespace OnFoot
{
    /// <summary>
    /// (Since 0.3.0.) Footsteps: one at every footfall while walking or running on something, by what is underfoot, and a heavier
    /// one on landing from a fall.
    ///
    /// What a footstep is: the heel striking and the sole rolling down, a twentieth to a tenth of a second, and what is struck
    /// ringing or rustling after. On a hard floor (concrete, steel plate) a dull knock with a short click of the heel; on a grating
    /// or catwalk the same with the steel ringing and rattling in its frame; on soft ground a muffled thud in the rustle of what
    /// gives under it. A kerbal is light, so its steps are light: soft, and higher than a person's. Running, harder and closer
    /// together. Made here, as Fragmentation's sounds are (a few takes of each, chosen among and pitched a little apart, so that
    /// no two steps in a row are the same). Since 0.4.0, where the mod has its recordings (Keystone 0.10.0's Sounds), real
    /// footsteps from Sonniss's libraries instead: paving for a hard floor, steel for a grating, grass for soft ground, and a
    /// landing's; pitched up a little, a kerbal being light. The made ones meanwhile, and wherever there are none.
    ///
    /// What is underfoot is told from the name of what a line straight down from the feet meets (its object's, its collider's
    /// and its material's): "grat", "grid", "mesh", "catwalk" or "walkway" for a grating; "grass", "terrain", "dirt", "sand" or
    /// "ground" for soft ground; anything else is a hard floor.
    /// </summary>
    static class Steps
    {
        public enum Floor { Hard, Grating, Soft }
        const int Rate = 44100, Takes = 4;
        static AudioClip[][] clips;
        static int last = -1;

        /// <summary>What is underfoot there, from what a line straight down meets (nothing: a hard floor).</summary>
        public static Floor Under(Vector3 feet, Vector3 up, int mask)
        {
            if (!Physics.Raycast(feet + up * 0.3f, -up, out RaycastHit hit, 1.2f, mask, QueryTriggerInteraction.Ignore) || hit.collider == null) return Floor.Hard;
            string names = (hit.collider.name + " " + hit.collider.gameObject.name + " " + (hit.collider.sharedMaterial != null ? hit.collider.sharedMaterial.name : "")
                            + " " + (hit.collider.GetComponent<Renderer>() is Renderer r && r.sharedMaterial != null ? r.sharedMaterial.name : "")).ToLowerInvariant();
            if (names.Contains("grat") || names.Contains("grid") || names.Contains("mesh") || names.Contains("catwalk") || names.Contains("walkway")) return Floor.Grating;
            if (names.Contains("grass") || names.Contains("terrain") || names.Contains("dirt") || names.Contains("sand") || names.Contains("ground")) return Floor.Soft;
            return Floor.Hard;
        }

        /// <summary>A footstep, or a landing, on what is underfoot.</summary>
        public static void Play(AudioSource s, Floor floor, bool run, float landing)
        {
            if (s == null || Hush.Paused) return;
            if (clips == null) Make();
            int take = Random.Range(0, Takes);
            if (take == last) take = (take + 1) % Takes;
            last = take;
            AudioClip heard = RecordedFor(floor, landing > 0.3f);
            AudioClip clip = heard != null ? heard : clips[(int)floor][take];
            float loud = Foot.steps != null ? Foot.steps.Value : 0.6f;
            loud *= GameSettings.AMBIENCE_VOLUME * (run ? 1.3f : 1f) * (1f + 1.5f * Mathf.Clamp01(landing));
            if (loud < 0.003f) return;
            s.pitch = (heard != null ? Random.Range(1.04f, 1.14f) : Random.Range(0.94f, 1.07f)) * (landing > 0f && heard == null ? 0.85f : 1f);
            s.PlayOneShot(clip, Mathf.Min(1f, loud));
#if DEV
            if (heard != null) Recorded++; else Made++;
#endif
        }
#if DEV
        /// <summary>(Development build: the steps heard so far, recorded and made, there being no ears here.)</summary>
        internal static int Recorded, Made;
#endif

        // ---- the recordings (since 0.4.0)
        static readonly string[] Kinds = { "step-paved-", "step-metal-", "step-grass-" };
        static System.Collections.Generic.List<string>[] recorded;
        static System.Collections.Generic.List<string> landings;
        static bool recordedGone;
        static int lastRecorded = -1;

        /// <summary>
        /// The recordings asked for ahead of the first step: as the space centre or a building where one can walk opens. (Asked
        /// for at the first step, the first four steps of a walk were made ones: Keystone reads one sound a frame, and there
        /// are fifty-five.)
        /// </summary>
        public static void AskAhead()
        {
            if (recordedGone || recorded != null) return;
            try { Recording(Floor.Hard, false); }
            catch (System.Exception) { recordedGone = true; }
        }

        /// <summary>A recorded step (or landing) for what is underfoot, once loaded; null without the recordings, or until then.</summary>
        static AudioClip RecordedFor(Floor floor, bool landing)
        {
            if (recordedGone) return null;
            try { return Recording(floor, landing); }
            catch (System.Exception) { recordedGone = true; return null; }      // (a Keystone older than 0.10.0: no Sounds there)
        }

        static AudioClip Recording(Floor floor, bool landing)
        {
            if (recorded == null)
            {
                recorded = new System.Collections.Generic.List<string>[3];
                for (int f = 0; f < 3; f++) recorded[f] = new System.Collections.Generic.List<string>();
                landings = new System.Collections.Generic.List<string>();
                foreach (string name in Keystone.Sounds.Names("OnFoot"))
                {
                    for (int f = 0; f < 3; f++) if (name.StartsWith(Kinds[f])) recorded[f].Add(name);
                    if (name.StartsWith("land-")) landings.Add(name);
                    // (all of them loaded at the first step: a step cannot wait for its sound)
                    Keystone.Sounds.Get("OnFoot", name, _ => { });
                }
            }
            System.Collections.Generic.List<string> from = landing && landings.Count > 0 ? landings : recorded[(int)floor];
            if (from.Count == 0) return null;
            int pick = Random.Range(0, from.Count);
            if (pick == lastRecorded && from.Count > 1) pick = (pick + 1) % from.Count;
            lastRecorded = pick;
            return Keystone.Sounds.Ready("OnFoot", from[pick]);
        }

        static void Make()
        {
            clips = new AudioClip[3][];
            var luck = new System.Random(5150);
            for (int f = 0; f < 3; f++)
            {
                clips[f] = new AudioClip[Takes];
                for (int t = 0; t < Takes; t++) clips[f][t] = One((Floor)f, luck);
            }
        }

        /// <summary>One take: the heel's click, the knock of the sole and what the floor does after, so many seconds long.</summary>
        static AudioClip One(Floor floor, System.Random luck)
        {
            float Rnd() => (float)luck.NextDouble();
            float seconds = floor == Floor.Grating ? 0.45f : floor == Floor.Soft ? 0.25f : 0.18f;
            int n = (int)(seconds * Rate);
            var s = new float[n];
            float lp = 0f, lp2 = 0f, hp = 0f, prev = 0f;
            // (the heel first, a twentieth of a second before the sole: two strikes)
            float roll = 0.03f + 0.03f * Rnd();
            float knockHz = floor == Floor.Soft ? 90f + 40f * Rnd() : 160f + 80f * Rnd();
            float ringHz = 900f + 900f * Rnd(), ring2 = ringHz * (1.41f + 0.2f * Rnd());
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float noise = Rnd() * 2f - 1f;
                float v = 0f;
                for (int strike = 0; strike < 2; strike++)
                {
                    float at = strike == 0 ? 0f : roll, u = t - at;
                    if (u < 0f) continue;
                    float hard = strike == 0 ? 0.6f : 1f;
                    // (the knock: a low tone dying in a few hundredths of a second)
                    v += hard * Mathf.Sin(2f * Mathf.PI * knockHz * u) * Mathf.Exp(-u / (floor == Floor.Soft ? 0.03f : 0.018f));
                    // (the click: noise for a few milliseconds, sharper on hard floors, smothered on soft ground)
                    v += hard * noise * Mathf.Exp(-u / (floor == Floor.Soft ? 0.004f : 0.0025f)) * (floor == Floor.Soft ? 0.25f : 0.7f);
                    if (floor == Floor.Grating)
                    {
                        // (the steel ringing and rattling in its frame)
                        v += hard * 0.35f * (Mathf.Sin(2f * Mathf.PI * ringHz * u) + 0.6f * Mathf.Sin(2f * Mathf.PI * ring2 * u)) * Mathf.Exp(-u / 0.12f);
                        v += hard * 0.2f * noise * Mathf.Exp(-u / 0.05f) * (0.5f + 0.5f * Mathf.Sin(2f * Mathf.PI * 31f * u));
                    }
                    if (floor == Floor.Soft) v += hard * 0.35f * noise * Mathf.Exp(-u / 0.06f);   // (what gives underfoot, rustling)
                }
                // (soft ground and a light foot take off the top; nothing below 50 Hz)
                float cut = floor == Floor.Soft ? 0.25f : floor == Floor.Grating ? 0.75f : 0.5f;
                lp = lp + cut * (v - lp); lp2 = lp2 + cut * (lp - lp2);
                hp = 0.993f * (hp + lp2 - prev); prev = lp2;
                s[i] = hp;
            }
            float peak = 1e-6f;
            for (int i = 0; i < n; i++) peak = Mathf.Max(peak, Mathf.Abs(s[i]));
            for (int i = 0; i < n; i++) s[i] = s[i] / peak * 0.8f * (i > n - 200 ? (n - i) / 200f : 1f);
            AudioClip clip = AudioClip.Create("On Foot step " + floor, n, 1, Rate, false);
            clip.SetData(s, 0);
            return clip;
        }
    }
}
