#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(EventDatabase))]
public class EventDatabaseEditor : Editor
{
    private class Node
    {
        public string Name;
        public string FullGroupPath;
        public readonly Dictionary<string, Node> Children = new();
        public readonly List<int> Leaves = new();
        public int Total;
    }

    private string _search = "";
    private bool _expandAll = false;
    private readonly Dictionary<string, bool> _foldouts = new();
    private readonly Dictionary<string, int> _pathCounts = new();

    private SerializedProperty _events;
    private static string _focusTargetControl = "";

    // Yeniden Adlandırma (Inline Rename Modu)
    private string _renamingGroupPath = null;
    private string _renameBuffer = "";

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        _events = serializedObject.FindProperty("events");
        RebuildDuplicateMap();

        DrawToolbar();

        var root = BuildTree();
        if (root.Total == 0 && _events.arraySize == 0)
        {
            EditorGUILayout.Space(12);
            EditorGUILayout.HelpBox("Veritabanı boş. Başlamak için yukarıdan '📁 + Grup' butonuna tıklayın.", MessageType.Info);
        }
        else
        {
            EditorGUILayout.Space(4);
            DrawNode(root, "root", 0);
        }

        serializedObject.ApplyModifiedProperties();
    }

    private void DrawToolbar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

        _search = EditorGUILayout.TextField(_search, EditorStyles.toolbarSearchField);
        if (GUILayout.Button("", GUI.skin.FindStyle("ToolbarSearchCancelButton") ?? EditorStyles.toolbarButton))
        {
            _search = "";
            GUIUtility.keyboardControl = 0;
        }

        GUILayout.FlexibleSpace();

        if (GUILayout.Button("Aç", EditorStyles.toolbarButton, GUILayout.Width(35)))
        {
            _expandAll = true;
            _foldouts.Clear();
        }
        if (GUILayout.Button("Kapat", EditorStyles.toolbarButton, GUILayout.Width(45)))
        {
            _expandAll = false;
            _foldouts.Clear();
        }

        if (GUILayout.Button("📁 + Grup", EditorStyles.toolbarButton, GUILayout.Width(65)))
        {
            AddEventToGroup("YeniGrup");
        }

        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        int dupe = CountDuplicateEntries();
        GUILayout.Label($"{_events.arraySize} Olay Kayıtlı", EditorStyles.miniLabel);
        if (dupe > 0)
        {
            var prev = GUI.color;
            GUI.color = new Color(1f, 0.4f, 0.4f);
            GUILayout.Label($"⚠ {dupe} İsim Çakışması", EditorStyles.miniLabel);
            GUI.color = prev;
        }
        EditorGUILayout.EndHorizontal();
    }

    private Node BuildTree()
    {
        var root = new Node { Name = "Root", FullGroupPath = "" };
        _pathCounts.Clear();
        RebuildDuplicateMap();

        for (int i = 0; i < _events.arraySize; i++)
        {
            var el = _events.GetArrayElementAtIndex(i);
            if (!Matches(el)) continue;

            string group = el.FindPropertyRelative("group").stringValue;
            var node = root;
            node.Total++;

            if (!string.IsNullOrEmpty(group))
            {
                string accumulated = "";
                foreach (string part in group.Split('/', StringSplitOptions.RemoveEmptyEntries))
                {
                    accumulated = string.IsNullOrEmpty(accumulated) ? part : $"{accumulated}/{part}";
                    if (!node.Children.TryGetValue(part, out var child))
                    {
                        child = new Node { Name = part, FullGroupPath = accumulated };
                        node.Children[part] = child;
                    }
                    node = child;
                    node.Total++;
                }
            }

            node.Leaves.Insert(0, i);
        }

        return root;
    }

    private void DrawNode(Node node, string key, int depth)
    {
        // 1. Önce bu gruba ait yaprakları girintili çiz
        for (int i = 0; i < node.Leaves.Count; i++)
            DrawEventCard(node.Leaves[i], depth);

        // 2. Ardından alt klasörleri derinliği 1 artırarak çiz
        foreach (var child in node.Children.Values.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase))
            DrawFolder(child, key + "/" + child.Name, depth + 1);
    }

    private void DrawFolder(Node node, string key, int depth)
    {
        bool open = _foldouts.TryGetValue(key, out bool v) ? v : (!string.IsNullOrEmpty(_search) || _expandAll);

        // Kademeli girinti payı
        float indentOffset = depth > 0 ? (depth - 1) * 14f : 0f;

        Rect totalRect = EditorGUILayout.GetControlRect(false, 22);

        // Hiyerarşi Sol Rehber Çizgisi
        if (depth > 1)
        {
            Rect lineRect = new Rect(totalRect.x + indentOffset - 6f, totalRect.y - 2, 2f, totalRect.height + 4);
            EditorGUI.DrawRect(lineRect, new Color(1f, 1f, 1f, 0.12f));
        }

        // Kademeli arka plan tonu
        Color headerColor = depth <= 1
            ? (EditorGUIUtility.isProSkin ? new Color(0.22f, 0.22f, 0.22f) : new Color(0.80f, 0.80f, 0.80f))
            : (EditorGUIUtility.isProSkin ? new Color(0.18f, 0.18f, 0.18f, 0.8f) : new Color(0.86f, 0.86f, 0.86f, 0.8f));

        Rect headerRect = new Rect(totalRect.x + indentOffset, totalRect.y, totalRect.width - indentOffset, totalRect.height);
        EditorGUI.DrawRect(headerRect, headerColor);

        // Yeniden Adlandırma (Rename) Modu
        if (_renamingGroupPath == node.FullGroupPath)
        {
            Rect renameRect = new Rect(headerRect.x + 24, headerRect.y + 2, headerRect.width - 95, 18);
            GUI.SetNextControlName("RenameField");
            _renameBuffer = EditorGUI.TextField(renameRect, _renameBuffer);
            EditorGUI.FocusTextInControl("RenameField");

            Rect okBtn = new Rect(headerRect.xMax - 66, headerRect.y + 2, 36, 18);
            Rect cancelBtn = new Rect(headerRect.xMax - 28, headerRect.y + 2, 24, 18);

            if (GUI.Button(okBtn, "Uygula", EditorStyles.miniButton) || (Event.current.isKey && Event.current.keyCode == KeyCode.Return))
            {
                ApplyGroupRename(node.FullGroupPath, _renameBuffer);
                _renamingGroupPath = null;
            }
            if (GUI.Button(cancelBtn, "✕", EditorStyles.miniButton))
            {
                _renamingGroupPath = null;
            }
        }
        else
        {
            string folderIcon = depth <= 1 ? "📁" : "└ 📁";
            Rect foldoutRect = new Rect(headerRect.x + 4, headerRect.y + 2, headerRect.width - 32, 18);

            open = EditorGUI.Foldout(foldoutRect, open, $"{folderIcon}  <b>{node.Name}</b> <color=grey>({node.Total})</color>", true,
                new GUIStyle(EditorStyles.foldout) { richText = true, fontStyle = FontStyle.Bold });
            _foldouts[key] = open;

            Rect menuBtnRect = new Rect(headerRect.xMax - 26, headerRect.y + 2, 22, 18);
            if (GUI.Button(menuBtnRect, "⋮", EditorStyles.miniButton))
            {
                ShowFolderMenu(node);
            }
        }

        HandleFolderRightClick(headerRect, node);

        if (open)
        {
            EditorGUILayout.Space(1);
            DrawNode(node, key, depth);
            EditorGUILayout.Space(2);
        }
    }

    private void DrawEventCard(int index, int depth)
    {
        var el = _events.GetArrayElementAtIndex(index);
        var nameProp = el.FindPropertyRelative("eventName");
        var persistentProp = el.FindPropertyRelative("isPersistent");
        var payloadProp = el.FindPropertyRelative("payload");

        string fullPath = FullPath(el);
        bool duplicate = !string.IsNullOrEmpty(fullPath) && _pathCounts.TryGetValue(fullPath, out int c) && c > 1;
        bool empty = string.IsNullOrEmpty(nameProp.stringValue);

        float indentOffset = depth * 14f;

        EditorGUILayout.BeginHorizontal();
        if (indentOffset > 0) GUILayout.Space(indentOffset);

        var prevBg = GUI.backgroundColor;
        if (duplicate || empty) GUI.backgroundColor = new Color(1f, 0.65f, 0.65f);

        Rect cardRect = EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        GUI.backgroundColor = prevBg;

        // 1. Satır: İsim + Kalıcılık + Menü
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField(empty ? "⚠" : "◆", GUILayout.Width(14));

        string oldFullPath = FullPath(el);

        string controlName = "EventField_" + index;
        GUI.SetNextControlName(controlName);

        EditorGUI.BeginChangeCheck();
        string newEventName = EditorGUILayout.TextField(nameProp.stringValue, EditorStyles.boldLabel, GUILayout.MinWidth(110));
        if (EditorGUI.EndChangeCheck())
        {
            nameProp.stringValue = newEventName;
            serializedObject.ApplyModifiedProperties();

            string newFullPath = FullPath(el);
            // İsim değiştiği anda sahnedeki [EventPublisher]/[EventListener] referanslarını da tazele
            if (!string.IsNullOrEmpty(oldFullPath) && !string.IsNullOrEmpty(newFullPath) && oldFullPath != newFullPath)
            {
                EventReferenceUpdater.UpdateAllReferences(oldFullPath, newFullPath);
            }
        }

        if (_focusTargetControl == controlName)
        {
            EditorGUI.FocusTextInControl(controlName);
            _focusTargetControl = "";
        }

        var persContent = new GUIContent(
            persistentProp.boolValue ? "💾" : "—",
            persistentProp.boolValue ? "Kalıcı Durum (Sahne geçişinde silinmez)" : "Geçici Durum");
        persistentProp.boolValue = GUILayout.Toggle(persistentProp.boolValue, persContent, EditorStyles.miniButton, GUILayout.Width(24));

        if (GUILayout.Button("⋮", EditorStyles.miniButton, GUILayout.Width(20)))
        {
            ShowEventContextMenu(index, nameProp.stringValue);
        }
        EditorGUILayout.EndHorizontal();

        // 2. Satır: Payload ve Varsayılan Değer
        EditorGUILayout.PropertyField(payloadProp, GUIContent.none, true);

        if (duplicate)
            EditorGUILayout.HelpBox($"'{fullPath}' zaten mevcut!", MessageType.Error);

        EditorGUILayout.EndVertical();
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.Space(1);

        HandleRowRightClick(cardRect, index, nameProp.stringValue);
    }

    private void ShowFolderMenu(Node node)
    {
        GenericMenu menu = new GenericMenu();

        menu.AddItem(new GUIContent("➕ Olay Ekle"), false, () =>
        {
            AddEventToGroup(node.FullGroupPath);
            _foldouts["root/" + node.FullGroupPath] = true;
        });

        menu.AddItem(new GUIContent("📁 Alt Grup Aç"), false, () =>
        {
            AddEventToGroup(node.FullGroupPath + "/AltGrup");
            _foldouts["root/" + node.FullGroupPath] = true;
        });

        menu.AddSeparator("");

        menu.AddItem(new GUIContent("✏️ Yeniden Adlandır"), false, () =>
        {
            _renamingGroupPath = node.FullGroupPath;
            _renameBuffer = node.Name;
        });

        menu.AddItem(new GUIContent("🗑️ Grubu Sil"), false, () =>
        {
            if (EditorUtility.DisplayDialog("Grubu Sil", $"'{node.Name}' grubunu ve içindeki {node.Total} olayı silmek istediğinizden emin misiniz?", "Evet, Sil", "İptal"))
            {
                DeleteGroup(node.FullGroupPath);
            }
        });

        menu.ShowAsContext();
    }

    private void HandleFolderRightClick(Rect rect, Node node)
    {
        Event e = Event.current;
        if (e.type == EventType.ContextClick && rect.Contains(e.mousePosition))
        {
            ShowFolderMenu(node);
            e.Use();
        }
    }

    private void ApplyGroupRename(string oldGroupPath, string newName)
    {
        if (string.IsNullOrWhiteSpace(newName)) return;

        string parentPath = "";
        int slash = oldGroupPath.LastIndexOf('/');
        if (slash >= 0) parentPath = oldGroupPath.Substring(0, slash);

        string newGroupPath = string.IsNullOrEmpty(parentPath) ? newName.Trim() : $"{parentPath}/{newName.Trim()}";

        for (int i = 0; i < _events.arraySize; i++)
        {
            var el = _events.GetArrayElementAtIndex(i);
            var groupProp = el.FindPropertyRelative("group");
            string g = groupProp.stringValue;

            if (g == oldGroupPath)
            {
                groupProp.stringValue = newGroupPath;
            }
            else if (g.StartsWith(oldGroupPath + "/"))
            {
                groupProp.stringValue = newGroupPath + g.Substring(oldGroupPath.Length);
            }
        }

        serializedObject.ApplyModifiedProperties();
        EditorUtility.SetDirty(target);
        Repaint();
    }

    private void DeleteGroup(string groupPath)
    {
        for (int i = _events.arraySize - 1; i >= 0; i--)
        {
            var el = _events.GetArrayElementAtIndex(i);
            string g = el.FindPropertyRelative("group").stringValue;
            if (g == groupPath || g.StartsWith(groupPath + "/"))
            {
                _events.DeleteArrayElementAtIndex(i);
            }
        }

        serializedObject.ApplyModifiedProperties();
        EditorUtility.SetDirty(target);
        Repaint();
    }

    private void AddEventToGroup(string groupPath)
    {
        _events.InsertArrayElementAtIndex(0);
        var el = _events.GetArrayElementAtIndex(0);
        el.FindPropertyRelative("group").stringValue = groupPath;
        el.FindPropertyRelative("eventName").stringValue = "YeniOlay";
        el.FindPropertyRelative("isPersistent").boolValue = false;
        el.FindPropertyRelative("payload").managedReferenceValue = new VoidPayload();

        _focusTargetControl = "EventField_0";

        serializedObject.ApplyModifiedProperties();
        EditorUtility.SetDirty(target);
        Repaint();
    }

    private void ShowEventContextMenu(int index, string eventName)
    {
        GenericMenu menu = new GenericMenu();
        string display = string.IsNullOrEmpty(eventName) ? "(isimsiz)" : eventName;
        menu.AddItem(new GUIContent($"'{display}' Olayını Sil"), false, () => SafeDelete(index, display));
        menu.ShowAsContext();
    }

    private void HandleRowRightClick(Rect rect, int index, string eventName)
    {
        Event e = Event.current;
        if (e.type == EventType.ContextClick && rect.Contains(e.mousePosition))
        {
            ShowEventContextMenu(index, eventName);
            e.Use();
        }
    }

    private void SafeDelete(int index, string eventName)
    {
        if (EditorUtility.DisplayDialog("Olayı Sil", $"'{eventName}' olayını silmek istediğinden emin misin?", "Sil", "İptal"))
        {
            _events.DeleteArrayElementAtIndex(index);
            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(target);
            Repaint();
        }
    }

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

    private int CountDuplicateEntries() => _pathCounts.Where(kv => kv.Value > 1).Sum(kv => kv.Value);

    private bool Matches(SerializedProperty el)
    {
        if (string.IsNullOrEmpty(_search)) return true;
        return Contains(el.FindPropertyRelative("eventName").stringValue)
            || Contains(el.FindPropertyRelative("group").stringValue)
            || Contains(FullPath(el));
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
}
#endif