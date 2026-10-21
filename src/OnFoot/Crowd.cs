using System.Collections.Generic;
using UnityEngine;

namespace OnFoot
{
    /// <summary>
    /// (Since 0.3.0.) The ground crew making way for the walker, as people walking among others do.
    ///
    /// What people do (studies of walkers crossing and meeting, filmed and tracked: Olivier and others 2012, Moussaïd and others
    /// 2009): each judges, from where the other is and how both are going, how near they would pass if neither changed course, and
    /// changes course only if that would be nearer than about a metre, beginning two to four metres off; the change is mostly
    /// sideways and a little slower, and they pass with some 0.8 m between them, most often each keeping to its right. One standing
    /// still steps back out of the way of one coming close. Without this the crew walk their rounds as the game has them, blind, and
    /// walk into the walker (whom the mod has made solid to them) and push the walker aside.
    ///
    /// Here, after the game has moved each of the crew for the frame: how near each would pass the walker if both kept on (the
    /// nearest point of their paths, from where both are and how both have been moving), and if it would be nearer than a metre
    /// within the next three seconds or four metres, it is moved aside, at no more than a stepping pace, by as much as brings that up to a metre by
    /// the time they pass, away from the side the walker would pass on (to its right if straight ahead), and held back a little if
    /// the walker stands right in its way. Its own walking takes it back to its round afterwards. One standing about steps back from
    /// a walker coming within a metre and a quarter, a metre at most. Nothing is changed of the crew but where they are.
    /// </summary>
    public sealed class Crowd : MonoBehaviour
    {
        const float Clear = 1.0f, Ahead = 3f, Near = 4f, Side = 0.6f, Back = 0.4f, Room = 1.25f, MostBack = 1f;

        Transform walker;
        readonly List<global::SpaceCenterCrew> crew = new List<global::SpaceCenterCrew>();
        readonly Dictionary<global::SpaceCenterCrew, Vector3> was = new Dictionary<global::SpaceCenterCrew, Vector3>();
        readonly Dictionary<global::SpaceCenterCrew, Vector3> stoodAt = new Dictionary<global::SpaceCenterCrew, Vector3>();
        Vector3 walkerWas;
        float lookAgain;
        bool first = true;
#if DEV
        // (for the development build: the nearest any of the crew came, how far they were moved aside in all, and how each was going last)
        internal static float Nearest = float.MaxValue, Moved;
        internal static int Gave;
        internal static readonly Dictionary<global::SpaceCenterCrew, Vector3> GoingNow = new Dictionary<global::SpaceCenterCrew, Vector3>();
#endif

        public static Crowd Join(GameObject holder, Transform walker)
        {
            var crowd = holder.AddComponent<Crowd>();
            crowd.walker = walker;
#if DEV
            Nearest = float.MaxValue; Moved = 0f; Gave = 0;
#endif
            return crowd;
        }

        void LateUpdate()
        {
            if (walker == null) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            if (Time.unscaledTime >= lookAgain)
            {
                // (the crew are looked for again now and then: some come and go)
                lookAgain = Time.unscaledTime + 3f;
                crew.Clear();
                crew.AddRange(FindObjectsOfType<global::SpaceCenterCrew>());
            }
            Vector3 w = walker.position, wGoing = first ? Vector3.zero : (w - walkerWas) / dt;
            wGoing.y = 0f;
            walkerWas = w;
            first = false;
            bool on = Foot.crew == null || Foot.crew.Value;
            foreach (global::SpaceCenterCrew one in crew)
            {
                if (one == null || !one.isActiveAndEnabled) continue;
                Transform t = one.transform;
                Vector3 c = t.position;
                Vector3 going = was.TryGetValue(one, out Vector3 before) ? (c - before) / dt : Vector3.zero;
                going.y = 0f;
                if (going.sqrMagnitude > 25f) going = Vector3.zero;                  // (put somewhere else by the game: not walking)
#if DEV
                GoingNow[one] = going;
#endif
                Vector3 r = w - c; r.y = 0f;
#if DEV
                Nearest = Mathf.Min(Nearest, r.magnitude);
#endif
                if (on && r.sqrMagnitude < 36f)
                {
                    Vector3 moved = going.sqrMagnitude > 0.04f ? Passing(one, c, going, r, wGoing, dt) : Standing(one, c, r, wGoing, dt);
                    if (moved.sqrMagnitude > 0f)
                    {
                        t.position = c + moved;
                        c += moved;
#if DEV
                        Moved += moved.magnitude;
#endif
                    }
                }
                else stoodAt.Remove(one);
                was[one] = c;
            }
        }

        /// <summary>One walking: aside (and held back a little) if it would pass nearer than a metre within the next few seconds.</summary>
        Vector3 Passing(global::SpaceCenterCrew one, Vector3 c, Vector3 going, Vector3 r, Vector3 wGoing, float dt)
        {
            stoodAt.Remove(one);
            Vector3 rel = wGoing - going;                                           // how the walker comes on, as the crewman sees it
            float speed2 = rel.sqrMagnitude;
            float when = speed2 > 1e-4f ? Mathf.Max(0f, -Vector3.Dot(r, rel) / speed2) : 0f;
            // (within the next few seconds, or within a few metres: at a walking pace three seconds is barely a metre, and people
            // begin to give way two to four metres off whatever their speed)
            if (when > Ahead && r.sqrMagnitude > Near * Near) return Vector3.zero;
            Vector3 nearest = r + rel * when;                                       // where the walker would be from it, nearest
            float near = nearest.magnitude;
            if (near >= Clear) return Vector3.zero;
            Vector3 way = going.normalized, right = Vector3.Cross(Vector3.up, way);
            // (away from the side the walker would pass on: across its way; straight ahead, to its right)
            float onRight = Vector3.Dot(nearest, right);
            Vector3 aside = Mathf.Abs(onRight) > 0.05f ? right * -Mathf.Sign(onRight) : right;
            float need = (Clear - near) / Mathf.Max(when, 0.4f);
            Vector3 moved = aside * (Mathf.Min(need, Side) * dt);
            // (the walker standing right in its way, close: it slows, to let the step aside do its work)
            float ahead = Vector3.Dot(r, way);
            if (ahead > 0f && ahead < 1.5f && Mathf.Abs(onRight) < 0.6f && wGoing.sqrMagnitude < 0.25f)
                moved -= way * (going.magnitude * dt * Mathf.Clamp01(1.6f - ahead));
#if DEV
            Gave++;
#endif
            return moved;
        }

        /// <summary>One standing about: a step back from a walker coming within a metre and a quarter, a metre at most from where it stood.</summary>
        Vector3 Standing(global::SpaceCenterCrew one, Vector3 c, Vector3 r, Vector3 wGoing, float dt)
        {
            float d = r.magnitude;
            if (d >= Room || d < 1e-3f) return Vector3.zero;
            if (!stoodAt.TryGetValue(one, out Vector3 home)) { home = c; stoodAt[one] = home; }
            Vector3 away = -r / d;
            // (out of the walker's way: back, and to the side the walker is passing it on, if the walker is going by)
            if (wGoing.sqrMagnitude > 0.04f)
            {
                Vector3 across = Vector3.Cross(Vector3.up, wGoing.normalized);
                away = (away + across * Mathf.Sign(Vector3.Dot(-r, across)) * 0.6f).normalized;
            }
            Vector3 moved = away * (Back * dt * (Room - d) / Room * 2f);
            Vector3 from = c + moved - home; from.y = 0f;
            if (from.magnitude > MostBack) return Vector3.zero;
#if DEV
            Gave++;
#endif
            return moved;
        }
    }
}
