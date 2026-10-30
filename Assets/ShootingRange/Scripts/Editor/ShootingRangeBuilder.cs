using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Inputs;

/// <summary>
/// Собирает всю сцену тира одной командой меню: "Тир VR → Собрать сцену тира".
/// Все модели (автомат, магазины, прицелы, мишени, стол) строятся из примитивов,
/// игрок — префаб "XR Interaction Setup" из Starter Assets (как в лабораторной №1).
/// </summary>
public static class ShootingRangeBuilder
{
    // Впиши свои данные — надпись появится на столе (требование лабораторной №1)
    const string StudentSign = "Фамилия И.О.  |  группа ______";

    const string RootFolder = "Assets/ShootingRange";
    const string GeneratedFolder = RootFolder + "/Generated";
    const string SceneFolder = RootFolder + "/Scenes";
    const string ScenePath = SceneFolder + "/ShootingRange.unity";
    static readonly string[] RigPrefabNames = { "Complete XR Origin Set Up", "XR Interaction Setup" };
    const float TableTop = 0.9f;

    static int grabLayer;
    static int playerLayer;
    static int hitMask;
    static Font font;

    static Material matConcrete, matWall, matBerm, matTable, matMetal, matGunSteel, matGunWood, matMagazine,
        matScope, matGlass, matLens, matWhite, matBlack, matRed, matCard, matSkin, matBrass, matImpact,
        matFlash, matBoard, matButton, matLine;

    [MenuItem("Тир VR/Собрать сцену тира", priority = 0)]
    public static void BuildScene()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        string rigPath = FindRigPrefab();
        if (rigPath == null &&
            !EditorUtility.DisplayDialog("Тир VR",
                "Не найден префаб игрока с XR Origin (\"Complete XR Origin Set Up\" / \"XR Interaction Setup\").\n\n" +
                "Можно собрать сцену без игрока, затем перетащить любой префаб XR Origin в сцену " +
                "и выбрать меню \"Тир VR → Настроить игрока в открытой сцене\".\n\n" +
                "Собрать сцену без игрока?", "Собрать без игрока", "Отмена"))
            return;

        grabLayer = EnsureLayer(RangePrimitives.GrabbableLayerName);
        playerLayer = EnsureLayer(RangePrimitives.PlayerLayerName);
        if (grabLayer >= 0 && playerLayer >= 0) Physics.IgnoreLayerCollision(grabLayer, playerLayer, true);

        hitMask = ~(1 << 2); // без Ignore Raycast
        if (grabLayer >= 0) hitMask &= ~(1 << grabLayer);
        if (playerLayer >= 0) hitMask &= ~(1 << playerLayer);

        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        PrepareFolders();
        font = LoadBuiltinFont();
        CreateMaterials();

        SetupDefaultObjects(scene, rigPath != null);

        new GameObject("RangeManager").AddComponent<RangeBootstrap>();

        var environment = new GameObject("Environment").transform;
        BuildEnvironment(environment);
        BuildTable(environment);
        BuildSigns(environment);

        // Автомат лежит на столе на боку, стволом вправо
        var rifle = BuildRifle(new Vector3(0.0f, TableTop + 0.04f, 0.62f), Quaternion.Euler(0f, 90f, 90f));

        // Магазин, который уже вставлен в автомат (автомат заряжен сразу)
        var socketTransform = rifle.magazineSocket.transform;
        var loadedMag = BuildMagazine("Magazine (в автомате)", socketTransform.position, socketTransform.rotation);
        rifle.magazineSocket.startingSelectedInteractable = loadedMag.GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();

        // 3 запасных магазина на столе
        var magRotation = Quaternion.Euler(0f, 90f, 90f);
        BuildMagazine("Magazine 1", new Vector3(-0.78f, TableTop + 0.02f, 0.86f), magRotation);
        BuildMagazine("Magazine 2", new Vector3(-0.69f, TableTop + 0.02f, 0.86f), magRotation);
        BuildMagazine("Magazine 3", new Vector3(-0.60f, TableTop + 0.02f, 0.86f), magRotation);

        // Прицелы 2x и 4x
        BuildScope("Scope 2x", 2f, 0.13f, 0.021f, new Vector3(0.58f, TableTop + 0.001f, 0.82f));
        BuildScope("Scope 4x", 4f, 0.17f, 0.026f, new Vector3(0.78f, TableTop + 0.001f, 0.82f));

        BuildResetButton(new Vector3(-0.32f, TableTop, 0.88f));

        var range = new GameObject("Targets").transform;
        BuildTargets(range);
        BuildScoreboard(environment);

        if (rigPath != null) BuildRig(rigPath);

        EditorSceneManager.SaveScene(scene, ScenePath);
        AddSceneToBuildSettings(ScenePath);
        AssetDatabase.SaveAssets();

