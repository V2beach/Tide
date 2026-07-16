using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 导入 V42 正式生存动作，并创建 Resources 轻量索引。
/// </summary>
public static class TideV42CharacterSurvivalCatalogBuilder
{
    private const string SourceRoot =
        "Assets/Art/GeneratedAI/ProductionCharacterSurvivalActionsV42/Runtime/Character";
    private const string CatalogFolder = "Assets/Resources/StiltFirstSliceAI";
    private const string CatalogPath = CatalogFolder + "/V42CharacterSurvivalCatalog.asset";
    private static readonly Vector2 SurfacePivot = new Vector2(0.5f, 0.0625f);
    private static readonly Vector2 DrownPivot = new Vector2(0.5f, 0.5f);

    [MenuItem("Tide/Resources/Rebuild V42 Character Survival Catalog")]
    public static void RebuildCatalog()
    {
        Sprite[] coldShiver = ImportFrames(
            TideV42CharacterSurvivalAction.ColdShiver,
            "ColdShiver",
            TideV42CharacterSurvivalPresentationModel.ColdShiverFrameCount);
        Sprite[] sleep = ImportFrames(
            TideV42CharacterSurvivalAction.Sleep,
            "Sleep",
            TideV42CharacterSurvivalPresentationModel.SleepFrameCount);
        Sprite[] drown = ImportFrames(
            TideV42CharacterSurvivalAction.Drown,
            "Drown",
            TideV42CharacterSurvivalPresentationModel.DrownFrameCount);
        Sprite[] coldCollapse = ImportFrames(
            TideV42CharacterSurvivalAction.ColdCollapse,
            "ColdCollapse",
            TideV42CharacterSurvivalPresentationModel.ColdCollapseFrameCount);

        EnsureFolder(CatalogFolder);
        TideV42CharacterSurvivalCatalog catalog =
            AssetDatabase.LoadAssetAtPath<TideV42CharacterSurvivalCatalog>(CatalogPath);
        if (catalog == null)
        {
            catalog = ScriptableObject.CreateInstance<TideV42CharacterSurvivalCatalog>();
            AssetDatabase.CreateAsset(catalog, CatalogPath);
        }

        catalog.Configure(coldShiver, sleep, drown, coldCollapse);
        EditorUtility.SetDirty(catalog);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        if (!catalog.IsComplete(out string reason))
        {
            throw new InvalidDataException("V42 生存动作索引生成后仍不完整：" + reason);
        }

        Debug.Log(
            $"PASS：V42 生存动作运行索引已生成，共 {catalog.TotalFrameCount} 帧；" +
            "脚/床接触 Pivot、溺水中心 Pivot、512 PPU 和透明边缘导入设置均已确认。");
    }

    private static Sprite[] ImportFrames(
        TideV42CharacterSurvivalAction action,
        string folder,
        int frameCount)
    {
        Sprite[] frames = new Sprite[frameCount];
        for (int i = 0; i < frameCount; i++)
        {
            string path = $"{SourceRoot}/{folder}/TideCharacterV42_{folder}_F{i:00}.png";
            frames[i] = ImportSprite(action, path);
        }

        return frames;
    }

    private static Sprite ImportSprite(
        TideV42CharacterSurvivalAction action,
        string assetPath)
    {
        if (!File.Exists(assetPath))
        {
            throw new FileNotFoundException($"V42 {action} 正式人物帧不存在", assetPath);
        }

        AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
        TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (importer == null)
        {
            throw new InvalidDataException("V42 PNG 不是可配置的 TextureImporter：" + assetPath);
        }

        Vector2 expectedPivot = action == TideV42CharacterSurvivalAction.Drown
            ? DrownPivot
            : SurfacePivot;
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = TideV42CharacterSurvivalPresentationModel.PixelsPerUnit;
        importer.spritePivot = expectedPivot;
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
        settings.spritePivot = expectedPivot;
        settings.spriteMeshType = SpriteMeshType.FullRect;
        importer.SetTextureSettings(settings);
        importer.SaveAndReimport();

        Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
        if (sprite == null)
        {
            throw new InvalidDataException("V42 Sprite 导入失败：" + assetPath);
        }

        Vector2 actualPivot = new Vector2(
            sprite.pivot.x / sprite.rect.width,
            sprite.pivot.y / sprite.rect.height);
        if (Vector2.Distance(actualPivot, expectedPivot) > 0.0001f)
        {
            throw new InvalidDataException(
                $"V42 {action} Pivot 漂移：{assetPath} actual={actualPivot} expected={expectedPivot}");
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
