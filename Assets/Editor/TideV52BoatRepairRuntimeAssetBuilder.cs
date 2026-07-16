using System;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 从 V67 的权威契约生成 V52 轻量运行索引。
///
/// 每个施工阶段单独成为一个 Resources 资产，PNG 仍留在正式资源目录中。运行时
/// 因而可以只加载船壳、帆、舱底各自当前的一张图，而不是进场就引用十八张 owner。
/// </summary>
public static class TideV52BoatRepairRuntimeAssetBuilder
{
    private const string ContractPath =
        "Assets/Art/GeneratedAI/ProductionBoatEdgeMatteRefinementV67/runtime-contract.json";
    private const string CatalogFolder = "Assets/Resources/StiltFirstSliceAI/V52BoatRepair";
    private const string BaseAssetPath = CatalogFolder + "/V67BalancedBase.asset";

    private static readonly string[] StageKeys =
    {
        "Damage",
        "Cleared",
        "TestFit",
        "Fastened",
        "Sealed",
        "Serviceable"
    };

    [MenuItem("Tide/Resources/Rebuild V67 V52 Boat Repair Runtime Assets")]
    public static void RebuildCatalog()
    {
        if (!File.Exists(ContractPath))
        {
            throw new FileNotFoundException("V67 船体清边契约不存在", ContractPath);
        }

        JObject contract = JObject.Parse(File.ReadAllText(ContractPath));
        if (!string.Equals(contract["version"]?.Value<string>(), "V67", StringComparison.Ordinal))
        {
            throw new InvalidDataException("船体维修运行索引只能从 V67 契约生成。");
        }

        JObject profile = RequireObject(contract["profile"], "profile");
        RequireVector2Int(profile["canvas"], "profile.canvas", new Vector2Int(2304, 1536));
        RequireFloat(profile["pixels_per_unit"], "profile.pixels_per_unit", 192f);
        JObject v52 = RequireObject(profile["v52"], "profile.v52");
        JObject bases = RequireObject(v52["bases"], "profile.v52.bases");
        JObject owners = RequireObject(v52["owners"], "profile.v52.owners");

        // V39 与 V52 必须是同一份 V67 Balanced 边缘处理。这里先验证并统一
        // Importer，避免场景随后仍然引用旧 V46 或因 Tight 网格产生透明菱形洞。
        ConfigureV67V39Importers(RequireObject(profile["v39"], "profile.v39"));
        EnsureFolder(CatalogFolder);

        JObject gunwaleJson = RequireObject(bases["FrontGunwaleStable"], "v52.bases.FrontGunwaleStable");
        JObject floorJson = RequireObject(bases["CockpitFloorStable"], "v52.bases.CockpitFloorStable");
        TideV52BoatRepairBaseAsset baseAsset =
            AssetDatabase.LoadAssetAtPath<TideV52BoatRepairBaseAsset>(BaseAssetPath);
        if (baseAsset != null && !HasSerializedScriptReference(baseAsset))
        {
            AssetDatabase.DeleteAsset(BaseAssetPath);
            baseAsset = null;
        }
        if (baseAsset == null)
        {
            baseAsset = ScriptableObject.CreateInstance<TideV52BoatRepairBaseAsset>();
            AssetDatabase.CreateAsset(baseAsset, BaseAssetPath);
        }

        baseAsset.Configure(
            TideV52BoatRepairPresentationModel.CatalogVersion,
            "Balanced",
            LoadV67Sprite(RequireString(gunwaleJson["path"], "FrontGunwaleStable.path")),
            ReadVector2(gunwaleJson["world_offset_from_boat_pivot"], "FrontGunwaleStable.offset"),
            LoadV67Sprite(RequireString(floorJson["path"], "CockpitFloorStable.path")),
            ReadVector2(floorJson["world_offset_from_boat_pivot"], "CockpitFloorStable.offset"));
        EditorUtility.SetDirty(baseAsset);

        int stageAssetCount = 0;
        for (int ownerIndex = 0; ownerIndex < TideV52BoatRepairPresentationModel.OwnerCount; ownerIndex++)
        {
            TideV52BoatRepairOwner owner = (TideV52BoatRepairOwner)ownerIndex;
            JObject ownerJson = RequireObject(owners[owner.ToString()], $"v52.owners.{owner}");
            for (int stageIndex = 0; stageIndex < StageKeys.Length; stageIndex++)
            {
                TideV52BoatRepairStage stage = (TideV52BoatRepairStage)stageIndex;
                string stageKey = StageKeys[stageIndex];
                JObject stageJson = RequireObject(ownerJson[stageKey], $"v52.owners.{owner}.{stageKey}");
                string assetPath = $"{CatalogFolder}/{owner}_{stageIndex:00}_{stageKey}.asset";
                TideV52BoatRepairStageAsset stageAsset =
                    AssetDatabase.LoadAssetAtPath<TideV52BoatRepairStageAsset>(assetPath);
                if (stageAsset != null && !HasSerializedScriptReference(stageAsset))
                {
                    // 早期脚本文件名不匹配时 Unity 会留下 m_Script=0 的 YAML。
                    // 这种对象即使能按 EditorClassIdentifier 暂时加载，也不能进入构建。
                    AssetDatabase.DeleteAsset(assetPath);
                    stageAsset = null;
                }
                if (stageAsset == null)
                {
                    stageAsset = ScriptableObject.CreateInstance<TideV52BoatRepairStageAsset>();
                    AssetDatabase.CreateAsset(stageAsset, assetPath);
                }

                stageAsset.Configure(
                    TideV52BoatRepairPresentationModel.CatalogVersion,
                    "Balanced",
                    owner,
                    stage,
                    LoadV67Sprite(RequireString(stageJson["path"], $"{owner}.{stageKey}.path")),
                    ReadVector2(stageJson["world_offset_from_boat_pivot"], $"{owner}.{stageKey}.offset"));
                EditorUtility.SetDirty(stageAsset);
                if (!stageAsset.IsComplete(out string stageReason))
                {
                    throw new InvalidDataException($"V52 阶段索引不完整：{stageReason}");
                }
                stageAssetCount++;
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        if (!baseAsset.IsComplete(out string baseReason))
        {
            throw new InvalidDataException("V52 稳定底索引不完整：" + baseReason);
        }

        Debug.Log($"PASS：V67/V52 Balanced 运行索引已生成；稳定底 2 张，" +
            $"独立阶段资产 {stageAssetCount} 个。运行控制器默认只持有每个 owner 当前阶段。");
    }

    private static bool HasSerializedScriptReference(UnityEngine.Object asset)
    {
        SerializedObject serialized = new SerializedObject(asset);
        SerializedProperty script = serialized.FindProperty("m_Script");
        return script != null && script.objectReferenceValue != null;
    }

    public static void RebuildCatalogBatch()
    {
        RebuildCatalog();
    }

    private static void ConfigureV67V39Importers(JObject v39)
    {
        string[] states = { "ServiceableDamaged", "Repaired" };
        string[] layers = { "BackRig", "SailRest", "BackHull", "CockpitFloor", "FrontGunwale", "RudderRest" };
        for (int stateIndex = 0; stateIndex < states.Length; stateIndex++)
        {
            JObject stateJson = RequireObject(v39[states[stateIndex]], $"profile.v39.{states[stateIndex]}");
            for (int layerIndex = 0; layerIndex < layers.Length; layerIndex++)
            {
                JObject layerJson = RequireObject(
                    stateJson[layers[layerIndex]],
                    $"profile.v39.{states[stateIndex]}.{layers[layerIndex]}");
                LoadV67Sprite(RequireString(layerJson["path"], $"{layers[layerIndex]}.path"));
            }
        }
    }

    private static Sprite LoadV67Sprite(string assetPath)
    {
        string normalizedPath = assetPath.Replace('\\', '/');
        TextureImporter importer = AssetImporter.GetAtPath(normalizedPath) as TextureImporter;
        if (importer == null)
        {
            throw new InvalidDataException("V67 PNG 不是可配置的 TextureImporter：" + normalizedPath);
        }

        TextureImporterSettings settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        bool settingsChanged = settings.spriteMeshType != SpriteMeshType.FullRect;
        settings.spriteMeshType = SpriteMeshType.FullRect;
        importer.SetTextureSettings(settings);

        bool importerChanged = importer.textureType != TextureImporterType.Sprite ||
            importer.spriteImportMode != SpriteImportMode.Single ||
            Mathf.Abs(importer.spritePixelsPerUnit - TideV52BoatRepairPresentationModel.PixelsPerUnit) > 0.01f ||
            importer.filterMode != FilterMode.Bilinear ||
            importer.wrapMode != TextureWrapMode.Clamp ||
            !importer.mipmapEnabled ||
            !importer.borderMipmap ||
            !importer.mipMapsPreserveCoverage ||
            importer.maxTextureSize != 2048 ||
            importer.textureCompression != TextureImporterCompression.Uncompressed;
        if (settingsChanged || importerChanged)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = TideV52BoatRepairPresentationModel.PixelsPerUnit;
            importer.filterMode = FilterMode.Bilinear;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.mipmapEnabled = true;
            importer.borderMipmap = true;
            importer.mipMapsPreserveCoverage = true;
            importer.alphaIsTransparency = true;
            importer.maxTextureSize = 2048;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }

        Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(normalizedPath);
        if (sprite == null)
        {
            throw new FileNotFoundException("无法加载 V67 Sprite", normalizedPath);
        }
        return sprite;
    }

    private static JObject RequireObject(JToken token, string fieldName)
    {
        JObject value = token as JObject;
        if (value == null)
        {
            throw new InvalidDataException($"V67 契约字段 {fieldName} 不是对象或不存在。");
        }
        return value;
    }

    private static string RequireString(JToken token, string fieldName)
    {
        string value = token?.Value<string>();
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidDataException($"V67 契约字段 {fieldName} 为空。");
        }
        return value;
    }

    private static Vector2 ReadVector2(JToken token, string fieldName)
    {
        JArray values = token as JArray;
        if (values == null || values.Count != 2)
        {
            throw new InvalidDataException($"V67 契约字段 {fieldName} 必须是二维数组。");
        }
        return new Vector2(values[0].Value<float>(), values[1].Value<float>());
    }

    private static void RequireVector2Int(JToken token, string fieldName, Vector2Int expected)
    {
        JArray values = token as JArray;
        if (values == null || values.Count != 2 ||
            values[0].Value<int>() != expected.x || values[1].Value<int>() != expected.y)
        {
            throw new InvalidDataException($"V67 契约字段 {fieldName} 不是预期的 {expected.x}x{expected.y}。");
        }
    }

    private static void RequireFloat(JToken token, string fieldName, float expected)
    {
        float value = token?.Value<float>() ?? float.NaN;
        if (float.IsNaN(value) || Mathf.Abs(value - expected) > 0.01f)
        {
            throw new InvalidDataException($"V67 契约字段 {fieldName} 不是预期的 {expected}。");
        }
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
