using System.Collections.Generic;
using Keystone;
using UnityEngine;

namespace OnFoot
{
    /// <summary>
    /// Somewhere to walk: what a walk needs of the scene it is in. It takes the game's camera over while the walk
    /// lasts and gives it back exactly afterwards; it makes the place solid underfoot (Solid); it says where to
    /// set off from; and it says when the scene has changed under it, so that the walk ends.
    /// </summary>
    public abstract class Place
    {
        /// <summary>Which way is up here (the walker stands along it).</summary>
        public virtual Vector3 Up => Vector3.up;

        /// <summary>How strongly things fall here, in m/s².</summary>
        public virtual float Gravity => 9.81f;

        /// <summary>Where to set off from, and which way to face (the view's heading, in degrees from the world's z towards x).</summary>
        public abstract bool Start(out Vector3 feet, out float heading, out float pitch);

        /// <summary>Take the game's camera over, lock its controls, and make the place solid.</summary>
        public abstract void Begin();

        /// <summary>
        /// The view, each frame, once everything else has moved: put the game's camera there. `walking` is how far into the walk the
        /// view is, from 0 (framed as the game frames it) to 1 (framed for walking), for what is eased in and out with the view.
        /// </summary>
        public abstract void Look(Vector3 eye, Quaternion facing, float fieldOfView, float walking);

        /// <summary>Where the game's camera was when the walk began (for easing into the walk and back out of it).</summary>
        public abstract void Was(out Vector3 eye, out Quaternion facing, out float fieldOfView);

        /// <summary>Everything as it was: the camera, its controls, the pointer, the interface; and whatever was made for the walk is gone.</summary>
        public abstract void End();

        /// <summary>False once the scene is no longer the one the walk began in (the editor switched building, a craft launched...).</summary>
        public abstract bool Still { get; }

        /// <summary>The layers the walker's body is stopped by here.</summary>
        public abstract int Solid { get; }

        /// <summary>A line for the window about the place.</summary>
        public abstract string About { get; }

        /// <summary>The place to walk in the scene the game is in, or nothing (and, if it is somewhere that cannot be walked just now, why not).</summary>
        public static Place Here(out string why)
        {
            why = null;
            if (HighLogic.LoadedSceneIsEditor) return EditorPlace.Make(out why);
            return null;
        }

        /// <summary>For the window: a few words on the place there is to walk in this scene, or nothing if there is none.</summary>
        public static string Describe()
        {
            if (HighLogic.LoadedSceneIsEditor) return EditorDriver.editorFacility == EditorFacility.SPH ? "In the hangar." : "In the assembly building.";
            return null;
        }

        // ---- shared by places

        /// <summary>The pointer, as it was before a walk took it.</summary>
        protected CursorLockMode cursorWas;
        protected bool cursorShownWas;
        /// <summary>Whether the game's interface was showing (a walk puts it away, as F2 does, and brings it back after).</summary>
        protected bool interfaceWas;
        protected bool interfaceHidden;
        protected const string LockName = "OnFoot";

        protected void TakePointer()
        {
            cursorWas = Cursor.lockState;
            cursorShownWas = Cursor.visible;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        protected void GivePointer()
        {
            Cursor.lockState = cursorWas;
            Cursor.visible = cursorShownWas;
        }

        /// <summary>
        /// The pointer let go for a while during the walk (a window to click in has opened over it), or taken again. Looking about
        /// stops meanwhile.
        /// </summary>
        public void Pointer(bool held)
        {
            Cursor.lockState = held ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !held;
        }

        protected void HideInterface()
        {
            interfaceWas = KSP.UI.UIMasterController.Instance != null && KSP.UI.UIMasterController.Instance.IsUIShowing;
            if (!interfaceWas) return;
            interfaceHidden = true;
            try { GameEvents.onHideUI.Fire(); } catch (System.Exception ex) { Kit.Log(Foot.Name, "hiding the interface: " + ex.Message); }
        }

        protected void ShowInterface()
        {
            if (!interfaceHidden) return;
            interfaceHidden = false;
            // (only if it is still hidden: the player may have brought it back with F2 meanwhile)
            if (KSP.UI.UIMasterController.Instance != null && KSP.UI.UIMasterController.Instance.IsUIShowing) return;
            try { GameEvents.onShowUI.Fire(); } catch (System.Exception ex) { Kit.Log(Foot.Name, "showing the interface: " + ex.Message); }
        }

        /// <summary>A camera's settings as they were, to be put back.</summary>
        protected struct Kept
        {
            public Camera Cam;
            public float Near, Far, Fov;
        }

        protected static List<Kept> Keep(IEnumerable<Camera> cams)
        {
            var kept = new List<Kept>();
            foreach (Camera c in cams) if (c != null) kept.Add(new Kept { Cam = c, Near = c.nearClipPlane, Far = c.farClipPlane, Fov = c.fieldOfView });
            return kept;
        }

        protected static void PutBack(List<Kept> kept)
        {
            if (kept == null) return;
            foreach (Kept k in kept)
            {
                if (k.Cam == null) continue;
                k.Cam.nearClipPlane = k.Near; k.Cam.farClipPlane = k.Far; k.Cam.fieldOfView = k.Fov;
            }
        }
    }
}
