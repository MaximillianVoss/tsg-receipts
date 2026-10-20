namespace Tsg.Domain;

public enum UserRole { Administrator, Accountant, Resident }
public enum CalculationMode { Area, Fixed, Meter }
public enum PaymentStatus { Pending, Confirmed }
public enum InvoiceStatus { Unpaid, PartiallyPaid, Paid, CarriedForward }

public sealed class AppException(string message) : Exception(message);

public sealed record User
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Login { get; init; } = "";
    public string FullName { get; init; } = "";
    public string PasswordHash { get; init; } = "";
    public UserRole Role { get; init; }
    public bool Active { get; init; } = true;
}

public sealed record PersonalAccount
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Number { get; init; } = "";
    public string Address { get; init; } = "";
    public decimal Area { get; init; }
    public decimal OpeningBalance { get; init; }
    public Guid ResidentId { get; init; }
    public bool Active { get; init; } = true;
}

public sealed record UtilityService
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = "";
    public string Unit { get; init; } = "";
    public CalculationMode Mode { get; init; }
}

public sealed record Tariff
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid ServiceId { get; init; }
    public decimal Price { get; init; }
    public DateOnly EffectiveFrom { get; init; }
    public DateOnly? EffectiveTo { get; init; }
    public bool Applies(DateOnly date) => date >= EffectiveFrom && (EffectiveTo is null || date <= EffectiveTo);
}

public sealed record Meter
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid AccountId { get; init; }
    public Guid ServiceId { get; init; }
    public string SerialNumber { get; init; } = "";
}

public sealed record MeterReading
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid MeterId { get; init; }
    public string Period { get; init; } = "";
    public decimal Value { get; init; }
}

public sealed record ChargeLine(Guid ServiceId, string ServiceName, string Unit, decimal Quantity, decimal Price, decimal Amount);

public sealed record Invoice
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid AccountId { get; init; }
    public string Number { get; init; } = "";
    public string Period { get; init; } = "";
    public DateTime CreatedAt { get; init; }
    public DateOnly DueDate { get; init; }
    // Снимок реквизитов и цены: последующие правки справочников не меняют документ.
    public string AccountNumber { get; init; } = "";
    public string Address { get; init; } = "";
    public string ResidentName { get; init; } = "";
    public decimal Area { get; init; }
    public decimal BeginningBalance { get; init; }
    public List<ChargeLine> Lines { get; init; } = [];
    public decimal TotalCharges => Lines.Sum(line => line.Amount);
    public decimal ClosingBalance => BeginningBalance + TotalCharges;
    public decimal AmountDue => Math.Max(0m, ClosingBalance);
}

public sealed record Payment
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid InvoiceId { get; init; }
    public decimal Amount { get; init; }
    public DateTime ConfirmedAt { get; init; }
    public string TransactionId { get; init; } = "";
    public PaymentStatus Status { get; init; }
}

public sealed class StoreState
{
    public int SchemaVersion { get; set; } = 1;
    public List<User> Users { get; set; } = [];
    public List<PersonalAccount> Accounts { get; set; } = [];
    public List<UtilityService> Services { get; set; } = [];
    public List<Tariff> Tariffs { get; set; } = [];
    public List<Meter> Meters { get; set; } = [];
    public List<MeterReading> Readings { get; set; } = [];
    public List<Invoice> Invoices { get; set; } = [];
    public List<Payment> Payments { get; set; } = [];
}
