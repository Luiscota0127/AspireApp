namespace AspireApp.Pos.ViewModels;

/// <summary>
/// Fila del desglose de cobros por método de pago en el cierre de caja.
/// </summary>
public sealed record PaymentBreakdown(string Method, decimal Amount, int Tickets, decimal Share)
{
    public string ShareLabel => Share.ToString("P0");
    public string AmountLabel => Amount.ToString("C");
    public string TicketsLabel => Tickets == 1 ? "1 ticket" : $"{Tickets} tickets";
}