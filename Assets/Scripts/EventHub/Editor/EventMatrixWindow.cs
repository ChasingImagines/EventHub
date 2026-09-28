#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public class EventMatrixWindow : EditorWindow
{
    private Vector2 _scrollPos;
    private string _searchQuery = "";
    private static readonly Dictionary<string, bool> _foldoutStates = new();
    private static readonly Dictionary<Type, List<(FieldInfo field, bool isPub, bool isLis)>> _typeFieldCache = new();

    // En son tetiklenen kanalı ve tetikleyen sender nesnesini takip eder
    private static readonly Dictionary<string, (object sender, double time)> _lastTriggers = new(StringComparer.OrdinalIgnoreCase);

    // --- PERFORMANS VE FPS KİLİTLERİ (THROTTLING) ---
    private static double _lastRepaintTime = 0.0;
    private const double REPAINT_INTERVAL = 0.1; // Maksimum 10 FPS (100ms) yenileme
    private static bool _repaintPending = false;

    private class EventItem
    {
        public string FullPath;
        public string Group;
        public string Name;
        public Type DataType;
        public bool IsPersistent;
        public object DefaultValue;
        public List<UnityEngine.Object> Publishers = new();
        public List<UnityEngine.Object> Listeners = new();
    }

    private List<EventItem> _cachedItems = new();
    private bool _needsRebuild = true;

    [MenuItem("Tools/Architecture/Event Matrix")]
    public static void OpenWindow()
    {
        var win = GetWindow<EventMatrixWindow>("Event Matrix");
        win.minSize = new Vector2(520, 320);
    }

    private void OnEnable()
    {
        _needsRebuild = true;
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;

        EventHub.OnEventRaisedInEditor -= OnEventFiredInEditor;
        EventHub.OnEventRaisedInEditor += OnEventFiredInEditor;

        EditorApplication.update -= ThrottledUpdate;
        EditorApplication.update += ThrottledUpdate;
    }

    private void OnDisable()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        EventHub.OnEventRaisedInEditor -= OnEventFiredInEditor;
        EditorApplication.update -= ThrottledUpdate;
    }

    private void OnEventFiredInEditor(string channel, object sender)
    {
        if (string.IsNullOrEmpty(channel)) return;

        double now = EditorApplication.timeSinceStartup;
        _lastTriggers[channel] = (sender, now);
        _repaintPending = true;
    }

    private void ThrottledUpdate()
    {
        if (!Application.isPlaying) return;

        double now = EditorApplication.timeSinceStartup;

        // Yoğun event bombardımanında bile UI en fazla 100ms aralıkla çizilir
        if (_repaintPending && (now - _lastRepaintTime) >= REPAINT_INTERVAL)
        {
            _lastRepaintTime = now;
            _repaintPending = false;
            Repaint();
        }
    }

    private void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode || state == PlayModeStateChange.EnteredEditMode)
        {
            _needsRebuild = true;
            Repaint();
        }
    }

    private void OnGUI()
    {
        if (_needsRebuild)
        {
            RebuildData();
            _needsRebuild = false;
        }

        DrawToolbar();
        DrawTableHeader();

        // Kalıcı dikey scrollbar
        _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos, false, true);

        if (_cachedItems.Count == 0)
        {
            EditorGUILayout.Space(20);
            EditorGUILayout.HelpBox("Tanımlı veya sahnede kullanılan olay bulunamadı.", MessageType.Info);
        }
        else
        {
            string currentGroup = null;

            for (int i = 0; i < _cachedItems.Count; i++)
            {
                var item = _cachedItems[i];

                if (!string.IsNullOrEmpty(_searchQuery))
                {
                    if (item.FullPath.IndexOf(_searchQuery, StringComparison.OrdinalIgnoreCase) < 0)
                        continue;
                }

                if (currentGroup != item.Group)
                {
                    currentGroup = item.Group;
                    DrawGroupHeader(string.IsNullOrEmpty(currentGroup) ? "Genel (Kök)" : currentGroup);
                }

                DrawEventRow(item, i);
            }

            EditorGUILayout.Space(16);
        }

        EditorGUILayout.EndScrollView();
    }

    private void DrawToolbar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

        if (GUILayout.Button("🔄 Yenile", EditorStyles.toolbarButton, GUILayout.Width(65)))
        {
            _needsRebuild = true;
            GUIUtility.keyboardControl = 0;
        }

        EditorGUI.BeginChangeCheck();
        _searchQuery = EditorGUILayout.TextField(_searchQuery, EditorStyles.toolbarSearchField);
        if (EditorGUI.EndChangeCheck()) { }

        if (GUILayout.Button("", GUI.skin.FindStyle("ToolbarSearchCancelButton") ?? EditorStyles.toolbarButton))
        {
            _searchQuery = "";
            GUIUtility.keyboardControl = 0;
        }

        GUILayout.FlexibleSpace();

        if (GUILayout.Button("Tümünü Aç", EditorStyles.toolbarButton, GUILayout.Width(75)))
        {
            foreach (var item in _cachedItems) _foldoutStates[item.FullPath] = true;
        }

        if (GUILayout.Button("Kapat", EditorStyles.toolbarButton, GUILayout.Width(50)))
        {
            _foldoutStates.Clear();
        }

        EditorGUILayout.EndHorizontal();
    }

    private void DrawTableHeader()
    {
        Rect r = EditorGUILayout.GetControlRect(false, 20);
        EditorGUI.DrawRect(r, EditorGUIUtility.isProSkin ? new Color(0.18f, 0.18f, 0.18f) : new Color(0.75f, 0.75f, 0.75f));

        var style = new GUIStyle(EditorStyles.miniBoldLabel) { alignment = TextAnchor.MiddleLeft };

        GUI.Label(new Rect(r.x + 22, r.y, 180, r.height), "OLAY KANALI", style);
        GUI.Label(new Rect(r.x + 210, r.y, 80, r.height), "TİP", style);
        GUI.Label(new Rect(r.x + 300, r.y, 90, r.height), "CANLI / DEĞER", style);
        GUI.Label(new Rect(r.x + 400, r.y, 50, r.height), "YAYIN", style);
        GUI.Label(new Rect(r.x + 460, r.y, 50, r.height), "DİNLE", style);
    }

    private void DrawGroupHeader(string groupName)
    {
        EditorGUILayout.Space(4);
        Rect r = EditorGUILayout.GetControlRect(false, 18);
        EditorGUI.DrawRect(r, EditorGUIUtility.isProSkin ? new Color(0.14f, 0.14f, 0.14f) : new Color(0.85f, 0.85f, 0.85f));
        GUI.Label(new Rect(r.x + 6, r.y, r.width, r.height), $"📁  {groupName}", EditorStyles.boldLabel);
    }

    private void DrawEventRow(EventItem item, int index)
    {
        bool isOpen = _foldoutStates.TryGetValue(item.FullPath, out bool v) && v;
        Rect rowRect = EditorGUILayout.GetControlRect(false, 22);

        // --- 1. KISITLANMIŞ VE SINIRLI PARLAMA (CLAMPED FLASH) ---
        float flashAlpha = 0f;
        if (_lastTriggers.TryGetValue(item.FullPath, out var trig))
        {
            float elapsed = (float)(EditorApplication.timeSinceStartup - trig.time);
            if (elapsed < 1.0f)
            {
                flashAlpha = Mathf.Clamp01(1.0f - elapsed);
            }
        }

        if (flashAlpha > 0.05f)
        {
            Color flashColor = new Color(0.2f, 1f, 0.4f, flashAlpha * 0.35f);
            EditorGUI.DrawRect(rowRect, flashColor);
        }
        else if (index % 2 == 0)
        {
            EditorGUI.DrawRect(rowRect, new Color(1, 1, 1, 0.03f));
        }

        // --- 2. FOLD OUT BUTONU ---
        Rect foldoutRect = new Rect(rowRect.x + 4, rowRect.y + 2, 16, 16);
        isOpen = EditorGUI.Foldout(foldoutRect, isOpen, GUIContent.none);
        _foldoutStates[item.FullPath] = isOpen;

        // --- 3. OLAY ADI & KALICILIK İKONU ---
        string displayName = item.Name + (item.IsPersistent ? "  💾" : "");
        GUI.Label(new Rect(rowRect.x + 22, rowRect.y, 180, rowRect.height), displayName, EditorStyles.boldLabel);

        // --- 4. TİP ROZETİ ---
        string typeName = GetFriendlyTypeName(item.DataType);
        GUI.color = item.DataType == typeof(void) ? Color.gray : new Color(0.4f, 0.8f, 1f);
        GUI.Label(new Rect(rowRect.x + 210, rowRect.y, 80, rowRect.height), $"[{typeName}]", EditorStyles.miniLabel);
        GUI.color = Color.white;

        // --- 5. CANLI DEĞER VEYA VARSAYILAN DEĞER ALANI ---
        string valStr = "-";
        if (Application.isPlaying)
        {
            if (EventHub.TryGetRawState(item.FullPath, out object rawVal, out bool hasVal, out _))
            {
                if (hasVal)
                {
                    if (item.DataType == typeof(void))
                    {
                        valStr = "⚡ Sinyal";
                        GUI.color = new Color(0.4f, 0.9f, 1f);
                    }
                    else
                    {
                        valStr = $"⚡ {rawVal}";
                        GUI.color = new Color(0.2f, 1f, 0.4f);
                    }
                }
                else
                {
                    valStr = "<Tüketildi>";
                    GUI.color = new Color(1f, 0.4f, 0.4f);
                }
            }
            else if (item.DefaultValue != null)
            {
                valStr = item.DefaultValue.ToString();
            }
        }
        else if (item.DefaultValue != null)
        {
            valStr = item.DefaultValue.ToString();
        }

        GUI.Label(new Rect(rowRect.x + 300, rowRect.y, 90, rowRect.height), valStr, EditorStyles.miniLabel);
        GUI.color = Color.white;

        // --- 6. YAYINLAYAN VE DİNLEYEN SAYILARI ---
        GUI.color = item.Publishers.Count > 0 ? new Color(1f, 0.7f, 0.3f) : Color.gray;
        GUI.Label(new Rect(rowRect.x + 400, rowRect.y, 50, rowRect.height), $"▶ {item.Publishers.Count}", EditorStyles.boldLabel);

        GUI.color = item.Listeners.Count > 0 ? new Color(0.4f, 1f, 0.5f) : Color.gray;
        GUI.Label(new Rect(rowRect.x + 460, rowRect.y, 50, rowRect.height), $"◀ {item.Listeners.Count}", EditorStyles.boldLabel);
        GUI.color = Color.white;

        // --- 7. DETAYLAR (AÇIKSA) ---
        if (isOpen)
        {
            DrawEventDetails(item);
        }
    }

    private void DrawEventDetails(EventItem item)
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.BeginHorizontal();

        // Sol Sütun: Yayınlayanlar
        EditorGUILayout.BeginVertical(GUILayout.Width(EditorGUIUtility.currentViewWidth * 0.47f));
        EditorGUILayout.LabelField($"▶ Yayınlayanlar ({item.Publishers.Count})", EditorStyles.boldLabel);

        // Son tetikleyen nesneyi anlık vurgula
        if (_lastTriggers.TryGetValue(item.FullPath, out var triggerInfo) && triggerInfo.sender != null)
        {
            double elapsed = EditorApplication.timeSinceStartup - triggerInfo.time;
            if (elapsed < 3.0)
            {
                var prevC = GUI.color;
                GUI.color = new Color(1f, 0.85f, 0.3f);
                EditorGUILayout.LabelField($"⚡ Son Tetikleyen: {triggerInfo.sender}", EditorStyles.miniBoldLabel);
                GUI.color = prevC;
            }
        }

        if (item.Publishers.Count == 0)
        {
            EditorGUILayout.LabelField("<i>(Yayınlayan yok)</i>", new GUIStyle(EditorStyles.miniLabel) { richText = true });
        }
        else
        {
            DrawGroupedTargets(item.Publishers, "pub_" + item.FullPath);
        }
        EditorGUILayout.EndVertical();

        GUILayout.Box("", GUILayout.Width(1), GUILayout.ExpandHeight(true));

        // Sağ Sütun: Dinleyenler
        EditorGUILayout.BeginVertical(GUILayout.Width(EditorGUIUtility.currentViewWidth * 0.47f));
        EditorGUILayout.LabelField($"◀ Dinleyenler ({item.Listeners.Count})", EditorStyles.boldLabel);

        if (item.Listeners.Count == 0)
        {
            EditorGUILayout.LabelField("<i>(Dinleyen yok)</i>", new GUIStyle(EditorStyles.miniLabel) { richText = true });
        }
        else
        {
            DrawGroupedTargets(item.Listeners, "lis_" + item.FullPath);
        }
        EditorGUILayout.EndVertical();

        EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndVertical();
        EditorGUILayout.Space(2);
    }

    private void DrawGroupedTargets(List<UnityEngine.Object> targets, string foldoutKeyPrefix)
    {
        var groups = new Dictionary<string, List<UnityEngine.Object>>();
        var ungrouped = new List<UnityEngine.Object>();

        for (int i = 0; i < targets.Count; i++)
        {
            var target = targets[i];
            if (target == null) continue;

            string groupPath = null;
            if (target is Component comp)
            {
                var groupComp = comp.GetComponentInParent<EventGroup>(true);
                if (groupComp != null)
                {
                    groupPath = groupComp.GetFullHierarchyPath();
                }
            }

            if (!string.IsNullOrEmpty(groupPath))
            {
                if (!groups.TryGetValue(groupPath, out var list))
                {
                    list = new List<UnityEngine.Object>();
                    groups[groupPath] = list;
                }
                list.Add(target);
            }
            else
            {
                ungrouped.Add(target);
            }
        }

        // 1. Gruplanmış Nesneler (Gereksiz "Seç" butonu kaldırıldı, sade foldout yapıldı)
        foreach (var (groupName, list) in groups)
        {
            string foldoutKey = $"{foldoutKeyPrefix}_{groupName}";
            bool open = _foldoutStates.TryGetValue(foldoutKey, out bool v) && v;

            open = EditorGUILayout.Foldout(open, $"🏷️ {groupName} <color=grey>({list.Count})</color>", true, new GUIStyle(EditorStyles.foldout) { richText = true });
            _foldoutStates[foldoutKey] = open;

            if (open)
            {
                EditorGUI.indentLevel++;
                for (int i = 0; i < list.Count; i++)
                {
                    DrawTargetObject(list[i]);
                }
                EditorGUI.indentLevel--;
            }
        }

        // 2. Grubu Olmayan Nesneler
        for (int i = 0; i < ungrouped.Count; i++)
        {
            DrawTargetObject(ungrouped[i]);
        }
    }
    private void DrawTargetObject(UnityEngine.Object target)
    {
        if (target == null) return;

        string typeName = target.GetType().Name;
        string objectName = target.name;

        // Nesne adı ile sınıf adı aynıysa tekrar etme
        string label = objectName == typeName ? $"• {objectName}" : $"• {objectName} <color=grey>({typeName})</color>";

        var style = new GUIStyle(EditorStyles.linkLabel) { richText = true };
        if (GUILayout.Button(label, style))
        {
            var pingTarget = (target is Component c) ? c.gameObject : target;
            EditorGUIUtility.PingObject(pingTarget);
            Selection.activeObject = pingTarget;
        }
    }

    private void RebuildData()
    {
        _cachedItems.Clear();
        var map = new Dictionary<string, EventItem>(StringComparer.OrdinalIgnoreCase);

        // 1. Database'den al
        var db = EventDatabase.Instance;
        if (db != null && db.events != null)
        {
            foreach (var ev in db.events)
            {
                if (ev == null || string.IsNullOrEmpty(ev.FullPath)) continue;

                map[ev.FullPath] = new EventItem
                {
                    FullPath = ev.FullPath,
                    Group = ev.group,
                    Name = ev.eventName,
                    DataType = ev.ExpectedType,
                    IsPersistent = ev.isPersistent,
                    DefaultValue = ev.payload?.GetDefaultRawValue()
                };
            }
        }

        // 2. Sahnedeki nesneleri tara
        var behaviours = FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (var mb in behaviours)
        {
            if (mb == null) continue;
            ScanObject(mb, map);
        }

        // 3. ScriptableObject dinleyicilerini tara
        var listenerTypes = TypeCache.GetTypesDerivedFrom<IEventHubListener>();
        foreach (var type in listenerTypes)
        {
            if (type.IsAbstract || !typeof(ScriptableObject).IsAssignableFrom(type)) continue;

            string[] guids = AssetDatabase.FindAssets($"t:{type.Name}");
            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var so = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
                if (so != null) ScanObject(so, map);
            }
        }

        _cachedItems.AddRange(map.Values);
    }

    private void ScanObject(UnityEngine.Object target, Dictionary<string, EventItem> map)
    {
        Type targetType = target.GetType();

        if (!_typeFieldCache.TryGetValue(targetType, out var fields))
        {
            fields = new List<(FieldInfo, bool, bool)>();
            foreach (var f in targetType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (f.FieldType != typeof(string)) continue;
                bool isPub = f.GetCustomAttribute<EventPublisherAttribute>() != null;
                bool isLis = f.GetCustomAttribute<EventListenerAttribute>() != null;
                if (isPub || isLis) fields.Add((f, isPub, isLis));
            }
            _typeFieldCache[targetType] = fields;
        }

        foreach (var (f, isPub, isLis) in fields)
        {
            string channel = f.GetValue(target) as string;
            if (string.IsNullOrEmpty(channel)) continue;

            if (!map.TryGetValue(channel, out var item))
            {
                int slash = channel.LastIndexOf('/');
                item = new EventItem
                {
                    FullPath = channel,
                    Group = slash >= 0 ? channel.Substring(0, slash) : "",
                    Name = slash >= 0 ? channel.Substring(slash + 1) : channel,
                    DataType = typeof(void),
                    DefaultValue = null
                };
                map[channel] = item;
            }

            if (isPub && !item.Publishers.Contains(target)) item.Publishers.Add(target);
            if (isLis && !item.Listeners.Contains(target)) item.Listeners.Add(target);
        }
    }

    private static string GetFriendlyTypeName(Type type)
    {
        if (type == null || type == typeof(void)) return "Void";
        if (type == typeof(float)) return "Float";
        if (type == typeof(int)) return "Int";
        if (type == typeof(bool)) return "Bool";
        if (type == typeof(string)) return "String";
        return type.Name;
    }
}
#endif