using System;
using System.Collections.Generic;
using UnityEngine;

public class Basket : MonoBehaviour
{
    public int colorIndex;
    public Transform[] slots; // 3 позиции для болтов (ожидается)
    private List<Bolt> bolts = new List<Bolt>();

    // Жёстко требуемая вместимость корзины (по правилам — 3 болта)
    private const int REQUIRED_CAPACITY = 3;

    // Фактическая вместимость — минимум между требуемой и конфигурацией слотов:
    // если префаб имеет больше слотов — используем их; если меньше — всё равно считаем capacity >= REQUIRED_CAPACITY
    public int Capacity => Mathf.Max(REQUIRED_CAPACITY, (slots != null ? slots.Length : 0));

    // Полная ли корзина — используем Capacity, а не строго slots.Length
    public bool IsFull => bolts.Count >= Capacity;

    public void Init(int colorIndex, Material mat)
    {
        this.colorIndex = colorIndex;

        // Красим модель корзины в цвет
        var rends = GetComponentsInChildren<Renderer>();
        foreach (var r in rends)
        {
            if (r != null && mat != null)
                r.material = mat;
        }
    }

    public bool TryAddBolt(Bolt bolt, Action<Basket, Bolt> onPlaced = null)
    {
        if (bolt == null) return false;

        // цвет не подходит
        if (bolt.colorIndex != colorIndex) return false;

        // уже полная
        if (bolts.Count >= Capacity) return false;

        bolts.Add(bolt);

        int slotIndex = bolts.Count - 1;

        // Если в префабе есть соответствующий слот — ставим туда
        if (slots != null && slotIndex < slots.Length && slots[slotIndex] != null)
        {
            bolt.transform.SetParent(slots[slotIndex], false);
            bolt.transform.localPosition = Vector3.zero;
            bolt.transform.localRotation = Quaternion.identity;
        }
        else
        {
            // fallback: если слотов меньше чем нужно, привязываем к корзине и ставим в аккуратную fallback-позицию
            bolt.transform.SetParent(this.transform, false);

            // простая аккуратная расстановка: 3 позиции в виде небольшого треугольника/строчки
            float spacing = 0.05f; // можно подправить в префабе
            Vector3 offset = Vector3.zero;
            switch (slotIndex)
            {
                case 0: offset = Vector3.up * spacing; break;
                case 1: offset = Vector3.right * spacing; break;
                default: offset = Vector3.left * spacing * (slotIndex - 1); break;
            }

            bolt.transform.localPosition = offset;
            bolt.transform.localRotation = Quaternion.identity;

            Debug.LogWarning($"[Basket] Недостаточно слотов в префабе корзины '{name}' (slots.Length = {(slots == null ? 0 : slots.Length)}). Болт будет размещён в fallback-позиции.");
        }

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
