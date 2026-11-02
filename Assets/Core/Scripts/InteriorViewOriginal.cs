using UnityEngine;

// Remembers the full mesh of an object while the map is shown in interior view (outward faces hidden).
[DisallowMultipleComponent]
public class InteriorViewOriginal : MonoBehaviour
{
    public Mesh original;
}
