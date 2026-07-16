using System.IO;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 从 V70 运行契约原子建立 Balanced 侧视漩涡索引。
///
/// 路径、帧数和偏移直接读取 JSON，避免资源会话更新裁切后代码仍悄悄沿用旧值。
/// Builder 不扫描目录，也不接入 High/QA/Source，因此 Catalog 不会因同名文件或
/// 后续交付增加内容而意外膨胀。
/// </summary>
public static class TideV70VortexCatalogBuilder
{
    private const string Root = "Assets/Art/GeneratedAI/ProductionSideViewVortexV70";
    private const string ContractPath = Root + "/runtime-contract.json";
    private const string CatalogFolder = "Assets/Resources/StiltFirstSliceAI";
    private const string CatalogPath = CatalogFolder + "/V70VortexCatalog.asset";
    private static readonly Vector2 CenterPivot = new Vector2(0.5f, 0.5f);

    [MenuItem("Tide/Resources/Rebuild V70 Side View Vortex Catalog")]
    public static void RebuildCatalog()
    {
        if (!File.Exists(ContractPath))
        {
            throw new FileNotFoundException("V70 运行契约不存在", ContractPath);
        }

        JObject contract = JObject.Parse(File.ReadAllText(ContractPath));
        string recommendedProfile = (string)contract["recommended_runtime_profile"];
        if (recommendedProfile != TideV70VortexPresentationModel.RuntimeProfile)
        {
            throw new InvalidDataException(
                $"V70 推荐档位是 {recommendedProfile}，不是预期的 Balanced");
        }

        JObject profiles = RequireObject(contract["profiles"], "profiles");
        JObject profile = RequireObject(
            profiles[TideV70VortexPresentationModel.RuntimeProfile],
            "profiles.Balanced");
        int pixelsPerUnit = (int?)profile["pixels_per_unit"] ?? 0;
        int maxTextureSize = (int?)profile["max_texture_size"] ?? 0;
        if (pixelsPerUnit != (int)TideV70VortexPresentationModel.PixelsPerUnit ||
            maxTextureSize != 1024)
        {
            throw new InvalidDataException(
                $"V70 Balanced 导入契约异常：PPU={pixelsPerUnit}，Max={maxTextureSize}");
        }

        JObject layers = RequireObject(profile["layers"], "profiles.Balanced.layers");
        Sprite[][] framesByLayer = new Sprite[TideV70VortexPresentationModel.LayerCount][];
        Vector2[] offsetsByLayer = new Vector2[TideV70VortexPresentationModel.LayerCount];
        for (int layerIndex = 0; layerIndex < TideV70VortexPresentationModel.LayerCount; layerIndex++)
        {
            TideV70VortexLayer layer = (TideV70VortexLayer)layerIndex;
            JObject layerJson = RequireObject(layers[layer.ToString()], $"layers.{layer}");
            JArray framePaths = layerJson["frames"] as JArray;
            if (framePaths == null || framePaths.Count != TideV70VortexPresentationModel.FrameCount)
            {
                throw new InvalidDataException($"V70 {layer} 不是完整十二相");
            }

            Sprite[] layerFrames = new Sprite[framePaths.Count];
            for (int frameIndex = 0; frameIndex < framePaths.Count; frameIndex++)
            {
                string path = (string)framePaths[frameIndex];
                if (string.IsNullOrWhiteSpace(path) ||
                    path.IndexOf("/Runtime/Balanced/", System.StringComparison.Ordinal) < 0)
                {
                    throw new InvalidDataException(
                        $"V70 {layer} 第 {frameIndex} 帧不属于唯一 Balanced 运行档：{path}");
                }

                layerFrames[frameIndex] = ImportSprite(path, pixelsPerUnit, maxTextureSize);
            }

            JArray offset = layerJson["world_offset_from_vortex_pivot"] as JArray;
            if (offset == null || offset.Count != 2)
            {
                throw new InvalidDataException($"V70 {layer} 缺少二维水线偏移");
            }

            framesByLayer[layerIndex] = layerFrames;
            offsetsByLayer[layerIndex] = new Vector2((float)offset[0], (float)offset[1]);
        }

        EnsureFolder(CatalogFolder);
        TideV70VortexCatalog catalog = AssetDatabase.LoadAssetAtPath<TideV70VortexCatalog>(CatalogPath);
        if (catalog == null)
        {
            catalog = ScriptableObject.CreateInstance<TideV70VortexCatalog>();
            AssetDatabase.CreateAsset(catalog, CatalogPath);
        }

        catalog.Configure(
            framesByLayer[(int)TideV70VortexLayer.ThroatDepression],
            framesByLayer[(int)TideV70VortexLayer.UnderwaterReturnFlow],
            framesByLayer[(int)TideV70VortexLayer.SurfaceConvergenceFoam],
            offsetsByLayer[(int)TideV70VortexLayer.ThroatDepression],
            offsetsByLayer[(int)TideV70VortexLayer.UnderwaterReturnFlow],
            offsetsByLayer[(int)TideV70VortexLayer.SurfaceConvergenceFoam]);
        EditorUtility.SetDirty(catalog);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        if (!catalog.IsComplete(out string reason))
        {
            throw new InvalidDataException("V70 侧视漩涡索引生成后仍不完整：" + reason);
        }

        Debug.Log(
            "PASS：V70 Balanced 三层十二相已建立运行索引；High、QA、Source 和局部海板均未进入 Catalog。");
    }

    private static Sprite ImportSprite(string path, int pixelsPerUnit, int maxTextureSize)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("V70 正式运行帧不存在", path);
        }

        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null)
        {
            throw new InvalidDataException("V70 PNG 不是可配置的 TextureImporter：" + path);
        }

        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = pixelsPerUnit;
        importer.spritePivot = CenterPivot;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = true;
        importer.borderMipmap = true;
        importer.mipMapsPreserveCoverage = true;
        importer.alphaTestReferenceValue = 0.3f;
        importer.filterMode = FilterMode.Bilinear;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.maxTextureSize = maxTextureSize;

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
            throw new InvalidDataException("V70 Sprite 导入失败：" + path);
        }

        return sprite;
    }

    private static JObject RequireObject(JToken token, string fieldName)
    {
        JObject value = token as JObject;
        if (value == null)
        {
            throw new InvalidDataException("V70 运行契约缺少对象：" + fieldName);
        }

        return value;
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
