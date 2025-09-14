using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class BoltPointData
{
    public string id;
    public int depth;
    public string parentMeshId;
    public string blockedByMeshId;
}

[Serializable]
public class BoltPointsRoot
{
    public BoltPointData[] points;
}

[Serializable]
public class BasketData
{
    public string colorName;
    public int colorIndex;
    public int basketIndex;
    public string[] boltPointIds;
}

[Serializable]
public class BasketsWrapper
{
    public BasketData[] baskets;
}

public class BoltGenerator : MonoBehaviour
{
    [Header("JSON Input")]
    [Tooltip("����� ������� JSON TextAsset (���������������)")]
    public TextAsset pointsJsonAsset;

    [TextArea(4, 10)]
    [Tooltip("��� �������� JSON ���� (����� �����������, ���� TextAsset ����)")]
    public string pointsJsonText;

    [Header("Generator Settings")]
    [Tooltip("���������� �������� (������ ���� ������ 3)")]
    public int totalBolts = 12;

    [Tooltip("���������� ������ (�� ��������� 6). ����� ������������� ��������� �� ���������� ��������, ���� ������.")]
    public int totalColors = 6;

    [Tooltip("�����������: ����������� seed ��� �����������������")]
    public bool useSeed = false;
    public int randomSeed = 12345;

    [Header("Prefab & Parents")]
    public GameObject boltPrefab;
    public Transform boltsParent;

    [Tooltip("������������ ������, ���������� ��� ����� ��� ������ (Transform)")]
    public Transform boltPointsParent;

    [Tooltip("������������ ������, ���������� ��� ���� (MeshRenderer)")]
    public Transform meshesParent;

    [Header("Color Materials")]
    [Tooltip("��������� ������. ������ ���� >= totalColors (��� ���� �� >= ��������� ���������� ������ ����� ����������).")]
    public List<Material> colorMaterials = new List<Material>();

    [HideInInspector]
    public string lastBasketsJson = "";

    [ContextMenu("Generate Level")]
    public void GenerateFromInspector()
    {
        string json = (pointsJsonAsset != null && !string.IsNullOrEmpty(pointsJsonAsset.text)) ? pointsJsonAsset.text : pointsJsonText;
        Generate(json);
    }

