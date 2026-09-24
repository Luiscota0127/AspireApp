namespace AspireApp.Pos.Models;

/// <summary>
/// Producto disponible para la venta.
/// </summary>
public sealed class Product : Mvvm.ObservableObject
{
    private int _stock;

    public required string Sku { get; init; }
    public required string Name { get; init; }
    public string Category { get; init; } = "General";
    public decimal Price { get; init; }
    public bool IsTaxExempt { get; init; }

    public int Stock
    {
        get => _stock;
        set
        {
            // Las existencias nunca bajan de cero, ni siquiera al cargar un turno
            // guardado con datos viejos.
            if (SetProperty(ref _stock, Math.Max(0, value)))
            {
                OnPropertyChanged(nameof(IsLowStock));
                OnPropertyChanged(nameof(IsOutOfStock));
                OnPropertyChanged(nameof(StockLabel));
            }
        }
    }

    public bool IsLowStock => Stock is > 0 and <= 5;
    public bool IsOutOfStock => Stock <= 0;
    public string StockLabel => Stock <= 0 ? "agotado" : $"stock {Stock}";

    public string Display => $"{Name} ({Sku})";
}