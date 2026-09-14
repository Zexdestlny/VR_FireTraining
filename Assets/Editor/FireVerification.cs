#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

/// <summary>
/// 灭火链路验证（不进入 Play 模式）。
///
/// 直接用编辑器的物理场景做射线检测，验证：
///   1. 手持姿态下喷口瞄准火源 → 射线命中该 Fire 的碰撞体
///   2. 纯水平射线 → 命中（修复喷口朝向 + 新增命中体之前必然打空）
///   3. A/B 对照：临时禁用 Fire 根上新增的 BoxCollider → 水平射线应打空
///   4. Fire.Extinguish 持续喷射 → 火势归零、OnExtinguished 只触发一次
///
/// 菜单：VR培训 → 验证灭火命中与熄灭
/// 批处理：
///   Unity.exe -batchmode -nographics -projectPath &lt;工程&gt; -executeMethod FireVerification.Verify -logFile -
///
/// 注意：编辑模式下 Fire.Update 不运行，所以"复燃"不会计入，
///       实际游戏里需要的喷射时间会略长于这里测出的值。
/// </summary>
public static class FireVerification
{
    const string ScenePath = "Assets/Scenes/VRFireTraining.unity";
    const string ResultFile = "verify_result.txt";

    [MenuItem("VR培训/验证灭火命中与熄灭")]
    public static void Verify()
    {
        var sb = new StringBuilder();
        bool allPass = true;

        try
        {
            sb.AppendLine("================ VR 灭火验证 ================");
            sb.AppendLine("Unity   : " + Application.unityVersion);
            sb.AppendLine("时间    : " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine();

            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Physics.SyncTransforms();

            var ext = UnityEngine.Object.FindObjectOfType<Extinguisher>();
            var fires = UnityEngine.Object.FindObjectsOfType<Fire>();

            if (ext == null) { Fail(sb, "场景中未找到 Extinguisher"); Write(sb, false); return; }
            if (ext.nozzle == null) { Fail(sb, "Extinguisher.nozzle 未赋值"); Write(sb, false); return; }
            if (fires.Length == 0) { Fail(sb, "场景中未找到 Fire"); Write(sb, false); return; }

            sb.AppendLine("灭火器  : " + ext.name
                + "  nozzle=" + ext.nozzle.name
                + "  range=" + ext.range
                + "  rate=" + ext.extinguishPerSecond + "/s");
            sb.AppendLine("火源数量: " + fires.Length);
            sb.AppendLine();

            // ---------- 模拟手持姿态 ----------
            // 手高取 1.1m（站立时常见值），灭火器局部喷口 (0, 0.3, 0.12) → 喷口世界 y ≈ 1.4
            ext.transform.position = new Vector3(0f, 1.1f, -1.5f);
            ext.transform.rotation = Quaternion.identity;
            Physics.SyncTransforms();

            Transform nozzle = ext.nozzle;
            Vector3 origin = nozzle.position;
            sb.AppendLine("手持模拟: 灭火器 root=(0.00, 1.10, -1.50)");
            sb.AppendLine("          喷口世界=" + Fmt(origin) + "  forward=" + Fmt(nozzle.forward));
            sb.AppendLine();

            // ---------- 测试 1：瞄准命中体中心 ----------
            sb.AppendLine("[测试1] 喷口瞄准每个火源命中体中心（Fire 根 + (0, 0.8, 0)）");
            foreach (var f in fires)
            {
                Vector3 aim = f.transform.position + new Vector3(0f, 0.8f, 0f);
                Vector3 dir = (aim - origin).normalized;
                var r = Cast(origin, dir, ext);
                bool pass = r.fire == f;
                allPass &= pass;
                sb.AppendLine("  [" + (pass ? "PASS" : "FAIL") + "] " + f.name + " @ " + Fmt(f.transform.position)
                    + " -> 命中 " + r.colliderName
                    + (r.hit ? " (" + r.distance.ToString("F2") + "m)" : "")
                    + "  解析 Fire=" + (r.fire != null ? r.fire.name : "null"));
            }
            sb.AppendLine();

            // ---------- 测试 2：纯水平射线 ----------
            sb.AppendLine("[测试2] 纯水平射线（喷口保持水平，只朝火源的水平方向）");
            foreach (var f in fires)
            {
                Vector3 dir = f.transform.position - origin;
                dir.y = 0f;
                if (dir.sqrMagnitude < 1e-6f) continue;
                dir.Normalize();

                var r = Cast(origin, dir, ext);
                bool pass = r.fire == f;
                allPass &= pass;
                sb.AppendLine("  [" + (pass ? "PASS" : "FAIL") + "] " + f.name
                    + " -> 命中 " + r.colliderName
                    + (r.hit ? " (" + r.distance.ToString("F2") + "m)" : "")
                    + "  解析 Fire=" + (r.fire != null ? r.fire.name : "null"));
            }
            sb.AppendLine();

            // ---------- 测试 3：A/B 对照 ----------
            sb.AppendLine("[测试3] A/B 对照：临时禁用 Fire 根上新增的 BoxCollider，重跑水平射线");
            var boxes = new List<BoxCollider>();
            foreach (var f in fires)
                boxes.AddRange(f.GetComponents<BoxCollider>());

            sb.AppendLine("        找到 " + boxes.Count + " 个命中体（期望 3 个）");
            foreach (var b in boxes) b.enabled = false;
            Physics.SyncTransforms();

            int missCount = 0;
            foreach (var f in fires)
            {
                Vector3 dir = f.transform.position - origin;
                dir.y = 0f;
                if (dir.sqrMagnitude < 1e-6f) continue;
                dir.Normalize();

                var r = Cast(origin, dir, ext);
                if (r.fire == null) missCount++;
                sb.AppendLine("        [禁用后] " + f.name + " -> 命中 " + r.colliderName
                    + "  解析 Fire=" + (r.fire != null ? r.fire.name : "null"));
            }

            foreach (var b in boxes) b.enabled = true;
            Physics.SyncTransforms();

            bool abPass = boxes.Count == 3 && missCount == fires.Length;
            allPass &= abPass;
            sb.AppendLine("  [" + (abPass ? "PASS" : "FAIL") + "] 禁用命中体后 " + missCount + "/" + fires.Length
                + " 个火源水平射线打空 → 证明新增命中体是必需的");
            sb.AppendLine();

            // ---------- 测试 4：熄灭逻辑链路 ----------
            sb.AppendLine("[测试4] 持续喷射熄灭链路（模拟 60Hz；编辑模式不计复燃，结果偏乐观）");
            var target = fires[0];
            InvokeAwake(target);

            int evtCount = 0;
            Action<Fire> handler = _ => evtCount++;
            target.OnExtinguished += handler;

            const float dt = 1f / 60f;
            float health0 = target.Health01;
            int frames = 0;
            Vector3 tAim = target.transform.position + new Vector3(0f, 0.8f, 0f);

            for (int i = 0; i < 900 && !target.IsOut; i++)
            {
                frames++;
                Vector3 dir = (tAim - nozzle.position).normalized;
                var r = Cast(nozzle.position, dir, ext);
                if (r.fire != null)
                    r.fire.Extinguish(ext.extinguishPerSecond * dt);
            }

            target.OnExtinguished -= handler;

            bool t4 = target.IsOut && evtCount == 1;
            allPass &= t4;
            sb.AppendLine("        初始火势=" + health0.ToString("F2")
                + "  模拟帧数=" + frames + " (" + (frames * dt).ToString("F2") + "s)"
                + "  已熄灭=" + target.IsOut
                + "  OnExtinguished 次数=" + evtCount);
            sb.AppendLine("  [" + (t4 ? "PASS" : "FAIL") + "] 喷射可熄灭且事件只触发一次");
            sb.AppendLine();

            // ---------- 测试 5：UI 面板朝向 ----------
            sb.AppendLine("[测试5] UI 面板朝向（世界空间 Canvas 的可读面在「局部 -Z 侧」）");
            var canvas = UnityEngine.Object.FindObjectOfType<Canvas>();
            var cam = Camera.main != null ? Camera.main : UnityEngine.Object.FindObjectOfType<Camera>();
            if (canvas != null && cam != null)
            {
                Vector3 toCam = cam.transform.position - canvas.transform.position;
                float dot = Vector3.Dot(canvas.transform.forward, toCam.normalized);
                bool readable = dot < 0f;
                allPass &= readable;
                sb.AppendLine("        Canvas = " + canvas.name + "  pos=" + Fmt(canvas.transform.position)
                    + "  forward=" + Fmt(canvas.transform.forward));
                sb.AppendLine("        Camera = " + cam.name + "  pos=" + Fmt(cam.transform.position)
                    + "  forward=" + Fmt(cam.transform.forward));
                sb.AppendLine("        dot(canvas.forward, dirToCamera) = " + dot.ToString("F3"));
                sb.AppendLine("  [" + (readable ? "PASS" : "FAIL") + "] 相机在画布 -Z 侧 → 文字不镜像");
            }
            else
            {
                allPass = false;
                sb.AppendLine("  [FAIL] 未找到 Canvas 或 Camera");
            }
            sb.AppendLine();

            // ---------- 测试 6：射线交互器为 Toggle 触发 ----------
            sb.AppendLine("[测试6] 射线交互器 SelectActionTrigger（Toggle = 按下抓取并保持，再按放下）");
            var rays = UnityEngine.Object.FindObjectsOfType<XRRayInteractor>();
            bool toggleOk = false;
            foreach (var r in rays)
            {
                sb.AppendLine("        " + r.name + " -> " + r.selectActionTrigger);
                if (r.selectActionTrigger == XRBaseControllerInteractor.InputTriggerType.Toggle)
                    toggleOk = true;
            }
            if (rays.Length == 0)
                sb.AppendLine("        （未找到 XRRayInteractor）");
            allPass &= toggleOk;
            sb.AppendLine("  [" + (toggleOk ? "PASS" : "FAIL") + "] 至少一个射线交互器为 Toggle");
            sb.AppendLine();

            // ---------- 测试 7：抓取物理配置 ----------
            sb.AppendLine("[测试7] 抓取物理配置（松手不抛出，避免灭火器被甩飞）");
            var grab = ext.GetComponent<XRGrabInteractable>();
            if (grab != null)
            {
                bool grabOk = !grab.throwOnDetach
                    && grab.movementType == XRBaseInteractable.MovementType.Instantaneous;
                allPass &= grabOk;
                sb.AppendLine("        throwOnDetach=" + grab.throwOnDetach
                    + "  movementType=" + grab.movementType + " (Instantaneous=2)");
                sb.AppendLine("  [" + (grabOk ? "PASS" : "FAIL") + "] 松手不抛出 且 运动模式为 Instantaneous");
            }
            else
            {
                allPass = false;
                sb.AppendLine("  [FAIL] 未找到 XRGrabInteractable");
            }
            sb.AppendLine();

            // ---------- 测试 8：主光与性能 HUD ----------
            sb.AppendLine("[测试8] 主光与性能 HUD");
            Light dirLight = null;
            foreach (var l in UnityEngine.Object.FindObjectsOfType<Light>())
            {
                if (l.type == LightType.Directional) { dirLight = l; break; }
            }
            var hud = UnityEngine.Object.FindObjectOfType<PerformanceHUD>();
            bool lightOk = dirLight != null;
            bool hudOk = hud != null;
            allPass &= lightOk && hudOk;
            sb.AppendLine("        方向光: " + (lightOk
                ? dirLight.name + "  intensity=" + dirLight.intensity + "  shadows=" + dirLight.shadows
                : "未找到"));
            sb.AppendLine("        性能 HUD: " + (hudOk ? hud.name : "未找到"));
            sb.AppendLine("  [" + (lightOk && hudOk ? "PASS" : "FAIL") + "] 方向光与性能 HUD 均已挂载");
            sb.AppendLine();

            sb.AppendLine("================ 结果: " + (allPass ? "全部 PASS" : "存在 FAIL") + " ================");
        }
        catch (Exception e)
        {
            sb.AppendLine("异常: " + e);
            allPass = false;
        }

        Write(sb, allPass);
    }

    // ===================== 辅助 =====================

    struct CastResult
    {
        public bool hit;
        public string colliderName;
        public float distance;
        public Fire fire;
    }

    static CastResult Cast(Vector3 origin, Vector3 dir, Extinguisher ext)
    {
        var res = new CastResult { hit = false, colliderName = "none", distance = 0f, fire = null };
        if (Physics.Raycast(origin, dir, out RaycastHit h, ext.range, ext.hitMask, QueryTriggerInteraction.Ignore))
        {
            res.hit = true;
            res.colliderName = h.collider.name;
            res.distance = h.distance;
            res.fire = h.collider.GetComponentInParent<Fire>();
        }
        return res;
    }

    /// <summary>编辑模式下 Awake 不会自动执行，手动调一次让 Fire._health 初始化。</summary>
    static void InvokeAwake(MonoBehaviour mb)
    {
        MethodInfo m = mb.GetType().GetMethod("Awake",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        if (m != null) m.Invoke(mb, null);
    }

    static string Fmt(Vector3 v)
    {
        return "(" + v.x.ToString("F2") + ", " + v.y.ToString("F2") + ", " + v.z.ToString("F2") + ")";
    }

    static void Fail(StringBuilder sb, string msg)
    {
        sb.AppendLine("FAIL: " + msg);
    }

    static void Write(StringBuilder sb, bool pass)
    {
        string text = sb.ToString();
        Debug.Log(text);

        try
        {
            string path = Path.Combine(Application.dataPath, "..", ResultFile);
            File.WriteAllText(path, text);
            Debug.Log("[FireVerification] 结果已写入: " + Path.GetFullPath(path));
        }
        catch (Exception e)
        {
            Debug.LogError("[FireVerification] 写入结果文件失败: " + e.Message);
        }

        Debug.Log("[FireVerification] RESULT=" + (pass ? "PASS" : "FAIL"));
    }
}
#endif
