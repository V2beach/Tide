using System.IO;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 从 V31 船体/乘员契约生成轻量 Resources 索引。
/// 正式 PNG 继续留在 ProductionBoatPassengerLayerV31，Builder 只建立 Sprite 引用。
/// </summary>
public static class TideV31BoatRuntimeCatalogBuilder
{
    private const string SourceRoot = "Assets/Art/GeneratedAI/ProductionBoatPassengerLayerV31";
    private const string ContractPath = SourceRoot + "/boat-v31-passenger-contract.json";
    // V21 Found 端点把五块规则透明洞直接当作破损，Game 视图会像调试标记。
    // V20 B12 与 V21 保持同一画布、Pivot 和船身份，提供完整船壳与撕裂帆，
    // 因而先作为运行发现态；更细的船壳腐损等资源侧交付后再替换此引用。
    private const string FoundBasePath =
        "Assets/Art/GeneratedAI/ProductionR03R06WorldMatchV20/Runtime/Boat/States18/TideBoatV20_B12_破帆试挂_6K.png";
    // B12 是同一画布上的可航中间态：船壳已封住规则破口，桅杆完整，帆仍有
    // 大块不规则撕裂。它只在短航视图使用，既避免无帆航行，也不冒充完全修复端点。
    private const string SailableBasePath =
        "Assets/Art/GeneratedAI/ProductionR03R06WorldMatchV20/Runtime/Boat/States18/TideBoatV20_B12_破帆试挂_6K.png";
    private const string RepairedBasePath =
        "Assets/Art/GeneratedAI/ProductionBoatR03WorldMatchV21/Runtime/Boat/Canonical/TideBoatV21_RepairedCanonical_6K.png";
    private const string CatalogFolder = "Assets/Resources/StiltFirstSliceAI";
    private const string CatalogPath = CatalogFolder + "/V31BoatRuntimeCatalog.asset";

    private static readonly string[] FoundLayerKeys =
    {
        "CockpitBack",
        "FrontGunwale"
    };

    private static readonly string[] RepairedLayerKeys =
    {
        "CockpitBack",
        "FrontGunwale",
        "BackRig",
        "SailRest",
        "RudderRest"
    };

    private static readonly string[] WaterlineKeys =
    {
        "Calm",
        "Loaded",
        "Storm"
    };

