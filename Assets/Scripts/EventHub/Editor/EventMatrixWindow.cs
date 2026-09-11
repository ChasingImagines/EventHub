#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public class EventMatrixWindow : EditorWindow
{
    private const float PULSE_DURATION = 0.5f;
    private const double MIN_FRAME_TIME = 0.0333; // Max 30 FPS sınırı

    private Vector2 _scrollPos;
    private string _searchQuery = "";
    private static readonly Dictionary<string, bool> _foldoutStates = new();
    private static readonly Dictionary<string, double> _pulseTimestamps = new();
    private static readonly List<string> _expiredKeysCache = new();

    private static EventFolderNode _cachedTreeRoot;
    private static bool _needsTreeRebuild = true;
    private static double _lastRepaintTime;

    private class ObjectTreeNode
    {
        public Dictionary<string, ObjectTreeNode> SubFolders = new();
        public Dictionary<string, List<UnityEngine.Object>> ComponentTypes = new();

        public int CleanAndGetTotalCount()
        {
            int total = 0;
            foreach (var list in ComponentTypes.Values)
            {
                list.RemoveAll(obj => obj == null);
                total += list.Count;
            }
            foreach (var sub in SubFolders.Values)
            {
                total += sub.CleanAndGetTotalCount();
            }
            return total;
        }
    }

    private class EventLink
    {
        public ObjectTreeNode publisherRoot = new();
        public ObjectTreeNode listenerRoot = new();
        public bool hasPublishers = false;
        public bool hasListeners = false;
    }

    private class EventFolderNode
    {
        public string Name;
        public Dictionary<string, EventFolderNode> SubFolders = new();
        public EventLink LeafEventData;
        public string FullPath;
        public int TotalChildEventsCount;
    }

    [MenuItem("Tools/Architecture/Event Matrix")]
    public static void OpenWindow()
    {
        GetWindow<EventMatrixWindow>("Event Matrix");
    }

    private void OnEnable()
    {
        _needsTreeRebuild = true;
        EventHub.OnEventRaisedInEditor -= HandleEventFired;
        EventHub.OnEventRaisedInEditor += HandleEventFired;
        EditorApplication.update -= OnEditorUpdate;
        EditorApplication.update += OnEditorUpdate;

        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        EditorApplication.hierarchyChanged -= OnHierarchyChanged;
        EditorApplication.hierarchyChanged += OnHierarchyChanged;
    }

    private void OnDisable()
    {
        EventHub.OnEventRaisedInEditor -= HandleEventFired;
        EditorApplication.update -= OnEditorUpdate;
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        EditorApplication.hierarchyChanged -= OnHierarchyChanged;
        _pulseTimestamps.Clear();
    }

    private void OnPlayModeChanged(PlayModeStateChange state)
    {
        _needsTreeRebuild = true;
        _cachedTreeRoot = null;
        _pulseTimestamps.Clear();
        Repaint();
    }

    private void OnHierarchyChanged()
    {
        _needsTreeRebuild = true;
    }

    private void HandleEventFired(string eventName)
    {
        double now = EditorApplication.timeSinceStartup;
        _pulseTimestamps[eventName] = now;

        if (now - _lastRepaintTime >= MIN_FRAME_TIME)
        {
            _lastRepaintTime = now;
            Repaint();
        }
    }

    private void OnEditorUpdate()
    {
        if (_pulseTimestamps.Count == 0) return;

        double now = EditorApplication.timeSinceStartup;
        if (now - _lastRepaintTime < MIN_FRAME_TIME) return;

        bool hasActivePulse = false;
        _expiredKeysCache.Clear();

        foreach (var (eventName, timestamp) in _pulseTimestamps)
        {
            if (now - timestamp < PULSE_DURATION)
                hasActivePulse = true;
            else
                _expiredKeysCache.Add(eventName);
        }

        for (int i = 0; i < _expiredKeysCache.Count; i++)
            _pulseTimestamps.Remove(_expiredKeysCache[i]);

        if (hasActivePulse || _expiredKeysCache.Count > 0)
        {
            _lastRepaintTime = now;
            Repaint();
        }
    }

    private void OnGUI()
    {
        if (_needsTreeRebuild || _cachedTreeRoot == null)
        {
            _cachedTreeRoot = BuildEventTree();
            _needsTreeRebuild = false;
        }

        DrawToolbar();

        _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

        if (_cachedTreeRoot.SubFolders.Count == 0)
        {
            EditorGUILayout.Space(10);
            EditorGUILayout.HelpBox("Sahnede veya ScriptableObject'lerde aktif olay bulunamadı.", MessageType.Info);
        }
        else
        {
            EditorGUILayout.Space(5);
            DrawEventFolders(_cachedTreeRoot, "Root");
        }

        EditorGUILayout.EndScrollView();
    }

    private void DrawToolbar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

        if (GUILayout.Button("🔄 Yeniden Tara", EditorStyles.toolbarButton, GUILayout.Width(95)))
        {
            _needsTreeRebuild = true;
            Repaint();
        }

        EditorGUI.BeginChangeCheck();
        _searchQuery = EditorGUILayout.TextField(_searchQuery, EditorStyles.toolbarSearchField);
        if (EditorGUI.EndChangeCheck())
        {
            _needsTreeRebuild = true;
        }

        if (GUILayout.Button("", GUI.skin.FindStyle("ToolbarSearchCancelButton") ?? EditorStyles.toolbarButton))
        {
            _searchQuery = "";
            GUIUtility.keyboardControl = 0;
            _needsTreeRebuild = true;
        }

        if (GUILayout.Button("Tümünü Aç", EditorStyles.toolbarButton, GUILayout.Width(75)))
        {
            if (_cachedTreeRoot != null) SetAllFoldoutsRecursive(_cachedTreeRoot, "Root", true);
        }

        if (GUILayout.Button("Kapat", EditorStyles.toolbarButton, GUILayout.Width(50)))
        {
            if (_cachedTreeRoot != null) SetAllFoldoutsRecursive(_cachedTreeRoot, "Root", false);
        }

        EditorGUILayout.EndHorizontal();
    }

    private EventFolderNode BuildEventTree()
    {
        var root = new EventFolderNode { Name = "Root" };
        var rawMap = new Dictionary<string, EventLink>();

        // 1. Sahnedeki MonoBehaviour'ları tara
        var behaviours = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
        foreach (var mb in behaviours)
        {
            if (mb == null) continue;
            var groupComp = mb.GetComponentInParent<EventGroup>();
            string groupPath = groupComp != null ? groupComp.GetFullHierarchyPath() : "";
            ScanObjectFields(mb, groupPath, rawMap);
        }

        // 2. IEventHubListener uygulayan tüm ScriptableObject varlıklarını tara
        var listenerTypes = TypeCache.GetTypesDerivedFrom<IEventHubListener>();
        var scannedGuids = new HashSet<string>();

        foreach (var type in listenerTypes)
        {
            if (!typeof(ScriptableObject).IsAssignableFrom(type) || type.IsAbstract) continue;

            string[] guids = AssetDatabase.FindAssets($"t:{type.Name}");
            foreach (var guid in guids)
            {
                if (!scannedGuids.Add(guid)) continue;

                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                var so = AssetDatabase.LoadAssetAtPath<ScriptableObject>(assetPath);
                if (so == null) continue;

                // Derin disk yolu yerine doğrudan temiz grup altında topla
                ScanObjectFields(so, "Scriptable Objects", rawMap);
            }
        }

        foreach (var (fullPath, link) in rawMap)
        {
            string[] parts = fullPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var current = root;

            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i];
                bool isLeaf = (i == parts.Length - 1);

                if (!current.SubFolders.TryGetValue(part, out var nextNode))
                {
                    nextNode = new EventFolderNode { Name = part };
                    current.SubFolders[part] = nextNode;
                }

                if (isLeaf)
                {
                    nextNode.LeafEventData = link;
                    nextNode.FullPath = fullPath;
                }

                current.TotalChildEventsCount++;
                current = nextNode;
            }
        }

        return root;
    }

    private void ScanObjectFields(UnityEngine.Object target, string groupPath, Dictionary<string, EventLink> rawMap)
    {
        var fields = target.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        foreach (var f in fields)
        {
            if (f.FieldType != typeof(string)) continue;
            string eventPath = f.GetValue(target) as string;
            if (string.IsNullOrEmpty(eventPath)) continue;

            if (!string.IsNullOrEmpty(_searchQuery) &&
                eventPath.IndexOf(_searchQuery, StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            if (f.GetCustomAttribute<EventPublisherAttribute>() != null)
            {
                if (!rawMap.ContainsKey(eventPath)) rawMap[eventPath] = new EventLink();
                InsertIntoObjectTree(rawMap[eventPath].publisherRoot, groupPath, target);
                rawMap[eventPath].hasPublishers = true;
            }

            if (f.GetCustomAttribute<EventListenerAttribute>() != null)
            {
                if (!rawMap.ContainsKey(eventPath)) rawMap[eventPath] = new EventLink();
                InsertIntoObjectTree(rawMap[eventPath].listenerRoot, groupPath, target);
                rawMap[eventPath].hasListeners = true;
            }
        }
    }

    private void DrawEventFolders(EventFolderNode node, string currentPathKey)
    {
        foreach (var (folderName, subNode) in node.SubFolders)
        {
            string nodeKey = $"{currentPathKey}/{folderName}";

            if (subNode.LeafEventData != null && subNode.SubFolders.Count == 0)
            {
                DrawEventCard(subNode.Name, subNode.FullPath, subNode.LeafEventData, nodeKey);
            }
            else
            {
                bool defaultOpen = !string.IsNullOrEmpty(_searchQuery);
                if (!_foldoutStates.ContainsKey(nodeKey)) _foldoutStates[nodeKey] = defaultOpen;

                int count = subNode.LeafEventData != null ? subNode.TotalChildEventsCount + 1 : subNode.TotalChildEventsCount;

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                _foldoutStates[nodeKey] = EditorGUILayout.Foldout(
                    _foldoutStates[nodeKey],
                    $"📁 <b>{folderName}</b> <color=grey>({count} Olay)</color>",
                    true,
                    new GUIStyle(EditorStyles.foldoutHeader) { richText = true }
                );

                if (_foldoutStates[nodeKey])
                {
                    EditorGUI.indentLevel++;
                    if (subNode.LeafEventData != null)
                    {
                        DrawEventCard(subNode.Name, subNode.FullPath, subNode.LeafEventData, $"{nodeKey}_leaf");
                    }
                    DrawEventFolders(subNode, nodeKey);
                    EditorGUI.indentLevel--;
                }
                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(2);
            }
        }
    }

    private void DrawEventCard(string displayName, string fullPath, EventLink link, string key)
    {
        float pulseFactor = 0f;
        if (_pulseTimestamps.TryGetValue(fullPath, out double lastFired))
        {
            float elapsed = (float)(EditorApplication.timeSinceStartup - lastFired);
            if (elapsed < PULSE_DURATION)
            {
                pulseFactor = 1f - (elapsed / PULSE_DURATION);
            }
        }

        Color defaultBg = GUI.backgroundColor;
        if (pulseFactor > 0f)
        {
            GUI.backgroundColor = Color.Lerp(defaultBg, new Color(0.2f, 1f, 0.3f, 1f), pulseFactor);
        }

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        GUI.backgroundColor = defaultBg;

        bool defaultOpen = !string.IsNullOrEmpty(_searchQuery);
        if (!_foldoutStates.ContainsKey(key)) _foldoutStates[key] = defaultOpen;

        int pubCount = link.publisherRoot.CleanAndGetTotalCount();
        int lisCount = link.listenerRoot.CleanAndGetTotalCount();

        Type expectedType = EventDatabase.Instance != null ? EventDatabase.Instance.GetExpectedType(fullPath) : typeof(void);
        string typeBadge = expectedType == typeof(void) ? "Void" : expectedType.Name;

        string statusIcon = pulseFactor > 0.05f ? "<color=#00FF66>● AKTİF</color> " : "⚡ ";

        // Canlı State (Değer) Bilgisini Çek
        string stateBadge = "";
        string stateDetailedText = "<color=grey><i>(Henüz veri fırlatılmadı)</i></color>";
        bool hasTrackedState = EventHub.TryGetRawState(fullPath, out object rawValue, out bool hasValue, out bool isPersistent);
        string persistBadge = isPersistent ? "<color=#FF66CC>💾 KALICI</color> " : "";

        if (hasTrackedState && hasValue)
        {
            if (expectedType == typeof(void))
            {
                stateBadge = "<color=#66E0FF>[Hazır]</color> ";
                stateDetailedText = "<color=#66E0FF>Tetiklendi (Void Sinyal Hazır)</color>";
            }
            else
            {
                string valStr = rawValue != null ? rawValue.ToString() : "null";
                stateBadge = $"<color=#FFD700>[Değer: {valStr}]</color> ";
                stateDetailedText = $"<color=#FFD700><b>{valStr}</b></color>";
            }
        }
        else if (hasTrackedState && !hasValue)
        {
            stateBadge = "<color=#FF6666>[Tüketildi]</color> ";
            stateDetailedText = "<color=#FF6666>Geçersiz / Tüketilmiş Durum</color>";
        }

        _foldoutStates[key] = EditorGUILayout.Foldout(
            _foldoutStates[key],
            $"{statusIcon}<b>{displayName}</b> <color=#88CCFF>[{typeBadge}]</color> {persistBadge}{stateBadge}<color=#FFAA55>({pubCount} Tetikleyici)</color> / <color=#66CC66>({lisCount} Dinleyici)</color>",
            true,
            new GUIStyle(EditorStyles.foldout) { richText = true, fontStyle = FontStyle.Bold }
        );

        if (_foldoutStates[key])
        {
            EditorGUILayout.Space(2);

            // Canlı Durum (State) Çubuğu
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            EditorGUILayout.LabelField($"<b>Canlı Durum:</b> {stateDetailedText}", new GUIStyle(EditorStyles.label) { richText = true });

            if (hasTrackedState && hasValue)
            {
                if (GUILayout.Button("Tüket (Invalidate)", EditorStyles.miniButton, GUILayout.Width(130)))
                {
                    EventHub.Invalidate(fullPath);
                    Repaint();
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(3);
            EditorGUILayout.BeginHorizontal();

            // Publishers
            EditorGUILayout.BeginVertical(GUILayout.Width(position.width * 0.46f));
            GUI.color = new Color(1f, 0.75f, 0.4f);
            EditorGUILayout.LabelField($"▶ Tetikleyiciler ({pubCount})", EditorStyles.boldLabel);
            GUI.color = Color.white;
            if (pubCount == 0)
                EditorGUILayout.LabelField("<i>(Tetikleyen yok)</i>", new GUIStyle { richText = true });
            else
                DrawObjectTree(link.publisherRoot, $"{key}_pub");
            EditorGUILayout.EndVertical();

            // Listeners
            EditorGUILayout.BeginVertical(GUILayout.Width(position.width * 0.46f));
            GUI.color = new Color(0.5f, 1f, 0.5f);
            EditorGUILayout.LabelField($"◀ Dinleyiciler ({lisCount})", EditorStyles.boldLabel);
            GUI.color = Color.white;
            if (lisCount == 0)
                EditorGUILayout.LabelField("<i>(Dinleyen yok)</i>", new GUIStyle { richText = true });
            else
                DrawObjectTree(link.listenerRoot, $"{key}_lis");
            EditorGUILayout.EndVertical();

            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.EndVertical();
        EditorGUILayout.Space(2);
    }

    private void DrawObjectTree(ObjectTreeNode node, string currentPath)
    {
        foreach (var (folderName, subNode) in node.SubFolders)
        {
            int memberCount = subNode.CleanAndGetTotalCount();
            if (memberCount == 0) continue;

            string folderKey = $"{currentPath}/{folderName}";
            bool defaultOpen = !string.IsNullOrEmpty(_searchQuery);
            if (!_foldoutStates.ContainsKey(folderKey)) _foldoutStates[folderKey] = defaultOpen;

            _foldoutStates[folderKey] = EditorGUILayout.Foldout(
                _foldoutStates[folderKey],
                $"📁 <b>{folderName}</b> <color=grey>({memberCount} Üye)</color>",
                true,
                new GUIStyle(EditorStyles.foldout) { richText = true }
            );

            if (_foldoutStates[folderKey])
            {
                EditorGUI.indentLevel++;
                DrawObjectTree(subNode, folderKey);
                EditorGUI.indentLevel--;
            }
        }

        foreach (var (typeName, instances) in node.ComponentTypes)
        {
            instances.RemoveAll(obj => obj == null);
            if (instances.Count == 0) continue;

            if (instances.Count == 1)
            {
                DrawPingableButton(instances[0], typeName);
            }
            else
            {
                string bundleKey = $"{currentPath}_{typeName}_bundle";
                if (!_foldoutStates.ContainsKey(bundleKey)) _foldoutStates[bundleKey] = false;

                _foldoutStates[bundleKey] = EditorGUILayout.Foldout(_foldoutStates[bundleKey], $"📦 {typeName} ({instances.Count} Adet)", true);

                if (_foldoutStates[bundleKey])
                {
                    EditorGUI.indentLevel++;
                    foreach (var target in instances)
                    {
                        DrawPingableButton(target, null);
                    }
                    EditorGUI.indentLevel--;
                }
            }
        }
    }

    private void DrawPingableButton(UnityEngine.Object target, string typeName)
    {
        if (target == null) return;

        bool isSO = target is ScriptableObject;
        string badge = isSO ? "<color=#FFD700>[SO]</color> " : "";
        string displayName = target.name;
        string label = string.IsNullOrEmpty(typeName) ? $"↳ {badge}{displayName}" : $"• {badge}{displayName} ({typeName})";

        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(EditorGUI.indentLevel * 14f);
        var style = new GUIStyle(EditorStyles.linkLabel) { richText = true };
        if (GUILayout.Button(label, style))
        {
            var pingTarget = (target is Component comp) ? comp.gameObject : target;
            EditorGUIUtility.PingObject(pingTarget);
            Selection.activeObject = pingTarget;
        }
        EditorGUILayout.EndHorizontal();
    }

    private void InsertIntoObjectTree(ObjectTreeNode root, string groupPath, UnityEngine.Object target)
    {
        var current = root;
        if (!string.IsNullOrEmpty(groupPath))
        {
            string[] parts = groupPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                if (!current.SubFolders.TryGetValue(part, out var nextNode))
                {
                    nextNode = new ObjectTreeNode();
                    current.SubFolders[part] = nextNode;
                }
                current = nextNode;
            }
        }

        string typeName = target.GetType().Name;
        if (!current.ComponentTypes.TryGetValue(typeName, out var list))
        {
            list = new List<UnityEngine.Object>();
            current.ComponentTypes[typeName] = list;
        }
        list.Add(target);
    }

    private void SetAllFoldoutsRecursive(EventFolderNode node, string currentPathKey, bool state)
    {
        foreach (var (folderName, subNode) in node.SubFolders)
        {
            string nodeKey = $"{currentPathKey}/{folderName}";
            _foldoutStates[nodeKey] = state;
            SetAllFoldoutsRecursive(subNode, nodeKey, state);
        }
    }
}
#endif