using UnityEngine;

/// <summary>
/// 上一完整潮的高潮记忆。
///
/// 场景层决定何时采样权威平均潮位、何时越过一个自然潮周期；本模型只维护
/// 峰值与周期所有权。短周期浪尖不会进入这里，因此立柱盐线不会随每一朵浪
/// 抖动，也不会把尚未走完的本潮提前冒充成“上一潮”。
/// </summary>
public static class TideHighWaterMemoryModel
{
    public readonly struct State
    {
        public State(
            float currentCyclePeakY,
            float previousCyclePeakY,
            bool hasPreviousCycle,
            int completedCycleCount)
        {
            CurrentCyclePeakY = currentCyclePeakY;
            PreviousCyclePeakY = previousCyclePeakY;
            HasPreviousCycle = hasPreviousCycle;
            CompletedCycleCount = Mathf.Max(0, completedCycleCount);
        }

        public float CurrentCyclePeakY { get; }
        public float PreviousCyclePeakY { get; }
        public bool HasPreviousCycle { get; }
        public int CompletedCycleCount { get; }
    }

    public static State Begin(float initialWaterY)
    {
        float safeInitialY = IsFinite(initialWaterY) ? initialWaterY : 0f;
        return new State(safeInitialY, 0f, false, 0);
    }

    public static State Observe(State state, float waterY)
    {
        if (!IsFinite(waterY))
        {
            return state;
        }

        return new State(
            Mathf.Max(state.CurrentCyclePeakY, waterY),
            state.PreviousCyclePeakY,
            state.HasPreviousCycle,
            state.CompletedCycleCount);
    }

    public static State CompleteCycle(State state, float nextCycleInitialWaterY)
    {
        float safeNextY = IsFinite(nextCycleInitialWaterY)
            ? nextCycleInitialWaterY
            : state.CurrentCyclePeakY;
        return new State(
            safeNextY,
            state.CurrentCyclePeakY,
            true,
            state.CompletedCycleCount + 1);
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
