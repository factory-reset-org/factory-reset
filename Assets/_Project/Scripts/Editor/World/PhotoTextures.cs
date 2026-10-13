using UnityEditor;
using UnityEngine;
using static ToyFactory.Editor.World.DressingKit;

namespace ToyFactory.Editor.World
{
    /// <summary>
    /// Photographic surface textures: CC0 sets from ambientCG, packed for URP Lit into
    /// <see cref="Folder"/> (a colour map, the OpenGL normal map, and a mask with R metallic,
    /// G ambient occlusion, A smoothness from the roughness map). Surfaces that take a palette
    /// colour (plastic, metal, walls, floor, ceiling) have a neutral grey-scale colour map that
    /// the material's own colour tints; wood and cardboard keep their photographed colour.
    /// Meshes from <see cref="DressingKit"/> are UV-mapped in metres, so each set repeats at its
    /// real size.
    /// </summary>
    /// <remarks>
    /// Sources (all CC0, ambientCG.com, 1K): Plastic010 (plastic), Metal032 (brushed metal),
    /// Concrete046 (walls, 2.4 m), Tiles107 (floor; 8 x 8 tiles to the metre, scaled up to the
    /// room's 1 m tiles), OfficeCeiling001 (ceiling, 3.85 m), Wood049 (wood, 0.8 m),
    /// Cardboard004 (cardboard), SheetMetal002 (perforated steel, its zinc pattern toned down and
    /// the holes dark, 0.4 m). The photographed smoothness drives the surface, so materials
    /// using a set have their own smoothness set to 1.
    /// </remarks>
    public static class PhotoTextures
    {
        public const string Folder = TextureFolder + "/Photo";

        public enum Kind { Plastic, Metal, WallPanel, FloorTile, Ceiling, Wood, Cardboard, Perforated }

        // Metres one repeat covers, normal strength, occlusion strength.
        static (float tile, float bump, float occlusion) Settings(Kind kind)
        {
            switch (kind)
            {
                case Kind.Plastic: return (1f, 0.6f, 0f);
                case Kind.Metal: return (1f, 0.6f, 0f);
                case Kind.WallPanel: return (2.4f, 0.8f, 0f);
                case Kind.FloorTile: return (8f, 0.7f, 0.7f);       // 8 x 8 tiles in a repeat: 1 m tiles
                case Kind.Ceiling: return (3.85f, 0.8f, 0.7f);
                case Kind.Wood: return (0.8f, 0.7f, 0f);
                case Kind.Perforated: return (0.4f, 0.9f, 0.9f);   // 16 holes a repeat, 2.5 cm apart
                default: return (0.6f, 0.6f, 0f);                   // cardboard
            }
        }

        /// <summary>
        /// Points <paramref name="material"/> (URP Lit) at <paramref name="kind"/>'s photo textures;
        /// <paramref name="tiling"/> overrides the repeat (default: the set's real size on a
        /// metre-mapped mesh).
        /// </summary>
        public static void Apply(Material material, Kind kind, Vector2? tiling = null)
        {
            var (tile, bump, occlusion) = Settings(kind);
            Texture2D albedo = Load(kind, "A", TextureImporterType.Default, true);
            Texture2D normal = Load(kind, "N", TextureImporterType.NormalMap, false);
            Texture2D mask = Load(kind, "M", TextureImporterType.Default, false);
            material.SetTexture("_BaseMap", albedo);
            material.SetTextureScale("_BaseMap", tiling ?? Vector2.one / tile);
            material.SetTexture("_BumpMap", normal);
            material.SetFloat("_BumpScale", bump);
            material.EnableKeyword("_NORMALMAP");
            material.SetTexture("_MetallicGlossMap", mask);
            material.EnableKeyword("_METALLICSPECGLOSSMAP");
            material.SetFloat("_Smoothness", 1f);
            material.SetTexture("_OcclusionMap", occlusion > 0f ? mask : null);
            material.SetFloat("_OcclusionStrength", occlusion);
            if (occlusion > 0f)
                material.EnableKeyword("_OCCLUSIONMAP");
            else
                material.DisableKeyword("_OCCLUSIONMAP");
            EditorUtility.SetDirty(material);
        }

        /// <summary>
        /// Gives <paramref name="material"/> only <paramref name="kind"/>'s photographed relief (its
        /// normal map), keeping its own colour map: for painted pictures, such as sign atlases, that
        /// should catch the light like the surface they are painted on. The relief follows the
        /// material's own tiling.
        /// </summary>
        public static void ApplyRelief(Material material, Kind kind, float strength)
        {
            material.SetTexture("_BumpMap", Load(kind, "N", TextureImporterType.NormalMap, false));
            material.SetFloat("_BumpScale", strength);
            material.EnableKeyword("_NORMALMAP");
            EditorUtility.SetDirty(material);
        }

        /// <summary>
        /// Gives every palette plastic the plastic set, and the grey and the chrome the brushed-metal
        /// set (so bolts, hubs, rails and taps are brushed steel rather than a mirror).
        /// Called by <see cref="RoomPieces.Load"/>, so every room builder keeps them.
        /// </summary>
        public static void ApplyPalette()
        {
            foreach (Material plastic in new[] { RoomPieces.M.White, RoomPieces.M.Blue, RoomPieces.M.Red, RoomPieces.M.Yellow,
                         RoomPieces.M.Purple, RoomPieces.M.Mint, RoomPieces.M.Orange, RoomPieces.M.Bubble, RoomPieces.M.Plum })
                if (plastic != null)
                    Apply(plastic, Kind.Plastic);
            foreach (Material metal in new[] { RoomPieces.M.Grey, RoomPieces.M.Chrome })
                if (metal != null)
                    Apply(metal, Kind.Metal);
        }

        // Loads one packed map, making sure it imports right (sRGB colour, normal map, linear mask with alpha).
        static Texture2D Load(Kind kind, string map, TextureImporterType type, bool sRGB)
        {
            string path = $"{Folder}/T_Photo_{kind}_{map}.png";
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            if (importer == null)
                throw new System.InvalidOperationException($"Missing photo texture {path}.");
            bool mask = map == "M";
            if (importer.textureType != type || importer.sRGBTexture != sRGB || importer.wrapMode != TextureWrapMode.Repeat ||
                importer.alphaSource != (mask ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None))
            {
                importer.textureType = type;
                importer.sRGBTexture = sRGB;
                importer.mipmapEnabled = true;
                importer.filterMode = FilterMode.Trilinear;
                importer.anisoLevel = 8;
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.alphaSource = mask ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
                importer.maxTextureSize = 1024;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
    }
}
