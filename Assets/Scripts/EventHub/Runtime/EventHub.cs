using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Projenin merkezi, tip korumalı, durum (state/blackboard) hafızalı,
/// sahneler arası kalıcılık destekli ve bellek sızıntılarına karşı yalıtılmış Event Hub motoru.
/// </summary>
public static class EventHub
{
    // Multicast delegate yerine List<Delegate> kullanımı: Sıfır GC Alloc ve anlık Index bazlı silme sağlar
    private static readonly Dictionary<string, List<Delegate>> _subscriptions = new();

    #region Stateful / Blackboard Altyapısı

    private interface IEventState
    {
        bool HasValue { get; set; }
        void Reset();
        object RawValue { get; }
    }

    private class EventState<T> : IEventState
    {
        public T Value;
        public bool HasValue { get; set; }

        public void Reset()
        {
            Value = default;
            HasValue = false;
        }

        public object RawValue => Value;
    }

    private class VoidState : IEventState
    {
        public bool HasValue { get; set; }

        public void Reset()
        {
            HasValue = false;
        }

        public object RawValue => null;
    }

    // Kanalların son durumlarını (değer ve bayraklarını) RAM'de tutan sözlük
    private static readonly Dictionary<string, IEventState> _states = new();

    // State nesnesinden bağımsız runtime kalıcılık geçersiz kılmaları (Set/Raise state'i yenilese bile korunur)
    private static readonly Dictionary<string, bool> _persistenceOverrides = new();

    #endregion

#if UNITY_EDITOR
    // Event Matrix editör penceresinin anlık nabız (pulse) yakalaması için delegasyon
    public static event Action<string> OnEventRaisedInEditor;

