using System.Collections.Generic;
using UnityEngine;

namespace LightningForge.Arcade.Game
{
    /// <summary>
    /// Builds the handful of materials a game board needs at runtime.
    ///
    /// Chess reached the screen with authored material assets wired through the inspector.
    /// That does not scale to six games: every new board would mean another set of assets
    /// to create, name and hook up by hand before anything could be seen on screen. These
    /// are described in code instead, so adding a game is adding a script.
    ///
    /// Materials are cached by their settings, because a board asks for the same dark
    /// square sixty-four times and each Material is a separate allocation otherwise.
    /// </summary>
    public static class ArcadeMaterials
    {
        static readonly Dictionary<int, Material> Cache = new Dictionary<int, Material>();
        static Shader litShader;

        /// <summary>
        /// URP's Lit shader, which everything in the arcade uses.
        ///
        /// Shader.Find only sees shaders a build actually included. This one is safe
        /// because the authored chess materials reference it, which is what pulls it in;
        /// if the chess materials ever go, it needs adding to Always Included Shaders or
        /// every procedural board turns magenta in the player and is fine in the editor.
        /// </summary>
        static Shader Lit
        {
            get
            {
                if (litShader == null) litShader = Shader.Find("Universal Render Pipeline/Lit");
                if (litShader == null) litShader = Shader.Find("Standard");
                return litShader;
            }
        }

        public static Material Get(Color color, float smoothness = 0.25f, float metallic = 0f)
        {
            int key = color.GetHashCode();
            key = key * 397 ^ Mathf.RoundToInt(smoothness * 1000f);
            key = key * 397 ^ Mathf.RoundToInt(metallic * 1000f);

            if (Cache.TryGetValue(key, out Material cached) && cached != null) return cached;

            var material = new Material(Lit) { name = "Arcade_" + ColorUtility.ToHtmlStringRGB(color) };
            material.SetColor("_BaseColor", color);
            material.SetColor("_Color", color);
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_Glossiness", smoothness);
            material.SetFloat("_Metallic", metallic);

            Cache[key] = material;
            return material;
        }

        /// <summary>
        /// A material that glows. Used for selections and legal move hints, which have to
        /// read against a dark board without a light of their own.
        /// </summary>
        public static Material Emissive(Color color, float strength = 1.6f)
        {
            int key = color.GetHashCode() * 31 ^ Mathf.RoundToInt(strength * 1000f) ^ 0x5EED;
            if (Cache.TryGetValue(key, out Material cached) && cached != null) return cached;

            var material = new Material(Lit) { name = "ArcadeGlow_" + ColorUtility.ToHtmlStringRGB(color) };
            material.SetColor("_BaseColor", color);
            material.SetColor("_Color", color);
            material.SetFloat("_Smoothness", 0.4f);
            material.EnableKeyword("_EMISSION");
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            material.SetColor("_EmissionColor", color * strength);

            Cache[key] = material;
            return material;
        }
    }
}
