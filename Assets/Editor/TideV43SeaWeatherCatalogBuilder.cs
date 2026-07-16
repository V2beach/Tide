using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 按 V43 契约导入透明浪、漩涡和云层，并建立 Resources 索引。
/// </summary>
public static class TideV43SeaWeatherCatalogBuilder
{
    private const string Root = "Assets/Art/GeneratedAI/ProductionSeaWeatherLayersV43/Runtime";
    private const string CatalogFolder = "Assets/Resources/StiltFirstSliceAI";
    private const string CatalogPath = CatalogFolder + "/V43SeaWeatherCatalog.asset";
    private static readonly Vector2 WavePivot = new Vector2(0.5f, 0.13671875f);
    private static readonly Vector2 CenterPivot = new Vector2(0.5f, 0.5f);

    [MenuItem("Tide/Resources/Rebuild V43 Sea Weather Catalog")]
    public static void RebuildCatalog()
    {
        Sprite[] longSwell = ImportSequence("Waves/LongSwell", "TideSeaV43_LongSwell", 8, WavePivot, 512f);
        Sprite[] windWave = ImportSequence("Waves/WindWave", "TideSeaV43_WindWave", 8, WavePivot, 512f);
        Sprite[] stormBreaker = ImportSequence("Waves/StormBreaker", "TideSeaV43_StormBreaker", 8, WavePivot, 512f);
        Sprite[] vortexDepression = ImportSequence("Vortex/DepressionMask", "TideSeaV43_VortexDepressionMask", 12, CenterPivot, 512f);
        Sprite[] vortexInner = ImportSequence("Vortex/InnerFlow", "TideSeaV43_VortexInnerFlow", 12, CenterPivot, 512f);
        Sprite[] vortexOuter = ImportSequence("Vortex/OuterFoam", "TideSeaV43_VortexOuterFoam", 12, CenterPivot, 512f);
        Sprite farCloud = ImportSprite("Clouds/TideSeaV43_FarCloudWall_Seamless.png", CenterPivot, 256f, true);
        Sprite midCloud = ImportSprite("Clouds/TideSeaV43_MidWeatherBank_Seamless.png", CenterPivot, 256f, true);
        Sprite nearCloud = ImportSprite("Clouds/TideSeaV43_NearScud_Seamless.png", CenterPivot, 256f, true);

        EnsureFolder(CatalogFolder);
        TideV43SeaWeatherCatalog catalog =
            AssetDatabase.LoadAssetAtPath<TideV43SeaWeatherCatalog>(CatalogPath);
        if (catalog == null)
        {
            catalog = ScriptableObject.CreateInstance<TideV43SeaWeatherCatalog>();
            AssetDatabase.CreateAsset(catalog, CatalogPath);
        }

        catalog.Configure(
            longSwell,
            windWave,
            stormBreaker,
            vortexDepression,
            vortexInner,
            vortexOuter,
            farCloud,
            midCloud,
            nearCloud);
        EditorUtility.SetDirty(catalog);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        if (!catalog.IsComplete(out string reason))
        {
            throw new InvalidDataException("V43 海况索引生成后仍不完整：" + reason);
        }

        Debug.Log("PASS：V43 透明浪 24 帧、漩涡三层 36 帧和三层云已建立运行索引；连续海体仍由模拟拥有。");
    }

    private static Sprite[] ImportSequence(
        string folder,
        string prefix,
        int count,
        Vector2 pivot,
        float pixelsPerUnit)
    {
        Sprite[] frames = new Sprite[count];
        for (int i = 0; i < count; i++)
        {
            frames[i] = ImportSprite(
                $"{folder}/{prefix}_F{i:00}.png",
                pivot,
                pixelsPerUnit,
                false);
        }

        return frames;
    }

    private static Sprite ImportSprite(
        string relativePath,
        Vector2 pivot,
        float pixelsPerUnit,
        bool repeat)
    {
        string path = $"{Root}/{relativePath}";
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("V43 正式海况层不存在", path);
        }

        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null)
        {
            throw new InvalidDataException("V43 PNG 不是可配置的 TextureImporter：" + path);
        }

        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = pixelsPerUnit;
        importer.spritePivot = pivot;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = true;
        importer.borderMipmap = true;
        importer.mipMapsPreserveCoverage = true;
        importer.alphaTestReferenceValue = 0.3f;
        importer.filterMode = FilterMode.Bilinear;
        importer.wrapMode = repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.maxTextureSize = repeat ? 4096 : 2048;

        TextureImporterSettings settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.spriteAlignment = (int)SpriteAlignment.Custom;
        settings.spritePivot = pivot;
        settings.spriteMeshType = SpriteMeshType.FullRect;
        importer.SetTextureSettings(settings);
        importer.SaveAndReimport();

        Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (sprite == null)
        {
            throw new InvalidDataException("V43 Sprite 导入失败：" + path);
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
