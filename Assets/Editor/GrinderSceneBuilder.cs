using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Oculus.Interaction;
using Oculus.Interaction.HandGrab;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// Sets up the hide-and-seek room in the grinder scene (Tools > Grinder > Build Hide And Seek Room). Re-runnable:
/// everything it makes is named "Hide_..." and is replaced on the next run. What it does:
///  - puts our toon room around the player's start spot and switches the old room, grinders and loose items off
///  - makes the Blue/Red item prefabs (wood, rope, stone) in Assets/Prefabs/Hide
///  - places a Blue and a Red toon grinder under their banners (they only take their own colour)
///  - places the hiding props: buckets and boxes to lift, crates and trash cans with loose lids,
///    cabinets and tables with sliding drawers, and marks the hiding spots
///  - adds Hide_Sync, which hides the items at random when the host starts
/// The old objects are only switched off, not deleted, so nothing is lost.
/// </summary>
public static class GrinderSceneBuilder
{
    const string ScenePath = "Assets/Scenes/SampleScene.unity";
    const string Models = "Assets/Models/GrinderProps/";
    const string RoomModel = "Assets/Models/GrinderRoom/ToonRoom.glb";
    const string PrefabDir = "Assets/Prefabs/Hide";
    const string Prefix = "Hide_";
    const float RoomRadius = 6f;

    static GameObject stoneTemplate;
    static readonly List<HidingSpot> spots = new List<HidingSpot>();
    static readonly List<SlideDrawer> drawers = new List<SlideDrawer>();

    [MenuItem("Tools/Grinder/Build Hide And Seek Room")]
    public static void BuildMenu()
    {
        Build();
        Screenshots();
    }

    /// <summary>For -executeMethod: builds the scene, saves pictures, exits with 0 when all went well.</summary>
    public static void BatchBuild()
    {
        int code = 0;
        try { Build(); Screenshots(); }
        catch (Exception e) { Debug.LogError("[Grinder] BUILD FAILED: " + e); code = 1; }
        Debug.Log("[Grinder] BATCH DONE exit=" + code);
        EditorApplication.Exit(code);
    }

    public static void Build()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        spots.Clear();
        drawers.Clear();
        stoneTemplate = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Stone.prefab");
        if (stoneTemplate == null || stoneTemplate.GetComponent<Grabbable>() == null)
            throw new Exception("Assets/Prefabs/Stone.prefab with a Grabbable is needed as the template for grabbing");

        foreach (GameObject root in scene.GetRootGameObjects())
            if (root.name.StartsWith(Prefix)) Object.DestroyImmediate(root);

        // ---- where the room goes: where the old room is, in the middle between its two grinders.
        // (The test area around the scene origin is left alone.)
        OVRCameraRig rig = Object.FindFirstObjectByType<OVRCameraRig>(FindObjectsInactive.Include);
        GameObject g1 = FindRoot(scene, "Grinder (1)"), g2 = FindRoot(scene, "Grinder (2)");
        Vector3 centre = g1 != null && g2 != null ? (g1.transform.position + g2.transform.position) * 0.5f
            : rig != null ? rig.transform.position : Vector3.zero;

