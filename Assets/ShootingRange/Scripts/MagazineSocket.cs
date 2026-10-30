/// <summary>Шахта магазина автомата.</summary>
public class MagazineSocket : ItemSocket
{
    protected override bool Accepts(SocketableItem item) => item is Magazine;
}
