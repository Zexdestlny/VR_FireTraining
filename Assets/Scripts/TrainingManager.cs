using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 培训流程管理：统计火源数量、已扑灭数量、用时与评分，驱动 UI 显示。
/// 对标完美维度 VR 安全培训产品的"技能考核"逻辑。
/// </summary>
public class TrainingManager : MonoBehaviour
{
    public static TrainingManager Instance { get; private set; }

    [Header("场景引用")]
    [Tooltip("所有火源的父物体，启动时自动收集子物体的 Fire")]
    public Transform firesRoot;
    public Text statusText;
    public GameObject completePanel;
    public Text completeText;

    readonly List<Fire> _fires = new List<Fire>();
    int _total;
    int _done;
    float _startTime;
    bool _finished;

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        if (firesRoot != null)
            _fires.AddRange(firesRoot.GetComponentsInChildren<Fire>());

        foreach (Fire f in _fires)
            f.OnExtinguished += HandleExtinguished;

        _total = _fires.Count;
        _startTime = Time.time;

        if (completePanel != null) completePanel.SetActive(false);
        UpdateStatus();
    }

    void OnDestroy()
    {
        foreach (Fire f in _fires)
            if (f != null) f.OnExtinguished -= HandleExtinguished;
    }

    void HandleExtinguished(Fire fire)
    {
        _done++;
        UpdateStatus();

        if (_done >= _total && !_finished)
        {
            _finished = true;
            float elapsed = Time.time - _startTime;
            int score = Mathf.Max(0, 100 - Mathf.RoundToInt(elapsed));

            if (completePanel != null) completePanel.SetActive(true);
            if (completeText != null)
                completeText.text = $"培训完成！\n用时 {elapsed:F1} 秒\n评分 {score}";
        }
    }

    void Update()
    {
        if (!_finished) UpdateStatus();
    }

    void UpdateStatus()
    {
        if (statusText == null) return;
        float elapsed = Time.time - _startTime;
        statusText.text = $"已扑灭 {_done}/{_total}\n用时 {elapsed:F1} 秒";
    }
}
