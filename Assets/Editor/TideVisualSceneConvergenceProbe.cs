using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 正式 Scene 的人物、船和楼梯几何门。每个探针都重新打开权威场景，避免一个
/// 预览方法留下的水位、维修阶段或视图状态掩盖下一个问题。它只验证锚点、尺度、
/// 层级和路径，不把自动截图当作美术验收。
/// </summary>
public static class TideVisualSceneConvergenceProbe
{
    private const string ScenePath = "Assets/Scenes/Tide_StiltHouse_FirstSlice.unity";

    [MenuItem("Tide/Validation/Run Visual Scene Convergence Probe")]
    public static void RunFromMenu()
    {
        RunFromCommandLine();
    }

    public static void RunFromCommandLine()
    {
        List<string> failures = new List<string>();
        RunProbe("开场承重", controller => controller.RunEditorOpeningGroundingProbe(), failures);
        RunProbe("人物船屋尺度", controller => controller.RunEditorActorBoatWysiwygProbe(), failures);
        RunProbe("船上完整人物", controller => controller.RunEditorBoatPassengerScaleProbe(), failures);
        RunProbe("行走帧连续", controller => controller.RunEditorLocomotionFrameContinuityProbe(), failures);
        RunProbe("登船人物动作", controller => controller.RunEditorV37BoatCharacterActionProbe(), failures);
        RunProbe("登船路径附着", controller => controller.RunEditorMooredBoatBoardingAttachmentProbe(), failures);
        RunProbe("登船切屏", controller => controller.RunEditorBoatViewTransitionProbe(), failures);
        RunProbe("外梯方向", controller => controller.RunEditorV32ClimbDirectionProbe(), failures);
        RunProbe("梯速一致", controller => controller.RunEditorClimbTimingRealismProbe(), failures);
        RunProbe("室内三层", controller => controller.RunEditorV35InteriorIntegrationProbe(), failures);
        RunProbe("室内往返", controller => controller.RunEditorInteriorTraversalProbe(), failures);
        RunProbe("可走面连续", controller => controller.RunEditorWalkSurfacePathContinuityProbe(), failures);
        RunProbe("外梯显式输入", controller => controller.RunEditorExplicitExteriorStairInputProbe(), failures);
        if (failures.Count > 0)
        {
            throw new InvalidOperationException(
                "TIDE_VISUAL_SCENE_PROBE FAIL | " + string.Join(" | ", failures));
        }
        Debug.Log(
            "TIDE_VISUAL_SCENE_PROBE PASS | 开场/尺度/船上人物/行走/登船/三段梯/可走面/显式输入");
    }

    private static void RunProbe(
        string label,
        Func<TideStiltHouseFirstSliceController, string> probe,
        List<string> failures)
    {
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        TideStiltHouseFirstSliceController controller =
            UnityEngine.Object.FindObjectOfType<TideStiltHouseFirstSliceController>(true);
        if (controller == null)
        {
            failures.Add($"{label}: {ScenePath} 缺少主控制器");
            return;
        }

        string result = probe(controller);
        if (string.IsNullOrEmpty(result) || !result.StartsWith("PASS", StringComparison.Ordinal))
        {
            failures.Add($"{label}: {result}");
            return;
        }
        Debug.Log($"TIDE_VISUAL_SCENE_CHECK PASS [{label}] {result}");
    }
}
