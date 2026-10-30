using UnityEngine;

/// <summary>Магазин АК на 30 патронов.</summary>
public class Magazine : SocketableItem
{
    public int capacity = 30;
    public int ammo = 30;

    public bool TakeRound()
    {
        if (ammo <= 0) return false;
        ammo--;
        return true;
    }

    public void Refill()
    {
        ammo = capacity;
    }
}
