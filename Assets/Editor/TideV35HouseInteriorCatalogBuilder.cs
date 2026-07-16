using System.IO;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 从 V35 维修契约导入稳定底图和十二组裁切状态，并建立 Resources 轻量索引。
/// 正式 PNG 保留在美术目录；Catalog 只保存引用、语义归属与可复算配准数据。
/// </summary>
public static class TideV35HouseInteriorCatalogBuilder
{
    private const string SourceRoot = "Assets/Art/GeneratedAI/ProductionStiltDockV35";
    private const string ContractPath = SourceRoot + "/tide-v35-interior-repair-contract.json";
    private const string CatalogFolder = "Assets/Resources/StiltFirstSliceAI";
    private const string CatalogPath = CatalogFolder + "/V35HouseInteriorCatalog.asset";

    [MenuItem("Tide/Resources/Rebuild V35 House Interior Catalog")]
    public static void RebuildCatalog()
    {
        if (!File.Exists(ContractPath))
        {
            throw new FileNotFoundException("V35 室内维修契约不存在", ContractPath);
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
        if (ownersJson == null || ownersJson.Count != TideV35HouseInteriorPresentationModel.OwnerCount)
        {
            throw new InvalidDataException("V35 室内契约没有 12 个维修 owner。");
        }

        Vector2 ownerPivot = ReadVector2(registration?["owner_pivot"]);
        TideV35HouseInteriorCatalog.OwnerEntry[] owners =
            new TideV35HouseInteriorCatalog.OwnerEntry[ownersJson.Count];
        for (int i = 0; i < ownersJson.Count; i++)
        {
            JObject ownerJson = (JObject)ownersJson[i];
            JArray bbox = ownerJson["bbox_top_left"] as JArray;
            if (bbox == null || bbox.Count != 4)
            {
                throw new InvalidDataException($"V35 owner {i} 的 bbox_top_left 无效。");
            }

            TideV35HouseInteriorCatalog.OwnerEntry owner =
                new TideV35HouseInteriorCatalog.OwnerEntry();
            owner.Configure(
                ownerJson["key"]?.Value<string>(),
                ownerJson["name_zh"]?.Value<string>(),
                ownerJson["gameplay_owner"]?.Value<string>(),
                ownerJson["required_step"]?.Value<int>() ?? 0,
                ImportSprite(ownerJson["damage"]?.Value<string>(), ownerPpu, ownerPivot),
                ImportSprite(ownerJson["repair"]?.Value<string>(), ownerPpu, ownerPivot),
                ReadVector2(ownerJson["world_offset_from_house_pivot"]),
                new Vector2Int(bbox[0].Value<int>(), bbox[1].Value<int>()),
                ReadVector2Int(ownerJson["canvas_size"]));
            owners[i] = owner;
        }

        EnsureFolder(CatalogFolder);
        TideV35HouseInteriorCatalog catalog =
            AssetDatabase.LoadAssetAtPath<TideV35HouseInteriorCatalog>(CatalogPath);
        if (catalog == null)
        {
            catalog = ScriptableObject.CreateInstance<TideV35HouseInteriorCatalog>();
            AssetDatabase.CreateAsset(catalog, CatalogPath);
        }

        catalog.Configure(version, stableBase, owners);
        EditorUtility.SetDirty(catalog);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        if (!catalog.IsComplete(out string reason))
        {
            throw new InvalidDataException("V35 室内索引生成后仍不完整：" + reason);
        }

        Debug.Log("PASS：V35 同外壳室内稳定底图与 12 个互斥维修 owner 已建立轻量索引；" +
            "V34 外景、V31 船和 V32 完整人物分层保持原所有权。");
    }

    private static Sprite ImportSprite(string assetPath, float pixelsPerUnit, Vector2 pivot)
    {
        if (string.IsNullOrWhiteSpace(assetPath) || !File.Exists(assetPath))
        {
            throw new FileNotFoundException("V35 正式资源不存在", assetPath);
        }

        string normalizedPath = assetPath.Replace('\\', '/');
        AssetDatabase.ImportAsset(normalizedPath, ImportAssetOptions.ForceSynchronousImport);
        TextureImporter importer = AssetImporter.GetAtPath(normalizedPath) as TextureImporter;
        if (importer == null)
        {
            throw new InvalidDataException("V35 PNG 不是可配置的 TextureImporter：" + normalizedPath);
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
            throw new InvalidDataException("V35 Sprite 导入失败：" + normalizedPath);
        }

        return sprite;
    }

    private static Vector2 ReadVector2(JToken token)
    {
        JArray values = token as JArray;
        if (values == null || values.Count != 2)
        {
            throw new InvalidDataException("V35 Vector2 契约字段无效。");
        }

        return new Vector2(values[0].Value<float>(), values[1].Value<float>());
    }

    private static Vector2Int ReadVector2Int(JToken token)
    {
        JArray values = token as JArray;
        if (values == null || values.Count != 2)
        {
            throw new InvalidDataException("V35 Vector2Int 契约字段无效。");
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
