#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;

/// <summary>
/// Editor 扩展：一键生成「VR 灭火器安全培训」场景骨架。
/// 菜单：VR培训 → 一键生成灭火器培训场景
///
/// 生成内容：培训房间（地面/墙）+ 3 处火源（粒子火焰 + 灯光 + Fire 逻辑）
///          + 可抓取灭火器（XRGrabInteractable + Extinguisher）
///          + 世界空间 UI + TrainingManager，并把引用自动连线。
///
/// 前置：Package Manager 已安装 XR Interaction Toolkit。
/// 注意：XR Origin（玩家）与 XR Device Simulator 会自动放入场景（需先在 Package Manager 导入对应样例）。
/// </summary>
public static class FireSceneBuilder
{
    const string ScenePath = "Assets/Scenes/VRFireTraining.unity";

    [MenuItem("VR培训/一键生成灭火器培训场景")]
    public static void BuildScene()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // ---------- 材质 ----------
        Material matFloor = MakeMat(new Color(0.35f, 0.36f, 0.38f));
        Material matWall = MakeMat(new Color(0.72f, 0.72f, 0.70f));
        Material matRed = MakeMat(new Color(0.78f, 0.12f, 0.10f));
        Material matDark = MakeMat(new Color(0.15f, 0.15f, 0.16f));

        // ---------- 房间 ----------
        var room = new GameObject("TrainingRoom");
        CreateBox("Floor", room.transform, new Vector3(0f, -0.05f, 0f), new Vector3(12f, 0.1f, 12f), matFloor);
        CreateBox("Wall_N", room.transform, new Vector3(0f, 1.5f, 6f), new Vector3(12f, 3f, 0.2f), matWall);
        CreateBox("Wall_S", room.transform, new Vector3(0f, 1.5f, -6f), new Vector3(12f, 3f, 0.2f), matWall);
        CreateBox("Wall_E", room.transform, new Vector3(6f, 1.5f, 0f), new Vector3(0.2f, 3f, 12f), matWall);
        CreateBox("Wall_W", room.transform, new Vector3(-6f, 1.5f, 0f), new Vector3(0.2f, 3f, 12f), matWall);

        // 地面加传送区域（XRI），需地面有 Collider（CreateBox 已加）
        var floor = GameObject.Find("Floor");
        if (floor != null && floor.GetComponent<TeleportationArea>() == null)
            floor.AddComponent<TeleportationArea>();

        // ---------- 主光 ----------
        // 空场景默认没有方向光，只靠环境光 + 火源点光会明显偏暗，这里补一盏。
        // 角度取较陡的俯角，让光从房间上方打进来（房间四面有墙、顶部是敞开的）。
        var dirLightGo = new GameObject("Directional Light");
        dirLightGo.transform.position = new Vector3(0f, 5f, 0f);
        dirLightGo.transform.rotation = Quaternion.Euler(55f, -35f, 0f);
        Light dirLight = dirLightGo.AddComponent<Light>();
        dirLight.type = LightType.Directional;
        dirLight.color = new Color(1f, 0.96f, 0.90f);
        dirLight.intensity = 1.1f;
        dirLight.shadows = LightShadows.Soft;

        // ---------- 火源 ----------
        var firesRoot = new GameObject("Fires");
        Vector3[] firePositions =
        {
            new Vector3(-2.5f, 0.4f, 2.0f),
            new Vector3( 2.6f, 0.4f, 1.2f),
            new Vector3( 0.2f, 0.4f, 4.2f),
        };
        foreach (Vector3 p in firePositions)
            CreateFire(firesRoot.transform, p, matDark);

        // ---------- 灭火器 ----------
        GameObject extinguisher = CreateExtinguisher(new Vector3(0f, 0.9f, -1.5f), matRed, matDark);

