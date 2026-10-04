using UnityEngine;

namespace DinoHunt.View
{
    /// <summary>
    /// Resolution-independent IMGUI. Overlays lay out in a virtual canvas ~720 units tall and this
    /// scales it to the real backbuffer, so panels and text keep the same on-screen size on a 1x
    /// monitor and on a 2x (retina / high-DPI browser) canvas instead of shrinking by half.
    /// </summary>
    public static class GuiScale
    {
        private const float ReferenceHeight = 720f;

        public static float Factor => Mathf.Max(1f, Screen.height / ReferenceHeight);

        /// <summary>Virtual canvas size to lay out against after <see cref="Apply"/>.</summary>
        public static float Width => Screen.width / Factor;
        public static float Height => Screen.height / Factor;

        /// <summary>Call at the top of OnGUI.</summary>
        public static void Apply()
        {
            float f = Factor;
            GUI.matrix = Matrix4x4.Scale(new Vector3(f, f, 1f));
        }

        /// <summary>Camera.WorldToScreenPoint result -> virtual GUI coordinates (top-left origin).</summary>
        public static Vector2 FromScreen(Vector3 screen)
        {
            float f = Factor;
            return new Vector2(screen.x / f, (Screen.height - screen.y) / f);
        }
    }
}
