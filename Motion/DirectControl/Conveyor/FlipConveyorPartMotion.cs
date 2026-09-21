using System.Collections.Generic;
using System.Globalization;
using NaughtyAttributes;
using NonsensicalKit.Tools;
using UnityEngine;

namespace NonsensicalKit.DigitalTwin.Motion
{

/// <summary>
/// 翻转输送工位（CSJ_TCC_*）：普通输送 + 独立翻转架旋转。
/// 翻转仅为给门让位，不挂货、不影响皮带与下传；按 TCC 翻转升/降及到位信号驱动。
/// </summary>
public class FlipConveyorPartMotion : ConveyorPartMotion
{
    private const string PointFlipUp = "LiftUp_FlipUp";
    private const string PointFlipDown = "LiftDown_FlipDown";
    private const string PointFlipUpInPos = "LiftUpInPos_FlipUpInPos";
    private const string PointFlipDownInPos = "LiftDownInPos_FlipDownInPos";

    [Header("翻转（独立机构，不带货）")]
    [Tooltip("翻转架 Transform；为空则不驱动翻转")]
    [SerializeField] private Transform m_flipAxis;
    [Tooltip("翻转下位（放平）本地欧拉角")]
    [SerializeField] private Vector3 m_downLocalEuler;
    [Tooltip("翻转上位（立起让门）本地欧拉角")]
    [SerializeField] private Vector3 m_upLocalEuler;
    [Tooltip("翻转角速度（度/秒）；≤0 则瞬间到位")]
    [SerializeField] private float m_flipSpeed = 45f;
    [Tooltip("初始化时把翻转架当前本地欧拉记为下位")]
    [SerializeField] private bool m_captureCurrentAsDown;

    private Vector3 _downEuler;
    private Vector3 _upEuler;
    private bool _poseCaptured;
    private Tweener _flipTweener;
    private bool _first = true;

    private bool _flipUp;
    private bool _flipDown;
    private bool _flipUpInPos;
    private bool _flipDownInPos;

    private bool WantRaised => _flipUpInPos || (_flipUp && !_flipDownInPos);
    private bool WantLowered => _flipDownInPos || (_flipDown && !_flipUpInPos);

    protected override void Init()
    {
        base.Init();
        CaptureFlipPose();
        _first = true;
    }

    protected override void Dispose()
    {
        AbortTween(ref _flipTweener);
        base.Dispose();
    }

    protected override void OnParseExtraSignals(List<PointData> part)
    {
        _flipUp = ReadBool(part, PointFlipUp, 5, _flipUp);
        _flipDown = ReadBool(part, PointFlipDown, 6, _flipDown);
        _flipUpInPos = ReadBool(part, PointFlipUpInPos, 7, _flipUpInPos);
        _flipDownInPos = ReadBool(part, PointFlipDownInPos, 8, _flipDownInPos);
    }

    protected override void OnAfterReceiveData(bool isLoad)
    {
        bool snap = _first;
        _first = false;
        bool raise = WantRaised && !WantLowered;
        ApplyFlipTarget(raise, snap);
    }

    private void ApplyFlipTarget(bool raised, bool snap)
    {
        if (m_flipAxis == null)
            return;

        CaptureFlipPose();

        Quaternion target = Quaternion.Euler(raised ? _upEuler : _downEuler);
        if (Quaternion.Angle(m_flipAxis.localRotation, target) < 0.05f)
            return;

        AbortTween(ref _flipTweener);

        if (snap || m_flipSpeed <= 0f)
        {
            m_flipAxis.localRotation = target;
            return;
        }

        _flipTweener = m_flipAxis.DoLocalRotate(target, m_flipSpeed).SetSpeedBased();
    }

    private void CaptureFlipPose()
    {
        if (_poseCaptured) return;
        if (m_flipAxis != null && m_captureCurrentAsDown)
            _downEuler = m_flipAxis.localEulerAngles;
        else
            _downEuler = m_downLocalEuler;
        _upEuler = m_upLocalEuler;
        _poseCaptured = true;
    }

    private static void AbortTween(ref Tweener tweener)
    {
        if (tweener == null) return;
        tweener.Abort();
        tweener = null;
    }

    private static bool TryFind(List<PointData> part, string pointCode, out PointData point)
    {
        point = null;
        if (string.IsNullOrEmpty(pointCode) || part == null) return false;

        for (int i = 0; i < part.Count; i++)
        {
            var p = part[i];
            if (p == null) continue;
            if (p.Point == pointCode || p.Name == pointCode)
            {
                point = p;
                return true;
            }
        }

        return false;
    }

    private static bool ReadBool(List<PointData> part, string pointCode, int fallbackIndex, bool fallbackValue)
    {
        if (!string.IsNullOrEmpty(pointCode) && TryFind(part, pointCode, out var point))
            return ParseBool(point.Value, fallbackValue);

        if (fallbackIndex >= 0 && fallbackIndex < part.Count && part[fallbackIndex] != null)
            return ParseBool(part[fallbackIndex].Value, fallbackValue);

        return fallbackValue;
    }

    private static bool ParseBool(string raw, bool fallback)
    {
        if (string.IsNullOrEmpty(raw)) return fallback;
        if (bool.TryParse(raw, out bool b)) return b;
        if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n))
            return n != 0;
        return fallback;
    }

    protected override string DiagStateSnapshot()
    {
        return base.DiagStateSnapshot()
            + $" flipUp={Bool01(_flipUp)} flipDown={Bool01(_flipDown)}"
            + $" upIn={Bool01(_flipUpInPos)} downIn={Bool01(_flipDownInPos)}";
    }

    protected override PartDataInfo GetInfo() =>
        new PartDataInfo("翻转输送机", m_partID,
            new List<PointDataInfo>
            {
                new PointDataInfo("输送正转状态", PointDataType.Bool, false),
                new PointDataInfo("输送反转状态", PointDataType.Bool, false),
                new PointDataInfo("有货", PointDataType.Bool, false),
                new PointDataInfo("翻转上升状态", PointDataType.Bool, false),
                new PointDataInfo("翻转下降状态", PointDataType.Bool, false),
                new PointDataInfo("翻转上到位", PointDataType.Bool, false),
                new PointDataInfo("翻转下到位", PointDataType.Bool, false),
            });

    [Button("测试翻转到上位")]
    [ContextMenu("FlipConveyor/测试翻转到上位")]
    private void TestFlipUp()
    {
        _flipUp = true;
        _flipUpInPos = true;
        _flipDown = false;
        _flipDownInPos = false;
        ApplyFlipTarget(true, snap: false);
    }

    [Button("测试翻转到下位")]
    [ContextMenu("FlipConveyor/测试翻转到下位")]
    private void TestFlipDown()
    {
        _flipUp = false;
        _flipUpInPos = false;
        _flipDown = true;
        _flipDownInPos = true;
        ApplyFlipTarget(false, snap: false);
    }
}
}
