using System;
using System.Collections.Generic;
using UnityEngine;

public class Basket : MonoBehaviour
{
    public int colorIndex;
    public Transform[] slots; // 3 ������� ��� ������ (���������)
    private List<Bolt> bolts = new List<Bolt>();

    // Ƹ���� ��������� ����������� ������� (�� �������� � 3 �����)
    private const int REQUIRED_CAPACITY = 3;

    // ����������� ����������� � ������� ����� ��������� � ������������� ������:
    // ���� ������ ����� ������ ������ � ���������� ��; ���� ������ � �� ����� ������� capacity >= REQUIRED_CAPACITY
    public int Capacity => Mathf.Max(REQUIRED_CAPACITY, (slots != null ? slots.Length : 0));

    // ������ �� ������� � ���������� Capacity, � �� ������ slots.Length
    public bool IsFull => bolts.Count >= Capacity;

    public void Init(int colorIndex, Material mat)
    {
        this.colorIndex = colorIndex;

        // ������ ������ ������� � ����
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

        // ���� �� ��������
        if (bolt.colorIndex != colorIndex) return false;

        // ��� ������
        if (bolts.Count >= Capacity) return false;

        bolts.Add(bolt);

        int slotIndex = bolts.Count - 1;

        // ���� � ������� ���� ��������������� ���� � ������ ����
        if (slots != null && slotIndex < slots.Length && slots[slotIndex] != null)
        {
            bolt.transform.SetParent(slots[slotIndex], false);
            bolt.transform.localPosition = Vector3.zero;
            bolt.transform.localRotation = Quaternion.identity;
        }
        else
        {
            // fallback: ���� ������ ������ ��� �����, ����������� � ������� � ������ � ���������� fallback-�������
            bolt.transform.SetParent(this.transform, false);

            // ������� ���������� �����������: 3 ������� � ���� ���������� ������������/�������
            float spacing = 0.05f; // ����� ���������� � �������
            Vector3 offset = Vector3.zero;
            switch (slotIndex)
            {
                case 0: offset = Vector3.up * spacing; break;
                case 1: offset = Vector3.right * spacing; break;
                default: offset = Vector3.left * spacing * (slotIndex - 1); break;
            }

            bolt.transform.localPosition = offset;
            bolt.transform.localRotation = Quaternion.identity;

            Debug.LogWarning($"[Basket] ������������ ������ � ������� ������� '{name}' (slots.Length = {(slots == null ? 0 : slots.Length)}). ���� ����� �������� � fallback-�������.");
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
