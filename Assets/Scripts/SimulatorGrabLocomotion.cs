using System.Reflection;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation;

/// <summary>
/// 修复 XR Device Simulator 在编辑器里测试时的抓取手感问题。
///
/// 现象：抓住灭火器后按住手柄操纵键（空格 = 右手柄 / 左 Shift = 左手柄），
///       再按 W/S（或 A/D、Q/E），手里的灭火器会沿相机前后跑，透视上忽大忽小。
///
/// 原因：XRDeviceSimulator.ProcessPoseInput() 在 axis2DTargets 含 Position 时，
///       会把键盘位移直接累加到被操纵手柄的 devicePosition 上；
///       物体被 XRGrabInteractable 牢牢跟住手柄，于是跟着一起跑。
///       这是模拟器的设计行为（用来「把手伸出去」），真机头显里不存在。
///
/// 本脚本在模拟器之后（DefaultExecutionOrder 10000）接管：
///       抓住物体时按 WASD/QE，改为把位移施加到 HMD + 双手柄上，
///       即「拿着灭火器走路」——物体相对相机不再移动，也就不会忽大忽小。
///
/// 不改 XRI 包源码，也不依赖 axis2DTargets 的当前取值。
/// </summary>
[DefaultExecutionOrder(10000)]
public class SimulatorGrabLocomotion : MonoBehaviour
{
    [Header("接管范围")]
    [Tooltip("留空则自动收集场景里所有 XRGrabInteractable")]
    public XRGrabInteractable[] grabTargets;

    [Header("诊断")]
    [Tooltip("每帧打印模拟器与接管状态，排查用")]
    public bool logDiagnostics;

    // XRDeviceSimulator 的三个内部状态（都是 struct，读写要整块换）
    const string kLeftStateField = "m_LeftControllerState";
    const string kRightStateField = "m_RightControllerState";
    const string kHmdStateField = "m_HMDState";

    XRDeviceSimulator _sim;
    Transform _camera;
    Transform _cameraParent;

    FieldInfo _fLeft;
    FieldInfo _fRight;
    FieldInfo _fHmd;

    XRGrabInteractable[] _targets;
    bool _fieldErrorLogged;

    // 接管期间的基准位置（cameraParent 局部空间）
    bool _engaged;
    Vector3 _baseRight;
    Vector3 _baseLeft;
    Vector3 _baseHmd;

    /// <summary>
    /// 自动挂载，用户不需要手动往场景里拖组件。
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (FindObjectOfType<SimulatorGrabLocomotion>() != null)
            return;

