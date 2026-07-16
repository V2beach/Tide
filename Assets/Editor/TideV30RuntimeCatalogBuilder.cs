using System;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 从 V30 运行契约生成一个轻量 Resources 索引。
/// 正式 PNG 和它们的 GUID 保持在资源会话目录；这里只建立引用，不复制纹理。
/// </summary>
public static class TideV30RuntimeCatalogBuilder
{
    private const string SourceRoot = "Assets/Art/GeneratedAI/ProductionHouseR03ReturnV30";
    private const string ContractPath = SourceRoot + "/house-v30-runtime-contract.json";
    private const string CatalogFolder = "Assets/Resources/StiltFirstSliceAI";
    private const string CatalogPath = CatalogFolder + "/V30HouseRuntimeCatalog.asset";

    [MenuItem("Tide/Resources/Rebuild V30 House Runtime Catalog")]
    public static void RebuildCatalog()
    {
        if (!File.Exists(ContractPath))
        {
            throw new FileNotFoundException("V30 运行契约不存在", ContractPath);
        }

        JObject contract = JObject.Parse(File.ReadAllText(ContractPath));
        Sprite[] exteriorFrames = LoadFrameSet(
            contract["profiles"]?["ExteriorAnimated"]?["path_pattern"]?.Value<string>(),
            TideV30HousePresentationModel.ExteriorFrameCount);
        Sprite[] interiorFrames = LoadFrameSet(
            contract["profiles"]?["InteriorRepairedAnimated"]?["path_pattern"]?.Value<string>(),
            TideV30HousePresentationModel.InteriorFrameCount);

        Sprite exteriorNoCloth = LoadRelativeSprite(
            contract["profiles"]?["ExteriorNoCloth"]?["path"]?.Value<string>());
        Sprite interiorFound = LoadRelativeSprite(
            contract["profiles"]?["InteriorFound"]?["path"]?.Value<string>());
        Sprite interiorClean = LoadRelativeSprite(
            contract["profiles"]?["InteriorClean"]?["path"]?.Value<string>());
        Sprite stableBase = LoadSprite(SourceRoot + "/Runtime/Repair/TideHouseV30_StableBase_1536.png");

        JArray ownersJson = contract["repair"]?["owners"] as JArray;
        if (ownersJson == null || ownersJson.Count != TideV30HousePresentationModel.RepairOwnerCount)
        {
            throw new InvalidDataException("V30 运行契约没有 12 个 repair owner。");
        }

        TideV30HouseRuntimeCatalog.RepairOwnerEntry[] owners =
            new TideV30HouseRuntimeCatalog.RepairOwnerEntry[ownersJson.Count];
        for (int i = 0; i < ownersJson.Count; i++)
        {
            JObject ownerJson = (JObject)ownersJson[i];
            JObject damageJson = (JObject)ownerJson["states"]?["damage"];
            JObject repairJson = (JObject)ownerJson["states"]?["repair"];
            if (damageJson == null || repairJson == null)
            {
                throw new InvalidDataException($"repair owner {i} 缺少 Damage/Repair 状态。");
            }

            TideV30HouseRuntimeCatalog.RepairOwnerEntry owner =
                new TideV30HouseRuntimeCatalog.RepairOwnerEntry();
            owner.Configure(
                ownerJson["key"]?.Value<string>(),
                ownerJson["name_zh"]?.Value<string>(),
                LoadSprite(damageJson["path"]?.Value<string>()),
                LoadSprite(repairJson["path"]?.Value<string>()),
                ReadVector2(damageJson["world_offset_from_house_pivot"]),
                ReadVector2Int(damageJson["origin_top_left"]),
                ReadVector2Int(damageJson["size"]));
            owners[i] = owner;
        }

        EnsureFolder(CatalogFolder);
        TideV30HouseRuntimeCatalog catalog =
            AssetDatabase.LoadAssetAtPath<TideV30HouseRuntimeCatalog>(CatalogPath);
        if (catalog == null)
        {
            catalog = ScriptableObject.CreateInstance<TideV30HouseRuntimeCatalog>();
            AssetDatabase.CreateAsset(catalog, CatalogPath);
        }

        catalog.Configure(
            contract["version"]?.Value<int>() ?? 0,
            exteriorFrames,
            exteriorNoCloth,
            interiorFound,
            interiorClean,
            interiorFrames,
            stableBase,
            owners);
        EditorUtility.SetDirty(catalog);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        if (!catalog.IsComplete(out string reason))
        {
            throw new InvalidDataException("V30 运行索引生成后仍不完整：" + reason);
        }

        Debug.Log($"PASS：V30 高脚屋运行索引已生成，外景 {exteriorFrames.Length} 帧、" +
            $"室内 {interiorFrames.Length} 帧、维修 owner {owners.Length} 组；未复制正式 PNG。 path={CatalogPath}");
    }

    public static void RebuildCatalogBatch()
    {
        RebuildCatalog();
    }

    private static Sprite[] LoadFrameSet(string pathPattern, int count)
    {
        if (string.IsNullOrWhiteSpace(pathPattern) || !pathPattern.Contains("##"))
        {
            throw new InvalidDataException("V30 帧路径模板无效：" + pathPattern);
        }

        Sprite[] frames = new Sprite[count];
        for (int i = 0; i < count; i++)
        {
            frames[i] = LoadRelativeSprite(pathPattern.Replace("##", i.ToString("00")));
        }
        return frames;
    }

    private static Sprite LoadRelativeSprite(string relativePath)
    {
        return LoadSprite(SourceRoot + "/" + relativePath);
    }

    private static Sprite LoadSprite(string assetPath)
    {
        if (string.IsNullOrWhiteSpace(assetPath))
        {
            throw new InvalidDataException("V30 Sprite 路径为空。");
        }

        string normalizedPath = assetPath.Replace('\\', '/');
        Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(normalizedPath);
        if (sprite == null)
        {
            throw new FileNotFoundException("无法按 Sprite 导入设置加载 V30 资源", normalizedPath);
        }
        return sprite;
    }

    private static Vector2 ReadVector2(JToken token)
    {
        JArray values = token as JArray;
        if (values == null || values.Count != 2)
        {
            throw new InvalidDataException("V30 Vector2 契约字段无效。");
        }
        return new Vector2(values[0].Value<float>(), values[1].Value<float>());
    }

    private static Vector2Int ReadVector2Int(JToken token)
    {
        JArray values = token as JArray;
        if (values == null || values.Count != 2)
        {
            throw new InvalidDataException("V30 Vector2Int 契约字段无效。");
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