    [MenuItem("Tide/Resources/Rebuild V31 Boat Runtime Catalog")]
    public static void RebuildCatalog()
    {
        if (!File.Exists(ContractPath))
        {
            throw new FileNotFoundException("V31 船体/乘员契约不存在", ContractPath);
        }

        JObject contract = JObject.Parse(File.ReadAllText(ContractPath));
        JObject layers = RequireObject(contract["layers"], "layers");
        TideV31BoatRuntimeCatalog.LayerEntry[] foundLayers = LoadLayerSet(
            RequireObject(layers["Found"], "layers.Found"),
            FoundLayerKeys);
        TideV31BoatRuntimeCatalog.LayerEntry[] repairedLayers = LoadLayerSet(
            RequireObject(layers["Repaired"], "layers.Repaired"),
            RepairedLayerKeys);

        JObject passenger = RequireObject(contract["passenger"], "passenger");
        JArray passengerFrameJson = passenger["frames"] as JArray;
        if (passengerFrameJson == null ||
            passengerFrameJson.Count != TideV31BoatPresentationModel.PassengerFrameCount)
        {
            throw new InvalidDataException("V31 passenger.frames 必须正好包含六帧。");
        }

        int durationMs = passenger["duration_ms"]?.Value<int>() ?? 0;
        if (durationMs != 120)
        {
            throw new InvalidDataException($"V31 乘员帧时长是 {durationMs}ms，不是契约要求的 120ms。");
        }

        string passengerMotion = passenger["motion"]?.Value<string>();
        if (string.IsNullOrWhiteSpace(passengerMotion))
        {
            throw new InvalidDataException("V31 passenger.motion 为空。");
        }

        TideV31BoatRuntimeCatalog.LayerEntry[] passengerFrames =
            new TideV31BoatRuntimeCatalog.LayerEntry[passengerFrameJson.Count];
        for (int i = 0; i < passengerFrameJson.Count; i++)
        {
            passengerFrames[i] = LoadLayerEntry(
                RequireObject(passengerFrameJson[i], $"passenger.frames[{i}]"),
                $"{passengerMotion}_F{i:00}");
        }

        JObject waterlineJson = RequireObject(contract["waterline_masks"], "waterline_masks");
        TideV31BoatRuntimeCatalog.WaterlineMaskEntry[] waterlineMasks =
            new TideV31BoatRuntimeCatalog.WaterlineMaskEntry[WaterlineKeys.Length];
        for (int i = 0; i < WaterlineKeys.Length; i++)
        {
            string key = WaterlineKeys[i];
            JObject maskJson = RequireObject(waterlineJson[key], $"waterline_masks.{key}");
            TideV31BoatRuntimeCatalog.WaterlineMaskEntry mask =
                new TideV31BoatRuntimeCatalog.WaterlineMaskEntry();
            mask.Configure(
                key,
                LoadSprite(RequireString(maskJson["path"], $"waterline_masks.{key}.path")),
                RequireInt(maskJson["waterline_y_top_left"], $"waterline_masks.{key}.waterline_y_top_left"),
                ReadVector2(maskJson["world_offset_from_boat_pivot"], $"waterline_masks.{key}.world_offset_from_boat_pivot"),
                ReadVector2Int(maskJson["origin_top_left"], $"waterline_masks.{key}.origin_top_left"),
                ReadVector2Int(maskJson["size"], $"waterline_masks.{key}.size"));
            waterlineMasks[i] = mask;
        }

        JObject anchorsJson = RequireObject(contract["anchors_top_left"], "anchors_top_left");
        TideV31BoatRuntimeCatalog.ContractAnchorSet anchors =
            new TideV31BoatRuntimeCatalog.ContractAnchorSet();
        anchors.Configure(
            ReadVector2Int(anchorsJson["seat"], "anchors_top_left.seat"),
            ReadVector2Int(anchorsJson["tiller_hand"], "anchors_top_left.tiller_hand"),
            ReadVector2Int(anchorsJson["boarding_stern"], "anchors_top_left.boarding_stern"),
            ReadVector2Int(anchorsJson["cargo_hook"], "anchors_top_left.cargo_hook"),
            ReadVector2Int(anchorsJson["calm_waterline"], "anchors_top_left.calm_waterline"));

        EnsureFolder(CatalogFolder);
        TideV31BoatRuntimeCatalog catalog =
            AssetDatabase.LoadAssetAtPath<TideV31BoatRuntimeCatalog>(CatalogPath);
        if (catalog == null)
        {
            catalog = ScriptableObject.CreateInstance<TideV31BoatRuntimeCatalog>();
            AssetDatabase.CreateAsset(catalog, CatalogPath);
        }

        catalog.Configure(
            contract["version"]?.Value<int>() ?? 0,
            ReadVector2Int(contract["canvas"], "canvas"),
            ReadVector2(contract["pivot"], "pivot"),
            contract["pixels_per_unit"]?.Value<float>() ?? 0f,
            LoadSprite(FoundBasePath),
            LoadSprite(SailableBasePath),
            LoadSprite(RepairedBasePath),
            foundLayers,
            repairedLayers,
            passengerFrames,
            durationMs / 1000f,
            waterlineMasks,
            anchors);
        EditorUtility.SetDirty(catalog);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        if (!catalog.IsComplete(out string reason))
        {
            throw new InvalidDataException("V31 船体运行索引生成后仍不完整：" + reason);
        }

        Debug.Log($"PASS：V31 船体运行索引已生成，Sprite {catalog.ReferencedSpriteCount} 个，" +
            $"乘员 {passengerFrames.Length} 帧，水线遮罩 {waterlineMasks.Length} 张；" +
            $"未复制正式 PNG。 path={CatalogPath}");
    }

