/// <summary>Планка для установки прицела.</summary>
public class ScopeSocket : ItemSocket
{
    protected override bool Accepts(SocketableItem item) => item is WeaponScope;
}
