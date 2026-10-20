using Tsg.Domain;

namespace Tsg.Tests;

[TestClass]
public sealed class HousingTests
{
    [TestMethod]
    public void DemoInvoiceHasFourCorrectLinesAndOpeningDebt()
    {
        var f = new Fixture(); var invoice = f.Generate();
        CollectionAssert.AreEqual(new[] { 1500m, 450m, 600m, 150m }, invoice.Lines.Select(l => l.Amount).ToArray());
        Assert.AreEqual(2700m, invoice.TotalCharges); Assert.AreEqual(500m, invoice.BeginningBalance);
        Assert.AreEqual(3200m, invoice.AmountDue); Assert.AreEqual(new DateOnly(2026, 11, 10), invoice.DueDate);
    }

    [TestMethod]
    public void CreditIsPreservedAndAmountDueCannotBeNegative()
    {
        var f = new Fixture(); var invoice = f.Housing.GenerateInvoice(f.Accountant, f.SecondId, "2026-10");
        Assert.AreEqual(2010m, invoice.TotalCharges); Assert.AreEqual(-990m, invoice.ClosingBalance);
        Assert.AreEqual(0m, invoice.AmountDue);
        Assert.AreEqual(InvoiceStatus.Paid, f.Housing.GetReceipt(f.Accountant, invoice.Id).Status);
    }

    [TestMethod]
    public void DuplicateInvoiceDoesNotSavePartialData()
    {
        var f = new Fixture(); f.Generate(); var saves = f.Repository.SaveCount;
        Assert.ThrowsExactly<AppException>(() => f.Generate());
        Assert.HasCount(1, f.Repository.Load().Invoices); Assert.AreEqual(saves, f.Repository.SaveCount);
    }

    [TestMethod]
    public void MissingCurrentReadingPreventsInvoice()
    {
        var f = new Fixture(); var state = f.Repository.Load();
        state.Readings.RemoveAll(r => r.MeterId == f.Meter().Id && r.Period == "2026-10"); f.Repository.Save(state);
        Assert.ThrowsExactly<AppException>(() => f.Generate()); Assert.IsEmpty(f.Repository.Load().Invoices);
    }

    [TestMethod]
    public void MissingPreviousReadingPreventsInvoice()
    {
        var f = new Fixture(); var state = f.Repository.Load();
        state.Readings.RemoveAll(r => r.MeterId == f.Meter().Id && r.Period == "2026-09"); f.Repository.Save(state);
        Assert.ThrowsExactly<AppException>(() => f.Generate()); Assert.IsEmpty(f.Repository.Load().Invoices);
    }

    [TestMethod]
    public void MissingTariffPreventsInvoice()
    {
        var f = new Fixture(); var state = f.Repository.Load(); state.Tariffs.Clear(); f.Repository.Save(state);
        Assert.ThrowsExactly<AppException>(() => f.Generate()); Assert.IsEmpty(f.Repository.Load().Invoices);
    }

    [TestMethod]
    public void OverlappingTariffsAreRejectedAtCalculation()
    {
        var f = new Fixture(); var state = f.Repository.Load(); var t = state.Tariffs[0];
        state.Tariffs.Add(t with { Id = Guid.NewGuid() }); f.Repository.Save(state);
        Assert.ThrowsExactly<AppException>(() => f.Generate());
    }

    [TestMethod]
    public void NewMonthTariffClosesOldVersionAndInvoiceKeepsSnapshot()
    {
        var f = new Fixture(); var old = f.Generate(); var water = f.Meter().ServiceId;
        f.Housing.SetTariff(f.Accountant, water, "2026-11", 50m); f.NovemberReadings();
        var next = f.Housing.GenerateInvoice(f.Accountant, f.AccountId, "2026-11");
        Assert.AreEqual(45m, f.Housing.GetReceipt(f.Resident, old.Id).Invoice.Lines.Single(l => l.ServiceId == water).Price);
        Assert.AreEqual(50m, next.Lines.Single(l => l.ServiceId == water).Price);
        Assert.AreEqual(5950m, next.AmountDue);
        Assert.AreEqual(new DateOnly(2026, 10, 31), f.Repository.Load().Tariffs.Single(t => t.ServiceId == water && t.Price == 45m).EffectiveTo);
    }

