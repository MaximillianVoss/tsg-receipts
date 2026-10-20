using System.Text;
using System.Text.Json;
using Tsg.Application;
using Tsg.Domain;
using Tsg.Infrastructure;

namespace Tsg.Tests;

[TestClass]
public sealed class InfrastructureTests
{
    private string directory = "";
    [TestInitialize]
    public void Initialize() { directory = Path.Combine(Path.GetTempPath(), "TsgTests", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory); }
    [TestCleanup]
    public void Cleanup()
    {
        var allowed = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "TsgTests")) + Path.DirectorySeparatorChar;
        if (Path.GetFullPath(directory).StartsWith(allowed, StringComparison.OrdinalIgnoreCase) && Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }

    [TestMethod]
    public void Pbkdf2UsesRandomSaltAndVerifiesOnlyCorrectPassword()
    {
        var hasher = new Pbkdf2PasswordHasher(); var first = hasher.Hash("Demo123!"); var second = hasher.Hash("Demo123!");
        Assert.AreNotEqual(first, second); Assert.IsTrue(hasher.Verify("Demo123!", first)); Assert.IsFalse(hasher.Verify("bad", first));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("plaintext")]
    [DataRow("pbkdf2-sha256:abc:bad:bad")]
    [DataRow("pbkdf2-sha256:999999999:bad:bad")]
    public void MalformedHashIsRejected(string hash) => Assert.IsFalse(new Pbkdf2PasswordHasher().Verify("Demo123!", hash));

    [TestMethod]
    public void JsonSaveReopenPreservesInvoiceAndPayment()
    {
        var path = Path.Combine(directory, "state.json"); var hasher = new Pbkdf2PasswordHasher(); Guid invoiceId;
        using (var repository = new JsonRepository(path))
        {
            repository.Initialize(() => DemoData.Create(hasher));
            var auth = new AuthenticationService(repository, hasher); var service = new HousingService(repository, hasher, new RecordingExporter());
            var accountId = repository.Load().Accounts.Single(a => a.Number == "1001").Id;
            var invoice = service.GenerateInvoice(auth.Login("accountant", "Demo123!"), accountId, "2026-10"); invoiceId = invoice.Id;
            service.ConfirmDemoPayment(auth.Login("resident", "Demo123!"), invoiceId, 1000, "json-payment");
        }
        using var reopened = new JsonRepository(path);
        var newAuth = new AuthenticationService(reopened, hasher); var newService = new HousingService(reopened, hasher, new RecordingExporter());
        Assert.AreEqual(2200m, newService.GetReceipt(newAuth.Login("resident", "Demo123!"), invoiceId).Remaining);
        Assert.IsTrue(File.Exists(path + ".bak")); Assert.HasCount(1, reopened.Load().Payments);
    }

    [TestMethod]
    public void BackupContainsPreviousValidVersion()
    {
        var path = Path.Combine(directory, "state.json"); using var repository = new JsonRepository(path);
        repository.Initialize(() => DemoData.Create(new TestHasher())); var first = File.ReadAllText(path);
        var state = repository.Load(); state.Accounts[0] = state.Accounts[0] with { Address = "Изменённый адрес" }; repository.Save(state);
        Assert.AreEqual(first, File.ReadAllText(path + ".bak")); Assert.AreNotEqual(first, File.ReadAllText(path));
        Assert.IsEmpty(Directory.GetFiles(directory, "*.tmp"));
    }

    [TestMethod]
    public void CorruptJsonIsNotSilentlyReplacedByDemo()
    {
        var path = Path.Combine(directory, "state.json"); File.WriteAllText(path, "corrupt-json");
        using var repository = new JsonRepository(path);
        Assert.ThrowsExactly<AppException>(() => repository.Initialize(() => DemoData.Create(new TestHasher())));
        Assert.AreEqual("corrupt-json", File.ReadAllText(path));
    }

    [TestMethod]
    public void SecondInstanceCannotOpenSameDataFile()
    {
        var path = Path.Combine(directory, "state.json"); using var repository = new JsonRepository(path);
        Assert.ThrowsExactly<AppException>(() => { using var other = new JsonRepository(path); });
    }

    [TestMethod]
    public void InvalidReferencesCannotOverwriteExistingData()
    {
        var path = Path.Combine(directory, "state.json"); using var repository = new JsonRepository(path);
        repository.Initialize(() => DemoData.Create(new TestHasher())); var original = File.ReadAllText(path);
        var state = repository.Load(); state.Accounts[0] = state.Accounts[0] with { ResidentId = Guid.NewGuid() };
        Assert.ThrowsExactly<AppException>(() => repository.Save(state)); Assert.AreEqual(original, File.ReadAllText(path));
    }

    [TestMethod]
    public void UnsupportedSchemaIsRejected()
    {
        var state = DemoData.Create(new TestHasher()); state.SchemaVersion = 2;
        var path = Path.Combine(directory, "state.json"); File.WriteAllText(path, JsonSerializer.Serialize(state));
        using var repository = new JsonRepository(path); Assert.ThrowsExactly<AppException>(() => repository.Load());
    }

    [TestMethod]
    public void PdfContainsFontUnicodeMappingAndValidTrailer()
    {
        var f = new Fixture(); var invoice = f.Generate(); var path = Path.Combine(directory, "receipt.pdf");
        new PdfReceiptExporter().Export(f.Housing.GetReceipt(f.Resident, invoice.Id), path);
        var bytes = File.ReadAllBytes(path); var ascii = Encoding.ASCII.GetString(bytes);
        Assert.StartsWith("%PDF-1.4", ascii); Assert.Contains("/ToUnicode", ascii); Assert.Contains("/FontFile2", ascii);
        Assert.EndsWith("%%EOF\n", ascii); Assert.IsGreaterThan(10000, bytes.Length);
    }

    [TestMethod]
    public void MissingFontGivesUnderstandableError()
    {
        var f = new Fixture(); var invoice = f.Generate();
        Assert.ThrowsExactly<AppException>(() => new PdfReceiptExporter(Path.Combine(directory, "missing.ttf"))
            .Export(f.Housing.GetReceipt(f.Resident, invoice.Id), Path.Combine(directory, "receipt.pdf")));
    }
}
