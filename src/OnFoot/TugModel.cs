using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Keystone;
using UnityEngine;
using UnityEngine.Rendering;

namespace OnFoot
{
    /// <summary>
    /// The crew car's body as Model/tug.py makes it in Blender: its meshes (tug.mesh) and its pictures (PNG files), read from
    /// the mod's PluginData folder the first time a car is dressed, and drawn with the game's own part shaders (so there is
    /// nothing of ours to build for each system).
    ///
    /// The mesh file: "ONFOOTM1", a count of meshes, and for each its name, its vertices (position, normal, tangent, uv: 12
    /// floats each, in Unity's axes and metres) and its submeshes (a material's name and its triangles' indices).
    /// </summary>
    public static class TugModel
    {
        public sealed class Piece
        {
            public string Name;
            public Mesh Mesh;
            /// <summary>Where the piece's own frame is in its parent's (the steering wheel's: on its column, z along it).</summary>
            public Vector3 Position;
            public Quaternion Rotation = Quaternion.identity;
            public Material[] Materials;
            /// <summary>Which submesh draws each kind of lamp ("headlamp", "taillamp", "beacon"), or -1.</summary>
            public readonly Dictionary<string, int> Lamps = new Dictionary<string, int>();
            public int Triangles;
        }

        static Dictionary<string, Piece> pieces;
        static string failed;

        public static string Folder => Path.Combine(KSPUtil.ApplicationRootPath, "GameData/OnFoot/PluginData");

        /// <summary>Whether the body can be drawn (read on the first call); if not, why not.</summary>
        public static bool Ready(out string why)
        {
            why = failed;
            // (kept from one scene to the next; read again should the game ever have thrown them away)
            if (pieces != null && Alive()) return true;
            pieces = null;
            if (failed != null) return false;
            try
            {
                Load();
                return true;
            }
            catch (Exception ex)
            {
                failed = why = "the crew car's body could not be read: " + ex.Message;
                Kit.Log(Foot.Name, failed + "\n" + ex);
                pieces = null;
                return false;
            }
        }

        /// <summary>Whether every mesh and material read is still there.</summary>
        static bool Alive()
        {
            foreach (Piece p in pieces.Values)
            {
                if (p.Mesh == null) return false;
                foreach (Material m in p.Materials) if (m == null || m.mainTexture == null) return false;
            }
            return true;
        }

        public static Piece Get(string name) => pieces != null && pieces.TryGetValue(name, out Piece p) ? p : null;

