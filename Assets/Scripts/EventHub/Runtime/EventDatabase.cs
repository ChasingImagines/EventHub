using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "EventDatabase", menuName = "Architecture/Event Database")]
public class EventDatabase : ScriptableObject
{
    [Serializable]
    public class EventDefinition
    {
        public string group = "";
        public string eventName = "";

        [Tooltip("İşaretlenirse bu kanalın son değeri sahne değişimlerinde silinmez (RAM'de korunur).")]
        public bool isPersistent = false;

        [SerializeReference, SubclassSelector]
        public IEventPayload payload = new VoidPayload();

        public string FullPath => string.IsNullOrEmpty(group) ? eventName : $"{group}/{eventName}";
        public Type ExpectedType => payload != null ? payload.DataType : typeof(void);
    }

    public List<EventDefinition> events = new();

    private Dictionary<string, EventDefinition> _index;
    private int _indexCount = -1;

    private Dictionary<string, EventDefinition> Index
    {
        get
        {
            if (_index != null && _indexCount == events.Count) return _index;

            _index = new Dictionary<string, EventDefinition>(events.Count);
            _indexCount = events.Count;
            for (int i = 0; i < events.Count; i++)
            {
                var def = events[i];
                string path = def.FullPath ?? "";
                if (!_index.ContainsKey(path)) _index[path] = def;
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

    public bool TryGetDefinition(string fullPath, out EventDefinition def)
    {
        return Index.TryGetValue(fullPath ?? "", out def);
    }

    public Type GetExpectedType(string fullPath)
    {
        return Index.TryGetValue(fullPath ?? "", out var def) ? def.ExpectedType : typeof(void);
    }

    public bool TryGetExpectedType(string fullPath, out Type expectedType)
    {
        if (Index.TryGetValue(fullPath ?? "", out var def))
        {
            expectedType = def.ExpectedType;
            return true;
        }
        expectedType = typeof(void);
        return false;
    }

    public bool IsPersistent(string fullPath)
    {
        return Index.TryGetValue(fullPath ?? "", out var def) && def.isPersistent;
    }
}