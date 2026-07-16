using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 从 V69 权威契约生成按当前阶段加载的 Resources 索引。
/// PNG 仍留在美术目录；这里不复制纹理，也不从 Alpha 反推碰撞或路径。
/// </summary>
public static class TideV69HouseRepairRuntimeAssetBuilder
{
    private const string SourceRoot =
        "Assets/Art/GeneratedAI/ProductionHouseStructuralRepairStagingV69";
    private const string ContractPath = SourceRoot + "/runtime-contract.json";
    private const string ResourceRoot = "Assets/Resources/StiltFirstSliceAI/V69House";
    private const string StructuralFolder = ResourceRoot + "/Structural";
    private const string BinaryFolder = ResourceRoot + "/Binary";

    [MenuItem("Tide/Resources/Rebuild V69 House Repair Runtime")]
    public static void RebuildRuntimeAssets()
    {
        if (!File.Exists(ContractPath))
        {
            throw new FileNotFoundException("V69 高脚屋维修契约不存在。", ContractPath);
        }

        JObject contract = JObject.Parse(File.ReadAllText(ContractPath));
        if (!string.Equals(contract["version"]?.Value<string>(), "V69", StringComparison.Ordinal))
        {
            throw new InvalidDataException("V69 契约版本字段无效。");
        }

        JObject profiles = contract["profiles"] as JObject;
        JObject exterior = profiles?["Exterior"] as JObject;
        JObject interior = profiles?["Interior"] as JObject;
        if (exterior == null || interior == null)
        {
            throw new InvalidDataException("V69 契约缺少 Exterior/Interior 配置。");
        }

        EnsureFolder(ResourceRoot);
        EnsureFolder(StructuralFolder);
        EnsureFolder(StructuralFolder + "/Exterior");
        EnsureFolder(StructuralFolder + "/Interior");
        EnsureFolder(BinaryFolder);
        DeleteLegacyPairedStructuralIndexes();

        BuildProfileBase(TideV69HouseProfile.Exterior, exterior);
        BuildProfileBase(TideV69HouseProfile.Interior, interior);

        Dictionary<string, JObject> exteriorStructural = ReadOwners(
            exterior,
            "structural_six_stage",
            TideV69HouseRepairPresentationModel.StructuralOwnerCount);
        Dictionary<string, JObject> interiorStructural = ReadOwners(
            interior,
            "structural_six_stage",
            TideV69HouseRepairPresentationModel.StructuralOwnerCount);

        foreach (TideV69HouseStructuralOwner owner in Enum.GetValues(typeof(TideV69HouseStructuralOwner)))
        {
            string semanticKey = owner.ToString();
            JObject exteriorOwner = GetRequiredOwner(exteriorStructural, semanticKey, "外景结构");
            JObject interiorOwner = GetRequiredOwner(interiorStructural, semanticKey, "室内结构");
            BuildStructuralStages(TideV69HouseProfile.Exterior, owner, exterior, exteriorOwner);
            BuildStructuralStages(TideV69HouseProfile.Interior, owner, interior, interiorOwner);
        }

        Dictionary<string, JObject> binaryOwners = ReadOwners(
            interior,
            "binary_passthrough",
            TideV69HouseRepairPresentationModel.BinaryOwnerCount);
        foreach (TideV69HouseBinaryOwner owner in Enum.GetValues(typeof(TideV69HouseBinaryOwner)))
        {
            BuildBinaryStages(owner, interior, GetRequiredOwner(binaryOwners, owner.ToString(), "室内设备"));
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("PASS：V69 两张稳定底、72 个按视图独占的结构阶段与 12 个设备二态索引已生成；" +
            "V38 路径、碰撞和现有维修账本未改。");
    }

    private static void BuildProfileBase(TideV69HouseProfile profile, JObject profileJson)
    {
        Sprite stable = ImportSprite(
            profileJson["stable_base"]?.Value<string>(),
            profileJson["stable_ppu"]?.Value<float>() ?? TideV69HouseRepairPresentationModel.PixelsPerUnit,
            ReadVector2(profileJson["stable_pivot"], profile + " stable_pivot"));
        string path = ResourceRoot + "/" + profile + "Base.asset";
        TideV69HouseProfileBaseAsset asset = LoadOrCreate<TideV69HouseProfileBaseAsset>(path);
        asset.Configure(profile, stable);
        EditorUtility.SetDirty(asset);
        if (!asset.IsComplete(out string reason))
        {
            throw new InvalidDataException(profile + " 稳定底索引无效：" + reason);
        }
    }

    private static void BuildStructuralStages(
        TideV69HouseProfile profile,
        TideV69HouseStructuralOwner owner,
        JObject profileJson,
        JObject ownerJson)
    {
        Vector2 offset = ReadVector2(
            ownerJson["world_offset_from_house_pivot"],
            owner + " " + profile + " offset");
        Vector2 pivot = ReadVector2(profileJson["owner_pivot"], profile + " owner_pivot");
        float ppu = profileJson["owner_ppu"]?.Value<float>() ?? 256f;
        JObject stages = ownerJson["stages"] as JObject;

        foreach (TideV69HouseRepairStage stage in Enum.GetValues(typeof(TideV69HouseRepairStage)))
        {
            string stageName = stage.ToString();
            Sprite sprite = ImportSprite(stages?[stageName]?.Value<string>(), ppu, pivot);
            // 文件名需要和运行 Resources 路径完全一致，避免编辑器端和运行端各写一套映射。
            string path = StructuralFolder + "/" + profile + "/" + owner + "_" +
                ((int)stage).ToString("00") + "_" + stageName + ".asset";
            TideV69HouseStructuralStageAsset asset =
                LoadOrCreate<TideV69HouseStructuralStageAsset>(path);
            asset.Configure(profile, owner, stage, sprite, offset);
            EditorUtility.SetDirty(asset);
            if (!asset.IsComplete(out string reason))
            {
                throw new InvalidDataException(owner + "/" + stage + " 结构索引无效：" + reason);
            }
        }
    }

    private static void BuildBinaryStages(
        TideV69HouseBinaryOwner owner,
        JObject interiorProfile,
        JObject ownerJson)
    {
        JObject stages = ownerJson["stages"] as JObject;
        Vector2 pivot = ReadVector2(interiorProfile["owner_pivot"], "Interior owner_pivot");
        Vector2 offset = ReadVector2(
            ownerJson["world_offset_from_house_pivot"],
            owner + " offset");
        float ppu = interiorProfile["owner_ppu"]?.Value<float>() ?? 256f;
        bool[] values = { false, true };
        for (int i = 0; i < values.Length; i++)
        {
            bool serviceable = values[i];
            TideV69HouseRepairStage stage = serviceable
                ? TideV69HouseRepairStage.Serviceable
                : TideV69HouseRepairStage.Damage;
            Sprite sprite = ImportSprite(stages?[stage.ToString()]?.Value<string>(), ppu, pivot);
            string path = BinaryFolder + "/" + owner + "_" + ((int)stage).ToString("00") +
                "_" + stage + ".asset";
            TideV69HouseBinaryStageAsset asset = LoadOrCreate<TideV69HouseBinaryStageAsset>(path);
            asset.Configure(owner, serviceable, sprite, offset);
            EditorUtility.SetDirty(asset);
            if (!asset.IsComplete(out string reason))
            {
                throw new InvalidDataException(owner + "/" + stage + " 设备索引无效：" + reason);
            }
        }
    }

    private static Dictionary<string, JObject> ReadOwners(
        JObject profile,
        string stageMode,
        int expectedCount)
    {
        JArray owners = profile["owners"] as JArray;
        Dictionary<string, JObject> result = new Dictionary<string, JObject>(StringComparer.Ordinal);
        if (owners != null)
        {
            for (int i = 0; i < owners.Count; i++)
            {
                JObject owner = owners[i] as JObject;
                if (!string.Equals(owner?["stage_mode"]?.Value<string>(), stageMode, StringComparison.Ordinal))
                {
                    continue;
                }

                string key = stageMode == "structural_six_stage"
                    ? owner["semantic_key"]?.Value<string>()
                    : owner["key"]?.Value<string>();
                if (string.IsNullOrWhiteSpace(key) || result.ContainsKey(key))
                {
                    throw new InvalidDataException("V69 owner key 缺失或重复：" + key);
                }
                result.Add(key, owner);
            }
        }

        if (result.Count != expectedCount)
        {
            throw new InvalidDataException(stageMode + " owner 数量应为 " + expectedCount +
                "，实际为 " + result.Count + "。");
        }
        return result;
    }

    private static JObject GetRequiredOwner(
        Dictionary<string, JObject> owners,
        string key,
        string label)
    {
        if (!owners.TryGetValue(key, out JObject owner))
        {
            throw new InvalidDataException(label + "缺少 owner：" + key);
        }
        return owner;
    }

    private static Sprite ImportSprite(string path, float ppu, Vector2 pivot)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            throw new FileNotFoundException("V69 运行 Sprite 不存在。", path);
        }

