using System.Globalization;

namespace Tsg.Domain;

public static class BillingPeriod
{
    public static DateOnly Parse(string period)
    {
        if (period.Length != 7 || !DateOnly.TryParseExact(period + "-01", "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            throw new AppException("Период должен иметь формат ГГГГ-ММ, например 2026-10.");
        if (date.Year is < 2000 or > 2099) throw new AppException("Год должен быть от 2000 до 2099.");
        return date;
    }

    public static string Format(DateOnly period) => period.ToString("yyyy-MM", CultureInfo.InvariantCulture);
}

public static class BillingCalculator
{
    public static decimal Money(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    public static ChargeLine Calculate(UtilityService service, decimal quantity, decimal price)
    {
        if (quantity < 0 || price < 0) throw new AppException("Объём и тариф не могут быть отрицательными.");
        try
        {
            return new ChargeLine(service.Id, service.Name, service.Unit, quantity, price, Money(checked(quantity * price)));
        }
        catch (OverflowException) { throw new AppException("Сумма начисления превышает допустимый диапазон."); }
    }

    public static decimal Consumption(decimal previous, decimal current)
    {
        if (previous < 0 || current < previous)
            throw new AppException("Показание не может быть отрицательным или меньше предыдущего.");
        return current - previous;
    }
}
