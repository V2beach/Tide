using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 按 V54 运行契约导入唯一的 Balanced 档七态网，并建立 Resources 索引。
/// UHD/High 只作源资源和 QA 备选，运行时不得混档。
/// </summary>
public static class TideV54NetCatalogBuilder
{
    private const string Root =
        "Assets/Art/GeneratedAI/ProductionTideNetStatesV54/Runtime/Balanced";
    private const string CatalogFolder = "Assets/Resources/StiltFirstSliceAI";
    private const string CatalogPath = CatalogFolder + "/V54NetCatalog.asset";
    private const float PixelsPerUnit = 192f;
    private static readonly Vector2 CenterPivot = new Vector2(0.5f, 0.5f);

    [MenuItem("Tide/Resources/Rebuild V54 Tide Net Catalog")]
    public static void RebuildCatalog()
    {
        Sprite deployedDry = ImportSprite("Deployed/TideNetV54_DeployedDry.png");
        Sprite deployedWet = ImportSprite("Deployed/TideNetV54_DeployedWet.png");
        Sprite deployedFrayed = ImportSprite("Deployed/TideNetV54_DeployedFrayed.png");
        Sprite brokenResidue = ImportSprite("Deployed/TideNetV54_BrokenResidue.png");
        Sprite storedDry = ImportSprite("Bundles/TideNetV54_StoredDry.png");
        Sprite carriedDry = ImportSprite("Bundles/TideNetV54_CarriedDry.png");
        Sprite hauledWet = ImportSprite("Bundles/TideNetV54_HauledWet.png");

        EnsureFolder(CatalogFolder);
        TideV54NetCatalog catalog = AssetDatabase.LoadAssetAtPath<TideV54NetCatalog>(CatalogPath);
        if (catalog == null)
        {
            catalog = ScriptableObject.CreateInstance<TideV54NetCatalog>();
            AssetDatabase.CreateAsset(catalog, CatalogPath);
        }

        catalog.Configure(
            deployedDry,
            deployedWet,
            deployedFrayed,
            brokenResidue,
            storedDry,
            carriedDry,
            hauledWet);
        EditorUtility.SetDirty(catalog);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        if (!catalog.IsComplete(out string reason))
        {
            throw new InvalidDataException("V54 潮网索引生成后仍不完整：" + reason);
        }

        Debug.Log("PASS：V54 Balanced 七态潮网已建立运行索引；悬绳、人物、捕获物和海水仍由运行侧拥有。");
    }

    private static Sprite ImportSprite(string relativePath)
    {
        string path = Root + "/" + relativePath;
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("V54 正式潮网资源不存在", path);
        }

        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null)
        {
            throw new InvalidDataException("V54 PNG 不是可配置的 TextureImporter：" + path);
        }

        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = PixelsPerUnit;
        importer.spritePivot = CenterPivot;
        importer.alphaIsTransparency = true;
        // Balanced 七态基准显存为 4.27 MiB；关闭 mipmap 才不会平白再增加约三分之一。
        importer.mipmapEnabled = false;
        importer.filterMode = FilterMode.Bilinear;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.maxTextureSize = 1024;

        TextureImporterSettings settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.spriteAlignment = (int)SpriteAlignment.Custom;
        settings.spritePivot = CenterPivot;
        settings.spriteMeshType = SpriteMeshType.FullRect;
        importer.SetTextureSettings(settings);
        importer.SaveAndReimport();

        Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (sprite == null)
        {
            throw new InvalidDataException("V54 Sprite 导入失败：" + path);
        }

        return sprite;
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