    [UnityEditor.InitializeOnLoadMethod]
    private static void InitEditorLifecycle()
    {
        UnityEditor.EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        UnityEditor.EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    private static void OnPlayModeStateChanged(UnityEditor.PlayModeStateChange state)
    {
        if (state == UnityEditor.PlayModeStateChange.ExitingPlayMode ||
            state == UnityEditor.PlayModeStateChange.EnteredEditMode)
        {
            _subscriptions.Clear();
            _states.Clear();
            _persistenceOverrides.Clear();
        }
    }
#endif

    // Unity Fast Play Mode (Domain Reload kapalıyken) statik hafızayı temizleme kalkanı
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticData()
    {
        _subscriptions.Clear();
        _states.Clear();
        _persistenceOverrides.Clear();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void InitializeSceneLifecycle()
    {
        SceneManager.sceneUnloaded -= OnSceneUnloaded;
        SceneManager.sceneUnloaded += OnSceneUnloaded;
    }

    // Sahne değiştiğinde ölü dinleyicileri budar ve sadece sahnelik (geçici) durumları süpürür
    private static void OnSceneUnloaded(Scene scene)
    {
        PruneDeadSubscribers();
        ResetTransientStates();
    }

    #region Parametreli Metotlar (Generic - Sıfır Boxing)

    public static void Subscribe<T>(string eventName, Action<T> callback)
    {
        if (string.IsNullOrEmpty(eventName) || callback == null) return;

#if UNITY_EDITOR
        if (!ValidateContract(eventName, typeof(T), callback.Target as UnityEngine.Object)) return;
#endif

        if (!_subscriptions.TryGetValue(eventName, out var list))
        {
            list = new List<Delegate>();
            _subscriptions[eventName] = list;
        }

        // Tip güvenliği kontrolü: Kanala ilk bağlanan dinleyicinin tipi ile sonraki uyuşmalı
        if (list.Count > 0 && list[0] is not Action<T>)
        {
            var targetObj = callback.Target as UnityEngine.Object;
            Debug.LogError(
                $"<b>[EventHub Çakışması]</b> '{eventName}' olayına kayıt başarısız!\n" +
                $"• <b>Bağlanmaya Çalışan:</b> {(callback.Target != null ? callback.Target.GetType().Name : "Static")} -> {callback.Method.Name}()\n" +
                $"• <b>Beklenen Tip:</b> {typeof(Action<T>).Name}\n" +
                $"• <b>Mevcut Dinleyiciler:</b>\n{GetListenersInfo(list)}",
                targetObj
            );
            return;
        }

        if (!list.Contains(callback))
        {
            list.Add(callback);
        }
    }

    public static void Unsubscribe<T>(string eventName, Action<T> callback)
    {
        if (string.IsNullOrEmpty(eventName) || callback == null) return;

        if (_subscriptions.TryGetValue(eventName, out var list))
        {
            list.Remove(callback);
            if (list.Count == 0)
            {
                _subscriptions.Remove(eventName);
            }
        }
    }

    public static void Raise<T>(string eventName, T payload, UnityEngine.Object sender = null)
    {
        if (string.IsNullOrEmpty(eventName)) return;

#if UNITY_EDITOR
        // 1. SÖZLEŞME DENETİMİ (Veritabanı ile tip eşleşmesi doğrulaması)
        var db = EventDatabase.Instance;
        if (db != null && db.TryGetExpectedType(eventName, out Type expected))
        {
            if (expected == typeof(void))
            {
                Debug.LogError(
                    $"<b>[EventHub Sözleşme Hatası]</b> '{eventName}' veritabanında <b>VOID (Parametresiz)</b> tanımlanmış!\n" +
                    $"Ancak koddan <i>{typeof(T).Name}</i> fırlatıldı.",
                    sender
                );
                return;
            }
            if (expected != typeof(T))
            {
                Debug.LogError(
                    $"<b>[EventHub Sözleşme Hatası]</b> '{eventName}' veritabanında <b>{expected.Name}</b> beklerken, koddan <i>{typeof(T).Name}</i> fırlatıldı!",
                    sender
                );
                return;
            }
        }
#endif

        // 2. DURUM (STATE) GÜNCELLEMESİ
        if (!_states.TryGetValue(eventName, out var rawState) || rawState is not EventState<T> state)
        {
            state = new EventState<T>();
            _states[eventName] = state;
        }
        state.Value = payload;
        state.HasValue = true;

#if UNITY_EDITOR
        // Yalnızca geçerli tetiklemeler editör telemetrisini aydınlatır
        OnEventRaisedInEditor?.Invoke(eventName);
#endif

        if (!_subscriptions.TryGetValue(eventName, out var list)) return;

        // 3. DAĞITIM DÖNGÜSÜ (Ters döngü: Sıfır-GC Alloc ve anında ölü obje budama)
        for (int i = list.Count - 1; i >= 0; i--)
        {
            var del = list[i];

            // Ölü Unity nesnesi zırhı: Destroy edildiyse listeden söküp at
            if (del.Target is UnityEngine.Object uObj && uObj == null)
            {
                list.RemoveAt(i);
                continue;
            }

            if (del is Action<T> action)
            {
                // İstisna Kalkanı: Tek bir dinleyicinin hatası diğer dinleyicileri durdurmaz
                try
                {
                    action.Invoke(payload);
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex, sender);
                }
            }
            else
            {
                Debug.LogError(
                    $"<b>[EventHub Tip Uyuşmazlığı]</b> '{eventName}' tetiklenemedi!\n" +
                    $"• <b>Tetikleyen:</b> {(sender != null ? sender.name : "Static")} ({typeof(T).Name})\n" +
                    $"• <b>Kayıtlı Dinleyiciler:</b>\n{GetListenersInfo(list)}",
                    sender
                );
            }
        }

        if (list.Count == 0)
        {
            _subscriptions.Remove(eventName);
        }
    }

    #endregion

    #region Parametresiz Metotlar (Void)

    public static void Subscribe(string eventName, Action callback)
    {
        if (string.IsNullOrEmpty(eventName) || callback == null) return;

#if UNITY_EDITOR
        if (!ValidateContract(eventName, typeof(void), callback.Target as UnityEngine.Object)) return;
#endif

        if (!_subscriptions.TryGetValue(eventName, out var list))
        {
            list = new List<Delegate>();
            _subscriptions[eventName] = list;
        }

        if (list.Count > 0 && list[0] is not Action)
        {
            var targetObj = callback.Target as UnityEngine.Object;
            Debug.LogError(
                $"<b>[EventHub Çakışması]</b> '{eventName}' parametresiz dinlenmek istendi fakat parametreli kayıt mevcut!",
                targetObj
            );
            return;
        }

        if (!list.Contains(callback))
        {
            list.Add(callback);
        }
    }

    public static void Unsubscribe(string eventName, Action callback)
    {
        if (string.IsNullOrEmpty(eventName) || callback == null) return;

        if (_subscriptions.TryGetValue(eventName, out var list))
        {
            list.Remove(callback);
            if (list.Count == 0)
            {
                _subscriptions.Remove(eventName);
            }
        }
    }

    public static void Raise(string eventName, UnityEngine.Object sender = null)
    {
        if (string.IsNullOrEmpty(eventName)) return;

#if UNITY_EDITOR
        var db = EventDatabase.Instance;
        if (db != null && db.TryGetExpectedType(eventName, out Type expected) && expected != typeof(void))
        {
            Debug.LogError(
                $"<b>[EventHub Sözleşme Hatası]</b> '{eventName}' olayı veritabanında <b>{expected.Name}</b> bekliyor!\n" +
                $"Ancak parametresiz Raise() çağrıldı.",
                sender
            );
            return;
        }
#endif

        if (!_states.TryGetValue(eventName, out var rawState) || rawState is not VoidState state)
        {
            state = new VoidState();
            _states[eventName] = state;
        }
        state.HasValue = true;

#if UNITY_EDITOR
        OnEventRaisedInEditor?.Invoke(eventName);
#endif

        if (!_subscriptions.TryGetValue(eventName, out var list)) return;

        for (int i = list.Count - 1; i >= 0; i--)
        {
            var del = list[i];

            if (del.Target is UnityEngine.Object uObj && uObj == null)
            {
                list.RemoveAt(i);
                continue;
            }

            if (del is Action action)
            {
                try
                {
                    action.Invoke();
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex, sender);
                }
            }
            else
            {
                Debug.LogError(
                    $"<b>[EventHub Tip Uyuşmazlığı]</b> '{eventName}' parametresiz tetiklenmek istendi fakat dinleyiciler parametre bekliyor!\n" +
                    $"• <b>Kayıtlı Dinleyiciler:</b>\n{GetListenersInfo(list)}",
                    sender
                );
            }
        }

        if (list.Count == 0)
        {
            _subscriptions.Remove(eventName);
        }
    }

