using System.IO;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 按 V34 JSON 契约导入稳定底图和六组裁切 owner，并建立 Resources 轻量索引。
/// 正式 PNG 保留在美术目录；运行时资产只保存引用和配准数据。
/// </summary>
public static class TideV34HouseExteriorCatalogBuilder
{
    private const string SourceRoot = "Assets/Art/GeneratedAI/ProductionStiltDockV34";
    private const string ContractPath = SourceRoot + "/tide-v34-exterior-repair-contract.json";
    private const string CatalogFolder = "Assets/Resources/StiltFirstSliceAI";
    private const string CatalogPath = CatalogFolder + "/V34HouseExteriorCatalog.asset";

    [MenuItem("Tide/Resources/Rebuild V34 House Exterior Catalog")]
    public static void RebuildCatalog()
    {
        if (!File.Exists(ContractPath))
        {
            throw new FileNotFoundException("V34 外景维修契约不存在", ContractPath);
        }

        JObject contract = JObject.Parse(File.ReadAllText(ContractPath));
        int version = contract["version"]?.Value<int>() ?? 0;
        JObject registration = contract["registration"] as JObject;
        float stablePpu = registration?["stable_ppu"]?.Value<float>() ?? 256f;
        float ownerPpu = registration?["owner_ppu"]?.Value<float>() ?? 256f;
        Sprite stableBase = ImportSprite(
            contract["stable_base"]?.Value<string>(),
            stablePpu,
            ReadVector2(registration?["stable_pivot"]));

        JArray ownersJson = contract["owners"] as JArray;
        if (ownersJson == null || ownersJson.Count != TideV34HouseExteriorPresentationModel.OwnerCount)
        {
            throw new InvalidDataException("V34 外景契约没有 6 个维修 owner。");
        }

        Vector2 ownerPivot = ReadVector2(registration?["owner_pivot"]);
        TideV34HouseExteriorCatalog.OwnerEntry[] owners =
            new TideV34HouseExteriorCatalog.OwnerEntry[ownersJson.Count];
        for (int i = 0; i < ownersJson.Count; i++)
        {
            JObject ownerJson = (JObject)ownersJson[i];
            JArray bbox = ownerJson["bbox_top_left"] as JArray;
            if (bbox == null || bbox.Count != 4)
            {
                throw new InvalidDataException($"V34 owner {i} 的 bbox_top_left 无效。");
            }

            TideV34HouseExteriorCatalog.OwnerEntry owner =
                new TideV34HouseExteriorCatalog.OwnerEntry();
            owner.Configure(
                ownerJson["key"]?.Value<string>(),
                ownerJson["name_zh"]?.Value<string>(),
                ownerJson["gameplay_owner"]?.Value<string>(),
                ImportSprite(ownerJson["damage"]?.Value<string>(), ownerPpu, ownerPivot),
                ImportSprite(ownerJson["repair"]?.Value<string>(), ownerPpu, ownerPivot),
                ReadVector2(ownerJson["world_offset_from_house_pivot"]),
                new Vector2Int(bbox[0].Value<int>(), bbox[1].Value<int>()),
                ReadVector2Int(ownerJson["canvas_size"]));
            owners[i] = owner;
        }

        EnsureFolder(CatalogFolder);
        TideV34HouseExteriorCatalog catalog =
            AssetDatabase.LoadAssetAtPath<TideV34HouseExteriorCatalog>(CatalogPath);
        if (catalog == null)
        {
            catalog = ScriptableObject.CreateInstance<TideV34HouseExteriorCatalog>();
            AssetDatabase.CreateAsset(catalog, CatalogPath);
        }

        catalog.Configure(version, stableBase, owners);
        EditorUtility.SetDirty(catalog);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        if (!catalog.IsComplete(out string reason))
        {
            throw new InvalidDataException("V34 外景索引生成后仍不完整：" + reason);
        }

        Debug.Log("PASS：V34 外景稳定底图与 6 个互斥维修 owner 已建立轻量索引；" +
            "V30 室内、V31 船和 V32 人物动画保持原所有权。");
    }

    private static Sprite ImportSprite(string assetPath, float pixelsPerUnit, Vector2 pivot)
    {
        if (string.IsNullOrWhiteSpace(assetPath) || !File.Exists(assetPath))
        {
            throw new FileNotFoundException("V34 正式资源不存在", assetPath);
        }

        string normalizedPath = assetPath.Replace('\\', '/');
        AssetDatabase.ImportAsset(normalizedPath, ImportAssetOptions.ForceSynchronousImport);
        TextureImporter importer = AssetImporter.GetAtPath(normalizedPath) as TextureImporter;
        if (importer == null)
        {
            throw new InvalidDataException("V34 PNG 不是可配置的 TextureImporter：" + normalizedPath);
        }

        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = pixelsPerUnit;
        importer.spritePivot = pivot;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.maxTextureSize = 4096;

        TextureImporterSettings settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.spriteAlignment = (int)SpriteAlignment.Custom;
        settings.spritePivot = pivot;
        settings.spriteMeshType = SpriteMeshType.FullRect;
        importer.SetTextureSettings(settings);
        importer.SaveAndReimport();

        Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(normalizedPath);
        if (sprite == null)
        {
            throw new InvalidDataException("V34 Sprite 导入失败：" + normalizedPath);
        }

        return sprite;
    }

    private static Vector2 ReadVector2(JToken token)
    {
        JArray values = token as JArray;
        if (values == null || values.Count != 2)
        {
            throw new InvalidDataException("V34 Vector2 契约字段无效。");
        }

        return new Vector2(values[0].Value<float>(), values[1].Value<float>());
    }

    private static Vector2Int ReadVector2Int(JToken token)
    {
        JArray values = token as JArray;
        if (values == null || values.Count != 2)
        {
            throw new InvalidDataException("V34 Vector2Int 契约字段无效。");
        }

        return new Vector2Int(values[0].Value<int>(), values[1].Value<int>());
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
