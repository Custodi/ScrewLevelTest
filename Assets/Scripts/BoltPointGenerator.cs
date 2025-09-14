using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using Unity.VisualScripting;
using UnityEngine;

[ExecuteInEditMode]
public class BoltPointGenerator : MonoBehaviour
{
    [Header("Show Point Gismos")]
    public bool ShowPointGismos;

    [Header("Target Object")]
    public Transform Root;

    // --- сериализуемые структуры для JsonUtility ---
    [Serializable]
    public class GraphData
    {
        public List<Layer> layers = new List<Layer>();
        public List<Connection> connections = new List<Connection>();
    }

    [Serializable]
    public class Layer
    {
        public int id;
        public List<Node> nodes = new List<Node>();
    }

    [Serializable]
    public class Node
    {
        public string id;
        public int typeId;
    }

    [Serializable]
    public class Connection
    {
        public string from;
        public string to;
    }

    public enum DistributionMode
    {
        Auto,
        Linear,
        Radial
    }

    [Serializable]
    public class PointData
    {
        public string id;            // название точки (например, BoltPoint_0)
        public int depth;            // глубина
        public string parentMeshId;  // имя меша, к которому принадлежит
        public string blockedByMeshId; // имя меша, который заблокировал (null/"" если не заблокирован)
    }

    [Serializable]
    public class PointDataCollection
    {
        public List<BoltPointGenerator.PointData> points;
        public PointDataCollection(List<BoltPointGenerator.PointData> pts) { points = pts; }
    }

    [Header("Output Points")]
    public List<PointData> PointsData = new List<PointData>();

    [Header("Settings")]
    public int TotalPoints = 10;
    public DistributionMode Mode = DistributionMode.Auto;
    public bool addMeshColliderIfMissing = true;
    public string pointsParentName = "BoltPoints";

    private List<MeshInfo> meshes = new List<MeshInfo>();
    private Transform pointsParent;

    // --- поле для вывода JSON (отобразится в инспекторе, можно скопировать) ---
    [Header("Output")]
    [TextArea(5, 20)]
    public string GraphJson;

    [Header("Output")]
    [TextArea(5, 20)]
    public string PointDataJson;

    [ContextMenu("Generate")]
    public void Generate()
    {
        if (Root == null)
        {
            Debug.LogError("[BoltPointGenerator] Root не указан.");
            return;
        }

        CollectMeshes();

        if (meshes.Count == 0)
        {
            Debug.LogWarning("[BoltPointGenerator] Меши не найдены под Root.");
            return;
        }

        // Создаём/очищаем parent для точек
        pointsParent = transform.Find(pointsParentName);
        if (pointsParent == null)
        {
            var go = new GameObject(pointsParentName);
            go.transform.SetParent(transform, false);
            pointsParent = go.transform;
        }

        for (int i = pointsParent.childCount - 1; i >= 0; i--)
        {
#if UNITY_EDITOR
            UnityEditor.Undo.DestroyObjectImmediate(pointsParent.GetChild(i).gameObject);
#else
            DestroyImmediate(pointsParent.GetChild(i).gameObject);
#endif
        }

        // распределяем количество точек по мешам пропорционально площади
        DistributePointsByArea();

        // готовим MeshCollider'ы
        PrepareMeshColliders();

        // генерируем точки по мешам (с заменой невалидных)
        foreach (var mi in meshes)
        {
            bool ok = mi.GenerateBoltPointsByBounds(this, Mode);
            if (!ok)
            {
                Debug.LogError($"[BoltPointGenerator] Не удалось найти подходящие позиции болтов для меша '{mi.Game.name}'. Генерация прервана.");
                return;
            }
        }

        // создаём финальные GameObject'ы точек и добавляем gizmo-скрипт
        PointsData.Clear();

        int created = 0;
        foreach (var mi in meshes)
        {
            foreach (var bp in mi.BoltPoints)
            {
                string pointId = $"BoltPoint_{created}";

                GameObject go = new GameObject(pointId);
                go.transform.SetParent(pointsParent, false);
                go.transform.position = bp.position;
                go.transform.rotation = Quaternion.LookRotation(bp.normal);

                var giz = go.AddComponent<BoltPointGizmo>();
                giz.MeshGameObject = mi.Game;
                giz.Depth = mi.Depth;
                giz.Normal = bp.normal;
                giz.ShowPointGismos = ShowPointGismos;

                // формируем PointData
                var pd = new PointData
                {
                    id = pointId,
                    depth = mi.Depth,
                    parentMeshId = mi.Game.name,
                    blockedByMeshId = bp.blockedBy // поле мы добавим в BoltPoint
                };
                PointsData.Add(pd);

                created++;
            }
        }

        BuildGraphJson();

        PointDataJson = GeneratePointsJSON();

        Debug.Log($"[BoltPointGenerator] Создано {created} точек для {meshes.Count} мешей.");
    }