        var go = new GameObject("[SimulatorGrabLocomotion]");
        go.AddComponent<SimulatorGrabLocomotion>();
    }

    void LateUpdate()
    {
        if (!ResolveRefs())
            return;

        bool holdingHandKey = _sim.manipulatingLeftDevice || _sim.manipulatingRightDevice;
        bool grabbing = IsGrabbing();

        if (logDiagnostics)
        {
            Debug.Log($"[SGL] handKey={holdingHandKey} grab={grabbing} " +
                      $"fps={_sim.manipulatingFPS} left={_sim.manipulatingLeftDevice} " +
                      $"right={_sim.manipulatingRightDevice} axis2DTargets={_sim.axis2DTargets} " +
                      $"engaged={_engaged}");
        }

        if (!holdingHandKey || !grabbing)
        {
            _engaged = false;
            return;
        }

        var right = (XRSimulatedControllerState)_fRight.GetValue(_sim);
        var left = (XRSimulatedControllerState)_fLeft.GetValue(_sim);
        var hmd = (XRSimulatedHMDState)_fHmd.GetValue(_sim);

        if (!_engaged)
        {
            // 进入接管的第一帧：只记基准，不干预
            _baseRight = right.devicePosition;
            _baseLeft = left.devicePosition;
            _baseHmd = hmd.centerEyePosition;
            _engaged = true;
            return;
        }

        Vector3 move = ComputeKeyboardMove(Time.deltaTime);

        if (move == Vector3.zero)
        {
            // 没按方向键：不干预，但让基准跟上当前位置，避免模拟器把手柄带偏
            _baseRight = right.devicePosition;
            _baseLeft = left.devicePosition;
            _baseHmd = hmd.centerEyePosition;
            return;
        }

        // 完全接管：三个设备一起沿相机水平朝向平移，相对位置不变
        right.devicePosition = _baseRight + move;
        left.devicePosition = _baseLeft + move;
        hmd.centerEyePosition = _baseHmd + move;
        hmd.devicePosition = hmd.centerEyePosition;

        _fRight.SetValue(_sim, right);
        _fLeft.SetValue(_sim, left);
        _fHmd.SetValue(_sim, hmd);

        _baseRight = right.devicePosition;
        _baseLeft = left.devicePosition;
        _baseHmd = hmd.centerEyePosition;
    }

    /// <summary>
    /// 与 XRDeviceSimulator 的 FPS 分支同一套算法：沿相机水平朝向平移。
    /// </summary>
    Vector3 ComputeKeyboardMove(float dt)
    {
        var kb = Keyboard.current;
        if (kb == null || _camera == null || _cameraParent == null)
            return Vector3.zero;

        float x = (kb.dKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed ? 1f : 0f);
        float y = (kb.eKey.isPressed ? 1f : 0f) - (kb.qKey.isPressed ? 1f : 0f);
        float z = (kb.wKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed ? 1f : 0f);

        if (Mathf.Approximately(x, 0f) && Mathf.Approximately(y, 0f) && Mathf.Approximately(z, 0f))
            return Vector3.zero;

        float bodyMultiplier = _sim.keyboardBodyTranslateMultiplier;
        var scaled = new Vector3(
            x * _sim.keyboardXTranslateSpeed * bodyMultiplier * dt,
            y * _sim.keyboardYTranslateSpeed * bodyMultiplier * dt,
            z * _sim.keyboardZTranslateSpeed * bodyMultiplier * dt);

        var cameraParentRotation = _cameraParent.rotation;
        var up = cameraParentRotation * Vector3.up;

        var forward = _camera.forward;
        if (Mathf.Approximately(Mathf.Abs(Vector3.Dot(forward, up)), 1f))
            forward = -_camera.up;

        var forwardRotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(forward, up), up);

        return Quaternion.Inverse(cameraParentRotation) * (forwardRotation * scaled);
    }

    bool IsGrabbing()
    {
        if (_targets == null)
        {
            _targets = grabTargets != null && grabTargets.Length > 0
                ? grabTargets
                : FindObjectsOfType<XRGrabInteractable>();
        }

        foreach (var target in _targets)
        {
            if (target != null && target.isSelected)
                return true;
        }

        return false;
    }

    bool ResolveRefs()
    {
        if (_sim == null)
        {
            _sim = FindObjectOfType<XRDeviceSimulator>();
            if (_sim == null)
                return false;

            var type = typeof(XRDeviceSimulator);
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            _fLeft = type.GetField(kLeftStateField, flags);
            _fRight = type.GetField(kRightStateField, flags);
            _fHmd = type.GetField(kHmdStateField, flags);

            if (_fLeft == null || _fRight == null || _fHmd == null)
            {
                if (!_fieldErrorLogged)
                {
                    _fieldErrorLogged = true;
                    Debug.LogError("[SimulatorGrabLocomotion] 找不到 XRDeviceSimulator 的内部状态字段，" +
                                   "可能是 XRI 版本变了。脚本已禁用。");
                }

                enabled = false;
                return false;
            }
        }

        if (_camera == null)
        {
            var cam = Camera.main;
            if (cam == null)
                cam = _sim.GetComponentInChildren<Camera>();

            if (cam != null)
            {
                _camera = cam.transform;
                _cameraParent = _camera.parent;
            }
        }

        return _camera != null && _cameraParent != null;
    }
}
