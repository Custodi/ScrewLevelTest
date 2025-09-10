using UnityEngine;

[ExecuteInEditMode]
public class BoltPointGizmo : MonoBehaviour
{
    public GameObject MeshGameObject;
    public int Depth = -1;
    public Vector3 Normal = Vector3.up;

    public float gizmoSize = 0.03f;
    public Color gizmoColor = Color.yellow;
    public Color normalColor = Color.cyan;

    private void OnDrawGizmos()
    {
        Gizmos.color = gizmoColor;
        Gizmos.DrawSphere(transform.position, HandleUtility_GetGizmoSize(transform.position, gizmoSize));
        // normal arrow
        Gizmos.color = normalColor;
        Vector3 tip = transform.position + Normal.normalized * HandleUtility_GetGizmoSize(transform.position, gizmoSize) * 6f;
        Gizmos.DrawLine(transform.position, tip);
#if UNITY_EDITOR
        UnityEditor.Handles.Label(transform.position + Vector3.up * HandleUtility_GetGizmoSize(transform.position, gizmoSize) * 2f, $"D:{Depth}");
#endif
    }

    private float HandleUtility_GetGizmoSize(Vector3 position, float size)
    {
        // approximate screen-size independent gizmo
        Camera cam = Camera.current;
        if (cam == null) cam = Camera.main;
        if (cam == null) return size;
        float dist = Vector3.Distance(position, cam.transform.position);
        return size * dist;
    }
}