        // ---- the old grinders give us the recipes and settings
        Grinder oldGrinder = g1 != null ? g1.GetComponent<Grinder>() : null;
        foreach (Grinder g in Object.FindObjectsByType<Grinder>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (!g.name.StartsWith(Prefix) && oldGrinder == null) oldGrinder = g;
        ObjectDetection oldDetect = oldGrinder != null ? oldGrinder.GetComponentInChildren<ObjectDetection>(true) : null;
        if (oldGrinder == null) Debug.LogWarning("[Grinder] No old Grinder in the scene: the new grinders have no recipes yet, set them in the Inspector.");

        // ---- the player starts in the middle of the room; the Host/Join panel comes along
        if (rig != null)
        {
            Vector3 delta = centre - rig.transform.position;
            if (delta.sqrMagnitude > 0.0001f)
            {
                Debug.Log($"[Grinder] moved the camera rig from {rig.transform.position} to {centre}");
                rig.transform.position = centre;
                foreach (RelayNetworkButtons ui in Object.FindObjectsByType<RelayNetworkButtons>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    ui.transform.root.position += delta;
            }
        }

        // ---- room
        // turned so the Blue and Red banner walls are where the two old grinders stood
        float roomYaw = g1 != null && g2 != null && Mathf.Abs(g1.transform.position.z - g2.transform.position.z) > Mathf.Abs(g1.transform.position.x - g2.transform.position.x) ? 90f : 0f;
        GameObject room = Spawn(RoomModel, Prefix + "Room", centre, Quaternion.Euler(0f, roomYaw, 0f));
        foreach (MeshFilter mf in room.GetComponentsInChildren<MeshFilter>())
        {
            string n = mf.name;
            MeshRenderer mr = mf.GetComponent<MeshRenderer>();
            if (mr != null) mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            bool solid = n.StartsWith("Floor") || n.StartsWith("Wall_") || n.StartsWith("Ceiling");
            if (solid) mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
            mf.gameObject.isStatic = true;
        }
        Vector3 blueDir = DirectionTo(room, "BannerBlue", centre, Vector3.right);
        Vector3 redDir = DirectionTo(room, "BannerRed", centre, Vector3.left);

        GameObject lightGo = new GameObject(Prefix + "Light");
        lightGo.transform.position = centre + Vector3.up * 3.6f;
        Light light = lightGo.AddComponent<Light>();
        light.type = LightType.Point;
        light.range = 16f;
        light.intensity = 9f;
        light.color = new Color(1f, 0.95f, 0.85f);
        light.shadows = LightShadows.None;

        // ---- item prefabs
        if (!AssetDatabase.IsValidFolder(PrefabDir)) AssetDatabase.CreateFolder("Assets/Prefabs", "Hide");
        var items = new List<GameObject>();
        foreach (string team in new[] { "Blue", "Red" })
        {
            int t = team == "Blue" ? 0 : 1;
            items.Add(ItemPrefab("Wood_" + team, t, TeamItemKind.Wood, "Assets/Prefabs/Wood.prefab", true));
            items.Add(ItemPrefab("Rope_" + team, t, TeamItemKind.Rope, "Assets/Prefabs/Rope.prefab", true));
            items.Add(ItemPrefab("Stone_" + team, t, TeamItemKind.Stone, "Assets/Prefabs/Stone.prefab", false));
        }
        RegisterNetworkPrefabs(items);

        // ---- grinders under their banners, facing the middle
        MakeGrinder("Blue", 0, centre + blueDir * (RoomRadius - 1.3f), -blueDir, oldGrinder, oldDetect);
        MakeGrinder("Red", 1, centre + redDir * (RoomRadius - 1.3f), -redDir, oldGrinder, oldDetect);

        // ---- props. Angles are measured from the Blue banner, so the layout is the same for both teams.
        float a0 = Mathf.Atan2(blueDir.z, blueDir.x) * Mathf.Rad2Deg;
        int k = 0;
        foreach (float a in new[] { 45f, 135f, 225f, 315f }) Cabinet(k++, centre, a0 + a, RoomRadius - 0.34f);
        k = 0;
        foreach (float a in new[] { 90f, 270f }) Table(k++, centre, a0 + a, RoomRadius - 0.5f);
        k = 0;
        foreach (float a in new[] { 62f, 118f, 242f, 298f }) Crate(k++, centre, a0 + a, 3.5f);
        k = 0;
        foreach (float a in new[] { 24f, 156f, 204f, 336f }) LiftBox(k++, centre, a0 + a, 4.4f);
        k = 0;
        foreach (float a in new[] { 78f, 102f, 258f, 282f }) Bucket(k++, centre, a0 + a, 2.3f);
        k = 0;
        foreach (float a in new[] { 70f, 250f }) TrashCan(k++, centre, a0 + a, RoomRadius - 0.45f);

        // ---- the script that hides the items and syncs the drawers
        GameObject sync = new GameObject(Prefix + "Sync");
        sync.transform.position = centre;
        sync.AddComponent<NetworkObject>();
        HideSync hs = sync.AddComponent<HideSync>();
        hs.itemPrefabs = items.ToArray();
        hs.copiesOfEach = 3;
        hs.spots = spots.ToArray();
        hs.drawers = drawers.ToArray();
        for (int i = 0; i < drawers.Count; i++) drawers[i].id = i;

        // ---- switch the old things off (kept in the scene, just inactive)
        int off = 0;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.name.StartsWith(Prefix) || !root.activeSelf) continue;
            Vector3 d = root.transform.position - centre;
            d.y = 0f;
            bool inRoom = d.magnitude < RoomRadius + 1.5f;
            bool oldRoom = root.name.StartsWith("corporate_clash");
            bool oldGrinderObj = inRoom && root.GetComponent<Grinder>() != null;
            bool oldProps = inRoom && root.name == "Props_TableFan";
            if (oldRoom || oldGrinderObj || oldProps) { root.SetActive(false); off++; Debug.Log("[Grinder] switched off: " + root.name); }
        }