    #endregion

    #region Durum Okuma, Sıfırlama ve Kalıcılık API'si

    /// <summary>
    /// Kanalda geçerli ve henüz tüketilmemiş bir durum/değer olup olmadığını bildirir.
    /// </summary>
    public static bool HasValue(string eventName)
    {
        return _states.TryGetValue(eventName, out var state) && state.HasValue;
    }

    /// <summary>
    /// Kanalın kalıcı (persistent) olup olmadığını runtime geçersiz kılma bayrağından veya EventDatabase'den doğrular.
    /// </summary>
    public static bool IsPersistent(string eventName)
    {
        if (_persistenceOverrides.TryGetValue(eventName, out bool overridden))
        {
            return overridden;
        }
        return EventDatabase.Instance != null && EventDatabase.Instance.IsPersistent(eventName);
    }

    /// <summary>
    /// Kanalın sahne değişimlerinde korunup korunmayacağını çalışma anında zorlar.
    /// </summary>
    public static void SetPersistent(string eventName, bool isPersistent)
    {
        _persistenceOverrides[eventName] = isPersistent;
    }

    /// <summary>
    /// Kanalın son geçerli değerini güvenle almaya çalışır.
    /// </summary>
    public static bool TryGet<T>(string eventName, out T value)
    {
        if (_states.TryGetValue(eventName, out var rawState) &&
            rawState is EventState<T> typedState &&
            typedState.HasValue)
        {
            value = typedState.Value;
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>
    /// Kanalın son değerini çeker. Veri yoksa verilen fallback değerini döner.
    /// </summary>
    public static T Get<T>(string eventName, T fallback = default)
    {
        return TryGet<T>(eventName, out T val) ? val : fallback;
    }

    /// <summary>
    /// Dinleyicileri ayağa kaldırmadan kanaldaki state verisini doğrudan RAM'e yazar.
    /// </summary>
    public static void Set<T>(string eventName, T value)
    {
        if (!_states.TryGetValue(eventName, out var rawState) || rawState is not EventState<T> state)
        {
            state = new EventState<T>();
            _states[eventName] = state;
        }
        state.Value = value;
        state.HasValue = true;
    }

    /// <summary>
    /// Kanalın geçerlilik bayrağını manuel olarak ayarlar.
    /// </summary>
    public static void SetValid(string eventName, bool isValid)
    {
        if (_states.TryGetValue(eventName, out var state))
        {
            state.HasValue = isValid;
        }
    }

    /// <summary>
    /// Kanalın değerini tüketildi (geçersiz) olarak işaretler (HasValue = false).
    /// </summary>
    public static void Invalidate(string eventName)
    {
        SetValid(eventName, false);
    }

    /// <summary>
    /// Belirtilen kanalı sıfırlar. Kanal kalıcıysa ve force=false verilmişse koruma kalkanı devreye girer.
    /// </summary>
    public static bool ResetState(string eventName, bool force = false)
    {
        if (!_states.TryGetValue(eventName, out var state)) return false;

        if (IsPersistent(eventName) && !force)
        {
            Debug.LogWarning(
                $"<b>[EventHub]</b> '{eventName}' kalıcı bir kanaldır ve sıfırlanmaya karşı korundu! " +
                "Zorla sıfırlamak için ResetState(channel, force: true) kullanın."
            );
            return false;
        }

        state.Reset();
        return true;
    }

    /// <summary>
    /// Yalnızca geçici (sahnelik) durumları sıfırlar. Kalıcı olarak işaretlenmiş olanlara dokunmaz.
    /// </summary>
    public static void ResetTransientStates()
    {
        var keysToRemove = new List<string>();

        foreach (var (key, _) in _states)
        {
            if (!IsPersistent(key))
            {
                keysToRemove.Add(key);
            }
        }

        for (int i = 0; i < keysToRemove.Count; i++)
        {
            _states.Remove(keysToRemove[i]);
        }
    }

    /// <summary>
    /// Tüm durumları tamamen temizler.
    /// </summary>
    public static void ResetAllStates(bool includePersistent = false)
    {
        if (includePersistent)
        {
            _states.Clear();
        }
        else
        {
            ResetTransientStates();
        }
    }

#if UNITY_EDITOR
    /// <summary>
    /// EventMatrixWindow için ham değer ve kalıcılık durumunu okuma desteği.
    /// </summary>
    public static bool TryGetRawState(string eventName, out object rawValue, out bool hasValue, out bool isPersistent)
    {
        isPersistent = IsPersistent(eventName);
        if (_states.TryGetValue(eventName, out var state))
        {
            rawValue = state.RawValue;
            hasValue = state.HasValue;
            return true;
        }
        rawValue = null;
        hasValue = false;
        return false;
    }

    public static bool TryGetRawState(string eventName, out object rawValue, out bool hasValue)
    {
        return TryGetRawState(eventName, out rawValue, out hasValue, out _);
    }
#endif

    #endregion

    #region Temizlik & Validasyon Yardımcıları

    /// <summary>
    /// Sahne geçişlerinde abonelikten çıkmamış ölü GameObject delegasyonlarını süpürür.
    /// </summary>
    public static void PruneDeadSubscribers()
    {
        var emptyChannels = new List<string>();

        foreach (var (channel, list) in _subscriptions)
        {
            list.RemoveAll(del => del.Target is UnityEngine.Object uObj && uObj == null);
            if (list.Count == 0)
            {
                emptyChannels.Add(channel);
            }
        }

        for (int i = 0; i < emptyChannels.Count; i++)
        {
            _subscriptions.Remove(emptyChannels[i]);
        }
    }

#if UNITY_EDITOR
    private static bool ValidateContract(string eventName, Type attemptingType, UnityEngine.Object context)
    {
        var db = EventDatabase.Instance;
        if (db == null) return true;

        if (!db.TryGetExpectedType(eventName, out Type expected)) return true;

        if (expected != attemptingType)
        {
            Debug.LogError(
                $"<b>[EventHub Sözleşme Hatası]</b> '{eventName}' olayına " +
                $"<i>{(attemptingType == typeof(void) ? "Void" : attemptingType.Name)}</i> olarak abone olunamaz!\n" +
                $"Veritabanındaki beklenen tip: <b>{(expected == typeof(void) ? "Void" : expected.Name)}</b>",
                context
            );
            return false;
        }

        return true;
    }
#endif

    // Hata mesajlarında kullanılır ve build'de de derlenmek zorundadır (runtime hata raporu).
    private static string GetListenersInfo(List<Delegate> list)
    {
        if (list == null || list.Count == 0) return "   (Kayıtlı dinleyici yok)";
        var report = "";

        foreach (var sub in list)
        {
            if (sub == null) continue;
            string targetName = sub.Target is UnityEngine.Object obj && obj != null
                ? obj.name
                : sub.Target?.GetType().Name ?? "Static/Yok Edilmiş";

            var genericArgs = sub.GetType().GetGenericArguments();
            string expectedType = genericArgs.Length > 0 ? genericArgs[0].Name : "void";
            report += $"   └─ [{targetName}] -> Metot: <i>{sub.Method.Name}()</i> | Beklenen: {expectedType}\n";
        }
        return report;
    }

    #endregion
}
