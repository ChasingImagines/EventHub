#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Play'e girmeden, sahneler + prefab'lar + ScriptableObject'ler üzerindeki tüm
/// EventPublisher/EventListener bağlamalarını EventDatabase ile karşılaştırır.
/// Amaç: kod yazmadan, inspector'dan kurulan akışın editörde güvenli kalması.
/// </summary>
public class EventAuditWindow : EditorWindow
{
    private enum Severity { Error, Warning, Info }

    private class Finding
    {
        public Severity Severity;
        public string Channel;
        public string Message;
        public string Where;
        public UnityEngine.Object Context;
    }

    private class Binding
    {
        public string Channel;
        public Type Type;
        public bool IsPublisher;
        public string FieldName;
        public string Where;
        public UnityEngine.Object Context;
    }

    private readonly List<Finding> _findings = new();
    private readonly List<(string channel, int pub, int lis, bool known, bool persistent)> _channelSummary = new();
    private readonly Dictionary<Type, List<ChannelField>> _fieldCache = new();

    private Vector2 _scroll;
    private bool _onlyProblems = true;
    private bool _hasScanned;
    private string _status = "Tara düğmesine bas.";

    [MenuItem("Tools/Architecture/Event Audit")]
    private static void Open()
    {
        var win = GetWindow<EventAuditWindow>("Event Audit");
        win.minSize = new Vector2(520, 320);
    }

    private void OnGUI()
    {
        DrawToolbar();

        if (!_hasScanned)
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.HelpBox("Play moduna girmeden tüm kanal bağlamalarını denetlemek için \"Tara\".", MessageType.Info);
            return;
        }

        DrawSummary();

        _scroll = EditorGUILayout.BeginScrollView(_scroll);

        int errors = _findings.Count(f => f.Severity == Severity.Error);
        int warnings = _findings.Count(f => f.Severity == Severity.Warning);
        int infos = _findings.Count(f => f.Severity == Severity.Info);

        if (errors == 0 && warnings == 0 && (!_onlyProblems || infos == 0))
        {
            EditorGUILayout.HelpBox("Aktif bulgu yok. 👍", MessageType.Info);
        }

        DrawSection("HATALAR", Severity.Error, errors);

        if (!_onlyProblems || warnings > 0)
            DrawSection("UYARILAR", Severity.Warning, warnings);

        DrawSection("BİLGİ", Severity.Info, infos);

        EditorGUILayout.Space(8);
        DrawChannelSummary();

