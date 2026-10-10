using System.Collections.Generic;
using Keystone;
using UnityEngine;

namespace OnFoot
{
    /// <summary>
    /// Walls for a building whose own walls cannot be made solid from their shape. (The assembly building's shell is drawn from
    /// one mesh the game combined with others when it was built, and the game keeps no copy of a combined mesh that can be read:
    /// so no collider can be made of the same shape.)
    ///
    /// The walls are found by looking at them instead. From the middle of the room a camera looks at each of the four sides in
    /// turn, square on (no perspective), seeing only the shell, through a band from just over the floor to a little over a
    /// kerbal's head; it is drawn with Unity's own shader for the distance of every point seen (the one Unity itself uses for a
    /// camera's depth). Each column of that picture then says how far the wall is in that direction, or that there is nothing
    /// there: a doorway. A slab is laid where the wall is, and nothing where the door is.
    /// </summary>
    public static class Shell
    {
        const float Band = 2.8f, Above = 0.25f, Cell = 0.25f, Thick = 0.6f;
        static readonly Vector3[] Sides = { Vector3.right, Vector3.left, Vector3.forward, Vector3.back };

        static Shader depth;

        /// <summary>One side as seen: how far the wall is in each column, from the middle (MaxValue: nothing there).</summary>
        sealed class Seen
        {
            public Vector3 Side, Across;
            public float Wide;
            public float[] Far;
        }

        /// <summary>Walls for the shell (the renderers given, whose bounds together are the room), under `parent`. Says how it went.</summary>
        public static string Walls(List<Renderer> shell, float floor, Transform parent, int layer, List<GameObject> made)
        {
            if (shell.Count == 0) return "";
            Bounds room = shell[0].bounds;
            foreach (Renderer r in shell) room.Encapsulate(r.bounds);
            if (depth == null) depth = Shader.Find("Hidden/Internal-DepthNormalsTexture");
            Vector3 middle = new Vector3(room.center.x, floor + Above + 0.5f * Band, room.center.z);

            // (measured with the depth shader where it can be; if that shows next to nothing, which it would where the shader cannot
            // draw, looked at again with the shell's own shaders, which show where the wall is but not how far: it is then put at the
            // side of the shell's outline)
            string how = "measured";
            List<Seen> seen = null;
            if (depth != null && depth.isSupported)
            {
                seen = Scan(shell, room, middle, true);
                if (Share(seen) < 0.1f) seen = null;
            }
            if (seen == null)
            {
                seen = Scan(shell, room, middle, false);
                how = "set at the shell's sides (" + (depth == null ? "Unity's depth shader is not in this game" : "Unity's depth shader drew nothing") + ")";
            }

            // ---- runs of columns with a wall at much the same distance: a slab each
            int segments = 0, open = 0;
            float high = room.max.y - floor;
            foreach (Seen s in seen)
            {
                int columns = s.Far.Length, start = -1;
                for (int c = 0; c <= columns; c++)
                {
                    bool wall = c < columns && s.Far[c] < float.MaxValue;
                    if (wall && start >= 0 && Mathf.Abs(s.Far[c] - s.Far[start]) > 0.75f) { Slab(s, start, c - 1); start = -1; }
                    if (wall && start < 0) start = c;
                    if (!wall && start >= 0) { Slab(s, start, c - 1); start = -1; }
                    if (!wall && c < columns) open++;
                }
            }

            void Slab(Seen s, int from, int to)
            {
                float sum = 0f; int n = 0;
                for (int k = from; k <= to; k++) { sum += s.Far[k]; n++; }
                float at = sum / n, columns = s.Far.Length;
                float left = -0.5f * s.Wide + from * s.Wide / columns, right = -0.5f * s.Wide + (to + 1) * s.Wide / columns;
                var slab = new GameObject("On Foot: wall") { layer = layer };
                slab.transform.SetParent(parent, false);
                slab.transform.position = new Vector3(middle.x, 0f, middle.z) + s.Side * (at + 0.5f * Thick) + s.Across * (0.5f * (left + right));
                slab.transform.rotation = Quaternion.LookRotation(s.Side, Vector3.up);
                var box = slab.AddComponent<BoxCollider>();
                box.size = new Vector3(right - left + 0.05f, high, Thick);
                box.center = new Vector3(0f, floor + 0.5f * high, 0f);
                made.Add(slab);
                segments++;
            }

            string said = "walls " + how + ": " + segments + " slabs, " + (open * Cell).ToString("F0") + " m of doorway";
            Kit.Log(Foot.Name, said);
            return said;
        }

