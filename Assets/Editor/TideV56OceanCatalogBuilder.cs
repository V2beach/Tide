using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 按 V56 运行契约导入 Balanced 连续海体。UHD/High 保留为源图和高分辨率验收档，
/// 运行时只允许一个档位，避免同一海面重复常驻。
/// </summary>
public static class TideV56OceanCatalogBuilder
{
    private const string SourcePath =
        "Assets/Art/GeneratedAI/ProductionContinuousOceanBaseV56/Runtime/Balanced/TideOceanV56_ContinuousBase.png";
    private const string CatalogFolder = "Assets/Resources/StiltFirstSliceAI";
    private const string CatalogPath = CatalogFolder + "/V56OceanCatalog.asset";
    private const float PixelsPerUnit = 100f;

    [MenuItem("Tide/Resources/Rebuild V56 Ocean Catalog")]
    public static void RebuildCatalog()
    {
        if (!File.Exists(SourcePath))
        {
            throw new FileNotFoundException("V56 Balanced 连续海体不存在", SourcePath);
        }

        AssetDatabase.ImportAsset(SourcePath, ImportAssetOptions.ForceSynchronousImport);
        TextureImporter importer = AssetImporter.GetAtPath(SourcePath) as TextureImporter;
        if (importer == null)
        {
            throw new InvalidDataException("V56 PNG 不是可配置的 TextureImporter：" + SourcePath);
        }

        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = PixelsPerUnit;
        importer.spritePivot = new Vector2(0.5f, 0.5f);
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = true;
        importer.borderMipmap = true;
        importer.filterMode = FilterMode.Bilinear;
        importer.wrapModeU = TextureWrapMode.Repeat;
        importer.wrapModeV = TextureWrapMode.Clamp;
        importer.textureCompression = TextureImporterCompression.CompressedHQ;
        importer.maxTextureSize = 4096;

        TextureImporterSettings settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.spriteAlignment = (int)SpriteAlignment.Custom;
        settings.spritePivot = new Vector2(0.5f, 0.5f);
        settings.spriteMeshType = SpriteMeshType.FullRect;
        importer.SetTextureSettings(settings);
        importer.SaveAndReimport();

        Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(SourcePath);
        if (sprite == null)
        {
            throw new InvalidDataException("V56 Sprite 导入失败：" + SourcePath);
        }

        EnsureFolder(CatalogFolder);
        TideV56OceanCatalog catalog = AssetDatabase.LoadAssetAtPath<TideV56OceanCatalog>(CatalogPath);
        if (catalog == null)
        {
            catalog = ScriptableObject.CreateInstance<TideV56OceanCatalog>();
            AssetDatabase.CreateAsset(catalog, CatalogPath);
        }

        catalog.Configure(sprite);
        EditorUtility.SetDirty(catalog);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        if (!catalog.IsComplete(out string reason))
        {
            throw new InvalidDataException("V56 海体索引生成后仍不完整：" + reason);
        }

        Debug.Log("PASS：V56 Balanced 低起伏连续海体已建立运行索引；V43 继续独占局部浪峰和泡沫。");
    }

    private static void EnsureFolder(string folderPath)
    {
        string[] parts = folderPath.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
            {
                AssetDatabase.CreateFolder(current, parts[i]);
            }

            current = next;
        }
    }
}
