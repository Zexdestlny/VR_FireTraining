using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit;

/// <summary>
/// 灭火器：挂在被 XRGrabInteractable 抓取的物体上。
/// 手柄按住扳机（Activate）开始喷射，从喷口向正前方做射线检测，
/// 命中 Fire 就按时间持续灭火。
///
/// 依赖：XR Interaction Toolkit 2.x（Unity 2022.3 推荐）
/// 组件：XRGrabInteractable（自动 Require）
/// </summary>
[RequireComponent(typeof(XRGrabInteractable))]
public class Extinguisher : MonoBehaviour
{
    [Header("喷射")]
    [Tooltip("喷口 Transform（空物体即可），射线从这里发出")]
    public Transform nozzle;
    [Tooltip("有效射程（米）")]
    public float range = 6f;
    [Tooltip("每秒灭火量")]
    public float extinguishPerSecond = 45f;
    [Tooltip("命中判定层，默认全部")]
    public LayerMask hitMask = ~0;

    [Header("表现")]
    public ParticleSystem sprayFX;
    public AudioSource sprayAudio;

    [Header("模拟器调试")]
    [Tooltip("无头显时允许鼠标右键直接喷射（XR Device Simulator 用）")]
    public bool mouseRightButtonSpray = true;

    XRGrabInteractable _grab;
    bool _xriActivated;     // 来自 XRI Activate（手柄扳机）
    bool _fxPlaying;        // 当前粒子/音效是否在播

    /// <summary>是否正在喷射：XRI 激活 或 鼠标右键按住。</summary>
    public bool IsSpraying => _xriActivated || RightMouseHeld();

    bool RightMouseHeld()
    {
        if (!mouseRightButtonSpray) return false;
        var mouse = Mouse.current;
        return mouse != null && mouse.rightButton.isPressed;
    }

    void Awake()
    {
        _grab = GetComponent<XRGrabInteractable>();
        _grab.activated.AddListener(OnActivated);
        _grab.deactivated.AddListener(OnDeactivated);

        StopSprayVisual();
    }

    void OnDestroy()
    {
        if (_grab != null)
        {
            _grab.activated.RemoveListener(OnActivated);
            _grab.deactivated.RemoveListener(OnDeactivated);
        }
    }

    void OnActivated(ActivateEventArgs args)
    {
        _xriActivated = true;
    }

    void OnDeactivated(DeactivateEventArgs args)
    {
        _xriActivated = false;
    }

    void StopSprayVisual()
    {
        if (sprayFX != null) sprayFX.Stop();
        if (sprayAudio != null) sprayAudio.Stop();
    }

    void Update()
    {
        bool want = IsSpraying;

        // 喷射开始/停止时同步粒子与音效
        if (want != _fxPlaying)
        {
            _fxPlaying = want;
            if (want)
            {
                if (sprayFX != null) sprayFX.Play();
                if (sprayAudio != null) sprayAudio.Play();
            }
            else
            {
                StopSprayVisual();
            }
        }

        if (!want) return;

        Vector3 origin = nozzle != null ? nozzle.position : transform.position;
        Vector3 dir = nozzle != null ? nozzle.forward : transform.forward;

        if (Physics.Raycast(origin, dir, out RaycastHit hit, range, hitMask, QueryTriggerInteraction.Ignore))
        {
            Fire fire = hit.collider.GetComponentInParent<Fire>();
            if (fire != null)
                fire.Extinguish(extinguishPerSecond * Time.deltaTime);
        }
    }

    // 场景视图里画出射程，方便调试
    void OnDrawGizmosSelected()
    {
        Vector3 origin = nozzle != null ? nozzle.position : transform.position;
        Vector3 dir = nozzle != null ? nozzle.forward : transform.forward;
        Gizmos.color = Color.cyan;
        Gizmos.DrawRay(origin, dir * range);
    }
}
