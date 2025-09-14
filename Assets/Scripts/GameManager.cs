using System.Collections.Generic;
using UnityEngine;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance;

    [Header("Prefabs & References")]
    public GameObject basketPrefab;
    public Material[] colorMaterials;
    public Transform basketParent;
    public Buffer buffer;

    [Header("Gameplay Settings")]
    public int totalBaskets = 4;
    public int activeBasketsCount = 2;

    private List<Basket> allBaskets = new List<Basket>();
    private int nextBasketIndex = 0;

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        // Создаем список корзинок
        for (int i = 0; i < totalBaskets; i++)
        {
            int colorIndex = i % colorMaterials.Length;
            allBaskets.Add(CreateBasket(colorIndex));
        }

        // Активируем первые две
        for (int i = 0; i < activeBasketsCount; i++)
        {
            ActivateNextBasket();
        }
    }

    private Basket CreateBasket(int colorIndex)
    {
        GameObject go = Instantiate(basketPrefab, basketParent);
        var basket = go.GetComponent<Basket>();
        basket.Init(colorIndex, colorMaterials[colorIndex]);
        go.SetActive(false);
        return basket;
    }

    private void ActivateNextBasket()
    {
        if (nextBasketIndex >= allBaskets.Count) return;
        allBaskets[nextBasketIndex].gameObject.SetActive(true);
        nextBasketIndex++;
    }

    public void OnBoltClicked(Bolt bolt)
    {
        // Пытаемся добавить в активные корзины
        foreach (var basket in GetActiveBaskets())
        {
            if (basket.TryAddBolt(bolt))
            {
                CheckBasketFull(basket);
                return;
            }
        }

        // Если не подошло — отправляем в буфер
        if (!buffer.TryAddBolt(bolt))
        {
            Debug.Log("Game Over! Буфер переполнен.");
        }
    }

    private void CheckBasketFull(Basket basket)
    {
        if (basket.IsFull)
        {
            Destroy(basket.gameObject);
            ActivateNextBasket();
            buffer.TryFlushToBaskets(GetActiveBaskets());
        }
    }

    private List<Basket> GetActiveBaskets()
    {
        var result = new List<Basket>();
        for (int i = 0; i < nextBasketIndex; i++)
        {
            if (allBaskets[i].gameObject.activeSelf && !allBaskets[i].IsFull)
                result.Add(allBaskets[i]);
        }
        return result;
    }
}
