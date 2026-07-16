public enum TideHeavyWreckPiece
{
    None,
    PieceA,
    PieceB
}

public enum TideHeavyWreckPieceOwner
{
    Unavailable,
    Worksite,
    Carried,
    ShelterStaging,
    BoatStaging,
    IntegratedIntoShelter,
    IntegratedIntoBoat
}

[System.Serializable]
public struct TideHeavyWreckPieceOwnershipState
{
    public TideHeavyWreckPieceOwner PieceAOwner;
    public TideHeavyWreckPieceOwner PieceBOwner;
    public TideHeavyWreckPiece CarriedPiece;
}

/// <summary>
/// 拆解后左右弯肋的唯一 owner 账本。状态只在作业架、手中、两个施工位和最终
/// 结构之间移动；任何一步都不会生成副本或提前兑换库存。
/// </summary>
public static class TideHeavyWreckPieceOwnershipModel
{
    public static TideHeavyWreckPieceOwnershipState CreateUnavailable()
    {
        return new TideHeavyWreckPieceOwnershipState();
    }

    public static TideHeavyWreckPieceOwnershipState CreateSeparated()
    {
        return new TideHeavyWreckPieceOwnershipState
        {
            PieceAOwner = TideHeavyWreckPieceOwner.Worksite,
            PieceBOwner = TideHeavyWreckPieceOwner.Worksite,
            CarriedPiece = TideHeavyWreckPiece.None
        };
    }

    public static TideHeavyWreckPieceOwner GetOwner(
        TideHeavyWreckPieceOwnershipState state,
        TideHeavyWreckPiece piece)
    {
        return piece == TideHeavyWreckPiece.PieceA
            ? state.PieceAOwner
            : piece == TideHeavyWreckPiece.PieceB
                ? state.PieceBOwner
                : TideHeavyWreckPieceOwner.Unavailable;
    }

    public static TideHeavyWreckPieceOwnershipState TryPickUp(
        TideHeavyWreckPieceOwnershipState state,
        TideHeavyWreckPiece piece,
        out bool pickedUp)
    {
        pickedUp = false;
        if (piece == TideHeavyWreckPiece.None ||
            state.CarriedPiece != TideHeavyWreckPiece.None ||
            GetOwner(state, piece) != TideHeavyWreckPieceOwner.Worksite)
        {
            return state;
        }

        SetOwner(ref state, piece, TideHeavyWreckPieceOwner.Carried);
        state.CarriedPiece = piece;
        pickedUp = true;
        return state;
    }

    public static TideHeavyWreckPieceOwnershipState TryStageCarried(
        TideHeavyWreckPieceOwnershipState state,
        TideIslandSalvageDestination destination,
        out TideHeavyWreckPiece stagedPiece)
    {
        stagedPiece = state.CarriedPiece;
        if (stagedPiece == TideHeavyWreckPiece.None)
        {
            return state;
        }

        TideHeavyWreckPieceOwner owner = destination == TideIslandSalvageDestination.ShelterStaging
            ? TideHeavyWreckPieceOwner.ShelterStaging
            : destination == TideIslandSalvageDestination.EscapeBoatStaging
                ? TideHeavyWreckPieceOwner.BoatStaging
                : TideHeavyWreckPieceOwner.Unavailable;
        if (owner == TideHeavyWreckPieceOwner.Unavailable)
        {
            stagedPiece = TideHeavyWreckPiece.None;
            return state;
        }

        SetOwner(ref state, stagedPiece, owner);
        state.CarriedPiece = TideHeavyWreckPiece.None;
        return state;
    }

    public static TideHeavyWreckPieceOwnershipState TryIntegrate(
        TideHeavyWreckPieceOwnershipState state,
        TideHeavyWreckPiece piece,
        TideIslandSalvageDestination stagingDestination,
        out bool integrated)
    {
        integrated = false;
        TideHeavyWreckPieceOwner expected = stagingDestination == TideIslandSalvageDestination.ShelterStaging
            ? TideHeavyWreckPieceOwner.ShelterStaging
            : stagingDestination == TideIslandSalvageDestination.EscapeBoatStaging
                ? TideHeavyWreckPieceOwner.BoatStaging
                : TideHeavyWreckPieceOwner.Unavailable;
        if (piece == TideHeavyWreckPiece.None || GetOwner(state, piece) != expected)
        {
            return state;
        }

        TideHeavyWreckPieceOwner finalOwner = expected == TideHeavyWreckPieceOwner.ShelterStaging
            ? TideHeavyWreckPieceOwner.IntegratedIntoShelter
            : TideHeavyWreckPieceOwner.IntegratedIntoBoat;
        SetOwner(ref state, piece, finalOwner);
        integrated = true;
        return state;
    }

    public static int GetStagedMask(
        TideHeavyWreckPieceOwnershipState state,
        TideIslandSalvageDestination destination)
    {
        TideHeavyWreckPieceOwner expected = destination == TideIslandSalvageDestination.ShelterStaging
            ? TideHeavyWreckPieceOwner.ShelterStaging
            : destination == TideIslandSalvageDestination.EscapeBoatStaging
                ? TideHeavyWreckPieceOwner.BoatStaging
                : TideHeavyWreckPieceOwner.Unavailable;
        int mask = 0;
        if (state.PieceAOwner == expected)
        {
            mask |= TideSalvageMaterialModel.HeavyKeelRibPieceABit;
        }
        if (state.PieceBOwner == expected)
        {
            mask |= TideSalvageMaterialModel.HeavyKeelRibPieceBBit;
        }
        return mask;
    }

    private static void SetOwner(
        ref TideHeavyWreckPieceOwnershipState state,
        TideHeavyWreckPiece piece,
        TideHeavyWreckPieceOwner owner)
    {
        if (piece == TideHeavyWreckPiece.PieceA)
        {
            state.PieceAOwner = owner;
        }
        else if (piece == TideHeavyWreckPiece.PieceB)
        {
            state.PieceBOwner = owner;
        }
    }
}
