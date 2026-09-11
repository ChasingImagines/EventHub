using System;
using UnityEngine;

public interface IEventPayload
{
    Type DataType { get; }
    string DisplayName { get; }
}

[Serializable]
public class VoidPayload : IEventPayload
{
    public Type DataType => typeof(void);
    public string DisplayName => "Void (Parametresiz)";
}

[Serializable]
public class IntPayload : IEventPayload
{
    public Type DataType => typeof(int);
    public string DisplayName => "Int";
}

[Serializable]
public class FloatPayload : IEventPayload
{
    public Type DataType => typeof(float);
    public string DisplayName => "Float";
}

[Serializable]
public class StringPayload : IEventPayload
{
    public Type DataType => typeof(string);
    public string DisplayName => "String";
}

[Serializable]
public class Vector3Payload : IEventPayload
{
    public Type DataType => typeof(Vector3);
    public string DisplayName => "Vector3";
}

[Serializable]
public class BoolPayload : IEventPayload
{
    public Type DataType => typeof(bool);
    public string DisplayName => "Bool";
}