        string normalized = path.Replace('\\', '/');
        AssetDatabase.ImportAsset(normalized, ImportAssetOptions.ForceSynchronousImport);
        TextureImporter importer = AssetImporter.GetAtPath(normalized) as TextureImporter;
        if (importer == null)
        {
            throw new InvalidDataException("V69 PNG 无法作为 TextureImporter 配置：" + normalized);
        }

        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = ppu;
        importer.spritePivot = pivot;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = true;
        importer.mipMapsPreserveCoverage = true;
        importer.borderMipmap = true;
        importer.filterMode = FilterMode.Bilinear;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.npotScale = TextureImporterNPOTScale.None;
        // 86 张阶段图必须进入最终构建。使用 Unity 的高质量平台压缩，把具体
        // 桌面格式交给目标平台设置；禁止把整套 RGBA32 原图原样塞进构建。
        importer.textureCompression = TextureImporterCompression.CompressedHQ;
        importer.maxTextureSize = 2048;

        TextureImporterSettings settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.spriteAlignment = (int)SpriteAlignment.Custom;
        settings.spritePivot = pivot;
        settings.spriteMeshType = SpriteMeshType.FullRect;
        importer.SetTextureSettings(settings);
        importer.SaveAndReimport();

        Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(normalized);
        if (sprite == null)
        {
            throw new InvalidDataException("V69 Sprite 导入失败：" + normalized);
        }
        return sprite;
    }

    private static T LoadOrCreate<T>(string path) where T : ScriptableObject
    {
        T asset = AssetDatabase.LoadAssetAtPath<T>(path);
        if (asset != null)
        {
            return asset;
        }
        if (File.Exists(path))
        {
            AssetDatabase.DeleteAsset(path);
        }

        asset = ScriptableObject.CreateInstance<T>();
        AssetDatabase.CreateAsset(asset, path);
        return asset;
    }

    private static void DeleteLegacyPairedStructuralIndexes()
    {
        string[] guids = AssetDatabase.FindAssets(
            "t:TideV69HouseStructuralStageAsset",
            new[] { StructuralFolder });
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]).Replace('\\', '/');
            string directory = Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (string.Equals(directory, StructuralFolder, StringComparison.Ordinal))
            {
                AssetDatabase.DeleteAsset(path);
            }
        }
    }

    private static Vector2 ReadVector2(JToken token, string label)
    {
        JArray values = token as JArray;
        if (values == null || values.Count != 2)
        {
            throw new InvalidDataException("V69 " + label + " 不是 Vector2。");
        }
        return new Vector2(values[0].Value<float>(), values[1].Value<float>());
    }

    private static void EnsureFolder(string path)
    {
        string[] parts = path.Split('/');
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
