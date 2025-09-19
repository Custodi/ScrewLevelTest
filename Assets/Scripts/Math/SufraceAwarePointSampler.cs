using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class SurfaceAwarePointSampler : MonoBehaviour
{
    [Header("Sampling Settings")]
    public int totalPoints = 6;
    public float normalAngleThreshold = 30f;

    [Header("Visualization")]
    public bool showGizmos = true;
    public bool colorizeSurfaces = true;
    public float pointSize = 0.05f;

    // Internal data
    private Mesh mesh;
    private List<MeshSurface> surfaces;
    private List<Vector3> sampledPoints;
    private List<Color> pointColors;

    void Start()
    {
        MeshFilter meshFilter = GetComponent<MeshFilter>();
        if (meshFilter != null)
        {
            mesh = meshFilter.mesh;
            AnalyzeAndSample();
        }
    }

    void AnalyzeAndSample()
    {
        // Step 1: Find surfaces by analyzing normals
        surfaces = FindSurfacesByNormals(mesh, normalAngleThreshold);

        // Step 2: Distribute points evenly between surfaces
        sampledPoints = new List<Vector3>();
        pointColors = new List<Color>();

        // Calculate points per surface
        int pointsPerSurface = totalPoints / surfaces.Count;
        int remainder = totalPoints % surfaces.Count;

        // Generate a random color for each surface
        Color[] surfaceColors = new Color[surfaces.Count];
        for (int i = 0; i < surfaces.Count; i++)
        {
            surfaceColors[i] = Random.ColorHSV(0f, 1f, 0.7f, 1f, 0.8f, 1f);
        }

        // Sample points for each surface
        for (int i = 0; i < surfaces.Count; i++)
        {
            int pointsForThisSurface = pointsPerSurface + (i < remainder ? 1 : 0);

            if (pointsForThisSurface > 0)
            {
                Vector3[] surfacePoints = SamplePointsOnSurface(mesh, surfaces[i], pointsForThisSurface);
                sampledPoints.AddRange(surfacePoints);

                // Assign color to each point based on its surface
                for (int j = 0; j < surfacePoints.Length; j++)
                {
                    pointColors.Add(surfaceColors[i]);
                }
            }
        }
    }

    void OnDrawGizmos()
    {
        if (!showGizmos || mesh == null || surfaces == null) return;

        // Visualize surfaces with colors
        if (colorizeSurfaces)
        {
            Gizmos.matrix = transform.localToWorldMatrix;

            for (int i = 0; i < surfaces.Count; i++)
            {
                Color surfaceColor = Random.ColorHSV(0f, 1f, 0.7f, 1f, 0.8f, 1f, 0.3f, 0.5f);
                Gizmos.color = surfaceColor;

                foreach (int triIndex in surfaces[i].triangleIndices)
                {
                    int baseIndex = triIndex * 3;
                    Vector3 v1 = mesh.vertices[mesh.triangles[baseIndex]];
                    Vector3 v2 = mesh.vertices[mesh.triangles[baseIndex + 1]];
                    Vector3 v3 = mesh.vertices[mesh.triangles[baseIndex + 2]];

                    Gizmos.DrawLine(v1, v2);
                    Gizmos.DrawLine(v2, v3);
                    Gizmos.DrawLine(v3, v1);
                }
            }

            Gizmos.matrix = Matrix4x4.identity;
        }

        // Visualize sampled points
        if (sampledPoints != null)
        {
            for (int i = 0; i < sampledPoints.Count; i++)
            {
                Vector3 worldPoint = transform.TransformPoint(sampledPoints[i]);

                if (colorizeSurfaces && i < pointColors.Count)
                {
                    Gizmos.color = pointColors[i];
                }
                else
                {
                    Gizmos.color = Color.red;
                }

                Gizmos.DrawSphere(worldPoint, pointSize);
            }
        }
    }

    // Surface analysis and sampling methods
    private List<MeshSurface> FindSurfacesByNormals(Mesh mesh, float angleThreshold)
    {
        Vector3[] vertices = mesh.vertices;
        Vector3[] normals = mesh.normals;
        int[] triangles = mesh.triangles;

        if (normals == null || normals.Length == 0)
        {
            mesh.RecalculateNormals();
            normals = mesh.normals;
        }

        Dictionary<int, List<int>> triangleAdjacency = BuildTriangleAdjacency(mesh);
        return GroupTrianglesIntoSurfaces(vertices, normals, triangles, triangleAdjacency, angleThreshold);
    }

    private Dictionary<int, List<int>> BuildTriangleAdjacency(Mesh mesh)
    {
        Dictionary<int, List<int>> adjacency = new Dictionary<int, List<int>>();
        Dictionary<Edge, int> edgeToTriangle = new Dictionary<Edge, int>();

        for (int i = 0; i < mesh.triangles.Length; i += 3)
        {
            int triangleIndex = i / 3;
            adjacency[triangleIndex] = new List<int>();

            for (int j = 0; j < 3; j++)
            {
                int v1 = mesh.triangles[i + j];
                int v2 = mesh.triangles[i + (j + 1) % 3];
                Edge edge = new Edge(Mathf.Min(v1, v2), Mathf.Max(v1, v2));

                if (edgeToTriangle.TryGetValue(edge, out int otherTriangle))
                {
                    adjacency[triangleIndex].Add(otherTriangle);
                    adjacency[otherTriangle].Add(triangleIndex);
                }
                else
                {
                    edgeToTriangle[edge] = triangleIndex;
                }
            }
        }

        return adjacency;
    }

    private List<MeshSurface> GroupTrianglesIntoSurfaces(Vector3[] vertices, Vector3[] normals,
        int[] triangles, Dictionary<int, List<int>> adjacency, float angleThreshold)
    {
        List<MeshSurface> surfaces = new List<MeshSurface>();
        bool[] visited = new bool[triangles.Length / 3];
        float cosThreshold = Mathf.Cos(angleThreshold * Mathf.Deg2Rad);

        for (int i = 0; i < triangles.Length; i += 3)
        {
            int triangleIndex = i / 3;
            if (visited[triangleIndex]) continue;

            MeshSurface surface = new MeshSurface();
            Queue<int> queue = new Queue<int>();
            queue.Enqueue(triangleIndex);
            visited[triangleIndex] = true;

            while (queue.Count > 0)
            {
                int currentTriangle = queue.Dequeue();
                surface.triangleIndices.Add(currentTriangle);

                foreach (int neighbor in adjacency[currentTriangle])
                {
                    if (visited[neighbor]) continue;

                    if (AreNormalsSimilar(normals, triangles, currentTriangle, neighbor, cosThreshold))
                    {
                        visited[neighbor] = true;
                        queue.Enqueue(neighbor);
                    }
                }
            }

            surfaces.Add(surface);
        }

        return surfaces;
    }

    private bool AreNormalsSimilar(Vector3[] normals, int[] triangles,
        int tri1, int tri2, float cosThreshold)
    {
        Vector3 normal1 = (normals[triangles[tri1 * 3]] +
                          normals[triangles[tri1 * 3 + 1]] +
                          normals[triangles[tri1 * 3 + 2]]) / 3f;

        Vector3 normal2 = (normals[triangles[tri2 * 3]] +
                          normals[triangles[tri2 * 3 + 1]] +
                          normals[triangles[tri2 * 3 + 2]]) / 3f;

        return Vector3.Dot(normal1.normalized, normal2.normalized) >= cosThreshold;
    }

    private Vector3[] SamplePointsOnSurface(Mesh mesh, MeshSurface surface, int pointCount)
    {
        Vector3[] vertices = mesh.vertices;
        int[] triangles = mesh.triangles;

        // Calculate cumulative areas for triangles in this surface
        float[] cumulativeAreas = new float[surface.triangleIndices.Count];
        float totalArea = 0f;

        for (int i = 0; i < surface.triangleIndices.Count; i++)
        {
            int triIndex = surface.triangleIndices[i];
            int baseIndex = triIndex * 3;
            Vector3 v1 = vertices[triangles[baseIndex]];
            Vector3 v2 = vertices[triangles[baseIndex + 1]];
            Vector3 v3 = vertices[triangles[baseIndex + 2]];

            float area = Vector3.Cross(v2 - v1, v3 - v1).magnitude / 2f;
            totalArea += area;
            cumulativeAreas[i] = totalArea;
        }

        // Generate points
        Vector3[] points = new Vector3[pointCount];
        for (int i = 0; i < pointCount; i++)
        {
            float randomValue = Random.Range(0f, totalArea);
            int surfaceTriIndex = System.Array.BinarySearch(cumulativeAreas, randomValue);
            if (surfaceTriIndex < 0) surfaceTriIndex = ~surfaceTriIndex;

            int selectedTri = surface.triangleIndices[surfaceTriIndex];
            points[i] = GetRandomPointOnTriangle(mesh, selectedTri);
        }

        return points;
    }

    private Vector3 GetRandomPointOnTriangle(Mesh mesh, int triangleIndex)
    {
        Vector3[] vertices = mesh.vertices;
        int[] triangles = mesh.triangles;

        int baseIndex = triangleIndex * 3;
        Vector3 v1 = vertices[triangles[baseIndex]];
        Vector3 v2 = vertices[triangles[baseIndex + 1]];
        Vector3 v3 = vertices[triangles[baseIndex + 2]];

        float r1 = Random.Range(0f, 1f);
        float r2 = Random.Range(0f, 1f);

        if (r1 + r2 > 1f)
        {
            r1 = 1 - r1;
            r2 = 1 - r2;
        }

        return v1 + r1 * (v2 - v1) + r2 * (v3 - v1);
    }

    // Helper classes
    public class MeshSurface
    {
        public List<int> triangleIndices = new List<int>();
    }

    public struct Edge
    {
        public int v1, v2;

        public Edge(int vertex1, int vertex2)
        {
            v1 = Mathf.Min(vertex1, vertex2);
            v2 = Mathf.Max(vertex1, vertex2);
        }

        public override bool Equals(object obj) => obj is Edge other && v1 == other.v1 && v2 == other.v2;
        public override int GetHashCode() => (v1 << 16) + v2;
    }

    // Public method to regenerate points (useful for editor)
    public void Regenerate()
    {
        MeshFilter meshFilter = GetComponent<MeshFilter>();
        if (meshFilter != null)
        {
            mesh = meshFilter.mesh;
            AnalyzeAndSample();
        }
    }

    // Editor button
#if UNITY_EDITOR
    [ContextMenu("Regenerate Points")]
    void RegeneratePoints()
    {
        Regenerate();
    }
#endif
}