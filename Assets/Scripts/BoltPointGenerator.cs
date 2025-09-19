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

    [Header("Output")]
    [TextArea(5, 20)]
    public string PointDataJson;

    public static List<Ray> _debugRays = new List<Ray>();
    private const int MAX_DEBUG_RAYS = 50; // Максимальное количество сохраняемых лучей
    [ContextMenu("Generate")]
    public void Generate()
    {
        if (Root == null)
        {
            Debug.LogError("[BoltPointGenerator] Root не указан.");
            return;
        }

        // --- 1) Удаляем все ранее созданные BoltPoint'ы (даже если они были прикреплены к мешам) ---
        // Ищем компоненты BoltPointGizmo под Root и удаляем их GameObject'ы.
        var oldGizmos = Root.GetComponentsInChildren<BoltPointGizmo>(true);
        for (int i = oldGizmos.Length - 1; i >= 0; i--)
        {
#if UNITY_EDITOR
            UnityEditor.Undo.DestroyObjectImmediate(oldGizmos[i].gameObject);
#else
        DestroyImmediate(oldGizmos[i].gameObject);
#endif
        }

        // Сбор мешей
        CollectMeshes();
        if (meshes.Count == 0)
        {
            Debug.LogWarning("[BoltPointGenerator] Меши не найдены под Root.");
            return;
        }

        // --- 2) Создаём/очищаем pointsParent (единая точка хранения всех сгенерированных точек) ---
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

                // ВАЖНО: теперь все точки создаются как дети pointsParent, чтобы их было легко удалить в следующем запуске
                GameObject go = new GameObject(pointId);
                go.transform.SetParent(pointsParent, false);
                go.transform.position = bp.position;
                go.transform.rotation = Quaternion.LookRotation(bp.normal);

                var giz = go.AddComponent<BoltPointGizmo>();
                giz.MeshGameObject = mi.Game;
                giz.Depth = mi.Depth;
                giz.Normal = bp.normal;
                giz.ShowPointGismos = ShowPointGismos;

                // формируем PointData (без ссылки на GameObject — пригодно для экспорта)
                var pd = new PointData
                {
                    id = pointId,
                    depth = mi.Depth,
                    parentMeshId = mi.Game.name,
                    blockedByMeshId = string.IsNullOrEmpty(bp.blockedBy) ? "" : bp.blockedBy
                };
                PointsData.Add(pd);

                created++;
            }
        }

        // генерируем JSON
        PointDataJson = GeneratePointsJSON();

        Debug.Log($"[BoltPointGenerator] Создано {created} точек для {meshes.Count} мешей. Старые точки удалены перед генерацией.");
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
                mc.convex = false;
                mi.TempAddedCollider = true;
            }
        }
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

        // внутри MeshInfo (дополнить поля)
