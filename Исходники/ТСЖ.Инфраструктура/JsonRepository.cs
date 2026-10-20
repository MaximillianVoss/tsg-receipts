using System.Text.Json;
using System.Text.Json.Serialization;
using Tsg.Application;
using Tsg.Domain;

namespace Tsg.Infrastructure;

/// <summary>Один экземпляр приложения владеет файлом до своего закрытия.</summary>
public sealed class JsonRepository : IRepository, IDisposable
{
    private readonly string path;
    private readonly FileStream ownership;
    private bool disposed;
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true, IgnoreReadOnlyProperties = true,
        Converters = { new JsonStringEnumConverter() }
    };
    public string FilePath => path;

    public JsonRepository(string filePath)
    {
        path = Path.GetFullPath(filePath);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            ownership = new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException) { throw new AppException("Файл данных занят другим экземпляром приложения или недоступен."); }
        catch (UnauthorizedAccessException) { throw new AppException("Нет прав на запись в выбранную папку данных."); }
    }

    public StoreState Load()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!File.Exists(path)) throw new AppException("Файл данных не найден.");
        try
        {
            var state = JsonSerializer.Deserialize<StoreState>(File.ReadAllText(path), Options)
                ?? throw new AppException("Файл данных пуст или повреждён.");
            Validate(state);
            return state;
        }
        catch (JsonException) { throw new AppException("Файл данных повреждён. Сохраните его и восстановите резервную копию state.json.bak."); }
        catch (IOException) { throw new AppException("Не удалось прочитать файл данных."); }
        catch (UnauthorizedAccessException) { throw new AppException("Нет прав на чтение файла данных."); }
    }

    public void Save(StoreState state)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        Validate(state);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(state, Options);
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                file.Write(bytes);
                file.Flush(flushToDisk: true);
            }
            if (File.Exists(path)) File.Copy(path, path + ".bak", overwrite: true);
            File.Move(temporary, path, overwrite: true);
        }
        catch (IOException) { throw new AppException("Не удалось сохранить данные. Предыдущая версия файла остаётся доступной."); }
        catch (UnauthorizedAccessException) { throw new AppException("Нет прав на сохранение данных в выбранной папке."); }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    public void Initialize(Func<StoreState> seed)
    {
        if (File.Exists(path)) { Load(); return; }
        Save(seed());
    }

    private static void Validate(StoreState state)
    {
        if (state.SchemaVersion != 1) throw new AppException("Версия структуры данных не поддерживается.");
        if (state.Users is null || state.Accounts is null || state.Services is null || state.Tariffs is null ||
            state.Meters is null || state.Readings is null || state.Invoices is null || state.Payments is null)
            throw new AppException("В файле данных отсутствуют обязательные разделы.");
        if (state.Users.Count == 0 || state.Services.Count == 0 ||
            !state.Users.Any(u => u.Active && u.Role == UserRole.Administrator))
            throw new AppException("В данных отсутствуют услуги или активный администратор.");
        EnsureUnique(state.Users.Select(u => u.Id)); EnsureUnique(state.Accounts.Select(a => a.Id));
        EnsureUnique(state.Services.Select(s => s.Id)); EnsureUnique(state.Tariffs.Select(t => t.Id));
        EnsureUnique(state.Meters.Select(m => m.Id)); EnsureUnique(state.Readings.Select(r => r.Id));
        EnsureUnique(state.Invoices.Select(i => i.Id)); EnsureUnique(state.Payments.Select(p => p.Id));
        if (state.Users.Any(u => string.IsNullOrWhiteSpace(u.Login) || string.IsNullOrWhiteSpace(u.PasswordHash) || !Enum.IsDefined(u.Role)) ||
            state.Users.GroupBy(u => u.Login, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1) ||
            state.Accounts.GroupBy(a => a.Number, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1) ||
            state.Readings.GroupBy(r => (r.MeterId, r.Period)).Any(g => g.Count() > 1) ||
            state.Invoices.GroupBy(i => (i.AccountId, i.Period)).Any(g => g.Count() > 1) ||
            state.Payments.GroupBy(p => p.TransactionId).Any(g => g.Count() > 1) ||
            state.Meters.GroupBy(m => (m.AccountId, m.ServiceId)).Any(g => g.Count() > 1))
            throw new AppException("В данных есть некорректные или повторяющиеся записи.");
        var userIds = state.Users.Where(u => u.Role == UserRole.Resident).Select(u => u.Id).ToHashSet();
        var accountIds = state.Accounts.Select(a => a.Id).ToHashSet();
        var serviceIds = state.Services.Select(s => s.Id).ToHashSet();
        var meterIds = state.Meters.Select(m => m.Id).ToHashSet();
        var invoiceIds = state.Invoices.Select(i => i.Id).ToHashSet();
        if (state.Accounts.Any(a => !userIds.Contains(a.ResidentId) || a.Area <= 0) ||
            state.Services.Any(s => !Enum.IsDefined(s.Mode)) ||
            state.Tariffs.Any(t => !serviceIds.Contains(t.ServiceId) || t.Price < 0 || t.EffectiveTo < t.EffectiveFrom) ||
            state.Meters.Any(m => !accountIds.Contains(m.AccountId) || !serviceIds.Contains(m.ServiceId)) ||
            state.Readings.Any(r => !meterIds.Contains(r.MeterId) || r.Value < 0) ||
            state.Invoices.Any(i => !accountIds.Contains(i.AccountId) || i.Lines is null || i.Lines.Count == 0 ||
                i.Lines.Any(l => l.Quantity < 0 || l.Price < 0 || l.Amount != BillingCalculator.Money(l.Quantity * l.Price))) ||
            state.Payments.Any(p => !invoiceIds.Contains(p.InvoiceId) || p.Amount <= 0 || !Enum.IsDefined(p.Status)))
            throw new AppException("В данных нарушены связи или допустимые значения.");
        foreach (var r in state.Readings) BillingPeriod.Parse(r.Period);
        foreach (var i in state.Invoices) BillingPeriod.Parse(i.Period);
    }

    private static void EnsureUnique(IEnumerable<Guid> ids)
    {
        var list = ids.ToList();
        if (list.Contains(Guid.Empty) || list.Distinct().Count() != list.Count)
            throw new AppException("Идентификаторы объектов повреждены или повторяются.");
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        ownership.Dispose();
    }
}