    private void CollectMeshes()
    {
        meshes.Clear();
        var filters = Root.GetComponentsInChildren<MeshFilter>(true);
        foreach (var mf in filters)
        {
            if (mf.sharedMesh == null) continue;
            var mr = mf.GetComponent<MeshRenderer>();
            if (mr != null && !mr.enabled) continue;

            var mi = new MeshInfo(mf.gameObject, mf);
            mi.ComputeSurfaceArea();

            // подхватываем глубину из компонента MeshDepth, если есть
            var depthComp = mf.GetComponent<MeshDepth>();
            mi.Depth = depthComp != null ? depthComp.Depth : 0;

            meshes.Add(mi);
        }
    }

    private void DistributePointsByArea()
    {
        if (TotalPoints <= 0) TotalPoints = 1;
        float totalArea = meshes.Sum(m => m.SurfaceArea);
        if (totalArea <= 0f)
        {
            // равномерно
            int per = Mathf.FloorToInt(TotalPoints / (float)meshes.Count);
            int rem = TotalPoints - per * meshes.Count;
            for (int i = 0; i < meshes.Count; i++)
                meshes[i].TargetSampleCount = per + (i < rem ? 1 : 0);
            return;
        }

        float[] raw = meshes.Select(m => (m.SurfaceArea / totalArea) * TotalPoints).ToArray();
        int[] counts = raw.Select(r => Mathf.FloorToInt(r)).ToArray();
        int assigned = counts.Sum();
        int remaining = TotalPoints - assigned;

        var frac = counts.Select((c, i) => (i, raw[i] - c)).OrderByDescending(x => x.Item2).ToList();
        int idx = 0;
        while (remaining > 0 && frac.Count > 0)
        {
            counts[frac[idx].Item1]++;
            remaining--;
            idx = (idx + 1) % frac.Count;
        }

        for (int i = 0; i < meshes.Count; i++)
            meshes[i].TargetSampleCount = Mathf.Max(1, counts[i]);
    }

    private void PrepareMeshColliders()
    {
        foreach (var mi in meshes)
        {
            MeshCollider mc = mi.Game.GetComponent<MeshCollider>();
            if (mc == null && addMeshColliderIfMissing)
            {
                mc = mi.Game.AddComponent<MeshCollider>();
                mc.sharedMesh = mi.MeshFilter.sharedMesh;
                mc.convex = true;
                mi.TempAddedCollider = true;
            }
        }
    }
    private void BuildGraphJson()
    {
        var graph = new GraphData();

        // слои по Depth -> Layer
        var layersDict = new Dictionary<int, Layer>();
        var nodeIdMap = new Dictionary<BoltPoint, string>();
        int globalId = 0;

        // собираем ноды и даём им id
        foreach (var mi in meshes)
        {
            if (!layersDict.TryGetValue(mi.Depth, out var layer))
            {
                layer = new Layer { id = mi.Depth };
                layersDict[mi.Depth] = layer;
            }

            foreach (var bp in mi.BoltPoints)
            {
                string nid = globalId.ToString();
                nodeIdMap[bp] = nid;
                layer.nodes.Add(new Node { id = nid, typeId = 1 });
                globalId++;
            }
        }

        foreach (var kv in layersDict.OrderBy(kv => kv.Key))
            graph.layers.Add(kv.Value);

        // связи: каждая точка со следующего слоя соединяется с ближайшей
        var orderedDepths = layersDict.Keys.OrderBy(d => d).ToList();
        for (int i = 0; i < orderedDepths.Count - 1; i++)
        {
            int d1 = orderedDepths[i];
            int d2 = orderedDepths[i + 1];

            var layer1Points = new List<BoltPoint>();
            var layer2Points = new List<BoltPoint>();
            foreach (var mi in meshes)
            {
                if (mi.Depth == d1) layer1Points.AddRange(mi.BoltPoints);
                else if (mi.Depth == d2) layer2Points.AddRange(mi.BoltPoints);
            }

            foreach (var p1 in layer1Points)
            {
                BoltPoint nearest = null;
                float bestDist = float.MaxValue;
                foreach (var p2 in layer2Points)
                {
                    float dist = Vector3.Distance(p1.position, p2.position);
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        nearest = p2;
                    }
                }
                if (nearest != null)
                {
                    graph.connections.Add(new Connection { from = nodeIdMap[p1], to = nodeIdMap[nearest] });
                }
            }
        }

