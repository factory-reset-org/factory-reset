using System.Linq;
using UnityEditor;
using UnityEditor.Presets;
using UnityEngine;

namespace ToyFactory.Editor
{
    /// <summary>
    /// Puts S3's shared texture import preset (<c>Textures/S3_Texture_BC7.preset</c>) on the character and prop
    /// textures, from <b>Factory Reset / Textures / Apply S3 Texture Preset</b>. The preset holds what every one of
    /// them shares (mipmaps, sRGB, not readable, no non-power-of-two scaling, at most 1,024 pixels, BC7 on Standalone
    /// at the top quality); filter mode, anisotropy, wrap mode and alpha handling are excluded from it, so a pixel-art
    /// crate stays point-filtered while the decal sheet stays trilinear.
    /// </summary>
    public static class TexturePresetMenu
    {
        /// <summary>The preset asset.</summary>
        public const string PresetPath = "Assets/_Project/Textures/S3_Texture_BC7.preset";

        /// <summary>The folders whose textures are S3's. Environment and FX textures belong to other students.</summary>
        public static readonly string[] Folders =
        {
            "Assets/_Project/Textures/Characters",
            "Assets/_Project/Textures/Props"
        };

        /// <summary>Applies the preset to every texture in <see cref="Folders"/> and reimports it. Returns how many textures it was applied to.</summary>
        [MenuItem("Factory Reset/Textures/Apply S3 Texture Preset")]
        public static int Apply()
        {
            var preset = AssetDatabase.LoadAssetAtPath<Preset>(PresetPath);
            if (preset == null)
            {
                Debug.LogError("The texture preset is missing: " + PresetPath);
                return 0;
            }

            int applied = 0;
            string[] guids = AssetDatabase.FindAssets("t:Texture2D", Folders);
            foreach (string path in guids.Select(AssetDatabase.GUIDToAssetPath).Distinct())
            {
                if (!(AssetImporter.GetAtPath(path) is TextureImporter importer))
                    continue;

                // Applying is idempotent: a texture that already matches comes out the same.
                preset.ApplyTo(importer);
                importer.SaveAndReimport();
                applied++;
            }

            Debug.Log($"S3 texture preset applied to {applied} textures.");
            return applied;
        }
    }
}