        // ---------- UI ----------
        var ui = new GameObject("TrainingUI");
        Canvas canvas = ui.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        ui.AddComponent<CanvasScaler>();
        ui.transform.position = new Vector3(0f, 1.8f, 4.5f);
        // 世界空间 Canvas 的 UI 在局部 XY 平面按 +X 方向排版，只有站在画布「局部 -Z 侧」看文字才是正的。
        // 玩家出生点在 (0,0,-3)，正处在画布的 -Z 侧，所以这里必须保持 identity。
        // 之前写成 Euler(0,180,0) 会让可读面朝 +Z（背对玩家），文字左右镜像。
        ui.transform.rotation = Quaternion.identity;
        ui.transform.localScale = Vector3.one * 0.004f;
        var canvasRect = ui.GetComponent<RectTransform>();
        canvasRect.sizeDelta = new Vector2(600f, 400f);

        Text statusText = CreateText("StatusText", ui.transform,
            new Vector2(0f, 60f), new Vector2(560f, 140f), 48, TextAnchor.MiddleCenter);

        var completePanel = new GameObject("CompletePanel");
        completePanel.transform.SetParent(ui.transform, false);
        var panelRect = completePanel.AddComponent<RectTransform>();
        panelRect.sizeDelta = new Vector2(560f, 260f);
        var panelImg = completePanel.AddComponent<Image>();
        panelImg.color = new Color(0f, 0f, 0f, 0.75f);
        Text completeText = CreateText("CompleteText", completePanel.transform,
            Vector2.zero, new Vector2(540f, 240f), 52, TextAnchor.MiddleCenter);
        completePanel.SetActive(false);

        // ---------- 管理器 ----------
        var managerGo = new GameObject("TrainingManager");
        TrainingManager mgr = managerGo.AddComponent<TrainingManager>();
        mgr.firesRoot = firesRoot.transform;
        mgr.statusText = statusText;
        mgr.completePanel = completePanel;
        mgr.completeText = completeText;

        // ---------- 性能 HUD ----------
        // VR 的帧率就是生命线（72/90fps，掉帧即眩晕），把帧率余量做成常驻可见。
        var diagnosticsGo = new GameObject("Diagnostics");
        diagnosticsGo.AddComponent<PerformanceHUD>();

        // ---------- 玩家（XR Interaction Setup = XR Origin + Interaction Manager + Input Action Manager + EventSystem）与设备模拟器 ----------
        GameObject xrSetup = InstantiatePrefabByName("XR Interaction Setup");
        if (xrSetup != null)
            xrSetup.transform.position = new Vector3(0f, 0f, -3f);
        else
            Debug.LogWarning("[VR培训] 未找到 'XR Interaction Setup' 预制体：" +
                "请到 Package Manager → XR Interaction Toolkit → Samples 导入 'Starter Assets'。");

        bool hasSimulator = InstantiatePrefabByName("XR Device Simulator") != null;
        if (!hasSimulator)
            Debug.LogWarning("[VR培训] 未找到 'XR Device Simulator' 预制体：" +
                "请到 Package Manager → XR Interaction Toolkit → Samples 导入 'XR Device Simulator'（无头显测试用）。");

