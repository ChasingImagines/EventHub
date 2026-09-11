#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Quick Asset Shelf — Prefab ve ScriptableObject'ler için kalıcı bir "hızlı erişim rafı".
/// Seçilen asset'ler otomatik kaydedilir, sabitlenebilir, aranabilir ve prefab'lar
/// Scene View'da "damga" olarak hızlıca yerleştirilebilir.
/// </summary>
[InitializeOnLoad]
public static class QuickAssetShelfService
{
    private const string PrefAutoRecord = "QuickAssetShelf_AutoRecord";
    private const string PrefRecents = "QuickAssetShelf_RecentGuids";
    private const string PrefPinned = "QuickAssetShelf_PinnedGuids";
    private const string PrefStamp = "QuickAssetShelf_StampGuid";
    private const int MaxHistory = 50;

    public static readonly List<string> RecentGuids = new();
    public static readonly List<string> PinnedGuids = new();

    private static GameObject _stampPrefab;

    /// <summary>Scene View'da yerleştirilecek aktif prefab (damga). GUID'i EditorPrefs'te saklanır.</summary>
    public static GameObject ActiveSpawnPrefab
    {
        get => _stampPrefab;
        set
        {
            _stampPrefab = value;
            string guid = value != null
                ? AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(value))
                : string.Empty;
            EditorPrefs.SetString(PrefStamp, guid);
            SceneView.RepaintAll();
        }
    }

    public static bool AutoRecord
    {
        get => EditorPrefs.GetBool(PrefAutoRecord, true);
        set => EditorPrefs.SetBool(PrefAutoRecord, value);
    }

    static QuickAssetShelfService()
    {
        Load();

        Selection.selectionChanged -= OnSelectionChanged;
        Selection.selectionChanged += OnSelectionChanged;

        SceneView.duringSceneGui -= OnSceneGUI;
        SceneView.duringSceneGui += OnSceneGUI;
    }

    // ---------- Kalıcılık ----------

    public static void Load()
    {
        RecentGuids.Clear();
        RecentGuids.AddRange(Split(EditorPrefs.GetString(PrefRecents, "")));

        PinnedGuids.Clear();
        PinnedGuids.AddRange(Split(EditorPrefs.GetString(PrefPinned, "")));

        string stampGuid = EditorPrefs.GetString(PrefStamp, "");
        _stampPrefab = string.IsNullOrEmpty(stampGuid)
            ? null
            : AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(stampGuid));
    }

    public static void SaveRecents() => EditorPrefs.SetString(PrefRecents, string.Join(";", RecentGuids));
    public static void SavePinned() => EditorPrefs.SetString(PrefPinned, string.Join(";", PinnedGuids));

    private static string[] Split(string raw)
        => string.IsNullOrEmpty(raw) ? Array.Empty<string>() : raw.Split(';', StringSplitOptions.RemoveEmptyEntries);

    // ---------- Sabitleme ----------

    public static bool IsPinned(string guid) => PinnedGuids.Contains(guid);

    public static void TogglePin(string guid)
    {
        if (!PinnedGuids.Remove(guid)) PinnedGuids.Insert(0, guid);
        SavePinned();
        QuickAssetShelf.RepaintWindow();
    }

    public static void Remove(string guid)
    {
        if (ActiveSpawnPrefab != null && AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(ActiveSpawnPrefab)) == guid)
            ActiveSpawnPrefab = null;

        RecentGuids.Remove(guid);
        PinnedGuids.Remove(guid);
        SaveRecents();
        SavePinned();
    }

    public static void ClearRecents()
    {
        RecentGuids.Clear();
        SaveRecents();
        QuickAssetShelf.RepaintWindow();
    }

    public static void ClearAll()
    {
        RecentGuids.Clear();
        PinnedGuids.Clear();
        ActiveSpawnPrefab = null;
        SaveRecents();
        SavePinned();
        QuickAssetShelf.RepaintWindow();
    }

    /// <summary>Silinmiş asset'lerin GUID'lerini iki listeden de temizler. Değişiklik olduysa true döner.</summary>
    public static bool PruneInvalid()
    {
        bool changed = PruneList(RecentGuids) | PruneList(PinnedGuids);
        if (changed)
        {
            SaveRecents();
            SavePinned();
        }
        return changed;
    }

    private static bool PruneList(List<string> guids)
    {
        int before = guids.Count;
        guids.RemoveAll(g => string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(g)));
        return guids.Count != before;
    }

    // ---------- Kayıt ----------

    private static void OnSelectionChanged()
    {
        if (!AutoRecord) return;

        var selected = Selection.activeObject;
        if (selected == null || !EditorUtility.IsPersistent(selected)) return;
        if (!IsShelfAsset(selected)) return;

        string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(selected));
        if (string.IsNullOrEmpty(guid)) return;

        RecentGuids.Remove(guid);
        RecentGuids.Insert(0, guid);

        if (RecentGuids.Count > MaxHistory)
            RecentGuids.RemoveAt(RecentGuids.Count - 1);

        SaveRecents();
        QuickAssetShelf.RepaintWindow();
    }

    public static bool IsShelfAsset(UnityEngine.Object obj)
    {
        if (obj is GameObject go)
            return PrefabUtility.GetPrefabAssetType(go) != PrefabAssetType.NotAPrefab;
        return obj is ScriptableObject;
    }

    // ---------- Damga (Scene View spawn) ----------

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
            var groundPlane = new Plane(Vector3.up, Vector3.zero);
            targetPoint = groundPlane.Raycast(ray, out float enter) ? ray.GetPoint(enter) : ray.GetPoint(15f);
        }

        Handles.color = new Color(0.2f, 0.9f, 1f, 0.8f);
        Handles.DrawWireDisc(targetPoint, surfaceNormal, 0.6f);
        Handles.DrawDottedLine(targetPoint, targetPoint + surfaceNormal * 0.8f, 2f);

        var textStyle = new GUIStyle
        {
            normal = { textColor = Color.cyan },
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
        };
        Handles.Label(targetPoint + surfaceNormal * 0.9f, $"[B / Shift+Tık]: {ActiveSpawnPrefab.name}", textStyle);

        bool isShiftClick = currentEvent.type == EventType.MouseDown && currentEvent.button == 0 && currentEvent.shift;
        bool isBKeyPressed = currentEvent.type == EventType.KeyDown && currentEvent.keyCode == KeyCode.B;

        if (isShiftClick || isBKeyPressed)
        {
            SpawnPrefab(ActiveSpawnPrefab, targetPoint);
            currentEvent.Use();
        }

        if (currentEvent.type == EventType.MouseMove)
            sceneView.Repaint();
    }

    private static void SpawnPrefab(GameObject prefab, Vector3 position)
    {
        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        instance.transform.position = position;

        Undo.RegisterCreatedObjectUndo(instance, $"Spawn {prefab.name}");
        Selection.activeGameObject = instance;
    }
}

