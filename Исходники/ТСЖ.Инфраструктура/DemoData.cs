using Tsg.Application;
using Tsg.Domain;

namespace Tsg.Infrastructure;

public static class DemoData
{
    public const string Password = "Demo123!";
    public const string Period = "2026-10";
    public static StoreState Create(IPasswordHasher hasher)
    {
        var state = new StoreState();
        var admin = new User { Login = "admin", FullName = "Администратор ТСЖ", Role = UserRole.Administrator, PasswordHash = hasher.Hash(Password) };
        var accountant = new User { Login = "accountant", FullName = "Бухгалтер ТСЖ", Role = UserRole.Accountant, PasswordHash = hasher.Hash(Password) };
        var resident = new User { Login = "resident", FullName = "Иванов Иван Иванович", Role = UserRole.Resident, PasswordHash = hasher.Hash(Password) };
        var resident2 = new User { Login = "resident2", FullName = "Петрова Анна Сергеевна", Role = UserRole.Resident, PasswordHash = hasher.Hash(Password) };
        state.Users.AddRange([admin, accountant, resident, resident2]);
        var maintenance = new UtilityService { Name = "Содержание жилья", Unit = "м²", Mode = CalculationMode.Area };
        var water = new UtilityService { Name = "Холодная вода", Unit = "м³", Mode = CalculationMode.Meter };
        var electricity = new UtilityService { Name = "Электроэнергия", Unit = "кВт·ч", Mode = CalculationMode.Meter };
        var waste = new UtilityService { Name = "Вывоз мусора", Unit = "услуга", Mode = CalculationMode.Fixed };
        state.Services.AddRange([maintenance, water, electricity, waste]);
        foreach (var pair in new[] { (maintenance, 30m), (water, 45m), (electricity, 6m), (waste, 150m) })
            state.Tariffs.Add(new Tariff { ServiceId = pair.Item1.Id, Price = pair.Item2, EffectiveFrom = new DateOnly(2026, 1, 1) });
        var first = new PersonalAccount { Number = "1001", Address = "ул. Учебная, д. 1, кв. 1", Area = 50m, ResidentId = resident.Id, OpeningBalance = 500m };
        var second = new PersonalAccount { Number = "1002", Address = "ул. Учебная, д. 1, кв. 2", Area = 40m, ResidentId = resident2.Id, OpeningBalance = -3000m };
        state.Accounts.AddRange([first, second]);
        AddMeter(state, first, water, "В-1001", 100m, 110m);
        AddMeter(state, first, electricity, "Э-1001", 1000m, 1100m);
        AddMeter(state, second, water, "В-1002", 50m, 54m);
        AddMeter(state, second, electricity, "Э-1002", 500m, 580m);
        return state;
    }

    private static void AddMeter(StoreState state, PersonalAccount account, UtilityService service, string serial, decimal previous, decimal current)
    {
        var meter = new Meter { AccountId = account.Id, ServiceId = service.Id, SerialNumber = serial };
        state.Meters.Add(meter);
        state.Readings.AddRange([new MeterReading { MeterId = meter.Id, Period = "2026-09", Value = previous },
            new MeterReading { MeterId = meter.Id, Period = Period, Value = current }]);
    }
}
