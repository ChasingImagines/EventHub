#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// 1. ARKA PLAN SERVİSİ: Saf C# sınıfı (ScriptableObject değildir, Domain Reload hatalarını engeller)
[InitializeOnLoad]
public static class QuickAssetShelfService
{
    private const string PREF_KEY_LISTEN = "QuickAssetShelf_AutoRecord";
    private const string PREF_KEY_ITEMS = "QuickAssetShelf_RecentGuids";
    private const int MAX_HISTORY = 50;

    public static readonly List<string> RecentGuids = new();
    public static GameObject ActiveSpawnPrefab;

    public static bool AutoRecord
    {
        get => EditorPrefs.GetBool(PREF_KEY_LISTEN, true);
        set => EditorPrefs.SetBool(PREF_KEY_LISTEN, value);
    }

    static QuickAssetShelfService()
    {
        LoadGuids();
        Selection.selectionChanged -= OnSelectionChanged;
        Selection.selectionChanged += OnSelectionChanged;

        SceneView.duringSceneGui -= OnSceneGUI;
        SceneView.duringSceneGui += OnSceneGUI;
    }

    public static void LoadGuids()
    {
        RecentGuids.Clear();
        string raw = EditorPrefs.GetString(PREF_KEY_ITEMS, "");
        if (!string.IsNullOrEmpty(raw))
        {
            string[] items = raw.Split(';', StringSplitOptions.RemoveEmptyEntries);
            RecentGuids.AddRange(items);
        }
    }

    public static void SaveGuids()
    {
        EditorPrefs.SetString(PREF_KEY_ITEMS, string.Join(";", RecentGuids));
    }

    public static void ClearAll()
    {
        RecentGuids.Clear();
        ActiveSpawnPrefab = null;
        SaveGuids();
        SceneView.RepaintAll();
        QuickAssetShelf.RepaintWindow();
    }

    private static void OnSelectionChanged()
    {
        if (!AutoRecord) return;

        var selected = Selection.activeObject;
        if (selected == null || !EditorUtility.IsPersistent(selected)) return;

        bool isPrefab = selected is GameObject go && PrefabUtility.GetPrefabAssetType(go) != PrefabAssetType.NotAPrefab;
        bool isSO = selected is ScriptableObject;

        if (!isPrefab && !isSO) return;

        string path = AssetDatabase.GetAssetPath(selected);
        string guid = AssetDatabase.AssetPathToGUID(path);
        if (string.IsNullOrEmpty(guid)) return;

        RecentGuids.Remove(guid);
        RecentGuids.Insert(0, guid);

        if (RecentGuids.Count > MAX_HISTORY)
            RecentGuids.RemoveAt(RecentGuids.Count - 1);

        SaveGuids();
        QuickAssetShelf.RepaintWindow();
    }

    private static void OnSceneGUI(SceneView sceneView)
    {
        if (ActiveSpawnPrefab == null) return;

        Event currentEvent = Event.current;
        Ray ray = HandleUtility.GUIPointToWorldRay(currentEvent.mousePosition);
        Vector3 targetPoint;
        Vector3 surfaceNormal = Vector3.up;

        if (Physics.Raycast(ray, out RaycastHit hit))
        {
            targetPoint = hit.point;
            surfaceNormal = hit.normal;
        }
        else
        {
            Plane groundPlane = new Plane(Vector3.up, Vector3.zero);
            if (groundPlane.Raycast(ray, out float enter))
                targetPoint = ray.GetPoint(enter);
            else
                targetPoint = ray.GetPoint(15f);
        }

        Handles.color = new Color(0.2f, 0.9f, 1f, 0.8f);
        Handles.DrawWireDisc(targetPoint, surfaceNormal, 0.6f);
        Handles.DrawDottedLine(targetPoint, targetPoint + surfaceNormal * 0.8f, 2f);

        var textStyle = new GUIStyle { normal = { textColor = Color.cyan }, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        Handles.Label(targetPoint + surfaceNormal * 0.9f, $"[B / Shift+Tık]: {ActiveSpawnPrefab.name}", textStyle);

        bool isShiftClick = (currentEvent.type == EventType.MouseDown && currentEvent.button == 0 && currentEvent.shift);
        bool isBKeyPressed = (currentEvent.type == EventType.KeyDown && currentEvent.keyCode == KeyCode.B);

        if (isShiftClick || isBKeyPressed)
        {
            SpawnPrefab(ActiveSpawnPrefab, targetPoint);
            currentEvent.Use();
        }

        if (currentEvent.type == EventType.MouseMove)
        {
            sceneView.Repaint();
        }
    }

    private static void SpawnPrefab(GameObject prefab, Vector3 position)
    {
        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        instance.transform.position = position;

        Undo.RegisterCreatedObjectUndo(instance, $"Spawn {prefab.name}");
        Selection.activeGameObject = instance;
    }
}

// 2. EDİTÖR PENCERESİ: Yalnızca görselleştirme ve liste yönetimi yapar
public class QuickAssetShelf : EditorWindow
{
    private Vector2 _scrollPos;
    private int _filterIndex = 0; // 0: Tümü, 1: Prefab, 2: SO
    private readonly string[] _filterOptions = { "Tümü", "Prefab", "SO" };

    [MenuItem("Tools/Quick Asset Shelf")]
    public static void OpenWindow()
    {
        GetWindow<QuickAssetShelf>("Asset Shelf", typeof(SceneView));
    }

    public static void RepaintWindow()
    {
        if (HasOpenInstances<QuickAssetShelf>())
        {
            GetWindow<QuickAssetShelf>().Repaint();
        }
    }

    private void OnGUI()
    {
        DrawToolbar();
        DrawActiveStampBanner();
        DrawItemList();
    }

