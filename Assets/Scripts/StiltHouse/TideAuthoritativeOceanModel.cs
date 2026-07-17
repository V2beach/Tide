using UnityEngine;

/// <summary>
/// 游戏运行时唯一的海况合成入口。
///
/// <see cref="TideOceanFieldModel"/> 提供连续长涌、短浪和阵风；
/// <see cref="TideWaveEventFieldModel"/> 把玩家看见的局部白浪投影为同位置的
/// 高度、坡度、推力和扰动。二者都使用原始现实秒，不读取压缩的昼夜进度。
/// </summary>
public static class TideAuthoritativeOceanModel
{
    public static TideOceanSample Sample(
        float meanWaterY,
        float worldX,
        float worldTimeSeconds,
        float tideStrength01,
        float storm01,
        float wind01,
        float signedWaveTravel)
    {
        TideOceanSample continuous = TideOceanFieldModel.Sample(
            meanWaterY,
            worldX,
            worldTimeSeconds,
            tideStrength01,
            storm01,
            wind01);
        TideWaveEventPhysicalSample localEvent =
            TideWaveEventFieldModel.SamplePhysicalInfluence(
                worldX,
                worldTimeSeconds,
                signedWaveTravel,
                wind01,
                storm01);

        // 独立扰动按“至少有一个来源发生”的方式合成，避免直接相加让普通风浪
        // 轻易饱和到 1；高度、坡度和速度则都是同一坐标系中的实际增量。
        float agitation01 = 1f -
            (1f - Mathf.Clamp01(continuous.Agitation01)) *
            (1f - Mathf.Clamp01(localEvent.Agitation01));
        return new TideOceanSample(
            continuous.SurfaceY + localEvent.HeightOffsetMeters,
            continuous.Slope + localEvent.SlopeOffset,
            continuous.HorizontalVelocity + localEvent.HorizontalVelocityOffset,
            Mathf.Clamp01(agitation01));
    }

