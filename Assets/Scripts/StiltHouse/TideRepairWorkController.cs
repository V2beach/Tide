using UnityEngine;

/// <summary>
/// 一次维修施工会话的唯一状态 owner。
///
/// 它只负责“正在修什么、做到哪一步、当前是否持续施工、是否已经提交”这组
/// 必须同步变化的状态；材料库存、世界物件 owner、潮位和最终维修效果仍由
/// 第一轮编排器负责。这样暂停施工不会丢进度，切换目标也不会留下半套旧状态。
/// </summary>
public sealed class TideRepairWorkController
{
    public bool ChoiceApplied { get; internal set; }
    public TideRepairTarget PendingChoice { get; internal set; } = TideRepairTarget.None;
    public int Step { get; internal set; }
    public float Progress01 { get; internal set; }
    public bool Active { get; internal set; }

    public void Reset(bool choiceApplied = false)
    {
        ChoiceApplied = choiceApplied;
        PendingChoice = TideRepairTarget.None;
        Step = 0;
        Progress01 = 0f;
        Active = false;
    }

    /// <summary>
    /// 选择一个真实施工点并从检查阶段开始。重复选择同一目标也明确重开，
    /// 调用方必须只在玩家首次按下且允许开工时调用。
    /// </summary>
    public void Begin(TideRepairTarget choice)
    {
        PendingChoice = choice;
        Step = (int)TideRepairWorkPhase.Inspect;
        Progress01 = 0f;
        Active = false;
        ChoiceApplied = false;
    }

    public void Pause()
    {
        Active = false;
    }

    /// <summary>
    /// 推进连续现实工时。返回 true 只表示工序完成，材料守恒检查和世界 owner
    /// 提交必须随后成功，才能调用 Complete；这里绝不提前生成维修收益。
    /// </summary>
    public bool Advance(float deltaTime, float durationSeconds)
    {
        if (PendingChoice == TideRepairTarget.None || ChoiceApplied || deltaTime <= 0f)
        {
            Active = false;
            return false;
        }

        Active = true;
        Progress01 = Mathf.Clamp01(
            Progress01 + deltaTime / Mathf.Max(0.01f, durationSeconds));
        Step = (int)TideRepairWorkPhaseModel.Evaluate(Progress01);
        return Progress01 >= 0.999f;
    }

    public void Complete()
    {
        if (PendingChoice == TideRepairTarget.None)
        {
            return;
        }

        Active = false;
        Step = (int)TideRepairWorkPhase.Seal;
        Progress01 = 1f;
        ChoiceApplied = true;
    }
}