    public static void RebuildCatalogBatch()
    {
        RebuildCatalog();
    }

    private static TideV31BoatRuntimeCatalog.LayerEntry[] LoadLayerSet(
        JObject layerSetJson,
        string[] orderedKeys)
    {
        TideV31BoatRuntimeCatalog.LayerEntry[] entries =
            new TideV31BoatRuntimeCatalog.LayerEntry[orderedKeys.Length];
        for (int i = 0; i < orderedKeys.Length; i++)
        {
            string key = orderedKeys[i];
            entries[i] = LoadLayerEntry(
                RequireObject(layerSetJson[key], $"layers.{key}"),
                key);
        }
        return entries;
    }

    private static TideV31BoatRuntimeCatalog.LayerEntry LoadLayerEntry(
        JObject entryJson,
        string key)
    {
        TideV31BoatRuntimeCatalog.LayerEntry entry =
            new TideV31BoatRuntimeCatalog.LayerEntry();
        entry.Configure(
            key,
            LoadSprite(RequireString(entryJson["path"], $"{key}.path")),
            ReadVector2(entryJson["world_offset_from_boat_pivot"], $"{key}.world_offset_from_boat_pivot"),
            ReadVector2Int(entryJson["origin_top_left"], $"{key}.origin_top_left"),
            ReadVector2Int(entryJson["size"], $"{key}.size"));
        return entry;
    }

    private static Sprite LoadSprite(string assetPath)
    {
        string normalizedPath = assetPath.Replace('\\', '/');
        TextureImporter importer = AssetImporter.GetAtPath(normalizedPath) as TextureImporter;
        if (importer == null)
        {
            throw new InvalidDataException("V31 PNG 不是可配置的 TextureImporter：" + normalizedPath);
        }

        // 船体、帆和索具包含多个彼此分离的 Alpha 岛。Unity 的 Tight 网格会对这类
        // 大图做激进三角化，Game 视图里会出现源 PNG 中不存在的菱形缺口。FullRect
        // 让渲染几何忠实覆盖整张 Sprite，实际透明范围仍完全由 PNG Alpha 决定。
        TextureImporterSettings settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        if (settings.spriteMeshType != SpriteMeshType.FullRect)
        {
            settings.spriteMeshType = SpriteMeshType.FullRect;
            importer.SetTextureSettings(settings);
            importer.SaveAndReimport();
        }

        Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(normalizedPath);
        if (sprite == null)
        {
            throw new FileNotFoundException("无法按 Sprite 导入设置加载 V31 资源", normalizedPath);
        }
        return sprite;
    }

    private static JObject RequireObject(JToken token, string fieldName)
    {
        JObject value = token as JObject;
        if (value == null)
        {
            throw new InvalidDataException($"V31 契约字段 {fieldName} 不是对象或不存在。");
        }
        return value;
    }

    private static string RequireString(JToken token, string fieldName)
    {
        string value = token?.Value<string>();
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidDataException($"V31 契约字段 {fieldName} 为空。");
        }
        return value;
    }

    private static int RequireInt(JToken token, string fieldName)
    {
        if (token == null || token.Type != JTokenType.Integer)
        {
            throw new InvalidDataException($"V31 契约字段 {fieldName} 不是整数或不存在。");
        }
        return token.Value<int>();
    }

    private static Vector2 ReadVector2(JToken token, string fieldName)
    {
        JArray values = token as JArray;
        if (values == null || values.Count != 2)
        {
            throw new InvalidDataException($"V31 Vector2 契约字段 {fieldName} 无效。");
        }
        return new Vector2(values[0].Value<float>(), values[1].Value<float>());
    }

    private static Vector2Int ReadVector2Int(JToken token, string fieldName)
    {
        JArray values = token as JArray;
        if (values == null || values.Count != 2)
        {
            throw new InvalidDataException($"V31 Vector2Int 契约字段 {fieldName} 无效。");
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
