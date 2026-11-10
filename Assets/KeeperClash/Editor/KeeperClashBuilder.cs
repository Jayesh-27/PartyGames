using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using TMPro;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.Rendering.Universal.ShaderGUI;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>
/// Builds the Keeper Clash scene.
/// Tools > Keeper Clash > Build Scene      : copies SampleScene (rig, Relay keypad, NetworkManager), strips the grinder
///                                          content and generates the arena, ball, match, bot, scoreboard, effects
///                                          and post-processing. Re-runnable: generated objects are rebuilt.
/// Tools > Keeper Clash > Make Startup Scene : moves the scene to the top of Build Settings for a Quest build.
/// Tools > Keeper Clash > Run Self-Test   : simulates hundreds of shots and a bot-vs-bot match.
/// Tools > Keeper Clash > Capture Screenshots : saves PNGs to <project>/Screenshots.
/// Batch: Unity.exe -batchmode -projectPath ... -executeMethod KeeperClashBuilder.BatchBuild
/// </summary>
public static class KeeperClashBuilder
{
    const string Root = "Assets/KeeperClash";
    const string SourceScene = "Assets/Scenes/SampleScene.unity";
    public const string ScenePath = "Assets/Scenes/KeeperClash.unity";

    static readonly Color Blue = new Color(0.1f, 0.75f, 1f);
    static readonly Color Red = new Color(1f, 0.18f, 0.35f);
    static readonly Color Neutral = new Color(0.7f, 0.8f, 1f);

    // materials (filled by MakeMaterials)
    static Material mFloor, mDark, mGlass, mCeiling, mLine, mNeonBlue, mNeonRed, mNeonWhite, mFrameBlue, mFrameRed,
        mNetBlue, mNetRed, mBoardBlue, mBoardRed, mBall, mGlow, mParticle, mShadow, mShock, mBotBody, mBotPaddle, mGoalFloorBlue, mGoalFloorRed;

    static readonly List<Renderer> blueNeon = new List<Renderer>();
    static readonly List<Renderer> redNeon = new List<Renderer>();
    static readonly List<Renderer> blueFrame = new List<Renderer>();
    static readonly List<Renderer> redFrame = new List<Renderer>();

    // ================================================================== menu

    [MenuItem("Tools/Keeper Clash/Build Scene")]
    public static void BuildSceneMenu()
    {
        BuildScene();
        CaptureScreenshots();
    }

    [MenuItem("Tools/Keeper Clash/Make Startup Scene")]
    public static void MakeStartupScene()
    {
        var list = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        list.RemoveAll(s => s.path == ScenePath);
        list.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
        EditorBuildSettings.scenes = list.ToArray();
        Debug.Log("[KeeperClash] KeeperClash is now the first scene in Build Settings.");
    }

    [MenuItem("Tools/Keeper Clash/Run Self-Test")]
    public static void SelfTestMenu() => SelfTest();

    [MenuItem("Tools/Keeper Clash/Capture Screenshots")]
    public static void CaptureMenu() => CaptureScreenshots();

    /// <summary>Command line entry: self-test, build the scene, screenshots, then quit with 0 (ok) / 1 (error) / 2 (test failed).</summary>
    public static void BatchBuild()
    {
        int code = 0;
        try
        {
            bool ok = SelfTest();
            BuildScene();
            CaptureScreenshots();
            if (!ok) code = 2;
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            code = 1;
        }
        Debug.Log($"[KeeperClash] BATCH DONE exit={code}");
        EditorApplication.Exit(code);
    }

    /// <summary>Command line entry: only the self-test (leaves the scene alone).</summary>
    public static void BatchTest()
    {
        int code;
        try { code = SelfTest() ? 0 : 2; }
        catch (Exception e) { Debug.LogException(e); code = 1; }
        Debug.Log($"[KeeperClash] BATCH DONE exit={code}");
        EditorApplication.Exit(code);
    }

    // ================================================================== scene

    public static void BuildScene()
    {
        EnsureFolder(Root, "Materials");
        EnsureFolder(Root, "Textures");
        EnsureFolder(Root, "Profiles");
        EnsureFolder(Root, "Meshes");

        if (!File.Exists(ScenePath))
        {
            if (!AssetDatabase.CopyAsset(SourceScene, ScenePath)) throw new Exception("Could not copy " + SourceScene);
            AssetDatabase.Refresh();
        }

        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        StripScene(scene);

        blueNeon.Clear(); redNeon.Clear(); blueFrame.Clear(); redFrame.Clear();
        MakeMaterials();

        KeeperArenaSpec a = new KeeperArenaSpec();
        GameObject arena = new GameObject("Keeper_Arena");
        arena.AddComponent<KeeperArena>().spec = a;
        BuildArena(arena.transform, a);

        KeeperBall ball = BuildBall(a);
        BuildMatch();
        BuildBot(a);
        BuildScoreboard();
        BuildFx();
        RelayNetworkButtons keypad = SetupLobby();
        BuildPlayer(keypad);
        SetupRendering();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        RefreshNetworkObjectHashes();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        var list = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        if (!list.Exists(s => s.path == ScenePath))
        {
            list.Add(new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = list.ToArray();
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[KeeperClash] Scene built: {ScenePath} (ball at {ball.transform.position})");
    }

    static void StripScene(UnityEngine.SceneManagement.Scene scene)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            bool keep =
                root.GetComponentInChildren<OVRCameraRig>(true) != null ||
                root.GetComponent<NetworkManager>() != null ||
                root.GetComponent<RelayNetworkButtons>() != null ||
                root.GetComponent<EventSystem>() != null ||
                (root.GetComponent<Volume>() != null && root.GetComponent<Volume>().isGlobal) ||
                (root.GetComponent<Light>() != null && root.GetComponent<Light>().type == LightType.Directional);
            if (!keep || root.name.StartsWith("Keeper_")) Object.DestroyImmediate(root);
        }
    }

    // ================================================================== materials and textures

