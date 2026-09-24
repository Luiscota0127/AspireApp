using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace AspireApp.Pos;

/// <summary>Convierte montos (decimal, número o texto) a moneda para mostrar en la vista.</summary>
public sealed class CurrencyConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var amount = value switch
        {
            decimal d => d,
            double d => (decimal)d,
            int i => i,
            long l => l,
            float f => (decimal)f,
            // El keypad guarda la entrada como texto; sin este caso la convierte a 0 y el
            // display nunca reflejaba lo tecleado.
            string s => decimal.TryParse(s, NumberStyles.Number,
                culture ?? CultureInfo.CurrentCulture, out var parsed) ? parsed : 0m,
            _ => 0m,
        };

        return amount.ToString("C", culture ?? CultureInfo.CurrentCulture);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var text = value?.ToString() ?? string.Empty;
        return decimal.TryParse(text, NumberStyles.Currency, culture ?? CultureInfo.CurrentCulture, out var parsed)
            ? parsed
            : 0m;
    }
}

/// <summary>Invierte un booleano: útil para IsEnabled de botones.</summary>
public sealed class InverseBooleanConverter : IValueConverter
{
    public static readonly InverseBooleanConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool flag && !flag;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool flag && !flag;
}

/// <summary>Muestra un elemento sólo cuando el texto está vacío (placeholder).</summary>
public sealed class EmptyTextToVisibilityConverter : IValueConverter
{
    public static readonly EmptyTextToVisibilityConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => string.IsNullOrEmpty(value as string) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Enlaza un RadioButton a un valor de enum (dos vías).</summary>
public sealed class EnumToBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is not null && value.Equals(parameter);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true && parameter is not null ? parameter : Binding.DoNothing;
}
/// <summary>
/// Muestra un elemento sólo cuando el monto es mayor que cero. Se usa para
/// ocultar filas que no aportan nada (por ejemplo, base exenta en un ticket que
/// no tiene productos exentos).
/// </summary>
public sealed class GreaterThanZeroToVisibilityConverter : IValueConverter
{
    public static readonly GreaterThanZeroToVisibilityConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var amount = value switch
        {
            decimal d => d,
            double d => (decimal)d,
            int i => i,
            long l => l,
            _ => 0m,
        };

        return amount > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
