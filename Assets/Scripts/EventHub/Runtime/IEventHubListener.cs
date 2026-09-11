/// <summary>
/// EventHub kanallarını dinleyen tüm ScriptableObject ve C# sınıfları için sözleşme arayüzü.
/// Başka sınıflardan türeyen ve EventHubSO miras alamayan SO'lar bunu uygular.
/// </summary>
public interface IEventHubListener
{
    void SubscribeEvents();
    void UnsubscribeEvents();
}