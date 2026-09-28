using System;
using UnityEngine;

public interface IEventPayload
{
    Type DataType { get; }
    string DisplayName { get; }
    object GetDefaultRawValue();
}

[Serializable]
public class VoidPayload : IEventPayload
{
    public Type DataType => typeof(void);
    public string DisplayName => "Void (Sinyal)";
    public object GetDefaultRawValue() => null;
}

/// <summary>
/// Tüm veri taşıyan tiplerin miras alacağı temel sınıf.
/// Kendi özel veri tiplerini eklemek için bu sınıftan türetmen yeterlidir.
/// </summary>
[Serializable]
public abstract class EventPayload<T> : IEventPayload
{
    [Tooltip("Oyun başladığında bu kanalın taşıyacağı ilk değer")]
    public T defaultValue;

    public Type DataType => typeof(T);
    public virtual string DisplayName => typeof(T).Name;
    public object GetDefaultRawValue() => defaultValue;
}

// --- Hazır Temel Tipler ---

[Serializable]
public class IntPayload : EventPayload<int> { }

[Serializable]
public class FloatPayload : EventPayload<float> { }

[Serializable]
public class BoolPayload : EventPayload<bool> { }

[Serializable]
public class StringPayload : EventPayload<string> { }

[Serializable]
public class Vector3Payload : EventPayload<Vector3> { }