#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

public class EventAuditWindow : EditorWindow
{
    private enum Severity { Error, Warning }

    private class Finding
    {
        public Severity Severity;
        public string Channel;
        public string Message;
        public string Where;
        public UnityEngine.Object Context;
        public Type MissingType;
    }

    private class Binding
    {
        public string Channel;
        public Type Type;
        public bool IsPublisher;
        public string FieldName;
        public string Where;
        public UnityEngine.Object Context;
        public bool IsPrefab;
    }

    private readonly List<Finding> _findings = new();
    private readonly List<(string channel, int pub, int lis, bool known, bool persistent, string defVal)> _channelSummary = new();
    private readonly Dictionary<Type, List<ChannelField>> _fieldCache = new();

    private Vector2 _scroll;
    private bool _hasScanned;
    private string _status = "Tara butonuna basınız.";

    [MenuItem("Tools/Architecture/Event Audit")]
    private static void Open()
    {
        var win = GetWindow<EventAuditWindow>("Event Audit");
        win.minSize = new Vector2(560, 340);
    }

    private void OnGUI()
    {
        DrawToolbar();

        if (!_hasScanned)
        {
            EditorGUILayout.Space(10);
            EditorGUILayout.HelpBox("Sahne, prefab ve ScriptableObject'lerdeki olay sözleşmelerini denetlemek için 'Tara' butonuna basınız.", MessageType.Info);
            return;
        }

        DrawSummary();

        _scroll = EditorGUILayout.BeginScrollView(_scroll);

        int errors = _findings.Count(f => f.Severity == Severity.Error);
        int warnings = _findings.Count(f => f.Severity == Severity.Warning);

        if (errors == 0 && warnings == 0)
        {
            EditorGUILayout.HelpBox("Tebrikler! Hiçbir eksik veya uyumsuz olay bağlaması bulunamadı. 👍", MessageType.Info);
        }

        DrawSection("HATALAR", Severity.Error, errors);
        DrawSection("UYARILAR", Severity.Warning, warnings);

        EditorGUILayout.Space(10);
        DrawChannelSummary();

        EditorGUILayout.EndScrollView();
    }

    private void DrawToolbar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

        if (GUILayout.Button("🔍 Projeyi Tara", EditorStyles.toolbarButton, GUILayout.Width(100)))
            Scan();

        GUILayout.FlexibleSpace();

