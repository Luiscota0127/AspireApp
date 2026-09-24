using System.Collections.ObjectModel;
using AspireApp.Pos.Models;
using AspireApp.Pos.Mvvm;
using AspireApp.Pos.Services;

namespace AspireApp.Pos.ViewModels;

/// <summary>
/// ViewModel de la terminal POS: catálogo con filtros, ticket, cobro e historial.
/// </summary>
public sealed class SaleViewModel : ObservableObject
{
    private const string AllCategoryValue = "Todos";

    /// <summary>Filas que muestra el historial en pantalla (el turno guarda todas).</summary>
    private const int HistorialMaximo = 20;

    private readonly ICatalogService _catalogService;
    private readonly TurnoStore _turnoStore;
    private readonly List<Product> _allProducts = [];
    private string _searchText = string.Empty;
    private string _selectedCategory = AllCategoryValue;
    private decimal _taxRate = 0.16m;
    private string _statusMessage = "Listo. Seleccione productos para iniciar la venta.";

    // Estado del cobro
    private bool _isPaymentOpen;
    private PaymentMethod _paymentMethod = PaymentMethod.Efectivo;
    private string _paymentEntry = string.Empty;
    private decimal _paidAmount;

    public SaleViewModel()
        : this(new InMemoryCatalogService(), new TurnoStore())
    {
    }

    public SaleViewModel(ICatalogService catalogService)
        : this(catalogService, new TurnoStore())
    {
    }

    public SaleViewModel(ICatalogService catalogService, TurnoStore turnoStore)
    {
        _catalogService = catalogService;
        _turnoStore = turnoStore;
        _allProducts.AddRange(catalogService.GetProducts());

        Cart = [];
        Cart.CollectionChanged += (_, _) =>
        {
            RefreshTotals();
            System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        };

        Products = new ObservableCollection<Product>(_allProducts);
        _categories = new ObservableCollection<CategoryInfo>
        (
        [
            new CategoryInfo(AllCategoryValue, "\uE8EC"),
            .. catalogService.GetCategories(),
        ]);
        _selectedCategoryItem = _categories[0];

        AddProductCommand = new RelayCommand(AddProduct);
        AddSelectedProductCommand = new RelayCommand(_ => AddSelectedProduct(), _ => SelectedProduct is not null);
        AddByCodeCommand = new RelayCommand(_ => AddByCode(SearchText));
        IncrementLineCommand = new RelayCommand(line => ChangeQuantity(line as SaleLine, 1));
        DecrementLineCommand = new RelayCommand(line => ChangeQuantity(line as SaleLine, -1));
        RemoveLineCommand = new RelayCommand(RemoveLine);
        ClearCartCommand = new RelayCommand(ClearCart, () => Cart.Count > 0);
        IncreaseDiscountCommand = new RelayCommand(() => CartDiscount += 5, () => Cart.Count > 0);
        DecreaseDiscountCommand = new RelayCommand(() => CartDiscount -= 5, () => Cart.Count > 0);
        OpenPaymentCommand = new RelayCommand(OpenPayment, () => Cart.Count > 0);
        CancelPaymentCommand = new RelayCommand(CancelPayment, () => IsPaymentOpen);
        ConfirmPaymentCommand = new RelayCommand(ConfirmPayment, () => IsPaymentOpen && IsPaymentSatisfied);
        SelectPaymentMethodCommand = new RelayCommand(p =>
        {
            if (p is PaymentMethod method)
            {
                PaymentMethod = method;
            }
        });
        PaymentKeyCommand = new RelayCommand(p => HandlePaymentKey(p as string));
        ExactAmountCommand = new RelayCommand(() => PaymentEntry = Total.ToString("0.00"));

        NavigateCommand = new RelayCommand(p =>
        {
            if (p is PosView view)
            {
                CurrentView = view;
            }
            else if (p is string name && Enum.TryParse<PosView>(name, out var parsed))
            {
                CurrentView = parsed;
            }
        });

        AdjustStockCommand = new RelayCommand(p => AdjustStock(p as Product), _ => true);

        LoadTurno();
        ApplyFilter();
    }

    public ObservableCollection<Product> Products { get; }

