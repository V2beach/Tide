using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 导入 V41 接触动作，并创建 Resources 轻量索引。
///
/// 人物在 Game 视图通常只有几十到一百像素高。即使源资源的 .meta 暂未配置，
/// 这里也强制启用保 Alpha 覆盖的 mipmap，避免衣纹和轮廓在移动时闪烁。
/// </summary>
public static class TideV41CharacterContactCatalogBuilder
{
    private const string SourceRoot =
        "Assets/Art/GeneratedAI/ProductionCharacterContactActionsV41/Runtime/Character";
    private const string CatalogFolder = "Assets/Resources/StiltFirstSliceAI";
    private const string CatalogPath = CatalogFolder + "/V41CharacterContactCatalog.asset";
    private static readonly Vector2 AuthoredPivot = new Vector2(0.5f, 0.0625f);

    [MenuItem("Tide/Resources/Rebuild V41 Character Contact Catalog")]
    public static void RebuildCatalog()
    {
        Sprite[] walk = ImportFrames(
            TideV41CharacterContactAction.Walk,
            "Walk",
            TideV41CharacterContactPresentationModel.WalkFrameCount);
        Sprite[] carryNetWalk = ImportFrames(
            TideV41CharacterContactAction.CarryNetWalk,
            "CarryNetWalk",
            TideV41CharacterContactPresentationModel.CarryNetWalkFrameCount);
        Sprite[] board = ImportFrames(
            TideV41CharacterContactAction.Board,
            "Board",
            TideV41CharacterContactPresentationModel.BoardFrameCount);
        Sprite[] tieNet = ImportFrames(
            TideV41CharacterContactAction.TieNet,
            "TieNet",
            TideV41CharacterContactPresentationModel.TieNetFrameCount);
        Sprite[] doorEnter = ImportFrames(
            TideV41CharacterContactAction.DoorEnter,
            "DoorEnter",
            TideV41CharacterContactPresentationModel.DoorEnterFrameCount);
        Sprite[] lowerSinkline = ImportFrames(
            TideV41CharacterContactAction.LowerSinkline,
            "LowerSinkline",
            TideV41CharacterContactPresentationModel.LowerSinklineFrameCount);
        Sprite[] lookout = ImportFrames(
            TideV41CharacterContactAction.Lookout,
            "Lookout",
            TideV41CharacterContactPresentationModel.LookoutFrameCount);

        EnsureFolder(CatalogFolder);
        TideV41CharacterContactCatalog catalog =
            AssetDatabase.LoadAssetAtPath<TideV41CharacterContactCatalog>(CatalogPath);
        if (catalog == null)
        {
            catalog = ScriptableObject.CreateInstance<TideV41CharacterContactCatalog>();
            AssetDatabase.CreateAsset(catalog, CatalogPath);
        }

        catalog.Configure(
            walk,
            carryNetWalk,
            board,
            tieNet,
            doorEnter,
            lowerSinkline,
            lookout);
        EditorUtility.SetDirty(catalog);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        if (!catalog.IsComplete(out string reason))
        {
            throw new InvalidDataException("V41 人物接触动作索引生成后仍不完整：" + reason);
        }

        Debug.Log(
            $"PASS：V41 人物接触动作运行索引已生成，共 {catalog.TotalFrameCount} 帧；" +
            "统一脚底 Pivot、512 PPU、mipmap 和 Alpha 覆盖已由导入器确认。");
    }

    private static Sprite[] ImportFrames(
        TideV41CharacterContactAction action,
        string folder,
        int frameCount)
    {
        Sprite[] frames = new Sprite[frameCount];
        for (int i = 0; i < frameCount; i++)
        {
            string path = $"{SourceRoot}/{folder}/TideCharacterV41_{folder}_F{i:00}.png";
            frames[i] = ImportSprite(action, path);
        }

        return frames;
    }

    private static Sprite ImportSprite(
        TideV41CharacterContactAction action,
        string assetPath)
    {
        if (!File.Exists(assetPath))
        {
            throw new FileNotFoundException($"V41 {action} 正式人物帧不存在", assetPath);
        }

        AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
        TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (importer == null)
        {
            throw new InvalidDataException("V41 PNG 不是可配置的 TextureImporter：" + assetPath);
        }

        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = TideV41CharacterContactPresentationModel.PixelsPerUnit;
        importer.spritePivot = AuthoredPivot;
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
        settings.spritePivot = AuthoredPivot;
        settings.spriteMeshType = SpriteMeshType.FullRect;
        importer.SetTextureSettings(settings);
        importer.SaveAndReimport();

        Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
        if (sprite == null)
        {
            throw new InvalidDataException("V41 Sprite 导入失败：" + assetPath);
        }

        Vector2 actualPivot = new Vector2(
            sprite.pivot.x / sprite.rect.width,
            sprite.pivot.y / sprite.rect.height);
        if (Vector2.Distance(actualPivot, AuthoredPivot) > 0.0001f)
        {
            throw new InvalidDataException(
                $"V41 {action} Pivot 漂移：{assetPath} actual={actualPivot} expected={AuthoredPivot}");
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
