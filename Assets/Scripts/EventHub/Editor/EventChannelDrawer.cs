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
        if (db == null)
        {
            EditorGUI.HelpBox(position, "⚠ EventDatabase asset'i bulunamadı! (Resources/EventDatabase)", MessageType.Warning);
            return;
        }

        string currentVal = property.stringValue;
        bool isEmpty = string.IsNullOrEmpty(currentVal);
        bool exists = db.TryGetDefinition(currentVal, out var definition);
        Type currentDbType = exists ? definition.ExpectedType : null;

        // Validasyon / Hata Durumları
        bool isMissing = !isEmpty && !exists;
        bool isTypeMismatch = !isEmpty && exists && currentDbType != targetType;

        Color originalBg = GUI.backgroundColor;
        string displayLabel;
        string tooltipText = "";

        if (isEmpty)
        {
            GUI.backgroundColor = new Color(1f, 0.88f, 0.4f); // Boş: Sarı uyarı
            displayLabel = $"[Seçilmedi] -> ({targetType.Name})";
            tooltipText = "Lütfen bir olay kanalı seçin.";
        }
        else if (isMissing)
        {
            GUI.backgroundColor = new Color(1f, 0.4f, 0.4f); // DB'de yok: Kırmızı
            displayLabel = $"❌ Kayıp Olay: '{currentVal}'";
            tooltipText = "Bu kanal EventDatabase'den silinmiş veya adı değiştirilmiş!";
        }
        else if (isTypeMismatch)
        {
            GUI.backgroundColor = new Color(1f, 0.45f, 0.2f); // Tip uyuşmazlığı: Turuncu
            string actualTypeName = currentDbType != null ? currentDbType.Name : "Bilinmiyor";
            displayLabel = $"❌ Tip Hatası: {currentVal} ({actualTypeName} != {targetType.Name})";
            tooltipText = $"Bu kanal '{actualTypeName}' taşıyor ancak alan '{targetType.Name}' bekliyor!";
        }
        else
        {
            // Başarılı eşleşme: Varsa varsayılan değeri de göster
            object defVal = definition?.payload?.GetDefaultRawValue();
            string valSuffix = defVal != null ? $" [Varsayılan: {defVal}]" : "";
            displayLabel = $"{currentVal}{valSuffix}";
            tooltipText = $"Kanal: {currentVal}\nTip: {targetType.Name}\nKalıcı mı: {(definition.isPersistent ? "Evet (💾)" : "Hayır")}";
        }

        // Label ve Buton ayrımı
        position = EditorGUI.PrefixLabel(position, label);

        var buttonContent = new GUIContent(displayLabel, tooltipText);
        if (EditorGUI.DropdownButton(position, buttonContent, FocusType.Keyboard))
        {
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
        minimumSize = new Vector2(300, 340);
    }

    protected override AdvancedDropdownItem BuildRoot()
    {
        string rootTitle = _filterType == typeof(void) ? "Parametresiz Olaylar (Void)" : $"Olaylar ({_filterType.Name})";
        var root = new AdvancedDropdownItem(rootTitle);

        // 1. Temizleme seçeneği
        root.AddChild(new EventDropdownItem("✕ <Seçimi Temizle>", ""));

        int matchingCount = 0;

        if (_db.events != null)
        {
            for (int e = 0; e < _db.events.Count; e++)
            {
                var evt = _db.events[e];
                if (evt == null || evt.ExpectedType == null) continue;

                // Tip filtresi: Sadece hedef tipi taşıyanları listele
                if (evt.ExpectedType != _filterType) continue;

                matchingCount++;
                string fullPath = evt.FullPath;
                if (string.IsNullOrEmpty(fullPath)) continue;

                string[] parts = fullPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
                AdvancedDropdownItem currentParent = root;

                // Hiyerarşik klasör yapısı
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

                // Olay yaprağı (Varsayılan değer bilgisiyle)
                object defVal = evt.payload?.GetDefaultRawValue();
                string leafName = parts[^1] + (defVal != null ? $"  ({defVal})" : "") + (evt.isPersistent ? " 💾" : "");

                var leaf = new EventDropdownItem(leafName, fullPath);
                currentParent.AddChild(leaf);
            }
        }

        if (matchingCount == 0)
        {
            root.AddChild(new AdvancedDropdownItem($"[Uygun '{_filterType.Name}' tipinde olay bulunamadı]") { enabled = false });
        }

        return root;
    }

    private AdvancedDropdownItem FindChild(AdvancedDropdownItem parent, string name)
    {
        if (parent.children == null) return null;
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