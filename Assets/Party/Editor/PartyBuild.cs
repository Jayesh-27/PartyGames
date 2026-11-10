using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using TMPro;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.Rendering.Universal.ShaderGUI;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>
/// Shared pieces for the scene builders: the UI kit (rounded cards, buttons, text), the white cloud
/// environment, scene copying, materials, textures and screenshots.
/// </summary>
public static class PartyBuild
{
    public const string Root = "Assets/Party";
    public const string SourceScene = "Assets/Scenes/SampleScene.unity";
    const string FontDir = "Assets/TextMesh Pro/Examples & Extras/Resources/Fonts & Materials/";

    public static readonly Color Fog = new Color(0.93f, 0.95f, 0.98f);

    // ================================================================== assets

    public static void EnsureFolder(string path)
    {
        path = path.Replace('\\', '/');
        if (AssetDatabase.IsValidFolder(path)) return;
        int cut = path.LastIndexOf('/');
        EnsureFolder(path.Substring(0, cut));
        AssetDatabase.CreateFolder(path.Substring(0, cut), path.Substring(cut + 1));
    }

    public static Material Mat(string root, string name, string shaderName, Color color, int surface = 0, int blend = 0, Texture tex = null, Vector2? tiling = null)
    {
        EnsureFolder(root + "/Materials");
        string path = $"{root}/Materials/{name}.mat";
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

    /// <summary>Writes a PNG made by f(u, v) and imports it. border > 0 imports it as a 9-slice sprite.</summary>
    public static string SaveTexture(string root, string name, int size, Func<float, float, Color> f, bool repeat = false, int border = -1)
    {
        EnsureFolder(root + "/Textures");
        string path = $"{root}/Textures/{name}.png";
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
        if (border >= 0)
        {
            imp.textureType = TextureImporterType.Sprite;
            imp.spriteImportMode = SpriteImportMode.Single;
            imp.spritePixelsPerUnit = 100f;
            imp.spriteBorder = new Vector4(border, border, border, border);
            TextureImporterSettings s = new TextureImporterSettings();
            imp.ReadTextureSettings(s);
            s.spriteMeshType = SpriteMeshType.FullRect;
            imp.SetTextureSettings(s);
        }
        imp.SaveAndReimport();
        return path;
    }

    public static Texture2D Tex(string path) => AssetDatabase.LoadAssetAtPath<Texture2D>(path);

    public static VolumeProfile Profile(string root, string name, Action<VolumeProfile> fill)
    {
        EnsureFolder(root + "/Profiles");
        string path = $"{root}/Profiles/{name}.asset";
        AssetDatabase.DeleteAsset(path);
        VolumeProfile p = ScriptableObject.CreateInstance<VolumeProfile>();
        fill(p);
        AssetDatabase.CreateAsset(p, path);
        foreach (VolumeComponent c in p.components) AssetDatabase.AddObjectToAsset(c, p);
        AssetDatabase.SaveAssets();
        return AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
    }

    // ================================================================== scenes

    /// <summary>Opens the scene, first copying SampleScene to it when it does not exist yet (keeps the camera rig setup).</summary>
    public static UnityEngine.SceneManagement.Scene OpenOrCopy(string scenePath)
    {
        if (!File.Exists(scenePath))
        {
            if (!AssetDatabase.CopyAsset(SourceScene, scenePath)) throw new Exception("Could not copy " + SourceScene);
            AssetDatabase.Refresh();
        }
        return EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
    }

    /// <summary>Removes everything except the camera rig, the EventSystem, the sun, and optionally the NetworkManager and the canvas with the Meta ray wiring.</summary>
    public static void Strip(UnityEngine.SceneManagement.Scene scene, bool keepNetwork, bool keepCanvas, string generatedPrefix)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            bool keep =
                root.GetComponentInChildren<OVRCameraRig>(true) != null ||
                root.GetComponent<EventSystem>() != null ||
                (keepNetwork && root.GetComponent<NetworkManager>() != null) ||
                (keepCanvas && root.GetComponent<Canvas>() != null && root.transform.Find("ISDK_RayCanvasInteraction") != null) ||
                (root.GetComponent<Light>() != null && root.GetComponent<Light>().type == LightType.Directional);
            if (!keep || (generatedPrefix.Length > 0 && root.name.StartsWith(generatedPrefix))) Object.DestroyImmediate(root);
        }
    }

    public static void SaveScene(UnityEngine.SceneManagement.Scene scene)
    {
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        RefreshNetworkObjectHashes();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
    }

    public static void RefreshNetworkObjectHashes()
    {
        MethodInfo validate = typeof(NetworkObject).GetMethod("OnValidate", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        var seen = new HashSet<long>();
        foreach (NetworkObject no in Object.FindObjectsByType<NetworkObject>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            validate?.Invoke(no, null);
            EditorUtility.SetDirty(no);
            long hash = new SerializedObject(no).FindProperty("GlobalObjectIdHash").longValue;
            if (hash == 0 || !seen.Add(hash)) Debug.LogError($"[Party] NetworkObject {no.name} has a bad hash {hash}");
            else Debug.Log($"[Party] NetworkObject {no.name} hash {hash}");
        }
    }

    /// <summary>Build Settings: these scenes first, in this order; other scenes keep their place after them.</summary>
    public static void ScenesFirst(params string[] paths)
    {
        var list = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        list.RemoveAll(s => Array.IndexOf(paths, s.path) >= 0);
        for (int i = paths.Length - 1; i >= 0; i--)
            if (File.Exists(paths[i])) list.Insert(0, new EditorBuildSettingsScene(paths[i], true));
        EditorBuildSettings.scenes = list.ToArray();
    }

    // ================================================================== cloud environment

    /// <summary>The "standing in the clouds" look: white fog, pale sky, a soft floor and slow cloud puffs.</summary>
    public static GameObject Clouds(string name, float platformRadius, bool postProcessing = false)
    {
        string dot = SaveTexture(Root, "CloudPuff", 128, (u, v) =>
        {
            float d = Mathf.Sqrt((u - 0.5f) * (u - 0.5f) + (v - 0.5f) * (v - 0.5f)) * 2f;
            float a = Mathf.Clamp01(1f - d);
            return new Color(1f, 1f, 1f, a * a * (3f - 2f * a));
        });
        string ring = SaveTexture(Root, "FloorFade", 256, (u, v) =>
        {
            float d = Mathf.Sqrt((u - 0.5f) * (u - 0.5f) + (v - 0.5f) * (v - 0.5f)) * 2f;
            float a = Mathf.Clamp01((1f - d) / 0.55f);
            return new Color(1f, 1f, 1f, a * a);
        });

        Material mGround = Mat(Root, "CloudGround", "Universal Render Pipeline/Lit", new Color(0.90f, 0.92f, 0.96f));
        mGround.SetFloat("_Smoothness", 0.05f);
        Material mPlatform = Mat(Root, "CloudPlatform", "Universal Render Pipeline/Lit", new Color(0.80f, 0.85f, 0.94f));
        mPlatform.SetFloat("_Smoothness", 0.25f);
        Material mRim = Mat(Root, "CloudPlatformRim", "Universal Render Pipeline/Lit", new Color(0.62f, 0.70f, 0.86f));
        Material mShade = Mat(Root, "CloudShade", "Universal Render Pipeline/Unlit", new Color(0.35f, 0.42f, 0.58f, 0.35f), 1, 0, Tex(ring));
        Material mPuff = Mat(Root, "CloudPuff", "Universal Render Pipeline/Particles/Unlit", new Color(1f, 1f, 1f, 0.5f), 1, 0, Tex(dot));

        GameObject root = new GameObject(name);

        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        Object.DestroyImmediate(ground.GetComponent<Collider>());
        ground.transform.SetParent(root.transform, false);
        ground.transform.localPosition = new Vector3(0f, -0.06f, 0f);
        ground.transform.localScale = new Vector3(14f, 1f, 14f);
        ground.GetComponent<Renderer>().sharedMaterial = mGround;
        ground.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;

        // soft shade around the platform so it does not look pasted on
        GameObject shade = GameObject.CreatePrimitive(PrimitiveType.Quad);
        shade.name = "PlatformShade";
        Object.DestroyImmediate(shade.GetComponent<Collider>());
        shade.transform.SetParent(root.transform, false);
        shade.transform.localPosition = new Vector3(0f, -0.055f, 0f);
        shade.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        shade.transform.localScale = Vector3.one * platformRadius * 3.2f;
        shade.GetComponent<Renderer>().sharedMaterial = mShade;
        shade.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;

        GameObject rim = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        rim.name = "PlatformRim";
        Object.DestroyImmediate(rim.GetComponent<Collider>());
        rim.transform.SetParent(root.transform, false);
        rim.transform.localPosition = new Vector3(0f, -0.05f, 0f);
        rim.transform.localScale = new Vector3(platformRadius * 2f + 0.16f, 0.04f, platformRadius * 2f + 0.16f);
        rim.GetComponent<Renderer>().sharedMaterial = mRim;
        rim.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;

        GameObject platform = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        platform.name = "Platform";
        Object.DestroyImmediate(platform.GetComponent<Collider>());
        platform.transform.SetParent(root.transform, false);
        platform.transform.localPosition = new Vector3(0f, -0.03f, 0f);
        platform.transform.localScale = new Vector3(platformRadius * 2f, 0.03f, platformRadius * 2f);
        platform.GetComponent<Renderer>().sharedMaterial = mPlatform;
        platform.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;

        // slow drifting puffs in a ring around the players
        GameObject puffs = new GameObject("CloudPuffs");
        puffs.transform.SetParent(root.transform, false);
        puffs.transform.localPosition = new Vector3(0f, 0.4f, 0f);
        puffs.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
        ParticleSystem ps = puffs.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.loop = true;
        main.prewarm = true;
        main.duration = 30f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(26f, 40f);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(3.5f, 7.5f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, 6.28f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 1f, 1f, 0.30f), new Color(1f, 1f, 1f, 0.55f));
        main.maxParticles = 26;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        var emission = ps.emission;
        emission.rateOverTime = 0.8f;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 13f;
        shape.radiusThickness = 0.55f;
        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.World;
        vel.x = new ParticleSystem.MinMaxCurve(0.05f, 0.22f);
        vel.y = new ParticleSystem.MinMaxCurve(0.0f, 0.03f);
        vel.z = new ParticleSystem.MinMaxCurve(-0.06f, 0.06f);
        var col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(1f, 0.8f), new GradientAlphaKey(0f, 1f) });
        col.color = g;
        var rot = ps.rotationOverLifetime;
        rot.enabled = true;
        rot.z = new ParticleSystem.MinMaxCurve(-0.03f, 0.03f);
        ParticleSystemRenderer pr = puffs.GetComponent<ParticleSystemRenderer>();
        pr.sharedMaterial = mPuff;
        pr.renderMode = ParticleSystemRenderMode.Billboard;
        pr.shadowCastingMode = ShadowCastingMode.Off;

        // light, sky, fog
        foreach (Light l in Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (l.type != LightType.Directional) continue;
            l.intensity = 1.15f;
            l.color = new Color(1f, 0.97f, 0.92f);
            l.shadows = LightShadows.Soft;
            l.shadowStrength = 0.45f;
            l.transform.rotation = Quaternion.Euler(58f, -35f, 0f);
        }

        // sky: fog-white up to the horizon, a hint of blue straight above
        Color zenith = new Color(0.74f, 0.85f, 1f);
        string skyTex = SaveTexture(Root, "CloudSkyGradient", 256, (u, v) =>
        {
            float k = Mathf.Clamp01((v - 0.52f) / 0.48f);
            Color c = Color.Lerp(Fog, zenith, k * k);
            c.a = 1f;
            return c;
        });
        EnsureFolder(Root + "/Materials");
        string skyPath = Root + "/Materials/CloudSky.mat";
        Material sky = AssetDatabase.LoadAssetAtPath<Material>(skyPath);
        Shader skyShader = Shader.Find("Skybox/Panoramic");
        if (sky == null)
        {
            sky = new Material(skyShader);
            AssetDatabase.CreateAsset(sky, skyPath);
        }
        else sky.shader = skyShader;
        sky.SetTexture("_MainTex", Tex(skyTex));
        sky.SetFloat("_Mapping", 1f);
        sky.SetFloat("_ImageType", 0f);
        sky.SetFloat("_Exposure", 1f);
        sky.SetColor("_Tint", new Color(0.5f, 0.5f, 0.5f, 0.5f));
        EditorUtility.SetDirty(sky);

        RenderSettings.skybox = sky;
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.86f, 0.90f, 1f);
        RenderSettings.ambientEquatorColor = new Color(0.80f, 0.82f, 0.88f);
        RenderSettings.ambientGroundColor = new Color(0.62f, 0.64f, 0.70f);
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = Fog;
        RenderSettings.fogDensity = 0.085f;

        OVRCameraRig rig = Object.FindFirstObjectByType<OVRCameraRig>(FindObjectsInactive.Include);
        if (rig != null)
        {
            foreach (Camera cam in rig.GetComponentsInChildren<Camera>(true))
            {
                cam.clearFlags = CameraClearFlags.Skybox;
                cam.backgroundColor = Fog;
                UniversalAdditionalCameraData data = cam.GetUniversalAdditionalCameraData();
                data.renderPostProcessing = postProcessing;
                EditorUtility.SetDirty(data);
                EditorUtility.SetDirty(cam);
            }
        }
        return root;
    }

    // ================================================================== UI kit

    public static Sprite Rounded, Ring, Glow, Dot;
    public static TMP_FontAsset TitleFont, BodyFont;

    public static void LoadUi()
    {
        const int S = 128;
        const float R = 40f;
        float Sdf(float u, float v)
        {
            // signed distance (pixels) to a rounded square that fills the texture
            float x = Mathf.Abs(u - 0.5f) * S, y = Mathf.Abs(v - 0.5f) * S;
            float h = S * 0.5f - 1f;
            float qx = Mathf.Max(x - (h - R), 0f), qy = Mathf.Max(y - (h - R), 0f);
            return Mathf.Sqrt(qx * qx + qy * qy) + Mathf.Min(Mathf.Max(x - (h - R), y - (h - R)), 0f) - R;
        }
        string rounded = SaveTexture(Root, "UIRounded", S, (u, v) => new Color(1f, 1f, 1f, Mathf.Clamp01(0.5f - Sdf(u, v))), false, 48);
        string ring = SaveTexture(Root, "UIRing", S, (u, v) =>
        {
            float d = Sdf(u, v);
            return new Color(1f, 1f, 1f, Mathf.Clamp01(0.5f - d) * Mathf.Clamp01(d + 5.5f));
        }, false, 48);
        string glow = SaveTexture(Root, "UIGlow", S, (u, v) =>
        {
            float x = Mathf.Abs(u - 0.5f) * S, y = Mathf.Abs(v - 0.5f) * S;
            float h = S * 0.5f - 26f;
            float qx = Mathf.Max(x - (h - 14f), 0f), qy = Mathf.Max(y - (h - 14f), 0f);
            float d = Mathf.Sqrt(qx * qx + qy * qy) - 14f;
            float a = Mathf.Clamp01(1f - d / 24f);
            return new Color(1f, 1f, 1f, a * a);
        }, false, 56);
        string dot = SaveTexture(Root, "UIDot", S, (u, v) =>
        {
            float d = Mathf.Sqrt((u - 0.5f) * (u - 0.5f) + (v - 0.5f) * (v - 0.5f)) * S;
            return new Color(1f, 1f, 1f, Mathf.Clamp01(S * 0.5f - 1f - d));
        }, false, 0);

        Rounded = AssetDatabase.LoadAssetAtPath<Sprite>(rounded);
        Ring = AssetDatabase.LoadAssetAtPath<Sprite>(ring);
        Glow = AssetDatabase.LoadAssetAtPath<Sprite>(glow);
        Dot = AssetDatabase.LoadAssetAtPath<Sprite>(dot);
        if (Rounded == null) throw new Exception("UI sprites did not import");

        TitleFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontDir + "Anton SDF.asset");
        BodyFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontDir + "Roboto-Bold SDF.asset");
        if (TitleFont == null) TitleFont = TMP_Settings.defaultFontAsset;
        if (BodyFont == null) BodyFont = TMP_Settings.defaultFontAsset;
    }

    /// <summary>A world-space canvas. size is in canvas pixels, metresWide is how wide it is in the world.</summary>
    public static Canvas WorldCanvas(string name, Transform parent, Vector3 pos, Quaternion rot, Vector2 size, float metresWide)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        Canvas c = go.AddComponent<Canvas>();
        c.renderMode = RenderMode.WorldSpace;
        c.additionalShaderChannels = AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.Normal | AdditionalCanvasShaderChannels.Tangent;
        CanvasScaler s = go.AddComponent<CanvasScaler>();
        s.dynamicPixelsPerUnit = 2f;
        RectTransform rt = (RectTransform)go.transform;
        rt.sizeDelta = size;
        rt.localPosition = pos;
        rt.localRotation = rot;
        rt.localScale = Vector3.one * (metresWide / size.x);
        return c;
    }

    /// <summary>An empty rect. x, y are from the parent's top-left corner; the pivot is the centre.</summary>
    public static RectTransform Rect(Transform parent, string name, float x, float y, float w, float h)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        RectTransform rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(w, h);
        rt.anchoredPosition = new Vector2(x + w * 0.5f, -(y + h * 0.5f));
        return rt;
    }

    /// <summary>A rect that fills its parent.</summary>
    public static RectTransform Fill(Transform parent, string name, float inset = 0f)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        RectTransform rt = (RectTransform)go.transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(inset, inset);
        rt.offsetMax = new Vector2(-inset, -inset);
        return rt;
    }

    static Image Img(RectTransform rt, Sprite sprite, Color color, float radius, bool raycast)
    {
        Image img = rt.gameObject.AddComponent<Image>();
        img.sprite = sprite;
        img.color = color;
        img.type = sprite == Dot ? Image.Type.Simple : Image.Type.Sliced;
        img.pixelsPerUnitMultiplier = Mathf.Max(0.05f, 40f / Mathf.Max(1f, radius));
        img.raycastTarget = raycast;
        return img;
    }

    public static Image Panel(Transform parent, string name, float x, float y, float w, float h, Color color, float radius = 28f, bool raycast = false)
        => Img(Rect(parent, name, x, y, w, h), Rounded, color, radius, raycast);

    public static Image Outline(Transform parent, string name, float x, float y, float w, float h, Color color, float radius = 28f)
        => Img(Rect(parent, name, x, y, w, h), Ring, color, radius, false);

    public static Image Circle(Transform parent, string name, float x, float y, float d, Color color)
        => Img(Rect(parent, name, x, y, d, d), Dot, color, 40f, false);

    /// <summary>A card with a soft shadow behind it and a faint outline. Returns the card's rect to put content in.</summary>
    public static RectTransform Card(Transform parent, string name, float x, float y, float w, float h, Color color, float radius = 36f, bool raycast = false)
    {
        const float g = 36f;
        Image shadow = Img(Rect(parent, name + "_Shadow", x - g, y - g + 10f, w + g * 2f, h + g * 2f), Glow, new Color(0f, 0.03f, 0.10f, 0.45f), 40f, false);
        shadow.pixelsPerUnitMultiplier = 1f;
        Image card = Panel(parent, name, x, y, w, h, color, radius, raycast);
        Img(Fill(card.transform, "Outline"), Ring, new Color(1f, 1f, 1f, 0.14f), radius, false);
        return card.rectTransform;
    }

    public static TextMeshProUGUI Label(Transform parent, string name, float x, float y, float w, float h, string text, float size, Color color,
        TMP_FontAsset font = null, TextAlignmentOptions align = TextAlignmentOptions.Center, bool autoSize = false)
    {
        RectTransform rt = Rect(parent, name, x, y, w, h);
        TextMeshProUGUI t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        t.font = font != null ? font : BodyFont;
        t.text = text;
        t.fontSize = size;
        t.color = color;
        t.alignment = align;
        t.richText = true;
        t.raycastTarget = false;
        t.textWrappingMode = TextWrappingModes.Normal;
        t.overflowMode = TextOverflowModes.Overflow;
        if (autoSize)
        {
            t.enableAutoSizing = true;
            t.fontSizeMin = size * 0.35f;
            t.fontSizeMax = size;
        }
        return t;
    }

    /// <summary>A rounded button with a label. The label is returned through 'label'.</summary>
    public static Button Btn(Transform parent, string name, float x, float y, float w, float h, string text, Color bg, Color fg, float fontSize,
        out TextMeshProUGUI label, float radius = 26f, TMP_FontAsset font = null)
    {
        Image img = Panel(parent, name, x, y, w, h, bg, radius, true);
        Button b = img.gameObject.AddComponent<Button>();
        b.targetGraphic = img;
        ColorBlock cb = b.colors;
        cb.normalColor = Color.white;
        cb.highlightedColor = new Color(1.0f, 1.0f, 1.0f, 1f);
        cb.pressedColor = new Color(0.82f, 0.82f, 0.82f, 1f);
        cb.selectedColor = Color.white;
        cb.disabledColor = new Color(0.7f, 0.7f, 0.7f, 1f);
        cb.fadeDuration = 0.06f;
        b.colors = cb;
        Navigation nav = b.navigation;
        nav.mode = Navigation.Mode.None;
        b.navigation = nav;
        img.gameObject.AddComponent<CanvasGroup>();
        img.gameObject.AddComponent<PartyButton>();
        Img(Fill(img.transform, "Shine"), Ring, new Color(1f, 1f, 1f, 0.22f), radius, false);
        label = Label(img.transform, "Label", 0f, 0f, w, h, text, fontSize, fg, font != null ? font : TitleFont);
        return b;
    }

    public static Button Btn(Transform parent, string name, float x, float y, float w, float h, string text, Color bg, Color fg, float fontSize)
        => Btn(parent, name, x, y, w, h, text, bg, fg, fontSize, out _);

    // ================================================================== screenshots

    public static string ShotDir()
    {
        string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Screenshots");
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static void Shot(string file, Vector3 pos, Vector3 lookAt, float fov, bool post = false)
    {
        Canvas.ForceUpdateCanvases();
        GameObject camGo = new GameObject("~PartyShotCam") { hideFlags = HideFlags.HideAndDontSave };
        Camera cam = camGo.AddComponent<Camera>();
        cam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(lookAt - pos));
        cam.fieldOfView = fov;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 300f;
        cam.clearFlags = CameraClearFlags.Skybox;
        UniversalAdditionalCameraData data = cam.GetUniversalAdditionalCameraData();
        data.renderPostProcessing = post;

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
        File.WriteAllBytes(Path.Combine(ShotDir(), file), tex.EncodeToPNG());

        cam.targetTexture = null;
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(tex);
        Object.DestroyImmediate(camGo);
    }
}