        static void Load()
        {
            float started = Time.realtimeSinceStartup;
            Texture2D paint = Picture("tug_colour.png", false, TextureWrapMode.Clamp);
            Texture2D tyre = Picture("tyre_colour.png", false, TextureWrapMode.Clamp);
            Texture2D tyreBumps = Picture("tyre_normal.png", true, TextureWrapMode.Clamp);
            Texture2D tread = Picture("tread_colour.png", false, TextureWrapMode.Repeat);
            Texture2D treadBumps = Picture("tread_normal.png", true, TextureWrapMode.Repeat);
            var white = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            var px = new Color32[16];
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(255, 255, 255, 255);
            white.SetPixels32(px);
            white.Apply(false, true);
            white.hideFlags = HideFlags.DontUnloadUnusedAsset;

            // the materials, on the shaders the game's own parts are drawn with
            var mats = new Dictionary<string, Material>
            {
                ["paint"] = Make("KSP/Specular", paint, null, new Color(0.32f, 0.32f, 0.32f), 0.22f),
                ["glass"] = Make("KSP/Specular", paint, null, new Color(0.85f, 0.85f, 0.85f), 0.85f),
                ["headlamp"] = Lamp(paint, white),
                ["taillamp"] = Lamp(paint, white),
                ["beacon"] = Lamp(paint, white),
                ["tyre"] = Make("KSP/Bumped Specular", tyre, tyreBumps, new Color(0.16f, 0.16f, 0.16f), 0.1f),
                ["deck"] = Make("KSP/Bumped Specular", tread, treadBumps, new Color(0.6f, 0.6f, 0.62f), 0.35f),
            };

            byte[] data = File.ReadAllBytes(Path.Combine(Folder, "tug.mesh"));
            var found = new Dictionary<string, Piece>();
            using (var r = new BinaryReader(new MemoryStream(data)))
            {
                if (Encoding.ASCII.GetString(r.ReadBytes(8)) != "ONFOOTM2") throw new InvalidDataException("tug.mesh is not a mesh file of this On Foot's (remake it with Model/tug.py)");
                uint count = r.ReadUInt32();
                for (int m = 0; m < count; m++)
                {
                    var piece = new Piece { Name = Text(r) };
                    piece.Position = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                    piece.Rotation = new Quaternion(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                    int n = checked((int)r.ReadUInt32());
                    var pos = new Vector3[n]; var nor = new Vector3[n]; var tan = new Vector4[n]; var uv = new Vector2[n];
                    for (int i = 0; i < n; i++)
                    {
                        pos[i] = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                        nor[i] = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                        tan[i] = new Vector4(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                        uv[i] = new Vector2(r.ReadSingle(), r.ReadSingle());
                    }
                    var mesh = piece.Mesh = new Mesh { name = "OnFoot tug " + piece.Name };
                    if (n > 65000) mesh.indexFormat = IndexFormat.UInt32;
                    mesh.vertices = pos; mesh.normals = nor; mesh.tangents = tan; mesh.uv = uv;
                    int subs = checked((int)r.ReadUInt32());
                    mesh.subMeshCount = subs;
                    piece.Materials = new Material[subs];
                    for (int s = 0; s < subs; s++)
                    {
                        string mat = Text(r);
                        int k = checked((int)r.ReadUInt32());
                        var idx = new int[k];
                        for (int i = 0; i < k; i++)
                        {
                            idx[i] = checked((int)r.ReadUInt32());
                            if (idx[i] >= n) throw new InvalidDataException("tug.mesh: an index past the end in " + piece.Name);
                        }
                        mesh.SetTriangles(idx, s, false);
                        piece.Triangles += k / 3;
                        if (!mats.TryGetValue(mat, out Material material)) material = mats["paint"];
                        piece.Materials[s] = material;
                        if (mat == "headlamp" || mat == "taillamp" || mat == "beacon") piece.Lamps[mat] = s;
                    }
                    mesh.RecalculateBounds();
                    mesh.UploadMeshData(true);
                    mesh.hideFlags = HideFlags.DontUnloadUnusedAsset;
                    found[piece.Name] = piece;
                }
            }
            foreach (string needed in new[] { "body", "interior", "steering", "tyre", "deck" })
                if (!found.ContainsKey(needed)) throw new InvalidDataException("tug.mesh has no " + needed);
            pieces = found;
            int tris = 0;
            foreach (Piece p in found.Values) tris += p.Triangles * (p.Name == "tyre" ? 4 : 1);
            Kit.Log(Foot.Name, "the crew car's body read in " + ((Time.realtimeSinceStartup - started) * 1000f).ToString("F0") + " ms: " + tris + " triangles drawn for a car, pictures "
                + paint.width + ", " + tyre.width + " and " + tread.width + " across");
        }

        static string Text(BinaryReader r) => Encoding.UTF8.GetString(r.ReadBytes(r.ReadUInt16()));

        /// <summary>
        /// A picture from the PluginData folder, with its smaller sizes made and squeezed as the game's own are. A normal map
        /// (`bumps`) is laid out as the game's shaders read one: its x in alpha, its y in green, red and blue full (which both
        /// ways of reading one, x from alpha alone or red times alpha, take right).
        /// </summary>
        static Texture2D Picture(string file, bool bumps, TextureWrapMode wrap)
        {
            string path = Path.Combine(Folder, file);
            var raw = new Texture2D(4, 4, TextureFormat.RGBA32, false, bumps);
            if (!raw.LoadImage(File.ReadAllBytes(path))) throw new InvalidDataException("could not read " + file);
            Color32[] px = raw.GetPixels32();
            if (bumps)
                for (int i = 0; i < px.Length; i++) px[i] = new Color32(255, px[i].g, 255, px[i].r);
            var tex = new Texture2D(raw.width, raw.height, TextureFormat.RGBA32, true, bumps) { name = "OnFoot " + file, wrapMode = wrap, filterMode = FilterMode.Trilinear, anisoLevel = 4, hideFlags = HideFlags.DontUnloadUnusedAsset };
            UnityEngine.Object.Destroy(raw);
            tex.SetPixels32(px);
            tex.Apply(true, false);
            try { tex.Compress(true); } catch (Exception ex) { Kit.Log(Foot.Name, "could not squeeze " + file + ": " + ex.Message); }
            tex.Apply(false, true);
            return tex;
        }

        static Material Make(string shader, Texture2D main, Texture2D bumps, Color spec, float shine)
        {
            Shader s = Shader.Find(shader);
            if (s == null) throw new InvalidDataException("the game has no shader " + shader);
            var m = new Material(s) { name = "OnFoot tug " + shader, hideFlags = HideFlags.DontUnloadUnusedAsset };
            m.SetTexture("_MainTex", main);
            if (bumps != null) m.SetTexture("_BumpMap", bumps);
            m.SetColor("_Color", Color.white);
            m.SetColor("_SpecColor", spec);
            m.SetFloat("_Shininess", shine);
            Plain(m);
            return m;
        }

        static Material Lamp(Texture2D main, Texture2D white)
        {
            Shader s = Shader.Find("KSP/Emissive/Specular");
            if (s == null) throw new InvalidDataException("the game has no shader KSP/Emissive/Specular");
            var m = new Material(s) { name = "OnFoot tug lamp", hideFlags = HideFlags.DontUnloadUnusedAsset };
            m.SetTexture("_MainTex", main);
            m.SetTexture("_Emissive", white);
            m.SetColor("_EmissiveColor", Color.black);
            m.SetColor("_Color", Color.white);
            m.SetColor("_SpecColor", new Color(0.8f, 0.8f, 0.8f));
            m.SetFloat("_Shininess", 0.8f);
            Plain(m);
            return m;
        }

        /// <summary>The game's part shaders' own extras (heat, burning, the rim light, fading) set to nothing.</summary>
        static void Plain(Material m)
        {
            if (m.HasProperty("_Opacity")) m.SetFloat("_Opacity", 1f);
            if (m.HasProperty("_RimFalloff")) m.SetFloat("_RimFalloff", 2f);
            if (m.HasProperty("_RimColor")) m.SetColor("_RimColor", Color.black);
            if (m.HasProperty("_TemperatureColor")) m.SetColor("_TemperatureColor", Color.clear);
            if (m.HasProperty("_BurnColor")) m.SetColor("_BurnColor", Color.white);
        }
    }
}
