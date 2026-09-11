using System;
using UnityEngine;

[AttributeUsage(AttributeTargets.Field, Inherited = true, AllowMultiple = false)]
public class EventChannelAttribute : PropertyAttribute
{
    public Type ExpectedType { get; }

    // Zorunlu parametre: '= null' kaldırıldı. Artık tip boş geçilemez.
    public EventChannelAttribute(Type expectedType)
    {
        ExpectedType = expectedType ?? throw new ArgumentNullException(
            nameof(expectedType),
            "Event tipi belirtilmek zorundadır! Parametresiz olaylar için 'typeof(void)' kullanın."
        );
    }
}

public class EventPublisherAttribute : EventChannelAttribute
{
    public EventPublisherAttribute(Type expectedType) : base(expectedType) { }
}

public class EventListenerAttribute : EventChannelAttribute
{
    public EventListenerAttribute(Type expectedType) : base(expectedType) { }
}