private float[] triAreas;
private Vector3[] triCentersLocal;
private Vector3[] triNormalsLocal;
private int triCount;


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

        // В классе MeshInfo (или в самом методе) заменим старую логику, связную с глубиной:
        // Заменяет старую реализацию внутри MeshInfo
        public bool GenerateBoltPointsByBounds(BoltPointGenerator parent, BoltPointGenerator.DistributionMode mode)
        {
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

            // Пытаемся два раза — если первая попытка дала плохое размещение (коллизии/блокировки), перегенерируем
            for (int attempt = 0; attempt < 2; attempt++)
            {
                int needed = Mathf.Max(0, TargetSampleCount);
                if (needed == 0) return true;

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

                bool failed = false;

                while (needed > 0)
                {
                    bool placedThisCycle = false;

                    foreach (int fi in faceOrder)
                    {
                        var f = faces[fi];
                        int current = placedPerFace[fi];
                        int propose = current + 1;

                        var localCandidates = GenerateCandidatesForFace(f, propose, mode, localSize);
                        if (localCandidates == null || localCandidates.Count < propose)
                            continue; // пропускаем эту грань — попробуем другую

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

                        // Проверка на минимальное расстояние до уже принятых позиций (чтобы избежать дубликатов)
                        List<Vector3> existingPositions = new List<Vector3>();
                        for (int j = 0; j < facesCount; j++)
                        {
                            if (j == fi) continue;
                            existingPositions.AddRange(facePositions[j]);
                        }

                        // minDist — доля от наименьшего размера bounds (адаптируйте если нужно)
                        float minDim = Mathf.Min(Mathf.Max(localSize.x, 1e-4f), Mathf.Max(localSize.y, 1e-4f));
                        minDim = Mathf.Min(minDim, Mathf.Max(localSize.z, 1e-4f));
                        float minDist = Mathf.Max(0.001f, minDim * 0.03f); // 3% от минимального измерения, минимум 1mm (в юнитах)

                        bool tooClose = false;
                        if (existingPositions.Count > 0)
                        {
                            foreach (var wc in worldCandidates)
                            {
                                foreach (var ex in existingPositions)
                                {
                                    if (Vector3.Distance(wc, ex) < minDist)
                                    {
                                        tooClose = true;
                                        break;
                                    }
                                }
                                if (tooClose) break;
                            }
                        }

                        if (tooClose)
                        {
                            // не принимаем эту конфигурацию — попробуем другую грань
                            continue;
                        }

                        // принимаем новую конфигурацию для этой грани (замещаем старые позиции)
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
                    } // foreach face

                    if (!placedThisCycle)
                    {
                        failed = true;
                        break;
                    }
                } // while needed

                if (!failed)
                {
                    // Собираем финальные BoltPoints
                    BoltPoints.Clear();
                    for (int fi = 0; fi < facesCount; fi++)
                    {
                        for (int k = 0; k < facePositions[fi].Count; k++)
                        {
                            Vector3 pos = facePositions[fi][k];
                            Vector3 normal = faceNormals[fi][k];

                            ProjectToMeshSurface(ref pos, ref normal);

                            BoltPoints.Add(new BoltPoint
                            {
                                position = pos,
                                normal = normal,
                                blockedBy = faceBlocked[fi][k]
                            });
                        }
                    }

                    return BoltPoints.Count > 0;
                }
                else
                {
                    Debug.LogWarning($"[BoltPointGenerator] Попытка {attempt + 1}: генерация точек для '{Game.name}' неудачна — пробуем заново...");
                    // продолжим на следующую попытку (внешний цикл)
                }
            } // attempts

            Debug.LogError($"[BoltPointGenerator] Не удалось разместить точки на меш '{Game.name}' даже после перегенерации.");
            return false;
        }

        // Новая функция для генерации кандидатов на основе данных о гранях.
        private List<Vector3> GenerateCandidatesForFace(FaceInfo face, int count, BoltPointGenerator.DistributionMode mode, Vector3 localSize)
        {
            var res = new List<Vector3>();
            Vector3 lc = face.localCenter;
            Vector3 ln = face.localNormal;

            // базис грани в локальных координатах
            Vector3 axisA = Vector3.Cross(ln, Vector3.up);
            if (axisA.sqrMagnitude < 1e-4f) axisA = Vector3.Cross(ln, Vector3.right);
            axisA.Normalize();
            Vector3 axisB = Vector3.Cross(ln, axisA).normalized;

            float sizeA = Mathf.Abs(Vector3.Dot(localSize, axisA));
            float sizeB = Mathf.Abs(Vector3.Dot(localSize, axisB));

            // запас на случай нулевых размеров
            sizeA = Mathf.Max(sizeA, 0.0001f);
            sizeB = Mathf.Max(sizeB, 0.0001f);

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

                // Для радиальной расстановки: если count нечётно — даём 1 центр + (count-1) на кольце,
                // если чётно — все на кольце. Это даёт требуемую симметрию (2 => противоположные точки)
                int ringCount = (count % 2 == 1) ? (count - 1) : count;
                float radius = Mathf.Min(sizeA, sizeB) * 0.35f;
                float rotation = Mathf.PI / 4f;

                if (count % 2 == 1)
                    res.Add(lc); // центр

                // Генерируем ringCount точек равномерно по углу
                for (int k = 0; k < ringCount; k++)
                {
                    float angle = (2f * Mathf.PI * k) / ringCount + rotation;
                    Vector3 localPos = lc + axisA * Mathf.Cos(angle) * radius + axisB * Mathf.Sin(angle) * radius;
                    res.Add(localPos);
                }
                return res;
            }
        }


        /// <summary>
        /// Проецирует точку на поверхность меша и обновляет нормаль
        /// </summary>
        /// <summary>
        /// Проецирует точку на поверхность меша и обновляет нормаль,
        /// используя луч через центр Bounds до противоположного края.
        /// </summary>
        /// <summary>
        /// Проецирует точку на поверхность меша и обновляет нормаль,
        /// строя луч через центр Bounds до противоположного края.
        /// </summary>
        private void ProjectToMeshSurface(ref Vector3 pos, ref Vector3 normal)
        {
            MeshCollider tempCollider = MeshFilter.GetComponent<MeshCollider>();
            bool created = false;

            if (tempCollider == null)
            {
                tempCollider = MeshFilter.gameObject.AddComponent<MeshCollider>();
                created = true;
            }

            // Убедимся, что он не convex (иначе пересечения могут быть странные)
            tempCollider.convex = false;

            Bounds localBounds = MeshFilter.sharedMesh.bounds;
            Vector3 worldCenter = MeshFilter.transform.TransformPoint(localBounds.center);

            // Направление через центр в "противоположную сторону"
            Vector3 toCenter = (worldCenter - pos).normalized;
            Vector3 throughCenter = -toCenter;

            // Чтобы не стартовать изнутри меша — немного вынесем точку наружу
            Vector3 rayStart = pos + throughCenter * localBounds.size.magnitude;

            // Строим луч от внешней точки через pos → через центр → наружу
            Ray ray = new Ray(rayStart, toCenter); // идем в сторону центра и дальше
            float rayLength = localBounds.size.magnitude * 3f;

            if (tempCollider.Raycast(ray, out RaycastHit hit, rayLength))
            {
                pos = hit.point;
                normal = hit.normal;
            }

            if (created)
                DestroyImmediate(tempCollider);
        }

        /// <summary>
        /// Попытка добавить точку: возвращает true если точка подходит.
        /// Точка НЕ подходит если луч по нормали попадает в меш другого MeshInfo с Depth >= this.Depth.
        /// В out blockedBy возвращается имя меша, который блокирует точку (или null).
        /// </summary>
        /// <summary>
        /// Попытка добавить точку: возвращает true если точка подходит.
        /// Точка НЕ подходит если луч по нормали попадает в меш другого MeshInfo с Depth > this.Depth.
        /// Если глубина совпадает — точка остаётся валидной.
        /// В out blockedBy возвращается имя меша, который блокирует точку (или null).
        /// </summary>
        private bool TryAddPoint(Vector3 posWorld, Vector3 normalWorld, BoltPointGenerator parent, out string blockedBy)
        {
            blockedBy = null;
            // небольшое смещение назад, чтобы не задеть собственную поверхность
            Vector3 origin = posWorld - normalWorld * 0.001f;
            Ray r = new Ray(origin, normalWorld);

            // debug rays (как раньше)
            _debugRays.Add(r);
            if (_debugRays.Count > MAX_DEBUG_RAYS) _debugRays.RemoveAt(0);

            var hits = Physics.RaycastAll(r, Mathf.Infinity);
            if (hits == null || hits.Length == 0) return true;

            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            // локальные пороги (берём из parent, если он задан — иначе значения по умолчанию)
            float sameDepthDot = parent != null ? -0.5f : -0.5f;
            float minSep = parent != null ? 0.02f : 0.02f;

            foreach (var hit in hits)
            {
                if (hit.collider == null) continue;

                // Найти MeshInfo, которому принадлежит хит
                MeshInfo hitMi = null;
                foreach (var m in parent.meshes)
                {
                    if (hit.collider.gameObject == m.Game) { hitMi = m; break; }
                    var mf = hit.collider.GetComponent<MeshFilter>();
                    if (mf != null && mf == m.MeshFilter) { hitMi = m; break; }
                    if (hit.collider.transform.IsChildOf(m.Game.transform)) { hitMi = m; break; }
                }

                if (hitMi == null)
                {
                    // попали во внешний объект — игнорируем
                    continue;
                }

                // если попали в свой меш — пропускаем (ищем следующий внешний хит)
                if (hitMi == this) continue;

                // если глубина больше (hit глубже) => блокируем
                if (hitMi.Depth > this.Depth)
                {
                    blockedBy = hitMi.Game.name;
                    return false;
                }

                // если глубина равна — дополнительная логика: блокируем только если поверхности реально "встречные"
                if (hitMi.Depth == this.Depth)
                {
                    Vector3 hitNormal = hit.normal.normalized; // нормаль попадания (world space)
                    float dot = Vector3.Dot(normalWorld.normalized, hitNormal);

                    // расстояние вдоль луча от origin до попадания
                    float separation = hit.distance;

                    // если нормали направлены навстречу (dot меньше порога) — блокируем
                    if (dot < sameDepthDot)
                    {
                        blockedBy = hitMi.Game.name;
                        return false;
                    }

                    // или если поверхности слишком близко — блокируем
                    if (separation < minSep)
                    {
                        blockedBy = hitMi.Game.name;
                        return false;
                    }

                    // иначе — считаем безопасным и продолжаем проверять следующие хиты
                    continue;
                }

                // если hitMi.Depth < this.Depth => встретили более "мелкий" меш — точка допустима,
                // но возвращаем имя блокировщика для информации
                blockedBy = hitMi.Game.name;
                return true;
            }

            // ни один хит не заблокировал точку
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

    private void OnDrawGizmos()
    {
        // Проверяем, активен ли объект и есть ли данные для отрисовки
        if (!isActiveAndEnabled || _debugRays.Count == 0) return;

        Gizmos.color = Color.red;

        // Отрисовываем все сохраненные лучи
        foreach (var ray in _debugRays)
        {
            Gizmos.DrawRay(ray.origin, ray.direction * 100f);
        }
    }
}