    private readonly ObservableCollection<CategoryInfo> _categories;
    public ObservableCollection<CategoryInfo> Categories => _categories;
    private CategoryInfo _selectedCategoryItem;
    public CategoryInfo SelectedCategoryItem
    {
        get => _selectedCategoryItem;
        set
        {
            if (value is not null && SetProperty(ref _selectedCategoryItem, value))
            {
                SelectedCategory = value.Name;
            }
        }
    }

    public ObservableCollection<SaleLine> Cart { get; }
    public ObservableCollection<Receipt> Receipts { get; } = [];

    /// <summary>
    /// Todas las ventas del turno, sin el tope del historial. Es la fuente de
    /// los totales de cierre de caja y de lo que se guarda en disco.
    /// </summary>
    private readonly List<Receipt> _allReceipts = [];

    /// <summary>Ventas del turno completo (para totales y persistencia).</summary>
    public IReadOnlyList<Receipt> AllReceipts => _allReceipts;

    private Product? _selectedProduct;
    /// <summary>Producto seleccionado en el catálogo (para el botón "Agregar").</summary>
    public Product? SelectedProduct
    {
        get => _selectedProduct;
        set => SetProperty(ref _selectedProduct, value);
    }

    private Receipt? _selectedReceipt;
    /// <summary>Ticket seleccionado en la vista de historial.</summary>
    public Receipt? SelectedReceipt
    {
        get => _selectedReceipt;
        set => SetProperty(ref _selectedReceipt, value);
    }

    public RelayCommand AddProductCommand { get; }
    public RelayCommand AddSelectedProductCommand { get; }
    public RelayCommand AddByCodeCommand { get; }
    public RelayCommand IncrementLineCommand { get; }
    public RelayCommand DecrementLineCommand { get; }
    public RelayCommand RemoveLineCommand { get; }
    public RelayCommand ClearCartCommand { get; }
    public RelayCommand IncreaseDiscountCommand { get; }
    public RelayCommand DecreaseDiscountCommand { get; }
    public RelayCommand OpenPaymentCommand { get; }
    public RelayCommand CancelPaymentCommand { get; }
    public RelayCommand ConfirmPaymentCommand { get; }
    public RelayCommand SelectPaymentMethodCommand { get; }
    public RelayCommand PaymentKeyCommand { get; }
    public RelayCommand ExactAmountCommand { get; }
    public RelayCommand NavigateCommand { get; }
    public RelayCommand AdjustStockCommand { get; }

    // ---- Navegación lateral ----------------------------------------------

    private PosView _currentView = PosView.Sale;
    public PosView CurrentView
    {
        get => _currentView;
        set
        {
            if (SetProperty(ref _currentView, value))
            {
                RaiseViewFlags();
            }
        }
    }

    public bool IsSaleView => CurrentView == PosView.Sale;
    public bool IsHistoryView => CurrentView == PosView.History;
    public bool IsInventoryView => CurrentView == PosView.Inventory;
    public bool IsCashCloseView => CurrentView == PosView.CashClose;

    public string CurrentViewTitle => CurrentView switch
    {
        PosView.History => "Historial de ventas",
        PosView.Inventory => "Inventario",
        PosView.CashClose => "Cierre de caja",
        _ => "Punto de Venta",
    };

    public string CurrentViewSubtitle => CurrentView switch
    {
        PosView.History => "Tickets cobrados en este turno",
        PosView.Inventory => "Existencias y alertas de reposición",
        PosView.CashClose => "Resumen y arqueo de la caja",
        _ => "Terminal 01",
    };

