using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 按 V59 运行契约导入唯一的 High 档十二件潮带物，并建立 Resources 索引。
/// Balanced/UHD 留作性能回退和近景 QA，禁止在同一运行视图混档。
/// </summary>
public static class TideV59FindCatalogBuilder
{
    private const string Root =
        "Assets/Art/GeneratedAI/ProductionTideBorneFindsV59/Runtime/High";
    private const string CatalogFolder = "Assets/Resources/StiltFirstSliceAI";
    private const string CatalogPath = CatalogFolder + "/V59TideFindCatalog.asset";
    private const float PixelsPerUnit = 512f;
    private static readonly Vector2 CenterPivot = new Vector2(0.5f, 0.5f);

    [MenuItem("Tide/Resources/Rebuild V59 Tide Find Catalog")]
    public static void RebuildCatalog()
    {
        Sprite[] fish = ImportGroup("Fish", "F01_SilverHerring", "F02_GreyMullet", "F03_PalePollock");
        Sprite[] wood = ImportGroup("Wood", "W01_SaltHullPlank", "W02_RopeSpar", "W03_TwinStrakes");
        Sprite[] trash = ImportGroup("Trash", "T01_NetRopeShackle", "T02_SailclothCorkChain", "T03_CaskStavesTinCup");
        Sprite[] relic = ImportGroup("Relic", "R01_OilclothChartPacket", "R02_CorrodedBearingCompass", "R03_LensRadioCoil");

        EnsureFolder(CatalogFolder);
        TideV59FindCatalog catalog = AssetDatabase.LoadAssetAtPath<TideV59FindCatalog>(CatalogPath);
        if (catalog == null)
        {
            catalog = ScriptableObject.CreateInstance<TideV59FindCatalog>();
            AssetDatabase.CreateAsset(catalog, CatalogPath);
        }

        catalog.Configure(fish, wood, trash, relic);
        EditorUtility.SetDirty(catalog);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        if (!catalog.IsComplete(out string reason))
        {
            throw new InvalidDataException("V59 潮带物索引生成后仍不完整：" + reason);
        }

        Debug.Log("PASS：V59 High 十二件潮带物已建立运行索引；水、网、手和生命周期状态仍由运行侧拥有。");
    }

    private static Sprite[] ImportGroup(string folder, params string[] ids)
    {
        Sprite[] sprites = new Sprite[ids.Length];
        for (int i = 0; i < ids.Length; i++)
        {
            string path = $"{Root}/{folder}/TideFindsV59_{ids[i]}.png";
            sprites[i] = ImportSprite(path);
        }

        return sprites;
    }

    private static Sprite ImportSprite(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("V59 High 正式潮带物不存在", path);
        }

        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null)
        {
            throw new InvalidDataException("V59 PNG 不是可配置的 TextureImporter：" + path);
        }

        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = PixelsPerUnit;
        importer.spritePivot = CenterPivot;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.filterMode = FilterMode.Bilinear;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.maxTextureSize = 512;

        TextureImporterSettings settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.spriteAlignment = (int)SpriteAlignment.Custom;
        settings.spritePivot = CenterPivot;
        // Tight mesh makes Sprite.bounds match the contract's visible dimensions rather
        // than the transparent export canvas, which keeps SetWorldSize physically honest.
        settings.spriteMeshType = SpriteMeshType.Tight;
        importer.SetTextureSettings(settings);
        importer.SaveAndReimport();

        Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (sprite == null)
        {
            throw new InvalidDataException("V59 Sprite 导入失败：" + path);
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
