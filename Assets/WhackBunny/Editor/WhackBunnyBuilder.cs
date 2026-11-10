using System;
using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using static PartyBuild;

/// <summary>
/// Builds the Whack-a-Bunny scene (Tools > Party > Build Whack-a-Bunny Scene): the table in the clouds, a bunny in
/// every hole, a Mokia hammer per player, the bot, the board over the table. Re-runnable.
/// </summary>
public static class WhackBunnyBuilder
{
    const string Root = "Assets/WhackBunny";
    public const string ScenePath = "Assets/Scenes/WhackBunny.unity";
    const string TableModel = "Assets/Models/Props/WhackTable.glb";
    const string BunnyModel = "Assets/Models/Props/Bunny.glb";
    const string HammerModel = "Assets/Models/Props/MokiaHammer.glb";

    [MenuItem("Tools/Party/Build Whack-a-Bunny Scene")]
    public static void BuildMenu()
    {
        BuildScene();
        CaptureScreenshots();
    }

    [MenuItem("Tools/Party/Run Whack-a-Bunny Self-Test")]
    public static void SelfTestMenu() => SelfTest();

    // ================================================================== scene

    public static void BuildScene()
    {
        LoadUi();
        AssetDatabase.Refresh();
        var scene = OpenOrCopy(ScenePath);
        Strip(scene, false, false, "Whack_");
        Clouds("Whack_Clouds", 2.6f);

        // ---- table
        GameObject table = Spawn(TableModel, "Whack_Table", null);
        Bounds tb = WorldBounds(table);
        if (tb.size.z > tb.size.x)
        {
            table.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
            tb = WorldBounds(table);
        }
        table.transform.position -= new Vector3(tb.center.x, tb.min.y, tb.center.z);
        tb = WorldBounds(table);
        float topY = tb.max.y;
        foreach (Renderer r in table.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = ShadowCastingMode.On;

        List<Vector3> holes = FindHoles(table, topY);
        Debug.Log($"[Whack] table {tb.size.x:0.00} x {tb.size.z:0.00} m, top at {topY:0.000} m, {holes.Count} holes");
        if (holes.Count == 0) throw new Exception("No holes found on the table model");

        // soft shadow under the table
        Material mShade = Mat(Root, "TableShade", "Universal Render Pipeline/Unlit", new Color(0.20f, 0.25f, 0.38f, 0.55f), 1, 0, Tex(PartyBuild.Root + "/Textures/FloorFade.png"));
        GameObject shade = GameObject.CreatePrimitive(PrimitiveType.Quad);
        shade.name = "Whack_TableShade";
        Object.DestroyImmediate(shade.GetComponent<Collider>());
        shade.transform.SetPositionAndRotation(new Vector3(0f, 0.004f, 0f), Quaternion.Euler(90f, 0f, 0f));
        shade.transform.localScale = new Vector3(tb.size.x * 1.9f, tb.size.z * 2.2f, 1f);
        shade.GetComponent<Renderer>().sharedMaterial = mShade;
        shade.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;

        // ---- field and bunnies
        GameObject fieldGo = new GameObject("Whack_Field");
        WhackField field = fieldGo.AddComponent<WhackField>();
        field.tableTopY = topY;
        field.tableHalfSize = new Vector2(tb.extents.x, tb.extents.z);
        field.standDistance = tb.extents.z + 0.42f;
        var bunnies = new List<WhackBunny>();
        for (int i = 0; i < holes.Count; i++) bunnies.Add(BuildBunny(fieldGo.transform, i, holes[i]));
        field.bunnies = bunnies.ToArray();

        // ---- hammers
        for (int side = 0; side < 2; side++) BuildHammer(side, field, topY);

        // ---- game objects
        GameObject matchGo = new GameObject("Whack_Match");
        matchGo.AddComponent<NetworkObject>();
        WhackMatch match = matchGo.AddComponent<WhackMatch>();
        match.tuning.holes = holes.Count;

        new GameObject("Whack_Bot").AddComponent<WhackBot>().side = 1;

        GameObject player = new GameObject("Whack_Player");
        PartyRig rig = player.AddComponent<PartyRig>();
        rig.rig = Object.FindFirstObjectByType<OVRCameraRig>(FindObjectsInactive.Include);
        rig.homeSpot = field.StandSpot(0);
        rig.homeForward = field.Forward(0);
        player.AddComponent<WhackLocalPlayer>();

        BuildFx();
        BuildBoard(topY);

        SaveScene(scene);
        var list = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        if (!list.Exists(s => s.path == ScenePath))
        {
            list.Add(new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = list.ToArray();
        }
        Debug.Log("[Whack] Scene built: " + ScenePath);
    }

    static GameObject Spawn(string path, string name, Transform parent)
    {
        GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (asset == null)
        {
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }
        if (asset == null) throw new Exception("Model not found or not imported: " + path);
        GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(asset);
        go.name = name;
        if (parent != null) go.transform.SetParent(parent, false);
        return go;
    }

    /// <summary>All mesh vertices in world space (skinned meshes are baked in their current pose).</summary>
    static List<Vector3> WorldVerts(GameObject go, Func<Renderer, bool> filter = null)
    {
        var verts = new List<Vector3>();
        foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
        {
            if (filter != null && !filter(r)) continue;
            if (r is SkinnedMeshRenderer smr && smr.sharedMesh != null)
            {
                Mesh baked = new Mesh();
                smr.BakeMesh(baked, true);
                foreach (Vector3 v in baked.vertices) verts.Add(smr.transform.TransformPoint(v));
                Object.DestroyImmediate(baked);
            }
            else
            {
                MeshFilter mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                foreach (Vector3 v in mf.sharedMesh.vertices) verts.Add(r.transform.TransformPoint(v));
            }
        }
        return verts;
    }

    static Bounds BoundsOf(List<Vector3> verts)
    {
        if (verts.Count == 0) return new Bounds();
        Bounds b = new Bounds(verts[0], Vector3.zero);
        foreach (Vector3 v in verts) b.Encapsulate(v);
        return b;
    }

    static Bounds WorldBounds(GameObject go) => BoundsOf(WorldVerts(go));

    /// <summary>Finds the hole centres from the burrow tubes of the table model.</summary>
    static List<Vector3> FindHoles(GameObject table, float topY)
    {
        List<Vector3> verts = WorldVerts(table, r => r.name.IndexOf("Burrow", StringComparison.OrdinalIgnoreCase) >= 0);
        var groups = new List<Bounds>();
        foreach (Vector3 v in verts)
        {
            Vector3 flat = new Vector3(v.x, 0f, v.z);
            int found = -1;
            for (int i = 0; i < groups.Count && found < 0; i++)
            {
                Vector3 c = groups[i].center;
                if ((new Vector3(c.x, 0f, c.z) - flat).magnitude < 0.13f) found = i;
            }
            if (found < 0) groups.Add(new Bounds(flat, Vector3.zero));
            else { Bounds b = groups[found]; b.Encapsulate(flat); groups[found] = b; }
        }
        var holes = new List<Vector3>();
        foreach (Bounds b in groups) holes.Add(new Vector3(b.center.x, topY, b.center.z));
        holes.Sort((a, b) => Mathf.Abs(a.z - b.z) > 0.1f ? a.z.CompareTo(b.z) : a.x.CompareTo(b.x));
        return holes;
    }

    static Transform FindBone(GameObject go, string part, char sideLetter)
    {
        foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
        {
            string n = t.name;
            int at = n.IndexOf(part, StringComparison.OrdinalIgnoreCase);
            if (at < 0) continue;
            string rest = n.Substring(at + part.Length);
            if (rest.Length >= 2 && (rest[0] == '.' || rest[0] == '_') && char.ToUpperInvariant(rest[1]) == sideLetter) return t;
        }
        return null;
    }

    static WhackBunny BuildBunny(Transform parent, int index, Vector3 hole)
    {
        GameObject go = new GameObject("Bunny_" + index);
        go.transform.SetParent(parent, false);
        go.transform.position = hole;
        WhackBunny bunny = go.AddComponent<WhackBunny>();

        GameObject pivot = new GameObject("Model");
        pivot.transform.SetParent(go.transform, false);
        GameObject inst = Spawn(BunnyModel, "Bunny", pivot.transform);
        foreach (Animator a in inst.GetComponentsInChildren<Animator>(true)) Object.DestroyImmediate(a);
        foreach (Animation a in inst.GetComponentsInChildren<Animation>(true)) Object.DestroyImmediate(a);

        // stand it on the pivot, facing +Z (the eyes tell which way is the front)
        Bounds all = WorldBounds(inst);
        // the eyes are the smallest part in the upper half of the model
        Bounds eyes = new Bounds();
        float smallest = float.MaxValue;
        foreach (Renderer r in inst.GetComponentsInChildren<Renderer>(true))
        {
            Renderer one = r;
            Bounds b = BoundsOf(WorldVerts(inst, x => x == one));
            float volume = b.size.x * b.size.y * b.size.z;
            if (b.center.y > all.min.y + all.size.y * 0.55f && volume < smallest) { smallest = volume; eyes = b; }
        }
        Vector3 front = eyes.size.sqrMagnitude > 0f ? eyes.center - all.center : Vector3.forward;
        front.y = 0f;
        if (front.magnitude > all.extents.z * 0.05f)
            inst.transform.rotation = Quaternion.FromToRotation(front.normalized, Vector3.forward) * inst.transform.rotation;
        all = WorldBounds(inst);
        inst.transform.position += pivot.transform.position - new Vector3(all.center.x, all.min.y, all.center.z);

        bunny.model = pivot.transform;
        bunny.height = all.size.y;
        bunny.renderers = inst.GetComponentsInChildren<Renderer>(true);
        foreach (Renderer r in bunny.renderers)
        {
            r.shadowCastingMode = ShadowCastingMode.Off;
            if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = true;
        }
        bunny.leftArm = FindBone(inst, "UpperArm", 'L');
        bunny.rightArm = FindBone(inst, "UpperArm", 'R');
        bunny.leftHand = FindBone(inst, "Hand", 'L');
        bunny.rightHand = FindBone(inst, "Hand", 'R');
        if (index == 0)
            Debug.Log($"[Whack] bunny {all.size.x:0.000} x {all.size.y:0.000} x {all.size.z:0.000} m, front {front.normalized}, arms {(bunny.leftArm ? bunny.leftArm.name : "none")} / {(bunny.rightArm ? bunny.rightArm.name : "none")}");
        return bunny;
    }

    static void BuildHammer(int side, WhackField field, float topY)
    {
        GameObject go = new GameObject(side == 0 ? "Whack_Hammer_Blue" : "Whack_Hammer_Red");
        WhackHammer hammer = go.AddComponent<WhackHammer>();
        hammer.side = side;
        GameObject inst = Spawn(HammerModel, "Model", go.transform);

        // handle along +Y with the grip at the origin, whatever way the importer turned it
        Bounds handle = BoundsOf(WorldVerts(inst, r => r.name.IndexOf("Handle", StringComparison.OrdinalIgnoreCase) >= 0));
        Bounds head = BoundsOf(WorldVerts(inst, r => r.name.IndexOf("Head", StringComparison.OrdinalIgnoreCase) >= 0));
        Vector3 axis = head.center - handle.center;
        int longest = Mathf.Abs(axis.x) > Mathf.Abs(axis.y) ? (Mathf.Abs(axis.x) > Mathf.Abs(axis.z) ? 0 : 2) : (Mathf.Abs(axis.y) > Mathf.Abs(axis.z) ? 1 : 2);
        Vector3 dir = Vector3.zero;
        dir[longest] = Mathf.Sign(axis[longest]);
        inst.transform.rotation = Quaternion.FromToRotation(dir, Vector3.up) * inst.transform.rotation;

        head = BoundsOf(WorldVerts(inst, r => r.name.IndexOf("Head", StringComparison.OrdinalIgnoreCase) >= 0));
        Bounds all = WorldBounds(inst);
        hammer.headCenter = go.transform.InverseTransformPoint(head.center);
        bool alongX = head.size.x >= head.size.z;
        float thin = Mathf.Min(head.size.y, alongX ? head.size.z : head.size.x);
        hammer.headRadius = Mathf.Clamp(Mathf.Max(thin, head.size.y) * 0.5f, 0.04f, 0.09f);
        float half = Mathf.Max(0f, (alongX ? head.size.x : head.size.z) * 0.5f - hammer.headRadius);
        hammer.headHalf = alongX ? new Vector3(half, 0f, 0f) : new Vector3(0f, 0f, half);
        hammer.model = inst;
        hammer.renderers = inst.GetComponentsInChildren<Renderer>(true);
        foreach (Renderer r in hammer.renderers) r.shadowCastingMode = ShadowCastingMode.Off;

        WhackBot bot = Object.FindFirstObjectByType<WhackBot>();
        if (bot != null) bot.hammerReach = hammer.headCenter.y;

        // lying on the table edge until the game drives it
        float sign = WhackField.Sign(side);
        go.transform.position = new Vector3(-0.2f * sign, topY + 0.03f, sign * (field.tableHalfSize.y + 0.12f));
        go.transform.rotation = Quaternion.LookRotation(Vector3.up, new Vector3(sign * 0.25f, 0f, -sign).normalized);
        if (side == 0)
            Debug.Log($"[Whack] hammer {all.size.x:0.000} x {all.size.y:0.000} x {all.size.z:0.000} m, head centre {hammer.headCenter}, half {hammer.headHalf}, radius {hammer.headRadius:0.000}, grip y range {all.min.y - go.transform.position.y:0.000}");
    }

    static void BuildFx()
    {
        string star = SaveTexture(Root, "Star", 128, (u, v) =>
        {
            float x = u - 0.5f, y = v - 0.5f;
            float r = Mathf.Sqrt(x * x + y * y);
            float a = Mathf.Atan2(y, x);
            float edge = 0.46f * Mathf.Lerp(0.42f, 1f, Mathf.Pow(Mathf.Abs(Mathf.Cos(2.5f * a)), 1.6f));
            return new Color(1f, 1f, 1f, Mathf.Clamp01((edge - r) * 40f));
        });
        Material mStar = Mat(Root, "Star", "Universal Render Pipeline/Particles/Unlit", new Color(1f, 0.93f, 0.45f, 1f), 1, 0, Tex(star));
        Material mGold = Mat(Root, "StarGold", "Universal Render Pipeline/Particles/Unlit", new Color(1f, 0.74f, 0.12f, 1f), 1, 0, Tex(star));
        Material mPuff = Mat(Root, "Puff", "Universal Render Pipeline/Particles/Unlit", new Color(1f, 1f, 1f, 0.75f), 1, 0, Tex(PartyBuild.Root + "/Textures/CloudPuff.png"));

        GameObject root = new GameObject("Whack_Fx");
        WhackFx fx = root.AddComponent<WhackFx>();
        fx.stars = Burst("Stars", root.transform, mStar, 0.55f, 1.9f, 0.075f, 2.2f, 200);
        fx.goldStars = Burst("GoldStars", root.transform, mGold, 0.8f, 2.6f, 0.10f, 2.0f, 200);
        fx.puff = Burst("Puff", root.transform, mPuff, 0.4f, 0.6f, 0.13f, -0.2f, 120);
    }

    static ParticleSystem Burst(string name, Transform parent, Material mat, float life, float speed, float size, float gravity, int max)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.loop = false;
        main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.6f, life);
        main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.4f, speed);
        main.startSize = new ParticleSystem.MinMaxCurve(size * 0.6f, size * 1.3f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, 6.28f);
        main.gravityModifier = gravity;
        main.maxParticles = max;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        var emission = ps.emission;
        emission.enabled = false;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Hemisphere;
        shape.radius = 0.04f;
        shape.rotation = new Vector3(-90f, 0f, 0f);
        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0f));
        var rot = ps.rotationOverLifetime;
        rot.enabled = true;
        rot.z = new ParticleSystem.MinMaxCurve(-4f, 4f);
        ParticleSystemRenderer r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = ShadowCastingMode.Off;
        return ps;
    }

    static void BuildBoard(float topY)
    {
        const float W = 1000f, H = 430f, metres = 1.45f;
        GameObject root = new GameObject("Whack_Board");
        root.transform.position = new Vector3(0f, topY + 1.42f, 0f);
        WhackBoard board = root.AddComponent<WhackBoard>();

        var rounds = new List<TMP_Text>(); var heads = new List<TMP_Text>(); var timers = new List<TMP_Text>();
        var details = new List<TMP_Text>(); var blues = new List<TMP_Text>(); var reds = new List<TMP_Text>();
        var blueNames = new List<TMP_Text>(); var redNames = new List<TMP_Text>(); var bars = new List<Image>();

        for (int face = 0; face < 2; face++)
        {
            // face 0 is read by Blue (standing at -Z, looking +Z), face 1 by Red
            float sign = face == 0 ? -1f : 1f;
            Canvas canvas = WorldCanvas(face == 0 ? "FaceBlue" : "FaceRed", root.transform, new Vector3(0f, 0f, sign * 0.02f),
                Quaternion.LookRotation(new Vector3(0f, 0f, -sign)), new Vector2(W, H), metres);
            Color solid = PartyTeam.Card; solid.a = 1f;
            Transform card = Card(canvas.transform, "Card", 0f, 0f, W, H, solid, 44f);

            rounds.Add(Label(card, "Round", 250f, 16f, 500f, 40f, "WHACK-A-BUNNY", 30f, PartyTeam.Soft));

            // on Red's face the sides are mirrored so each score sits on that player's own left/right in the world
            bool blueLeft = face == 0;
            ScoreBlock(card, blueLeft ? 36f : W - 36f - 220f, PartyTeam.Blue, "BLUE", blueNames, blues);
            ScoreBlock(card, blueLeft ? W - 36f - 220f : 36f, PartyTeam.Red, "RED", redNames, reds);

            heads.Add(Label(card, "Headline", 270f, 60f, 460f, 70f, "", 52f, Color.white, TitleFont, TextAlignmentOptions.Center, true));
            timers.Add(Label(card, "Timer", 270f, 122f, 460f, 160f, "", 150f, PartyTeam.Gold, TitleFont));
            Panel(card, "BarBack", 290f, 290f, 420f, 16f, new Color(1f, 1f, 1f, 0.12f), 8f);
            Image bar = Panel(card, "Bar", 290f, 290f, 420f, 16f, PartyTeam.Gold, 8f);
            bar.type = Image.Type.Filled;
            bar.fillMethod = Image.FillMethod.Horizontal;
            bar.fillAmount = 0f;
            bars.Add(bar);
            details.Add(Label(card, "Detail", 40f, 326f, W - 80f, 86f, "", 28f, PartyTeam.Soft, BodyFont, TextAlignmentOptions.Center, true));
        }

        board.roundTexts = rounds.ToArray();
        board.headlineTexts = heads.ToArray();
        board.timerTexts = timers.ToArray();
        board.detailTexts = details.ToArray();
        board.blueTexts = blues.ToArray();
        board.redTexts = reds.ToArray();
        board.blueNames = blueNames.ToArray();
        board.redNames = redNames.ToArray();
        board.timerBars = bars.ToArray();
    }

    static void ScoreBlock(Transform card, float x, Color color, string name, List<TMP_Text> names, List<TMP_Text> scores)
    {
        Image block = Panel(card, name + "Block", x, 66f, 220f, 240f, color, 30f);
        Panel(block.transform, "Sheen", 0f, 0f, 220f, 110f, new Color(1f, 1f, 1f, 0.10f), 30f);
        names.Add(Label(block.transform, "Name", 0f, 12f, 220f, 50f, name, 40f, Color.white, TitleFont));
        scores.Add(Label(block.transform, "Score", 0f, 62f, 220f, 160f, "0", 130f, Color.white, TitleFont, TextAlignmentOptions.Center, true));
    }

    // ================================================================== screenshots

    [MenuItem("Tools/Party/Capture Whack-a-Bunny Screenshots")]
    public static void CaptureScreenshots()
    {
        WhackField field = Object.FindFirstObjectByType<WhackField>();
        WhackBoard board = Object.FindFirstObjectByType<WhackBoard>();
        if (field == null) return;

        // pose a few bunnies up for the picture (not saved)
        int[] up = { 1, 3, 5 };
        foreach (int i in up)
        {
            if (i >= field.bunnies.Length) continue;
            WhackBunny b = field.bunnies[i];
            b.model.localPosition = new Vector3(0f, b.upY, 0f);
            b.transform.rotation = Quaternion.LookRotation(Vector3.back);
        }
        foreach (WhackBunny b in field.bunnies)
            if (Array.IndexOf(up, Array.IndexOf(field.bunnies, b)) < 0) b.model.localPosition = new Vector3(0f, -0.4f, 0f);
        if (board != null)
        {
            foreach (TMP_Text t in board.headlineTexts) t.text = PartyTeam.Tag(0, "BLUE") + "'S TURN";
            foreach (TMP_Text t in board.timerTexts) t.text = "12";
            foreach (TMP_Text t in board.roundTexts) t.text = "ROUND 1 / 2";
            foreach (TMP_Text t in board.detailTexts) t.text = "PARTY   " + PartyTeam.Tag(0, "BLUE 90") + "   :   " + PartyTeam.Tag(1, "40 RED");
            foreach (TMP_Text t in board.blueTexts) t.text = "35";
            foreach (TMP_Text t in board.redTexts) t.text = "0";
            foreach (Image i in board.timerBars) { i.fillAmount = 0.8f; i.color = PartyTeam.Blue; }
        }

        float top = field.tableTopY;
        Shot("whack_warmup.png", new Vector3(0.75f, 1.45f, -1.25f), new Vector3(0f, top, 0f), 50f);   // first render of a scene can miss textures
        System.IO.File.Delete(System.IO.Path.Combine(ShotDir(), "whack_warmup.png"));
        Shot("whack_blue_pov.png", new Vector3(0f, 1.62f, -field.standDistance - 0.05f), new Vector3(0f, top + 0.35f, 0.1f), 78f);
        Shot("whack_table.png", new Vector3(0.75f, 1.45f, -1.25f), new Vector3(0f, top + 0.05f, 0f), 50f);
        Shot("whack_overview.png", new Vector3(2.4f, 2.0f, -2.9f), new Vector3(0f, 1.25f, 0f), 55f);
        Shot("whack_close.png", new Vector3(0.28f, top + 0.42f, -0.62f), new Vector3(0f, top + 0.06f, 0f), 50f);
        Debug.Log("[Whack] Screenshots saved to " + ShotDir());
    }

    // ================================================================== self-test

    public static bool SelfTest()
    {
        int fails = 0;
        void Check(bool ok, string what)
        {
            if (ok) return;
            fails++;
            Debug.LogError("[Whack] SELF-TEST FAIL: " + what);
        }

        WhackTuning t = new WhackTuning();
        const float T = 15f;
        int minPops = int.MaxValue, maxPops = 0, goldTotal = 0, total = 0;
        float botSum = 0f, perfectSum = 0f;
        System.Random rng = new System.Random(7);
        for (int seed = 1; seed <= 300; seed++)
        {
            List<WhackPop> pops = WhackBunnySim.Schedule(seed * 7919, T, t);
            minPops = Mathf.Min(minPops, pops.Count);
            maxPops = Mathf.Max(maxPops, pops.Count);
            float[] free = new float[t.holes];
            float botFree = 0f;
            for (int i = 0; i < pops.Count; i++)
            {
                WhackPop p = pops[i];
                total++;
                if (p.golden) goldTotal++;
                Check(p.id == i, $"seed {seed}: pop id {p.id} at index {i}");
                Check(p.hole >= 0 && p.hole < t.holes, $"seed {seed}: hole {p.hole}");
                Check(p.up >= 0f && p.Down <= T + 0.001f, $"seed {seed}: pop outside the turn ({p.up:0.00}..{p.Down:0.00})");
                Check(p.stay > 0.3f, $"seed {seed}: pop stays only {p.stay:0.00}s");
                Check(p.up >= free[p.hole] - 0.001f, $"seed {seed}: two bunnies in hole {p.hole}");
                free[p.hole] = p.Down + t.holeRest;
                int up = 0;
                foreach (WhackPop q in pops)
                    if (q.up <= p.up && q.Down > p.up) up++;
                Check(up <= t.maxUp, $"seed {seed}: {up} bunnies up at once");

                perfectSum += p.golden ? 15 : 5;
                if (rng.NextDouble() <= (p.golden ? 0.5 : 0.72))
                {
                    float time = Mathf.Max(p.up + 0.42f + (float)rng.NextDouble() * 0.38f, botFree);
                    if (time <= p.Down - 0.03f) { botSum += p.golden ? 15 : 5; botFree = time + 0.33f; }
                }
            }
            // the same seed always gives the same turn
            List<WhackPop> again = WhackBunnySim.Schedule(seed * 7919, T, t);
            Check(again.Count == pops.Count && (pops.Count == 0 || Mathf.Approximately(again[pops.Count - 1].up, pops[pops.Count - 1].up)), $"seed {seed}: schedule not repeatable");
        }
        Check(minPops >= 14 && maxPops <= 45, $"pops per turn out of range: {minPops}..{maxPops}");
        float botAvg = botSum / 300f, perfect = perfectSum / 300f;
        Check(botAvg > 25f && botAvg < perfect * 0.8f, $"bot average {botAvg:0} of {perfect:0}");

        // party points
        PartyScoring.Round(50, 30, 50, out int b, out int r, out int w);
        Check(b == 100 && r == 30 && w == 0, "round bonus to the winner");
        PartyScoring.Round(40, 40, 50, out b, out r, out w);
        Check(b == 65 && r == 65 && w == 2, "round bonus split on a tie");
        for (int n = 1; n <= 6; n++)
        {
            byte[] q = PartyScoring.Shuffle(n, new System.Random(n));
            var seen = new HashSet<byte>(q);
            Check(q.Length == n && seen.Count == n, $"shuffle of {n} repeats a game");
        }

        Debug.Log($"[Whack] SELF-TEST {(fails == 0 ? "PASS" : "FAIL (" + fails + ")")}: {minPops}-{maxPops} pops per {T:0}s turn, {100f * goldTotal / Mathf.Max(1, total):0}% golden, " +
                  $"perfect turn {perfect:0} points, bot average {botAvg:0} points");
        return fails == 0;
    }
}

/// <summary>Builds everything: Tools > Party > Build All, or -executeMethod PartyBuilder.BatchBuildAll from the command line.</summary>
public static class PartyBuilder
{
    [MenuItem("Tools/Party/Build All Scenes")]
    public static void BuildAll()
    {
        LobbyBuilder.BuildScene();
        LobbyBuilder.CaptureScreenshots();
        KeeperClashBuilder.BuildScene();
        KeeperClashBuilder.CaptureScreenshots();
        WhackBunnyBuilder.BuildScene();
        WhackBunnyBuilder.CaptureScreenshots();
        ScenesFirst(LobbyBuilder.ScenePath, KeeperClashBuilder.ScenePath, WhackBunnyBuilder.ScenePath);
        AssetDatabase.SaveAssets();
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene(LobbyBuilder.ScenePath);
    }

    public static void BatchBuildAll()
    {
        int code = 0;
        try
        {
            bool ok = KeeperClashBuilder.SelfTest();
            ok &= WhackBunnyBuilder.SelfTest();
            BuildAll();
            if (!ok) code = 2;
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            code = 1;
        }
        Debug.Log($"[Party] BATCH DONE exit={code}");
        EditorApplication.Exit(code);
    }
}
