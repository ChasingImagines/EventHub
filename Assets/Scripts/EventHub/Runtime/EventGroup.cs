using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[AddComponentMenu("Architecture/Event Group")]
public class EventGroup : MonoBehaviour
{
    [Tooltip("Grup yolu. Eğik çizgi '/' kullanarak alt grup tanımlayabilirsiniz (Örn: 'UI/Popups' veya 'Enemies/Melee').")]
    [SerializeField] private string groupName = "General";

    public string GroupName => string.IsNullOrWhiteSpace(groupName) ? "General" : groupName;

    public string GetFullHierarchyPath()
    {
        var groups = GetComponentsInParent<EventGroup>(true);
        var pathParts = new List<string>();

        for (int i = groups.Length - 1; i >= 0; i--)
        {
            string raw = groups[i].GroupName.Trim('/', ' ');
            if (!string.IsNullOrEmpty(raw))
            {
                pathParts.Add(raw);
            }
        }

        return string.Join("/", pathParts);
    }
}