/// <summary>
/// Quick Asset Shelf penceresi: arama, sabitlenenler + son kullanılanlar listesi, damga kontrolü.
/// </summary>
public class QuickAssetShelf : EditorWindow
{
    private const string PrefFilter = "QuickAssetShelf_Filter";
    private const string PrefSearch = "QuickAssetShelf_Search";

    private Vector2 _scrollPos;
    private int _filterIndex;
    private string _search = "";
    private readonly string[] _filterOptions = { "Tümü", "Prefab", "SO" };

    [MenuItem("Tools/Quick Asset Shelf")]
    public static void OpenWindow()
    {
        var window = GetWindow<QuickAssetShelf>("Asset Shelf", typeof(SceneView));
        window.minSize = new Vector2(240, 200);
    }

    public static void RepaintWindow()
    {
        if (HasOpenInstances<QuickAssetShelf>())
            GetWindow<QuickAssetShelf>().Repaint();
    }

    private void OnEnable()
    {
        _filterIndex = EditorPrefs.GetInt(PrefFilter, 0);
        _search = EditorPrefs.GetString(PrefSearch, "");
    }

    private void OnDisable()
    {
        EditorPrefs.SetInt(PrefFilter, _filterIndex);
        EditorPrefs.SetString(PrefSearch, _search);
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
            QuickAssetShelfService.AutoRecord = !listening;
        GUI.color = Color.white;

        _filterIndex = EditorGUILayout.Popup(_filterIndex, _filterOptions, EditorStyles.toolbarPopup, GUILayout.Width(62));

        EditorGUI.BeginChangeCheck();
        _search = GUILayout.TextField(_search, EditorStyles.toolbarSearchField);
        if (EditorGUI.EndChangeCheck()) Repaint();

        if (GUILayout.Button("", GUI.skin.FindStyle("ToolbarSearchCancelButton") ?? EditorStyles.toolbarButton)
            && !string.IsNullOrEmpty(_search))
        {
            _search = "";
            GUIUtility.keyboardControl = 0;
            Repaint();
        }

        EditorGUILayout.EndHorizontal();
    }

    private void DrawActiveStampBanner()
    {
        if (QuickAssetShelfService.ActiveSpawnPrefab == null) return;

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

        var titleStyle = new GUIStyle(EditorStyles.boldLabel) { richText = true };
        GUI.color = new Color(0.4f, 0.9f, 1f);
        EditorGUILayout.LabelField($"🎯 Aktif Damga: <b>{QuickAssetShelfService.ActiveSpawnPrefab.name}</b>", titleStyle);
        GUI.color = Color.white;

        EditorGUILayout.LabelField("Scene View'da <b>Shift + Sol Tık</b> veya <b>'B'</b> tuşu ile yerleştir.",
            new GUIStyle(EditorStyles.miniLabel) { richText = true });

        if (GUILayout.Button("Damga Modunu Kapat", EditorStyles.miniButton))
            QuickAssetShelfService.ActiveSpawnPrefab = null;

        EditorGUILayout.EndVertical();
    }