    [TestMethod]
    public void EditingTariffOfSameMonthDoesNotModifyIssuedInvoice()
    {
        var f = new Fixture(); var invoice = f.Generate(); var water = f.Meter().ServiceId;
        f.Housing.SetTariff(f.Accountant, water, "2026-01", 99m);
        Assert.AreEqual(45m, f.Housing.GetReceipt(f.Resident, invoice.Id).Invoice.Lines.Single(l => l.ServiceId == water).Price);
        Assert.HasCount(1, f.Repository.Load().Tariffs.Where(t => t.ServiceId == water).ToList());
    }

    [TestMethod]
    public void TariffInsertedBeforeAndBetweenVersionsLeavesNoOverlap()
    {
        var f = new Fixture(); var water = f.Meter().ServiceId;
        f.Housing.SetTariff(f.Accountant, water, "2025-12", 40m);
        f.Housing.SetTariff(f.Accountant, water, "2026-03", 60m);
        f.Housing.SetTariff(f.Accountant, water, "2026-02", 50m);
        var tariffs = f.Repository.Load().Tariffs.Where(t => t.ServiceId == water).ToList();
        Assert.AreEqual(40m, tariffs.Single(t => t.Applies(new DateOnly(2025, 12, 1))).Price);
        Assert.AreEqual(45m, tariffs.Single(t => t.Applies(new DateOnly(2026, 1, 1))).Price);
        Assert.AreEqual(50m, tariffs.Single(t => t.Applies(new DateOnly(2026, 2, 1))).Price);
        Assert.AreEqual(60m, tariffs.Single(t => t.Applies(new DateOnly(2026, 3, 1))).Price);
    }

    [TestMethod]
    [DataRow(-1)]
    [DataRow(1000001)]
    public void InvalidTariffIsRejected(int price)
    {
        var f = new Fixture(); Assert.ThrowsExactly<AppException>(() => f.Housing.SetTariff(f.Accountant, f.Meter().ServiceId, "2026-11", price));
    }

    [TestMethod]
    public void ReadingCanBeSameButCannotDecreaseOrRepeat()
    {
        var f = new Fixture(); var meter = f.Meter();
        Assert.ThrowsExactly<AppException>(() => f.Housing.SubmitReading(f.Resident, meter.Id, "2026-11", 109m));
        f.Housing.SubmitReading(f.Resident, meter.Id, "2026-11", 110m);
        Assert.ThrowsExactly<AppException>(() => f.Housing.SubmitReading(f.Resident, meter.Id, "2026-11", 120m));
        Assert.HasCount(1, f.Repository.Load().Readings.Where(r => r.MeterId == meter.Id && r.Period == "2026-11").ToList());
    }

    [TestMethod]
    public void EarlierReadingCannotExceedAlreadyEnteredNextMonth()
    {
        var f = new Fixture(); var meter = f.Meter();
        f.Housing.SubmitReading(f.Resident, meter.Id, "2026-12", 130m);
        Assert.ThrowsExactly<AppException>(() => f.Housing.SubmitReading(f.Resident, meter.Id, "2026-11", 131m));
        f.Housing.SubmitReading(f.Resident, meter.Id, "2026-11", 120m);
    }

    [TestMethod]
    public void BilledReadingsCannotBeChanged()
    {
        var f = new Fixture(); f.Generate();
        Assert.ThrowsExactly<AppException>(() => f.Housing.SubmitReading(f.Resident, f.Meter().Id, "2026-10", 115m));
    }

    [TestMethod]
    public void MonthsCannotBeSkippedAfterFirstInvoice()
    {
        var f = new Fixture(); f.Generate(); f.NovemberReadings();
        f.Housing.SubmitReading(f.Resident, f.Meter().Id, "2026-12", 130m);
        f.Housing.SubmitReading(f.Resident, f.Meter("Э-1001").Id, "2026-12", 1300m);
        Assert.ThrowsExactly<AppException>(() => f.Housing.GenerateInvoice(f.Accountant, f.AccountId, "2026-12"));
    }

