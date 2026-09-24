using AspireApp.Pos.Models;

namespace AspireApp.Pos.Services;

/// <summary>
/// Fuente de productos de la terminal. Hoy es en memoria; se puede sustituir
/// por un cliente HTTP hacia AspireApp.ApiService sin tocar los ViewModels.
/// </summary>
public interface ICatalogService
{
    IReadOnlyList<Product> GetProducts();
    IReadOnlyList<CategoryInfo> GetCategories();
}

public sealed record CategoryInfo(string Name, string Glyph);

/// <summary>
/// Catálogo de demostración de una cafetería.
/// </summary>
public sealed class InMemoryCatalogService : ICatalogService
{
    private readonly List<Product> _products =
    [
        new() { Sku = "BEV-001", Name = "Café americano", Category = "Bebidas", Price = 35.00m, Stock = 120 },
        new() { Sku = "BEV-002", Name = "Capuccino", Category = "Bebidas", Price = 48.00m, Stock = 80 },
        new() { Sku = "BEV-003", Name = "Agua mineral", Category = "Bebidas", Price = 22.00m, Stock = 200 },
        new() { Sku = "BEV-004", Name = "Té chai latte", Category = "Bebidas", Price = 52.00m, Stock = 45 },
        // Alimentos sin IVA (tasa 0 en México). El resto causa 16%.
        new() { Sku = "FOD-001", Name = "Sándwich club", Category = "Alimentos", Price = 89.00m, Stock = 25, IsTaxExempt = true },
        new() { Sku = "FOD-002", Name = "Bagel con queso", Category = "Alimentos", Price = 62.00m, Stock = 30, IsTaxExempt = true },
        new() { Sku = "FOD-003", Name = "Muffin de chocolate", Category = "Alimentos", Price = 45.00m, Stock = 40, IsTaxExempt = true },
        new() { Sku = "FOD-004", Name = "Ensalada césar", Category = "Alimentos", Price = 95.00m, Stock = 4, IsTaxExempt = true },
        new() { Sku = "POS-001", Name = "Cheesecake", Category = "Postres", Price = 75.00m, Stock = 15 },
        new() { Sku = "POS-002", Name = "Brownie", Category = "Postres", Price = 55.00m, Stock = 18 },
        new() { Sku = "POS-003", Name = "Galleta de avena", Category = "Postres", Price = 28.00m, Stock = 3 },
        new() { Sku = "MER-001", Name = "Taza térmica", Category = "Mercancía", Price = 250.00m, Stock = 10 },
        new() { Sku = "MER-002", Name = "Grano de café 500g", Category = "Mercancía", Price = 320.00m, Stock = 12 },
        new() { Sku = "MER-003", Name = "Prensa francesa", Category = "Mercancía", Price = 480.00m, Stock = 0 },
    ];

    public IReadOnlyList<Product> GetProducts() => _products;

    public IReadOnlyList<CategoryInfo> GetCategories() =>
        _products
            .Select(p => p.Category)
            .Distinct()
            .OrderBy(c => c)
            .Select(c => new CategoryInfo(c, GlyphFor(c)))
            .ToList();

    private static string GlyphFor(string category) => category switch
    {
        "Bebidas" => "\uE95B",
        "Alimentos" => "\uE8B6",
        "Postres" => "\uE706",
        "Mercancía" => "\uE7B8",
        _ => "\uE8EC",
    };
}