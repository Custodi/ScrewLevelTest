using System.Collections.Generic;
using UnityEngine;

public class Buffer : MonoBehaviour
{
    public Transform[] slots; // 5 позиций для болтов
    private List<Bolt> bolts = new List<Bolt>();

    public bool IsFull => bolts.Count >= slots.Length;

    public bool TryAddBolt(Bolt bolt)
    {
        if (IsFull) return false;

        bolts.Add(bolt);
        int slotIndex = bolts.Count - 1;
        bolt.transform.SetParent(slots[slotIndex]);
        bolt.transform.localPosition = Vector3.zero;
        bolt.transform.localRotation = Quaternion.identity;
        return true;
    }

    // Проверяем, можно ли выгрузить болты в активные корзины
    public void TryFlushToBaskets(List<Basket> activeBaskets)
    {
        for (int i = bolts.Count - 1; i >= 0; i--)
        {
            Bolt bolt = bolts[i];
            foreach (var basket in activeBaskets)
            {
                if (basket.TryAddBolt(bolt))
                {
                    bolts.RemoveAt(i);
                    break;
                }
            }
        }

        // Перерасположить оставшиеся болты по слотам
        for (int i = 0; i < bolts.Count; i++)
        {
            bolts[i].transform.SetParent(slots[i]);
            bolts[i].transform.localPosition = Vector3.zero;
            bolts[i].transform.localRotation = Quaternion.identity;
        }
    }
}
