using UnityEngine;

public class HealthUIController : MonoBehaviour
{
    [Header("Event Kanalları (Inspector'dan Seçiniz)")]
    // Sadece INT olaylarını listeler
    [EventListener(typeof(int))]
    [SerializeField] private string damageListenerChannel;

    // Sadece VOID olaylarını listeler
    [EventListener(typeof(void))]
    [SerializeField] private string deathListenerChannel;

    private int _currentHealth = 100;

    private void Start()
    {
        // STATE KONTROLÜ (Geç abone olma / Late-Joiner çözümü):
        // Eğer UI oyun başladıktan sonra açıldıysa, son hasarı hafızadan anında çekebilir
        if (EventHub.TryGet(damageListenerChannel, out int lastDamage))
        {
            Debug.Log($"<color=cyan>[HealthUI]</color> UI geç açıldı fakat son hasar verisi RAM'den okundu: {lastDamage}");
        }
    }

    private void OnEnable()
    {
        // Inspector'dan seçim yapılmadıysa abone olmaya çalışma
        if (string.IsNullOrEmpty(damageListenerChannel) || string.IsNullOrEmpty(deathListenerChannel))
        {
            Debug.LogWarning("[HealthUI] Kanallar Inspector'dan seçilmedi!", this);
            return;
        }

        EventHub.Subscribe<int>(damageListenerChannel, OnDamageReceived);
        EventHub.Subscribe(deathListenerChannel, OnPlayerDied);
    }

    private void OnDisable()
    {
        EventHub.Unsubscribe<int>(damageListenerChannel, OnDamageReceived);
        EventHub.Unsubscribe(deathListenerChannel, OnPlayerDied);
    }

    private void OnDamageReceived(int damage)
    {
        if (_currentHealth <= 0) return; // Zaten ölü, tekrar hasar almasın

        _currentHealth = Mathf.Max(0, _currentHealth - damage);
        Debug.Log($"<color=green>[HealthUI]</color> Hasar alındı: {damage} | Kalan Can: {_currentHealth}", this);

        if (_currentHealth <= 0)
        {
            // Can bittiğinde otomatik ölüm olayını fırlat
            // Derleyiciye bunun payload değil 'sender' olduğunu açıkça söyleyin:
            EventHub.Raise(deathListenerChannel, sender: this);
        }
    }

    private void OnPlayerDied()
    {
        Debug.Log("<color=red>[HealthUI]</color> Ölüm sinyali yakalandı! Ekran karartılıyor...", this);
    }
}