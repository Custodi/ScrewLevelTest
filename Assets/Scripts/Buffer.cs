using System;
using System.Collections.Generic;
using UnityEngine;

public class Buffer : MonoBehaviour
{
    public Transform[] slots; // 5 позиций для болтов
    private List<Bolt> bolts = new List<Bolt>();

    public bool IsFull => bolts.Count >= (slots != null ? slots.Length : 0);

    public bool TryAddBolt(Bolt bolt, Action<Bolt> onPlaced = null)
    {
        if (IsFull) return false;
        if (bolt == null) return false;

        bolts.Add(bolt);
        int slotIndex = bolts.Count - 1;

        if (slots != null && slotIndex < slots.Length && slots[slotIndex] != null)
        {
            bolt.transform.SetParent(slots[slotIndex], false);
            bolt.transform.localPosition = Vector3.zero;
            bolt.transform.localRotation = Quaternion.identity;
        }
        else
        {
            // fallback: если нет слота — ставим рядом с буфером
            bolt.transform.SetParent(this.transform, false);
            float spacing = 0.05f;
            Vector3 offset = new Vector3((slotIndex % 2 == 0 ? 1 : -1) * spacing * (slotIndex / 2 + 1), spacing * (slotIndex / 2), 0f);
            bolt.transform.localPosition = offset;
            bolt.transform.localRotation = Quaternion.identity;
            Debug.LogWarning($"[Buffer] Нет слота для индекса {slotIndex}. Болт размещён в fallback-позиции.");
        }

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
            if (slots != null && i < slots.Length && slots[i] != null)
            {
                bolts[i].transform.SetParent(slots[i], false);
                bolts[i].transform.localPosition = Vector3.zero;
                bolts[i].transform.localRotation = Quaternion.identity;
            }
            else
            {
                // fallback
                bolts[i].transform.SetParent(this.transform, false);
                float spacing = 0.05f;
                Vector3 offset = new Vector3((i % 2 == 0 ? 1 : -1) * spacing * (i / 2 + 1), spacing * (i / 2), 0f);
                bolts[i].transform.localPosition = offset;
                bolts[i].transform.localRotation = Quaternion.identity;
            }
        }
    }
}
