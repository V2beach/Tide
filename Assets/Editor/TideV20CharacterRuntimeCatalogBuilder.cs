using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 从 V20 正式人物动作目录生成轻量 Resources 索引。
/// 正式 PNG 和 GUID 始终留在原目录；这里只建立 Sprite 引用，不复制纹理。
/// </summary>
public static class TideV20CharacterRuntimeCatalogBuilder
{
    private const string SourceRoot =
        "Assets/Art/GeneratedAI/ProductionR03R06WorldMatchV20/Runtime/Character/Cycles";
    private const string CatalogFolder = "Assets/Resources/StiltFirstSliceAI";
    private const string CatalogPath = CatalogFolder + "/V20CharacterRuntimeCatalog.asset";

    [MenuItem("Tide/Resources/Rebuild V20 Character Runtime Catalog")]
    public static void RebuildCatalog()
    {
        Sprite[] idleFrames = LoadFrameSet(
            TideV20CharacterActionState.Idle,
            TideV20CharacterPresentationModel.IdleFrameCount);
        Sprite[] walkFrames = LoadFrameSet(
            TideV20CharacterActionState.Walk,
            TideV20CharacterPresentationModel.WalkFrameCount);
        Sprite[] swimFrames = LoadFrameSet(
            TideV20CharacterActionState.Swim,
            TideV20CharacterPresentationModel.SwimFrameCount);
        Sprite[] repairFrames = LoadFrameSet(
            TideV20CharacterActionState.Repair,
            TideV20CharacterPresentationModel.RepairFrameCount);
        Sprite[] haulFrames = LoadFrameSet(
            TideV20CharacterActionState.Haul,
            TideV20CharacterPresentationModel.HaulFrameCount);

        EnsureFolder(CatalogFolder);
        TideV20CharacterRuntimeCatalog catalog =
            AssetDatabase.LoadAssetAtPath<TideV20CharacterRuntimeCatalog>(CatalogPath);
        if (catalog == null)
        {
            catalog = ScriptableObject.CreateInstance<TideV20CharacterRuntimeCatalog>();
            AssetDatabase.CreateAsset(catalog, CatalogPath);
        }

        catalog.Configure(
            TideV20CharacterPresentationModel.CatalogVersion,
            idleFrames,
            walkFrames,
            swimFrames,
            repairFrames,
            haulFrames);
        EditorUtility.SetDirty(catalog);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        if (!catalog.IsComplete(out string reason))
        {
            throw new InvalidDataException("V20 人物运行索引生成后仍不完整：" + reason);
        }

        Debug.Log($"PASS：V20 人物运行索引已生成，共 {catalog.TotalFrameCount} 帧；" +
            $"未复制正式 PNG。 path={CatalogPath}");
    }

    public static void RebuildCatalogBatch()
    {
        RebuildCatalog();
    }

    private static Sprite[] LoadFrameSet(TideV20CharacterActionState actionState, int count)
    {
        string actionName = actionState.ToString();
        Sprite[] frames = new Sprite[count];
        for (int i = 0; i < count; i++)
        {
            string assetPath = $"{SourceRoot}/{actionName}/" +
                $"TideCharacterV20_{actionName}_F{i:00}.png";
            Sprite sprite = LoadSprite(assetPath);
            ValidatePivot(sprite, actionState, assetPath);
            frames[i] = sprite;
        }

        return frames;
    }

    private static Sprite LoadSprite(string assetPath)
    {
        string normalizedPath = assetPath.Replace('\\', '/');
        Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(normalizedPath);
        if (sprite == null)
        {
            throw new FileNotFoundException("无法按 Sprite 导入设置加载 V20 人物资源", normalizedPath);
        }

        return sprite;
    }

    private static void ValidatePivot(
        Sprite sprite,
        TideV20CharacterActionState actionState,
        string assetPath)
    {
        Vector2 expected = TideV20CharacterPresentationModel.GetPivotNormalized(actionState);
        Vector2 actual = new Vector2(
            sprite.pivot.x / sprite.rect.width,
            sprite.pivot.y / sprite.rect.height);
        if (Vector2.Distance(actual, expected) > 0.0001f)
        {
            throw new InvalidDataException(
                $"V20 人物 Pivot 漂移：{assetPath} actual={actual} expected={expected}");
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
