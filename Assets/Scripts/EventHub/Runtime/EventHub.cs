using System;
using System.Collections.Generic;
using UnityEngine;

public static class EventHub
{
#if UNITY_EDITOR
    // Editör pencerelerinin (EventMatrixWindow) anlık tetiklemeleri yakalaması için
    public static event Action<string, object> OnEventRaisedInEditor;
#endif

    private class StateEntry
    {
        public object Value;
        public bool HasValue;
        public bool IsPersistent;
    }

    // Dinleyici abonelikleri
    private static readonly Dictionary<string, Delegate> _subscribers = new(StringComparer.OrdinalIgnoreCase);

    // Blackboard canlı durum deposu
    private static readonly Dictionary<string, StateEntry> _states = new(StringComparer.OrdinalIgnoreCase);

    // --- State Yönetimi (Blackboard) ---

    public static bool TryGetRawState(string channel, out object rawValue, out bool hasValue, out bool isPersistent)
    {
        rawValue = null;
        hasValue = false;
        isPersistent = false;

        if (string.IsNullOrEmpty(channel)) return false;

        if (_states.TryGetValue(channel, out var entry))
        {
            rawValue = entry.Value;
            hasValue = entry.HasValue;
            isPersistent = entry.IsPersistent;
            return true;
        }

        return false;
    }

    public static bool HasValue(string channel)
    {
        return !string.IsNullOrEmpty(channel) && _states.TryGetValue(channel, out var e) && e.HasValue;
    }

    public static bool TryGet<T>(string channel, out T value)
    {
        value = default;
        if (string.IsNullOrEmpty(channel)) return false;

        if (_states.TryGetValue(channel, out var entry) && entry.HasValue)
        {
            if (entry.Value is T casted)
            {
                value = casted;
                return true;
            }
        }
        return false;
    }

    public static void SetState<T>(string channel, T value, bool isPersistent = false)
    {
        if (string.IsNullOrEmpty(channel)) return;

        if (!_states.TryGetValue(channel, out var entry))
        {
            entry = new StateEntry();
            _states[channel] = entry;
        }

        entry.Value = value;
        entry.HasValue = true;
        entry.IsPersistent = isPersistent;
    }

    public static void Invalidate(string channel)
    {
        if (string.IsNullOrEmpty(channel)) return;
        if (_states.TryGetValue(channel, out var entry))
        {
            entry.HasValue = false;
            entry.Value = null;
        }
    }

    // --- Generic Pub / Sub ---

    public static void Subscribe<T>(string channel, Action<T> callback)
    {
        if (string.IsNullOrEmpty(channel) || callback == null) return;

        if (_subscribers.TryGetValue(channel, out var existing))
            _subscribers[channel] = Delegate.Combine(existing, callback);
        else
            _subscribers[channel] = callback;
    }

    public static void Unsubscribe<T>(string channel, Action<T> callback)
    {
        if (string.IsNullOrEmpty(channel) || callback == null) return;

        if (_subscribers.TryGetValue(channel, out var existing))
        {
            var current = Delegate.Remove(existing, callback);
            if (current == null)
                _subscribers.Remove(channel);
            else
                _subscribers[channel] = current;
        }
    }

    public static void Publish<T>(string channel, T data, object sender = null)
    {
        if (string.IsNullOrEmpty(channel)) return;

        bool persistent = _states.TryGetValue(channel, out var existing) && existing.IsPersistent;
        SetState(channel, data, persistent);

        if (_subscribers.TryGetValue(channel, out var del) && del is Action<T> callback)
        {
            callback.Invoke(data);
        }

#if UNITY_EDITOR
        OnEventRaisedInEditor?.Invoke(channel, sender);
#endif
    }

    // --- Void (Parametresiz) Pub / Sub ---

    public static void Subscribe(string channel, Action callback)
    {
        if (string.IsNullOrEmpty(channel) || callback == null) return;

        if (_subscribers.TryGetValue(channel, out var existing))
            _subscribers[channel] = Delegate.Combine(existing, callback);
        else
            _subscribers[channel] = callback;
    }

    public static void Unsubscribe(string channel, Action callback)
    {
        if (string.IsNullOrEmpty(channel) || callback == null) return;

        if (_subscribers.TryGetValue(channel, out var existing))
        {
            var current = Delegate.Remove(existing, callback);
            if (current == null)
                _subscribers.Remove(channel);
            else
                _subscribers[channel] = current;
        }
    }

    public static void Publish(string channel, object sender = null)
    {
        if (string.IsNullOrEmpty(channel)) return;

        bool persistent = _states.TryGetValue(channel, out var existing) && existing.IsPersistent;
        SetState<object>(channel, null, persistent);

        if (_subscribers.TryGetValue(channel, out var del) && del is Action callback)
        {
            callback.Invoke();
        }

#if UNITY_EDITOR
        OnEventRaisedInEditor?.Invoke(channel, sender);
#endif
    }

    // --- Raise Aşırı Yüklemeleri (Sender & Alias Destekli) ---

    public static void Raise<T>(string channel, T data, object sender = null) => Publish(channel, data, sender);
    public static void Raise(string channel, object sender = null) => Publish(channel, sender);
    public static void Raise<T>(object sender, string channel, T data) => Publish(channel, data, sender);

    // --- Temizlik İşlemleri ---

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetHub()
    {
        _subscribers.Clear();
        _states.Clear();
    }

    public static void ClearNonPersistent()
    {
        var toRemove = new List<string>();
        foreach (var (key, state) in _states)
        {
            if (!state.IsPersistent) toRemove.Add(key);
        }

        for (int i = 0; i < toRemove.Count; i++)
        {
            _states.Remove(toRemove[i]);
        }
    }
}