    [TestMethod]
    public void EarlierInvoiceCannotBeCreatedAfterLaterOne()
    {
        var f = new Fixture(); f.Generate();
        Assert.ThrowsExactly<AppException>(() => f.Housing.GenerateInvoice(f.Accountant, f.AccountId, "2026-09"));
    }

    [TestMethod]
    public void PartialAndFullPaymentsUpdateRemainingAndStatus()
    {
        var f = new Fixture(); var invoice = f.Generate();
        f.Housing.ConfirmDemoPayment(f.Resident, invoice.Id, 1000m, "first");
        var partial = f.Housing.GetReceipt(f.Resident, invoice.Id);
        Assert.AreEqual(2200m, partial.Remaining); Assert.AreEqual(InvoiceStatus.PartiallyPaid, partial.Status);
        f.Housing.ConfirmDemoPayment(f.Resident, invoice.Id, 2200m, "second");
        Assert.AreEqual(InvoiceStatus.Paid, f.Housing.GetReceipt(f.Resident, invoice.Id).Status);
        Assert.AreEqual(0m, f.Housing.GetSnapshot(f.Resident).Accounts.Single().Balance);
    }

    [TestMethod]
    [DataRow("0")]
    [DataRow("-1")]
    [DataRow("3200.01")]
    [DataRow("1.001")]
    public void InvalidPaymentsDoNotChangeState(string amount)
    {
        var f = new Fixture(); var invoice = f.Generate();
        Assert.ThrowsExactly<AppException>(() => f.Housing.ConfirmDemoPayment(f.Resident, invoice.Id,
            decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture), "bad"));
        Assert.IsEmpty(f.Repository.Load().Payments);
    }

    [TestMethod]
    public void DuplicateTransactionDoesNotChargeTwice()
    {
        var f = new Fixture(); var invoice = f.Generate(); f.Housing.ConfirmDemoPayment(f.Resident, invoice.Id, 500m, "same");
        Assert.ThrowsExactly<AppException>(() => f.Housing.ConfirmDemoPayment(f.Resident, invoice.Id, 500m, "same"));
        Assert.HasCount(1, f.Repository.Load().Payments); Assert.AreEqual(2700m, f.Housing.GetReceipt(f.Resident, invoice.Id).Remaining);
    }

    [TestMethod]
    public void CarryingDebtDoesNotCountItTwiceAndOnlyLatestInvoiceCanBePaid()
    {
        var f = new Fixture(); var old = f.Generate();
        f.Housing.ConfirmDemoPayment(f.Resident, old.Id, 1000m, "old"); f.NovemberReadings();
        var next = f.Housing.GenerateInvoice(f.Accountant, f.AccountId, "2026-11");
        Assert.AreEqual(4900m, next.AmountDue);
        Assert.AreEqual(InvoiceStatus.CarriedForward, f.Housing.GetReceipt(f.Resident, old.Id).Status);
        Assert.AreEqual(0m, f.Housing.GetReceipt(f.Resident, old.Id).Remaining);
        Assert.ThrowsExactly<AppException>(() => f.Housing.ConfirmDemoPayment(f.Resident, old.Id, 500m, "late"));
        f.Housing.ConfirmDemoPayment(f.Resident, next.Id, 4900m, "latest");
        Assert.AreEqual(0m, f.Housing.GetSnapshot(f.Resident).Accounts.Single().Balance);
    }

    [TestMethod]
    public void PendingPaymentDoesNotAffectBalance()
    {
        var f = new Fixture(); var invoice = f.Generate(); var state = f.Repository.Load();
        state.Payments.Add(new Payment { InvoiceId = invoice.Id, Amount = 1000m, TransactionId = "pending", Status = PaymentStatus.Pending });
        f.Repository.Save(state); Assert.AreEqual(3200m, f.Housing.GetReceipt(f.Resident, invoice.Id).Remaining);
    }

    [TestMethod]
    public void ReportSeparatesBillingMonthFromPaymentDate()
    {
        var f = new Fixture(); var invoice = f.Generate(); f.Housing.GenerateInvoice(f.Accountant, f.SecondId, "2026-10");
        f.Housing.ConfirmDemoPayment(f.Resident, invoice.Id, 1000m, "report");
        var state = f.Repository.Load(); var payment = state.Payments.Single();
        state.Payments[0] = payment with { ConfirmedAt = new DateTime(2026, 11, 5) }; f.Repository.Save(state);
        var october = f.Housing.GetReport(f.Accountant, "2026-10"); var november = f.Housing.GetReport(f.Accountant, "2026-11");
        Assert.AreEqual(4710m, october.Charges); Assert.AreEqual(0m, october.Payments); Assert.AreEqual(1000m, november.Payments);
        Assert.AreEqual(2200m, october.TotalDebt); Assert.AreEqual(990m, october.TotalCredit);
    }

    [TestMethod]
    public void FailedSaveLeavesPreviousDataIntact()
    {
        var f = new Fixture(); f.Repository.FailSave = true;
        Assert.ThrowsExactly<AppException>(() => f.Generate()); Assert.IsEmpty(f.Repository.Load().Invoices);
    }

    [TestMethod]
    public void AccountEditDoesNotChangeIssuedReceipt()
    {
        var f = new Fixture(); var invoice = f.Generate();
        f.Housing.UpdateAccount(f.Accountant, f.AccountId, "Новый адрес", 99m, f.Resident.UserId);
        var saved = f.Housing.GetReceipt(f.Resident, invoice.Id).Invoice;
        Assert.AreEqual("ул. Учебная, д. 1, кв. 1", saved.Address); Assert.AreEqual(50m, saved.Area);
    }

    [TestMethod]
    public void InactiveAccountRejectsReadingAndCalculation()
    {
        var f = new Fixture(); f.Housing.SetAccountActive(f.Accountant, f.AccountId, false);
        Assert.ThrowsExactly<AppException>(() => f.Generate());
        Assert.ThrowsExactly<AppException>(() => f.Housing.SubmitReading(f.Resident, f.Meter().Id, "2026-11", 120));
    }

    [TestMethod]
    public void AddsNewAccountWithTwoMetersAndBaselines()
    {
        var f = new Fixture(); var account = f.Housing.AddAccount(f.Accountant, "1003", "Новый дом", 50m,
            f.Resident.UserId, 0m, "2026-09", 100m, 1000m);
        var state = f.Repository.Load(); var meters = state.Meters.Where(m => m.AccountId == account.Id).ToList();
        Assert.HasCount(2, meters); Assert.HasCount(2, state.Readings.Where(r => meters.Any(m => m.Id == r.MeterId)).ToList());
    }

    [TestMethod]
    public void DuplicateAccountAndZeroAreaAreRejected()
    {
        var f = new Fixture();
        Assert.ThrowsExactly<AppException>(() => f.Housing.AddAccount(f.Accountant, "1001", "Адрес", 50, f.Resident.UserId, 0, "2026-09", 0, 0));
        Assert.ThrowsExactly<AppException>(() => f.Housing.AddAccount(f.Accountant, "1003", "Адрес", 0, f.Resident.UserId, 0, "2026-09", 0, 0));
    }

    [TestMethod]
    public void ArchivedAccountCanKeepItsDisabledOwnerDuringAddressEdit()
    {
        var f = new Fixture(); var account = f.Repository.Load().Accounts.Single(a => a.Number == "1002");
        f.Housing.SetAccountActive(f.Accountant, account.Id, false);
        f.Housing.SetUserActive(f.Admin, account.ResidentId, false);
        f.Housing.UpdateAccount(f.Accountant, account.Id, "Архивный адрес", account.Area, account.ResidentId);
        var saved = f.Repository.Load().Accounts.Single(a => a.Id == account.Id);
        Assert.AreEqual(account.ResidentId, saved.ResidentId); Assert.AreEqual("Архивный адрес", saved.Address);
        Assert.ThrowsExactly<AppException>(() => f.Housing.SetAccountActive(f.Accountant, account.Id, true));
    }
}