        RefreshNetworkObjectHashes();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log($"[Grinder] Scene built: room at {centre}, {spots.Count} hiding spots, {drawers.Count} drawers, {items.Count} item prefabs, {off} old objects switched off");
    }

    // ================================================================== helpers

    static GameObject Spawn(string assetPath, string name, Vector3 pos, Quaternion rot, bool unpack = true)
    {
        GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
        if (asset == null) throw new Exception("Model not found (is it imported?): " + assetPath);
        GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(asset);
        if (unpack) PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        go.name = name;
        go.transform.SetPositionAndRotation(pos, rot);
        return go;
    }

    static GameObject FindRoot(UnityEngine.SceneManagement.Scene scene, string name)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
            if (root.name == name) return root;
        return null;
    }

    static Vector3 DirectionTo(GameObject room, string child, Vector3 centre, Vector3 fallback)
    {
        foreach (Renderer r in room.GetComponentsInChildren<Renderer>())
            if (r.name == child)
            {
                Vector3 d = r.bounds.center - centre;
                d.y = 0f;
                if (d.sqrMagnitude > 0.01f) return d.normalized;
            }
        Debug.LogWarning("[Grinder] Room part not found: " + child);
        return fallback;
    }

    static Transform Find(GameObject root, string name)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == name || t.name.StartsWith(name + ".")) return t;   // Blender names duplicates "Lid.001"
        return null;
    }

    static Bounds BoundsOf(GameObject go)
    {
        Renderer[] rs = go.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
        Bounds b = rs[0].bounds;
        foreach (Renderer r in rs) b.Encapsulate(r.bounds);
        return b;
    }

    static void SetLayer(GameObject go, int layer)
    {
        foreach (Transform t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
    }

    /// <summary>A box collider on its own child object, given in the parent's local space.</summary>
    static BoxCollider Box(Transform parent, string name, Vector3 localCentre, Vector3 size, float yaw = 0f)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localCentre;
        go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        go.layer = parent.gameObject.layer;
        BoxCollider c = go.AddComponent<BoxCollider>();
        c.size = new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z));
        return c;
    }

    /// <summary>A ring of thin boxes: the wall of a round, hollow thing (bucket, trash can).</summary>
    static void Ring(Transform parent, float radius, float y0, float y1, float thickness = 0.02f, int n = 8)
    {
        float width = 2f * radius * Mathf.Tan(Mathf.PI / n) + 0.01f;
        for (int i = 0; i < n; i++)
        {
            float a = i * 360f / n;
            Vector3 dir = Quaternion.Euler(0f, a, 0f) * Vector3.forward;
            Box(parent, "Wall" + i, dir * radius + Vector3.up * (y0 + y1) * 0.5f, new Vector3(width, y1 - y0, thickness), a);
        }
    }

    static HidingSpot Spot(Transform parent, Vector3 worldPos, bool large)
    {
        GameObject go = new GameObject("HidingSpot");
        go.transform.SetParent(parent, true);
        go.transform.position = worldPos;
        HidingSpot s = go.AddComponent<HidingSpot>();
        s.large = large;
        spots.Add(s);
        return s;
    }

    static Vector3 Polar(Vector3 centre, float angleDeg, float radius)
    {
        float a = angleDeg * Mathf.Deg2Rad;
        return centre + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius;
    }

    /// <summary>Rotation that turns an object's +Z towards the middle of the room.</summary>
    static Quaternion FaceCentre(Vector3 centre, Vector3 pos, float extraYaw = 0f)
    {
        Vector3 d = centre - pos;
        d.y = 0f;
        return Quaternion.LookRotation(d.sqrMagnitude > 1e-4f ? d.normalized : Vector3.forward) * Quaternion.Euler(0f, extraYaw, 0f);
    }

    // ---- grabbing, copied from the Stone prefab so it behaves like the items the game already has

    static Grabbable MakeGrabbable(GameObject root, Rigidbody rb, bool throwable)
    {
        Grabbable g = root.AddComponent<Grabbable>();
        EditorUtility.CopySerialized(stoneTemplate.GetComponent<Grabbable>(), g);
        SerializedObject so = new SerializedObject(g);
        so.FindProperty("_targetTransform").objectReferenceValue = root.transform;
        so.FindProperty("_rigidbody").objectReferenceValue = rb;
        so.FindProperty("_oneGrabTransformer").objectReferenceValue = null;
        so.FindProperty("_twoGrabTransformer").objectReferenceValue = null;
        so.FindProperty("_forwardElement").objectReferenceValue = null;
        so.FindProperty("_throwWhenUnselected").boolValue = throwable;
        so.ApplyModifiedPropertiesWithoutUndo();

        GameObject isdk = new GameObject("ISDK_HandGrabInteraction");
        isdk.transform.SetParent(root.transform, false);
        isdk.layer = root.layer;
        HandGrabInteractable hand = isdk.AddComponent<HandGrabInteractable>();
        EditorUtility.CopySerialized(stoneTemplate.GetComponentInChildren<HandGrabInteractable>(true), hand);
        GrabInteractable grab = isdk.AddComponent<GrabInteractable>();
        EditorUtility.CopySerialized(stoneTemplate.GetComponentInChildren<GrabInteractable>(true), grab);
        foreach (Object c in new Object[] { hand, grab })
        {
            SerializedObject s = new SerializedObject(c);
            s.FindProperty("_pointableElement").objectReferenceValue = g;
            s.FindProperty("_rigidbody").objectReferenceValue = rb;
            s.ApplyModifiedPropertiesWithoutUndo();
        }
        return g;
    }

    static Rigidbody Body(GameObject root, float mass, bool kinematic = false)
    {
        Rigidbody rb = root.AddComponent<Rigidbody>();
        rb.mass = mass;
        rb.isKinematic = kinematic;
        rb.useGravity = !kinematic;
        rb.collisionDetectionMode = kinematic ? CollisionDetectionMode.Discrete : CollisionDetectionMode.ContinuousDynamic;
        rb.angularDamping = 0.4f;
        return rb;
    }

    /// <summary>Makes a loose object that both players see move: grab, carry, throw.</summary>
    static void MakeNetworkedLoose(GameObject root, Rigidbody rb)
    {
        MakeGrabbable(root, rb, true);
        root.AddComponent<NetworkObject>();
        NetworkTransform nt = root.AddComponent<NetworkTransform>();
        EditorUtility.CopySerialized(stoneTemplate.GetComponent<NetworkTransform>(), nt);
        root.AddComponent<NetworkedPickable>();
    }

    // ================================================================== item prefabs

    static GameObject ItemPrefab(string name, int team, TeamItemKind kind, string oldPrefabPath, bool needsLarge)
    {
        GameObject old = AssetDatabase.LoadAssetAtPath<GameObject>(oldPrefabPath);
        GameObject root = new GameObject(name);
        if (old != null) { root.tag = old.tag; root.layer = old.layer; }

        GameObject visual = Spawn(Models + name + ".glb", "Visual", Vector3.zero, Quaternion.identity);
        // keep the rotation the model was exported with (the wood lies flat), centre it on the root
        GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(Models + name + ".glb");
        visual.transform.rotation = modelAsset.transform.rotation;
        visual.transform.SetParent(root.transform, true);
        Bounds b = BoundsOf(visual);
        visual.transform.position -= b.center;
        SetLayer(root, root.layer);

        BoxCollider col = root.AddComponent<BoxCollider>();
        col.size = Vector3.Max(b.size, Vector3.one * 0.03f);
        Rigidbody rb = Body(root, 0.3f);
        MakeNetworkedLoose(root, rb);
        TeamItem item = root.AddComponent<TeamItem>();
        item.team = team;
        item.kind = kind;
        item.needsLargeSpot = needsLarge;

        string path = $"{PrefabDir}/{name}.prefab";
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
        Debug.Log($"[Grinder] item prefab {path} size {b.size} tag {prefab.tag} layer {prefab.layer}");
        return prefab;
    }

    static void RegisterNetworkPrefabs(List<GameObject> prefabs)
    {
        NetworkPrefabsList list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>("Assets/DefaultNetworkPrefabs.asset");
        if (list == null) { Debug.LogError("[Grinder] Assets/DefaultNetworkPrefabs.asset not found: add the Hide prefabs to the NetworkManager by hand"); return; }
        foreach (GameObject p in prefabs)
            if (!list.Contains(p)) list.Add(new NetworkPrefab { Prefab = p });
        EditorUtility.SetDirty(list);
    }

    // ================================================================== grinder

    static void MakeGrinder(string team, int teamIndex, Vector3 pos, Vector3 front, Grinder oldGrinder, ObjectDetection oldDetect)
    {
        const float scale = 1.1f;
        GameObject root = Spawn($"{Models}Grinder_{team}.glb", $"{Prefix}Grinder_{team}", pos, Quaternion.identity);
        root.transform.localScale = Vector3.one * scale;
        Transform hopper = Find(root, "Hopper"), chute = Find(root, "Chute"), crank = Find(root, "Crank");
        if (hopper == null || chute == null || crank == null) throw new Exception("Grinder model needs Hopper, Chute and Crank parts");

        // turn it so the chute points at the middle of the room
        Vector3 modelFront = chute.position - root.transform.position;
        modelFront.y = 0f;
        root.transform.rotation = Quaternion.FromToRotation(modelFront.normalized, front.normalized);

        Bounds b = BoundsOf(root);
        float bodyTop = hopper.position.y;
        BoxCollider body = root.AddComponent<BoxCollider>();
        Vector3 bodySize = new Vector3(0.7f, (bodyTop - pos.y) / scale, 0.6f);
        body.center = new Vector3(0f, bodySize.y * 0.5f, 0f);
        body.size = bodySize;
        // the model's local axes: find which one is "front" so the box is not turned sideways
        Vector3 localFront = root.transform.InverseTransformDirection(front);
        if (Mathf.Abs(localFront.x) > Mathf.Abs(localFront.z)) body.size = new Vector3(bodySize.z, bodySize.y, bodySize.x);

        // spinning parts: Grinder.cs turns its two objects around their local up axis
        Vector3 side = crank.position - root.transform.position;
        side.y = 0f;
        GameObject crankSpin = Pivot(root.transform, "CrankSpin", crank.position, side.normalized);
        crank.SetParent(crankSpin.transform, true);
        Transform gear = Find(root, $"Grinder_{team}_Gear"), hub = Find(root, $"Grinder_{team}_GearHub");
        GameObject gearSpin = null;
        if (gear != null)
        {
            Bounds gb = gear.GetComponent<Renderer>().bounds;
            gearSpin = Pivot(root.transform, "GearSpin", gb.center, front.normalized);
            gear.SetParent(gearSpin.transform, true);
            if (hub != null) hub.SetParent(gearSpin.transform, true);
        }

        // where items go in
        Bounds hb = BoundsOf(hopper.gameObject);
        GameObject trigger = new GameObject("HopperTrigger");
        trigger.transform.SetParent(root.transform, true);
        trigger.transform.position = new Vector3(hb.center.x, hb.min.y + hb.size.y * 0.35f, hb.center.z);
        SphereCollider sc = trigger.AddComponent<SphereCollider>();
        sc.isTrigger = true;
        sc.radius = 0.16f / scale;
        GameObject animate = new GameObject("AnimatePosition");
        animate.transform.SetParent(root.transform, true);
        animate.transform.SetPositionAndRotation(new Vector3(hb.center.x, hb.min.y + hb.size.y * 0.5f, hb.center.z), root.transform.rotation);

        // where the reward comes out
        GameObject spawn = new GameObject("RewardSpawnPoint");
        spawn.transform.SetParent(root.transform, true);
        spawn.transform.SetPositionAndRotation(chute.position + Vector3.up * 0.12f + front.normalized * 0.05f, Quaternion.LookRotation(front.normalized));

        Grinder g = root.AddComponent<Grinder>();
        if (oldGrinder != null) EditorUtility.CopySerialized(oldGrinder, g);
        SerializedObject so = new SerializedObject(g);
        so.FindProperty("team").intValue = teamIndex;
        so.FindProperty("Grinder1").objectReferenceValue = crankSpin;
        so.FindProperty("Grinder2").objectReferenceValue = gearSpin;
        so.FindProperty("rotationSpeed").floatValue = 120f;
        so.FindProperty("rewardSpawnPoint").objectReferenceValue = spawn.transform;
        so.FindProperty("pipeDeformable").objectReferenceValue = null;
        so.FindProperty("travelDirection").vector3Value = Vector3.forward;
        so.FindProperty("travelDistance").floatValue = 0.45f;
        so.FindProperty("addedItems").ClearArray();
        so.ApplyModifiedPropertiesWithoutUndo();

        ObjectDetection od = trigger.AddComponent<ObjectDetection>();
        if (oldDetect != null) EditorUtility.CopySerialized(oldDetect, od);
        SerializedObject sd = new SerializedObject(od);
        sd.FindProperty("animatePosition").objectReferenceValue = animate;
        sd.FindProperty("grinder").objectReferenceValue = g;
        if (oldDetect == null)
        {
            int mask = 0;
            foreach (string p in new[] { "Wood", "Rope" })
            {
                GameObject old = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Prefabs/{p}.prefab");
                if (old != null) mask |= 1 << old.layer;
            }
            sd.FindProperty("pickable_layers").intValue = mask;
        }
        sd.ApplyModifiedPropertiesWithoutUndo();
        Debug.Log($"[Grinder] {root.name} at {pos}, hopper rim {hb.max.y - pos.y:F2} m high, size {b.size}");
    }

    /// <summary>An empty whose local up axis points along 'axis'; children spin around it when Grinder.cs rotates it.</summary>
    static GameObject Pivot(Transform parent, string name, Vector3 worldPos, Vector3 axis)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, true);
        go.transform.SetPositionAndRotation(worldPos, Quaternion.FromToRotation(Vector3.up, axis));
        return go;
    }

    // ================================================================== hiding props

    static void Bucket(int k, Vector3 centre, float angle, float radius)
    {
        const float s = 1.25f;  // a bit bigger than life so a stone fits under it
        Vector3 pos = Polar(centre, angle, radius);
        GameObject root = new GameObject($"{Prefix}Bucket_{k}");
        root.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, k * 67f, 0f));
        // upside down: the open side is on the floor and hides what is under it
        GameObject visual = Spawn(Models + "Bucket.glb", "Visual", pos + Vector3.up * 0.30f * s, Quaternion.Euler(180f, 0f, 0f));
        visual.transform.localScale = Vector3.one * s;
        visual.transform.SetParent(root.transform, true);
        Ring(root.transform, 0.15f * s, 0f, 0.30f * s);
        Box(root.transform, "Top", new Vector3(0f, 0.29f * s, 0f), new Vector3(0.28f, 0.02f, 0.28f) * s);
        MakeNetworkedLoose(root, Body(root, 0.5f));
        Spot(null, pos + Vector3.up * 0.06f, false).transform.SetParent(HolderFor(root), true);
    }

    static void LiftBox(int k, Vector3 centre, float angle, float radius)
    {
        const float w = 0.46f, d = 0.36f, h = 0.34f, t = 0.02f;
        Vector3 pos = Polar(centre, angle, radius);
        GameObject root = new GameObject($"{Prefix}CardboardBox_{k}");
        root.transform.SetPositionAndRotation(pos, FaceCentre(centre, pos, 20f * (k % 2 == 0 ? 1 : -1)));
        GameObject visual = Spawn(Models + "CardboardBox.glb", "Visual", pos, root.transform.rotation);
        visual.transform.SetParent(root.transform, true);
        Vector3 size = LocalSize(root.transform, visual, new Vector3(w, h, d));
        float lx = size.x, lz = size.z;
        Box(root.transform, "Top", new Vector3(0f, h - t * 0.5f, 0f), new Vector3(lx, t, lz));
        Box(root.transform, "WallL", new Vector3(-lx * 0.5f + t * 0.5f, h * 0.5f, 0f), new Vector3(t, h, lz));
        Box(root.transform, "WallR", new Vector3(lx * 0.5f - t * 0.5f, h * 0.5f, 0f), new Vector3(t, h, lz));
        Box(root.transform, "WallF", new Vector3(0f, h * 0.5f, lz * 0.5f - t * 0.5f), new Vector3(lx, h, t));
        Box(root.transform, "WallB", new Vector3(0f, h * 0.5f, -lz * 0.5f + t * 0.5f), new Vector3(lx, h, t));
        MakeNetworkedLoose(root, Body(root, 0.6f));
        Spot(null, pos + Vector3.up * 0.06f, true).transform.SetParent(HolderFor(root), true);
    }

    /// <summary>Hiding spots of loose props must not move with the prop, so they live under one fixed holder.</summary>
    static Transform HolderFor(GameObject prop)
    {
        GameObject holder = GameObject.Find(Prefix + "Spots");
        if (holder == null) holder = new GameObject(Prefix + "Spots");
        return holder.transform;
    }

    /// <summary>The model's footprint measured in the root's local axes (models may come in turned by 90 degrees).</summary>
    static Vector3 LocalSize(Transform root, GameObject visual, Vector3 fallback)
    {
        Quaternion keep = root.rotation;
        root.rotation = Quaternion.identity;
        Bounds b = BoundsOf(visual);
        root.rotation = keep;
        return b.size.sqrMagnitude > 1e-4f ? b.size : fallback;
    }

    static void Crate(int k, Vector3 centre, float angle, float radius)
    {
        const float s = 0.52f, h = 0.44f, t = 0.03f;
        Vector3 pos = Polar(centre, angle, radius);
        Quaternion rot = FaceCentre(centre, pos, 15f * (k % 2 == 0 ? 1 : -1));
        GameObject root = Spawn(Models + "Crate.glb", $"{Prefix}Crate_{k}", pos, rot);
        Box(root.transform, "Bottom", new Vector3(0f, 0.01f, 0f), new Vector3(s, 0.02f, s));
        Box(root.transform, "WallL", new Vector3(-s * 0.5f + t * 0.5f, h * 0.5f, 0f), new Vector3(t, h, s));
        Box(root.transform, "WallR", new Vector3(s * 0.5f - t * 0.5f, h * 0.5f, 0f), new Vector3(t, h, s));
        Box(root.transform, "WallF", new Vector3(0f, h * 0.5f, s * 0.5f - t * 0.5f), new Vector3(s, h, t));
        Box(root.transform, "WallB", new Vector3(0f, h * 0.5f, -s * 0.5f + t * 0.5f), new Vector3(s, h, t));
        Spot(root.transform, pos + Vector3.up * 0.08f, true);

        // the lid is a loose plank you lift off
        Transform lidPart = Find(root, "Lid");
        GameObject lid = new GameObject($"{Prefix}CrateLid_{k}");
        lid.transform.SetPositionAndRotation(pos + Vector3.up * (h + 0.025f), rot);
        if (lidPart != null)
        {
            Bounds lb = BoundsOf(lidPart.gameObject);
            lidPart.SetParent(lid.transform, true);
            lidPart.position += lid.transform.position - new Vector3(lb.center.x, lb.center.y, lb.center.z);
        }
        lid.AddComponent<BoxCollider>().size = new Vector3(s + 0.02f, 0.045f, s + 0.02f);
        MakeNetworkedLoose(lid, Body(lid, 0.6f));
    }

    static void TrashCan(int k, Vector3 centre, float angle, float radius)
    {
        const float r = 0.185f, h = 0.52f;
        Vector3 pos = Polar(centre, angle, radius);
        GameObject root = Spawn(Models + "TrashCan.glb", $"{Prefix}TrashCan_{k}", pos, Quaternion.Euler(0f, k * 40f, 0f));
        Ring(root.transform, r, 0f, h, 0.025f);
        Box(root.transform, "Bottom", new Vector3(0f, 0.01f, 0f), new Vector3(0.3f, 0.02f, 0.3f));
        Spot(root.transform, pos + Vector3.up * 0.07f, false);

        Transform lidPart = Find(root, "Lid");
        GameObject lid = new GameObject($"{Prefix}TrashLid_{k}");
        lid.transform.SetPositionAndRotation(pos + Vector3.up * (h + 0.03f), root.transform.rotation);
        if (lidPart != null)
        {
            Bounds lb = BoundsOf(lidPart.gameObject);
            lidPart.SetParent(lid.transform, true);
            lidPart.position += new Vector3(lid.transform.position.x - lb.center.x, lid.transform.position.y - 0.025f - lb.min.y, lid.transform.position.z - lb.center.z);
        }
        BoxCollider c = lid.AddComponent<BoxCollider>();
        c.size = new Vector3(0.44f, 0.05f, 0.44f);
        MakeNetworkedLoose(lid, Body(lid, 0.4f));
    }

    static void Cabinet(int k, Vector3 centre, float angle, float radius)
    {
        const float w = 0.62f, d = 0.46f, h = 0.92f, t = 0.03f;
        Vector3 pos = Polar(centre, angle, radius);
        GameObject root = Spawn(Models + "DrawerCabinet.glb", $"{Prefix}Cabinet_{k}", pos, Quaternion.identity);
        Transform d0 = Find(root, "Drawer_0");
        if (d0 == null) throw new Exception("DrawerCabinet model has no Drawer_0");
        // the drawer fronts must face the middle of the room
        Vector3 modelFront = d0.position - root.transform.position;
        modelFront.y = 0f;
        Vector3 toCentre = centre - pos;
        toCentre.y = 0f;
        root.transform.rotation = Quaternion.FromToRotation(modelFront.normalized, toCentre.normalized);

        Vector3 f = root.transform.InverseTransformDirection(toCentre.normalized);   // front, in the cabinet's axes
        Vector3 sAxis = Vector3.Cross(Vector3.up, f);
        Box(root.transform, "Top", Vector3.up * (h - 0.02f), Along(sAxis, w + 0.04f, 0.04f, f, d + 0.03f));
        Box(root.transform, "Base", Vector3.up * 0.035f, Along(sAxis, w, 0.07f, f, d));
        Box(root.transform, "SideL", sAxis * (-w * 0.5f + t * 0.5f) + Vector3.up * (h * 0.5f), Along(sAxis, t, h - 0.1f, f, d));
        Box(root.transform, "SideR", sAxis * (w * 0.5f - t * 0.5f) + Vector3.up * (h * 0.5f), Along(sAxis, t, h - 0.1f, f, d));
        Box(root.transform, "Back", -f * (d * 0.5f - t * 0.5f) + Vector3.up * (h * 0.5f), Along(sAxis, w, h - 0.1f, f, t));

        float dh = (h - 0.1f - 0.02f) / 3f;
        for (int i = 0; i < 3; i++)
        {
            Transform drawer = Find(root, "Drawer_" + i);
            if (drawer != null) MakeDrawer(drawer, root.transform, f, w - 2f * t - 0.01f, d - 0.06f, dh - 0.02f, 0.3f);
        }
    }

    static void Table(int k, Vector3 centre, float angle, float radius)
    {
        const float w = 1.2f, d = 0.7f, h = 0.76f;
        Vector3 pos = Polar(centre, angle, radius);
        GameObject root = Spawn(Models + "Table.glb", $"{Prefix}Table_{k}", pos, Quaternion.identity);
        Transform d0 = Find(root, "Drawer_0");
        if (d0 == null) throw new Exception("Table model has no Drawer_0");
        Vector3 modelFront = d0.position - root.transform.position;
        modelFront.y = 0f;
        Vector3 toCentre = centre - pos;
        toCentre.y = 0f;
        root.transform.rotation = Quaternion.FromToRotation(modelFront.normalized, toCentre.normalized);

        Vector3 f = root.transform.InverseTransformDirection(toCentre.normalized);
        Vector3 sAxis = Vector3.Cross(Vector3.up, f);
        Box(root.transform, "Top", Vector3.up * (h - 0.03f), Along(sAxis, w, 0.06f, f, d));
        for (int x = -1; x <= 1; x += 2)
            for (int z = -1; z <= 1; z += 2)
                Box(root.transform, "Leg", sAxis * (x * (w * 0.5f - 0.08f)) + f * (z * (d * 0.5f - 0.08f)) + Vector3.up * ((h - 0.06f) * 0.5f), new Vector3(0.07f, h - 0.06f, 0.07f));
        Box(root.transform, "DrawerShelf", Vector3.up * (h - 0.19f), Along(sAxis, w - 0.6f, 0.012f, f, d - 0.2f));
        MakeDrawer(d0, root.transform, f, w - 0.6f, d - 0.25f, 0.1f, 0.3f);
    }

    /// <summary>A box size given as lengths along the side axis, up, and the front axis (which are X or Z of the parent).</summary>
    static Vector3 Along(Vector3 sideAxis, float side, float up, Vector3 frontAxis, float front)
    {
        Vector3 v = sideAxis * side + Vector3.up * up + frontAxis * front;
        return new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
    }

    /// <summary>Turns a drawer part of a model into a drawer you pull: it slides along 'front' (given in the parent's axes).</summary>
    static void MakeDrawer(Transform drawer, Transform parent, Vector3 front, float w, float d, float h, float maxSlide)
    {
        if (drawer.parent != parent) drawer.SetParent(parent, true);
        Vector3 side = Vector3.Cross(Vector3.up, front);
        const float t = 0.015f;
        // the drawer's own axes are the parent's axes (no rotation), and its origin is the middle of its front
        Quaternion toLocal = Quaternion.Inverse(drawer.localRotation);
        Vector3 fl = toLocal * front, sl = toLocal * side;
        Transform dt = drawer;
        BoxCollider b;
        b = Box(dt, "ColFront", Vector3.zero, Along(sl, w, h, fl, 0.03f));
        b = Box(dt, "ColBottom", -fl * (d * 0.5f) + Vector3.up * (-h * 0.5f + 0.03f), Along(sl, w - 0.04f, t, fl, d));
        b = Box(dt, "ColBack", -fl * d + Vector3.up * -0.01f, Along(sl, w - 0.04f, h - 0.05f, fl, t));
        b = Box(dt, "ColL", sl * (-w * 0.5f + 0.03f) - fl * (d * 0.5f) + Vector3.up * -0.01f, Along(sl, t, h - 0.05f, fl, d));
        b = Box(dt, "ColR", sl * (w * 0.5f - 0.03f) - fl * (d * 0.5f) + Vector3.up * -0.01f, Along(sl, t, h - 0.05f, fl, d));

        Rigidbody rb = Body(drawer.gameObject, 1f, true);
        Grabbable g = MakeGrabbable(drawer.gameObject, rb, false);
        OneGrabTranslateTransformer slide = drawer.gameObject.AddComponent<OneGrabTranslateTransformer>();
        var c = new OneGrabTranslateTransformer.OneGrabTranslateConstraints
        {
            ConstraintsAreRelative = true,
            MinX = Limit(Mathf.Min(0f, front.x * maxSlide)), MaxX = Limit(Mathf.Max(0f, front.x * maxSlide)),
            MinY = Limit(0f), MaxY = Limit(0f),
            MinZ = Limit(Mathf.Min(0f, front.z * maxSlide)), MaxZ = Limit(Mathf.Max(0f, front.z * maxSlide))
        };
        slide.InjectOptionalConstraints(c);
        EditorUtility.SetDirty(slide);
        SerializedObject so = new SerializedObject(g);
        so.FindProperty("_oneGrabTransformer").objectReferenceValue = slide;
        so.ApplyModifiedPropertiesWithoutUndo();

        SlideDrawer sd = drawer.gameObject.AddComponent<SlideDrawer>();
        sd.grabbable = g;
        sd.closedLocalPosition = drawer.localPosition;
        sd.slideAxis = front;
        sd.maxSlide = maxSlide;
        drawers.Add(sd);

        // an item hidden in the drawer lies on its bottom, in the middle
        Spot(drawer, drawer.TransformPoint(-fl * (d * 0.5f) + Vector3.up * (-h * 0.5f + 0.09f)), true);
    }

    static FloatConstraint Limit(float v) => new FloatConstraint { Constrain = true, Value = v };

    // ================================================================== network ids + pictures + APK

    static void RefreshNetworkObjectHashes()
    {
        MethodInfo validate = typeof(NetworkObject).GetMethod("OnValidate", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        var seen = new HashSet<long>();
        foreach (NetworkObject no in Object.FindObjectsByType<NetworkObject>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!no.name.StartsWith(Prefix)) continue;
            validate?.Invoke(no, null);
            EditorUtility.SetDirty(no);
            long hash = new SerializedObject(no).FindProperty("GlobalObjectIdHash").longValue;
            if (hash == 0 || !seen.Add(hash)) Debug.LogError($"[Grinder] NetworkObject {no.name} has a bad id {hash}");
        }
        Debug.Log($"[Grinder] {seen.Count} in-scene network objects have ids");
    }

    [MenuItem("Tools/Grinder/Capture Room Screenshots")]
    public static void Screenshots()
    {
        GameObject room = GameObject.Find(Prefix + "Room");
        if (room == null) return;
        Vector3 c = room.transform.position;
        string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Screenshots");
        Directory.CreateDirectory(dir);
        Shot(dir, "hide_room_a.png", c + new Vector3(0f, 1.6f, -4.8f), c + new Vector3(0f, 1.2f, 2f), 75f);
        Shot(dir, "hide_room_b.png", c + new Vector3(0f, 1.6f, 4.8f), c + new Vector3(0f, 1.2f, -2f), 75f);
        Shot(dir, "hide_room_c.png", c + new Vector3(-3.5f, 1.6f, 0f), c + new Vector3(5f, 1.0f, 0f), 75f);
        Shot(dir, "hide_room_d.png", c + new Vector3(3.5f, 1.6f, 0f), c + new Vector3(-5f, 1.0f, 0f), 75f);
        Shot(dir, "hide_room_top.png", c + new Vector3(0.01f, 4.0f, 0f), c, 110f);
        GameObject cab = GameObject.Find(Prefix + "Cabinet_0");
        if (cab != null)
        {
            Vector3 p = cab.transform.position, to = (c - p).normalized;
            Shot(dir, "hide_cabinet.png", p + to * 1.8f + Vector3.up * 1.3f, p + Vector3.up * 0.5f, 60f);
        }
        GameObject gr = GameObject.Find(Prefix + "Grinder_Blue");
        if (gr != null)
        {
            Vector3 p = gr.transform.position, to = (c - p).normalized;
            Shot(dir, "hide_grinder.png", p + to * 2.2f + Vector3.up * 1.5f + Vector3.Cross(Vector3.up, to) * 0.8f, p + Vector3.up * 0.6f, 60f);
        }
        Debug.Log("[Grinder] Screenshots saved to " + dir);
    }

    static void Shot(string dir, string file, Vector3 pos, Vector3 lookAt, float fov)
    {
        GameObject camGo = new GameObject("~ShotCam") { hideFlags = HideFlags.HideAndDontSave };
        Camera cam = camGo.AddComponent<Camera>();
        cam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(lookAt - pos));
        cam.fieldOfView = fov;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 100f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.15f, 0.15f, 0.18f);
        RenderTexture rt = new RenderTexture(1600, 900, 24);
        cam.targetTexture = rt;
        cam.Render();
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

    /// <summary>For -executeMethod: builds an APK of the scenes in Build Settings to Builds/GrinderGame.apk.</summary>
    public static void BatchBuildApk()
    {
        try { Build(); Screenshots(); }
        catch (Exception e) { Debug.LogError("[Grinder] BUILD FAILED: " + e); EditorApplication.Exit(1); return; }
        string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Builds");
        Directory.CreateDirectory(dir);
        var scenes = new List<string>();
        foreach (EditorBuildSettingsScene s in EditorBuildSettings.scenes) if (s.enabled) scenes.Add(s.path);
        var options = new BuildPlayerOptions
        {
            scenes = scenes.ToArray(),
            locationPathName = Path.Combine(dir, "GrinderGame.apk"),
            target = BuildTarget.Android,
            options = BuildOptions.None
        };
        var report = BuildPipeline.BuildPlayer(options);
        bool ok = report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded;
        Debug.Log($"[Grinder] APK {report.summary.result}: {options.locationPathName} ({report.summary.totalSize / (1024 * 1024)} MB), errors {report.summary.totalErrors}");
        EditorApplication.Exit(ok ? 0 : 1);
    }
}
