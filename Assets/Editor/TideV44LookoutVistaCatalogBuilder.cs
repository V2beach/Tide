using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 按 V44 契约导入瞭望透明层，并建立 Resources 索引。
/// </summary>
public static class TideV44LookoutVistaCatalogBuilder
{
    private const string Root = "Assets/Art/GeneratedAI/ProductionLookoutVistaLayersV44/Runtime";
    private const string CatalogFolder = "Assets/Resources/StiltFirstSliceAI";
    private const string CatalogPath = CatalogFolder + "/V44LookoutVistaCatalog.asset";
    private static readonly Vector2 CenterPivot = new Vector2(0.5f, 0.5f);
    private static readonly Vector2 BeamPivot = new Vector2(0.046875f, 0.5f);

    [MenuItem("Tide/Resources/Rebuild V44 Lookout Vista Catalog")]
    public static void RebuildCatalog()
    {
        Sprite roofDamage = ImportSprite("Near/TideLookoutV44_NearRoofDamage.png", CenterPivot, 4096);
        Sprite roofRepair = ImportSprite("Near/TideLookoutV44_NearRoofRepair.png", CenterPivot, 4096);
        Sprite crane = ImportSprite("Near/TideLookoutV44_NearCrane.png", CenterPivot, 4096);
        Sprite wreckField = ImportSprite("Mid/TideLookoutV44_MidWreckField.png", CenterPivot, 4096);
        Sprite lighthouse = ImportSprite("Far/TideLookoutV44_FarLighthouse.png", CenterPivot, 2048);
        Sprite[] beamFrames = new Sprite[TideV44LookoutVistaPresentationModel.BeamFrameCount];
        for (int i = 0; i < beamFrames.Length; i++)
        {
            beamFrames[i] = ImportSprite(
                $"Far/LighthouseBeam/TideLookoutV44_LighthouseBeam_F{i:00}.png",
                BeamPivot,
                2048);
        }

        EnsureFolder(CatalogFolder);
        TideV44LookoutVistaCatalog catalog =
            AssetDatabase.LoadAssetAtPath<TideV44LookoutVistaCatalog>(CatalogPath);
        if (catalog == null)
        {
            catalog = ScriptableObject.CreateInstance<TideV44LookoutVistaCatalog>();
            AssetDatabase.CreateAsset(catalog, CatalogPath);
        }

        catalog.Configure(roofDamage, roofRepair, crane, wreckField, lighthouse, beamFrames);
        EditorUtility.SetDirty(catalog);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        if (!catalog.IsComplete(out string reason))
        {
            throw new InvalidDataException("V44 瞭望远景索引生成后仍不完整：" + reason);
        }

        Debug.Log("PASS：V44 近景屋檐/吊机、中景残骸、远景灯塔和十二相雾光已建立运行索引；未导入 QA 合成海板。");
    }

    private static Sprite ImportSprite(string relativePath, Vector2 pivot, int maxTextureSize)
    {
        string path = $"{Root}/{relativePath}";
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("V44 正式瞭望层不存在", path);
        }

        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null)
        {
            throw new InvalidDataException("V44 PNG 不是可配置的 TextureImporter：" + path);
        }

        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = TideV44LookoutVistaPresentationModel.PixelsPerUnit;
        importer.spritePivot = pivot;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = true;
        importer.borderMipmap = true;
        importer.mipMapsPreserveCoverage = true;
        importer.alphaTestReferenceValue = 0.3f;
        importer.filterMode = FilterMode.Bilinear;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.maxTextureSize = maxTextureSize;

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
            throw new InvalidDataException("V44 Sprite 导入失败：" + path);
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
