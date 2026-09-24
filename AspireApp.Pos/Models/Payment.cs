namespace AspireApp.Pos.Models;

/// <summary>
/// Formas de pago aceptadas por la terminal.
/// </summary>
public enum PaymentMethod
{
    Efectivo,
    TarjetaDebito,
    TarjetaCredito,
    Transferencia,
}

/// <summary>
/// Desglose de un cobro: método, montos recibidos y cambio.
/// </summary>
public sealed class Payment
{
    public required PaymentMethod Method { get; init; }
    public decimal Subtotal { get; init; }
    public decimal Discount { get; init; }
    public decimal Tax { get; init; }
    public decimal Total { get; init; }
    public decimal AmountPaid { get; init; }
    public decimal Change => Math.Max(0, AmountPaid - Total);
    public bool IsPaid => AmountPaid >= Total;

    public string MethodLabel => Method switch
    {
        PaymentMethod.Efectivo => "Efectivo",
        PaymentMethod.TarjetaDebito => "Tarjeta débito",
        PaymentMethod.TarjetaCredito => "Tarjeta crédito",
        PaymentMethod.Transferencia => "Transferencia",
        _ => Method.ToString(),
    };
}