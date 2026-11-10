using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Writes the hierarchy of the grinder scene and the item prefabs to a text file, so the layout can be read outside Unity.</summary>
public static class GrinderSceneDump
{
    [MenuItem("Tools/Grinder/Dump Scene Hierarchy")]
    public static void Dump()
    {
        var sb = new StringBuilder();
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);
        foreach (GameObject root in scene.GetRootGameObjects()) Write(sb, root.transform, 0, 3);
        foreach (string p in new[] { "Wood", "Rope", "Stone", "Slingshot", "Player" })
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Prefabs/{p}.prefab");
            sb.AppendLine($"\n=== PREFAB {p}");
            if (prefab != null) Write(sb, prefab.transform, 0, 4);
        }
        sb.AppendLine("\n=== TAGS " + string.Join(", ", UnityEditorInternal.InternalEditorUtility.tags));
        sb.Append("=== LAYERS ");
        for (int i = 0; i < 32; i++) if (!string.IsNullOrEmpty(LayerMask.LayerToName(i))) sb.Append($"{i}:{LayerMask.LayerToName(i)} ");
        string file = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Screenshots", "scene_dump.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(file));
        File.WriteAllText(file, sb.ToString());
        Debug.Log("[Grinder] dump written to " + file);
    }

    static void Write(StringBuilder sb, Transform t, int depth, int maxDepth)
    {
        sb.Append(new string(' ', depth * 2)).Append(t.gameObject.activeSelf ? "" : "(off) ").Append(t.name);
        sb.Append($"  pos={t.position:F2} rot={t.eulerAngles:F0} scale={t.lossyScale:F2} tag={t.tag} layer={t.gameObject.layer} [");
        foreach (Component c in t.GetComponents<Component>())
            if (c != null && !(c is Transform)) sb.Append(c.GetType().Name).Append(' ');
        sb.AppendLine("]");
        if (depth >= maxDepth) { if (t.childCount > 0) sb.Append(new string(' ', depth * 2 + 2)).AppendLine($"... {t.childCount} children"); return; }
        foreach (Transform c in t) Write(sb, c, depth + 1, maxDepth);
    }
}