        // сохраняем JSON в поле
        var graphString = JsonUtility.ToJson(graph, true);

        GraphJson = graphString;

#if UNITY_EDITOR
        Debug.Log("[BoltPointGenerator] Graph JSON:\n" + graphString);
#endif
    }

    public string GeneratePointsJSON()
    {
        var points = new List<PointData>();
        int counter = 0;

        foreach (var mi in meshes)
        {
            foreach (var bp in mi.BoltPoints)
            {
                var pd = new PointData
                {
                    id = $"BoltPoint_{counter++}",
                    depth = mi.Depth,
                    parentMeshId = mi.Game.name,
                    blockedByMeshId = bp.blockedBy // может быть null или ""
                };
                points.Add(pd);
            }
        }

        // ✅ упаковываем в обёртку
        var wrapper = new PointDataCollection(points);
        var str = JsonUtility.ToJson(wrapper, true);

#if UNITY_EDITOR
        Debug.Log("[BoltPointGenerator] Point Data JSON:\n" + str);
#endif

        return str;
    }

    // === внутренние типы ===
    private class MeshInfo
    {
        public GameObject Game;
        public MeshFilter MeshFilter;
        public float SurfaceArea;
        public int TargetSampleCount;
        public bool TempAddedCollider = false;
        public int Depth = 0; // глубина меша (подхватывается извне)
        public List<BoltPoint> BoltPoints = new List<BoltPoint>();

        // локальные данные меша
        private Vector3[] vertices;
        private int[] triangles;

        public MeshInfo(GameObject go, MeshFilter mf)
        {
            Game = go;
            MeshFilter = mf;
        }

        public void ComputeSurfaceArea()
        {
            Mesh m = MeshFilter.sharedMesh;
            if (m == null) { SurfaceArea = 0f; return; }
            vertices = m.vertices;
            triangles = m.triangles;
            float area = 0f;
            int triCount = triangles.Length / 3;
            for (int t = 0; t < triCount; t++)
            {
                Vector3 a = vertices[triangles[t * 3 + 0]];
                Vector3 b = vertices[triangles[t * 3 + 1]];
                Vector3 c = vertices[triangles[t * 3 + 2]];
                area += Vector3.Cross(b - a, c - a).magnitude * 0.5f;
            }
            SurfaceArea = area;
        }

        /// <summary>
        /// Генерация позиций болтов по центрам граней bounding box с по-раундовой выдачей:
        /// по центру каждой грани, затем по 2 на каждой, затем по 3 и т.д.
        /// На каждой попытке точка проверяется TryAddPoint: если луч попадает в меш с Depth >= this.Depth —
        /// точка отвергается и пробуется следующая грань. Если все грани отказали — возвращается false.
        /// </summary>
        public bool GenerateBoltPointsByBounds(BoltPointGenerator parent, BoltPointGenerator.DistributionMode mode)
        {
            // очистим старые точки
            BoltPoints.Clear();
            Mesh mesh = MeshFilter.sharedMesh;
            if (mesh == null) return false;

            Bounds lb = mesh.bounds;
            Vector3 localSize = lb.size;
            Vector3 localCenter = lb.center;

            var faces = new List<FaceInfo>
    {
        new FaceInfo(localCenter + new Vector3(lb.extents.x, 0f, 0f), Vector3.right, localSize.y * localSize.z),
        new FaceInfo(localCenter + new Vector3(-lb.extents.x, 0f, 0f), Vector3.left, localSize.y * localSize.z),
        new FaceInfo(localCenter + new Vector3(0f, lb.extents.y, 0f), Vector3.up, localSize.x * localSize.z),
        new FaceInfo(localCenter + new Vector3(0f, -lb.extents.y, 0f), Vector3.down, localSize.x * localSize.z),
        new FaceInfo(localCenter + new Vector3(0f, 0f, lb.extents.z), Vector3.forward, localSize.x * localSize.y),
        new FaceInfo(localCenter + new Vector3(0f, 0f, -lb.extents.z), Vector3.back, localSize.x * localSize.y)
    };

            var faceOrder = faces.Select((f, i) => (i, f)).OrderByDescending(x => x.f.area).Select(x => x.i).ToArray();
            int facesCount = faces.Count;

            int[] placedPerFace = new int[facesCount];
            List<Vector3>[] facePositions = new List<Vector3>[facesCount];
            List<Vector3>[] faceNormals = new List<Vector3>[facesCount];
            List<string>[] faceBlocked = new List<string>[facesCount];

            for (int i = 0; i < facesCount; i++)
            {
                facePositions[i] = new List<Vector3>();
                faceNormals[i] = new List<Vector3>();
                faceBlocked[i] = new List<string>();
            }

            int needed = Mathf.Max(0, TargetSampleCount);
            if (needed == 0) return true;

            // функция для генерации раскладки локальных позиций на грани
            System.Func<FaceInfo, int, List<Vector3>> GenLocalLayout = (face, count) =>
            {
                var res = new List<Vector3>();
                Vector3 lc = face.localCenter;
                Vector3 ln = face.localNormal;

                Vector3 axisA = Vector3.Cross(ln, Vector3.up);
                if (axisA.sqrMagnitude < 1e-4f) axisA = Vector3.Cross(ln, Vector3.right);
                axisA.Normalize();
                Vector3 axisB = Vector3.Cross(ln, axisA).normalized;

                float sizeA = Mathf.Abs(Vector3.Dot(localSize, axisA));
                float sizeB = Mathf.Abs(Vector3.Dot(localSize, axisB));

                bool linearAuto = sizeA > sizeB * 1.5f || sizeB > sizeA * 1.5f;
                bool useLinear = (mode == BoltPointGenerator.DistributionMode.Linear) ||
                                 (mode == BoltPointGenerator.DistributionMode.Auto && linearAuto);

                if (count <= 0) return res;

                if (useLinear)
                {
                    if (count == 1)
                    {
                        res.Add(lc);
                        return res;
                    }

                    Vector3 dir = (sizeA >= sizeB) ? axisA : axisB;
                    float length = Mathf.Max(sizeA, sizeB) * 0.8f;
                    float step = (count > 1) ? length / (count - 1) : 0f;

                    for (int i = 0; i < count; i++)
                    {
                        float offset = -length * 0.5f + step * i;
                        res.Add(lc + dir * offset);
                    }
                    return res;
                }
                else
                {
                    if (count == 1)
                    {
                        res.Add(lc);
                        return res;
                    }

                    int ringCount = (count % 2 == 1) ? (count - 1) : count;
                    float radius = Mathf.Min(sizeA, sizeB) * 0.35f;
                    float rotation = Mathf.PI / 4f;

                    if (count % 2 == 1)
                        res.Add(lc);

                    for (int k = 0; k < ringCount; k++)
                    {
                        float angle = (2f * Mathf.PI * k) / ringCount + rotation;
                        Vector3 localPos = lc + axisA * Mathf.Cos(angle) * radius + axisB * Mathf.Sin(angle) * radius;
                        res.Add(localPos);
                    }
                    return res;
                }
            };

            // основной цикл по раундам
            while (needed > 0)
            {
                bool placedThisCycle = false;

                foreach (int fi in faceOrder)
                {
                    var f = faces[fi];
                    int current = placedPerFace[fi];
                    int propose = current + 1;

                    var localCandidates = GenLocalLayout(f, propose);
                    if (localCandidates == null || localCandidates.Count < propose) continue;

                    var worldCandidates = new List<Vector3>(propose);
                    var worldNormals = new List<Vector3>(propose);
                    var blockedByList = new List<string>(propose);

                    Vector3 worldNormal = MeshFilter.transform.TransformDirection(f.localNormal).normalized;

                    for (int i = 0; i < localCandidates.Count; i++)
                    {
                        Vector3 w = MeshFilter.transform.TransformPoint(localCandidates[i]);
                        worldCandidates.Add(w);
                        worldNormals.Add(worldNormal);

                        TryAddPoint(w, worldNormal, parent, out var blockedByCandidate);
                        blockedByList.Add(blockedByCandidate);
                    }

                    // принимаем новую конфигурацию (даже если часть точек заблокирована)
                    placedPerFace[fi] = propose;
                    facePositions[fi].Clear();
                    faceNormals[fi].Clear();
                    faceBlocked[fi].Clear();

                    for (int i = 0; i < worldCandidates.Count; i++)
                    {
                        facePositions[fi].Add(worldCandidates[i]);
                        faceNormals[fi].Add(worldNormals[i]);
                        faceBlocked[fi].Add(blockedByList[i]);
                    }

                    needed--;
                    placedThisCycle = true;
                    if (needed <= 0) break;
                }

                if (!placedThisCycle)
                {
                    Debug.LogError($"[BoltPointGenerator] Не удалось разместить оставшиеся {needed} точек на меш '{Game.name}' — все кандидаты отклонены правилами глубин.");
                    return false;
                }
            }

            BoltPoints.Clear();
            for (int fi = 0; fi < facesCount; fi++)
            {
                for (int k = 0; k < facePositions[fi].Count; k++)
                {
                    var pos = facePositions[fi][k];
                    var normal = faceNormals[fi][k];
                    var blockedBy = faceBlocked[fi][k];

                    // === новая часть: проецируем на поверхность ===
                    ProjectOnMeshSurface(ref pos, ref normal);

                    BoltPoints.Add(new BoltPoint
                    {
                        position = pos,
                        normal = normal,
                        blockedBy = blockedBy
                    });
                }
            }

            return BoltPoints.Count > 0;
        }

        /// <summary>
        /// Проецирует точку на поверхность меша и обновляет нормаль
        /// </summary>
        private void ProjectOnMeshSurface(ref Vector3 position, ref Vector3 normal)
        {
            Debug.Log($"{1}");
            var go = Game;
            bool tempAdded = false;

            MeshCollider mc = go.GetComponent<MeshCollider>();
            if (mc == null)
            {
                mc = go.AddComponent<MeshCollider>();
                mc.sharedMesh = MeshFilter.sharedMesh;
                mc.convex = true;
                tempAdded = true;
            }

            // Находим ближайшую точку на поверхности
            Vector3 closest = mc.ClosestPoint(position);

            // Смещаем позицию
            position = closest;

            // Получаем нормаль с поверхности через Raycast
            Vector3 dir = (closest - MeshFilter.transform.position).normalized;
            Ray ray = new Ray(closest + dir * 0.01f, -dir); // стреляем внутрь
            if (mc.Raycast(ray, out RaycastHit hit, 0.1f))
            {
                normal = hit.normal;
            }

            // Удаляем MeshCollider если создавали временно
            if (tempAdded)
            {
#if UNITY_EDITOR
                UnityEngine.Object.DestroyImmediate(mc);
#else
        UnityEngine.Object.Destroy(mc);
#endif
            }
        }

        /// <summary>
        /// Попытка добавить точку: возвращает true если точка подходит.
        /// Точка НЕ подходит если луч по нормали попадает в меш другого MeshInfo с Depth >= this.Depth.
        /// В out blockedBy возвращается имя меша, который блокирует точку (или null).
        /// </summary>
        private bool TryAddPoint(Vector3 posWorld, Vector3 normalWorld, BoltPointGenerator parent, out string blockedBy)
        {
            blockedBy = null;

            // сдвинем начало луча чуть внутрь (чтобы корректно различать соседние плоскости)
            Vector3 origin = posWorld - normalWorld * 0.01f;
            Ray r = new Ray(origin, normalWorld);

            var hits = Physics.RaycastAll(r, Mathf.Infinity);
            if (hits == null || hits.Length == 0) return true;

            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            foreach (var hit in hits)
            {
                if (hit.collider == null) continue;

                // попробуем найти MeshInfo, которому принадлежит этот коллайдер
                MeshInfo hitMi = null;
                foreach (var m in parent.meshes)
                {
                    if (hit.collider.gameObject == m.Game) { hitMi = m; break; }
                    var mf = hit.collider.GetComponent<MeshFilter>();
                    if (mf != null && mf == m.MeshFilter) { hitMi = m; break; }
                    // учитывать дочерние коллайдеры
                    if (hit.collider.transform.IsChildOf(m.Game.transform)) { hitMi = m; break; }
                }

                if (hitMi == null)
                {
                    // попали в объект вне нашего списка — считаем безопасным и продолжаем проверять дальше
                    continue;
                }

                // если попали в свой меш — пропускаем его (искать следующий чужой хит)
                if (hitMi == this) continue;

                // первый встретившийся чужой меш — решающий
                if (hitMi.Depth >= this.Depth)
                {
                    // блокирующий меш (глубина >= текущей) — точка не годится
                    return false;
                }
                else
                {
                    // попали в меш с меньшей глубиной — значит точка допустима
                    blockedBy = hitMi.Game.name;
                    return true;
                }
            }

            // если прошли все хиты и не нашли чужого блокирующего меша — допустимо
            return true;
        }

        private struct FaceInfo
        {
            public Vector3 localCenter;
            public Vector3 localNormal;
            public float area;
            public FaceInfo(Vector3 c, Vector3 n, float a) { localCenter = c; localNormal = n; area = a; }
        }

        // ВНИМАНИЕ: этот метод должен быть на уровне BoltPointGenerator, а не внутри MeshInfo


    } // MeshInfo

    public class BoltPoint
    {
        public Vector3 position;
        public Vector3 normal;
        public string blockedBy; // имя меша, если точка была отвергнута или блокирована
    }
}