namespace AspireApp.Pos.Models;

/// <summary>
/// Una línea ya cobrada, guardada para el historial y el ticket impreso.
/// </summary>
public sealed record ReceiptLine(string Sku, string Name, int Quantity, decimal UnitPrice, decimal Subtotal);

/// <summary>
/// Venta cerrada. Se conserva en memoria para consultar el historial de la terminal.
/// </summary>
public sealed class Receipt
{
    public required string Number { get; init; }
    public required DateTime Timestamp { get; init; }
    public required IReadOnlyList<ReceiptLine> Lines { get; init; }
    public required Payment Payment { get; init; }
    public string Cashier { get; init; } = "Cajero 01";

    public decimal Total => Payment.Total;
    public int ItemCount => Lines.Sum(l => l.Quantity);
    public string TimeLabel => Timestamp.ToString("HH:mm:ss");
    public string TotalLabel => Total.ToString("C");
    /// <summary>Etiqueta corta del importe, para listados y columnas estrechas.</summary>
    public string AmountLabel => Total.ToString("C");
}
/// <summary>
/// Formas serializables del ticket. Se mantienen aparte del modelo de la vista
/// para que el XAML siga intacto si algún día cambia la forma de guardar.
/// </summary>
public sealed class ReceiptDto
{
    public string Number { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public List<ReceiptLineDto> Lines { get; set; } = [];
    public PaymentDto Payment { get; set; } = new();
    public string Cashier { get; set; } = "Cajero 01";

    public Receipt ToReceipt() => new()
    {
        Number = Number,
        Timestamp = Timestamp,
        Cashier = Cashier,
        Lines = Lines.Select(l => new ReceiptLine(l.Sku, l.Name, l.Quantity, l.UnitPrice, l.Subtotal)).ToList(),
        Payment = new Payment
        {
            Method = Payment.Method,
            Subtotal = Payment.Subtotal,
            Discount = Payment.Discount,
            Tax = Payment.Tax,
            Total = Payment.Total,
            AmountPaid = Payment.AmountPaid,
        },
    };

    public static ReceiptDto From(Receipt receipt) => new()
    {
        Number = receipt.Number,
        Timestamp = receipt.Timestamp,
        Cashier = receipt.Cashier,
        Lines = receipt.Lines
            .Select(l => new ReceiptLineDto { Sku = l.Sku, Name = l.Name, Quantity = l.Quantity, UnitPrice = l.UnitPrice, Subtotal = l.Subtotal })
            .ToList(),
        Payment = new PaymentDto
        {
            Method = receipt.Payment.Method,
            Subtotal = receipt.Payment.Subtotal,
            Discount = receipt.Payment.Discount,
            Tax = receipt.Payment.Tax,
            Total = receipt.Payment.Total,
            AmountPaid = receipt.Payment.AmountPaid,
        },
    };
}

public sealed class ReceiptLineDto
{
    public string Sku { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Subtotal { get; set; }
}

public sealed class PaymentDto
{
    public PaymentMethod Method { get; set; }
    public decimal Subtotal { get; set; }
    public decimal Discount { get; set; }
    public decimal Tax { get; set; }
    public decimal Total { get; set; }
    public decimal AmountPaid { get; set; }
}
