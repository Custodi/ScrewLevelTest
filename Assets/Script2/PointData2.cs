
using UnityEngine;

[System.Serializable]
public class PointData2
{
    public Vector3 Position;
    public Vector3 Normal;
    public int PointID;
    public int Depth;
    public Mesh ParentMesh;
    public Mesh HitMesh;
}