using UnityEngine;
using UnityEngine.InputSystem;

public class CombatAttacker : MonoBehaviour
{
    [Header("Event Kanalları (Inspector'dan Seçiniz)")]
    // Açılır menüde sadece INT taşıyan event'ler görünür
    [EventPublisher(typeof(int))]
    [SerializeField] private string damageChannel;

    // Açılır menüde sadece VOID (parametresiz) event'ler görünür
    [EventPublisher(typeof(void))]
    [SerializeField] private string playerDeathChannel;

    private void Update()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null) return;

        // Space: Hasar fırlat (int)
        if (keyboard.spaceKey.wasPressedThisFrame)
        {
            int damageAmount = Random.Range(15, 30);
            Debug.Log($"<color=orange>[Attacker]</color> Saldırı yapıldı! {damageAmount} hasar basılıyor...", this);

            // EventHub üzerinden güvenli fırlatma
            EventHub.Raise(damageChannel, damageAmount, this);
        }

        // K: Oyuncu öldü sinyali fırlat (void)
        if (keyboard.kKey.wasPressedThisFrame)
        {
            Debug.Log("<color=red>[Attacker]</color> Ölüm sinyali basılıyor...", this);
            EventHub.Raise(playerDeathChannel, this);
        }
    }
}