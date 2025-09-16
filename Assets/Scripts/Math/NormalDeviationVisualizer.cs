using UnityEngine;

[ExecuteInEditMode]
public class NormalDeviationVisualizer : MonoBehaviour
{
    [Range(0.1f, 10.0f)] public float sensitivity = 1.0f;
    public float gizmoSize = 0.05f;
    public bool showGizmos = true;
    public float checkDistance = 0.1f;

    private Mesh mesh;
    private Vector3[] vertices;
    private Vector3[] normals;
    private int[] triangles;
    private float[] normalChanges;
    private Vector3[] faceCenters;
    private Vector3[] faceNormals;

    void OnEnable()
    {
        UpdateMeshData();
    }

    void OnValidate()
    {
        UpdateMeshData();
    }

    void UpdateMeshData()
    {
        MeshFilter meshFilter = GetComponent<MeshFilter>();
        if (meshFilter != null && meshFilter.sharedMesh != null)
        {
            mesh = meshFilter.sharedMesh;
            vertices = mesh.vertices;
            normals = mesh.normals;
            triangles = mesh.triangles;

            CalculateFaceNormals();
            CalculateNormalChanges();
        }
    }

    void CalculateFaceNormals()
    {
        int triangleCount = triangles.Length / 3;
        faceCenters = new Vector3[triangleCount];
        faceNormals = new Vector3[triangleCount];

        for (int i = 0; i < triangleCount; i++)
        {
            int index1 = triangles[i * 3];
            int index2 = triangles[i * 3 + 1];
            int index3 = triangles[i * 3 + 2];

            Vector3 v1 = vertices[index1];
            Vector3 v2 = vertices[index2];
            Vector3 v3 = vertices[index3];

            // Вычисляем центр треугольника
            faceCenters[i] = (v1 + v2 + v3) / 3f;

            // Вычисляем нормаль треугольника
            Vector3 side1 = v2 - v1;
            Vector3 side2 = v3 - v1;
            faceNormals[i] = Vector3.Cross(side1, side2).normalized;
        }
    }

    void CalculateNormalChanges()
    {
        int triangleCount = triangles.Length / 3;
        normalChanges = new float[triangleCount];

        for (int i = 0; i < triangleCount; i++)
        {
            Vector3 currentNormal = faceNormals[i];
            Vector3 currentCenter = faceCenters[i];
            float totalChange = 0f;
            int neighborCount = 0;

            // Ищем соседние треугольники в пределах checkDistance
            for (int j = 0; j < triangleCount; j++)
            {
                if (i == j) continue;

                float distance = Vector3.Distance(currentCenter, faceCenters[j]);
                if (distance <= checkDistance)
                {
                    // Вычисляем изменение нормали между текущим и соседним треугольником
                    float dotProduct = Vector3.Dot(currentNormal, faceNormals[j]);
                    float angleChange = Mathf.Acos(Mathf.Clamp(dotProduct, -1f, 1f)) * Mathf.Rad2Deg;

                    totalChange += angleChange;
                    neighborCount++;
                }
            }

            // Вычисляем среднее изменение нормали
            if (neighborCount > 0)
            {
                normalChanges[i] = Mathf.Clamp01((totalChange / neighborCount) / 90f * sensitivity);
            }
            else
            {
                normalChanges[i] = 0f;
            }
        }
    }

    void OnDrawGizmos()
    {
        if (!showGizmos || normalChanges == null || faceCenters == null)
            return;

        for (int i = 0; i < faceCenters.Length; i++)
        {
            // Преобразуем в мировые координаты
            Vector3 worldPos = transform.TransformPoint(faceCenters[i]);

            // Интерполируем цвет от белого к красному
            Color gizmoColor = Color.Lerp(Color.white, Color.red, normalChanges[i]);

            Gizmos.color = gizmoColor;
            Gizmos.DrawSphere(worldPos, gizmoSize);

            // Также рисуем нормаль треугольника
            Gizmos.color = Color.blue;
            Gizmos.DrawRay(worldPos, transform.TransformDirection(faceNormals[i]) * gizmoSize * 2);
        }
    }

    [ContextMenu("Recalculate")]
    void Recalculate()
    {
        UpdateMeshData();
    }
}