using System.Text.Json;
using Tsg.Application;
using Tsg.Domain;
using Tsg.Infrastructure;

[assembly: DoNotParallelize]

namespace Tsg.Tests;

internal sealed class TestHasher : IPasswordHasher
{
    public string Hash(string password) => "test:" + password;
    public bool Verify(string password, string hash) => hash == Hash(password);
}

internal sealed class MemoryRepository : IRepository
{
    private string data = "";
    public int SaveCount { get; private set; }
    public bool FailSave { get; set; }
    public MemoryRepository(StoreState state) => data = JsonSerializer.Serialize(state);
    public StoreState Load() => JsonSerializer.Deserialize<StoreState>(data)!;
    public void Save(StoreState state)
    {
        if (FailSave) throw new AppException("Имитированная ошибка сохранения.");
        data = JsonSerializer.Serialize(state); SaveCount++;
    }
}

internal sealed class RecordingExporter : IReceiptExporter
{
    public ReceiptData? LastReceipt { get; private set; }
    public string? LastPath { get; private set; }
    public void Export(ReceiptData receipt, string path) { LastReceipt = receipt; LastPath = path; }
}

internal sealed class Fixture
{
    public MemoryRepository Repository { get; } = new(DemoData.Create(new TestHasher()));
    public RecordingExporter Exporter { get; } = new();
    public AuthenticationService Auth { get; }
    public HousingService Housing { get; }
    public UserSession Admin => Auth.Login("admin", DemoData.Password);
    public UserSession Accountant => Auth.Login("accountant", DemoData.Password);
    public UserSession Resident => Auth.Login("resident", DemoData.Password);
    public Guid AccountId => Repository.Load().Accounts.Single(a => a.Number == "1001").Id;
    public Guid SecondId => Repository.Load().Accounts.Single(a => a.Number == "1002").Id;
    public Fixture()
    {
        var hasher = new TestHasher();
        Auth = new AuthenticationService(Repository, hasher);
        Housing = new HousingService(Repository, hasher, Exporter);
    }
    public Invoice Generate() => Housing.GenerateInvoice(Accountant, AccountId, "2026-10");
    public Meter Meter(string serial = "В-1001") => Repository.Load().Meters.Single(m => m.SerialNumber == serial);
    public void NovemberReadings(decimal water = 120m, decimal power = 1200m)
    {
        Housing.SubmitReading(Resident, Meter().Id, "2026-11", water);
        Housing.SubmitReading(Resident, Meter("Э-1001").Id, "2026-11", power);
    }
}
