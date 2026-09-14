using System;
using UnityEngine;

/// <summary>
/// 单个火源。被灭火器持续喷射会衰减，火势会缓慢复燃，归零后熄灭并广播事件。
/// 对标完美维度「VR 灭火器培训软件」的核心玩法对象。
/// </summary>
public class Fire : MonoBehaviour
{
    [Header("火势")]
    [Tooltip("火势总量，被喷射到 0 即熄灭")]
    public float maxHealth = 100f;
    [Tooltip("每秒自然复燃量（让玩家必须连续喷）")]
    public float regrowPerSecond = 3f;

    [Header("表现")]
    public ParticleSystem flame;
    public ParticleSystem smoke;
    public Light fireLight;
    [Tooltip("满火势时的火焰发射速率")]
    public float flameRateAtFull = 60f;
    [Tooltip("满火势时的灯光强度")]
    public float lightIntensityAtFull = 3f;

    /// <summary>被扑灭时触发（TrainingManager 订阅）</summary>
    public event Action<Fire> OnExtinguished;

    float _health;
    bool _isOut;

    public bool IsOut => _isOut;
    public float Health01 => maxHealth <= 0f ? 0f : _health / maxHealth;

    void Awake()
    {
        _health = maxHealth;
        ApplyVisual();
    }

    /// <summary>灭火器调用：扣减火势</summary>
    public void Extinguish(float amount)
    {
        if (_isOut || amount <= 0f) return;

        _health -= amount;
        if (_health <= 0f)
        {
            _health = 0f;
            ApplyVisual();
            ExtinguishOut();
        }
        else
        {
            ApplyVisual();
        }
    }

    void Update()
    {
        if (_isOut) return;

        // 复燃：只要没满就慢慢涨回去
        if (_health < maxHealth)
        {
            _health = Mathf.Min(maxHealth, _health + regrowPerSecond * Time.deltaTime);
            ApplyVisual();
        }
    }

    /// <summary>按火势比例同步粒子与灯光</summary>
    void ApplyVisual()
    {
        float t = Health01;

        if (flame != null)
        {
            var emission = flame.emission;
            emission.rateOverTimeMultiplier = flameRateAtFull * t;
            if (!flame.isPlaying && t > 0.01f) flame.Play();
        }

        if (smoke != null)
        {
            var emission = smoke.emission;
            emission.rateOverTimeMultiplier = 12f * t;
        }

        if (fireLight != null)
            fireLight.intensity = lightIntensityAtFull * t;
    }

    void ExtinguishOut()
    {
        _isOut = true;

        if (flame != null) flame.Stop();
        if (smoke != null) smoke.Stop();
        if (fireLight != null) fireLight.enabled = false;

        OnExtinguished?.Invoke(this);
    }
}
