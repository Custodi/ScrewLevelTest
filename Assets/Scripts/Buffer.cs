using System;
using System.Collections.Generic;
using UnityEngine;

public class Buffer : MonoBehaviour
{
    public Transform[] slots; // 5 позиций для болтов
    private List<Bolt> bolts = new List<Bolt>();

    public bool IsFull => bolts.Count >= slots.Length;

    public bool TryAddBolt(Bolt bolt, Action<Bolt> onPlaced = null)
    {
        if (IsFull) return false;

        bolts.Add(bolt);
        int slotIndex = bolts.Count - 1;

        bolt.transform.SetParent(slots[slotIndex]);
        bolt.transform.localPosition = Vector3.zero;
        bolt.transform.localRotation = Quaternion.identity;
        bolt.isPlaced = true;

        onPlaced?.Invoke(bolt);
        return true;
    }

    public void RemoveBolt(Bolt bolt)
    {
        if (bolts.Remove(bolt))
        {
            Rearrange();
        }
    }

    /// <summary>
    /// Пытаемся выгрузить болты из буфера в доступные корзины
    /// </summary>
    public void FlushToBaskets(List<Basket> activeBaskets, Action<Basket, Bolt> onBoltPlacedInBasket = null)
    {
        for (int i = bolts.Count - 1; i >= 0; i--)
        {
            Bolt bolt = bolts[i];
            foreach (var basket in activeBaskets)
            {
                if (basket.TryAddBolt(bolt, onBoltPlacedInBasket))
                {
                    bolts.RemoveAt(i);
                    break;
                }
            }
        }

        Rearrange();
    }

    private void Rearrange()
    {
        for (int i = 0; i < bolts.Count; i++)
        {
            bolts[i].transform.SetParent(slots[i]);
            bolts[i].transform.localPosition = Vector3.zero;
            bolts[i].transform.localRotation = Quaternion.identity;
        }
    }
}
