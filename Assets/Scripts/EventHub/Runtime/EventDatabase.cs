using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "EventDatabase", menuName = "Architecture/Event Database")]
public class EventDatabase : ScriptableObject
{
    [Serializable]
    public class EventDefinition
    {
        public string group;
        public string eventName;

        [Tooltip("İşaretlenirse bu kanalın son değeri sahne değişimlerinde silinmez (RAM'de korunur).")]
        public bool isPersistent = false;

        [SerializeReference, SubclassSelector]
        public IEventPayload payload = new VoidPayload();

        public string FullPath => string.IsNullOrEmpty(group) ? eventName : $"{group}/{eventName}";
        public Type ExpectedType => payload != null ? payload.DataType : typeof(void);
    }

    public List<EventDefinition> events = new();

    // Serileştirilen veri yukarıdaki liste olarak kalır (Unity Dictionary serialize edemez).
    // Aramalar için listenin türevi bir indeks, ilk kullanımda kurulur ve liste değişince yenilenir.
    private Dictionary<string, EventDefinition> _index;
    private int _indexCount = -1;

    private Dictionary<string, EventDefinition> Index
    {
        get
        {
            if (_index != null && _indexCount == events.Count) return _index;

            _index = new Dictionary<string, EventDefinition>(events.Count);
            _indexCount = events.Count;
            foreach (var e in events)
            {
                string path = e.FullPath ?? "";   // null anahtar Dictionary'de exception atar
                // List.Find ile aynı davranış: aynı yol birden fazla tanımlıysa ilki kazanır.
                if (!_index.ContainsKey(path)) _index[path] = e;
            }
            return _index;
        }
    }

    private void OnValidate()
    {
        _index = null;
        _indexCount = -1;
    }

    private static EventDatabase _cachedInstance;
    public static EventDatabase Instance
    {
        get
        {
            if (_cachedInstance == null) _cachedInstance = Resources.Load<EventDatabase>("EventDatabase");
            return _cachedInstance;
        }
    }

    public Type GetExpectedType(string fullPath)
    {
        return Index.TryGetValue(fullPath ?? "", out var match) ? match.ExpectedType : typeof(void);
    }

    public bool TryGetExpectedType(string fullPath, out Type expectedType)
    {
        if (!Index.TryGetValue(fullPath ?? "", out var match))
        {
            expectedType = typeof(void);
            return false;
        }

        expectedType = match.ExpectedType;
        return true;
    }

    public bool IsPersistent(string fullPath)
    {
        return Index.TryGetValue(fullPath ?? "", out var match) && match.isPersistent;
    }
}
