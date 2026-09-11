#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// EventDatabase için grup ağaçlı, aranabilir özel inspector.
/// Olaylar 'group' alanına göre klasörlenir; arama yalnızca eşleşen dalları gösterir.
/// </summary>
[CustomEditor(typeof(EventDatabase))]
public class EventDatabaseEditor : Editor
{
    private class Node
    {
        public string Name;
        public readonly Dictionary<string, Node> Children = new();
        public readonly List<int> Leaves = new();
        public int Total;
    }

    private string _search = "";
    private bool _onlyProblems;
    private bool _expandAll = true;
    private readonly Dictionary<string, bool> _foldouts = new();
    private readonly Dictionary<string, int> _pathCounts = new();

    private SerializedProperty _events;

    private static GUIStyle _folderStyle;
    private static GUIStyle FolderStyle => _folderStyle ??= new GUIStyle(EditorStyles.foldoutHeader)
    {
        richText = true,
        fontStyle = FontStyle.Bold,
    };

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        _events = serializedObject.FindProperty("events");
        RebuildDuplicateMap();

        DrawToolbar();

        var root = BuildTree();
        if (root.Total == 0)
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.HelpBox(
                _events.arraySize == 0 ? "Veritabanında hiç olay yok. Sağ üstten ➕ ile ekle."
                                       : "Filtreyle eşleşen olay yok.",
                MessageType.Info);
        }
        else
        {
            DrawNode(root, "root");
        }

        serializedObject.ApplyModifiedProperties();
    }

    // ---------- Toolbar ----------

    private void DrawToolbar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

        EditorGUI.BeginChangeCheck();
        _search = EditorGUILayout.TextField(_search, EditorStyles.toolbarSearchField);
        if (EditorGUI.EndChangeCheck()) _foldouts.Clear();

        if (GUILayout.Button("", GUI.skin.FindStyle("ToolbarSearchCancelButton") ?? EditorStyles.toolbarButton))
        {
            _search = "";
            GUIUtility.keyboardControl = 0;
            _foldouts.Clear();
        }

        _onlyProblems = GUILayout.Toggle(_onlyProblems, "⚠ Uyarılar", EditorStyles.toolbarButton, GUILayout.Width(72));
        GUILayout.FlexibleSpace();

        if (GUILayout.Button("Tümünü Aç", EditorStyles.toolbarButton, GUILayout.Width(72)))
        {
            _expandAll = true;
            _foldouts.Clear();
        }
        if (GUILayout.Button("Kapat", EditorStyles.toolbarButton, GUILayout.Width(52)))
        {
            _expandAll = false;
            _foldouts.Clear();
        }

        if (GUILayout.Button("➕", EditorStyles.toolbarButton, GUILayout.Width(28)))
        {
            AddEvent();
            serializedObject.ApplyModifiedProperties();
            GUIUtility.ExitGUI();
        }

        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        GUILayout.Label($"{_events.arraySize} olay • {rootGroups} grup", EditorStyles.miniLabel);
        int dupe = CountDuplicateEntries();
        if (dupe > 0)
        {
            var prev = GUI.color;
            GUI.color = new Color(1f, 0.55f, 0.55f);
            GUILayout.Label($"⚠ {dupe} çakışan kayıt", EditorStyles.miniLabel);
            GUI.color = prev;
        }
        EditorGUILayout.EndHorizontal();
    }

    private int rootGroups;

    // ---------- Tree ----------

    private Node BuildTree()
    {
        var root = new Node { Name = "Root" };
        _pathCounts.Clear();
        RebuildDuplicateMap();

        for (int i = 0; i < _events.arraySize; i++)
        {
            var el = _events.GetArrayElementAtIndex(i);
            if (!Matches(el)) continue;
            if (_onlyProblems && !HasProblem(el)) continue;

            string group = el.FindPropertyRelative("group").stringValue;
            var node = root;
            node.Total++;

            foreach (string part in group.Split('/', StringSplitOptions.RemoveEmptyEntries))
            {
                if (!node.Children.TryGetValue(part, out var child))
                {
                    child = new Node { Name = part };
                    node.Children[part] = child;
                }
                node = child;
                node.Total++;
            }

            node.Leaves.Add(i);
        }

        rootGroups = CountGroups(root);
        return root;
    }

    private static int CountGroups(Node node)
        => node.Children.Count + node.Children.Values.Sum(CountGroups);

    private void DrawNode(Node node, string key)
    {
        foreach (var child in node.Children.Values.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase))
            DrawFolder(child, key + "/" + child.Name);

        foreach (int idx in node.Leaves)
            DrawEventRow(idx);
    }

    private void DrawFolder(Node node, string key)
    {
        bool open = _foldouts.TryGetValue(key, out bool v) ? v : DefaultOpen;

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        open = EditorGUILayout.Foldout(
            open,
            $"📁 {node.Name} <color=grey>({node.Total} olay)</color>",
            true,
            FolderStyle);
        _foldouts[key] = open;

        if (open)
        {
            EditorGUI.indentLevel++;
            DrawNode(node, key);
            EditorGUI.indentLevel--;
        }
        EditorGUILayout.EndVertical();
        EditorGUILayout.Space(2);
    }

    private bool DefaultOpen => !string.IsNullOrEmpty(_search) || _expandAll;

    // ---------- Event row ----------

    private void DrawEventRow(int index)
    {
        var el = _events.GetArrayElementAtIndex(index);
        var groupProp = el.FindPropertyRelative("group");
        var nameProp = el.FindPropertyRelative("eventName");
        var persistentProp = el.FindPropertyRelative("isPersistent");
        var payloadProp = el.FindPropertyRelative("payload");

        string fullPath = FullPath(el);
        string typeName = ShortTypeName(payloadProp);
        bool duplicate = !string.IsNullOrEmpty(fullPath) && _pathCounts.TryGetValue(fullPath, out int c) && c > 1;
        bool empty = string.IsNullOrEmpty(nameProp.stringValue);

        var prevBg = GUI.backgroundColor;
        if (duplicate || empty) GUI.backgroundColor = new Color(1f, 0.55f, 0.55f);

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        GUI.backgroundColor = prevBg;

        EditorGUILayout.BeginHorizontal();
        string key = "evt:" + index;
        bool open = _foldouts.TryGetValue(key, out bool v) ? v : DefaultOpen;

        string label = empty ? "(isimsiz)" : nameProp.stringValue;
        string title = $"{(empty ? "⚠" : "◆")} <b>{label}</b> <color=#88CCFF>[{typeName}]</color>"
            + (persistentProp.boolValue ? " <color=#FF66CC>💾</color>" : "")
            + (duplicate ? " <color=#FF5555>⚠ çakışma</color>" : "");

        open = EditorGUILayout.Foldout(open, title, true, new GUIStyle(EditorStyles.foldout) { richText = true });
        _foldouts[key] = open;

        GUILayout.FlexibleSpace();
        if (GUILayout.Button("✕", EditorStyles.miniButton, GUILayout.Width(22)))
        {
            _events.DeleteArrayElementAtIndex(index);
            serializedObject.ApplyModifiedProperties();
            GUIUtility.ExitGUI();
        }
        EditorGUILayout.EndHorizontal();

        if (open)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(groupProp, new GUIContent("Grup", "Klasör yolu. '/' ile alt grup açılır."));
            EditorGUILayout.PropertyField(nameProp);
            EditorGUILayout.PropertyField(persistentProp);
            EditorGUILayout.PropertyField(payloadProp, new GUIContent("Payload"), true);

            if (duplicate)
                EditorGUILayout.HelpBox($"'{fullPath}' yolu birden fazla kez tanımlı. Çözümleme ilk kaydı döner.", MessageType.Error);
            else if (empty)
                EditorGUILayout.HelpBox("Event Name boş; kanal adresi oluşmaz.", MessageType.Warning);
            EditorGUI.indentLevel--;
        }

        EditorGUILayout.EndVertical();
    }

    private void AddEvent()
    {
        int idx = _events.arraySize;
        _events.InsertArrayElementAtIndex(idx);
        var el = _events.GetArrayElementAtIndex(idx);
        el.FindPropertyRelative("group").stringValue = "";
        el.FindPropertyRelative("eventName").stringValue = "";
        el.FindPropertyRelative("isPersistent").boolValue = false;
        el.FindPropertyRelative("payload").managedReferenceValue = new VoidPayload();
        _foldouts["evt:" + idx] = true;
    }

    // ---------- Helpers ----------

    private void RebuildDuplicateMap()
    {
        _pathCounts.Clear();
        for (int i = 0; i < _events.arraySize; i++)
        {
            string p = FullPath(_events.GetArrayElementAtIndex(i));
            if (string.IsNullOrEmpty(p)) continue;
            _pathCounts[p] = _pathCounts.TryGetValue(p, out int c) ? c + 1 : 1;
        }
    }

    private int CountDuplicateEntries()
        => _pathCounts.Where(kv => kv.Value > 1).Sum(kv => kv.Value);

    private bool Matches(SerializedProperty el)
    {
        if (string.IsNullOrEmpty(_search)) return true;
        return Contains(el.FindPropertyRelative("eventName").stringValue)
            || Contains(el.FindPropertyRelative("group").stringValue)
            || Contains(FullPath(el))
            || Contains(ShortTypeName(el.FindPropertyRelative("payload")));
    }

    private bool HasProblem(SerializedProperty el)
    {
        if (string.IsNullOrEmpty(el.FindPropertyRelative("eventName").stringValue)) return true;
        string p = FullPath(el);
        return !string.IsNullOrEmpty(p) && _pathCounts.TryGetValue(p, out int c) && c > 1;
    }

    private bool Contains(string haystack)
        => !string.IsNullOrEmpty(haystack) && haystack.IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0;

    private static string FullPath(SerializedProperty el)
    {
        string group = el.FindPropertyRelative("group").stringValue;
        string name = el.FindPropertyRelative("eventName").stringValue;
        if (string.IsNullOrEmpty(name)) return "";
        return string.IsNullOrEmpty(group) ? name : $"{group}/{name}";
    }

    private static string ShortTypeName(SerializedProperty payloadProp)
    {
        string full = payloadProp.managedReferenceFullTypename;
        if (string.IsNullOrEmpty(full)) return "null";
        int cut = Math.Max(full.LastIndexOf('.'), full.LastIndexOf(' '));
        return cut >= 0 && cut < full.Length - 1 ? full.Substring(cut + 1) : full;
    }
}
#endif
