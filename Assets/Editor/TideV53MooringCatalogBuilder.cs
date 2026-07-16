using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 按 V53 运行契约导入唯一的 Balanced 档，并建立 Resources 索引。
/// </summary>
public static class TideV53MooringCatalogBuilder
{
    private const string Root =
        "Assets/Art/GeneratedAI/ProductionMooringConnectionV53/Runtime/Balanced";
    private const string CatalogFolder = "Assets/Resources/StiltFirstSliceAI";
    private const string CatalogPath = CatalogFolder + "/V53MooringCatalog.asset";
    private const float PixelsPerUnit = 192f;

    [MenuItem("Tide/Resources/Rebuild V53 Mooring Catalog")]
    public static void RebuildCatalog()
    {
        Sprite found = ImportSprite("Pier/TideMooringPierV53_FoundWeathered.png");
        Sprite serviceable = ImportSprite("Pier/TideMooringPierV53_Serviceable.png");
        Sprite gangplank = ImportSprite("Gangplank/TideMooringGangplankV53_Serviceable.png");

        EnsureFolder(CatalogFolder);
        TideV53MooringCatalog catalog = AssetDatabase.LoadAssetAtPath<TideV53MooringCatalog>(CatalogPath);
        if (catalog == null)
        {
            catalog = ScriptableObject.CreateInstance<TideV53MooringCatalog>();
            AssetDatabase.CreateAsset(catalog, CatalogPath);
        }

        catalog.Configure(found, serviceable, gangplank);
        EditorUtility.SetDirty(catalog);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        if (!catalog.IsComplete(out string reason))
        {
            throw new InvalidDataException("V53 泊位索引生成后仍不完整：" + reason);
        }

        Debug.Log("PASS：V53 Balanced 固定木路与活动跳板已建立运行索引。碰撞、船艉和潮位仍由运行侧拥有。");
    }

    private static Sprite ImportSprite(string relativePath)
    {
        string path = Root + "/" + relativePath;
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("V53 正式泊位资源不存在", path);
        }

        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null)
        {
            throw new InvalidDataException("V53 PNG 不是可配置的 TextureImporter：" + path);
        }

        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = PixelsPerUnit;
        importer.spritePivot = new Vector2(0.5f, 0.5f);
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.filterMode = FilterMode.Bilinear;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.maxTextureSize = 1024;

        TextureImporterSettings settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.spriteAlignment = (int)SpriteAlignment.Custom;
        settings.spritePivot = new Vector2(0.5f, 0.5f);
        settings.spriteMeshType = SpriteMeshType.FullRect;
        importer.SetTextureSettings(settings);
        importer.SaveAndReimport();

        Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (sprite == null)
        {
            throw new InvalidDataException("V53 Sprite 导入失败：" + path);
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