        // ---------- 保存 ----------
        Directory.CreateDirectory("Assets/Scenes");
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);

        Debug.Log("[VR培训] 场景已生成：" + ScenePath +
                  "\n玩家与模拟器已自动放入。");
        EditorUtility.DisplayDialog("完成",
            "场景已生成：\n" + ScenePath +
            "\n\n玩家 XR Interaction Setup 与 XR Device Simulator 已自动放入。",
            "好的");
    }

    /// <summary>
    /// 就地修复已生成场景里的粒子材质（洋红色）。
    /// 用于修复早期版本生成器留下的场景——不必重跑 BuildScene，
    /// 避免把手工调过的场景设置一起冲掉。
    /// </summary>
    [MenuItem("VR培训/修复粒子材质（洋红色）")]
    public static void RepairParticleMaterials()
    {
        Material mat = DefaultParticleMaterial();
        if (mat == null)
        {
            EditorUtility.DisplayDialog("修复失败", "取不到内置默认粒子材质，请查看 Console。", "好的");
            return;
        }

        var renderers = Object.FindObjectsByType<ParticleSystemRenderer>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);

        int fixedCount = 0;
        foreach (var r in renderers)
        {
            if (r.sharedMaterial != null) continue;
            r.sharedMaterial = mat;
            EditorUtility.SetDirty(r);
            fixedCount++;
        }

        if (fixedCount > 0)
        {
            EditorSceneManager.MarkAllScenesDirty();
            EditorSceneManager.SaveOpenScenes();
        }

        Debug.Log($"[VR培训] 粒子材质修复：处理 {fixedCount} 个（场景共 {renderers.Length} 个粒子渲染器）。");
        EditorUtility.DisplayDialog("修复粒子材质",
            fixedCount > 0
                ? $"已为 {fixedCount} 个粒子渲染器赋上默认粒子材质，场景已保存。\n\n按 Play 确认火焰是橙色的。"
                : "没有发现材质为空的粒子渲染器，无需修复。",
            "好的");
    }

    // ===================== 辅助 =====================

    /// <summary>按文件名在所有预制体里查找并实例化（用于自动放入 XRI 样例的 XR Origin / Device Simulator）。</summary>
    static GameObject InstantiatePrefabByName(string prefabName)
    {
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (Path.GetFileNameWithoutExtension(path) != prefabName) continue;
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab != null)
                return (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        }
        return null;
    }

    static Material MakeMat(Color c)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        var m = new Material(shader) { color = c };
        return m;
    }

    /// <summary>
    /// 内置默认粒子材质（shader = Particles/Standard Unlit）。
    /// 取 Unity 自带的内置资源，不依赖任何包，也不用往工程里塞资源文件。
    /// </summary>
    static Material DefaultParticleMaterial()
    {
        Material mat = AssetDatabase.GetBuiltinExtraResource<Material>("Default-ParticleSystem.mat");
        if (mat == null)
            mat = Resources.GetBuiltinResource<Material>("Default-ParticleSystem.mat");
        if (mat == null)
            Debug.LogWarning("[VR培训] 取不到内置默认粒子材质，火焰与喷射粒子会渲染成洋红色。");
        return mat;
    }

    static GameObject CreateBox(string name, Transform parent, Vector3 pos, Vector3 scale, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.position = pos;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = mat;
        return go;
    }

    static void CreateFire(Transform parent, Vector3 pos, Material mat)
    {
        var go = new GameObject("Fire");
        go.transform.SetParent(parent, false);
        go.transform.position = pos;

        // 火盆
        var bowl = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        bowl.name = "Bowl";
        bowl.transform.SetParent(go.transform, false);
        bowl.transform.localPosition = Vector3.zero;
        bowl.transform.localScale = new Vector3(0.5f, 0.4f, 0.5f);
        bowl.GetComponent<Renderer>().sharedMaterial = mat;

        // 火焰粒子
        ParticleSystem flame = CreateParticles(go.transform, "Flame",
            new Color(1f, 0.45f, 0.05f), 60f, 0.35f, 1.2f, 1.8f, 25f);
        flame.transform.localPosition = new Vector3(0f, 0.45f, 0f);

        // 烟雾粒子
        ParticleSystem smoke = CreateParticles(go.transform, "Smoke",
            new Color(0.2f, 0.2f, 0.2f, 0.35f), 12f, 0.5f, 1.0f, 2.2f, 12f);
        smoke.transform.localPosition = new Vector3(0f, 0.6f, 0f);

        // 火光
        var lightGo = new GameObject("FireLight");
        lightGo.transform.SetParent(go.transform, false);
        lightGo.transform.localPosition = new Vector3(0f, 0.5f, 0f);
        Light light = lightGo.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(1f, 0.55f, 0.15f);
        light.range = 8f;
        light.intensity = 3f;

        // 逻辑
        Fire fire = go.AddComponent<Fire>();
        fire.flame = flame;
        fire.smoke = smoke;
        fire.fireLight = light;
        fire.flameRateAtFull = 60f;
        fire.lightIntensityAtFull = 3f;

        // 命中体：覆盖可见火焰。
        // 火焰发射器在 local y=0.45（世界 0.85），粒子向上延伸约 2m；
        // 而火盆碰撞体顶端只有 y=0.8 —— 只靠火盆的话，射线瞄准火焰时会在命中体之上掠过。
        var hitBox = go.AddComponent<BoxCollider>();
        hitBox.size = new Vector3(0.7f, 1.6f, 0.7f);
        hitBox.center = new Vector3(0f, 0.8f, 0f);
    }

    static ParticleSystem CreateParticles(Transform parent, string name, Color color,
        float rate, float startSize, float startLifetime, float speed, float angle)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        ParticleSystem ps = go.AddComponent<ParticleSystem>();

        // 材质必须显式赋。脚本 AddComponent 出来的 ParticleSystem，其默认材质引用
        // 在保存场景时会被序列化成空（m_Materials: - {fileID: 0}），
        // 渲染出来就是 Unity 的「缺材质洋红」——火焰和喷射粒子全变成紫色方块。
        var psr = go.GetComponent<ParticleSystemRenderer>();
        psr.sharedMaterial = DefaultParticleMaterial();

        var main = ps.main;
        main.startColor = color;
        main.startSize = startSize;
        main.startLifetime = startLifetime;
        main.startSpeed = speed;
        main.loop = true;

        var emission = ps.emission;
        emission.rateOverTimeMultiplier = rate;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = angle;
        shape.radius = 0.12f;

        // 生命周期内颜色渐隐（对火焰/烟雾都自然）
        var col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        col.color = new ParticleSystem.MinMaxGradient(grad);

        ps.Stop();
        return ps;
    }

    static GameObject CreateExtinguisher(Vector3 pos, Material matRed, Material matDark)
    {
        var go = new GameObject("FireExtinguisher");
        go.transform.position = pos;

        // 瓶身
        var body = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        body.name = "Body";
        body.transform.SetParent(go.transform, false);
        body.transform.localPosition = Vector3.zero;
        body.transform.localScale = new Vector3(0.14f, 0.28f, 0.14f);
        body.GetComponent<Renderer>().sharedMaterial = matRed;

        // 顶部把手
        var handle = GameObject.CreatePrimitive(PrimitiveType.Cube);
        handle.name = "Handle";
        handle.transform.SetParent(go.transform, false);
        handle.transform.localPosition = new Vector3(0f, 0.32f, 0f);
        handle.transform.localScale = new Vector3(0.16f, 0.05f, 0.05f);
        handle.GetComponent<Renderer>().sharedMaterial = matDark;

        // 喷口（射线起点，朝瓶身正前方 +Z，与喷口所处位置一致）
        var nozzle = new GameObject("Nozzle");
        nozzle.transform.SetParent(go.transform, false);
        nozzle.transform.localPosition = new Vector3(0f, 0.3f, 0.12f);
        nozzle.transform.localRotation = Quaternion.identity;

        // 喷射粒子
        ParticleSystem spray = CreateParticles(go.transform, "Spray",
            new Color(0.85f, 0.92f, 1f, 0.8f), 120f, 0.12f, 0.5f, 6f, 6f);
        spray.transform.SetParent(nozzle.transform, false);
        spray.transform.localPosition = Vector3.zero;
        spray.transform.localRotation = Quaternion.identity;

        // 物理 + 抓取
        var rb = go.AddComponent<Rigidbody>();
        rb.mass = 1.5f;
        var col = go.AddComponent<BoxCollider>();
        col.size = new Vector3(0.2f, 0.7f, 0.2f);

        XRGrabInteractable grab = go.AddComponent<XRGrabInteractable>();
        grab.movementType = XRBaseInteractable.MovementType.Instantaneous;
        // 关掉抛出：模拟器里手柄跟着鼠标动，松手瞬间若鼠标还在动会把灭火器甩飞。
        grab.throwOnDetach = false;

        Extinguisher ext = go.AddComponent<Extinguisher>();
        ext.nozzle = nozzle.transform;
        ext.sprayFX = spray;

        return go;
    }

    static Text CreateText(string name, Transform parent, Vector2 anchoredPos,
        Vector2 size, int fontSize, TextAnchor anchor)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rect = go.AddComponent<RectTransform>();
        rect.anchoredPosition = anchoredPos;
        rect.sizeDelta = size;

        Text text = go.AddComponent<Text>();
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        text.font = font;
        text.fontSize = fontSize;
        text.alignment = anchor;
        text.color = Color.white;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.text = name == "StatusText" ? "已扑灭 0/0" : "培训完成！";
        return text;
    }
}
#endif