    public void Generate(string jsonInput)
    {
        if (string.IsNullOrEmpty(jsonInput))
        {
            Debug.LogError("[BoltGenerator] ��� JSON ����� (pointsJsonAsset ��� pointsJsonText).");
            return;
        }

        if (totalBolts <= 0 || totalBolts % 3 != 0)
        {
            Debug.LogError("[BoltGenerator] totalBolts ������ ���� ������������� � ������� 3.");
            return;
        }

        if (boltPrefab == null)
        {
            Debug.LogError("[BoltGenerator] boltPrefab �� �����.");
            return;
        }

        BoltPointsRoot root = null;
        try
        {
            root = JsonUtility.FromJson<BoltPointsRoot>(jsonInput);
        }
        catch (Exception e)
        {
            Debug.LogError("[BoltGenerator] ������ �������� JSON: " + e.Message);
            return;
        }

        if (root == null || root.points == null || root.points.Length == 0)
        {
            Debug.LogError("[BoltGenerator] � JSON ��� ����� (points).");
            return;
        }

        // 1) �������� ������ Transform ����� �� ��������
        var transformByName = new Dictionary<string, Transform>(StringComparer.Ordinal);
        if (boltPointsParent != null)
        {
            var allPoints = boltPointsParent.GetComponentsInChildren<Transform>();
            foreach (var t in allPoints)
            {
                if (!transformByName.ContainsKey(t.name))
                    transformByName.Add(t.name, t);
            }
        }
        else
        {
            Debug.LogError("[BoltGenerator] boltPointsParent �� �����.");
            return;
        }

        // 2) �������� ���� (���� ����� ��� �������� parentMeshId)
        var meshByName = new Dictionary<string, MeshRenderer>(StringComparer.Ordinal);
        if (meshesParent != null)
        {
            var allMeshes = meshesParent.GetComponentsInChildren<MeshRenderer>();
            foreach (var mr in allMeshes)
            {
                if (!meshByName.ContainsKey(mr.name))
                    meshByName.Add(mr.name, mr);
            }
        }

        // 3) ��������� usablePoints
        var usablePoints = new List<BoltPointData>();
        foreach (var p in root.points)
        {
            if (p == null) continue;
            if (!string.IsNullOrEmpty(p.blockedByMeshId))
                continue;

            if (!transformByName.ContainsKey(p.id))
            {
                Debug.LogWarning($"[BoltGenerator] ��� Transform ��� ����� � id '{p.id}' � ����� ���������.");
                continue;
            }
            usablePoints.Add(p);
        }

        if (usablePoints.Count < totalBolts)
        {
            Debug.LogError($"[BoltGenerator] ���������� ����� ��� ���������� ������: ����� {totalBolts}, ���� {usablePoints.Count}");
            return;
        }

        // 4) ������� � �����
        int basketCount = totalBolts / 3;
        if (totalColors <= 0) totalColors = 1;

        if (totalColors > basketCount)
        {
            Debug.LogWarning($"[BoltGenerator] totalColors ({totalColors}) ������ ���������� ������ ({basketCount}). ������� totalColors = basketCount.");
            totalColors = basketCount;
        }

        if (colorMaterials == null || colorMaterials.Count < totalColors)
        {
            Debug.LogError($"[BoltGenerator] ������������ ����������: ����� {totalColors}, ���� {(colorMaterials == null ? 0 : colorMaterials.Count)}.");
            return;
        }

        int[] basketsPerColor = new int[totalColors];
        int baseCount = basketCount / totalColors;
        int remainder = basketCount % totalColors;
        for (int i = 0; i < totalColors; i++)
            basketsPerColor[i] = baseCount + (i < remainder ? 1 : 0);

        var baskets = new List<BasketData>(basketCount);
        int basketIdx = 0;
        for (int colorIdx = 0; colorIdx < totalColors; colorIdx++)
        {
            string colorName = $"Color_{colorIdx}";
            for (int k = 0; k < basketsPerColor[colorIdx]; k++)
            {
                baskets.Add(new BasketData
                {
                    colorName = colorName,
                    colorIndex = colorIdx,
                    basketIndex = basketIdx,
                    boltPointIds = new string[0]
                });
                basketIdx++;
            }
        }

        // 5) ����� �����
        System.Random rng = useSeed ? new System.Random(randomSeed) : new System.Random();
        var shuffled = new List<BoltPointData>(usablePoints);
        for (int i = shuffled.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]);
        }
        var chosen = shuffled.GetRange(0, totalBolts);

        // 6) ������� ������
        var boltIdLists = new List<List<string>>(basketCount);
        for (int i = 0; i < basketCount; i++) boltIdLists.Add(new List<string>());

        for (int i = 0; i < totalBolts; i++)
        {
            int currentBasketIndex = i / 3;
            var basket = baskets[currentBasketIndex];
            var point = chosen[i];

            Transform pointT = transformByName[point.id];
            GameObject boltGO = Instantiate(boltPrefab, pointT.position, Quaternion.identity, boltsParent);

            Vector3 normal = pointT.up;
            if (normal == Vector3.zero) normal = Vector3.up;
            boltGO.transform.rotation = Quaternion.LookRotation(normal);

            boltGO.name = $"Bolt_{point.id}_B{currentBasketIndex}C{basket.colorIndex}";

            var rends = boltGO.GetComponentsInChildren<Renderer>();
            foreach (var r in rends)
                r.material = colorMaterials[basket.colorIndex];

            boltIdLists[currentBasketIndex].Add(point.id);
        }

        for (int i = 0; i < basketCount; i++)
            baskets[i].boltPointIds = boltIdLists[i].ToArray();

        var wrapper = new BasketsWrapper { baskets = baskets.ToArray() };
        lastBasketsJson = JsonUtility.ToJson(wrapper, true);
        Debug.Log("[BoltGenerator] Baskets JSON:\n" + lastBasketsJson);
    }
}