    private void DrawItemList()
    {
        _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

        QuickAssetShelfService.PruneInvalid();

        var pinned = Query(QuickAssetShelfService.PinnedGuids);
        var recents = Query(QuickAssetShelfService.RecentGuids.Where(g => !QuickAssetShelfService.IsPinned(g)));

        if (pinned.Count == 0 && recents.Count == 0)
        {
            EditorGUILayout.Space(15);
            EditorGUILayout.HelpBox(
                string.IsNullOrEmpty(_search)
                    ? "Arka plan dinlemede. Project panelinden Prefab veya ScriptableObject seçtiğinizde buraya kaydedilir."
                    : "Aramayla eşleşen asset yok.",
                MessageType.Info);
        }
        else
        {
            DrawSection("📌 Sabitlenenler", pinned, true);
            DrawSection("🕘 Son Kullanılanlar", recents, false);
        }

        EditorGUILayout.EndScrollView();
    }

    private void DrawSection(string title, List<(string guid, UnityEngine.Object item)> items, bool pinnedSection)
    {
        if (items.Count == 0) return;

        EditorGUILayout.Space(3);
        EditorGUILayout.LabelField($"{title} ({items.Count})", EditorStyles.miniBoldLabel);
        foreach (var (guid, item) in items)
            DrawItemRow(item, guid, pinnedSection);
    }

    private void DrawItemRow(UnityEngine.Object item, string guid, bool pinned)
    {
        bool isStamp = QuickAssetShelfService.ActiveSpawnPrefab == item;
        bool isPrefab = item is GameObject;

        if (isStamp) GUI.backgroundColor = new Color(0.4f, 0.8f, 1f);
        Rect rowRect = EditorGUILayout.BeginHorizontal(EditorStyles.helpBox, GUILayout.Height(26));
        GUI.backgroundColor = Color.white;

        // Çift tıklama bilgisini satır çizilmeden önce yakala (butonlar event'i tüketebilir)
        Event evt = Event.current;
        bool doubleClick = evt != null && evt.type == EventType.MouseDown && evt.clickCount == 2;
        Vector2 mousePosition = evt != null ? evt.mousePosition : Vector2.zero;

        GUILayout.Label(new GUIContent(AssetPreview.GetMiniThumbnail(item)), GUILayout.Width(20), GUILayout.Height(20));

        string badge = isPrefab ? "<color=#88CCFF>[Prefab]</color>" : "<color=#FFD700>[SO]</color>";
        var labelStyle = new GUIStyle(EditorStyles.boldLabel) { richText = true, alignment = TextAnchor.MiddleLeft };

        if (GUILayout.Button($"<b>{item.name}</b> {badge}", labelStyle, GUILayout.Height(20)))
        {
            EditorGUIUtility.PingObject(item);
            Selection.activeObject = item;
        }

        if (isPrefab)
        {
            GUI.color = isStamp ? Color.cyan : Color.white;
            if (GUILayout.Button(isStamp ? "🎯 Hazır" : "Damgala", EditorStyles.miniButton, GUILayout.Width(58), GUILayout.Height(18)))
                QuickAssetShelfService.ActiveSpawnPrefab = isStamp ? null : (GameObject)item;
            GUI.color = Color.white;
        }

        GUI.color = pinned ? new Color(1f, 0.85f, 0.3f) : Color.white;
        if (GUILayout.Button(pinned ? "📌" : "📍", EditorStyles.miniButton, GUILayout.Width(24), GUILayout.Height(18)))
            QuickAssetShelfService.TogglePin(guid);
        GUI.color = Color.white;

        if (GUILayout.Button("✕", EditorStyles.miniButton, GUILayout.Width(20), GUILayout.Height(18)))
        {
            QuickAssetShelfService.Remove(guid);
            GUIUtility.ExitGUI();
        }

        EditorGUILayout.EndHorizontal();

        if (doubleClick && rowRect.Contains(mousePosition))
        {
            AssetDatabase.OpenAsset(item);
            evt.Use();
        }

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

    /// <summary>GUID listesini yüklenebilir asset'lere çevirir, filtre ve aramayı uygular.</summary>
    private List<(string guid, UnityEngine.Object item)> Query(IEnumerable<string> guids)
    {
        var result = new List<(string, UnityEngine.Object)>();

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path)) continue;

            var item = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
            if (item == null) continue;

            bool isPrefab = item is GameObject;
            bool isSo = item is ScriptableObject;
            if (_filterIndex == 1 && !isPrefab) continue;
            if (_filterIndex == 2 && !isSo) continue;

            if (!string.IsNullOrEmpty(_search) &&
                item.name.IndexOf(_search, StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            result.Add((guid, item));
        }

        return result;
    }
}
#endif
