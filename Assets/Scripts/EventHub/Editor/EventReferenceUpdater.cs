#if UNITY_EDITOR
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class EventReferenceUpdater
{
    /// <summary>
    /// Eski yol ile eşleşen tüm [EventPublisher] ve [EventListener] alanlarını yeni yolla günceller.
    /// </summary>
    public static int UpdateAllReferences(string oldPath, string newPath)
    {
        if (string.IsNullOrEmpty(oldPath) || string.IsNullOrEmpty(newPath) || oldPath == newPath)
            return 0;

        int updatedCount = 0;

        // 1. Açık Sahnelerdeki MonoBehaviour'ları Güncelle
        for (int s = 0; s < SceneManager.sceneCount; s++)
        {
            Scene scene = SceneManager.GetSceneAt(s);
            if (!scene.isLoaded) continue;

            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var mb in root.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (mb == null) continue;
                    if (UpdateSerializedObjectChannels(mb, oldPath, newPath))
                    {
                        EditorUtility.SetDirty(mb);
                        updatedCount++;
                    }
                }
            }
        }

        // 2. Prefab Varlıklarını Güncelle
        string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab");
        for (int i = 0; i < prefabGuids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(prefabGuids[i]);
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (go == null) continue;

            bool prefabDirty = false;
            foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb != null && UpdateSerializedObjectChannels(mb, oldPath, newPath))
                {
                    prefabDirty = true;
                    updatedCount++;
                }
            }

            if (prefabDirty)
            {
                EditorUtility.SetDirty(go);
                PrefabUtility.RecordPrefabInstancePropertyModifications(go);
            }
        }

        // 3. ScriptableObject Varlıklarını Güncelle
        foreach (var type in TypeCache.GetTypesDerivedFrom<ScriptableObject>())
        {
            if (type.IsAbstract) continue;

            string[] soGuids = AssetDatabase.FindAssets($"t:{type.Name}");
            for (int i = 0; i < soGuids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(soGuids[i]);
                var so = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
                if (so != null && UpdateSerializedObjectChannels(so, oldPath, newPath))
                {
                    EditorUtility.SetDirty(so);
                    updatedCount++;
                }
            }
        }

        if (updatedCount > 0)
        {
            AssetDatabase.SaveAssets();
            Debug.Log($"<color=#66FF66><b>[EventHub]</b></color> '{oldPath}' -> '{newPath}' olarak güncellendi. Toplam <b>{updatedCount}</b> referans yenilendi.");
        }

        return updatedCount;
    }

    private static bool UpdateSerializedObjectChannels(UnityEngine.Object target, string oldPath, string newPath)
    {
        var so = new SerializedObject(target);
        var type = target.GetType();
        bool anyChanged = false;

        var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        for (int i = 0; i < fields.Length; i++)
        {
            var f = fields[i];
            if (f.FieldType != typeof(string)) continue;

            bool isChannel = f.GetCustomAttribute<EventPublisherAttribute>() != null ||
                             f.GetCustomAttribute<EventListenerAttribute>() != null;

            if (!isChannel) continue;

            var prop = so.FindProperty(f.Name);
            if (prop != null && prop.propertyType == SerializedPropertyType.String)
            {
                string currentVal = prop.stringValue;
                // Birebir eşleşme veya klasör yolunun altındaysa
                if (currentVal == oldPath)
                {
                    prop.stringValue = newPath;
                    anyChanged = true;
                }
                else if (currentVal.StartsWith(oldPath + "/"))
                {
                    prop.stringValue = newPath + currentVal.Substring(oldPath.Length);
                    anyChanged = true;
                }
            }
        }

        if (anyChanged)
        {
            so.ApplyModifiedProperties();
        }

        return anyChanged;
    }
}
#endif