using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 在正式运行 Scene 上验证维修账本、施工 owner 与首潮反馈。纯模型探针不能证明
/// 场景中的 V52/V69 资源真的消费了同一份权威状态，因此这里作为第二道门。
/// </summary>
public static class TideRepairSceneConvergenceProbe
{
    private const string ScenePath = "Assets/Scenes/Tide_StiltHouse_FirstSlice.unity";

    [MenuItem("Tide/Validation/Run Repair Scene Convergence Probe")]
    public static void RunFromMenu()
    {
        RunFromCommandLine();
    }

    public static void RunFromCommandLine()
    {
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        TideStiltHouseFirstSliceController controller =
            UnityEngine.Object.FindObjectOfType<TideStiltHouseFirstSliceController>(true);
        if (controller == null)
        {
            throw new InvalidOperationException($"TIDE_REPAIR_SCENE_PROBE FAIL: {ScenePath} 缺少主控制器");
        }

        RequirePass("连续施工", controller.RunEditorRepairContinuityProbe());
        RequirePass("全部维修点", controller.RunEditorAllRepairChoicesContinuityProbe());
        RequirePass("V52 船体 owner", controller.RunEditorV52BoatRepairIntegrationProbe());
        RequirePass("V69 房屋 owner", controller.RunEditorV69HouseRepairIntegrationProbe());
        RequirePass("材料落地", controller.RunEditorRepairMaterialGroundingProbe());
        RequirePass("首潮修船反馈", controller.RunEditorFirstTideSaltWoodBoatRepairFeedbackProbe());
        RequirePass("船体部件操控反馈", controller.RunEditorBoatComponentHandlingFeedbackProbe());
        RequirePass("借潮重物", controller.RunEditorHeavyWreckTidalLiftIntegrationProbe());
        Debug.Log(
            "TIDE_REPAIR_SCENE_PROBE PASS | 连续施工/全部维修点/V52/V69/材料落地/首潮修船反馈/部件操控/借潮重物");
    }

    private static void RequirePass(string label, string result)
    {
        if (string.IsNullOrEmpty(result) || !result.StartsWith("PASS", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"TIDE_REPAIR_SCENE_PROBE FAIL [{label}]: {result}");
        }
    }
}
