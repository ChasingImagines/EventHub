using System;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

public abstract class EventHubSO : ScriptableObject, IEventHubListener
{
    [NonSerialized] private bool _isSubscribed;

    protected virtual void OnEnable()
    {
#if UNITY_EDITOR
        EditorApplication.playModeStateChanged -= HandlePlayMode;
        EditorApplication.playModeStateChanged += HandlePlayMode;
#endif
        if (Application.isPlaying)
        {
            Register();
        }
    }

    protected virtual void OnDisable()
    {
#if UNITY_EDITOR
        EditorApplication.playModeStateChanged -= HandlePlayMode;
#endif
        Unregister();
    }

#if UNITY_EDITOR
    private void HandlePlayMode(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            Register();
        }
        else if (state == PlayModeStateChange.ExitingPlayMode)
        {
            Unregister();
        }
    }
#endif

    public void Register()
    {
        if (_isSubscribed) return;
        SubscribeEvents();
        _isSubscribed = true;
    }

    public void Unregister()
    {
        if (!_isSubscribed) return;
        UnsubscribeEvents();
        _isSubscribed = false;
    }

    public abstract void SubscribeEvents();
    public abstract void UnsubscribeEvents();
}