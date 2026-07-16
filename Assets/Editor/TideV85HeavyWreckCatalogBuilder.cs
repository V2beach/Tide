using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 只导入首轮实际使用的外露龙骨肋五张图。其余 V85 family 留在源资产目录，
/// 不进入 Resources Catalog，也不会被首轮构建引用。
/// </summary>
public static class TideV85HeavyWreckCatalogBuilder
{
    private const string V72Root =
        "Assets/Art/GeneratedAI/ProductionShipwreckArrivalV72/Runtime/Balanced/ExposedKeelRib";
    private const string V85Root =
        "Assets/Art/GeneratedAI/ProductionHeavyWreckDismantlingV85/Runtime/Balanced/ExposedKeelRib";
    private const string CatalogPath =
        "Assets/Resources/StiltFirstSliceAI/V85HeavyWreckCatalog.asset";
    private const float PixelsPerUnit = 192f;

    [MenuItem("Tide/Resources/Rebuild V85 Heavy Wreck Catalog")]
    public static void RebuildCatalog()
    {
        Sprite intact = ImportSprite(
            V72Root + "/TideShipwreckV72_ExposedKeelRib.png",
            new Vector2(0.5f, 0.5f));
        Sprite score = ImportSprite(
            V85Root + "/TideWreckV85_ExposedKeelRib_ScoreMarks.png",
            new Vector2(0.42742902f, 0.47150505f));
        Sprite remainder = ImportSprite(
            V85Root + "/TideWreckV85_ExposedKeelRib_Remainder.png",
            new Vector2(0.46365687f, 0.39981971f));
        Sprite pieceA = ImportSprite(
            V85Root + "/TideWreckV85_ExposedKeelRib_PieceA.png",
            new Vector2(0.48812171f, 0.50672168f));
        Sprite pieceB = ImportSprite(
            V85Root + "/TideWreckV85_ExposedKeelRib_PieceB.png",
            new Vector2(0.57071125f, 0.49397274f));

        TideV85HeavyWreckCatalog catalog =
            AssetDatabase.LoadAssetAtPath<TideV85HeavyWreckCatalog>(CatalogPath);
        if (catalog == null)
        {
            catalog = ScriptableObject.CreateInstance<TideV85HeavyWreckCatalog>();
            AssetDatabase.CreateAsset(catalog, CatalogPath);
        }

        catalog.Configure(intact, score, remainder, pieceA, pieceB);
        EditorUtility.SetDirty(catalog);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        if (!catalog.IsComplete(out string reason))
        {
            throw new InvalidDataException("V85 重型残骸索引生成后仍不完整：" + reason);
        }

        Debug.Log("PASS：V85 首轮只接入外露龙骨肋五 owner；其余 family 未进入运行索引。");
    }

    private static Sprite ImportSprite(string path, Vector2 pivot)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("V72/V85 正式重型残骸资源不存在", path);
        }

        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null)
        {
            throw new InvalidDataException("重型残骸 PNG 不是可配置的 TextureImporter：" + path);
        }

        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = PixelsPerUnit;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.filterMode = FilterMode.Bilinear;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.maxTextureSize = 1024;

        TextureImporterSettings settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.spriteAlignment = (int)SpriteAlignment.Custom;
        settings.spritePivot = pivot;
        settings.spriteMeshType = SpriteMeshType.Tight;
        importer.SetTextureSettings(settings);
        importer.SaveAndReimport();

        Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (sprite == null)
        {
            throw new InvalidDataException("重型残骸 Sprite 导入失败：" + path);
        }

        return sprite;
    }
}
