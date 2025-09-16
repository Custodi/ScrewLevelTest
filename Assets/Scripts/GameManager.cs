using System.Collections.Generic;
using UnityEngine;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance;

    [Header("Prefabs & References")]
    public GameObject basketPrefab;
    public Material[] colorMaterials;
    public Transform basketParent;
    public Transform[] basketSlots; // разные позиции для корзинок
    public Buffer buffer;

    [Header("Meshes (optional)")]
    [Tooltip("Родитель, содержащий объекты-меши (опционально) — помогает находить сам GameObject по parentMeshId")]
    public Transform meshesParent;

    [Header("Gameplay Settings")]
    public int totalBaskets = 4;
    public int activeBasketsCount = 2;

    private List<Basket> allBaskets = new List<Basket>();
    private List<Basket> activeBaskets = new List<Basket>();
    private int nextBasketIndex = 0;

    // словари для учёта болтов и мешей
    // meshBoltCount: key = meshId (string), value = сколько болтов осталось прикручено
    private Dictionary<string, int> meshBoltCount = new Dictionary<string, int>();
    // meshObjects: key = meshId, value = GameObject с мешем (если найден)
    private Dictionary<string, GameObject> meshObjects = new Dictionary<string, GameObject>();

    // флаг, чтобы не переинициализировать кажд кадр
    private bool meshesInitialized = false;

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        // Создаём очередь корзин (создаются но деактивируются)
        for (int i = 0; i < totalBaskets; i++)
        {
            int colorIndex = (colorMaterials != null && colorMaterials.Length > 0) ? (i % colorMaterials.Length) : 0;
            var basket = CreateBasket(colorIndex, i);
            if (basket != null)
            {
                basket.gameObject.SetActive(false);
                allBaskets.Add(basket);
            }
        }

        // Активируем начальные корзины
        for (int i = 0; i < activeBasketsCount; i++)
            ActivateNextBasket();

        // Попытка один раз инициализировать меши/счётчики (если болты уже есть в сцене)
        TryInitializeMeshCountsIfNeeded();
    }

    private void Update()
    {
        // Обработка клика через Raycast
        if (Input.GetMouseButtonDown(0))
        {
            if (Camera.main == null) return;
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit, 100f))
            {
                Bolt bolt = hit.collider.GetComponentInParent<Bolt>();
                if (bolt != null)
                {
                    // Обработка клика: убираем болт со сцены/перемещаем в корзину/буфер.
                    // Bolt.OnClicked вызовет GameManager.OnBoltClicked и затем сообщит о снятии с меша.
                    bolt.OnClicked();
                }
            }
        }
    }

    private Basket CreateBasket(int colorIndex, int slotIndex)
    {
        if (basketPrefab == null)
        {
            Debug.LogError("[GameManager] basketPrefab не задан.");
            return null;
        }

        // создаём экземпляр и ставим в позицию слота, если слот есть
        GameObject go = Instantiate(basketPrefab, basketParent);
        if (basketSlots != null && slotIndex < basketSlots.Length && basketSlots[slotIndex] != null)
        {
            go.transform.position = basketSlots[slotIndex].position;
            go.transform.rotation = basketSlots[slotIndex].rotation;
        }

        var basket = go.GetComponent<Basket>();
        if (basket == null)
        {
            Debug.LogError("[GameManager] Basket prefab не содержит компонент Basket!");
            Destroy(go);
            return null;
        }

        Material mat = (colorMaterials != null && colorMaterials.Length > colorIndex) ? colorMaterials[colorIndex] : null;
        basket.Init(colorIndex, mat);
        return basket;
    }

    private void ActivateNextBasket()
    {
        if (nextBasketIndex >= allBaskets.Count) return;

        Basket next = allBaskets[nextBasketIndex];

        // ✅ Устанавливаем позицию корзины в слот (если слот задан)
        if (basketSlots != null && nextBasketIndex < basketSlots.Length && basketSlots[nextBasketIndex] != null)
        {
            next.transform.position = basketSlots[nextBasketIndex].position;
            next.transform.rotation = basketSlots[nextBasketIndex].rotation;
        }

        next.gameObject.SetActive(true);
        activeBaskets.Add(next);
        nextBasketIndex++;
    }


    /// <summary>
    /// Вызывается при клике на болт (bolt.OnClicked -> GameManager.OnBoltClicked).
    /// Не меняет счётчики мешей — это делает OnBoltDetachedFromMesh (вызывается отдельно).
    /// </summary>
    public void OnBoltClicked(Bolt bolt)
    {
        if (bolt == null) return;

        // Сначала пытаемся положить в активную корзину нужного цвета
        Basket target = activeBaskets.Find(b => b.colorIndex == bolt.colorIndex && !b.IsFull);
        if (target != null)
        {
            // Если болт был ранее в буфере, удаляем его оттуда
            if (buffer != null) buffer.RemoveBolt(bolt);
            // Добавляем в корзину; коллбэк OnBoltPlacedInBasket вызовется из Basket
            target.TryAddBolt(bolt, OnBoltPlacedInBasket);
            return;
        }

        // Иначе — в буфер
        if (buffer != null)
        {
            if (!buffer.TryAddBolt(bolt, OnBoltPlacedInBuffer))
            {
                Debug.Log("GameManager: Game Over! Буфер переполнен.");
            }
        }
    }

    private void OnBoltPlacedInBasket(Basket basket, Bolt bolt)
    {
        // Когда болт становится в корзину, проверяем заполнение корзины и обрабатываем
        if (basket.IsFull)
        {
            activeBaskets.Remove(basket);
            basket.ClearAndDestroy();
            ActivateNextBasket();

            // После появления следующей корзины пробуем переложить из буфера
            if (buffer != null)
                buffer.FlushToBaskets(activeBaskets, OnBoltPlacedInBasket);
        }
    }

    private void OnBoltPlacedInBuffer(Bolt bolt)
    {
        // Просто логируем — остальные действия происходят при клике или при Flush
        Debug.Log($"GameManager: Болт {bolt.name} помещён в буфер.");
    }

    // ---------- MESH COUNT / REGISTRATION ----------

    /// <summary>
    /// Вызывается генератором: регистрирует меш (или увеличивает счётчик).
    /// Можно вызывать при спавне болта (boltsCount обычно 1).
    /// </summary>
    public void RegisterMesh(string meshId, GameObject mesh, int boltsCount)
    {
        if (string.IsNullOrEmpty(meshId)) return;

        // помечаем, что у нас есть внешняя регистрация — всё равно ещё можем инициализировать позже
        meshesInitialized = true;

        if (!meshBoltCount.ContainsKey(meshId))
        {
            meshBoltCount[meshId] = 0;
            if (mesh != null)
                meshObjects[meshId] = mesh;
        }

        meshBoltCount[meshId] += boltsCount;
    }

    /// <summary>
    /// Вызывается, когда болт окончательно снят (когда игрок открутил болт и он ушёл в буфер/корзину).
    /// Уменьшает соответствующий счётчик; если дошёл до нуля — удаляет меш.
    /// Если счётчики не инициализированы — сначала попытается автоматически их собрать.
    /// </summary>
    public void OnBoltDetachedFromMesh(Bolt bolt)
    {
        if (bolt == null) return;
        if (string.IsNullOrEmpty(bolt.parentMeshId)) return;

        // Если ключа нет — попробуем автоматическую инициализацию (соберём текущие болты в сцене)
        if (!meshBoltCount.ContainsKey(bolt.parentMeshId))
        {
            TryInitializeMeshCountsIfNeeded();
        }

        if (!meshBoltCount.ContainsKey(bolt.parentMeshId))
        {
            // всё ещё нет информации — создаём запись с 0 и продолжим (защита от ошибок)
            meshBoltCount[bolt.parentMeshId] = 0;
        }

        meshBoltCount[bolt.parentMeshId]--;

        if (meshBoltCount[bolt.parentMeshId] <= 0)
        {
            // Удаляем меш-объект, если известен
            if (meshObjects.TryGetValue(bolt.parentMeshId, out var meshGO) && meshGO != null)
            {
                Debug.Log($"[GameManager] Меш {bolt.parentMeshId} разрушен (все болты откручены).");
                Destroy(meshGO);
            }

            meshBoltCount.Remove(bolt.parentMeshId);
            meshObjects.Remove(bolt.parentMeshId);
        }

        // После снятия болта — попытка положить его в корзину/буфер (если не сделано ранее)
        // (Заметим: обычно OnBoltClicked уже попробовал положить; этот код в случае, если мы
        // вызвали OnBoltDetachedFromMesh отдельно — дублирует логику.)
        Basket target = activeBaskets.Find(b => b.colorIndex == bolt.colorIndex && !b.IsFull);
        if (target != null)
        {
            if (buffer != null) buffer.RemoveBolt(bolt);
            target.TryAddBolt(bolt, OnBoltPlacedInBasket);
            return;
        }

        if (buffer != null)
        {
            if (!buffer.TryAddBolt(bolt, OnBoltPlacedInBuffer))
            {
                Debug.Log("GameManager: Game Over! Буфер переполнен.");
            }
        }
    }

    /// <summary>
    /// Если meshBoltCount ещё не инициализирован, пробуем собрать данные автоматически:
    /// - сканируем meshesParent для поиска объектов мешей;
    /// - сканируем все Bolt в сцене и считаем bolts по parentMeshId.
    /// Этот метод безопасно сбрасывает и пересоздаёт словари.
    /// </summary>
    private void TryInitializeMeshCountsIfNeeded()
    {
        // Не инициализируем повторно, если уже была ручная регистрация или уже проинициализировано
        if (meshesInitialized) return;

        meshesInitialized = true; // помечаем, чтобы не запускать снова

        meshBoltCount.Clear();
        meshObjects.Clear();

        // Собираем map имён мешей -> GameObject (по meshesParent, если задан)
        Dictionary<string, GameObject> meshLookup = new Dictionary<string, GameObject>(System.StringComparer.Ordinal);
        if (meshesParent != null)
        {
            var meshRenderers = meshesParent.GetComponentsInChildren<MeshRenderer>();
            foreach (var mr in meshRenderers)
            {
                if (mr == null || mr.gameObject == null) continue;
                string key = mr.gameObject.name;
                if (!meshLookup.ContainsKey(key))
                    meshLookup[key] = mr.gameObject;
            }
        }

        // Сканируем все болты в сцене и считаем parentMeshId
        Bolt[] bolts = FindObjectsOfType<Bolt>();
        foreach (var b in bolts)
        {
            if (b == null) continue;
            string meshId = b.parentMeshId;
            if (string.IsNullOrEmpty(meshId)) continue;

            if (!meshBoltCount.ContainsKey(meshId))
                meshBoltCount[meshId] = 0;
            meshBoltCount[meshId]++;

            // Если в lookup нашли GameObject с таким именем — сохраним
            if (!meshObjects.ContainsKey(meshId))
            {
                if (meshLookup.TryGetValue(meshId, out var go))
                    meshObjects[meshId] = go;
                else
                {
                    // попытка найти по имени в глобальной сцене (fallback)
                    var foundGO = GameObject.Find(meshId);
                    if (foundGO != null)
                        meshObjects[meshId] = foundGO;
                    else
                        meshObjects[meshId] = null; // можем не знать объект
                }
            }
        }

        // Лог: сколько мешей зарегистрировано
        if (meshBoltCount.Count > 0)
            Debug.Log($"[GameManager] Автоинициализация meshBoltCount: найдено {meshBoltCount.Count} меш(ов) с болтами.");
    }
}
