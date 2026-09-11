#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

[CustomPropertyDrawer(typeof(EventChannelAttribute), true)]
public class EventChannelDrawer : PropertyDrawer
{
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        if (property.propertyType != SerializedPropertyType.String)
        {
            EditorGUI.PropertyField(position, property, label);
            return;
        }

        var channelAttr = attribute as EventChannelAttribute;
        Type targetType = channelAttr?.ExpectedType ?? typeof(void);

        var db = EventDatabase.Instance;
        if (db == null || db.events.Count == 0)
        {
            EditorGUI.HelpBox(position, "EventDatabase bulunamadı!", MessageType.Warning);
            return;
        }

        string currentVal = property.stringValue;
        bool isEmpty = string.IsNullOrEmpty(currentVal);
        bool exists = db.TryGetExpectedType(currentVal, out Type currentDbType);

        // Hata durumları
        bool isMissing = !isEmpty && !exists;
        bool isTypeMismatch = !isEmpty && exists && currentDbType != targetType;

        Color originalBg = GUI.backgroundColor;
        string displayLabel;

        if (isEmpty)
        {
            GUI.backgroundColor = new Color(1f, 0.9f, 0.4f); // Boş bırakılmışsa sarı dikkat uyarısı
            displayLabel = $"⚠ Event Seçilmedi! [{targetType.Name}]";
        }
        else if (isMissing)
        {
            GUI.backgroundColor = new Color(1f, 0.3f, 0.3f); // DB'den silinmişse kırmızı
            displayLabel = $"❌ Kayıp Olay: '{currentVal}'";
        }
        else if (isTypeMismatch)
        {
            GUI.backgroundColor = new Color(1f, 0.4f, 0.4f); // Tip uyuşmazlığı
            displayLabel = $"❌ Tip Hatası: {currentVal} (Beklenen: {targetType.Name})";
        }
        else
        {
            displayLabel = $"{currentVal} [{targetType.Name}]";
        }

        position = EditorGUI.PrefixLabel(position, label);

        if (EditorGUI.DropdownButton(position, new GUIContent(displayLabel), FocusType.Keyboard))
        {
            // Asenkron referans kaybını önlemek için SerializedObject ve path'i sabitle
            SerializedObject targetSerializedObject = property.serializedObject;
            string propertyPath = property.propertyPath;

            var dropdown = new EventSearchDropdown(new AdvancedDropdownState(), db, targetType, selectedPath =>
            {
                targetSerializedObject.Update();
                var prop = targetSerializedObject.FindProperty(propertyPath);
                if (prop != null)
                {
                    prop.stringValue = selectedPath;
                    targetSerializedObject.ApplyModifiedProperties();
                }
            });

            dropdown.Show(position);
        }

        GUI.backgroundColor = originalBg;
    }
}

public class EventSearchDropdown : AdvancedDropdown
{
    private readonly EventDatabase _db;
    private readonly Type _filterType;
    private readonly Action<string> _onSelect;

    public EventSearchDropdown(AdvancedDropdownState state, EventDatabase db, Type filterType, Action<string> onSelect) : base(state)
    {
        _db = db;
        _filterType = filterType;
        _onSelect = onSelect;
        minimumSize = new Vector2(280, 320);
    }

    protected override AdvancedDropdownItem BuildRoot()
    {
        var root = new AdvancedDropdownItem($"Olaylar ({_filterType.Name})");

        // 1. Seçimi Temizleme Seçeneği
        root.AddChild(new EventDropdownItem("<Hiçbiri / Temizle>", ""));

        int matchingCount = 0;

        foreach (var evt in _db.events)
        {
            // Sadece beklenen tipe uyan event'leri menüye ekle
            if (evt.ExpectedType != _filterType) continue;

            matchingCount++;
            string fullPath = evt.FullPath;
            string[] parts = fullPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            AdvancedDropdownItem currentParent = root;

            for (int i = 0; i < parts.Length - 1; i++)
            {
                string folder = parts[i];
                var child = FindChild(currentParent, folder);
                if (child == null)
                {
                    child = new AdvancedDropdownItem(folder);
                    currentParent.AddChild(child);
                }
                currentParent = child;
            }

            var leaf = new EventDropdownItem(parts[^1], fullPath);
            currentParent.AddChild(leaf);
        }

        if (matchingCount == 0)
        {
            root.AddChild(new AdvancedDropdownItem($"[Uygun {_filterType.Name} tipinde olay yok]") { enabled = false });
        }

        return root;
    }

    private AdvancedDropdownItem FindChild(AdvancedDropdownItem parent, string name)
    {
        foreach (var child in parent.children)
        {
            if (child.name == name) return child;
        }
        return null;
    }

    protected override void ItemSelected(AdvancedDropdownItem item)
    {
        if (item is EventDropdownItem eventItem)
        {
            _onSelect?.Invoke(eventItem.FullPath);
        }
    }

    private class EventDropdownItem : AdvancedDropdownItem
    {
        public string FullPath { get; }
        public EventDropdownItem(string displayName, string fullPath) : base(displayName)
        {
            FullPath = fullPath;
        }
    }
}
#endif