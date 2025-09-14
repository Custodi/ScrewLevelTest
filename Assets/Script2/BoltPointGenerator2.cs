using System.Collections.Generic;
using UnityEngine;
using static BoltPointGenerator;

public enum ArrangementType
{
    Linear,
    Grid,
    Radial
}

// Основной генератор
public class BoltPointGenerator2 : MonoBehaviour
{
    public int TotalPoints = 10;
    public bool GenerateOnStart = false;
    public float GizmoSize = 0.1f;
    public Color GizmoColor = Color.red;

    private List<PointData2> generatedPoints = new List<PointData2>();
    private Dictionary<Mesh, MeshDepth2> meshData = new Dictionary<Mesh, MeshDepth2>();

    void Start()
    {
        if (GenerateOnStart) GeneratePoints();
    }

    public void GeneratePoints()
    {
        InitializeMeshData();
        DistributePoints();
        ValidatePoints();
    }

    private void InitializeMeshData()
    {
        meshData.Clear();
        MeshDepth2[] meshDepths = FindObjectsOfType<MeshDepth2>();

        foreach (MeshDepth2 md in meshDepths)
        {
            MeshFilter mf = md.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null)
            {
                meshData.Add(mf.sharedMesh, md);
            }
        }
    }

    private void DistributePoints()
    {
        // Распределение точек по мешам на основе объема
        Dictionary<Mesh, int> pointsPerMesh = new Dictionary<Mesh, int>();
        List<Mesh> meshes = new List<Mesh>(meshData.Keys);

        // Сортируем меши по объему
        meshes.Sort((a, b) =>
            GetMeshVolume(b).CompareTo(GetMeshVolume(a)));

        // Распределяем точки
        int pointsLeft = TotalPoints;
        foreach (Mesh mesh in meshes)
        {
            float volumeShare = GetMeshVolume(mesh) / GetTotalVolume();
            int pointsForMesh = Mathf.RoundToInt(TotalPoints * volumeShare);
            pointsForMesh = Mathf.Min(pointsForMesh, pointsLeft);
            pointsPerMesh.Add(mesh, pointsForMesh);
            pointsLeft -= pointsForMesh;
        }
    }

    private void ValidatePoints()
    {
        // Проверка точек по глубине
        foreach (PointData2 point in generatedPoints.ToArray())
        {
            if (!CheckPointDepth(point))
            {
                generatedPoints.Remove(point);
                RegeneratePoint(point);
            }
        }
    }

    private bool CheckPointDepth(PointData2 point)
    {
        RaycastHit hit;
        if (Physics.Raycast(point.Position, point.Normal, out hit))
        {
            MeshCollider hitCollider = hit.collider as MeshCollider;
            if (hitCollider != null)
            {
                Mesh hitMesh = hitCollider.sharedMesh;
                if (meshData.ContainsKey(hitMesh))
                {
                    point.HitMesh = hitMesh;
                    return meshData[hitMesh].Depth < point.Depth;
                }
            }
        }
        return true;
    }

    private void RegeneratePoint(PointData2 originalPoint)
    {
        // Регенерация точки с учетом новых требований
        Mesh mesh = originalPoint.ParentMesh;
        MeshDepth2 md = meshData[mesh];

        Vector3 newPosition = CalculatePointPosition(mesh, md);
        Vector3 newNormal = CalculatePointNormal(newPosition, mesh);

        PointData2 newPoint = new PointData2()
        {
            Position = newPosition,
            Normal = newNormal,
            ParentMesh = mesh,
            Depth = md.Depth
        };

        if (CheckPointDepth(newPoint))
        {
            generatedPoints.Add(newPoint);
        }
        else
        {
            Debug.LogError("Cannot generate valid point for mesh: " + mesh.name);
        }
    }

    private Vector3 CalculatePointPosition(Mesh mesh, MeshDepth2 md)
    {
        Bounds bounds = mesh.bounds;
        Vector3 center = bounds.center;

        switch (md.Arrangement)
        {
            case ArrangementType.Linear:
                return CalculateLinearPosition(bounds);
            case ArrangementType.Grid:
                return CalculateGridPosition(bounds);
            case ArrangementType.Radial:
                return CalculateRadialPosition(bounds);
            default:
                return center;
        }
    }

    private Vector3 CalculatePointNormal(Vector3 position, Mesh mesh)
    {
        // Проецируем точку на поверхность меша
        RaycastHit hit;
        if (Physics.Raycast(position + Vector3.up * 10, Vector3.down, out hit))
        {
            if (hit.collider.GetComponent<Mesh>() == mesh)
            {
                return hit.normal;
            }
        }
        return Vector3.up;
    }

    // Вспомогательные методы для расчетов
    private float GetMeshVolume(Mesh mesh)
    {
        return mesh.bounds.size.x * mesh.bounds.size.y * mesh.bounds.size.z;
    }

    private float GetTotalVolume()
    {
        float total = 0;
        foreach (Mesh mesh in meshData.Keys)
        {
            total += GetMeshVolume(mesh);
        }
        return total;
    }

    private Vector3 CalculateLinearPosition(Bounds bounds)
    {
        // Реализация линейного распределения
        return bounds.center;
    }

    private Vector3 CalculateGridPosition(Bounds bounds)
    {
        // Реализация сетчатого распределения
        return bounds.center;
    }

    private Vector3 CalculateRadialPosition(Bounds bounds)
    {
        // Реализация радиального распределения
        return bounds.center;
    }

    // Отрисовка гизмо для визуализации точек
    private void OnDrawGizmos()
    {
        if (generatedPoints == null) return;

        Gizmos.color = GizmoColor;

        foreach (PointData2 point in generatedPoints)
        {
            // Рисуем сферу в позиции точки
            Gizmos.DrawSphere(point.Position, GizmoSize);

            // Рисуем линию, показывающую нормаль
            Gizmos.DrawLine(point.Position, point.Position + point.Normal * GizmoSize * 2);
        }
    }
}

// Визуализатор для отладки
#if UNITY_EDITOR
[UnityEditor.CustomEditor(typeof(BoltPointGenerator2))]
public class BoltPointGeneratorEditor : UnityEditor.Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        BoltPointGenerator2 generator = (BoltPointGenerator2)target;
        if (GUILayout.Button("Generate Points"))
        {
            generator.GeneratePoints();
        }
    }
}
#endif