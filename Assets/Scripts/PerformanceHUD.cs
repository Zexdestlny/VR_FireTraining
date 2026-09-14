using Unity.Profiling;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// 运行时性能 HUD：FPS / 帧耗时 / DrawCall / 三角面数。
///
/// 为什么用「世界空间 Canvas 挂在相机下」而不是 Screen Space Overlay：
///   Overlay 画布在 VR 头显里不渲染（它是屏幕空间的概念），
///   只有世界空间画布才能在头显和 Game 视图里同时可见。
///
/// 用途：VR 的目标帧率是 72/90fps，掉帧是眩晕的首要原因。
///   这个 HUD 让帧率余量随时可见——无头显时用 XR Device Simulator 跑，
///   读到的是编辑器里的真实帧率，可以据此判断场景还有多少性能空间。
/// </summary>
public class PerformanceHUD : MonoBehaviour
{
    [Header("位置（相对相机）")]
    [Tooltip("相对相机的偏移：前下方，不挡视线")]
    public Vector3 localOffset = new Vector3(0f, -0.28f, 1.1f);
    [Tooltip("画布缩放（世界空间下 0.0007 约等于 26px 字号）")]
    public float canvasScale = 0.0007f;
    [Tooltip("面板尺寸（像素）")]
    public Vector2 panelSize = new Vector2(260f, 130f);

    [Header("显示")]
    public int fontSize = 26;
    [Tooltip("刷新间隔（秒），避免每帧改文本触发 UI 重建")]
    public float updateInterval = 0.25f;
    [Tooltip("低于该帧率文字变黄")]
    public float warnFps = 72f;
    [Tooltip("低于该帧率文字变红")]
    public float badFps = 45f;

    [Header("开关")]
    [Tooltip("按下该键显示/隐藏 HUD（录演示视频时用）")]
    public Key toggleKey = Key.F1;

    GameObject _canvasGo;
    Text _text;
    float _timer;
    float _smoothedDelta;
    ProfilerRecorder _drawCalls;
    ProfilerRecorder _triangles;

    void Awake()
    {
        BuildUI();

        // DrawCall / 三角面靠 ProfilerRecorder 取，部分环境（如非开发版构建）拿不到，
        // 拿不到就显示 "-"，不影响 FPS 显示。
        _drawCalls = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
        _triangles = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
    }

    void OnDestroy()
    {
        if (_drawCalls.Valid) _drawCalls.Dispose();
        if (_triangles.Valid) _triangles.Dispose();
    }

    void Update()
    {
        var keyboard = Keyboard.current;
        if (keyboard != null && keyboard[toggleKey].wasPressedThisFrame && _canvasGo != null)
            _canvasGo.SetActive(!_canvasGo.activeSelf);

        // 指数平滑，避免数字每帧乱跳
        _smoothedDelta += (Time.unscaledDeltaTime - _smoothedDelta) * 0.1f;

        _timer += Time.unscaledDeltaTime;
        if (_timer < updateInterval) return;
        _timer = 0f;

        if (_text == null) return;

        float fps = _smoothedDelta > 0f ? 1f / _smoothedDelta : 0f;
        _text.color = fps < badFps ? new Color(1f, 0.40f, 0.35f)
            : fps < warnFps ? new Color(1f, 0.85f, 0.35f)
            : Color.white;

        string dc = _drawCalls.Valid ? _drawCalls.LastValue.ToString() : "-";
        string tris = _triangles.Valid ? _triangles.LastValue.ToString() : "-";

        _text.text = $"{fps:F1} FPS\n{_smoothedDelta * 1000f:F1} ms\nDC {dc}\nTris {tris}";
    }

    void BuildUI()
    {
        var cam = Camera.main;
        Transform parent = cam != null ? cam.transform : transform;

        _canvasGo = new GameObject("PerformanceHUDCanvas");
        _canvasGo.transform.SetParent(parent, false);
        _canvasGo.transform.localPosition = localOffset;
        _canvasGo.transform.localRotation = Quaternion.identity;
        _canvasGo.transform.localScale = Vector3.one * canvasScale;

        var canvas = _canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;

        var rect = _canvasGo.GetComponent<RectTransform>();
        rect.sizeDelta = panelSize;

        // 半透明底板：场景有亮色墙面和火光，没有底板文字会看不清
        var bgGo = new GameObject("BG");
        bgGo.transform.SetParent(_canvasGo.transform, false);
        var bgRect = bgGo.AddComponent<RectTransform>();
        bgRect.sizeDelta = panelSize;
        var bg = bgGo.AddComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.55f);
        bg.raycastTarget = false;

        var textGo = new GameObject("Text");
        textGo.transform.SetParent(_canvasGo.transform, false);
        var textRect = textGo.AddComponent<RectTransform>();
        textRect.sizeDelta = panelSize;
        textRect.anchoredPosition = new Vector2(8f, 0f);

        _text = textGo.AddComponent<Text>();
        _text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (_text.font == null) _text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        _text.fontSize = fontSize;
        _text.alignment = TextAnchor.MiddleLeft;
        _text.color = Color.white;
        _text.raycastTarget = false;
        _text.horizontalOverflow = HorizontalWrapMode.Overflow;
        _text.verticalOverflow = VerticalWrapMode.Overflow;
    }
}
