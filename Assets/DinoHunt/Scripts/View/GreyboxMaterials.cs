using System.Collections.Generic;
using UnityEngine;

namespace DinoHunt.View
{
    /// <summary>
    /// Creates flat, unlit URP materials for the greybox. Pure presentation — nothing in
    /// the simulation depends on this. Materials are cached per color so we don't allocate
    /// one per primitive.
    /// </summary>
    public static class GreyboxMaterials
    {
        private static readonly Dictionary<Color, Material> Cache = new Dictionary<Color, Material>();
        private static Shader _unlit;

        // Shared greybox palette — flat/unlit but cohesive.
        public static readonly Color Ground = new Color(0.42f, 0.42f, 0.42f);
        public static readonly Color BlueBase = new Color(0.18f, 0.40f, 0.72f);
        public static readonly Color RedBase = new Color(0.74f, 0.24f, 0.26f);
        public static readonly Color Nest = new Color(0.92f, 0.70f, 0.26f);
        public static readonly Color Cover = new Color(0.44f, 0.46f, 0.50f);
        public static readonly Color BlueAgent = new Color(0.38f, 0.66f, 1f);
        public static readonly Color RedAgent = new Color(1f, 0.46f, 0.42f);
        public static readonly Color Egg = new Color(0.96f, 0.93f, 0.80f);
        public static readonly Color Gun = new Color(0.09f, 0.09f, 0.11f);
        public static readonly Color Bullet = new Color(1f, 0.92f, 0.40f);
        public static readonly Color Raptor = new Color(0.30f, 0.36f, 0.16f);

        // A few warm concrete tones for the scattered structures, for a bit of variety.
        public static readonly Color[] Buildings =
        {
            new Color(0.40f, 0.37f, 0.33f),
            new Color(0.34f, 0.32f, 0.30f),
            new Color(0.45f, 0.40f, 0.34f),
            new Color(0.30f, 0.29f, 0.28f),
        };

        public static Material Get(Color color)
        {
            if (Cache.TryGetValue(color, out var mat) && mat != null)
                return mat;

            if (_unlit == null)
                _unlit = Shader.Find("Universal Render Pipeline/Unlit");

            // Fall back to the built-in unlit shader if URP isn't resolvable for any reason.
            mat = new Material(_unlit != null ? _unlit : Shader.Find("Unlit/Color"));
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
            mat.color = color;

            Cache[color] = mat;
            return mat;
        }
    }
}
