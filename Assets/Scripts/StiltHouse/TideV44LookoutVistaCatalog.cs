using UnityEngine;

/// <summary>
/// V44 瞭望远景的运行索引。
///
/// Catalog 只保存正式透明层。连续海面、潮位、云、昼夜、天气和灯塔是否已被
/// 玩家发现仍由现有世界状态拥有，避免把 QA 合成图误当成静态场景背景。
/// </summary>
[CreateAssetMenu(menuName = "Tide/V44 Lookout Vista Catalog", fileName = "V44LookoutVistaCatalog")]
public sealed class TideV44LookoutVistaCatalog : ScriptableObject
{
    [SerializeField] private int version;
    [SerializeField] private Sprite nearRoofDamage;
    [SerializeField] private Sprite nearRoofRepair;
    [SerializeField] private Sprite nearCrane;
    [SerializeField] private Sprite midWreckField;
    [SerializeField] private Sprite farLighthouse;
    [SerializeField] private Sprite[] lighthouseBeamFrames;

    public int Version => version;
    public Sprite NearRoofDamage => nearRoofDamage;
    public Sprite NearRoofRepair => nearRoofRepair;
    public Sprite NearCrane => nearCrane;
    public Sprite MidWreckField => midWreckField;
    public Sprite FarLighthouse => farLighthouse;

    public void Configure(
        Sprite damagedRoof,
        Sprite repairedRoof,
        Sprite crane,
        Sprite wreckField,
        Sprite lighthouse,
        Sprite[] beamFrames)
    {
        version = TideV44LookoutVistaPresentationModel.CatalogVersion;
        nearRoofDamage = damagedRoof;
        nearRoofRepair = repairedRoof;
        nearCrane = crane;
        midWreckField = wreckField;
        farLighthouse = lighthouse;
        lighthouseBeamFrames = beamFrames;
    }

    public Sprite GetLighthouseBeamFrame(int frameIndex)
    {
        if (lighthouseBeamFrames == null || lighthouseBeamFrames.Length == 0)
        {
            return null;
        }

        int wrapped = ((frameIndex % lighthouseBeamFrames.Length) + lighthouseBeamFrames.Length) %
            lighthouseBeamFrames.Length;
        return lighthouseBeamFrames[wrapped];
    }

    public bool IsComplete(out string reason)
    {
        if (version != TideV44LookoutVistaPresentationModel.CatalogVersion)
        {
            reason = $"索引版本是 {version}，不是 V44";
            return false;
        }

        if (nearRoofDamage == null || nearRoofRepair == null || nearCrane == null ||
            midWreckField == null || farLighthouse == null)
        {
            reason = "近景屋檐/吊机、中景残骸或远景灯塔缺失";
            return false;
        }

        if (lighthouseBeamFrames == null ||
            lighthouseBeamFrames.Length != TideV44LookoutVistaPresentationModel.BeamFrameCount)
        {
            reason = "灯塔雾光不是完整十二相";
            return false;
        }

        for (int i = 0; i < lighthouseBeamFrames.Length; i++)
        {
            if (lighthouseBeamFrames[i] == null)
            {
                reason = $"灯塔雾光第 {i} 帧缺失";
                return false;
            }
        }

        reason = "完整";
        return true;
    }
}
