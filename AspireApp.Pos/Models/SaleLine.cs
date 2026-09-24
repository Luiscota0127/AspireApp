using AspireApp.Pos.Mvvm;

namespace AspireApp.Pos.Models;

/// <summary>
/// Línea del carrito de venta. Recalcula el subtotal al cambiar la cantidad.
/// </summary>
public sealed class SaleLine : ObservableObject
{
    private int _quantity;
    private decimal _lineDiscount;

    public SaleLine(Product product, int quantity = 1)
    {
        Product = product;
        _quantity = quantity;
    }

    public Product Product { get; }

    public string Sku => Product.Sku;
    public string Name => Product.Name;
    public decimal UnitPrice => Product.Price;
    public bool IsTaxExempt => Product.IsTaxExempt;

    public int Quantity
    {
        get => _quantity;
        set
        {
            if (SetProperty(ref _quantity, Math.Max(0, value)))
            {
                RaiseAmounts();
            }
        }
    }

    /// <summary>Descuento manual aplicado a la línea (monto, no porcentaje).</summary>
    public decimal LineDiscount
    {
        get => _lineDiscount;
        set
        {
            if (SetProperty(ref _lineDiscount, Math.Max(0, value)))
            {
                RaiseAmounts();
            }
        }
    }

    public decimal Gross => UnitPrice * Quantity;

    public decimal Subtotal => Math.Max(0, Gross - LineDiscount);

    private void RaiseAmounts()
    {
        OnPropertyChanged(nameof(Gross));
        OnPropertyChanged(nameof(Subtotal));
    }
}