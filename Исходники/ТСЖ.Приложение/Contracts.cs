using Tsg.Domain;

namespace Tsg.Application;

public interface IRepository
{
    StoreState Load();
    void Save(StoreState state);
}

public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string hash);
}

public interface IReceiptExporter
{
    void Export(ReceiptData receipt, string path);
}

public sealed record UserSession(Guid UserId, string FullName, UserRole Role);
public sealed record UserInfo(Guid Id, string Login, string FullName, UserRole Role, bool Active);
public sealed record InvoiceInfo(Invoice Invoice, decimal Paid, decimal Remaining, InvoiceStatus Status);
public sealed record AccountInfo(PersonalAccount Account, string ResidentName, decimal Balance);
public sealed record ReceiptData(Invoice Invoice, decimal Paid, decimal Remaining, InvoiceStatus Status);
public sealed record ReportData(string Period, int InvoiceCount, decimal Charges, decimal Payments, decimal TotalDebt, decimal TotalCredit);
public sealed record HousingSnapshot(IReadOnlyList<AccountInfo> Accounts, IReadOnlyList<UtilityService> Services,
    IReadOnlyList<Tariff> Tariffs, IReadOnlyList<Meter> Meters, IReadOnlyList<MeterReading> Readings,
    IReadOnlyList<InvoiceInfo> Invoices, IReadOnlyList<Payment> Payments, IReadOnlyList<UserInfo> Residents);

public static class Access
{
    public static User RequireUser(StoreState state, UserSession session) =>
        state.Users.SingleOrDefault(u => u.Id == session.UserId && u.Active)
        ?? throw new AppException("Сеанс недействителен. Войдите заново.");

    public static void RequireStaff(User user)
    {
        if (user.Role == UserRole.Resident) throw new AppException("Действие доступно бухгалтеру или администратору.");
    }

    public static void RequireAdministrator(User user)
    {
        if (user.Role != UserRole.Administrator) throw new AppException("Действие доступно только администратору.");
    }

    public static void RequireAccount(User user, PersonalAccount account)
    {
        if (user.Role == UserRole.Resident && account.ResidentId != user.Id)
            throw new AppException("Жилец может работать только со своими лицевыми счетами.");
    }
}
