using System;
using System.Collections.Generic;
using UnityEngine;

public class Basket : MonoBehaviour
{
    public int colorIndex;
    public Transform[] slots; // 3 позиции для болтов
    private List<Bolt> bolts = new List<Bolt>();

    public bool IsFull => bolts.Count >= slots.Length;

    public void Init(int colorIndex, Material mat)
    {
        this.colorIndex = colorIndex;

        // Красим модель корзины в цвет
        var rends = GetComponentsInChildren<Renderer>();
        foreach (var r in rends)
        {
            r.material = mat;
        }
    }

    public bool TryAddBolt(Bolt bolt, Action<Basket, Bolt> onPlaced = null)
    {
        if (IsFull || bolt.colorIndex != colorIndex) return false;

        bolts.Add(bolt);

        int slotIndex = bolts.Count - 1;
        bolt.transform.SetParent(slots[slotIndex]);
        bolt.transform.localPosition = Vector3.zero;
        bolt.transform.localRotation = Quaternion.identity;
        bolt.isPlaced = true;

        onPlaced?.Invoke(this, bolt);
        return true;
    }

    public void ClearAndDestroy()
    {
        bolts.Clear();
        Destroy(gameObject);
    }
}
