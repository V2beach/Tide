using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 将 V32 正式 PNG 导入为统一 Pivot/PPU，并建立 Resources 轻量索引。
/// Builder 不复制图片；美术包可继续独立迭代，运行时只消费稳定引用。
/// </summary>
public static class TideV32FirstSliceArtCatalogBuilder
{
    private const string SourceRoot =
        "Assets/Art/GeneratedAI/ProductionStiltDockV32/Runtime";
    private const string V33RepairedHousePath =
        "Assets/Art/GeneratedAI/ProductionStiltDockV33/Runtime/House/TideHouseV33_Repaired.png";
    private const string CatalogFolder = "Assets/Resources/StiltFirstSliceAI";
    private const string CatalogPath = CatalogFolder + "/V32FirstSliceArtCatalog.asset";

    [MenuItem("Tide/Resources/Rebuild V32 First Slice Art Catalog")]
    public static void RebuildCatalog()
    {
        Sprite houseFound = ImportSprite(
            SourceRoot + "/House/TideHouseV32_Found.png",
            256f,
            new Vector2(0.5f, 0.03125f));
        Sprite houseRepaired = ImportSprite(
            V33RepairedHousePath,
            256f,
            new Vector2(0.5f, 0.03125f));
        Sprite[] climbFrames = ImportFrames(
            SourceRoot + "/Character/Climb/TideCharacterV32_Climb_F{0:00}.png",
            TideV32FirstSliceArtCatalog.ClimbFrameCount,
            512f,
            new Vector2(0.5f, 0.0625f));
        Sprite[] seatedFrames = ImportFrames(
            SourceRoot + "/Boat/Passenger/TideCharacterV32_Seated_F{0:00}.png",
            TideV32FirstSliceArtCatalog.SeatedFrameCount,
            512f,
            new Vector2(0.5f, 0.45f));

        EnsureFolder(CatalogFolder);
        TideV32FirstSliceArtCatalog catalog =
            AssetDatabase.LoadAssetAtPath<TideV32FirstSliceArtCatalog>(CatalogPath);
        if (catalog == null)
        {
            catalog = ScriptableObject.CreateInstance<TideV32FirstSliceArtCatalog>();
            AssetDatabase.CreateAsset(catalog, CatalogPath);
        }

        catalog.Configure(
            houseFound,
            houseRepaired,
            climbFrames,
            seatedFrames,
            0.1f,
            0.14f,
            0.58f,
            0.42f);
        EditorUtility.SetDirty(catalog);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        if (!catalog.IsComplete(out string reason))
        {
            throw new InvalidDataException("V32 运行索引生成后仍不完整：" + reason);
        }

        Debug.Log(
            "PASS：V32 发现态、V33 同坐标修复态、背身爬梯 6 帧、完整坐姿乘员 6 帧已建立轻量索引；" +
            "V30 室内与 V31 船体分层保持原所有权。");
    }

    private static Sprite[] ImportFrames(
        string pathPattern,
        int count,
        float pixelsPerUnit,
        Vector2 pivot)
    {
        Sprite[] frames = new Sprite[count];
        for (int i = 0; i < count; i++)
        {
            frames[i] = ImportSprite(
                string.Format(pathPattern, i),
                pixelsPerUnit,
                pivot);
        }

        return frames;
    }

    private static Sprite ImportSprite(string assetPath, float pixelsPerUnit, Vector2 pivot)
    {
        if (!File.Exists(assetPath))
        {
            throw new FileNotFoundException("V32 正式资源不存在", assetPath);
        }

        AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
        TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (importer == null)
        {
            throw new InvalidDataException("V32 PNG 不是可配置的 TextureImporter：" + assetPath);
        }

        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = pixelsPerUnit;
        importer.spritePivot = pivot;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.maxTextureSize = 4096;

        // Unity 2022.3 把单图 Sprite 的对齐方式保存在 TextureImporterSettings，
        // TextureImporter 本身没有 spriteAlignment 属性。Pivot 也在同一设置对象中
        // 再写一次，保证 SaveAndReimport 后不会被默认中心点覆盖。
        TextureImporterSettings settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.spriteAlignment = (int)SpriteAlignment.Custom;
        settings.spritePivot = pivot;
        settings.spriteMeshType = SpriteMeshType.FullRect;
        importer.SetTextureSettings(settings);
        importer.SaveAndReimport();

        Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
        if (sprite == null)
        {
            throw new InvalidDataException("V32 Sprite 导入失败：" + assetPath);
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
