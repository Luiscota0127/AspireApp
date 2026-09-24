using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using AspireApp.Pos.ViewModels;
namespace AspireApp.Pos;
public partial class MainWindow : Window
{
    private readonly SaleViewModel _viewModel;
    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new SaleViewModel();
        DataContext = _viewModel;
    }
    /// <summary>
    /// Al cerrar la terminal se guarda el turno: si se cobra algo y se cierra sin
    /// volver a la vista de venta, el ticket tampoco se pierde.
    /// </summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        _viewModel.SaveTurno();
        base.OnClosing(e);
    }
    /// <summary>
    /// Guarda también al cerrar de verdad. OnClosing no corre cuando el proceso
    /// se mata (cerrar sesión de Windows, taskkill, apagón), y el turno igual
    /// debe quedar en disco.
    /// </summary>
    protected override void OnClosed(EventArgs e)
    {
        _viewModel.SaveTurno();
        base.OnClosed(e);
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.F1:
                SearchBox.Focus();
                SearchBox.SelectAll();
                e.Handled = true;
                return;
            case Key.F2:
                if (_viewModel.OpenPaymentCommand.CanExecute(null))
                {
                    _viewModel.OpenPaymentCommand.Execute(null);
                }

                e.Handled = true;
                return;
            case Key.F4:
                if (_viewModel.ClearCartCommand.CanExecute(null))
                {
                    _viewModel.ClearCartCommand.Execute(null);
                }

                e.Handled = true;
                return;
        }

        base.OnPreviewKeyDown(e);
    }

    /// <summary>Enter en el buscador da de alta el SKU escrito (lector de códigos de barras).</summary>
    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        _viewModel.AddByCode(SearchBox.Text);
        SearchBox.SelectAll();
        e.Handled = true;
    }
}