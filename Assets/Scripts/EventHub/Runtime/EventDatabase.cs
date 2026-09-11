using System;
using System.Collections.Generic;
using UnityEngine;
using MackySoft.SerializeReferenceExtensions;

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
        var match = events.Find(e => e.FullPath == fullPath);
        return match?.ExpectedType ?? typeof(void);
    }

    public bool TryGetExpectedType(string fullPath, out Type expectedType)
    {
        var match = events.Find(e => e.FullPath == fullPath);
        if (match == null)
        {
            expectedType = typeof(void);
            return false;
        }

        expectedType = match.ExpectedType;
        return true;
    }

    public bool IsPersistent(string fullPath)
    {
        var match = events.Find(e => e.FullPath == fullPath);
        return match != null && match.isPersistent;
    }
}