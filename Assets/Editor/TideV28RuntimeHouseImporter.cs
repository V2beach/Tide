using UnityEditor;
using UnityEngine;

/// <summary>
/// Keeps the lightweight V28/V27 runtime copies on the same registration contract
/// as their 4096px production sources.  This is path-scoped and cannot alter the
/// production art packs maintained by the resource session.
/// </summary>
public sealed class TideV28RuntimeHouseImporter : AssetPostprocessor
{
    private const string RuntimeHouseFolder =
        "Assets/Resources/StiltFirstSliceAI/V28House/";

    private void OnPreprocessTexture()
    {
        if (!assetPath.StartsWith(RuntimeHouseFolder, System.StringComparison.Ordinal))
        {
            return;
        }

        TextureImporter importer = (TextureImporter)assetImporter;
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePivot = new Vector2(0.5f, 0.03125f);
        importer.spritePixelsPerUnit = 256f;
        importer.mipmapEnabled = false;
        importer.alphaIsTransparency = true;
        importer.filterMode = FilterMode.Bilinear;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.maxTextureSize = 2048;
        importer.textureCompression = TextureImporterCompression.CompressedHQ;

        // Unity 2022 keeps alignment and mesh mode in TextureImporterSettings
        // instead of exposing them directly on TextureImporter.
        TextureImporterSettings settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.spriteAlignment = (int)SpriteAlignment.Custom;
        settings.spritePivot = new Vector2(0.5f, 0.03125f);
        settings.spriteMeshType = SpriteMeshType.FullRect;
        importer.SetTextureSettings(settings);
    }
}