        /// <summary>How much of what was looked at has a wall in it.</summary>
        static float Share(List<Seen> seen)
        {
            int all = 0, walls = 0;
            foreach (Seen s in seen) foreach (float f in s.Far) { all++; if (f < float.MaxValue) walls++; }
            return all > 0 ? walls / (float)all : 0f;
        }

        /// <summary>Look at the four sides from the middle, with the depth shader (how far) or without it (only where).</summary>
        static List<Seen> Scan(List<Renderer> shell, Bounds room, Vector3 middle, bool measure)
        {
            var seen = new List<Seen>();
            int layer = shell[0].gameObject.layer;
            // (only the shell is drawn while it is looked at: everything else of the building is hidden from the camera for the moment)
            var hidden = new List<Renderer>();
            var holder = new GameObject("On Foot: looking at the walls");
            Camera eye = holder.AddComponent<Camera>();
            eye.enabled = false;
            eye.orthographic = true; eye.allowHDR = false; eye.allowMSAA = false; eye.useOcclusionCulling = false;
            eye.clearFlags = CameraClearFlags.SolidColor;
            // (what the depth shader writes for "as far as can be"; without it, black, against which anything drawn shows)
            eye.backgroundColor = measure ? new Color(0f, 0f, 1f, 1f) : Color.black;
            eye.cullingMask = 1 << layer;
            eye.renderingPath = RenderingPath.Forward;
            try
            {
                foreach (Renderer r in Object.FindObjectsOfType<Renderer>())
                    if (r.gameObject.layer == layer && !shell.Contains(r) && !r.forceRenderingOff) { r.forceRenderingOff = true; hidden.Add(r); }
                foreach (Vector3 side in Sides)
                {
                    Vector3 across = Vector3.Cross(Vector3.up, side);                // (to the right, looking at the side)
                    float reach = Mathf.Abs(Vector3.Dot(room.extents, side)) + 3f;   // from the middle to just past the side
                    float wide = Mathf.Abs(Vector3.Dot(room.size, across)) + 2f;
                    int columns = Mathf.Clamp(Mathf.CeilToInt(wide / Cell), 8, 1024), rows = Mathf.Clamp(Mathf.CeilToInt(Band / Cell), 4, 64);
                    eye.transform.position = middle;
                    eye.transform.rotation = Quaternion.LookRotation(side, Vector3.up);
                    eye.orthographicSize = 0.5f * Band;
                    eye.nearClipPlane = 0.3f; eye.farClipPlane = reach;
                    var rt = RenderTexture.GetTemporary(columns, rows, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                    var read = new Texture2D(columns, rows, TextureFormat.RGBA32, false, true);
                    try
                    {
                        eye.targetTexture = rt;
                        eye.aspect = wide / Band;                                    // (after the target: the picture is as wide as the room, whatever its pixels)
                        if (measure) eye.RenderWithShader(depth, "");
                        else eye.Render();
                        RenderTexture was = RenderTexture.active;
                        RenderTexture.active = rt;
                        read.ReadPixels(new Rect(0, 0, columns, rows), 0, 0, false);
                        RenderTexture.active = was;
                        Color32[] px = read.GetPixels32();
                        // ---- each column: the nearest wall in it (metres from the middle), or nothing (a doorway)
                        var far = new float[columns];
                        for (int c = 0; c < columns; c++)
                        {
                            float nearest = float.MaxValue;
                            for (int r = 0; r < rows; r++)
                            {
                                Color32 p = px[r * columns + c];
                                float d;
                                if (measure)
                                {
                                    float d01 = p.b / 255f + p.a / 255f / 255f;          // (Unity's DecodeFloatRG of the depth it wrote: 0 at the eye, 1 as far as the camera sees)
                                    if (d01 >= 0.985f) continue;
                                    d = d01 * reach;
                                }
                                else
                                {
                                    if (p.r + p.g + p.b < 6) continue;
                                    d = reach - 3f;
                                }
                                if (d < nearest) nearest = d;
                            }
                            far[c] = nearest;
                        }
                        seen.Add(new Seen { Side = side, Across = across, Wide = wide, Far = far });
                    }
                    finally
                    {
                        eye.targetTexture = null;
                        RenderTexture.ReleaseTemporary(rt);
                        Object.Destroy(read);
                    }
                }
            }
            finally
            {
                foreach (Renderer r in hidden) if (r != null) r.forceRenderingOff = false;
                Object.Destroy(holder);
            }
            return seen;
        }
    }
}
