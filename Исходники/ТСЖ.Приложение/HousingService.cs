using Tsg.Domain;

namespace Tsg.Application;

public sealed class HousingService(IRepository repository, IPasswordHasher hasher, IReceiptExporter exporter)
{
    public HousingSnapshot GetSnapshot(UserSession session)
    {
        var state = repository.Load();
        var user = Access.RequireUser(state, session);
        var accounts = state.Accounts.Where(a => user.Role != UserRole.Resident || a.ResidentId == user.Id).ToList();
        var ids = accounts.Select(a => a.Id).ToHashSet();
        var meters = state.Meters.Where(m => ids.Contains(m.AccountId)).ToList();
        var meterIds = meters.Select(m => m.Id).ToHashSet();
        var invoices = state.Invoices.Where(i => ids.Contains(i.AccountId)).OrderByDescending(i => i.Period)
            .Select(i => Summarize(state, i)).ToList();
        var invoiceIds = invoices.Select(i => i.Invoice.Id).ToHashSet();
        return new HousingSnapshot(accounts.Select(a => new AccountInfo(a,
                state.Users.Single(u => u.Id == a.ResidentId).FullName, Balance(state, a))).ToList(),
            state.Services, state.Tariffs, meters, state.Readings.Where(r => meterIds.Contains(r.MeterId)).ToList(),
            invoices, state.Payments.Where(p => invoiceIds.Contains(p.InvoiceId)).ToList(),
            state.Users.Where(u => u.Role == UserRole.Resident && (user.Role != UserRole.Resident || u.Id == user.Id))
                .Select(ToInfo).ToList());
    }

    public IReadOnlyList<UserInfo> GetUsers(UserSession session)
    {
        var state = repository.Load();
        Access.RequireAdministrator(Access.RequireUser(state, session));
        return state.Users.Select(ToInfo).ToList();
    }