        EditorGUILayout.EndScrollView();
    }

    private void DrawToolbar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

        if (GUILayout.Button("Tara", EditorStyles.toolbarButton, GUILayout.Width(60)))
            Scan();

        _onlyProblems = GUILayout.Toggle(_onlyProblems, "Kısa liste", EditorStyles.toolbarButton, GUILayout.Width(75));

        GUILayout.FlexibleSpace();

        var db = EventDatabase.Instance;
        GUILayout.Label(db != null ? $"EventDatabase: {db.events.Count} kanal" : "EventDatabase YOK!", EditorStyles.miniLabel);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.LabelField(_status, EditorStyles.miniLabel);
    }

    private void DrawSummary()
    {
        int errors = _findings.Count(f => f.Severity == Severity.Error);
        int warnings = _findings.Count(f => f.Severity == Severity.Warning);
        int infos = _findings.Count(f => f.Severity == Severity.Info);

        EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
        Badge($"{errors} hata", errors > 0 ? new Color(1f, 0.5f, 0.5f) : Color.grey);
        Badge($"{warnings} uyarı", warnings > 0 ? new Color(1f, 0.8f, 0.4f) : Color.grey);
        Badge($"{infos} bilgi", Color.grey);
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();
    }

    private static void Badge(string text, Color color)
    {
        var prev = GUI.color;
        GUI.color = color;
        GUILayout.Label(text, EditorStyles.boldLabel, GUILayout.Width(90));
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

        string icon = f.Severity switch
        {
            Severity.Error => "<color=#FF5555>✖</color>",
            Severity.Warning => "<color=#FFB347>▲</color>",
            _ => "<color=grey>●</color>",
        };

        var rich = new GUIStyle(EditorStyles.label) { richText = true };
        GUILayout.Label($"{icon} <b>{f.Channel}</b>  <color=grey>{f.Message}</color>", rich);
        GUILayout.FlexibleSpace();

        if (f.Context != null && GUILayout.Button("Göster", EditorStyles.miniButton, GUILayout.Width(58)))
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

        EditorGUILayout.LabelField("Kanal Özeti", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("Tetikleyici / Dinleyici  —  kanal", EditorStyles.miniLabel);

        foreach (var (channel, pub, lis, known, persistent) in _channelSummary)
        {
            EditorGUILayout.BeginHorizontal();
            var prev = GUI.color;
            if (!known) GUI.color = new Color(1f, 0.8f, 0.4f);

            GUILayout.Label($"{pub,3} / {lis,-3}", EditorStyles.miniLabel, GUILayout.Width(60));
            GUILayout.Label(channel, EditorStyles.label);
            if (persistent) GUILayout.Label("💾", EditorStyles.miniLabel, GUILayout.Width(20));
            GUI.color = prev;
            EditorGUILayout.EndHorizontal();
        }
    }

    // ---------- Tarama ----------

    private void Scan()
    {
        _findings.Clear();
        _channelSummary.Clear();

        var bindings = new List<Binding>();
        ScanPrefabs(bindings);
        ScanScriptableObjects(bindings);
        ScanOpenScenes(bindings);

        Analyze(bindings);

        _hasScanned = true;
        _status = $"{bindings.Count} bağlama tarandı • {_channelSummary.Count} kanal";
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
                Extract(comp, path, bindings);
        }
    }

    private void ScanScriptableObjects(List<Binding> bindings)
    {
        foreach (var type in ScriptableObjectTypesWithChannels())
        {
            foreach (string guid in AssetDatabase.FindAssets($"t:{type.Name}"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var so = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
                if (so == null || !type.IsInstanceOfType(so)) continue;
                Extract(so, path, bindings);
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
                    Extract(comp, scene.name, bindings);
            }
        }
    }

    private void Extract(UnityEngine.Object obj, string whereBase, List<Binding> bindings)
    {
        if (obj == null) return;

        var fields = ChannelFields(obj.GetType());
        if (fields.Count == 0) return;

        string where = obj is Component comp && comp.transform.parent != null
            ? $"{whereBase} :: {HierarchyPath(comp.transform)}"
            : whereBase;

        foreach (var (field, type, isPublisher) in fields.Select(cf => (cf.field, cf.type, cf.isPublisher)))
        {
            bindings.Add(new Binding
            {
                Channel = field.GetValue(obj) as string ?? "",
                Type = type,
                IsPublisher = isPublisher,
                FieldName = field.Name,
                Where = where,
                Context = obj,
            });
        }
    }

    private void Analyze(List<Binding> bindings)
    {
        var db = EventDatabase.Instance;

        // DB çakışmaları
        if (db != null)
        {
            foreach (var grp in db.events.Where(e => !string.IsNullOrEmpty(e.FullPath)).GroupBy(e => e.FullPath))
            {
                if (grp.Count() > 1)
                    Add(Severity.Error, grp.Key, $"{grp.Count()} kez tanımlı (çakışma)", "", null);
            }
        }

        // Bağlama bazlı kontroller
        var seen = new HashSet<string>();
        foreach (var b in bindings)
        {
            string key = $"{b.IsPublisher}|{b.FieldName}|{b.Channel}|{b.Type}|{b.Where}";
            if (!seen.Add(key)) continue;

            string role = b.IsPublisher ? "üretici" : "dinleyici";

            if (string.IsNullOrEmpty(b.Channel))
            {
                Add(Severity.Error, "(boş)", $"Kanal seçilmemiş — {b.FieldName} ({role})", b.Where, b.Context);
                continue;
            }

            if (db == null) continue;

            if (!db.TryGetExpectedType(b.Channel, out Type expected))
            {
                Add(Severity.Warning, b.Channel,
                    $"Veritabanında tanımlı değil — {b.FieldName} ({role})", b.Where, b.Context);
                continue;
            }

            Type fieldType = b.Type;
            if (expected != fieldType)
            {
                Add(Severity.Error, b.Channel,
                    $"Tip uyuşmazlığı — alan {Name(fieldType)}, veritabanı {Name(expected)} ({b.FieldName})",
                    b.Where, b.Context);
            }
        }

        // Kanal özeti + ölü kanal tespiti
        var grouped = bindings.Where(b => !string.IsNullOrEmpty(b.Channel))
                              .GroupBy(b => b.Channel)
                              .ToDictionary(g => g.Key, g => g.ToList());

        if (db != null)
        {
            foreach (var ev in db.events.Where(e => !string.IsNullOrEmpty(e.FullPath)))
            {
                if (_channelSummary.Any(c => c.channel == ev.FullPath)) continue;

                grouped.TryGetValue(ev.FullPath, out var list);
                int pub = list?.Count(b => b.IsPublisher) ?? 0;
                int lis = list?.Count(b => !b.IsPublisher) ?? 0;

                _channelSummary.Add((ev.FullPath, pub, lis, true, ev.isPersistent));

                if (pub == 0)
                    Add(Severity.Info, ev.FullPath, "Hiç tetikleyici (publisher) yok", "", null);
                if (lis == 0)
                    Add(Severity.Info, ev.FullPath, "Hiç dinleyici (listener) yok", "", null);
            }
        }

        // DB'de olmayan ama kullanılan kanallar da özete girsin
        foreach (var (channel, list) in grouped)
        {
            if (_channelSummary.Any(c => c.channel == channel)) continue;
            _channelSummary.Add((channel, list.Count(b => b.IsPublisher), list.Count(b => !b.IsPublisher), false, false));
        }

        // Aynı kanalı hem void hem tipli kullanan var mı?
        foreach (var (channel, list) in grouped)
        {
            var types = list.Select(b => b.Type).Distinct().ToList();
            if (types.Count > 1)
                Add(Severity.Error, channel,
                    $"Aynı kanal {types.Count} farklı tiple kullanılıyor: {string.Join(", ", types.Select(Name))}",
                    "", null);
        }

        _channelSummary.Sort((a, b) => string.Compare(a.channel, b.channel, StringComparison.OrdinalIgnoreCase));
    }

    private void Add(Severity s, string channel, string message, string where, UnityEngine.Object ctx)
        => _findings.Add(new Finding { Severity = s, Channel = channel, Message = message, Where = where, Context = ctx });

    private static string Name(Type t) => t == null || t == typeof(void) ? "void" : t.Name;

    private static string HierarchyPath(Transform t)
    {
        string path = t.name;
        while (t.parent != null)
        {
            t = t.parent;
            path = t.name + "/" + path;
        }
        return path;
    }

    // ---------- Yansıma önbelleği ----------

    private List<ChannelField> ChannelFields(Type type)
    {
        if (_fieldCache.TryGetValue(type, out var cached))
            return cached;

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

    private static IEnumerable<Type> ScriptableObjectTypesWithChannels()
    {
        foreach (var t in TypeCache.GetTypesDerivedFrom<ScriptableObject>())
        {
            if (t.IsAbstract) continue;
            if (t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                 .Any(f => f.FieldType == typeof(string) &&
                           (f.GetCustomAttribute<EventPublisherAttribute>() != null ||
                            f.GetCustomAttribute<EventListenerAttribute>() != null)))
            {
                yield return t;
            }
        }
    }
}
#endif