    public static bool ProbeVisibleWaveCoupling(out string reason)
    {
        const float wind01 = 0.9f;
        const float storm01 = 0.92f;
        const float travelDirection = 1f;
        TideWaveEventSample visibleEvent = default;
        float eventTime = 0f;
        bool found = false;
        for (int timeStep = 0; timeStep < 480 && !found; timeStep++)
        {
            float time = timeStep * 0.25f;
            for (int cell = -2; cell <= 2; cell++)
            {
                float cellCenterX = (cell + 0.5f) * TideWaveEventFieldModel.CellWidthMeters;
                TideWaveEventSample candidate = TideWaveEventFieldModel.Sample(
                    0,
                    1,
                    cellCenterX,
                    time,
                    travelDirection,
                    wind01,
                    storm01);
                if (candidate.Visible && candidate.Opacity01 >= 0.45f &&
                    candidate.Kind == TideWaveEventKind.StormBreaker)
                {
                    visibleEvent = candidate;
                    eventTime = time;
                    found = true;
                    break;
                }
            }
        }

        if (!found)
        {
            reason = "高能海况中没有形成可用于物理校验的可见破浪";
            return false;
        }

        TideWaveEventPhysicalSample physical =
            TideWaveEventFieldModel.SamplePhysicalInfluence(
                visibleEvent.WorldX,
                eventTime,
                travelDirection,
                wind01,
                storm01);
        TideWaveEventPhysicalSample repeated =
            TideWaveEventFieldModel.SamplePhysicalInfluence(
                visibleEvent.WorldX,
                eventTime,
                travelDirection,
                wind01,
                storm01);
        bool localPhysicsExists = physical.HeightOffsetMeters >= 0.045f &&
            physical.HorizontalVelocityOffset >= 0.12f &&
            physical.Agitation01 >= 0.2f &&
            physical.StrongestVisibleWeight01 >= 0.2f;
        bool deterministic =
            Mathf.Abs(physical.HeightOffsetMeters - repeated.HeightOffsetMeters) <= 0.000001f &&
            Mathf.Abs(physical.HorizontalVelocityOffset - repeated.HorizontalVelocityOffset) <= 0.000001f;

        TideOceanSample continuous = TideOceanFieldModel.Sample(
            0f,
            visibleEvent.WorldX,
            eventTime,
            0.82f,
            storm01,
            wind01);
        TideOceanSample composed = Sample(
            0f,
            visibleEvent.WorldX,
            eventTime,
            0.82f,
            storm01,
            wind01,
            travelDirection);
        bool composedIntoAuthority = composed.SurfaceY > continuous.SurfaceY + 0.02f &&
            composed.HorizontalVelocity > continuous.HorizontalVelocity + 0.08f &&
            composed.Agitation01 >= continuous.Agitation01;

        TideWaveEventSample reverseEvent = TideWaveEventFieldModel.Sample(
            0,
            1,
            (visibleEvent.CellIndex + 0.5f) * TideWaveEventFieldModel.CellWidthMeters,
            eventTime,
            -1f,
            wind01,
            storm01);
        TideWaveEventPhysicalSample reversePhysical =
            TideWaveEventFieldModel.SamplePhysicalInfluence(
                reverseEvent.WorldX,
                eventTime,
                -1f,
                wind01,
                storm01);
        bool directionCoupled = reversePhysical.HorizontalVelocityOffset < -0.08f;

        TideWaveEventSample nearSlackEbb = TideWaveEventFieldModel.Sample(
            0,
            1,
            (visibleEvent.CellIndex + 0.5f) * TideWaveEventFieldModel.CellWidthMeters,
            eventTime,
            -0.01f,
            wind01,
            storm01);
        TideWaveEventSample slack = TideWaveEventFieldModel.Sample(
            0,
            1,
            (visibleEvent.CellIndex + 0.5f) * TideWaveEventFieldModel.CellWidthMeters,
            eventTime,
            0f,
            wind01,
            storm01);
        TideWaveEventSample nearSlackFlood = TideWaveEventFieldModel.Sample(
            0,
            1,
            (visibleEvent.CellIndex + 0.5f) * TideWaveEventFieldModel.CellWidthMeters,
            eventTime,
            0.01f,
            wind01,
            storm01);
        TideWaveEventPhysicalSample nearSlackEbbPhysical =
            TideWaveEventFieldModel.SamplePhysicalInfluence(
                slack.WorldX,
                eventTime,
                -0.01f,
                wind01,
                storm01);
        TideWaveEventPhysicalSample slackPhysical =
            TideWaveEventFieldModel.SamplePhysicalInfluence(
                slack.WorldX,
                eventTime,
                0f,
                wind01,
                storm01);
        TideWaveEventPhysicalSample nearSlackFloodPhysical =
            TideWaveEventFieldModel.SamplePhysicalInfluence(
                slack.WorldX,
                eventTime,
                0.01f,
                wind01,
                storm01);
        bool slackWaterContinuous =
            Mathf.Abs(nearSlackEbb.WorldX - slack.WorldX) <= 0.03f &&
            Mathf.Abs(nearSlackFlood.WorldX - slack.WorldX) <= 0.03f &&
            Mathf.Abs(nearSlackEbbPhysical.HorizontalVelocityOffset) <= 0.02f &&
            Mathf.Abs(slackPhysical.HorizontalVelocityOffset) <= 0.0001f &&
            Mathf.Abs(nearSlackFloodPhysical.HorizontalVelocityOffset) <= 0.02f;
        reason =
            $"抬升={physical.HeightOffsetMeters:F2}m；推力={physical.HorizontalVelocityOffset:F2}m/s；" +
            $"扰动={physical.Agitation01:F2}；确定={deterministic}；合成={composedIntoAuthority}；" +
            $"反向={directionCoupled}；过零={slackWaterContinuous}";
        return localPhysicsExists && deterministic && composedIntoAuthority &&
            directionCoupled && slackWaterContinuous;
    }
}
