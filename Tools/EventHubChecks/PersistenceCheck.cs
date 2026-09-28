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

        // 1) Kalıcı kanal state'i kurulabilir ve IsPersistent bayrağı saklanır
        EventHub.SetState(persistent, 42, isPersistent: true);
        Check(EventHub.TryGetRawState(persistent, out _, out _, out bool isPers) && isPers, "SetState isPersistent=true korunuyor");

        // 2) Kalıcı kanal değeri doğru okunabilmeli
        Check(EventHub.TryGet(persistent, out int vInit) && vInit == 42, "kalıcı ilk değer doğru (42)");

        // 3) Geçici kanal kurulabilir
        EventHub.SetState(transient, 7, isPersistent: false);
        Check(EventHub.HasValue(transient), "geçici değer kuruldu");

        // 4) ClearNonPersistent yalnızca geçici kanalları silmeli, kalıcıları korumalı
        EventHub.ClearNonPersistent();
        Check(!EventHub.HasValue(transient), "geçici değer ClearNonPersistent ile silindi");
        Check(EventHub.HasValue(persistent), "kalıcı değer ClearNonPersistent sonrası korundu");
        Check(EventHub.TryGet(persistent, out int v) && v == 42, "kalıcı değer doğru okunuyor (42)");

        Console.WriteLine(_failures == 0 ? "ALL OK" : _failures + " FAILED");
        return _failures == 0 ? 0 : 1;
    }
}