    private void RaiseViewFlags()
    {
        OnPropertyChanged(nameof(IsSaleView));
        OnPropertyChanged(nameof(IsHistoryView));
        OnPropertyChanged(nameof(IsInventoryView));
        OnPropertyChanged(nameof(IsCashCloseView));
        OnPropertyChanged(nameof(CurrentViewTitle));
        OnPropertyChanged(nameof(CurrentViewSubtitle));
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                ApplyFilter();
            }
        }
    }

    /// <summary>
    /// Etiquetas de stock bajo, recalculadas solo cuando cambia el inventario y
    /// no en cada tecla del buscador (antes se recorría todo el catálogo aquí).
    /// </summary>
    private int _lowStockCache = -1;

    private void InvalidateLowStockCache() => _lowStockCache = -1;

    public string SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            if (SetProperty(ref _selectedCategory, value))
            {
                ApplyFilter();
            }
        }
    }

    public decimal TaxRate
    {
        get => _taxRate;
        set
        {
            if (SetProperty(ref _taxRate, Math.Clamp(value, 0, 1)))
            {
                RefreshTotals();
            }
        }
    }

    private decimal _cartDiscount;
    /// <summary>Descuento global del ticket (monto). Se reparte proporcionalmente por línea.</summary>
    public decimal CartDiscount
    {
        get => _cartDiscount;
        set
        {
            var normalized = Math.Clamp(value, 0, GrossTotal);
            if (SetProperty(ref _cartDiscount, normalized))
            {
                RefreshTotals();
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    // ---- Estado del cobro -------------------------------------------------

    public bool IsPaymentOpen
    {
        get => _isPaymentOpen;
        private set
        {
            if (SetProperty(ref _isPaymentOpen, value))
            {
                OnPropertyChanged(nameof(IsCartOpen));
                OnPropertyChanged(nameof(CashPanelVisibility));
            }
        }
    }

    public bool IsCartOpen => !IsPaymentOpen;

    public System.Windows.Visibility CashPanelVisibility =>
        PaymentMethod == PaymentMethod.Efectivo ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

    public PaymentMethod PaymentMethod
    {
        get => _paymentMethod;
        set
        {
            if (SetProperty(ref _paymentMethod, value))
            {
                OnPropertyChanged(nameof(IsCashPayment));
                OnPropertyChanged(nameof(CashPanelVisibility));
                if (value != PaymentMethod.Efectivo)
                {
                    // Tarjeta/transferencia: se cobra el total exacto.
                    PaymentEntry = Total.ToString("0.00");
                }
            }
        }
    }

    public bool IsCashPayment => PaymentMethod == PaymentMethod.Efectivo;

    /// <summary>Monto recibido capturado en el teclado numérico (texto con formato).</summary>
    public string PaymentEntry
    {
        get => _paymentEntry;
        private set
        {
            if (SetProperty(ref _paymentEntry, value))
            {
                PaidAmount = ParseEntry(value);
            }
        }
    }

    public decimal PaidAmount
    {
        get => _paidAmount;
        private set
        {
            if (SetProperty(ref _paidAmount, value))
            {
                OnPropertyChanged(nameof(ChangeDue));
                OnPropertyChanged(nameof(IsPaymentSatisfied));
                OnPropertyChanged(nameof(ChangeLabel));
                System.Windows.Input.CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public decimal ChangeDue => Math.Max(0, PaidAmount - Total);
    public decimal AmountRemaining => Math.Max(0, Total - PaidAmount);
    public bool IsPaymentSatisfied => PaidAmount >= Total;

    public string ChangeLabel => IsPaymentSatisfied
        ? $"Cambio: {ChangeDue:C}"
        : $"Faltan: {AmountRemaining:C}";

    // ---- Totales ----------------------------------------------------------

    /// <summary>Suma de líneas antes de descuentos: base de los cálculos.</summary>
    public decimal GrossTotal => Cart.Sum(l => l.Gross);

    /// <summary>
    /// Suma de líneas con el descuento de línea YA restado (SaleLine.Subtotal
    /// descuenta, no es el bruto). Es la base de NetSubtotal.
    /// </summary>
    public decimal Subtotal => Cart.Sum(l => l.Subtotal);

    public decimal LineDiscountTotal => Cart.Sum(l => l.LineDiscount);

    /// <summary>
    /// Descuento total del ticket. Ojo: NO se suma LineDiscountTotal porque ya
    /// está incluido en Subtotal; sumarlo aquí restaba el descuento de línea dos
    /// veces y el ticket no cuadraba con el importe cobrado.
    /// </summary>
    public decimal DiscountTotal => _cartDiscount;

    /// <summary>
    /// Base gravable del IVA: solo las líneas que NO son exentas, ya con su
    /// descuento de línea aplicado. Antes se calculaba el impuesto sobre todo el
    /// subtotal, cobrando IVA a alimentos que van a tasa 0.
    /// </summary>
    public decimal TaxableSubtotal => Cart.Where(l => !l.IsTaxExempt).Sum(l => l.Subtotal);

    /// <summary>Base exenta: no causa IVA (alimentos, por ejemplo).</summary>
    public decimal ExemptSubtotal => Cart.Where(l => l.IsTaxExempt).Sum(l => l.Subtotal);

    /// <summary>
    /// Parte del descuento del ticket que aplica a las líneas gravables, en la
    /// misma proporción que esas líneas representan del subtotal. Sin esto, el
    /// descuento global bajaba el IVA aunque todo el descuento fuera de exentos.
    /// </summary>
    private decimal TaxableDiscountFactor =>
        Subtotal <= 0 ? 0 : TaxableSubtotal / Subtotal;

    public decimal NetSubtotal => Math.Max(0, Subtotal - _cartDiscount);

    /// <summary>Base gravable después de aplicar la parte proporcional del descuento.</summary>
    public decimal NetTaxableSubtotal =>
        Math.Max(0, TaxableSubtotal - _cartDiscount * TaxableDiscountFactor);

    public decimal TaxAmount => Math.Round(NetTaxableSubtotal * TaxRate, 2);

    public decimal Total => Math.Max(0, NetSubtotal) + TaxAmount;

    public int ItemCount => Cart.Sum(l => l.Quantity);

    public int LineCount => Cart.Count;

    public string ItemCountLabel => ItemCount == 1 ? "1 artículo" : $"{ItemCount} artículos";

    /// <summary>Etiqueta compacta del ticket actual (número de líneas y artículos).</summary>
    public string LabelLabel => Cart.Count == 0
        ? "Ticket vacío"
        : $"{LineCount} línea(s) · {ItemCountLabel}";

    public int LowStockCount
    {
        get
        {
            if (_lowStockCache < 0)
            {
                _lowStockCache = _allProducts.Count(p => p.IsLowStock || p.IsOutOfStock);
            }
            return _lowStockCache;
        }
    }

    // ---- Operaciones de venta --------------------------------------------

    private void AddProduct(object? parameter)
    {
        if (parameter is not Product product)
        {
            return;
        }

        var existing = Cart.FirstOrDefault(l => l.Sku == product.Sku);
        var currentQuantity = existing?.Quantity ?? 0;

        if (currentQuantity >= product.Stock)
        {
            StatusMessage = $"Sin stock suficiente de {product.Name} (disponible: {product.Stock}).";
            return;
        }

        if (existing is not null)
        {
            existing.Quantity++;
        }
        else
        {
            Cart.Add(new SaleLine(product));
        }

        StatusMessage = $"Agregado: {product.Name}";
    }

    /// <summary>Agrega el producto seleccionado en el catálogo al ticket.</summary>
    private void AddSelectedProduct()
    {
        if (SelectedProduct is { } product)
        {
            AddProduct(product);
        }
    }

    /// <summary>Alta rápida desde el buscador: SKU exacto o primer resultado.</summary>
    public bool AddByCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return false;
        }

        var term = code.Trim();
        var product = _allProducts.FirstOrDefault(p => string.Equals(p.Sku, term, StringComparison.OrdinalIgnoreCase))
                      ?? _allProducts.FirstOrDefault(p => p.Name.Contains(term, StringComparison.OrdinalIgnoreCase));

        if (product is null)
        {
            StatusMessage = $"No se encontró ningún producto para \"{term}\".";
            return false;
        }

        AddProduct(product);
        SearchText = string.Empty;
        StatusMessage = $"Agregado por código: {product.Name}.";
        return true;
    }

    private void ChangeQuantity(SaleLine? line, int delta)
    {
        if (line is null)
        {
            return;
        }

        var target = line.Quantity + delta;
        if (delta > 0 && target > line.Product.Stock)
        {
            StatusMessage = $"Sin stock suficiente de {line.Name} (disponible: {line.Product.Stock}).";
            return;
        }

        if (target <= 0)
        {
            Cart.Remove(line);
            StatusMessage = $"Eliminado: {line.Name}";
            return;
        }

        line.Quantity = target;
    }

    private void RemoveLine(object? parameter)
    {
        if (parameter is SaleLine line)
        {
            Cart.Remove(line);
            StatusMessage = $"Eliminado: {line.Name}";
        }
    }

    private void ClearCart()
    {
        Cart.Clear();
        CartDiscount = 0;
        IsPaymentOpen = false;
        StatusMessage = "Ticket vaciado.";
    }

    private void OpenPayment()
    {
        PaymentEntry = string.Empty;
        PaymentMethod = PaymentMethod.Efectivo;
        IsPaymentOpen = true;
        StatusMessage = $"Cobrando {Total:C}. Seleccione el método de pago.";
    }

    private void CancelPayment()
    {
        IsPaymentOpen = false;
        PaymentEntry = string.Empty;
        StatusMessage = "Cobro cancelado. El ticket sigue abierto.";
    }

    private void ConfirmPayment()
    {
        var receipt = new Receipt
        {
            Number = $"T-{DateTime.Now:yyyyMMdd-HHmmss}",
            Timestamp = DateTime.Now,
            Lines = Cart.Select(l => new ReceiptLine(l.Sku, l.Name, l.Quantity, l.UnitPrice, l.Subtotal)).ToList(),
            Payment = new Payment
            {
                Method = PaymentMethod,
                Subtotal = Subtotal,
                Discount = DiscountTotal,
                Tax = TaxAmount,
                Total = Total,
                AmountPaid = PaidAmount,
            },
        };

        // Descontar inventario y registrar la venta.
        foreach (var line in Cart)
        {
            line.Product.Stock -= line.Quantity;
        }

        Receipts.Insert(0, receipt);

        // El historial visible se limita a 20 filas para no crecer sin control,
        // pero TODAS las ventas del turno se conservan en la lista completa: el
        // cierre de caja se calcula sobre esa, no sobre lo que se ve en pantalla.
        _allReceipts.Add(receipt);
        if (Receipts.Count > HistorialMaximo)
        {
            Receipts.RemoveAt(Receipts.Count - 1);
        }

        LastReceipt = receipt;
        Cart.Clear();
        CartDiscount = 0;
        IsPaymentOpen = false;
        PaymentEntry = string.Empty;

        // El turno se guarda recién cobrado: si la terminal se cierra, la venta
        // ya no se pierde.
        SaveTurno();

        RaiseInventoryTotals();
        RaiseCashCloseTotals();

        StatusMessage = $"Venta {receipt.Number} cobrada: {receipt.ItemCount} artículo(s) por {receipt.Total:C} ({receipt.Payment.MethodLabel}).";
    }

    private Receipt? _lastReceipt;
    public Receipt? LastReceipt
    {
        get => _lastReceipt;
        private set
        {
            if (SetProperty(ref _lastReceipt, value))
            {
                OnPropertyChanged(nameof(HasLastReceipt));
            }
        }
    }

    public bool HasLastReceipt => LastReceipt is not null;

    public decimal ReceiptsTotal => _allReceipts.Sum(r => r.Total);

    // ---- Teclado numérico -------------------------------------------------

    private void HandlePaymentKey(string? key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return;
        }

        switch (key)
        {
            case "C":
                PaymentEntry = string.Empty;
                return;
            case "B":
                PaymentEntry = PaymentEntry.Length <= 1 ? string.Empty : PaymentEntry[..^1];
                return;
            case "OK":
                if (ConfirmPaymentCommand.CanExecute(null))
                {
                    ConfirmPaymentCommand.Execute(null);
                }
                return;
            case ".":
                if (!PaymentEntry.Contains('.'))
                {
                    PaymentEntry = PaymentEntry.Length == 0 ? "0." : PaymentEntry + ".";
                }
                return;
        }

        if (PaymentEntry.Length >= 10)
        {
            return;
        }

        PaymentEntry = PaymentEntry == "0" ? key : PaymentEntry + key;
    }

    private static decimal ParseEntry(string value)
        => decimal.TryParse(value, System.Globalization.NumberStyles.Number,
            System.Globalization.CultureInfo.CurrentCulture, out var parsed) ? parsed : 0m;

    // ---- Filtro de catálogo ----------------------------------------------

    private void ApplyFilter()
    {
        var term = SearchText?.Trim() ?? string.Empty;
        var category = SelectedCategory;

        var matches = _allProducts.Where(p =>
            (string.IsNullOrEmpty(category) || category == AllCategoryValue || p.Category == category) &&
            (string.IsNullOrEmpty(term) ||
             p.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
             p.Sku.Contains(term, StringComparison.OrdinalIgnoreCase) ||
             p.Category.Contains(term, StringComparison.OrdinalIgnoreCase)));

        Products.Clear();
        foreach (var product in matches)
        {
            Products.Add(product);
        }

        OnPropertyChanged(nameof(ResultsLabel));
    }

    public string ResultsLabel => $"{Products.Count} producto(s) · {LowStockCount} con stock bajo";

    // ---- Inventario ------------------------------------------------------

    public ObservableCollection<Product> Inventory => Products;

    public int TotalUnits => _allProducts.Sum(p => p.Stock);
    public decimal InventoryValue => _allProducts.Sum(p => p.Stock * p.Price);
    public int OutOfStockCount => _allProducts.Count(p => p.IsOutOfStock);

    public string InventorySummary =>
        $"{_allProducts.Count} productos · {TotalUnits} unidades · valor {InventoryValue:C}";

    /// <summary>Alta/baja manual de existencias desde la vista de inventario.</summary>
    private void AdjustStock(Product? product)
    {
        if (product is null)
        {
            return;
        }

        // Los productos agotados se reponen a un lote inicial; el resto suma 1.
        var wasOutOfStock = product.IsOutOfStock;
        product.Stock = wasOutOfStock ? 10 : product.Stock + 1;

        StatusMessage = wasOutOfStock
            ? $"Repuesto: {product.Name} (+10 unidades)."
            : $"Stock actualizado: {product.Name} = {product.Stock}.";

        // El stock es parte del turno: se guarda junto con las ventas.
        SaveTurno();
        RaiseInventoryTotals();
    }

    private void RaiseInventoryTotals()
    {
        InvalidateLowStockCache();
        OnPropertyChanged(nameof(TotalUnits));
        OnPropertyChanged(nameof(InventoryValue));
        OnPropertyChanged(nameof(OutOfStockCount));
        OnPropertyChanged(nameof(InventorySummary));
        OnPropertyChanged(nameof(LowStockCount));
        OnPropertyChanged(nameof(ResultsLabel));
        OnPropertyChanged(nameof(LowStockItems));
    }

    public IEnumerable<Product> LowStockItems =>
        _allProducts.Where(p => p.IsLowStock || p.IsOutOfStock).OrderBy(p => p.Stock);

    // ---- Cierre de caja --------------------------------------------------

    public int TicketCount => _allReceipts.Count;
    public decimal TicketsTotal => _allReceipts.Sum(r => r.Total);
    public decimal AverageTicket => TicketCount == 0 ? 0 : TicketsTotal / TicketCount;
    public decimal CashTotal => _allReceipts.Where(r => r.Payment.Method == PaymentMethod.Efectivo).Sum(r => r.Total);
    public decimal CardTotal => _allReceipts.Where(r => r.Payment.Method is PaymentMethod.TarjetaDebito or PaymentMethod.TarjetaCredito).Sum(r => r.Total);
    public decimal TransferTotal => _allReceipts.Where(r => r.Payment.Method == PaymentMethod.Transferencia).Sum(r => r.Total);

    /// <summary>Efectivo que debe haber en el cajón: fondo inicial + ventas en efectivo.</summary>
    public decimal ExpectedCash => OpeningFloat + CashTotal;

    public decimal OpeningFloat { get; } = 500m;

    public string CashCloseSummary => TicketCount == 0
        ? "Aún no hay ventas registradas en este turno."
        : $"{TicketCount} ticket(s) · {ItemSoldTotal} artículos vendidos · promedio {AverageTicket:C}";

    public int ItemSoldTotal => _allReceipts.Sum(r => r.ItemCount);

    private void RaiseCashCloseTotals()
    {
        OnPropertyChanged(nameof(TicketCount));
        OnPropertyChanged(nameof(TicketsTotal));
        OnPropertyChanged(nameof(AverageTicket));
        OnPropertyChanged(nameof(CashTotal));
        OnPropertyChanged(nameof(CardTotal));
        OnPropertyChanged(nameof(TransferTotal));
        OnPropertyChanged(nameof(ExpectedCash));
        OnPropertyChanged(nameof(ItemSoldTotal));
        OnPropertyChanged(nameof(CashCloseSummary));
        OnPropertyChanged(nameof(ReceiptsTotal));
    }

    public IReadOnlyList<PaymentBreakdown> PaymentBreakdown => PaymentBreakdownFor(_allReceipts);

    public static IReadOnlyList<PaymentBreakdown> PaymentBreakdownFor(IEnumerable<Receipt> receipts)
    {
        var list = receipts.ToList();
        var total = list.Sum(r => r.Total);
        return list
            .GroupBy(r => r.Payment.MethodLabel)
            .Select(g => new PaymentBreakdown(
                g.Key,
                g.Sum(r => r.Total),
                g.Count(),
                total <= 0 ? 0 : g.Sum(r => r.Total) / total))
            .OrderByDescending(b => b.Amount)
            .ToList();
    }

    // ---- Persistencia del turno -----------------------------------------

    /// <summary>
    /// Carga el turno guardado en disco al abrir la terminal: repone el historial
    /// y las existencias que quedaron de la sesión anterior.
    /// </summary>
    public void LoadTurno()
    {
        var state = _turnoStore.Load();
        LoadFrom(state.Receipts.Select(r => r.ToReceipt()), state.Stock);
    }

    /// <summary>
    /// Carga el turno desde lo que haya guardado el host (hoy en memoria; después
    /// será la llamada al ApiService cuando exponga los endpoints de ventas).
    /// </summary>
    public void LoadFrom(IEnumerable<Receipt> receipts, IReadOnlyDictionary<string, int>? stock = null)
    {
        // El stock guardado manda: es el que refleja las ventas ya cobradas.
        if (stock is not null)
        {
            foreach (var product in _allProducts)
            {
                if (stock.TryGetValue(product.Sku, out var units))
                {
                    product.Stock = units;
                }
            }
        }

        _allReceipts.Clear();
        _allReceipts.AddRange(receipts.OrderByDescending(r => r.Timestamp));

        // El historial en pantalla solo muestra las últimas HistorialMaximo;
        // los totales del turno salen de _allReceipts, no de aquí.
        Receipts.Clear();
        foreach (var receipt in _allReceipts.Take(HistorialMaximo))
        {
            Receipts.Add(receipt);
        }

        LastReceipt = _allReceipts.FirstOrDefault();

        RaiseInventoryTotals();
        RaiseCashCloseTotals();
        OnPropertyChanged(nameof(PaymentBreakdown));
        OnPropertyChanged(nameof(HasLastReceipt));

        StatusMessage = _allReceipts.Count == 0
            ? "Turno abierto. Seleccione productos para iniciar la venta."
            : $"Turno abierto con {_allReceipts.Count} venta(s) registrada(s) ({ReceiptsTotal:C}).";
    }

    /// <summary>
    /// Guarda el turno en disco. Si falla, la venta ya está cobrada y registrada
    /// en memoria (el modelo tampoco simula persistencia en base de datos).
    /// </summary>
    public void SaveTurno()
    {
        _turnoStore.Save(new TurnoState
        {
            // Se guarda el turno COMPLETO, no solo lo que muestra el historial.
            Receipts = _allReceipts.Select(ReceiptDto.From).ToList(),
            Stock = _allProducts.ToDictionary(p => p.Sku, p => p.Stock),
        });
    }

    private void RefreshTotals()
    {
        OnPropertyChanged(nameof(GrossTotal));
        OnPropertyChanged(nameof(Subtotal));
        OnPropertyChanged(nameof(LineDiscountTotal));
        OnPropertyChanged(nameof(DiscountTotal));
        OnPropertyChanged(nameof(NetSubtotal));
        OnPropertyChanged(nameof(TaxableSubtotal));
        OnPropertyChanged(nameof(ExemptSubtotal));
        OnPropertyChanged(nameof(NetTaxableSubtotal));
        OnPropertyChanged(nameof(TaxAmount));
        OnPropertyChanged(nameof(Total));
        OnPropertyChanged(nameof(ItemCount));
        OnPropertyChanged(nameof(LineCount));
        OnPropertyChanged(nameof(ItemCountLabel));
        OnPropertyChanged(nameof(LabelLabel));
        OnPropertyChanged(nameof(HasLastReceipt));
        OnPropertyChanged(nameof(ReceiptsTotal));
        OnPropertyChanged(nameof(ChangeDue));
        OnPropertyChanged(nameof(IsPaymentSatisfied));
        OnPropertyChanged(nameof(ChangeLabel));
    }
}
