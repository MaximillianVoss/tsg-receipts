using System.Globalization;
using Tsg.Domain;

namespace Tsg.Tests;

[TestClass]
public sealed class CalculationTests
{
    [TestMethod]
    [DataRow("50", "30", "1500")]
    [DataRow("10", "45", "450")]
    [DataRow("100", "6", "600")]
    [DataRow("1", "150", "150")]
    [DataRow("10.01", "45.50", "455.46")]
    [DataRow("0", "45", "0")]
    [DataRow("50", "0", "0")]
    public void CalculatesAmountAndRoundsHalfAwayFromZero(string quantity, string price, string expected)
    {
        var service = new UtilityService { Name = "Тестовая услуга", Unit = "ед." };
        var actual = BillingCalculator.Calculate(service, Parse(quantity), Parse(price));
        Assert.AreEqual(Parse(expected), actual.Amount);
    }

    [TestMethod]
    [DataRow(-1, 10)]
    [DataRow(10, -1)]
    public void RejectsNegativeCalculationInputs(int quantity, int price) =>
        Assert.ThrowsExactly<AppException>(() => BillingCalculator.Calculate(new UtilityService(), quantity, price));

    [TestMethod]
    public void OverflowHasUnderstandableError() =>
        Assert.ThrowsExactly<AppException>(() => BillingCalculator.Calculate(new UtilityService(), decimal.MaxValue, 2m));

    [TestMethod]
    [DataRow(100, 110, 10)]
    [DataRow(110, 110, 0)]
    public void ConsumptionIsDifference(int previous, int current, int expected) =>
        Assert.AreEqual((decimal)expected, BillingCalculator.Consumption(previous, current));

    [TestMethod]
    [DataRow(-1, 110)]
    [DataRow(110, 100)]
    public void RejectsInvalidConsumption(int previous, int current) =>
        Assert.ThrowsExactly<AppException>(() => BillingCalculator.Consumption(previous, current));

    [TestMethod]
    [DataRow("2026-13")]
    [DataRow("2026-00")]
    [DataRow("2026-1")]
    [DataRow("26-10")]
    [DataRow("1999-12")]
    [DataRow("2100-01")]
    [DataRow("")]
    public void InvalidPeriodIsRejected(string period) => Assert.ThrowsExactly<AppException>(() => BillingPeriod.Parse(period));

    [TestMethod]
    public void PeriodRoundTrip() => Assert.AreEqual("2026-10", BillingPeriod.Format(BillingPeriod.Parse("2026-10")));
    private static decimal Parse(string value) => decimal.Parse(value, CultureInfo.InvariantCulture);
}