        Selection.activeGameObject = rifle.gameObject;
        EditorUtility.DisplayDialog("Тир VR",
            "Сцена собрана и сохранена:\n" + ScenePath +
            (rigPath == null
                ? "\n\nВНИМАНИЕ: игрок не добавлен. Перетащи префаб XR Origin в сцену и выбери \"Тир VR → Настроить игрока в открытой сцене\"."
                : "\n\nИгрок: " + rigPath) +
            "\n\nНажми Play (с шлемом или с XR Device Simulator).", "OK");
    }

    // ------------------------------------------------------------------ подготовка

    /// <summary>
    /// Ищет префаб игрока из Starter Assets. В XRI 2.x он называется
    /// "Complete XR Origin Set Up", в XRI 3.x — "XR Interaction Setup".
    /// Если ни одного из имён нет — берётся любой префаб с компонентом XR Origin.
    /// </summary>
    static string FindRigPrefab()
    {
        foreach (var name in RigPrefabNames)
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab " + name))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(path) == name) return path;
            }
        }

        string best = null;
        int bestScore = 0;
        foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null || FindComponentByTypeName(prefab, "XROrigin") == null) continue;

            int score = 1;
            if (prefab.GetComponentInChildren<XRInteractionManager>(true) != null) score += 4;
            if (prefab.GetComponentInChildren<ActionBasedController>(true) != null) score += 2;
            if (path.Contains("Starter Assets")) score += 2;
            if (path.Contains("Hands")) score -= 3; // префабы из примера Hands требуют XR Hands
            if (score > bestScore)
            {
                bestScore = score;
                best = path;
            }
        }
        if (best != null) Debug.Log("Тир VR: префаб игрока — " + best);
        return best;
    }

    static Component FindComponentByTypeName(GameObject root, string typeName)
    {
        foreach (var mb in root.GetComponentsInChildren<MonoBehaviour>(true))
            if (mb != null && mb.GetType().Name == typeName) return mb;
        return null;
    }

    static int EnsureLayer(string layerName)
    {
        int existing = LayerMask.NameToLayer(layerName);
        if (existing >= 0) return existing;

        var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
        if (assets == null || assets.Length == 0) return -1;

        var tagManager = new SerializedObject(assets[0]);
        var layers = tagManager.FindProperty("layers");
        for (int i = 8; i < layers.arraySize; i++)
        {
            var element = layers.GetArrayElementAtIndex(i);
            if (string.IsNullOrEmpty(element.stringValue))
            {
                element.stringValue = layerName;
                tagManager.ApplyModifiedProperties();
                return i;
            }
        }
        Debug.LogWarning("Тир VR: нет свободного слоя для " + layerName);
        return -1;
    }

    static void EnsureFolder(string parent, string name)
    {
        if (!AssetDatabase.IsValidFolder(parent + "/" + name)) AssetDatabase.CreateFolder(parent, name);
    }

    static void PrepareFolders()
    {
        EnsureFolder("Assets", "ShootingRange");
        if (AssetDatabase.IsValidFolder(GeneratedFolder)) AssetDatabase.DeleteAsset(GeneratedFolder);
        EnsureFolder(RootFolder, "Generated");
        EnsureFolder(RootFolder, "Scenes");
    }

    static Font LoadBuiltinFont()
    {
        Font f = null;
        try { f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { }
        if (f == null)
        {
            try { f = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch { }
        }
        return f;
    }

    static void SetupDefaultObjects(Scene scene, bool removeCamera)
    {
        foreach (var go in scene.GetRootGameObjects())
        {
            if (go.GetComponent<Camera>() != null)
            {
                if (removeCamera) Object.DestroyImmediate(go);
                else go.transform.SetPositionAndRotation(new Vector3(0f, 1.6f, -0.3f), Quaternion.Euler(10f, 0f, 0f));
            }
            else if (go.GetComponent<Light>() != null)
            {
                go.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            }
        }
    }

    static void AddSceneToBuildSettings(string path)
    {
        var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        scenes.RemoveAll(s => s.path == path);
        scenes.Insert(0, new EditorBuildSettingsScene(path, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }

    // ------------------------------------------------------------------ материалы

    static bool UsesSrp => GraphicsSettings.currentRenderPipeline != null;

    static Shader LitShader()
    {
        if (UsesSrp)
        {
            var s = Shader.Find("Universal Render Pipeline/Lit");
            if (s != null) return s;
            s = Shader.Find("HDRP/Lit");
            if (s != null) return s;
        }
        return Shader.Find("Standard");
    }

    static Shader UnlitShader()
    {
        if (UsesSrp)
        {
            var s = Shader.Find("Universal Render Pipeline/Unlit");
            if (s != null) return s;
        }
        return Shader.Find("Unlit/Color");
    }

    static Material Mat(string name, Color color, float metallic = 0f, float smoothness = 0.25f, bool unlit = false)
    {
        var m = new Material(unlit ? UnlitShader() : LitShader()) { name = name, enableInstancing = true };
        m.color = color;
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
        if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
        if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smoothness);
        AssetDatabase.CreateAsset(m, GeneratedFolder + "/" + name + ".mat");
        return m;
    }

    static void CreateMaterials()
    {
        matConcrete = Mat("Concrete", new Color(0.52f, 0.52f, 0.5f));
        matWall = Mat("Wall", new Color(0.62f, 0.6f, 0.55f));
        matBerm = Mat("Berm", new Color(0.36f, 0.27f, 0.18f));
        matTable = Mat("Table", new Color(0.45f, 0.3f, 0.18f), 0f, 0.35f);
        matMetal = Mat("Metal", new Color(0.35f, 0.36f, 0.38f), 0.6f, 0.45f);
        matGunSteel = Mat("GunSteel", new Color(0.1f, 0.1f, 0.11f), 0.75f, 0.5f);
        matGunWood = Mat("GunWood", new Color(0.45f, 0.2f, 0.08f), 0f, 0.55f);
        matMagazine = Mat("Magazine", new Color(0.32f, 0.12f, 0.06f), 0.1f, 0.45f);
        matScope = Mat("Scope", new Color(0.06f, 0.06f, 0.06f), 0.4f, 0.6f);
        matGlass = Mat("ScopeGlass", new Color(0.05f, 0.12f, 0.2f), 0.2f, 0.95f);
        matWhite = Mat("TargetWhite", new Color(0.95f, 0.95f, 0.93f));
        matBlack = Mat("TargetBlack", new Color(0.05f, 0.05f, 0.05f));
        matRed = Mat("TargetRed", new Color(0.85f, 0.08f, 0.06f));
        matCard = Mat("TargetCard", new Color(0.82f, 0.74f, 0.55f));
        matSkin = Mat("Skin", new Color(0.87f, 0.68f, 0.56f), 0f, 0.3f);
        matBrass = Mat("Brass", new Color(0.78f, 0.6f, 0.2f), 0.9f, 0.7f);
        matImpact = Mat("BulletHole", new Color(0.03f, 0.03f, 0.03f), 0f, 0f, true);
        matFlash = Mat("MuzzleFlash", new Color(1f, 0.75f, 0.3f), 0f, 0f, true);
        matBoard = Mat("ScoreboardBack", new Color(0.08f, 0.09f, 0.12f));
        matButton = Mat("ButtonRed", new Color(0.8f, 0.05f, 0.05f), 0f, 0.6f);
        matLine = Mat("FloorLine", new Color(0.95f, 0.95f, 0.95f), 0f, 0f, true);

        var lensShader = Shader.Find("ShootingRange/ScopeLens");
        if (lensShader == null)
        {
            Debug.LogError("Тир VR: не найден шейдер ShootingRange/ScopeLens");
            lensShader = UnlitShader();
        }
        matLens = new Material(lensShader) { name = "ScopeLens" };
        AssetDatabase.CreateAsset(matLens, GeneratedFolder + "/ScopeLens.mat");
    }

    // ------------------------------------------------------------------ хелперы

    static GameObject P(PrimitiveType type, string name, Transform parent, Vector3 pos, Vector3 euler, Vector3 scale,
        Material mat, bool collider = false)
    {
        return RangePrimitives.Create(type, name, parent, pos, euler, scale, mat, collider);
    }

    static Transform Empty(string name, Transform parent, Vector3 pos, Vector3 euler = default)
    {
        var t = new GameObject(name).transform;
        t.SetParent(parent, false);
        t.localPosition = pos;
        t.localRotation = Quaternion.Euler(euler);
        return t;
    }

    static BoxCollider Box(string name, Transform parent, Vector3 pos, Vector3 euler, Vector3 size)
    {
        var t = Empty(name, parent, pos, euler);
        var box = t.gameObject.AddComponent<BoxCollider>();
        box.size = size;
        return box;
    }

    static AudioSource Audio(GameObject go, float spatialBlend = 1f)
    {
        var a = go.AddComponent<AudioSource>();
        a.playOnAwake = false;
        a.spatialBlend = spatialBlend;
        a.minDistance = 1.5f;
        a.maxDistance = 80f;
        return a;
    }

    static TextMesh Text3D(Transform parent, string name, string text, Vector3 pos, Vector3 euler,
        float characterSize, int fontSize, Color color)
    {
        var t = Empty(name, parent, pos, euler);
        var renderer = t.gameObject.AddComponent<MeshRenderer>();
        var tm = t.gameObject.AddComponent<TextMesh>();
        tm.text = text;
        tm.font = font;
        tm.fontSize = fontSize;
        tm.characterSize = characterSize;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;
        tm.color = color;
        if (font != null) renderer.sharedMaterial = font.material;
        return tm;
    }

    static Canvas WorldCanvas(string name, Transform parent, Vector3 pos, Quaternion rot, Vector2 sizePixels, float metersPerPixel)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        var scaler = go.AddComponent<CanvasScaler>();
        scaler.dynamicPixelsPerUnit = 3f;

        var rt = (RectTransform)go.transform;
        rt.sizeDelta = sizePixels;
        rt.position = pos;
        rt.rotation = rot;
        rt.localScale = Vector3.one * metersPerPixel;
        return canvas;
    }

    static RectTransform Stretch(GameObject go, Transform parent, Vector2 anchorMin, Vector2 anchorMax)
    {
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        return rt;
    }

    static Text UIText(Transform parent, string name, string text, int size, Color color,
        Vector2 anchorMin, Vector2 anchorMax, TextAnchor alignment = TextAnchor.MiddleCenter, FontStyle style = FontStyle.Normal)
    {
        var go = new GameObject(name, typeof(RectTransform));
        Stretch(go, parent, anchorMin, anchorMax);
        var t = go.AddComponent<Text>();
        t.font = font;
        t.text = text;
        t.fontSize = size;
        t.fontStyle = style;
        t.color = color;
        t.alignment = alignment;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        return t;
    }

    static void UIPanel(Transform parent, Color color)
    {
        var go = new GameObject("Background", typeof(RectTransform));
        Stretch(go, parent, Vector2.zero, Vector2.one);
        var img = go.AddComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
    }

    // ------------------------------------------------------------------ окружение

    static void BuildEnvironment(Transform parent)
    {
        P(PrimitiveType.Plane, "Floor", parent, new Vector3(0f, 0f, 14f), Vector3.zero, new Vector3(4f, 1f, 5f), matConcrete, true);

        P(PrimitiveType.Cube, "WallLeft", parent, new Vector3(-7.5f, 1.5f, 17f), Vector3.zero, new Vector3(0.3f, 3f, 38f), matWall, true);
        P(PrimitiveType.Cube, "WallRight", parent, new Vector3(7.5f, 1.5f, 17f), Vector3.zero, new Vector3(0.3f, 3f, 38f), matWall, true);
        P(PrimitiveType.Cube, "Backstop", parent, new Vector3(0f, 3f, 37f), Vector3.zero, new Vector3(15.3f, 6f, 2f), matBerm, true);
        P(PrimitiveType.Cube, "BoothWall", parent, new Vector3(0f, 1.5f, -2.2f), Vector3.zero, new Vector3(15.3f, 3f, 0.3f), matWall, true);
        P(PrimitiveType.Cube, "BoothRoof", parent, new Vector3(0f, 3.1f, -0.5f), Vector3.zero, new Vector3(15.3f, 0.2f, 3.6f), matWall, true);
        // Барьер за столом — чтобы игрок не уходил на линию мишеней
        P(PrimitiveType.Cube, "FiringLineBarrier", parent, new Vector3(0f, 0.45f, 1.3f), Vector3.zero, new Vector3(14.7f, 0.9f, 0.06f), matMetal, true);

        foreach (float z in new[] { 10f, 20f, 30f })
            P(PrimitiveType.Cube, "DistanceLine_" + z + "m", parent, new Vector3(0f, 0.003f, z), Vector3.zero, new Vector3(14.6f, 0.005f, 0.08f), matLine);
    }

    static void BuildTable(Transform parent)
    {
        var table = Empty("Table", parent, Vector3.zero);
        P(PrimitiveType.Cube, "TableTop", table, new Vector3(0f, TableTop - 0.025f, 0.65f), Vector3.zero, new Vector3(1.8f, 0.05f, 0.6f), matTable, true);
        float legHeight = TableTop - 0.05f;
        foreach (float x in new[] { -0.85f, 0.85f })
            foreach (float z in new[] { 0.4f, 0.9f })
                P(PrimitiveType.Cube, "Leg", table, new Vector3(x, legHeight * 0.5f, z), Vector3.zero, new Vector3(0.06f, legHeight, 0.06f), matTable, true);
    }

    static void BuildSigns(Transform parent)
    {
        // Подпись студента, лежит на столе (лабораторная №1)
        var sign = WorldCanvas("StudentSign", parent, new Vector3(0.5f, TableTop + 0.002f, 0.42f), Quaternion.Euler(90f, 0f, 0f), new Vector2(520f, 50f), 0.001f);
        UIText(sign.transform, "Text", StudentSign, 30, new Color(1f, 0.85f, 0.2f), Vector2.zero, Vector2.one, TextAnchor.MiddleCenter, FontStyle.Bold);

        // Подсказка по управлению
        var info = WorldCanvas("InfoPanel", parent, new Vector3(-1.4f, 1.35f, 0.9f), Quaternion.Euler(0f, -35f, 0f), new Vector2(520f, 360f), 0.0015f);
        UIPanel(info.transform, new Color(0f, 0f, 0f, 0.8f));
        UIText(info.transform, "Text",
            "ТИР VR\n\n" +
            "Grip — взять предмет\n" +
            "Trigger — огонь (автоматический)\n" +
            "Вторая рука на цевьё — двуручный хват\n\n" +
            "Магазин: поднести снизу к автомату и отпустить\n" +
            "Вынуть магазин: схватить его второй рукой\n" +
            "Прицел 2x / 4x: поднести к планке сверху и отпустить\n\n" +
            "Левый стик — ходьба, правый — поворот\n" +
            "СБРОС — обнулить табло и пополнить магазины",
            20, Color.white, new Vector2(0.05f, 0.04f), new Vector2(0.95f, 0.96f), TextAnchor.UpperLeft);
    }

    // ------------------------------------------------------------------ автомат

    static AKRifle BuildRifle(Vector3 position, Quaternion rotation)
    {
        var root = new GameObject("AK (Автомат Калашникова)");
        root.transform.SetPositionAndRotation(position, rotation);

        var rb = root.AddComponent<Rigidbody>();
        rb.mass = 3.5f;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

        // --- внешний вид (дочерний объект Visual дёргается при отдаче) ---
        var v = Empty("Visual", root.transform, Vector3.zero);
        P(PrimitiveType.Cube, "Receiver", v, new Vector3(0f, 0f, 0f), Vector3.zero, new Vector3(0.05f, 0.07f, 0.30f), matGunSteel);
        P(PrimitiveType.Cube, "DustCover", v, new Vector3(0f, 0.045f, -0.01f), Vector3.zero, new Vector3(0.048f, 0.022f, 0.27f), matGunSteel);
        P(PrimitiveType.Cube, "ScopeRail", v, new Vector3(0f, 0.061f, -0.02f), Vector3.zero, new Vector3(0.02f, 0.008f, 0.10f), matGunSteel);
        P(PrimitiveType.Cylinder, "Barrel", v, new Vector3(0f, 0.012f, 0.40f), new Vector3(90f, 0f, 0f), new Vector3(0.022f, 0.20f, 0.022f), matGunSteel);
        P(PrimitiveType.Cylinder, "MuzzleBrake", v, new Vector3(0f, 0.012f, 0.625f), new Vector3(90f, 0f, 0f), new Vector3(0.028f, 0.03f, 0.028f), matGunSteel);
        P(PrimitiveType.Cube, "HandguardLower", v, new Vector3(0f, -0.004f, 0.26f), Vector3.zero, new Vector3(0.056f, 0.046f, 0.2f), matGunWood);
        P(PrimitiveType.Cube, "HandguardUpper", v, new Vector3(0f, 0.042f, 0.255f), Vector3.zero, new Vector3(0.042f, 0.03f, 0.17f), matGunWood);
        P(PrimitiveType.Cylinder, "GasTube", v, new Vector3(0f, 0.045f, 0.40f), new Vector3(90f, 0f, 0f), new Vector3(0.022f, 0.055f, 0.022f), matGunSteel);
        P(PrimitiveType.Cube, "GasBlock", v, new Vector3(0f, 0.03f, 0.465f), Vector3.zero, new Vector3(0.03f, 0.05f, 0.025f), matGunSteel);
        P(PrimitiveType.Cube, "FrontSightBase", v, new Vector3(0f, 0.04f, 0.56f), Vector3.zero, new Vector3(0.022f, 0.05f, 0.025f), matGunSteel);
        P(PrimitiveType.Cube, "FrontSightPost", v, new Vector3(0f, 0.071f, 0.56f), Vector3.zero, new Vector3(0.003f, 0.014f, 0.003f), matGunSteel);
        P(PrimitiveType.Cube, "RearSight", v, new Vector3(0f, 0.063f, 0.125f), Vector3.zero, new Vector3(0.032f, 0.016f, 0.04f), matGunSteel);
        P(PrimitiveType.Cube, "RearNotchL", v, new Vector3(0.008f, 0.075f, 0.11f), Vector3.zero, new Vector3(0.008f, 0.008f, 0.006f), matGunSteel);
        P(PrimitiveType.Cube, "RearNotchR", v, new Vector3(-0.008f, 0.075f, 0.11f), Vector3.zero, new Vector3(0.008f, 0.008f, 0.006f), matGunSteel);
        P(PrimitiveType.Cube, "PistolGrip", v, new Vector3(0f, -0.08f, -0.08f), new Vector3(18f, 0f, 0f), new Vector3(0.032f, 0.11f, 0.042f), matGunWood);
        P(PrimitiveType.Cube, "TriggerGuard", v, new Vector3(0f, -0.05f, -0.02f), Vector3.zero, new Vector3(0.008f, 0.006f, 0.07f), matGunSteel);
        P(PrimitiveType.Cube, "Trigger", v, new Vector3(0f, -0.045f, -0.025f), new Vector3(15f, 0f, 0f), new Vector3(0.006f, 0.025f, 0.006f), matGunSteel);
        P(PrimitiveType.Cube, "Stock", v, new Vector3(0f, -0.025f, -0.29f), new Vector3(-4f, 0f, 0f), new Vector3(0.042f, 0.065f, 0.24f), matGunWood);
        P(PrimitiveType.Cube, "ButtPlate", v, new Vector3(0f, -0.035f, -0.415f), new Vector3(-4f, 0f, 0f), new Vector3(0.044f, 0.085f, 0.012f), matGunSteel);
        P(PrimitiveType.Cube, "MagWell", v, new Vector3(0f, -0.042f, 0.075f), Vector3.zero, new Vector3(0.034f, 0.014f, 0.07f), matGunSteel);
        P(PrimitiveType.Cube, "ChargingHandle", v, new Vector3(0.035f, 0.02f, 0.10f), Vector3.zero, new Vector3(0.02f, 0.01f, 0.012f), matGunSteel);

        var ammoCounter = Text3D(v, "AmmoCounter", "30+1", new Vector3(-0.0265f, 0.0f, -0.05f), new Vector3(0f, 90f, 0f),
            0.0022f, 60, new Color(0.3f, 1f, 0.3f));

        // --- коллайдеры (не двигаются при отдаче) ---
        var cols = Empty("Colliders", root.transform, Vector3.zero);
        var solid = new[]
        {
            Box("Col_Body", cols, new Vector3(0f, 0.01f, 0f), Vector3.zero, new Vector3(0.05f, 0.09f, 0.32f)),
            Box("Col_Front", cols, new Vector3(0f, 0.02f, 0.41f), Vector3.zero, new Vector3(0.05f, 0.07f, 0.5f)),
            Box("Col_Grip", cols, new Vector3(0f, -0.08f, -0.08f), new Vector3(18f, 0f, 0f), new Vector3(0.035f, 0.11f, 0.045f)),
            Box("Col_Stock", cols, new Vector3(0f, -0.025f, -0.29f), new Vector3(-4f, 0f, 0f), new Vector3(0.045f, 0.07f, 0.26f)),
        };

        // --- служебные точки ---
        var gripAttach = Empty("GripAttach", root.transform, new Vector3(0f, -0.07f, -0.075f));
        var handguardAttach = Empty("HandguardAttach", root.transform, new Vector3(0f, 0f, 0.26f));
        var muzzle = Empty("Muzzle", root.transform, new Vector3(0f, 0.012f, 0.66f));
        var sight = Empty("IronSightLine", root.transform, new Vector3(0f, 0.078f, 0.11f));
        var ejectionPort = Empty("EjectionPort", root.transform, new Vector3(0.028f, 0.03f, 0.03f));

        var flash = P(PrimitiveType.Sphere, "MuzzleFlash", muzzle, new Vector3(0f, 0f, 0.05f), Vector3.zero, new Vector3(0.06f, 0.06f, 0.13f), matFlash);
        flash.SetActive(false);

        // --- слоты (XR Socket Interactor) ---
        var magSocketGo = Empty("MagazineSocket", root.transform, new Vector3(0f, -0.035f, 0.06f)).gameObject;
        var magTrigger = magSocketGo.AddComponent<SphereCollider>();
        magTrigger.isTrigger = true;
        magTrigger.radius = 0.06f;
        magTrigger.center = new Vector3(0f, -0.07f, 0.02f);
        var magSocket = magSocketGo.AddComponent<MagazineSocket>();
        magSocket.attachTransform = magSocketGo.transform;
        magSocket.showInteractableHoverMeshes = true;

        var scopeSocketGo = Empty("ScopeSocket", root.transform, new Vector3(0f, 0.065f, -0.02f)).gameObject;
        var scopeTrigger = scopeSocketGo.AddComponent<SphereCollider>();
        scopeTrigger.isTrigger = true;
        scopeTrigger.radius = 0.07f;
        scopeTrigger.center = new Vector3(0f, 0.035f, 0f);
        var scopeSocket = scopeSocketGo.AddComponent<ScopeSocket>();
        scopeSocket.attachTransform = scopeSocketGo.transform;
        scopeSocket.showInteractableHoverMeshes = true;

        // --- XR Grab Interactable (лабораторная №4) ---
        var grab = root.AddComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();
        grab.movementType = UnityEngine.XR.Interaction.Toolkit.Interactables.XRBaseInteractable.MovementType.Instantaneous;
        grab.attachTransform = gripAttach;
        grab.secondaryAttachTransform = handguardAttach;
        grab.selectMode = UnityEngine.XR.Interaction.Toolkit.Interactables.InteractableSelectMode.Multiple; // вторая рука на цевьё
        grab.useDynamicAttach = false;
        grab.throwOnDetach = true;
        grab.colliders.Clear();
        grab.colliders.AddRange(solid);

        Audio(root);

        var casings = root.AddComponent<CasingEjector>();
        casings.ejectionPort = ejectionPort;
        casings.casingMaterial = matBrass;

        var rifle = root.AddComponent<AKRifle>();
        rifle.muzzle = muzzle;
        rifle.ironSightOrigin = sight;
        rifle.recoilVisual = v;
        rifle.magazineSocket = magSocket;
        rifle.scopeSocket = scopeSocket;
        rifle.muzzleFlash = flash;
        rifle.ammoCounter = ammoCounter;
        rifle.casingEjector = casings;
        rifle.impactMaterial = matImpact;
        rifle.hitMask = hitMask;

        root.AddComponent<ItemHome>();
        RangePrimitives.SetLayerRecursively(root, grabLayer);
        return rifle;
    }

    // ------------------------------------------------------------------ магазин

    static Magazine BuildMagazine(string name, Vector3 position, Quaternion rotation)
    {
        var root = new GameObject(name);
        root.transform.SetPositionAndRotation(position, rotation);

        var rb = root.AddComponent<Rigidbody>();
        rb.mass = 0.45f;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

        // Изогнутый магазин из трёх сегментов, начало координат — горловина (верх)
        P(PrimitiveType.Cube, "Seg1", root.transform, new Vector3(0f, -0.045f, 0f), Vector3.zero, new Vector3(0.028f, 0.09f, 0.062f), matMagazine, true);
        P(PrimitiveType.Cube, "Seg2", root.transform, new Vector3(0f, -0.122f, 0.02f), new Vector3(-15f, 0f, 0f), new Vector3(0.028f, 0.07f, 0.064f), matMagazine, true);
        P(PrimitiveType.Cube, "Seg3", root.transform, new Vector3(0f, -0.185f, 0.052f), new Vector3(-30f, 0f, 0f), new Vector3(0.028f, 0.065f, 0.066f), matMagazine, true);
        P(PrimitiveType.Cube, "FeedLips", root.transform, new Vector3(0f, 0.004f, -0.005f), Vector3.zero, new Vector3(0.02f, 0.008f, 0.05f), matGunSteel);

        var grab = root.AddComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();
        grab.movementType = UnityEngine.XR.Interaction.Toolkit.Interactables.XRBaseInteractable.MovementType.Instantaneous;
        grab.useDynamicAttach = true;
        grab.throwOnDetach = true;

        var mag = root.AddComponent<Magazine>();
        root.AddComponent<ItemHome>();
        RangePrimitives.SetLayerRecursively(root, grabLayer);
        return mag;
    }

    // ------------------------------------------------------------------ прицел

    static WeaponScope BuildScope(string name, float magnification, float length, float objectiveRadius, Vector3 position)
    {
        const float axisY = 0.035f;

        var root = new GameObject(name);
        root.transform.SetPositionAndRotation(position, Quaternion.identity);

        var rb = root.AddComponent<Rigidbody>();
        rb.mass = 0.35f;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

        var t = root.transform;
        P(PrimitiveType.Cube, "Mount", t, new Vector3(0f, 0.012f, 0f), Vector3.zero, new Vector3(0.022f, 0.024f, 0.06f), matGunSteel);
        P(PrimitiveType.Cylinder, "Tube", t, new Vector3(0f, axisY, 0f), new Vector3(90f, 0f, 0f), new Vector3(0.032f, length * 0.5f, 0.032f), matScope);
        P(PrimitiveType.Cylinder, "Objective", t, new Vector3(0f, axisY, length * 0.5f), new Vector3(90f, 0f, 0f),
            new Vector3(objectiveRadius * 2f, 0.02f, objectiveRadius * 2f), matScope);
        P(PrimitiveType.Cylinder, "Eyepiece", t, new Vector3(0f, axisY, -length * 0.5f), new Vector3(90f, 0f, 0f), new Vector3(0.04f, 0.018f, 0.04f), matScope);
        P(PrimitiveType.Cylinder, "Turret", t, new Vector3(0f, axisY + 0.02f, 0f), Vector3.zero, new Vector3(0.02f, 0.008f, 0.02f), matScope);
        P(PrimitiveType.Quad, "FrontGlass", t, new Vector3(0f, axisY, length * 0.5f + 0.0205f), new Vector3(0f, 180f, 0f),
            new Vector3(objectiveRadius * 1.8f, objectiveRadius * 1.8f, 1f), matGlass);
        var lens = P(PrimitiveType.Quad, "Lens", t, new Vector3(0f, axisY, -length * 0.5f - 0.0185f), Vector3.zero, new Vector3(0.034f, 0.034f, 1f), matLens);

        Text3D(t, "Label", magnification + "x", new Vector3(-0.0175f, axisY, 0f), new Vector3(0f, 90f, 0f), 0.002f, 60, Color.white);

        // Камера прицела -> Render Texture -> линза (лабораторная №3)
        var camTransform = Empty("ScopeCamera", t, new Vector3(0f, axisY, length * 0.5f + 0.03f));
        var cam = camTransform.gameObject.AddComponent<Camera>();
        cam.enabled = false;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 300f;
        cam.depth = -10f;
        cam.fieldOfView = 24f / magnification;
        cam.stereoTargetEye = StereoTargetEyeMask.None;
        cam.cullingMask = hitMask;

        var box = root.AddComponent<BoxCollider>();
        box.center = new Vector3(0f, 0.03f, 0f);
        box.size = new Vector3(Mathf.Max(0.04f, objectiveRadius * 2f), 0.06f, length + 0.05f);

        var grab = root.AddComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();
        grab.movementType = UnityEngine.XR.Interaction.Toolkit.Interactables.XRBaseInteractable.MovementType.Instantaneous;
        grab.useDynamicAttach = true;
        grab.throwOnDetach = true;

        var scope = root.AddComponent<WeaponScope>();
        scope.magnification = magnification;
        scope.baseFieldOfView = 24f;
        scope.scopeCamera = cam;
        scope.lensRenderer = lens.GetComponent<Renderer>();

        root.AddComponent<ItemHome>();
        RangePrimitives.SetLayerRecursively(root, grabLayer);
        return scope;
    }

    // ------------------------------------------------------------------ кнопка сброса

    static void BuildResetButton(Vector3 position)
    {
        var root = new GameObject("ResetButton");
        root.transform.position = position;

        P(PrimitiveType.Cube, "Base", root.transform, new Vector3(0f, 0.015f, 0f), Vector3.zero, new Vector3(0.12f, 0.03f, 0.12f), matGunSteel, true);
        var cap = P(PrimitiveType.Cylinder, "Cap", root.transform, new Vector3(0f, 0.042f, 0f), Vector3.zero, new Vector3(0.08f, 0.012f, 0.08f), matButton);
        cap.AddComponent<BoxCollider>().size = new Vector3(1f, 2f, 1f);

        root.AddComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRSimpleInteractable>();
        Audio(root);
        var button = root.AddComponent<ResetButton>();
        button.buttonCap = cap.transform;

        var label = WorldCanvas("Label", root.transform, position + new Vector3(0f, 0.11f, 0f), Quaternion.identity, new Vector2(240f, 60f), 0.001f);
        UIText(label.transform, "Text", "СБРОС", 44, Color.white, Vector2.zero, Vector2.one, TextAnchor.MiddleCenter, FontStyle.Bold);
    }

    // ------------------------------------------------------------------ мишени

    static void BuildTargets(Transform parent)
    {
        float[] z = { 10f, 14f, 18f, 23f, 28f };
        float[] x = { -2f, 1.5f, -1f, 2f, 0f };
        float[] amplitude = { 2f, 2.5f, 3f, 3.5f, 4f };
        float[] speed = { 0.9f, 0.45f, 0.7f, 0.35f, 0.6f };
        float[] radii = { 0.30f, 0.24f, 0.18f, 0.12f, 0.06f };
        Material[] ringMats = { matWhite, matBlack, matWhite, matBlack, matRed };

        for (int i = 0; i < z.Length; i++)
        {
            var root = new GameObject("Target_" + (i + 1));
            root.transform.SetParent(parent, false);
            root.transform.position = new Vector3(x[i], 1.5f, z[i]);

            var rb = root.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;

            var box = root.AddComponent<BoxCollider>();
            box.size = new Vector3(0.64f, 0.64f, 0.04f);

            P(PrimitiveType.Cube, "Card", root.transform, new Vector3(0f, 0f, 0.012f), Vector3.zero, new Vector3(0.64f, 0.64f, 0.01f), matCard);
            Renderer bullseye = null;
            for (int r = 0; r < radii.Length; r++)
            {
                var ring = P(PrimitiveType.Cylinder, "Ring_" + r, root.transform, new Vector3(0f, 0f, -0.0015f * r), new Vector3(90f, 0f, 0f),
                    new Vector3(radii[r] * 2f, 0.01f, radii[r] * 2f), ringMats[r]);
                bullseye = ring.GetComponent<Renderer>();
            }
            P(PrimitiveType.Cube, "Pole", root.transform, new Vector3(0f, -0.9f, 0.03f), Vector3.zero, new Vector3(0.04f, 1.2f, 0.04f), matMetal);
            P(PrimitiveType.Cube, "Base", root.transform, new Vector3(0f, -1.48f, 0.03f), Vector3.zero, new Vector3(0.3f, 0.04f, 0.3f), matMetal);

            P(PrimitiveType.Cube, "Rail_" + (i + 1), parent, new Vector3(x[i], 0.01f, z[i] + 0.03f), Vector3.zero,
                new Vector3(amplitude[i] * 2f + 0.4f, 0.02f, 0.08f), matMetal);

            var target = root.AddComponent<ShootingTarget>();
            target.faceRadius = radii[0];
            target.bullseyeRadius = radii[radii.Length - 1];
            target.bullseyeRenderer = bullseye;
            Audio(root, 0.3f);

            var mover = root.AddComponent<TargetMover>();
            mover.travel = new Vector3(amplitude[i], 0f, 0f);
            mover.speed = speed[i];
            mover.phase = i * 1.3f;
            mover.pattern = i % 2 == 0 ? TargetMover.Pattern.Smooth : TargetMover.Pattern.Linear;
        }
    }

    // ------------------------------------------------------------------ табло

    static void BuildScoreboard(Transform parent)
    {
        var frame = Empty("Scoreboard", parent, Vector3.zero);
        P(PrimitiveType.Cube, "PillarLeft", frame, new Vector3(-7.1f, 2.9f, 8.1f), Vector3.zero, new Vector3(0.2f, 5.8f, 0.2f), matMetal, true);
        P(PrimitiveType.Cube, "PillarRight", frame, new Vector3(7.1f, 2.9f, 8.1f), Vector3.zero, new Vector3(0.2f, 5.8f, 0.2f), matMetal, true);
        P(PrimitiveType.Cube, "Beam", frame, new Vector3(0f, 5.7f, 8.1f), Vector3.zero, new Vector3(14.4f, 0.2f, 0.2f), matMetal, true);
        P(PrimitiveType.Cube, "BoardBack", frame, new Vector3(0f, 4.5f, 8.06f), Vector3.zero, new Vector3(6.2f, 1.9f, 0.08f), matBoard, true);
        foreach (float cx in new[] { -2.5f, 2.5f })
            P(PrimitiveType.Cube, "Hanger", frame, new Vector3(cx, 5.53f, 8.06f), Vector3.zero, new Vector3(0.05f, 0.16f, 0.05f), matMetal);

        var canvas = WorldCanvas("ScoreboardCanvas", frame, new Vector3(0f, 4.5f, 8.0f), Quaternion.identity, new Vector2(600f, 180f), 0.01f);
        UIText(canvas.transform, "Title", "ПОПАДАНИЙ В ЦЕНТР МИШЕНИ", 30, Color.white, new Vector2(0f, 0.74f), new Vector2(1f, 0.98f), TextAnchor.MiddleCenter, FontStyle.Bold);
        var center = UIText(canvas.transform, "CenterHits", "0", 90, new Color(1f, 0.85f, 0.1f), new Vector2(0f, 0.22f), new Vector2(1f, 0.76f), TextAnchor.MiddleCenter, FontStyle.Bold);
        var stats = UIText(canvas.transform, "Stats", "", 22, new Color(0.8f, 0.85f, 0.9f), new Vector2(0f, 0.02f), new Vector2(1f, 0.24f));

        var board = canvas.gameObject.AddComponent<ScoreBoard>();
        board.centerHitsText = center;
        board.statsText = stats;
    }

    // ------------------------------------------------------------------ игрок

    static void BuildRig(string prefabPath)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        var rig = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        rig.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        ConfigureRig(rig);
    }

    /// <summary>
    /// Запасной вариант: игрок (любой префаб с XR Origin) уже лежит в открытой сцене —
    /// добавляем ему руки, ходьбу, слой Player, менеджеры взаимодействия и ввода.
    /// </summary>
    [MenuItem("Тир VR/Настроить игрока в открытой сцене", priority = 1)]
    public static void ConfigureRigInOpenScene()
    {
        GameObject rig = null;
        var scene = SceneManager.GetActiveScene();
        foreach (var root in scene.GetRootGameObjects())
        {
            if (FindComponentByTypeName(root, "XROrigin") != null)
            {
                rig = root;
                break;
            }
        }
        if (rig == null)
        {
            EditorUtility.DisplayDialog("Тир VR",
                "В сцене нет объекта с компонентом XR Origin.\n\n" +
                "Перетащи в иерархию префаб игрока из Samples/XR Interaction Toolkit/<версия>/Starter Assets/Prefabs " +
                "(например \"Complete XR Origin Set Up\" или \"XR Origin (XR Rig)\") и повтори.", "OK");
            return;
        }

        PrepareRigSettings();
        RemoveExtraCameras(scene, rig);

        rig.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        ConfigureRig(rig);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Selection.activeGameObject = rig;
        EditorUtility.DisplayDialog("Тир VR", "Игрок \"" + rig.name + "\" настроен, сцена сохранена.", "OK");
    }

    /// <summary>
    /// Проверяет всё, из-за чего игрок может не управляться, и чинит что можно.
    /// Работает на открытой сцене.
    /// </summary>
    [MenuItem("Тир VR/Проверить и починить управление", priority = 2)]
    public static void FixControls()
    {
        var report = new System.Text.StringBuilder();
        var scene = SceneManager.GetActiveScene();

        // 1. Новая Input System (без неё XR Interaction Toolkit не получает ввод)
        var playerSettingsAsset = Unsupported.GetSerializedAssetInterfaceSingleton("PlayerSettings");
        if (playerSettingsAsset != null)
        {
            var playerSettings = new SerializedObject(playerSettingsAsset);
            var inputHandler = playerSettings.FindProperty("activeInputHandler");
            if (inputHandler != null && inputHandler.intValue == 0)
            {
                inputHandler.intValue = 2; // Both
                playerSettings.ApplyModifiedProperties();
                AssetDatabase.SaveAssets();
                if (EditorUtility.DisplayDialog("Тир VR",
                        "Новая Input System была ВЫКЛЮЧЕНА (Active Input Handling = Input Manager Old) — " +
                        "из-за этого контроллеры не реагируют.\n\nВключил режим Both. Нужно перезапустить Unity.",
                        "Перезапустить сейчас", "Позже"))
                {
                    EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();
                    EditorApplication.OpenProject(Directory.GetCurrentDirectory());
                    return;
                }
                report.AppendLine("• Включил новую Input System — ПЕРЕЗАПУСТИ Unity.");
            }
            else report.AppendLine("• Input System включена — OK");
        }

        // 2. Игрок в сцене (ровно один)
        var rigs = new System.Collections.Generic.List<GameObject>();
        foreach (var root in scene.GetRootGameObjects())
            if (FindComponentByTypeName(root, "XROrigin") != null) rigs.Add(root);
        if (rigs.Count == 0)
        {
            EditorUtility.DisplayDialog("Тир VR",
                "В открытой сцене нет игрока (XR Origin).\nПеретащи префаб игрока из Starter Assets/Prefabs в сцену и запусти проверку снова.", "OK");
            return;
        }
        var rig = rigs[0];
        for (int i = 1; i < rigs.Count; i++)
        {
            rigs[i].SetActive(false);
            report.AppendLine("• В сцене было несколько игроков — выключил лишний: " + rigs[i].name);
        }

        // 3. Лишние камеры, симулятор, лежащий в сцене (его теперь создаёт XRModeSelector)
        int removedCameras = RemoveExtraCameras(scene, rig);
        if (removedCameras > 0) report.AppendLine("• Удалил лишних камер: " + removedCameras);
        foreach (var root in scene.GetRootGameObjects())
        {
            if (root != rig && FindComponentByTypeName(root, "XRDeviceSimulator") != null)
            {
                Object.DestroyImmediate(root);
                report.AppendLine("• Убрал XR Device Simulator из сцены — теперь он создаётся сам, если шлема нет");
            }
        }

        // 4. Менеджеры и настройка игрока
        if (Object.FindObjectOfType<XRInteractionManager>() == null) report.AppendLine("• Добавил XR Interaction Manager");
        var actionManager = Object.FindObjectOfType<InputActionManager>();
        if (actionManager == null) report.AppendLine("• Добавил Input Action Manager (без него кнопки не работают)");
        else if (actionManager.actionAssets == null || actionManager.actionAssets.Count == 0 || actionManager.actionAssets.Contains(null))
        {
            var actions = FindDefaultActions();
            if (actions != null)
            {
                actionManager.actionAssets = new System.Collections.Generic.List<InputActionAsset> { actions };
                EditorUtility.SetDirty(actionManager);
                report.AppendLine("• В Input Action Manager не было действий — назначил XRI Default Input Actions");
            }
        }

        // Скрипт System_Input из ЛР2 ставит Time.timeScale = 0 до нажатия кнопки — игрок "замерзает"
        foreach (var root in scene.GetRootGameObjects())
        {
            var systemInput = FindComponentByTypeName(root, "System_Input");
            if (systemInput != null)
                report.AppendLine("• ВНИМАНИЕ: на \"" + systemInput.gameObject.name + "\" есть System_Input из ЛР2 — он ставит Time.timeScale = 0 " +
                                  "и всё стоит, пока не нажата кнопка выбора. Для тира выключи этот объект.");
        }

        PrepareRigSettings();
        ConfigureRig(rig);
        report.AppendLine("• Игрок настроен: руки, ходьба, слой Player, автовыбор шлем/симулятор");

        if (FindSimulatorPrefab() == null)
            report.AppendLine("• ВНИМАНИЕ: не найден префаб XR Device Simulator — импортируй sample \"XR Device Simulator\" в Package Manager, иначе без шлема управлять нельзя");

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        report.AppendLine();
        report.AppendLine("Не забудь: Project Settings → XR Plug-in Management → OpenXR включён (для шлема).");
        report.AppendLine("Без шлема: после Play кликни мышкой в окно Game, иначе клавиатура не работает.");
        Debug.Log("Тир VR — проверка управления:\n" + report);
        EditorUtility.DisplayDialog("Тир VR — проверка управления", report.ToString(), "OK");
    }

    static void PrepareRigSettings()
    {
        grabLayer = EnsureLayer(RangePrimitives.GrabbableLayerName);
        playerLayer = EnsureLayer(RangePrimitives.PlayerLayerName);
        if (matSkin == null) matSkin = AssetDatabase.LoadAssetAtPath<Material>(GeneratedFolder + "/Skin.mat");
        if (matSkin == null)
        {
            EnsureFolder("Assets", "ShootingRange");
            EnsureFolder(RootFolder, "Generated");
            matSkin = Mat("Skin", new Color(0.87f, 0.68f, 0.56f), 0f, 0.3f);
        }
    }

    // Обычная камера сцены не нужна — у игрока своя
    static int RemoveExtraCameras(Scene scene, GameObject rig)
    {
        int removed = 0;
        foreach (var root in scene.GetRootGameObjects())
        {
            if (root != rig && root.GetComponent<Camera>() != null)
            {
                Object.DestroyImmediate(root);
                removed++;
            }
        }
        return removed;
    }

    static GameObject FindSimulatorPrefab()
    {
        foreach (var guid in AssetDatabase.FindAssets("t:Prefab XR Device Simulator"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (Path.GetFileNameWithoutExtension(path) == "XR Device Simulator")
                return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }
        return null;
    }

    static InputActionAsset FindDefaultActions()
    {
        foreach (var guid in AssetDatabase.FindAssets("t:InputActionAsset XRI Default Input Actions"))
        {
            var actions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(AssetDatabase.GUIDToAssetPath(guid));
            if (actions != null) return actions;
        }
        return null;
    }

    // Шлем подключён — работает шлем, нет — сам создаётся XR Device Simulator
    static void EnsureModeSelector()
    {
        var selector = Object.FindObjectOfType<XRModeSelector>();
        if (selector == null) selector = new GameObject("XR Mode Selector (шлем / симулятор)").AddComponent<XRModeSelector>();
        if (selector.simulatorPrefab == null)
        {
            selector.simulatorPrefab = FindSimulatorPrefab();
            EditorUtility.SetDirty(selector);
        }
    }

    static void ConfigureRig(GameObject rig)
    {
        EnsureInteractionManager(rig);
        EnsureInputActionManager(rig);
        EnsureModeSelector();
        EnsureBodyBlocker(rig);

        // Тело игрока (Character Controller) — на слой Player
        GameObject body = null;
        var characterController = rig.GetComponentInChildren<CharacterController>(true);
        if (characterController != null) body = characterController.gameObject;
        if (body == null)
        {
            foreach (var mb in rig.GetComponentsInChildren<MonoBehaviour>(true))
                if (mb != null && mb.GetType().Name == "XROrigin") { body = mb.gameObject; break; }
            if (body == null) body = rig;
            var cc = body.AddComponent<CharacterController>();
            cc.height = 1.7f;
            cc.radius = 0.2f;
            cc.center = new Vector3(0f, 0.85f, 0f);
        }
        if (playerLayer >= 0) body.layer = playerLayer;

        // Левый стик — плавная ходьба (Continuous Move), правый — поворот/телепорт
        foreach (var mb in rig.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (mb == null || mb.GetType().Name != "ActionBasedControllerManager") continue;
            var so = new SerializedObject(mb);
            var smooth = so.FindProperty("m_SmoothMotionEnabled");
            if (smooth != null)
            {
                smooth.boolValue = mb.gameObject.name.ToLower().Contains("left");
                so.ApplyModifiedProperties();
            }
        }

        // Руки
        int handsAdded = 0;
        var controllers = rig.GetComponentsInChildren<XRBaseController>(true);
        foreach (var controller in controllers)
        {
            if (controller.GetComponentInChildren<UnityEngine.XR.Interaction.Toolkit.Interactors.XRDirectInteractor>(true) == null) continue;
            AddHand(controller.gameObject);
            handsAdded++;
        }
        if (handsAdded == 0)
        {
            foreach (var controller in controllers)
            {
                string n = controller.gameObject.name.ToLower();
                if (n.Contains("teleport") || n.Contains("locomotion")) continue;
                AddHand(controller.gameObject);
            }
        }
        // Рука уже была настроена старой версией — добавим помощника захвата
        foreach (var hand in rig.GetComponentsInChildren<HandPresence>(true))
            if (hand.GetComponent<SimulatorGrabAssist>() == null)
                hand.gameObject.AddComponent<SimulatorGrabAssist>();
    }

    static void EnsureInteractionManager(GameObject rig)
    {
        if (Object.FindObjectOfType<XRInteractionManager>() != null) return;
        new GameObject("XR Interaction Manager").AddComponent<XRInteractionManager>();
    }

    // Без Input Action Manager действия контроллеров не включаются и игрок не реагирует на кнопки
    static void EnsureInputActionManager(GameObject rig)
    {
        if (Object.FindObjectOfType<InputActionManager>() != null) return;

        var actions = FindDefaultActions();
        if (actions == null)
        {
            Debug.LogWarning("Тир VR: не найден ассет \"XRI Default Input Actions\" (Starter Assets) — назначь Input Action Manager вручную.");
            return;
        }

        var manager = rig.AddComponent<InputActionManager>();
        manager.actionAssets = new System.Collections.Generic.List<InputActionAsset> { actions };
    }

    static void AddHand(GameObject controller)
    {
        if (controller.GetComponent<HandPresence>() != null) return;
        var hand = controller.AddComponent<HandPresence>();
        hand.isLeftHand = controller.name.ToLower().Contains("left");
        hand.skinMaterial = matSkin;
        if (controller.GetComponent<SimulatorGrabAssist>() == null)
            controller.AddComponent<SimulatorGrabAssist>();
    }

    // Не даём пройти головой/телом сквозь стены и стол
    static void EnsureBodyBlocker(GameObject rig)
    {
        var origin = FindComponentByTypeName(rig, "XROrigin");
        var target = origin != null ? origin.gameObject : rig;
        if (target.GetComponent<BodyCollisionBlocker>() != null) return;

        var blocker = target.AddComponent<BodyCollisionBlocker>();
        var cam = target.GetComponentInChildren<Camera>(true);
        if (cam != null) blocker.head = cam.transform;
    }
}
