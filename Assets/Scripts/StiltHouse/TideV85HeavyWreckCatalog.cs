using UnityEngine;

/// <summary>
/// V72 外露龙骨肋和 V85 可见拆解层的最小运行索引。运行时只有这一件重物进入
/// 首轮，不把其余四个 family 和 QA 图带入构建。
/// </summary>
[CreateAssetMenu(menuName = "Tide/V85 Heavy Wreck Catalog", fileName = "V85HeavyWreckCatalog")]
public sealed class TideV85HeavyWreckCatalog : ScriptableObject
{
    public const int CatalogVersion = 85;
    public const string RuntimeProfile = "Balanced";
    public static readonly Vector2 VisibleWorldSize = new Vector2(510f / 192f, 363f / 192f);
    public static readonly Vector2 WaterlineFromSourcePivot = new Vector2(0f, -0.074882f);
    public static readonly Vector2 SecurePointAFromSourcePivot = new Vector2(-0.95625f, 0.378125f);
    public static readonly Vector2 SecurePointBFromSourcePivot = new Vector2(0.95625f, 0.378125f);
    public static readonly Vector2 ScoreMarksOffset = new Vector2(0.012614f, -0.556872f);
    public static readonly Vector2 RemainderOffset = new Vector2(0.064847f, -0.65586f);
    public static readonly Vector2 PieceAOffset = new Vector2(-0.852715f, 0.110336f);
    public static readonly Vector2 PieceBOffset = new Vector2(1.030753f, -0.105458f);

    [SerializeField] private int version;
    [SerializeField] private string profile;
    [SerializeField] private Sprite intactKeelRib;
    [SerializeField] private Sprite scoreMarks;
    [SerializeField] private Sprite remainder;
    [SerializeField] private Sprite pieceA;
    [SerializeField] private Sprite pieceB;

    public Sprite IntactKeelRib => intactKeelRib;
    public Sprite ScoreMarks => scoreMarks;
    public Sprite Remainder => remainder;
    public Sprite PieceA => pieceA;
    public Sprite PieceB => pieceB;

    public void Configure(Sprite intact, Sprite score, Sprite remaining, Sprite leftPiece, Sprite rightPiece)
    {
        version = CatalogVersion;
        profile = RuntimeProfile;
        intactKeelRib = intact;
        scoreMarks = score;
        remainder = remaining;
        pieceA = leftPiece;
        pieceB = rightPiece;
    }

    public bool IsComplete(out string reason)
    {
        bool complete = version == CatalogVersion && profile == RuntimeProfile &&
            intactKeelRib != null && scoreMarks != null && remainder != null &&
            pieceA != null && pieceB != null;
        reason = complete
            ? "V72/V85 Balanced 外露龙骨肋五 owner 完整"
            : $"V85 索引不完整：version={version}, profile={profile}, " +
              $"intact={intactKeelRib != null}, score={scoreMarks != null}, " +
              $"remainder={remainder != null}, A={pieceA != null}, B={pieceB != null}";
        return complete;
    }
}
