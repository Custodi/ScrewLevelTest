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
    public Transform boltsParent; // Родитель для поиска болтов

    [Header("JSON Data")]
    [TextArea(5, 15)] public string generatedJson;
    [TextArea(5, 15)] public string inputJson;

    /// <summary>
    /// Собирает болты и строит JSON.
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

            // parents = все болты, у которых ParentMeshId == наш BlockingMeshId
            info.parents = bolts
                .Where(o => o.parentMeshId == b.blockingMeshId)
                .Select(o => o.name)
                .ToList();

            // pawns = все болты, у которых BlockingMeshId == наш ParentMeshId
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
    /// Анализирует inputJson, сортирует болты и выводит порядок.
    /// </summary>
    [ContextMenu("Analyze JSON And Sort Bolts")]
    public void AnalyzeAndSort()
    {
        if (string.IsNullOrEmpty(inputJson))
        {
            Debug.LogError("[BoltAnalyzer] inputJson пуст.");
            return;
        }

        BoltInfoArray data;
        try
        {
            data = JsonUtility.FromJson<BoltInfoArray>(inputJson);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[BoltAnalyzer] Ошибка парсинга JSON: {ex}");
            return;
        }

        // 1) Упорядочиваем массив болтов
        var ordered = data.bolts.OrderBy(b => b.depth)
            .ThenBy(b => b.parents.Count)
            .ThenByDescending(b => b.pawns.Count)
            .ToList();

        Debug.Log("[BoltAnalyzer] Упорядоченные болты:");
        foreach (var b in ordered)
            Debug.Log($"Bolt {b.bolt_id} | depth={b.depth}, parents={b.parents.Count}, pawns={b.pawns.Count}");

        // 2) Формируем тройки одного цвета
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
                // Если не хватает до тройки, удаляем все оставшиеся этого цвета
                remaining.RemoveAll(b => b.color_id == first.color_id);
            }
        }

        // 3) Сортировка троек (корзинок)
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

        // Вывод порядка цветов корзинок
        Debug.Log("[BoltAnalyzer] Оптимальный порядок корзинок:");
        foreach (var triple in sortedTriples)
        {
            string ids = string.Join(", ", triple.Select(b => b.bolt_id));
            int color = triple[0].color_id;
            Debug.Log($"Корзина (цвет {color}): {ids}");
        }
    }
}