    public UserInfo AddUser(UserSession session, string login, string fullName, string password, UserRole role)
    {
        var state = repository.Load();
        Access.RequireAdministrator(Access.RequireUser(state, session));
        login = Required(login, "Логин", 40);
        fullName = Required(fullName, "ФИО", 120);
        if (!Enum.IsDefined(role)) throw new AppException("Неизвестная роль.");
        if (login.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_' && c != '-'))
            throw new AppException("Логин: латинские буквы, цифры, знак подчёркивания или дефис.");
        if (state.Users.Any(u => string.Equals(u.Login, login, StringComparison.OrdinalIgnoreCase)))
            throw new AppException("Пользователь с таким логином уже существует.");
        if (password.Length is < 8 or > 128) throw new AppException("Пароль должен содержать от 8 до 128 символов.");
        var user = new User { Login = login, FullName = fullName, PasswordHash = hasher.Hash(password), Role = role };
        state.Users.Add(user);
        repository.Save(state);
        return ToInfo(user);
    }

    public void SetUserActive(UserSession session, Guid userId, bool active)
    {
        var state = repository.Load();
        var actor = Access.RequireUser(state, session);
        Access.RequireAdministrator(actor);
        var user = state.Users.SingleOrDefault(u => u.Id == userId) ?? throw new AppException("Пользователь не найден.");
        if (!active && user.Id == actor.Id) throw new AppException("Нельзя отключить собственную учётную запись.");
        if (!active && state.Accounts.Any(a => a.ResidentId == userId && a.Active))
            throw new AppException("Сначала архивируйте активные лицевые счета жильца.");
        state.Users[state.Users.IndexOf(user)] = user with { Active = active };
        repository.Save(state);
    }

    public PersonalAccount AddAccount(UserSession session, string number, string address, decimal area, Guid residentId,
        decimal openingBalance, string baselinePeriod, decimal waterBaseline, decimal electricityBaseline)
    {
        var state = repository.Load();
        Access.RequireStaff(Access.RequireUser(state, session));
        number = Required(number, "Номер счёта", 30);
        address = Required(address, "Адрес", 200);
        CheckArea(area);
        if (Math.Abs(openingBalance) > 1_000_000_000m) throw new AppException("Начальное сальдо превышает допустимое значение.");
        BillingPeriod.Parse(baselinePeriod);
        CheckReading(waterBaseline);
        CheckReading(electricityBaseline);
        if (state.Accounts.Any(a => string.Equals(a.Number, number, StringComparison.OrdinalIgnoreCase)))
            throw new AppException("Лицевой счёт с таким номером уже существует.");
        var resident = state.Users.SingleOrDefault(u => u.Id == residentId && u.Active && u.Role == UserRole.Resident)
            ?? throw new AppException("Выберите активного пользователя с ролью Жилец.");
        var account = new PersonalAccount { Number = number, Address = address, Area = area,
            ResidentId = resident.Id, OpeningBalance = BillingCalculator.Money(openingBalance) };
        state.Accounts.Add(account);
        // Учебный справочник содержит два вида счётчиков: вода и электричество.
        var meteredServices = state.Services.Where(s => s.Mode == CalculationMode.Meter).ToList();
        if (meteredServices.Count != 2) throw new AppException("Нужны две услуги по счётчикам в справочнике.");
        for (var index = 0; index < meteredServices.Count; index++)
        {
            var meter = new Meter { AccountId = account.Id, ServiceId = meteredServices[index].Id,
                SerialNumber = $"{number}-{index + 1}" };
            state.Meters.Add(meter);
            state.Readings.Add(new MeterReading { MeterId = meter.Id, Period = baselinePeriod,
                Value = index == 0 ? waterBaseline : electricityBaseline });
        }
        repository.Save(state);
        return account;
    }

    public void UpdateAccount(UserSession session, Guid accountId, string address, decimal area, Guid residentId)
    {
        var state = repository.Load();
        Access.RequireStaff(Access.RequireUser(state, session));
        var account = FindAccount(state, accountId);
        address = Required(address, "Адрес", 200);
        CheckArea(area);
        if (!state.Users.Any(u => u.Id == residentId && u.Role == UserRole.Resident &&
                (u.Active || residentId == account.ResidentId)))
            throw new AppException("Новый владелец должен быть активным жильцом. Текущего отключённого владельца можно сохранить.");
        state.Accounts[state.Accounts.IndexOf(account)] = account with { Address = address, Area = area, ResidentId = residentId };
        repository.Save(state);
    }

    public void SetAccountActive(UserSession session, Guid accountId, bool active)
    {
        var state = repository.Load();
        Access.RequireStaff(Access.RequireUser(state, session));
        var account = FindAccount(state, accountId);
        if (active && !state.Users.Any(u => u.Id == account.ResidentId && u.Active))
            throw new AppException("Сначала включите учётную запись жильца.");
        state.Accounts[state.Accounts.IndexOf(account)] = account with { Active = active };
        repository.Save(state);
    }

    public void SetTariff(UserSession session, Guid serviceId, string fromPeriod, decimal price)
    {
        var state = repository.Load();
        Access.RequireStaff(Access.RequireUser(state, session));
        var from = BillingPeriod.Parse(fromPeriod);
        if (price < 0 || price > 1_000_000m) throw new AppException("Тариф должен быть от 0 до 1 000 000.");
        if (!state.Services.Any(s => s.Id == serviceId)) throw new AppException("Услуга не найдена.");
        price = BillingCalculator.Money(price);
        var exact = state.Tariffs.SingleOrDefault(t => t.ServiceId == serviceId && t.EffectiveFrom == from);
        if (exact is not null)
        {
            state.Tariffs[state.Tariffs.IndexOf(exact)] = exact with { Price = price };
        }
        else
        {
            var current = state.Tariffs.SingleOrDefault(t => t.ServiceId == serviceId && t.Applies(from));
            if (current is not null)
                state.Tariffs[state.Tariffs.IndexOf(current)] = current with { EffectiveTo = from.AddDays(-1) };
            var next = state.Tariffs.Where(t => t.ServiceId == serviceId && t.EffectiveFrom > from)
                .OrderBy(t => t.EffectiveFrom).FirstOrDefault();
            state.Tariffs.Add(new Tariff { ServiceId = serviceId, Price = price, EffectiveFrom = from,
                EffectiveTo = next?.EffectiveFrom.AddDays(-1) });
        }
        repository.Save(state);
    }

    public MeterReading SubmitReading(UserSession session, Guid meterId, string period, decimal value)
    {
        var state = repository.Load();
        var user = Access.RequireUser(state, session);
        var meter = state.Meters.SingleOrDefault(m => m.Id == meterId) ?? throw new AppException("Счётчик не найден.");
        var account = FindAccount(state, meter.AccountId);
        Access.RequireAccount(user, account);
        if (!account.Active) throw new AppException("Лицевой счёт архивирован.");
        BillingPeriod.Parse(period);
        CheckReading(value);
        if (state.Invoices.Any(i => i.AccountId == account.Id && string.CompareOrdinal(i.Period, period) >= 0))
            throw new AppException("Нельзя менять показания за уже рассчитанный или более ранний период.");
        var readings = state.Readings.Where(r => r.MeterId == meterId).ToList();
        if (readings.Any(r => r.Period == period)) throw new AppException("Показание за этот месяц уже передано.");
        var previous = readings.Where(r => string.CompareOrdinal(r.Period, period) < 0).OrderByDescending(r => r.Period).FirstOrDefault();
        var next = readings.Where(r => string.CompareOrdinal(r.Period, period) > 0).OrderBy(r => r.Period).FirstOrDefault();
        if (previous is not null && value < previous.Value) throw new AppException("Показание не может быть меньше предыдущего.");
        if (next is not null && value > next.Value) throw new AppException("Показание не может быть больше следующего.");
        var reading = new MeterReading { MeterId = meterId, Period = period, Value = value };
        state.Readings.Add(reading);
        repository.Save(state);
        return reading;
    }

    public Invoice GenerateInvoice(UserSession session, Guid accountId, string period)
    {
        var state = repository.Load();
        Access.RequireStaff(Access.RequireUser(state, session));
        var date = BillingPeriod.Parse(period);
        var account = FindAccount(state, accountId);
        if (!account.Active) throw new AppException("Лицевой счёт архивирован.");
        if (state.Invoices.Any(i => i.AccountId == accountId && i.Period == period))
            throw new AppException("Квитанция по этому счёту за этот месяц уже существует.");
        if (state.Invoices.Any(i => i.AccountId == accountId && string.CompareOrdinal(i.Period, period) > 0))
            throw new AppException("Квитанции нужно формировать по порядку месяцев, от ранних к поздним.");
        var latest = LatestInvoice(state, accountId);
        if (latest is not null && date != BillingPeriod.Parse(latest.Period).AddMonths(1))
            throw new AppException("После первой квитанции формируйте каждый следующий месяц без пропусков.");
        var lines = new List<ChargeLine>();
        foreach (var service in state.Services)
        {
            var tariffs = state.Tariffs.Where(t => t.ServiceId == service.Id && t.Applies(date)).ToList();
            if (tariffs.Count != 1) throw new AppException($"Для услуги «{service.Name}» нужен ровно один тариф на {period}.");
            decimal quantity;
            switch (service.Mode)
            {
                case CalculationMode.Area: quantity = account.Area; break;
                case CalculationMode.Fixed: quantity = 1m; break;
                case CalculationMode.Meter:
                    var meter = state.Meters.SingleOrDefault(m => m.AccountId == accountId && m.ServiceId == service.Id)
                        ?? throw new AppException($"Для услуги «{service.Name}» не найден счётчик.");
                    var previousPeriod = BillingPeriod.Format(date.AddMonths(-1));
                    var previous = state.Readings.SingleOrDefault(r => r.MeterId == meter.Id && r.Period == previousPeriod);
                    var current = state.Readings.SingleOrDefault(r => r.MeterId == meter.Id && r.Period == period);
                    if (previous is null || current is null)
                        throw new AppException($"Услуга «{service.Name}»: нужны показания за {previousPeriod} и {period}.");
                    quantity = BillingCalculator.Consumption(previous.Value, current.Value); break;
                default: throw new AppException("Неизвестный способ расчёта.");
            }
            lines.Add(BillingCalculator.Calculate(service, quantity, tariffs[0].Price));
        }
        if (lines.Count == 0) throw new AppException("В справочнике нет услуг для расчёта.");
        var invoice = new Invoice { AccountId = accountId, Number = $"{account.Number}-{period}", Period = period,
            AccountNumber = account.Number, Address = account.Address, Area = account.Area,
            ResidentName = state.Users.Single(u => u.Id == account.ResidentId).FullName,
            CreatedAt = DateTime.Now, DueDate = date.AddMonths(1).AddDays(9),
            BeginningBalance = Balance(state, account), Lines = lines };
        state.Invoices.Add(invoice);
        repository.Save(state);
        return invoice;
    }

    // Это локальная имитация подтверждённого платежа. Внешних денежных операций нет.
    public Payment ConfirmDemoPayment(UserSession session, Guid invoiceId, decimal amount, string transactionId)
    {
        var state = repository.Load();
        var user = Access.RequireUser(state, session);
        var invoice = state.Invoices.SingleOrDefault(i => i.Id == invoiceId) ?? throw new AppException("Квитанция не найдена.");
        var account = FindAccount(state, invoice.AccountId);
        Access.RequireAccount(user, account);
        transactionId = Required(transactionId, "Номер учебной операции", 80);
        if (state.Payments.Any(p => p.TransactionId == transactionId))
            throw new AppException("Платёж с таким номером операции уже зарегистрирован.");
        if (LatestInvoice(state, invoice.AccountId)?.Id != invoiceId)
            throw new AppException("Оплачивайте последнюю квитанцию: в неё перенесён долг предыдущих периодов.");
        var due = Math.Max(0, Balance(state, account));
        if (amount <= 0 || amount != BillingCalculator.Money(amount) || amount > due)
            throw new AppException($"Сумма платежа должна быть положительной, с точностью до копеек и не больше {due:N2} руб.");
        var payment = new Payment { InvoiceId = invoiceId, Amount = amount, ConfirmedAt = DateTime.Now,
            TransactionId = transactionId, Status = PaymentStatus.Confirmed };
        state.Payments.Add(payment);
        repository.Save(state);
        return payment;
    }

    public ReceiptData GetReceipt(UserSession session, Guid invoiceId)
    {
        var state = repository.Load();
        var user = Access.RequireUser(state, session);
        var invoice = state.Invoices.SingleOrDefault(i => i.Id == invoiceId) ?? throw new AppException("Квитанция не найдена.");
        Access.RequireAccount(user, FindAccount(state, invoice.AccountId));
        var summary = Summarize(state, invoice);
        return new ReceiptData(invoice, summary.Paid, summary.Remaining, summary.Status);
    }

    public void ExportReceipt(UserSession session, Guid invoiceId, string path) => exporter.Export(GetReceipt(session, invoiceId), path);

    public ReportData GetReport(UserSession session, string period)
    {
        var state = repository.Load();
        Access.RequireStaff(Access.RequireUser(state, session));
        var month = BillingPeriod.Parse(period);
        var invoices = state.Invoices.Where(i => i.Period == period).ToList();
        var payments = state.Payments.Where(p => p.Status == PaymentStatus.Confirmed &&
            p.ConfirmedAt.Year == month.Year && p.ConfirmedAt.Month == month.Month).Sum(p => p.Amount);
        var balances = state.Accounts.Select(a => Balance(state, a)).ToList();
        return new ReportData(period, invoices.Count, invoices.Sum(i => i.TotalCharges), payments,
            balances.Where(b => b > 0).Sum(), -balances.Where(b => b < 0).Sum());
    }

    private static Invoice? LatestInvoice(StoreState state, Guid accountId) =>
        state.Invoices.Where(i => i.AccountId == accountId).OrderByDescending(i => i.Period).FirstOrDefault();

    private static InvoiceInfo Summarize(StoreState state, Invoice invoice)
    {
        var paid = state.Payments.Where(p => p.InvoiceId == invoice.Id && p.Status == PaymentStatus.Confirmed).Sum(p => p.Amount);
        var isLatest = LatestInvoice(state, invoice.AccountId)?.Id == invoice.Id;
        var remaining = isLatest ? Math.Max(0, Balance(state, FindAccount(state, invoice.AccountId))) : 0;
        var status = !isLatest && paid < invoice.AmountDue ? InvoiceStatus.CarriedForward
            : remaining == 0 && (isLatest || paid >= invoice.AmountDue) ? InvoiceStatus.Paid
            : paid > 0 ? InvoiceStatus.PartiallyPaid : InvoiceStatus.Unpaid;
        return new InvoiceInfo(invoice, paid, remaining, status);
    }

    private static decimal Balance(StoreState state, PersonalAccount account)
    {
        var invoices = state.Invoices.Where(i => i.AccountId == account.Id).ToList();
        var ids = invoices.Select(i => i.Id).ToHashSet();
        // Не суммируем ClosingBalance квитанций: в них повторяется переносимый долг.
        return account.OpeningBalance + invoices.Sum(i => i.TotalCharges)
            - state.Payments.Where(p => ids.Contains(p.InvoiceId) && p.Status == PaymentStatus.Confirmed).Sum(p => p.Amount);
    }

    private static PersonalAccount FindAccount(StoreState state, Guid id) => state.Accounts.SingleOrDefault(a => a.Id == id)
        ?? throw new AppException("Лицевой счёт не найден.");
    private static UserInfo ToInfo(User u) => new(u.Id, u.Login, u.FullName, u.Role, u.Active);
    private static string Required(string text, string name, int max)
    {
        text = text.Trim();
        if (text.Length == 0 || text.Length > max || text.Any(char.IsControl))
            throw new AppException($"Поле «{name}» должно содержать от 1 до {max} символов без управляющих знаков.");
        return text;
    }
    private static void CheckArea(decimal area)
    {
        if (area <= 0 || area > 100_000m) throw new AppException("Площадь должна быть больше 0 и не больше 100 000 м².");
    }
    private static void CheckReading(decimal value)
    {
        if (value < 0 || value > 1_000_000_000m) throw new AppException("Показание должно быть от 0 до 1 000 000 000.");
    }
}
