using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Level-design helpers that Claude can drive.
// Tools > Level > Place Table + Fan   : places the props (re-runnable, replaces the old set)
// Tools > Level > Capture Screenshots : saves PNGs to <project>/Screenshots so Claude can "see" the scene
public static class LevelTools
{
    const string TablePath = "Assets/Models/Props/WoodTable.glb";
    const string FanPath = "Assets/Models/Props/TableFan.glb";
    const string SetName = "Props_TableFan";

    const float TableHeight = 0.8f;  // meters, real-world desk height
    const float FanHeight = 0.4f;

    [MenuItem("Tools/Level/Place Table + Fan")]
    public static void PlaceTableAndFan()
    {
        GameObject tableAsset = AssetDatabase.LoadAssetAtPath<GameObject>(TablePath);
        GameObject fanAsset = AssetDatabase.LoadAssetAtPath<GameObject>(FanPath);
        if (tableAsset == null || fanAsset == null)
        {
            Debug.LogError("[LevelTools] Could not load table/fan GLBs. Are they imported?");
            return;
        }

        GameObject old = GameObject.Find(SetName);
        if (old != null) Undo.DestroyObjectImmediate(old);

        Vector3 spot = PickSpot(out float yaw);

        GameObject set = new GameObject(SetName);
        Undo.RegisterCreatedObjectUndo(set, "Place Table + Fan");
        set.transform.SetPositionAndRotation(spot, Quaternion.Euler(0f, yaw, 0f));

        GameObject table = SpawnScaled(tableAsset, "Table", set.transform, TableHeight);
        table.transform.localPosition = Vector3.zero;
        SitOnY(table, spot.y);
        Bounds tb = WorldBounds(table);

        GameObject fan = SpawnScaled(fanAsset, "TableFan", set.transform, FanHeight);
        fan.transform.position = new Vector3(tb.center.x, 0f, tb.center.z);
        fan.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        SitOnY(fan, tb.max.y);

        AddBoxCollider(table);
        AddBoxCollider(fan);

        Selection.activeGameObject = set;
        EditorSceneManager.MarkSceneDirty(set.scene);
        Debug.Log($"[LevelTools] Placed table+fan at {spot}, yaw {yaw}. Table size {tb.size}");

        CaptureScreenshots();
    }

    // Default spot: halfway between the two grinders (center of the 1v1 arena), dropped onto the floor.
    static Vector3 PickSpot(out float yaw)
    {
        yaw = 0f;
        GameObject a = GameObject.Find("Grinder");
        GameObject b = GameObject.Find("Grinder (1)");
        Vector3 p = Vector3.zero;
        if (a != null && b != null)
        {
            p = (a.transform.position + b.transform.position) * 0.5f;
            Vector3 dir = b.transform.position - a.transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.01f) yaw = Quaternion.LookRotation(dir).eulerAngles.y;
        }
        else if (SceneView.lastActiveSceneView != null)
        {
            p = SceneView.lastActiveSceneView.pivot;
        }

        if (Physics.Raycast(p + Vector3.up * 3f, Vector3.down, out RaycastHit hit, 20f, ~0, QueryTriggerInteraction.Ignore))
            p.y = hit.point.y;
        return p;
    }

    static GameObject SpawnScaled(GameObject asset, string name, Transform parent, float targetHeight)
    {
        GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(asset, parent);
        go.name = name;
        go.transform.localScale = Vector3.one;
        float h = WorldBounds(go).size.y;
        if (h > 0.0001f) go.transform.localScale = Vector3.one * (targetHeight / h);
        go.isStatic = false;
        return go;
    }

    static void SitOnY(GameObject go, float y)
    {
        Bounds b = WorldBounds(go);
        go.transform.position += Vector3.up * (y - b.min.y);
    }

    static Bounds WorldBounds(GameObject go)
    {
        Renderer[] rs = go.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
        Bounds b = rs[0].bounds;
        foreach (Renderer r in rs) b.Encapsulate(r.bounds);
        return b;
    }

    static void AddBoxCollider(GameObject go)
    {
        Bounds wb = WorldBounds(go);
        BoxCollider c = Undo.AddComponent<BoxCollider>(go);
        Vector3 s = go.transform.lossyScale;
        c.center = go.transform.InverseTransformPoint(wb.center);
        c.size = new Vector3(wb.size.x / s.x, wb.size.y / s.y, wb.size.z / s.z);
    }

    // ---------------- Screenshots ----------------

    [MenuItem("Tools/Level/Capture Screenshots")]
    public static void CaptureScreenshots()
    {
        string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Screenshots");
        Directory.CreateDirectory(dir);

        // 1) whatever the Scene view is looking at
        SceneView sv = SceneView.lastActiveSceneView;
        if (sv != null && sv.camera != null)
            Render(sv.camera.transform.position, sv.camera.transform.rotation, 60f, false, 0f, Path.Combine(dir, "sceneview.png"));

        // 2) close-up of the selection (or the table set)
        GameObject focus = Selection.activeGameObject != null ? Selection.activeGameObject : GameObject.Find(SetName);
        if (focus != null)
        {
            Bounds b = WorldBounds(focus);
            float r = Mathf.Max(b.extents.magnitude, 0.5f);
            Vector3 eye = b.center + (focus.transform.forward * -1f + Vector3.up * 0.6f + focus.transform.right * 0.5f).normalized * r * 3f;
            Render(eye, Quaternion.LookRotation(b.center - eye), 50f, false, 0f, Path.Combine(dir, "closeup.png"));
        }

        // 3) top-down map of the whole level
        Bounds level = new Bounds();
        bool any = false;
        foreach (Renderer rr in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
        {
            if (rr.bounds.size.magnitude > 200f) continue; // skip skyboxes/huge planes
            if (!any) { level = rr.bounds; any = true; } else level.Encapsulate(rr.bounds);
        }
        if (any)
        {
            Vector3 eye = level.center + Vector3.up * (level.extents.y + 1.5f);
            float ortho = Mathf.Max(level.extents.x, level.extents.z) * 1.05f;
            // just below the ceiling would be better, but ceilings block the view; cull by near plane instead
            Render(eye, Quaternion.Euler(90f, 0f, 0f), 0f, true, ortho, Path.Combine(dir, "topdown.png"), level.extents.y * 0.6f);
        }

        AssetDatabase.Refresh();
        Debug.Log($"[LevelTools] Screenshots saved to {dir}");
    }

    static void Render(Vector3 pos, Quaternion rot, float fov, bool ortho, float orthoSize, string file, float nearClip = 0.05f)
    {
        GameObject camGo = new GameObject("~LevelToolsCam") { hideFlags = HideFlags.HideAndDontSave };
        Camera cam = camGo.AddComponent<Camera>();
        cam.transform.SetPositionAndRotation(pos, rot);
        cam.fieldOfView = fov > 0 ? fov : 60f;
        cam.orthographic = ortho;
        cam.orthographicSize = orthoSize;
        cam.nearClipPlane = Mathf.Max(0.01f, nearClip);
        cam.farClipPlane = 500f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.15f, 0.15f, 0.18f);

        RenderTexture rt = new RenderTexture(1280, 720, 24);
        cam.targetTexture = rt;
        cam.Render();

        RenderTexture prev = RenderTexture.active;
        RenderTexture.active = rt;
        Texture2D tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;

        File.WriteAllBytes(file, tex.EncodeToPNG());

        cam.targetTexture = null;
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(tex);
        Object.DestroyImmediate(camGo);
    }
}
