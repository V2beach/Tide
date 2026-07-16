using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 导入 V37 完整船上人物，并创建 Resources 轻量索引。
///
/// 这些 1024 图在实机通常缩到约 80-100px 高，所以必须保留 Alpha 覆盖的
/// mipmap；否则衣纹和轮廓会在海面上产生白点闪烁。
/// </summary>
public static class TideV37BoatCharacterCatalogBuilder
{
    private const string SourceRoot =
        "Assets/Art/GeneratedAI/ProductionBoatCharacterActionsV37/Runtime/Character";
    private const string PropRoot =
        "Assets/Art/GeneratedAI/ProductionBoatCharacterActionsV37/Runtime/Props";
    private const string CatalogFolder = "Assets/Resources/StiltFirstSliceAI";
    private const string CatalogPath = CatalogFolder + "/V37BoatCharacterCatalog.asset";

    [MenuItem("Tide/Resources/Rebuild V37 Boat Character Catalog")]
    public static void RebuildCatalog()
    {
        Sprite[] trimFrames = ImportFrames("Trim", "TideCharacterV37_Trim");
        Sprite[] bailFrames = ImportFrames("Bail", "TideCharacterV37_Bail");
        Sprite[] braceFrames = ImportFrames("Brace", "TideCharacterV37_Brace");
        Sprite bailingBucket = ImportSprite(
            PropRoot + "/TidePropV37_BailingBucket.png",
            new Vector2(0.5f, 0.5f));

        EnsureFolder(CatalogFolder);
        TideV37BoatCharacterCatalog catalog =
            AssetDatabase.LoadAssetAtPath<TideV37BoatCharacterCatalog>(CatalogPath);
        if (catalog == null)
        {
            catalog = ScriptableObject.CreateInstance<TideV37BoatCharacterCatalog>();
            AssetDatabase.CreateAsset(catalog, CatalogPath);
        }

        // V37 和 V32 使用同一 1024 画布、512 PPU、座位基线与 Pivot。
        // 保持 0.42 可让动作切换时身体尺度不跳变。
        catalog.Configure(trimFrames, bailFrames, braceFrames, bailingBucket, 0.42f);
        EditorUtility.SetDirty(catalog);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        if (!catalog.IsComplete(out string reason))
        {
            throw new InvalidDataException("V37 运行索引生成后仍不完整：" + reason);
        }

        Debug.Log(
            "PASS：V37 操帆、舀水、暴潮受击各 6 帧已建立轻量索引；" +
            "完整身体保持在 V31 后船体与前船舷之间；小型船用桶保持独立道具层。" +
            "缩小采样 mipmap 已启用。");
    }

    private static Sprite[] ImportFrames(string folder, string prefix)
    {
        Sprite[] frames = new Sprite[TideV37BoatCharacterCatalog.FrameCount];
        for (int i = 0; i < frames.Length; i++)
        {
            string path = $"{SourceRoot}/{folder}/{prefix}_F{i:00}.png";
            frames[i] = ImportSprite(path, new Vector2(0.5f, 0.45f));
        }

        return frames;
    }

    private static Sprite ImportSprite(string assetPath, Vector2 pivot)
    {
        if (!File.Exists(assetPath))
        {
            throw new FileNotFoundException("V37 正式人物帧不存在", assetPath);
        }

        AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
        TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (importer == null)
        {
            throw new InvalidDataException("V37 PNG 不是可配置的 TextureImporter：" + assetPath);
        }

        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = 512f;
        importer.spritePivot = pivot;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = true;
        importer.borderMipmap = true;
        importer.mipMapsPreserveCoverage = true;
        importer.alphaTestReferenceValue = 0.35f;
        importer.filterMode = FilterMode.Bilinear;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.maxTextureSize = 2048;

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
            throw new InvalidDataException("V37 Sprite 导入失败：" + assetPath);
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
