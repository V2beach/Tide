using System;

[Serializable]
public struct TideMaterialBundle
{
    public int Timber;
    public int Rope;
    public int Cloth;
    public int Metal;
    public int Food;

    public TideMaterialBundle(int timber, int rope, int cloth, int metal, int food)
    {
        Timber = timber;
        Rope = rope;
        Cloth = cloth;
        Metal = metal;
        Food = food;
    }

    public static TideMaterialBundle operator +(TideMaterialBundle left, TideMaterialBundle right)
    {
        return new TideMaterialBundle(
            left.Timber + right.Timber,
            left.Rope + right.Rope,
            left.Cloth + right.Cloth,
            left.Metal + right.Metal,
            left.Food + right.Food);
    }

    public bool Covers(TideMaterialBundle needs)
    {
        return Timber >= needs.Timber &&
            Rope >= needs.Rope &&
            Cloth >= needs.Cloth &&
            Metal >= needs.Metal &&
            Food >= needs.Food;
    }
}

/// <summary>
/// 船骸原物在真正施工前仍是可见物件；本模型只负责在最终固定点选择最少的
/// 原物组合。返回掩码不改变场景，也不会提前把所有暂存物一次性兑换成库存。
/// </summary>
public static class TideSalvageMaterialModel
{
    public const int HullPlankBit = 1 << 0;
    public const int SailclothBit = 1 << 1;
    public const int RivetedPlateBit = 1 << 2;
    public const int HeavyKeelRibPieceABit = 1 << 3;
    public const int HeavyKeelRibPieceBBit = 1 << 4;

    public static int GetPartBit(TideIslandSalvagePart part)
    {
        return part == TideIslandSalvagePart.HullPlank ? HullPlankBit :
            part == TideIslandSalvagePart.Sailcloth ? SailclothBit :
            part == TideIslandSalvagePart.RivetedPlate ? RivetedPlateBit : 0;
    }

    public static TideMaterialBundle GetYield(TideIslandSalvagePart part)
    {
        // 木板仍带着从船壳拆下的短索和钉孔；帆布保留边绳；铆接板只提供金属。
        // 这些产出描述的是一件具体原物能进入哪道工序，不是凭空奖励。
        return part == TideIslandSalvagePart.HullPlank
            ? new TideMaterialBundle(2, 1, 0, 0, 0)
            : part == TideIslandSalvagePart.Sailcloth
                ? new TideMaterialBundle(0, 1, 1, 0, 0)
                : part == TideIslandSalvagePart.RivetedPlate
                    ? new TideMaterialBundle(0, 0, 0, 2, 0)
                    : new TideMaterialBundle();
    }

    public static int GetHeavyPieceBit(TideHeavyWreckPiece piece)
    {
        return piece == TideHeavyWreckPiece.PieceA
            ? HeavyKeelRibPieceABit
            : piece == TideHeavyWreckPiece.PieceB
                ? HeavyKeelRibPieceBBit
                : 0;
    }

    public static TideMaterialBundle GetYield(TideHeavyWreckPiece piece)
    {
        // 两根弯肋都保留成形木、铜钉和旧绑绳，正好可作为一处柱脚斜撑或
        // 一段船体肋骨；用途由摆放位置决定，不能拆去补网或室内家具。
        return piece == TideHeavyWreckPiece.PieceA || piece == TideHeavyWreckPiece.PieceB
            ? new TideMaterialBundle(2, 1, 0, 0, 0)
            : new TideMaterialBundle();
    }

    /// <summary>
    /// 返回应在最终固定时消耗的部件掩码。-1 表示即使使用所有可见暂存物也不足；
    /// 0 表示现有库存/手持收获已经满足，不应误吞任何船骸原物。
    /// </summary>
    public static int SelectMinimumParts(
        int availablePartMask,
        TideMaterialBundle securedMaterials,
        TideMaterialBundle needs)
    {
        if (securedMaterials.Covers(needs))
        {
            return 0;
        }

        int bestMask = -1;
        int bestPartCount = int.MaxValue;
        int bestWaste = int.MaxValue;
        for (int candidateMask = 1; candidateMask < 32; candidateMask++)
        {
            if ((candidateMask & availablePartMask) != candidateMask)
            {
                continue;
            }

            TideMaterialBundle total = securedMaterials + GetYieldForMask(candidateMask);
            if (!total.Covers(needs))
            {
                continue;
            }

            int partCount = CountBits(candidateMask);
            int waste = Math.Max(0, total.Timber - needs.Timber) +
                Math.Max(0, total.Rope - needs.Rope) +
                Math.Max(0, total.Cloth - needs.Cloth) +
                Math.Max(0, total.Metal - needs.Metal) +
                Math.Max(0, total.Food - needs.Food);
            if (partCount < bestPartCount ||
                (partCount == bestPartCount && waste < bestWaste) ||
                (partCount == bestPartCount && waste == bestWaste && candidateMask < bestMask))
            {
                bestMask = candidateMask;
                bestPartCount = partCount;
                bestWaste = waste;
            }
        }

        return bestMask;
    }

    public static TideMaterialBundle GetYieldForMask(int partMask)
    {
        TideMaterialBundle result = new TideMaterialBundle();
        if ((partMask & HullPlankBit) != 0)
        {
            result += GetYield(TideIslandSalvagePart.HullPlank);
        }
        if ((partMask & SailclothBit) != 0)
        {
            result += GetYield(TideIslandSalvagePart.Sailcloth);
        }
        if ((partMask & RivetedPlateBit) != 0)
        {
            result += GetYield(TideIslandSalvagePart.RivetedPlate);
        }
        if ((partMask & HeavyKeelRibPieceABit) != 0)
        {
            result += GetYield(TideHeavyWreckPiece.PieceA);
        }
        if ((partMask & HeavyKeelRibPieceBBit) != 0)
        {
            result += GetYield(TideHeavyWreckPiece.PieceB);
        }
        return result;
    }

    private static int CountBits(int value)
    {
        int count = 0;
        while (value != 0)
        {
            count += value & 1;
            value >>= 1;
        }
        return count;
    }
}