    private void DrawToolbar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

        bool listening = QuickAssetShelfService.AutoRecord;
        GUI.color = listening ? new Color(0.4f, 1f, 0.4f) : new Color(1f, 0.4f, 0.4f);
        if (GUILayout.Button(listening ? "● Dinliyor" : "○ Duraklatıldı", EditorStyles.toolbarButton, GUILayout.Width(80)))
        {
            QuickAssetShelfService.AutoRecord = !listening;
        }
        GUI.color = Color.white;

        _filterIndex = EditorGUILayout.Popup(_filterIndex, _filterOptions, EditorStyles.toolbarPopup, GUILayout.Width(65));

        GUILayout.FlexibleSpace();

        if (GUILayout.Button("🗑 Temizle", EditorStyles.toolbarButton, GUILayout.Width(65)))
        {
            QuickAssetShelfService.ClearAll();
        }

        EditorGUILayout.EndHorizontal();
    }

    private void DrawActiveStampBanner()
    {
        if (QuickAssetShelfService.ActiveSpawnPrefab == null) return;

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        GUI.color = new Color(0.4f, 0.9f, 1f);
        EditorGUILayout.LabelField($"🎯 Aktif Damga: <b>{QuickAssetShelfService.ActiveSpawnPrefab.name}</b>", new GUIStyle(EditorStyles.boldLabel) { richText = true });
        GUI.color = Color.white;
        EditorGUILayout.LabelField("Scene View'da <b>Shift + Sol Tık</b> veya <b>'B' Tuşu</b> ile spawn et.", new GUIStyle(EditorStyles.miniLabel) { richText = true });

        if (GUILayout.Button("Damga Modunu Kapat", EditorStyles.miniButton))
        {
            QuickAssetShelfService.ActiveSpawnPrefab = null;
            SceneView.RepaintAll();
        }
        EditorGUILayout.EndVertical();
    }

    private void DrawItemList()
    {
        _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

        var guids = QuickAssetShelfService.RecentGuids;

        if (guids.Count == 0)
        {
            EditorGUILayout.Space(15);
            EditorGUILayout.HelpBox("Arka plan dinlemede. Project panelinden Prefab veya ScriptableObject seçtiğinizde buraya kalıcı olarak kaydedilecektir.", MessageType.Info);
            EditorGUILayout.EndScrollView();
            return;
        }

        bool hasPruned = false;

        for (int i = 0; i < guids.Count; i++)
        {
            string guid = guids[i];
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var item = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);

            if (item == null)
            {
                guids.RemoveAt(i);
                i--;
                hasPruned = true;
                continue;
            }

            bool isPrefab = item is GameObject;
            bool isSO = item is ScriptableObject;

            if (_filterIndex == 1 && !isPrefab) continue;
            if (_filterIndex == 2 && !isSO) continue;

            DrawItemRow(item, guid, isPrefab);
        }

        if (hasPruned)
        {
            QuickAssetShelfService.SaveGuids();
        }

        EditorGUILayout.EndScrollView();
    }

    private void DrawItemRow(UnityEngine.Object item, string guid, bool isPrefab)
    {
        bool isCurrentStamp = (QuickAssetShelfService.ActiveSpawnPrefab == item);

        if (isCurrentStamp) GUI.backgroundColor = new Color(0.4f, 0.8f, 1f);
        Rect rowRect = EditorGUILayout.BeginHorizontal(EditorStyles.helpBox, GUILayout.Height(28));
        GUI.backgroundColor = Color.white;

        Texture icon = AssetPreview.GetMiniThumbnail(item);
        GUILayout.Label(new GUIContent(icon), GUILayout.Width(22), GUILayout.Height(22));

        string badge = isPrefab ? "<color=#88CCFF>[Prefab]</color>" : "<color=#FFD700>[SO]</color>";
        var labelStyle = new GUIStyle(EditorStyles.boldLabel) { richText = true, alignment = TextAnchor.MiddleLeft };

        if (GUILayout.Button($"<b>{item.name}</b> {badge}", labelStyle, GUILayout.Height(22)))
        {
            EditorGUIUtility.PingObject(item);
            Selection.activeObject = item;
        }

        if (isPrefab)
        {
            GUI.color = isCurrentStamp ? Color.cyan : Color.white;
            string btnText = isCurrentStamp ? "🎯 Hazır" : "Damgala";
            if (GUILayout.Button(btnText, EditorStyles.miniButton, GUILayout.Width(60), GUILayout.Height(20)))
            {
                QuickAssetShelfService.ActiveSpawnPrefab = isCurrentStamp ? null : (GameObject)item;
                SceneView.RepaintAll();
            }
            GUI.color = Color.white;
        }

        if (GUILayout.Button("✕", EditorStyles.miniButton, GUILayout.Width(20), GUILayout.Height(20)))
        {
            if (QuickAssetShelfService.ActiveSpawnPrefab == item) QuickAssetShelfService.ActiveSpawnPrefab = null;
            QuickAssetShelfService.RecentGuids.Remove(guid);
            QuickAssetShelfService.SaveGuids();
            GUIUtility.ExitGUI();
        }

        EditorGUILayout.EndHorizontal();

        HandleDragDrop(rowRect, item);
    }

    private void HandleDragDrop(Rect rect, UnityEngine.Object target)
    {
        Event evt = Event.current;
        if (evt.type == EventType.MouseDrag && rect.Contains(evt.mousePosition))
        {
            DragAndDrop.PrepareStartDrag();
            DragAndDrop.objectReferences = new[] { target };
            DragAndDrop.StartDrag(target.name);
            evt.Use();
        }
    }
}
#endif