    static void MakeMaterials()
    {
        const string unlit = "Universal Render Pipeline/Unlit";
        const string simpleLit = "Universal Render Pipeline/Simple Lit";
        const string particles = "Universal Render Pipeline/Particles/Unlit";

        Texture2D dot = SaveTexture("SoftDot", 64, (u, v) =>
        {
            float d = Mathf.Clamp01(1f - Vector2.Distance(new Vector2(u, v), new Vector2(0.5f, 0.5f)) * 2f);
            return new Color(1f, 1f, 1f, d * d);
        }, false);
        Texture2D blob = SaveTexture("BlobShadow", 64, (u, v) =>
        {
            float d = Mathf.Clamp01(1f - Vector2.Distance(new Vector2(u, v), new Vector2(0.5f, 0.5f)) * 2f);
            return new Color(1f, 1f, 1f, Mathf.SmoothStep(0f, 1f, d));
        }, false);
        Texture2D net = SaveTexture("NetGrid", 64, (u, v) =>
        {
            float gx = Mathf.Abs((u * 4f) % 1f - 0.5f), gy = Mathf.Abs((v * 4f) % 1f - 0.5f);
            float line = Mathf.Max(gx, gy) > 0.44f ? 1f : 0.08f;
            return new Color(1f, 1f, 1f, line);
        }, true);

        mFloor = Mat("Floor", simpleLit, new Color(0.035f, 0.045f, 0.085f));
        mFloor.SetFloat("_Smoothness", 0.75f);
        mDark = Mat("DarkPanel", simpleLit, new Color(0.04f, 0.045f, 0.07f));
        mGlass = Mat("Glass", unlit, new Color(0.35f, 0.55f, 1f, 0.06f), 1, 0);
        mCeiling = Mat("Ceiling", unlit, new Color(0.02f, 0.03f, 0.08f, 0.55f), 1, 0);
        mLine = Mat("FieldLine", unlit, new Color(0.55f, 0.62f, 0.8f));
        mNeonBlue = Mat("NeonBlue", unlit, Blue);
        mNeonRed = Mat("NeonRed", unlit, Red);
        mNeonWhite = Mat("NeonWhite", unlit, Neutral);
        mFrameBlue = Mat("GoalFrameBlue", unlit, Blue);
        mFrameRed = Mat("GoalFrameRed", unlit, Red);
        mNetBlue = Mat("NetBlue", unlit, new Color(Blue.r, Blue.g, Blue.b, 0.55f), 1, 0, net, new Vector2(6f, 4f));
        mNetRed = Mat("NetRed", unlit, new Color(Red.r, Red.g, Red.b, 0.55f), 1, 0, net, new Vector2(6f, 4f));
        mBoardBlue = Mat("BackboardBlue", unlit, new Color(Blue.r * 0.5f, Blue.g * 0.5f, Blue.b * 0.6f, 0.14f), 1, 0);
        mBoardRed = Mat("BackboardRed", unlit, new Color(Red.r * 0.6f, Red.g * 0.5f, Red.b * 0.5f, 0.14f), 1, 0);
        mGoalFloorBlue = Mat("GoalFloorBlue", unlit, Blue * 0.35f);
        mGoalFloorRed = Mat("GoalFloorRed", unlit, Red * 0.35f);
        mBall = Mat("Ball", unlit, new Color(0.2f, 0.85f, 1f));
        mGlow = Mat("BallGlow", particles, new Color(0.2f, 0.85f, 1f, 0.5f), 1, 2, dot);
        mParticle = Mat("Spark", particles, Color.white, 1, 2, dot);
        mShadow = Mat("BallShadow", unlit, new Color(0f, 0f, 0f, 0.55f), 1, 0, blob);
        mShock = Mat("Shockwave", unlit, new Color(1f, 1f, 1f, 0.4f), 1, 2);
        mBotBody = Mat("BotBody", simpleLit, new Color(0.12f, 0.12f, 0.16f));
        mBotPaddle = Mat("BotPaddle", unlit, new Color(Red.r, Red.g, Red.b, 0.35f), 1, 0);
        AssetDatabase.SaveAssets();
    }

    static Material Mat(string name, string shaderName, Color color, int surface = 0, int blend = 0, Texture tex = null, Vector2? tiling = null)
    {
        string path = $"{Root}/Materials/{name}.mat";
        Shader shader = Shader.Find(shaderName);
        if (shader == null) throw new Exception("Shader not found: " + shaderName);

        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(shader);
            AssetDatabase.CreateAsset(m, path);
        }
        else m.shader = shader;

        m.SetColor("_BaseColor", color);
        if (m.HasProperty("_Color")) m.SetColor("_Color", color);
        if (tex != null)
        {
            m.SetTexture("_BaseMap", tex);
            m.SetTextureScale("_BaseMap", tiling ?? Vector2.one);
        }
        m.SetFloat("_Surface", surface);
        m.SetFloat("_Blend", blend);
        if (surface == 1 && m.HasProperty("_Cull")) m.SetFloat("_Cull", 0f);

        if (shaderName.Contains("Particles")) BaseShaderGUI.SetMaterialKeywords(m, null, ParticleGUI.SetMaterialKeywords);
        else BaseShaderGUI.SetMaterialKeywords(m);

