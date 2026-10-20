using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Presets;
using UnityEngine;

namespace ToyFactory.Tests.EditMode
{
    /// <summary>
    /// S3's textures follow the pipeline in CONTRIBUTING: power of two, mipmapped, BC7 on Standalone, all through the
    /// one shared preset instead of one-off settings. Read from the importers, so the result does not depend on the
    /// build target the editor happens to be on.
    /// </summary>
    public class TexturePresetTests
    {
        const string PresetPath = "Assets/_Project/Textures/S3_Texture_BC7.preset";

        static readonly string[] Folders =
        {
            "Assets/_Project/Textures/Characters",
            "Assets/_Project/Textures/Props"
        };

        static string[] TexturePaths() =>
            AssetDatabase.FindAssets("t:Texture2D", Folders).Select(AssetDatabase.GUIDToAssetPath).Distinct().OrderBy(p => p).ToArray();

        static TextureImporter ImporterAt(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            Assert.IsNotNull(importer, path + " is not imported as a texture.");
            return importer;
        }

        [Test]
        public void ThePresetExistsAndTargetsTextureImporters()
        {
            var preset = AssetDatabase.LoadAssetAtPath<Preset>(PresetPath);
            Assert.IsNotNull(preset, "The shared texture preset is missing.");
            Assert.AreEqual("TextureImporter", preset.GetTargetTypeName());
        }

        [Test]
        public void ThePresetLeavesFilterWrapAndAlphaToEachTexture()
        {
            var preset = AssetDatabase.LoadAssetAtPath<Preset>(PresetPath);
            string[] excluded = preset.excludedProperties;
            foreach (string property in new[] { "m_TextureSettings.m_FilterMode", "m_TextureSettings.m_Aniso", "m_TextureSettings.m_WrapU", "m_AlphaUsage" })
                CollectionAssert.Contains(excluded, property);
        }

        [Test]
        public void TheCharacterAndPropTexturesAreFoundAndEveryOneIsPowerOfTwo()
        {
            string[] paths = TexturePaths();
            Assert.GreaterOrEqual(paths.Length, 3, "Guard tread, Unit 047 decals and the crate texture.");
            foreach (string path in paths)
            {
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                Assert.IsTrue(Mathf.IsPowerOfTwo(texture.width) && Mathf.IsPowerOfTwo(texture.height),
                    $"{path} is {texture.width} x {texture.height}: not a power of two.");
            }
        }

        [Test]
        public void EveryTextureIsBc7OnStandaloneWithMipmapsAndIsNotReadable()
        {
            foreach (string path in TexturePaths())
            {
                TextureImporter importer = ImporterAt(path);
                TextureImporterPlatformSettings standalone = importer.GetPlatformTextureSettings("Standalone");

                Assert.IsTrue(standalone.overridden, path + ": no Standalone override.");
                Assert.AreEqual(TextureImporterFormat.BC7, standalone.format, path);
                Assert.LessOrEqual(standalone.maxTextureSize, 1024, path);
                Assert.IsTrue(importer.mipmapEnabled, path + ": no mipmaps.");
                Assert.IsFalse(importer.isReadable, path + ": readable textures keep a second copy in memory.");
                Assert.AreEqual(TextureImporterNPOTScale.None, importer.npotScale, path + ": non-power-of-two scaling would hide a bad size.");
            }
        }

        [Test]
        public void ApplyingThePresetChangesNothingThatIsAlreadyBc7AndKeepsEachTexturesFilter()
        {
            var preset = AssetDatabase.LoadAssetAtPath<Preset>(PresetPath);
            foreach (string path in TexturePaths())
            {
                TextureImporter importer = ImporterAt(path);
                FilterMode filter = importer.filterMode;
                int aniso = importer.anisoLevel;
                TextureWrapMode wrap = importer.wrapMode;

                // Applied to a copy of the settings in memory only: the importer is not saved or reimported.
                Assert.IsTrue(preset.ApplyTo(importer), path);
                Assert.AreEqual(filter, importer.filterMode, path + ": the preset must not touch the filter.");
                Assert.AreEqual(aniso, importer.anisoLevel, path + ": the preset must not touch anisotropy.");
                Assert.AreEqual(wrap, importer.wrapMode, path + ": the preset must not touch the wrap mode.");
                Assert.AreEqual(TextureImporterFormat.BC7, importer.GetPlatformTextureSettings("Standalone").format, path);
            }
        }
    }
}
