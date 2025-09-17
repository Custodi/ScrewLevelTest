using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

[Serializable]
public class BoltInfo
{
    public string bolt_id;
    public int color_id;
    public int depth;
    public List<string> parents = new List<string>();
    public List<string> pawns = new List<string>();
}

[Serializable]
public class BoltInfoArray
{
    public List<BoltInfo> bolts = new List<BoltInfo>();
}

public class BoltAnalyzer : MonoBehaviour
{
    [Header("References")]
    public Transform boltsParent; // �������� ��� ������ ������

    [Header("JSON Data")]
    [TextArea(5, 15)] public string generatedJson;
    [TextArea(5, 15)] public string inputJson;

    /// <summary>
    /// �������� ����� � ������ JSON.
    /// </summary>
    [ContextMenu("Generate JSON From Scene")]
    public void GenerateJson()
    {
        Bolt[] bolts = boltsParent.GetComponentsInChildren<Bolt>(true);
        var result = new BoltInfoArray();

        foreach (var b in bolts)
        {
            var info = new BoltInfo
            {
                bolt_id = b.name,
                color_id = b.colorIndex,
                depth = b.depth
            };

            // parents = ��� �����, � ������� ParentMeshId == ��� BlockingMeshId
            info.parents = bolts
                .Where(o => o.parentMeshId == b.blockingMeshId)
                .Select(o => o.name)
                .ToList();

            // pawns = ��� �����, � ������� BlockingMeshId == ��� ParentMeshId
            info.pawns = bolts
                .Where(o => o.blockingMeshId == b.parentMeshId)
                .Select(o => o.name)
                .ToList();

            result.bolts.Add(info);
        }

        generatedJson = JsonUtility.ToJson(result, true);
#if UNITY_EDITOR
        EditorUtility.SetDirty(this);
#endif
    }

    /// <summary>
    /// ����������� inputJson, ��������� ����� � ������� �������.
    /// </summary>
    [ContextMenu("Analyze JSON And Sort Bolts")]
    public void AnalyzeAndSort()
    {
        if (string.IsNullOrEmpty(inputJson))
        {
            Debug.LogError("[BoltAnalyzer] inputJson ����.");
            return;
        }

        BoltInfoArray data;
        try
        {
            data = JsonUtility.FromJson<BoltInfoArray>(inputJson);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[BoltAnalyzer] ������ �������� JSON: {ex}");
            return;
        }

        // 1) ������������� ������ ������
        var ordered = data.bolts.OrderBy(b => b.depth)
            .ThenBy(b => b.parents.Count)
            .ThenByDescending(b => b.pawns.Count)
            .ToList();

        Debug.Log("[BoltAnalyzer] ������������� �����:");
        foreach (var b in ordered)
            Debug.Log($"Bolt {b.bolt_id} | depth={b.depth}, parents={b.parents.Count}, pawns={b.pawns.Count}");

        // 2) ��������� ������ ������ �����
        var triples = new List<List<BoltInfo>>();
        var remaining = new List<BoltInfo>(ordered);

        while (remaining.Count > 0)
        {
            var first = remaining[0];
            var sameColor = remaining.Where(b => b.color_id == first.color_id).Take(3).ToList();

            if (sameColor.Count == 3)
            {
                triples.Add(sameColor);
                foreach (var t in sameColor)
                    remaining.Remove(t);
            }
            else
            {
                // ���� �� ������� �� ������, ������� ��� ���������� ����� �����
                remaining.RemoveAll(b => b.color_id == first.color_id);
            }
        }

        // 3) ���������� ����� (��������)
        var sortedTriples = triples.OrderBy(tr =>
        {
            int sumDepth = tr.Sum(b => b.depth);
            return sumDepth;
        }).ThenBy(tr =>
        {
            float avg = (float)tr.Average(b => b.depth);
            float variance = tr.Sum(b => Mathf.Abs(b.depth - avg));
            return variance;
        }).ToList();

        // ����� ������� ������ ��������
        Debug.Log("[BoltAnalyzer] ����������� ������� ��������:");
        foreach (var triple in sortedTriples)
        {
            string ids = string.Join(", ", triple.Select(b => b.bolt_id));
            int color = triple[0].color_id;
            Debug.Log($"������� (���� {color}): {ids}");
        }
    }
}
