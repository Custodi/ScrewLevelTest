using UnityEditor;
using UnityEngine;

[ExecuteInEditMode]
public class BoltPointGizmo : MonoBehaviour
{
    public GameObject MeshGameObject;
    public int Depth = -1;
    public Vector3 Normal = Vector3.up;

    public bool SnapToSurface = false; // новое поле

    public float gizmoSize = 0.03f;
    public Color gizmoColor = Color.yellow;
    public Color normalColor = Color.cyan;

    public bool ShowPointGismos;

    private void OnDrawGizmos()
    {
        if (ShowPointGismos == false) return;

        Gizmos.color = gizmoColor;
        Gizmos.DrawSphere(transform.position, HandleUtility_GetGizmoSize(transform.position, gizmoSize));

        Gizmos.color = normalColor;
        Vector3 tip = transform.position + Normal.normalized * HandleUtility_GetGizmoSize(transform.position, gizmoSize) * 6f;
        Gizmos.DrawLine(transform.position, tip);

#if UNITY_EDITOR
        UnityEditor.Handles.Label(
            transform.position + Vector3.up * HandleUtility_GetGizmoSize(transform.position, gizmoSize) * 2f,
            $"D:{Depth}");
#endif
    }

    private float HandleUtility_GetGizmoSize(Vector3 position, float size)
    {
        Camera cam = Camera.current;
        if (cam == null) cam = Camera.main;
        if (cam == null) return size;
        float dist = Vector3.Distance(position, cam.transform.position);
        return size * dist;
    }

#if UNITY_EDITOR
    /// <summary>
    /// Пересчитывает нормаль на основе ближайшей поверхности MeshCollider.
    /// Если SnapToSurface = true, перемещает точку на поверхность.
    /// </summary>
    public void RecalculateNormal()
    {
        if (MeshGameObject == null) return;

        var mf = MeshGameObject.GetComponent<MeshFilter>();
        if (mf == null || mf.sharedMesh == null) return;

        var mc = MeshGameObject.GetComponent<MeshCollider>();
        bool tempAdded = false;
        if (mc == null)
        {
            mc = MeshGameObject.AddComponent<MeshCollider>();
            mc.sharedMesh = mf.sharedMesh;
            mc.convex = false;
            tempAdded = true;
        }

        Vector3 pos = transform.position;
        Vector3 dir = (pos - MeshGameObject.transform.position).normalized;
        Ray ray = new Ray(pos + dir * 0.01f, -dir);

        if (mc.Raycast(ray, out RaycastHit hit, 10f))
        {
            Normal = hit.normal;

            if (SnapToSurface)
            {
                transform.position = hit.point;
            }
        }

        if (tempAdded)
        {
            Object.DestroyImmediate(mc);
        }

        UnityEditor.EditorUtility.SetDirty(this);
    }
#endif
}

[CustomEditor(typeof(BoltPointGizmo))]
public class BoltPointGizmoEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        BoltPointGizmo giz = (BoltPointGizmo)target;

        EditorGUILayout.Space();
        if (GUILayout.Button("Пересчитать нормаль"))
        {
            giz.RecalculateNormal();
        }
    }
}

