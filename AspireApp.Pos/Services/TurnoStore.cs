using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using AspireApp.Pos.Models;

namespace AspireApp.Pos.Services;

/// <summary>
/// Estado del turno tal como se guarda en disco: las ventas cobradas y las
/// existencias resultantes. Guardar el stock (y no solo los tickets) evita tener
/// que reconstruirlo descontando ventas cada vez que se abre la terminal.
/// </summary>
public sealed class TurnoState
{
    public List<ReceiptDto> Receipts { get; set; } = [];
    public Dictionary<string, int> Stock { get; set; } = [];
}

public sealed class TurnoStore
{
    private readonly string _path;
    private readonly JsonSerializerOptions _options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public TurnoStore()
    {
        var dir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AspireApp.Pos");
        Directory.CreateDirectory(dir);
        _path = System.IO.Path.Combine(dir, "turno.json");
    }

    public string Path => _path;

    public TurnoState Load()
    {
        if (!File.Exists(_path))
        {
            return new TurnoState();
        }

        try
        {
            var json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<TurnoState>(json, _options) ?? new TurnoState();
        }
        catch (Exception ex)
        {
            // Un turno corrupto no debe impedir abrir la terminal: se arranca en
            // limpio y se conserva el archivo (renombrado) para poder revisarlo.
            TryBackupCorrupt();
            Console.Error.WriteLine($"No se pudo leer el turno ({_path}): {ex.Message}");
            return new TurnoState();
        }
    }

    public void Save(TurnoState state)
    {
        try
        {
            File.WriteAllText(_path, JsonSerializer.Serialize(state, _options));
        }
        catch (Exception ex)
        {
            // No poder guardar no cancela una venta ya cobrada; solo se avisa.
            Console.Error.WriteLine($"No se pudo guardar el turno ({_path}): {ex.Message}");
        }
    }

    private void TryBackupCorrupt()
    {
        try
        {
            var backup = _path + ".corrupt";
            File.Copy(_path, backup, overwrite: true);
        }
        catch
        {
            // Un respaldo que falla no cambia el resultado: seguimos en limpio.
        }
    }
}