        var db = EventDatabase.Instance;
        GUILayout.Label(db != null ? $"EventDatabase: {db.events.Count} Kayıt" : "⚠ EventDatabase Bulunamadı!", EditorStyles.miniLabel);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.LabelField(_status, EditorStyles.miniLabel);
    }

    private void DrawSummary()
    {
        int errors = _findings.Count(f => f.Severity == Severity.Error);
        int warnings = _findings.Count(f => f.Severity == Severity.Warning);

        EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
        Badge($"{errors} Hata", errors > 0 ? new Color(1f, 0.45f, 0.45f) : Color.grey);
        Badge($"{warnings} Uyarı", warnings > 0 ? new Color(1f, 0.85f, 0.4f) : Color.grey);
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();
    }

    private static void Badge(string text, Color color)
    {
        var prev = GUI.color;
        GUI.color = color;
        GUILayout.Label(text, EditorStyles.boldLabel, GUILayout.Width(100));
        GUI.color = prev;
    }

    private void DrawSection(string title, Severity severity, int count)
    {
        if (count == 0) return;

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField($"{title} ({count})", EditorStyles.boldLabel);

        foreach (var f in _findings.Where(f => f.Severity == severity))
            DrawFinding(f);
    }

    private void DrawFinding(Finding f)
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.BeginHorizontal();

        string icon = f.Severity == Severity.Error ? "<color=#FF5555>✖</color>" : "<color=#FFB347>▲</color>";
        var rich = new GUIStyle(EditorStyles.label) { richText = true };

        GUILayout.Label($"{icon} <b>{f.Channel}</b>  <color=grey>{f.Message}</color>", rich);
        GUILayout.FlexibleSpace();

        // Hızlı Çözüm: DB'ye ekle butonu
        if (f.MissingType != null && GUILayout.Button("➕ DB'ye Ekle", EditorStyles.miniButton, GUILayout.Width(85)))
        {
            AddMissingEventToDb(f.Channel, f.MissingType);
            Scan();
            GUIUtility.ExitGUI();
        }

        if (f.Context != null && GUILayout.Button("Göster", EditorStyles.miniButton, GUILayout.Width(50)))
        {
            Selection.activeObject = f.Context;
            EditorGUIUtility.PingObject(f.Context);
        }

        EditorGUILayout.EndHorizontal();

        if (!string.IsNullOrEmpty(f.Where))
            EditorGUILayout.LabelField(f.Where, EditorStyles.miniLabel);

        EditorGUILayout.EndVertical();
    }

    private void DrawChannelSummary()
    {
        if (_channelSummary.Count == 0) return;

        EditorGUILayout.LabelField("Kayıtlı Kanalların Kullanım Özeti", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("Tetikleyici / Dinleyici  —  Kanal Yolu", EditorStyles.miniLabel);

        foreach (var item in _channelSummary)
        {
            EditorGUILayout.BeginHorizontal();
            var prev = GUI.color;
            if (!item.known) GUI.color = new Color(1f, 0.8f, 0.4f);

            GUILayout.Label($"{item.pub,3} / {item.lis,-3}", EditorStyles.miniLabel, GUILayout.Width(60));
            GUILayout.Label(item.channel, EditorStyles.label);
            if (!string.IsNullOrEmpty(item.defVal)) GUILayout.Label($"[Varsayılan: {item.defVal}]", EditorStyles.miniLabel);
            if (item.persistent) GUILayout.Label("💾", EditorStyles.miniLabel, GUILayout.Width(20));

            GUI.color = prev;
            EditorGUILayout.EndHorizontal();
        }
    }

    private void Scan()
    {
        _findings.Clear();
        _channelSummary.Clear();

        var bindings = new List<Binding>();
        ScanOpenScenes(bindings);
        ScanScriptableObjects(bindings);
        ScanPrefabs(bindings);

        Analyze(bindings);

        _hasScanned = true;
        _status = $"{bindings.Count} bağlama tarandı • {_channelSummary.Count} kanal listelendi.";
        Repaint();
    }

    private void ScanPrefabs(List<Binding> bindings)
    {
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (go == null) continue;

            foreach (var comp in go.GetComponentsInChildren<Component>(true))
                Extract(comp, path, bindings, true);
        }
    }

    private void ScanScriptableObjects(List<Binding> bindings)
    {
        foreach (var type in TypeCache.GetTypesDerivedFrom<ScriptableObject>())
        {
            if (type.IsAbstract) continue;
            foreach (string guid in AssetDatabase.FindAssets($"t:{type.Name}"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var so = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
                if (so != null) Extract(so, path, bindings, false);
            }
        }
    }

    private void ScanOpenScenes(List<Binding> bindings)
    {
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            if (!scene.isLoaded) continue;

            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var comp in root.GetComponentsInChildren<Component>(true))
                    Extract(comp, scene.name, bindings, false);
            }
        }
    }

    private void Extract(UnityEngine.Object obj, string whereBase, List<Binding> bindings, bool isPrefab)
    {
        if (obj == null) return;

        var fields = ChannelFields(obj.GetType());
        if (fields.Count == 0) return;

        string where = obj is Component comp && comp.transform.parent != null
            ? $"{whereBase} -> {comp.gameObject.name}"
            : whereBase;

        foreach (var cf in fields)
        {
            bindings.Add(new Binding
            {
                Channel = cf.field.GetValue(obj) as string ?? "",
                Type = cf.type,
                IsPublisher = cf.isPublisher,
                FieldName = cf.field.Name,
                Where = where,
                Context = obj,
                IsPrefab = isPrefab
            });
        }
    }

    private void Analyze(List<Binding> bindings)
    {
        var db = EventDatabase.Instance;

        // DB içinde çakışan aynı isimli tanımlar
        if (db != null)
        {
            foreach (var grp in db.events.Where(e => !string.IsNullOrEmpty(e.FullPath)).GroupBy(e => e.FullPath))
            {
                if (grp.Count() > 1)
                    Add(Severity.Error, grp.Key, $"{grp.Count()} kez tanımlanmış (İsim Çakışması!)", "", null, null);
            }
        }

        var seen = new HashSet<string>();
        foreach (var b in bindings)
        {
            string key = $"{b.IsPublisher}|{b.FieldName}|{b.Channel}|{b.Type}|{b.Where}";
            if (!seen.Add(key)) continue;

            string role = b.IsPublisher ? "üretici" : "dinleyici";

            if (string.IsNullOrEmpty(b.Channel))
            {
                // Sahnede aktifse HATA, sadece diskte bekleyen prefab şablonuysa UYARI
                var sev = b.IsPrefab ? Severity.Warning : Severity.Error;
                Add(sev, "(boş)", $"Kanal seçilmemiş — {b.FieldName} ({role})", b.Where, b.Context, null);
                continue;
            }

            if (db == null) continue;

            if (!db.TryGetExpectedType(b.Channel, out Type expected))
            {
                Add(Severity.Warning, b.Channel,
                    $"Veritabanında kayıtlı değil — {b.FieldName} ({role})", b.Where, b.Context, b.Type);
                continue;
            }

            if (expected != b.Type)
            {
                Add(Severity.Error, b.Channel,
                    $"Tip uyuşmazlığı — Alan: {Name(b.Type)}, DB: {Name(expected)} ({b.FieldName})",
                    b.Where, b.Context, null);
            }
        }

        // Özet Listesi
        var grouped = bindings.Where(b => !string.IsNullOrEmpty(b.Channel))
                              .GroupBy(b => b.Channel)
                              .ToDictionary(g => g.Key, g => g.ToList());

        if (db != null)
        {
            foreach (var ev in db.events.Where(e => !string.IsNullOrEmpty(e.FullPath)))
            {
                grouped.TryGetValue(ev.FullPath, out var list);
                int pub = list?.Count(b => b.IsPublisher) ?? 0;
                int lis = list?.Count(b => !b.IsPublisher) ?? 0;
                object defVal = ev.payload?.GetDefaultRawValue();

                _channelSummary.Add((ev.FullPath, pub, lis, true, ev.isPersistent, defVal != null ? defVal.ToString() : ""));
            }
        }

        _channelSummary.Sort((a, b) => string.Compare(a.channel, b.channel, StringComparison.OrdinalIgnoreCase));
    }

    private void Add(Severity s, string channel, string message, string where, UnityEngine.Object ctx, Type missingType)
        => _findings.Add(new Finding { Severity = s, Channel = channel, Message = message, Where = where, Context = ctx, MissingType = missingType });

    private static string Name(Type t) => t == null || t == typeof(void) ? "Void" : t.Name;

    private List<ChannelField> ChannelFields(Type type)
    {
        if (_fieldCache.TryGetValue(type, out var cached)) return cached;

        var result = new List<ChannelField>();
        foreach (var f in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (f.FieldType != typeof(string)) continue;

            var publisher = f.GetCustomAttribute<EventPublisherAttribute>();
            var listener = f.GetCustomAttribute<EventListenerAttribute>();
            if (publisher == null && listener == null) continue;

            EventChannelAttribute attr = publisher ?? (EventChannelAttribute)listener;
            result.Add(new ChannelField { field = f, type = attr.ExpectedType, isPublisher = publisher != null });
        }

        _fieldCache[type] = result;
        return result;
    }

    private class ChannelField
    {
        public FieldInfo field;
        public Type type;
        public bool isPublisher;
    }

    private void AddMissingEventToDb(string channelPath, Type type)
    {
        var db = EventDatabase.Instance;
        if (db == null) return;

        string group = "";
        string name = channelPath;
        int lastSlash = channelPath.LastIndexOf('/');
        if (lastSlash >= 0)
        {
            group = channelPath.Substring(0, lastSlash);
            name = channelPath.Substring(lastSlash + 1);
        }

        IEventPayload payload;
        if (type == typeof(int)) payload = new IntPayload();
        else if (type == typeof(float)) payload = new FloatPayload();
        else if (type == typeof(bool)) payload = new BoolPayload();
        else if (type == typeof(string)) payload = new StringPayload();
        else if (type == typeof(Vector3)) payload = new Vector3Payload();
        else payload = new VoidPayload();

        var newDef = new EventDatabase.EventDefinition
        {
            group = group,
            eventName = name,
            payload = payload,
            isPersistent = false
        };

        db.events.Insert(0, newDef);
        EditorUtility.SetDirty(db);
        AssetDatabase.SaveAssets();
    }
}
#endif