        m.enableInstancing = true;
        EditorUtility.SetDirty(m);
        return m;
    }

    static Texture2D SaveTexture(string name, int size, Func<float, float, Color> f, bool repeat)
    {
        string path = $"{Root}/Textures/{name}.png";
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                tex.SetPixel(x, y, f((x + 0.5f) / size, (y + 0.5f) / size));
        tex.Apply();
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);

        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        TextureImporter imp = (TextureImporter)AssetImporter.GetAtPath(path);
        imp.alphaIsTransparency = true;
        imp.alphaSource = TextureImporterAlphaSource.FromInput;
        imp.wrapMode = repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
        imp.mipmapEnabled = true;
        imp.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    // ================================================================== geometry helpers

    static GameObject Box(string name, Transform parent, Vector3 center, Vector3 size, Material mat, bool isStatic = true)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = center;
        go.transform.localScale = size;
        Renderer r = go.GetComponent<Renderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = ShadowCastingMode.Off;
        go.isStatic = isStatic;
        return go;
    }

    static GameObject Cylinder(string name, Transform parent, Vector3 from, Vector3 to, float radius, Material mat)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = name;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        Vector3 d = to - from;
        go.transform.localPosition = (from + to) * 0.5f;
        go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, d.normalized);
        go.transform.localScale = new Vector3(radius * 2f, d.magnitude * 0.5f, radius * 2f);
        Renderer r = go.GetComponent<Renderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = ShadowCastingMode.Off;
        go.isStatic = true;
        return go;
    }

    static GameObject Sphere(string name, Transform parent, Vector3 pos, float diameter, Material mat)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = name;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = Vector3.one * diameter;
        Renderer r = go.GetComponent<Renderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = ShadowCastingMode.Off;
        return go;
    }

    static GameObject Ring(string name, Transform parent, Vector3 center, float radius, float width, Material mat)
    {
        string path = $"{Root}/Meshes/Ring_{radius:0.00}_{width:0.00}.asset";
        Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (mesh == null)
        {
            const int seg = 72;
            var verts = new Vector3[seg * 2];
            var tris = new int[seg * 6];
            for (int i = 0; i < seg; i++)
            {
                float ang = i / (float)seg * Mathf.PI * 2f;
                Vector3 dir = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
                verts[i * 2] = dir * (radius - width * 0.5f);
                verts[i * 2 + 1] = dir * (radius + width * 0.5f);
                int n = (i + 1) % seg;
                int t = i * 6;
                tris[t] = i * 2; tris[t + 1] = n * 2; tris[t + 2] = i * 2 + 1;
                tris[t + 3] = i * 2 + 1; tris[t + 4] = n * 2; tris[t + 5] = n * 2 + 1;
            }
            mesh = new Mesh { name = "Ring", vertices = verts, triangles = tris };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, path);
        }
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = center;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = ShadowCastingMode.Off;
        go.isStatic = true;
        return go;
    }

    static TextMeshPro Text(string name, Transform parent, Vector3 pos, Quaternion rot, Vector2 size, string text, Color color, float maxSize = 30f)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localRotation = rot;
        TextMeshPro t = go.AddComponent<TextMeshPro>();
        if (t.font == null) t.font = TMP_Settings.defaultFontAsset;
        t.rectTransform.sizeDelta = size;
        t.text = text;
        t.color = color;
        t.alignment = TextAlignmentOptions.Center;
        t.enableAutoSizing = true;
        t.fontSizeMin = 0.1f;
        t.fontSizeMax = maxSize;
        t.textWrappingMode = TextWrappingModes.Normal;
        t.richText = true;
        return t;
    }

    static Material NeonFor(float z) => z < 0f ? mNeonBlue : mNeonRed;

    static void Track(GameObject go, int side)
    {
        Renderer r = go.GetComponent<Renderer>();
        if (side == 0) blueNeon.Add(r); else redNeon.Add(r);
    }

    // ================================================================== arena

    static void BuildArena(Transform root, KeeperArenaSpec a)
    {
        float hl = a.HalfLength, hw = a.HalfWidth, h = a.height, gw = a.goalWidth, gh = a.goalHeight, gd = a.goalDepth;

        // floor and field lines
        Box("Floor", root, new Vector3(0f, -0.05f, 0f), new Vector3(a.width + 0.4f, 0.1f, a.length + gd * 2f + 0.4f), mFloor);
        Transform lines = new GameObject("Lines").transform;
        lines.SetParent(root, false);
        Box("CenterLine", lines, new Vector3(0f, 0.004f, 0f), new Vector3(a.width, 0.008f, 0.06f), mLine);
        Ring("CenterCircle", lines, new Vector3(0f, 0.005f, 0f), 1.6f, 0.06f, mLine);
        for (int side = 0; side < 2; side++)
        {
            float s = KeeperArenaSpec.Sign(side);
            Material team = side == 0 ? mNeonBlue : mNeonRed;
            float boxZ = s * (hl - 2.5f);
            Track(Box("BoxFront", lines, new Vector3(0f, 0.004f, boxZ), new Vector3(5f, 0.008f, 0.05f), team), side);
            Track(Box("BoxLeft", lines, new Vector3(-2.5f, 0.004f, s * (hl - 1.25f)), new Vector3(0.05f, 0.008f, 2.5f), team), side);
            Track(Box("BoxRight", lines, new Vector3(2.5f, 0.004f, s * (hl - 1.25f)), new Vector3(0.05f, 0.008f, 2.5f), team), side);
            Track(Ring("KeeperSpot", lines, a.KeeperSpot(side) + Vector3.up * 0.006f, 0.35f, 0.04f, team), side);
            Box(side == 0 ? "GoalFloorBlue" : "GoalFloorRed", lines, new Vector3(0f, 0.003f, s * (hl + gd * 0.5f)), new Vector3(gw, 0.006f, gd), side == 0 ? mGoalFloorBlue : mGoalFloorRed);
        }

        // side walls: glass with team-coloured neon edges and pillars
        Transform walls = new GameObject("Walls").transform;
        walls.SetParent(root, false);
        for (int i = -1; i <= 1; i += 2)
        {
            float x = i * hw;
            Box("SideGlass", walls, new Vector3(x, h * 0.5f, 0f), new Vector3(0.02f, h, a.length), mGlass);
            for (int half = 0; half < 2; half++)
            {
                float zc = (half == 0 ? -1f : 1f) * hl * 0.5f;
                Material m = half == 0 ? mNeonBlue : mNeonRed;
                Track(Box("EdgeLow", walls, new Vector3(x - i * 0.03f, 0.04f, zc), new Vector3(0.05f, 0.05f, hl), m), half);
                Track(Box("EdgeHigh", walls, new Vector3(x - i * 0.03f, h, zc), new Vector3(0.05f, 0.05f, hl), m), half);
            }
            foreach (float z in new[] { -hl, -hl * 0.5f, 0f, hl * 0.5f, hl })
            {
                Material m = z < -0.1f ? mNeonBlue : z > 0.1f ? mNeonRed : mNeonWhite;
                GameObject p = Box("Pillar", walls, new Vector3(x, h * 0.5f, z), new Vector3(0.08f, h, 0.08f), m);
                if (z < -0.1f) Track(p, 0); else if (z > 0.1f) Track(p, 1);
            }
        }

        // ceiling
        Box("Ceiling", walls, new Vector3(0f, h + 0.01f, 0f), new Vector3(a.width, 0.02f, a.length), mCeiling);
        foreach (float z in new[] { -hl * 0.5f, 0f, hl * 0.5f })
            Box("CeilingStrip", walls, new Vector3(0f, h - 0.02f, z), new Vector3(a.width, 0.04f, 0.06f), z == 0f ? mNeonWhite : NeonFor(z));

        // ends: backboards around each goal, the goal frames and nets
        for (int side = 0; side < 2; side++)
        {
            float s = KeeperArenaSpec.Sign(side);
            float z = s * (hl + 0.02f);
            Material board = side == 0 ? mBoardBlue : mBoardRed;
            Material neon = side == 0 ? mNeonBlue : mNeonRed;
            Material frameMat = side == 0 ? mFrameBlue : mFrameRed;
            Material netMat = side == 0 ? mNetBlue : mNetRed;
            Transform end = new GameObject(side == 0 ? "BlueEnd" : "RedEnd").transform;
            end.SetParent(root, false);

            float sideW = hw - gw * 0.5f;
            Box("BoardLeft", end, new Vector3(-(gw * 0.5f + sideW * 0.5f), h * 0.5f, z), new Vector3(sideW, h, 0.04f), board);
            Box("BoardRight", end, new Vector3(gw * 0.5f + sideW * 0.5f, h * 0.5f, z), new Vector3(sideW, h, 0.04f), board);
            Box("BoardTop", end, new Vector3(0f, gh + (h - gh) * 0.5f, z), new Vector3(gw, h - gh, 0.04f), board);
            Track(Box("BoardEdgeTop", end, new Vector3(0f, h, s * (hl - 0.03f)), new Vector3(a.width, 0.05f, 0.05f), neon), side);
            Track(Box("BoardEdgeLow", end, new Vector3(0f, 0.04f, s * (hl - 0.03f)), new Vector3(a.width, 0.05f, 0.05f), neon), side);

            List<Renderer> frames = side == 0 ? blueFrame : redFrame;
            float lz = s * hl;
            frames.Add(Cylinder("PostLeft", end, new Vector3(-gw * 0.5f, 0f, lz), new Vector3(-gw * 0.5f, gh, lz), a.postRadius, frameMat).GetComponent<Renderer>());
            frames.Add(Cylinder("PostRight", end, new Vector3(gw * 0.5f, 0f, lz), new Vector3(gw * 0.5f, gh, lz), a.postRadius, frameMat).GetComponent<Renderer>());
            frames.Add(Cylinder("Crossbar", end, new Vector3(-gw * 0.5f - a.postRadius, gh, lz), new Vector3(gw * 0.5f + a.postRadius, gh, lz), a.postRadius, frameMat).GetComponent<Renderer>());

            Box("NetBack", end, new Vector3(0f, gh * 0.5f, s * (hl + gd)), new Vector3(gw, gh, 0.02f), netMat);
            Box("NetLeft", end, new Vector3(-gw * 0.5f, gh * 0.5f, s * (hl + gd * 0.5f)), new Vector3(0.02f, gh, gd), netMat);
            Box("NetRight", end, new Vector3(gw * 0.5f, gh * 0.5f, s * (hl + gd * 0.5f)), new Vector3(0.02f, gh, gd), netMat);
            Box("NetRoof", end, new Vector3(0f, gh, s * (hl + gd * 0.5f)), new Vector3(gw, 0.02f, gd), netMat);

            Quaternion faceField = Quaternion.LookRotation(new Vector3(0f, 0f, s));
            Text("TeamName", end, new Vector3(0f, gh + (h - gh) * 0.55f, s * (hl - 0.05f)), faceField, new Vector2(4f, 1.4f),
                side == 0 ? "BLUE" : "RED", side == 0 ? Blue : Red);

            GameObject lightGo = new GameObject("GoalLight");
            lightGo.transform.SetParent(end, false);
            lightGo.transform.localPosition = new Vector3(0f, 2.6f, s * (hl - 1.6f));
            Light l = lightGo.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = side == 0 ? Blue : Red;
            l.range = 7f;
            l.intensity = 1.6f;
            l.shadows = LightShadows.None;
        }

        // stands outside the glass, and the arena name
        Transform deco = new GameObject("Stands").transform;
        deco.SetParent(root, false);
        for (int i = -1; i <= 1; i += 2)
        {
            for (int tier = 0; tier < 3; tier++)
            {
                float x = i * (hw + 1.1f + tier * 0.9f);
                float height = (tier + 1) * 0.8f;
                Box("Tier", deco, new Vector3(x, height * 0.5f, 0f), new Vector3(0.9f, height, a.length + 2f), mDark);
                for (int half = 0; half < 2; half++)
                {
                    float zc = (half == 0 ? -1f : 1f) * (a.length + 2f) * 0.25f;
                    Track(Box("TierStrip", deco, new Vector3(x - i * 0.45f, height, zc), new Vector3(0.04f, 0.04f, (a.length + 2f) * 0.5f), half == 0 ? mNeonBlue : mNeonRed), half);
                }
            }
            Text("ArenaName", deco, new Vector3(i * (hw - 0.05f), h - 0.9f, 0f), Quaternion.LookRotation(new Vector3(i, 0f, 0f)),
                new Vector2(6f, 1f), "KEEPER CLASH", Neutral);
        }
    }

    // ================================================================== gameplay objects

    static KeeperBall BuildBall(KeeperArenaSpec a)
    {
        GameObject root = new GameObject("Keeper_Ball");
        root.transform.position = a.parkPoint;
        root.AddComponent<NetworkObject>();
        KeeperBall ball = root.AddComponent<KeeperBall>();

        GameObject visual = Sphere("Visual", root.transform, Vector3.zero, a.ballRadius * 2f, mBall);
        GameObject glow = Sphere("Glow", visual.transform, Vector3.zero, 1.7f, mGlow);

        TrailRenderer trail = visual.AddComponent<TrailRenderer>();
        trail.sharedMaterial = mParticle;
        trail.time = 0.3f;
        trail.minVertexDistance = 0.04f;
        trail.widthMultiplier = a.ballRadius * 1.8f;
        trail.widthCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);
        trail.shadowCastingMode = ShadowCastingMode.Off;
        trail.numCapVertices = 4;

        ParticleSystem sparks = Particles("HeatSparks", visual.transform, mParticle, 0.35f, 1.2f, 0.06f, 0f, false, 200, true);
        var shape = sparks.shape;
        shape.radius = a.ballRadius;

        GameObject shadow = GameObject.CreatePrimitive(PrimitiveType.Quad);
        shadow.name = "Keeper_BallShadow";
        Object.DestroyImmediate(shadow.GetComponent<Collider>());
        shadow.transform.SetPositionAndRotation(new Vector3(a.parkPoint.x, 0.012f, a.parkPoint.z), Quaternion.Euler(90f, 0f, 0f));
        Renderer shadowR = shadow.GetComponent<Renderer>();
        shadowR.sharedMaterial = mShadow;
        shadowR.shadowCastingMode = ShadowCastingMode.Off;

        AudioSource hum = root.AddComponent<AudioSource>();
        hum.loop = true;
        hum.playOnAwake = false;
        hum.spatialBlend = 1f;
        hum.minDistance = 1.5f;
        hum.maxDistance = 30f;
        hum.volume = 0.3f;

        ball.visual = visual.transform;
        ball.tintRenderers = new[] { visual.GetComponent<Renderer>() };
        ball.glowRenderer = glow.GetComponent<Renderer>();
        ball.trail = trail;
        ball.heatSparks = sparks;
        ball.shadow = shadow.transform;
        ball.shadowRenderer = shadowR;
        ball.hum = hum;
        ball.heatColors = KeeperBall.DefaultHeatColors();
        return ball;
    }

    static void BuildMatch()
    {
        GameObject go = new GameObject("Keeper_Match");
        go.AddComponent<NetworkObject>();
        go.AddComponent<KeeperMatch>();
    }

    static void BuildBot(KeeperArenaSpec a)
    {
        GameObject root = new GameObject("Keeper_Bot");
        KeeperBot bot = root.AddComponent<KeeperBot>();
        GameObject body = new GameObject("Body");
        body.transform.SetParent(root.transform, false);
        Vector3 spot = a.KeeperSpot(KeeperBot.Side);
        body.transform.position = new Vector3(spot.x, 1.2f, spot.z);
        body.transform.rotation = Quaternion.LookRotation(a.Forward(KeeperBot.Side));
        Sphere("Core", body.transform, Vector3.zero, 0.45f, mBotBody);
        Box("Paddle", body.transform, new Vector3(0f, 0f, 0.12f), new Vector3(1.1f, 0.9f, 0.04f), mBotPaddle, false);
        Box("Eye", body.transform, new Vector3(0f, 0.05f, 0.2f), new Vector3(0.3f, 0.05f, 0.05f), mNeonRed, false);
        bot.body = body.transform;
    }

    static void BuildScoreboard()
    {
        GameObject root = new GameObject("Keeper_Scoreboard");
        root.transform.position = new Vector3(0f, 4.4f, 0f);
        KeeperScoreboard board = root.AddComponent<KeeperScoreboard>();
        Box("Panel", root.transform, Vector3.zero, new Vector3(2.8f, 1.4f, 0.12f), mDark, false);
        Box("EdgeTop", root.transform, new Vector3(0f, 0.71f, 0f), new Vector3(2.84f, 0.03f, 0.14f), mNeonWhite, false);
        Box("EdgeBottom", root.transform, new Vector3(0f, -0.71f, 0f), new Vector3(2.84f, 0.03f, 0.14f), mNeonWhite, false);
        Box("EdgeLeft", root.transform, new Vector3(-1.41f, 0f, 0f), new Vector3(0.03f, 1.44f, 0.14f), mNeonBlue, false);
        Box("EdgeRight", root.transform, new Vector3(1.41f, 0f, 0f), new Vector3(0.03f, 1.44f, 0.14f), mNeonRed, false);

        var scores = new List<TMP_Text>();
        var statuses = new List<TMP_Text>();
        var speeds = new List<TMP_Text>();
        for (int face = 0; face < 2; face++)
        {
            float z = face == 0 ? -0.07f : 0.07f;
            Quaternion rot = Quaternion.LookRotation(new Vector3(0f, 0f, face == 0 ? 1f : -1f));
            scores.Add(Text("Score", root.transform, new Vector3(0f, 0.32f, z), rot, new Vector2(2.6f, 0.6f), "KEEPER CLASH", Color.white));
            statuses.Add(Text("Status", root.transform, new Vector3(0f, -0.18f, z), rot, new Vector2(2.6f, 0.45f), "", new Color(1f, 0.92f, 0.6f)));
            speeds.Add(Text("Speed", root.transform, new Vector3(0f, -0.55f, z), rot, new Vector2(2.6f, 0.2f), "", Neutral));
        }
        board.scoreTexts = scores.ToArray();
        board.statusTexts = statuses.ToArray();
        board.speedTexts = speeds.ToArray();
    }

    static void BuildFx()
    {
        GameObject root = new GameObject("Keeper_Fx");
        KeeperFx fx = root.AddComponent<KeeperFx>();
        fx.hitSparks = Particles("HitSparks", root.transform, mParticle, 0.45f, 7f, 0.07f, 0.4f, true, 400, false);
        fx.goalBurst = Particles("GoalBurst", root.transform, mParticle, 1.4f, 11f, 0.18f, 0.25f, true, 600, false);

        GameObject shock = Sphere("Shockwave", root.transform, Vector3.zero, 1f, mShock);
        shock.SetActive(false);
        fx.shockwave = shock.transform;
        fx.shockwaveRenderer = shock.GetComponent<Renderer>();

        fx.blueNeon = blueNeon.ToArray();
        fx.redNeon = redNeon.ToArray();
        fx.blueGoalFrame = blueFrame.ToArray();
        fx.redGoalFrame = redFrame.ToArray();
        fx.blueColor = Blue;
        fx.redColor = Red;

        GameObject flash = new GameObject("GoalFlashVolume");
        flash.transform.SetParent(root.transform, false);
        Volume v = flash.AddComponent<Volume>();
        v.isGlobal = true;
        v.priority = 10f;
        v.weight = 0f;
        v.sharedProfile = Profile("KeeperClash_GoalFlash", p =>
        {
            var ca = p.Add<ColorAdjustments>(true);
            ca.postExposure.value = 0.5f;
            ca.contrast.value = 20f;
            ca.saturation.value = 30f;
            ca.colorFilter.value = new Color(1f, 0.95f, 0.85f);
            ca.hueShift.overrideState = false;
            var bloom = p.Add<Bloom>(true);
            bloom.intensity.value = 3.5f;
            bloom.threshold.value = 0.6f;
            bloom.scatter.value = 0.75f;
            bloom.highQualityFiltering.value = false;
            bloom.dirtTexture.overrideState = false;
            bloom.dirtIntensity.overrideState = false;
            bloom.tint.overrideState = false;
            bloom.clamp.overrideState = false;
            bloom.downscale.overrideState = false;
            bloom.maxIterations.overrideState = false;
            var chroma = p.Add<ChromaticAberration>(true);
            chroma.intensity.value = 0.35f;
        });
        fx.goalFlashVolume = v;

        AudioSource ui = root.AddComponent<AudioSource>();
        ui.playOnAwake = false;
        ui.spatialBlend = 0f;
        fx.uiAudio = ui;
    }

    static ParticleSystem Particles(string name, Transform parent, Material mat, float life, float speed, float size, float gravity, bool stretch, int max, bool loop)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.playOnAwake = loop;
        main.loop = loop;
        main.duration = 1f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.5f, life);
        main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.3f, speed);
        main.startSize = new ParticleSystem.MinMaxCurve(size * 0.5f, size);
        main.gravityModifier = gravity;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = max;

        var emission = ps.emission;
        emission.rateOverTime = 0f;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.05f;

        var col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.5f), new GradientAlphaKey(0f, 1f) });
        col.color = g;

        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.1f));

        ParticleSystemRenderer r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = ShadowCastingMode.Off;
        if (stretch)
        {
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = 0.04f;
            r.lengthScale = 1.5f;
        }
        return ps;
    }

    // ================================================================== lobby, player, rendering

    static RelayNetworkButtons SetupLobby()
    {
        RelayNetworkButtons keypad = Object.FindFirstObjectByType<RelayNetworkButtons>(FindObjectsInactive.Include);
        if (keypad == null)
        {
            Debug.LogWarning("[KeeperClash] No keypad canvas found in the copied scene.");
            return null;
        }

        SerializedObject so = new SerializedObject(keypad);
        SerializedProperty maxPlayers = so.FindProperty("maxPlayers");
        if (maxPlayers != null) maxPlayers.intValue = 2;
        so.ApplyModifiedPropertiesWithoutUndo();

        Transform old = keypad.transform.Find("Practice");
        if (old != null) Object.DestroyImmediate(old.gameObject);

        RectTransform host = keypad.transform.Find("Host") as RectTransform;
        RectTransform client = keypad.transform.Find("Client") as RectTransform;
        if (host == null)
        {
            Debug.LogWarning("[KeeperClash] No Host button on the keypad canvas; Practice button not added.");
            return keypad;
        }

        GameObject practice = Object.Instantiate(host.gameObject, host.parent);
        practice.name = "Practice";
        RectTransform rt = (RectTransform)practice.transform;
        Vector2 basePos = client != null ? (host.anchoredPosition + client.anchoredPosition) * 0.5f : host.anchoredPosition;
        float lowest = client != null ? Mathf.Min(host.anchoredPosition.y, client.anchoredPosition.y) : host.anchoredPosition.y;
        rt.anchoredPosition = new Vector2(basePos.x, lowest - host.rect.height * 1.25f);

        TMP_Text label = practice.GetComponentInChildren<TMP_Text>(true);
        if (label != null) label.text = "Practice";

        Button button = practice.GetComponent<Button>();
        button.onClick = new Button.ButtonClickedEvent();
        return keypad;
    }

    static void BuildPlayer(RelayNetworkButtons keypad)
    {
        GameObject go = new GameObject("Keeper_Player");
        KeeperLocalPlayer player = go.AddComponent<KeeperLocalPlayer>();
        KeeperLobby lobby = go.AddComponent<KeeperLobby>();
        player.rig = Object.FindFirstObjectByType<OVRCameraRig>(FindObjectsInactive.Include);
        lobby.keypad = keypad;
        if (keypad != null)
        {
            player.lobbyCanvas = keypad.GetComponent<Canvas>();
            Transform practice = keypad.transform.Find("Practice");
            if (practice != null)
            {
                Button b = practice.GetComponent<Button>();
                UnityEventTools.AddPersistentListener(b.onClick, lobby.StartPractice);
            }
        }
    }

    static void SetupRendering()
    {
        // post-processing on the headset camera of this scene only
        OVRCameraRig rig = Object.FindFirstObjectByType<OVRCameraRig>(FindObjectsInactive.Include);
        if (rig != null)
        {
            foreach (Camera cam in rig.GetComponentsInChildren<Camera>(true))
            {
                UniversalAdditionalCameraData data = cam.GetUniversalAdditionalCameraData();
                data.renderPostProcessing = true;
                EditorUtility.SetDirty(data);
            }
        }

        VolumeProfile post = Profile("KeeperClash_Post", p =>
        {
            var bloom = p.Add<Bloom>(true);
            bloom.intensity.value = 1.3f;
            bloom.threshold.value = 0.8f;
            bloom.scatter.value = 0.7f;
            bloom.highQualityFiltering.value = false;
            bloom.dirtTexture.overrideState = false;
            bloom.dirtIntensity.overrideState = false;
            bloom.tint.overrideState = false;
            bloom.clamp.overrideState = false;
            bloom.downscale.overrideState = false;
            bloom.maxIterations.overrideState = false;
            var tone = p.Add<Tonemapping>(true);
            tone.mode.value = TonemappingMode.Neutral;
            var ca = p.Add<ColorAdjustments>(true);
            ca.postExposure.value = 0.1f;
            ca.contrast.value = 14f;
            ca.saturation.value = 22f;
            ca.colorFilter.overrideState = false;
            ca.hueShift.overrideState = false;
            var vig = p.Add<Vignette>(true);
            vig.intensity.value = 0.2f;
            vig.smoothness.value = 0.45f;
            vig.color.value = Color.black;
            vig.center.overrideState = false;
            vig.rounded.overrideState = false;
            var wb = p.Add<WhiteBalance>(true);
            wb.temperature.value = -8f;
            wb.tint.value = 4f;
        });

        Volume global = null;
        foreach (Volume v in Object.FindObjectsByType<Volume>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (v.isGlobal && v.gameObject.name != "GoalFlashVolume") { global = v; break; }
        if (global == null)
        {
            global = new GameObject("Global Volume").AddComponent<Volume>();
            global.isGlobal = true;
        }
        global.sharedProfile = post;
        global.weight = 1f;

        foreach (Light l in Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (l.type != LightType.Directional) continue;
            l.intensity = 0.6f;
            l.color = new Color(0.72f, 0.78f, 1f);
            l.shadows = LightShadows.None;
            l.transform.rotation = Quaternion.Euler(55f, -30f, 0f);
        }

        Material sky = AssetDatabase.LoadAssetAtPath<Material>($"{Root}/Materials/Sky.mat");
        if (sky == null)
        {
            sky = new Material(Shader.Find("Skybox/Procedural"));
            AssetDatabase.CreateAsset(sky, $"{Root}/Materials/Sky.mat");
        }
        sky.SetFloat("_SunSize", 0f);
        sky.SetFloat("_SunDisk", 0f);
        sky.SetFloat("_AtmosphereThickness", 0.35f);
        sky.SetColor("_SkyTint", new Color(0.22f, 0.12f, 0.35f));
        sky.SetColor("_GroundColor", new Color(0.02f, 0.02f, 0.04f));
        sky.SetFloat("_Exposure", 0.45f);
        EditorUtility.SetDirty(sky);

        RenderSettings.skybox = sky;
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.16f, 0.18f, 0.32f);
        RenderSettings.ambientEquatorColor = new Color(0.1f, 0.08f, 0.18f);
        RenderSettings.ambientGroundColor = new Color(0.03f, 0.03f, 0.05f);
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = new Color(0.04f, 0.035f, 0.08f);
        RenderSettings.fogStartDistance = 18f;
        RenderSettings.fogEndDistance = 70f;
    }

    static VolumeProfile Profile(string name, Action<VolumeProfile> fill)
    {
        string path = $"{Root}/Profiles/{name}.asset";
        AssetDatabase.DeleteAsset(path);
        VolumeProfile p = ScriptableObject.CreateInstance<VolumeProfile>();
        fill(p);
        AssetDatabase.CreateAsset(p, path);
        foreach (VolumeComponent c in p.components) AssetDatabase.AddObjectToAsset(c, p);
        AssetDatabase.SaveAssets();
        return AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
    }

    static void RefreshNetworkObjectHashes()
    {
        MethodInfo validate = typeof(NetworkObject).GetMethod("OnValidate", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        var seen = new HashSet<long>();
        foreach (NetworkObject no in Object.FindObjectsByType<NetworkObject>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            validate?.Invoke(no, null);
            EditorUtility.SetDirty(no);
            long hash = new SerializedObject(no).FindProperty("GlobalObjectIdHash").longValue;
            if (hash == 0 || !seen.Add(hash)) Debug.LogError($"[KeeperClash] NetworkObject {no.name} has a bad hash {hash}");
            else Debug.Log($"[KeeperClash] NetworkObject {no.name} hash {hash}");
        }
    }

    static void EnsureFolder(string parent, string name)
    {
        if (!AssetDatabase.IsValidFolder($"{parent}/{name}")) AssetDatabase.CreateFolder(parent, name);
    }

    // ================================================================== self-test

    public static bool SelfTest()
    {
        KeeperArenaSpec a = new KeeperArenaSpec();
        KeeperBallTuning t = new KeeperBallTuning();
        System.Random rng = new System.Random(12345);
        float R() => (float)rng.NextDouble();
        const float dt = 1f / 72f;
        bool ok = true;

        // 1) shots and serves always finish (goal or backboard) and stay inside the arena,
        //    with the default steering and with no steering at all
        foreach (float steer in new[] { t.homingStrength, 0f, 180f })
        {
            KeeperBallTuning tt = JsonUtility.FromJson<KeeperBallTuning>(JsonUtility.ToJson(t));
            tt.homingStrength = steer;
            int goals = 0, backboards = 0, stuck = 0, escaped = 0;
            float maxTime = 0f;
            for (int i = 0; i < 500; i++)
            {
                KeeperBallState s;
                if (i % 5 == 0)
                {
                    s = KeeperBallSim.Serve(a, tt, i % 2, 1, R(), R(), R());
                }
                else
                {
                    int hitter = i % 2;
                    s = KeeperBallSim.Parked(a, tt, 1);
                    s.pos = a.KeeperSpot(hitter) + new Vector3((R() - 0.5f) * 3f, 0.4f + R() * 1.8f, 0f) + a.Forward(hitter) * 0.3f;
                    s.touches = (byte)rng.Next(0, 20);
                    Vector3 away = (a.Forward(hitter) + new Vector3(R() - 0.5f, R() - 0.5f, R() - 0.5f) * 1.5f).normalized;
                    Vector3 swing = new Vector3(R() - 0.5f, R() - 0.5f, R() - 0.3f * KeeperArenaSpec.Sign(hitter)) * (R() * 8f);
                    KeeperBallSim.ApplyTouch(ref s, a, tt, hitter, away, swing, R(), R());
                }

                bool done = false;
                float time;
                for (time = 0f; time < 10f; time += dt)
                {
                    KeeperStepResult res = KeeperBallSim.Step(ref s, dt, a, tt, true, out _);
                    Vector3 p = s.pos;
                    if (Mathf.Abs(p.x) > a.HalfWidth + 0.01f || p.y < -0.01f || p.y > a.height + 0.01f || Mathf.Abs(p.z) > a.HalfLength + a.goalDepth + 0.01f)
                        escaped++;
                    if (res == KeeperStepResult.Goal) { goals++; done = true; break; }
                    if (res == KeeperStepResult.Backboard) { backboards++; done = true; break; }
                }
                maxTime = Mathf.Max(maxTime, time);
                if (!done) stuck++;
            }
            Debug.Log($"[KeeperClash] TEST shots (steering {steer} deg/s): goals={goals} backboards={backboards} stuck={stuck} escaped={escaped} longest={maxTime:0.00}s");
            if (stuck > 0 || escaped > 0) ok = false;
        }

        // 1b) with no steering the ball flies dead straight in open air
        {
            KeeperBallTuning t0 = JsonUtility.FromJson<KeeperBallTuning>(JsonUtility.ToJson(t));
            t0.homingStrength = 0f;
            KeeperBallState s0 = KeeperBallSim.Parked(a, t0, 1);
            s0.pos = new Vector3(0f, 2f, -5f);
            KeeperBallSim.ApplyTouch(ref s0, a, t0, 0, new Vector3(0.3f, 0.1f, 1f), Vector3.zero, 0.5f, 0.5f);
            Vector3 d0 = s0.vel.normalized;
            for (int k = 0; k < 20; k++) KeeperBallSim.Step(ref s0, dt, a, t0, true, out _);
            bool straight = Vector3.Angle(d0, s0.vel) < 0.01f && s0.lastHitter == 0;
            Debug.Log($"[KeeperClash] TEST no steering flies straight: {(straight ? "ok" : "FAIL")}");
            ok &= straight;
        }

        // 2) speed goes up per touch and stops at the max
        bool speedOk = true;
        for (int k = 0; k < 40; k++)
        {
            float expected = Mathf.Min(t.maxSpeed, t.baseSpeed + t.speedPerTouch * k);
            if (Mathf.Abs(t.SpeedFor(k) - expected) > 1e-4f || t.SpeedFor(k + 1) < t.SpeedFor(k)) speedOk = false;
        }
        KeeperBallState sp = KeeperBallSim.Parked(a, t, 1);
        sp.pos = a.KeeperSpot(0) + Vector3.up;
        for (int k = 0; k < 30; k++) KeeperBallSim.ApplyTouch(ref sp, a, t, k % 2, Vector3.forward, Vector3.zero, 0.5f, 0.5f);
        if (Mathf.Abs(sp.speed - t.maxSpeed) > 1e-3f) speedOk = false;
        Debug.Log($"[KeeperClash] TEST speed: {(speedOk ? "ok" : "FAIL")} (after 30 touches {sp.speed} m/s)");
        ok &= speedOk;

        // 3) backboard sends the ball at the other goal
        KeeperBallState bb = KeeperBallSim.Parked(a, t, 7);
        bb.Mode = KeeperBallMode.Flying;
        bb.pos = new Vector3(3.2f, 1.5f, 3f);
        bb.speed = 10f;
        bb.vel = new Vector3(0f, 0f, 10f);
        bb.targetSide = 1;
        bb.target = KeeperBallSim.GoalTarget(a, t, 1);
        bb.homeTimer = -100f; // no homing: fly straight into the board
        bool sawBackboard = false;
        for (float time = 0f; time < 2f && !sawBackboard; time += dt)
            if (KeeperBallSim.Step(ref bb, dt, a, t, true, out _) == KeeperStepResult.Backboard) sawBackboard = true;
        bool bbOk = sawBackboard && bb.targetSide == 0 && bb.seq == 8;
        Debug.Log($"[KeeperClash] TEST backboard: {(bbOk ? "ok" : "FAIL")} (target now {bb.targetSide}, seq {bb.seq})");
        ok &= bbOk;

        // 4) bot vs bot match reaches 7
        int[] score = new int[2];
        int rallies = 0, longestRally = 0;
        int serveSide = 0;
        while (score[0] < 7 && score[1] < 7 && rallies < 2000)
        {
            rallies++;
            KeeperBallState s = KeeperBallSim.Serve(a, t, serveSide, (ushort)(rallies * 10), R(), R(), R());
            serveSide = 1 - serveSide;
            int touches = 0;
            int decidedSeq = -1;
            bool save = false;
            for (float time = 0f; time < 120f; time += dt)
            {
                int defender = s.targetSide;
                float sign = KeeperArenaSpec.Sign(defender);
                float plane = a.KeeperSpot(defender).z;
                if (decidedSeq != s.seq)
                {
                    decidedSeq = s.seq;
                    save = R() < KeeperBot.SaveChance(t.Heat01(s.speed), 0.95f, 0.45f);
                }
                if (save && (s.pos.z - plane) * sign >= 0f)
                {
                    Vector3 swing = (a.Forward(defender) * 3f + new Vector3(R() * 3f - 1.5f, R() - 0.3f, 0f));
                    KeeperBallSim.ApplyTouch(ref s, a, t, defender, a.Forward(defender), swing, R(), R());
                    touches++;
                    continue;
                }
                KeeperStepResult res = KeeperBallSim.Step(ref s, dt, a, t, true, out int goalSide);
                if (res == KeeperStepResult.Goal)
                {
                    score[1 - goalSide]++;
                    break;
                }
            }
            longestRally = Mathf.Max(longestRally, touches);
        }
        bool matchOk = score[0] == 7 || score[1] == 7;
        Debug.Log($"[KeeperClash] TEST bot match: {(matchOk ? "ok" : "FAIL")} Blue {score[0]} - {score[1]} Red, {rallies} rallies, longest rally {longestRally} touches");
        ok &= matchOk;

        Debug.Log($"[KeeperClash] TEST RESULT {(ok ? "PASS" : "FAIL")}");
        return ok;
    }

    // ================================================================== screenshots

    public static void CaptureScreenshots()
    {
        string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Screenshots");
        Directory.CreateDirectory(dir);
        Debug.Log($"[KeeperClash] Render pipeline for screenshots: {(GraphicsSettings.currentRenderPipeline != null ? GraphicsSettings.currentRenderPipeline.name : "built-in")}");

        Shot(dir, "keeper_overview.png", new Vector3(6.5f, 7.5f, -11f), new Vector3(0f, 1f, 0f), 60f);
        Shot(dir, "keeper_blue_pov.png", new Vector3(0f, 1.65f, -6.1f), new Vector3(0f, 1.6f, 7f), 75f);
        Shot(dir, "keeper_red_pov.png", new Vector3(0f, 1.65f, 6.1f), new Vector3(0f, 1.6f, -7f), 75f);
        Shot(dir, "keeper_goal.png", new Vector3(2.4f, 1.7f, 3.6f), new Vector3(0f, 1.1f, 7.3f), 60f);
        Debug.Log($"[KeeperClash] Screenshots saved to {dir}");
    }

    static void Shot(string dir, string file, Vector3 pos, Vector3 lookAt, float fov)
    {
        GameObject camGo = new GameObject("~KeeperShotCam") { hideFlags = HideFlags.HideAndDontSave };
        Camera cam = camGo.AddComponent<Camera>();
        cam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(lookAt - pos));
        cam.fieldOfView = fov;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 300f;
        cam.clearFlags = CameraClearFlags.Skybox;
        UniversalAdditionalCameraData data = cam.GetUniversalAdditionalCameraData();
        data.renderPostProcessing = true;

        RenderTexture rt = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        cam.targetTexture = rt;
        var request = new RenderPipeline.StandardRequest { destination = rt };
        if (RenderPipeline.SupportsRenderRequest(cam, request)) RenderPipeline.SubmitRenderRequest(cam, request);
        else cam.Render();

        RenderTexture prev = RenderTexture.active;
        RenderTexture.active = rt;
        Texture2D tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;
        File.WriteAllBytes(Path.Combine(dir, file), tex.EncodeToPNG());

        cam.targetTexture = null;
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(tex);
        Object.DestroyImmediate(camGo);
    }
}
