using System;

// EventHub kalıcılık (persistence) davranışının çalıştırılabilir kontrolü.
// Native Unity çağrısı yapmayan yolları (Set/SetPersistent/ResetTransientStates) kullanır.
// Çalıştırmak için: Tools/EventHubChecks/run_persistence_check.sh
public static class PersistenceCheck
{
    private static int _failures;

    private static void Check(bool condition, string message)
    {
        Console.WriteLine((condition ? "ok  : " : "FAIL: ") + message);
        if (!condition) _failures++;
    }

    public static int Main()
    {
        const string persistent = "Test/PersistentChannel";
        const string transient = "Test/TransientChannel";

        // 1) Override, kanalın state'i henüz yokken de görünür olmalı
        EventHub.SetPersistent(persistent, true);
        Check(EventHub.IsPersistent(persistent), "override=true, state yokken korunuyor");

        // 2) ASIL HATA SENARYOSU: Set<int> yeni bir EventState<int> kurar; override kaybolmamalı
        EventHub.Set(persistent, 42);
        Check(EventHub.IsPersistent(persistent), "override, Set<int> state'i yeniledikten sonra da korunuyor");

        // 3) Geçici kanal sahne değişiminde silinmeli
        EventHub.Set(transient, 7);
        EventHub.SetPersistent(transient, false);
        EventHub.ResetTransientStates();
        Check(!EventHub.HasValue(transient), "geçici değer ResetTransientStates ile silindi");

        // 4) Kalıcı kanalın değeri sahne değişiminde korunmalı ve okunabilmeli
        EventHub.ResetTransientStates();
        Check(EventHub.HasValue(persistent), "kalıcı değer ResetTransientStates sonrası korundu");
        Check(EventHub.TryGet(persistent, out int v) && v == 42, "kalıcı değer doğru okunuyor (42)");

        Console.WriteLine(_failures == 0 ? "ALL OK" : _failures + " FAILED");
        return _failures == 0 ? 0 : 1;
    }
}
