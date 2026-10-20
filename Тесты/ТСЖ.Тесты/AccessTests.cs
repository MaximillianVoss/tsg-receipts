using Tsg.Domain;

namespace Tsg.Tests;

[TestClass]
public sealed class AccessTests
{
    [TestMethod]
    [DataRow("admin", UserRole.Administrator)]
    [DataRow("accountant", UserRole.Accountant)]
    [DataRow("resident", UserRole.Resident)]
    public void CorrectCredentialsReturnActualRole(string login, UserRole role) =>
        Assert.AreEqual(role, new Fixture().Auth.Login(login, "Demo123!").Role);

    [TestMethod]
    [DataRow("resident", "bad")]
    [DataRow("missing", "Demo123!")]
    [DataRow("", "")]
    public void IncorrectCredentialsAreRejected(string login, string password) =>
        Assert.ThrowsExactly<AppException>(() => new Fixture().Auth.Login(login, password));

    [TestMethod]
    public void LoginIgnoresCaseAndOuterSpaces() => Assert.AreEqual(UserRole.Accountant, new Fixture().Auth.Login(" ACCOUNTANT ", "Demo123!").Role);

    [TestMethod]
    public void ResidentCannotGenerateInvoicesSetTariffsManageAccountsOrGetReport()
    {
        var f = new Fixture();
        Assert.ThrowsExactly<AppException>(() => f.Housing.GenerateInvoice(f.Resident, f.AccountId, "2026-10"));
        Assert.ThrowsExactly<AppException>(() => f.Housing.SetTariff(f.Resident, f.Meter().ServiceId, "2026-11", 50));
        Assert.ThrowsExactly<AppException>(() => f.Housing.SetAccountActive(f.Resident, f.AccountId, false));
        Assert.ThrowsExactly<AppException>(() => f.Housing.GetReport(f.Resident, "2026-10"));
    }

    [TestMethod]
    public void AccountantCannotManageUsers()
    {
        var f = new Fixture();
        Assert.ThrowsExactly<AppException>(() => f.Housing.GetUsers(f.Accountant));
        Assert.ThrowsExactly<AppException>(() => f.Housing.AddUser(f.Accountant, "third", "Жилец", "Demo123!", UserRole.Resident));
    }

    [TestMethod]
    public void ResidentCannotReadExportPayOrSubmitReadingsForOtherAccount()
    {
        var f = new Fixture(); var other = f.Housing.GenerateInvoice(f.Accountant, f.SecondId, "2026-10");
        Assert.HasCount(1, f.Housing.GetSnapshot(f.Resident).Accounts);
        Assert.IsEmpty(f.Housing.GetSnapshot(f.Resident).Invoices);
        Assert.ThrowsExactly<AppException>(() => f.Housing.GetReceipt(f.Resident, other.Id));
        Assert.ThrowsExactly<AppException>(() => f.Housing.ExportReceipt(f.Resident, other.Id, "other.pdf"));
        Assert.ThrowsExactly<AppException>(() => f.Housing.ConfirmDemoPayment(f.Resident, other.Id, 1m, "forbidden"));
        Assert.ThrowsExactly<AppException>(() => f.Housing.SubmitReading(f.Resident, f.Meter("В-1002").Id, "2026-11", 60));
        Assert.IsNull(f.Exporter.LastReceipt);
    }

    [TestMethod]
    public void DisabledUserCannotLoginOrContinueOldSession()
    {
        var f = new Fixture(); var session = f.Resident;
        f.Housing.SetAccountActive(f.Accountant, f.AccountId, false);
        f.Housing.SetUserActive(f.Admin, session.UserId, false);
        Assert.ThrowsExactly<AppException>(() => f.Auth.Login("resident", "Demo123!"));
        Assert.ThrowsExactly<AppException>(() => f.Housing.GetSnapshot(session));
    }

    [TestMethod]
    public void CannotDisableOwnAdminOrResidentWithActiveAccount()
    {
        var f = new Fixture(); var admin = f.Admin;
        Assert.ThrowsExactly<AppException>(() => f.Housing.SetUserActive(admin, admin.UserId, false));
        Assert.ThrowsExactly<AppException>(() => f.Housing.SetUserActive(admin, f.Resident.UserId, false));
    }

    [TestMethod]
    public void AdministratorCreatesUserWithoutExposingHash()
    {
        var f = new Fixture(); var user = f.Housing.AddUser(f.Admin, "third", "Сидоров", "NewPass123!", UserRole.Resident);
        Assert.AreEqual(user.Id, f.Auth.Login("third", "NewPass123!").UserId);
        Assert.IsNull(user.GetType().GetProperty("PasswordHash"));
    }

    [TestMethod]
    [DataRow("resident", "Demo123!")]
    [DataRow("bad login", "Demo123!")]
    [DataRow("third", "short")]
    public void InvalidNewUserDoesNotSave(string login, string password)
    {
        var f = new Fixture(); Assert.ThrowsExactly<AppException>(() => f.Housing.AddUser(f.Admin, login, "Сидоров", password, UserRole.Resident));
        Assert.HasCount(4, f.Repository.Load().Users);
    }
}
