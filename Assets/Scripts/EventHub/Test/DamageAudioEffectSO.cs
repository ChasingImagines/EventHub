using UnityEngine;

[CreateAssetMenu(fileName = "DamageAudioEffectSO", menuName = "Audio/Damage Audio Effect")]
public class DamageAudioEffectSO : EventHubSO
{
    [Header("Kanal Seçimi")]
    [EventListener(typeof(int))]
    [SerializeField] private string onDamageEvent;

    public override void SubscribeEvents()
    {
        if (string.IsNullOrEmpty(onDamageEvent)) return;
        EventHub.Subscribe<int>(onDamageEvent, PlayDamageSFX);
    }

    public override void UnsubscribeEvents()
    {
        if (string.IsNullOrEmpty(onDamageEvent)) return;
        EventHub.Unsubscribe<int>(onDamageEvent, PlayDamageSFX);
    }

    private void PlayDamageSFX(int damage)
    {
        Debug.Log($"<color=#FF88EE>[AudioSO]</color> {damage} hasar için SFX sesi çalındı!", this);
    }
}