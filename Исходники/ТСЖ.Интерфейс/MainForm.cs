using System.Diagnostics;
using Tsg.Application;
using Tsg.Domain;
using Tsg.Infrastructure;

namespace Tsg.Desktop;

internal sealed class MainForm : Form
{
    private readonly HousingService service;
    private readonly UserSession session;
    private readonly bool staff;
    private HousingSnapshot snapshot = null!;
    internal TabControl Tabs { get; } = new() { Dock = DockStyle.Fill, Padding = new Point(15, 9) };
    internal Label Status { get; } = new() { Dock = DockStyle.Bottom, Height = 48, Padding = new Padding(12, 10, 12, 5) };
    private readonly Label summary = new() { Dock = DockStyle.Top, Height = 45, Padding = new Padding(12, 12, 0, 0) };
    internal DataGridView AccountsGrid { get; } = Ui.Grid("Счёт", "Адрес", "Жилец", "Площадь, м²", "Сальдо, ₽", "Активен");
    internal DataGridView InvoicesGrid { get; } = Ui.Grid("Номер", "Период", "Начислено, ₽", "Сальдо, ₽", "К оплате, ₽", "Остаток, ₽", "Статус");
    internal DataGridView ReadingsGrid { get; } = Ui.Grid("Счёт", "Счётчик", "Услуга", "Период", "Показание");
    private readonly DataGridView tariffsGrid = Ui.Grid("Услуга", "Ед.", "Тариф, ₽", "Начало", "Окончание");
    private readonly DataGridView paymentsGrid = Ui.Grid("Квитанция", "Дата", "Сумма, ₽", "Учебная операция", "Статус");
    private readonly DataGridView usersGrid = Ui.Grid("Логин", "ФИО", "Роль", "Активен");
    private readonly TextBox accountNumber = Ui.Text(100), accountAddress = Ui.Text(270);
    private readonly NumericUpDown accountArea = Ui.Number(0, 100_000, 4), openingBalance = Ui.Number(-1_000_000_000, 1_000_000_000),
        waterBaseline = Ui.Number(decimals: 4), electricityBaseline = Ui.Number(decimals: 4);
    private readonly ComboBox accountResident = Ui.Combo(230);
    private readonly DateTimePicker baselineMonth = Ui.Month();
    internal ComboBox MeterChoice { get; } = Ui.Combo(360);
    internal DateTimePicker ReadingMonth { get; } = Ui.Month();
    internal NumericUpDown ReadingValue { get; } = Ui.Number(decimals: 4);
    internal Button ReadingButton { get; }
    internal ComboBox InvoiceAccount { get; } = Ui.Combo(330);
    internal DateTimePicker InvoiceMonth { get; } = Ui.Month();
    internal Button GenerateButton { get; }
    internal NumericUpDown PaymentAmount { get; } = Ui.Number();
    internal TextBox TransactionInput { get; } = Ui.Text(220);
    internal Button PaymentButton { get; }
    internal Button LogoutButton { get; }
    private readonly TextBox receiptDetail = new() { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill,
        ScrollBars = ScrollBars.Vertical, BackColor = Color.White, BorderStyle = BorderStyle.None };
    private readonly ComboBox tariffService = Ui.Combo(255);
    private readonly DateTimePicker tariffMonth = Ui.Month(), reportMonth = Ui.Month();
    private readonly NumericUpDown tariffPrice = Ui.Number(0, 1_000_000);
    private readonly TextBox reportText = new() { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill,
        Font = new Font("Segoe UI", 13), BorderStyle = BorderStyle.None, BackColor = Color.White };
    private readonly TextBox newLogin = Ui.Text(130), newName = Ui.Text(230), newPassword = Ui.Text(160);
    private readonly ComboBox newRole = Ui.Combo(155);
    private bool refreshing;
    internal TextBox AccountAddressInput => accountAddress;
    internal Button SaveAccountButton { get; private set; } = null!;

    public MainForm(HousingService housing, UserSession currentUser, string dataPath)
    {
        service = housing; session = currentUser; staff = session.Role != UserRole.Resident;
        Text = "Квитанции ТСЖ — " + session.FullName; StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1230, 780); MinimumSize = new Size(1130, 720);
        Font = new Font("Segoe UI", 10); BackColor = Color.FromArgb(244, 247, 251);
        var header = new Panel { Dock = DockStyle.Top, Width = ClientSize.Width, Height = 85, BackColor = Ui.Navy };
        header.Controls.Add(new Label { Text = "Квитанции ТСЖ", ForeColor = Color.White,
            Font = new Font("Segoe UI", 23, FontStyle.Bold), Location = new Point(18, 14), AutoSize = true });
        header.Controls.Add(new Label { Text = $"{session.FullName}  ·  {Ui.Role(session.Role)}", ForeColor = Color.White,
            Location = new Point(23, 56), AutoSize = true });
        LogoutButton = Ui.Button("Выйти", () => { DialogResult = DialogResult.Retry; Close(); });
        LogoutButton.Anchor = AnchorStyles.Top | AnchorStyles.Right; LogoutButton.Location = new Point(1080, 24);
        header.Controls.Add(LogoutButton);
        var dataTip = new ToolTip();
        dataTip.SetToolTip(summary, "Файл данных: " + dataPath);
        Status.Text = "Выберите вкладку. Оплата в программе является учебной.";
        ReadingButton = Ui.Button("Передать показание", () => Run(() => service.SubmitReading(session,
            Ui.ChoiceValue<Meter>(MeterChoice).Id, Ui.Period(ReadingMonth), ReadingValue.Value), "Показание сохранено."));
        GenerateButton = Ui.Button("Сформировать квитанцию", () => Run(() => service.GenerateInvoice(session,
            Ui.ChoiceValue<PersonalAccount>(InvoiceAccount).Id, Ui.Period(InvoiceMonth)), "Квитанция сформирована."));
        PaymentButton = Ui.Button("Подтвердить учебную оплату", () => Run(() => service.ConfirmDemoPayment(session,
            SelectedInvoice().Id, PaymentAmount.Value, TransactionInput.Text), "Учебный платёж подтверждён."));
        BuildAccounts(); BuildReadings(); if (staff) BuildTariffs(); BuildInvoices(); BuildPayments();
        if (staff) BuildReport(); if (session.Role == UserRole.Administrator) BuildUsers();
        Controls.Add(Tabs); Controls.Add(summary); Controls.Add(header); Controls.Add(Status);
        RefreshData();
    }

    private TabPage Page(string title, Control content, Control? toolbar = null)
    {
        var page = new TabPage(title) { Padding = new Padding(10), BackColor = Color.White };
        page.Controls.Add(content); if (toolbar is not null) page.Controls.Add(toolbar);
        Tabs.TabPages.Add(page); return page;
    }

    private void BuildAccounts()
    {
        if (!staff) { Page("Лицевые счета", AccountsGrid); return; }
        baselineMonth.Value = new DateTime(2026, 9, 1);
        accountArea.Value = 50; accountNumber.MaxLength = 30; accountAddress.MaxLength = 200;
        SaveAccountButton = Ui.Button("Сохранить изменения", () => Run(() => service.UpdateAccount(session, SelectedAccount().Id,
            accountAddress.Text, accountArea.Value, Ui.ChoiceValue<UserInfo>(accountResident).Id), "Реквизиты счёта обновлены."));
        var edit = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        edit.Controls.Add(Ui.Row(Ui.Field("Номер нового счёта", accountNumber), Ui.Field("Адрес", accountAddress),
            Ui.Field("Площадь", accountArea), Ui.Field("Ответственный жилец", accountResident), Ui.Field("Начальное сальдо", openingBalance)));
        edit.Controls.Add(Ui.Row(Ui.Field("Базовый месяц", baselineMonth), Ui.Field("Вода на начало", waterBaseline),
            Ui.Field("Электричество на начало", electricityBaseline),
            Ui.Button("Добавить счёт", () => Run(() => service.AddAccount(session, accountNumber.Text, accountAddress.Text,
                accountArea.Value, Ui.ChoiceValue<UserInfo>(accountResident).Id, openingBalance.Value, Ui.Period(baselineMonth),
                waterBaseline.Value, electricityBaseline.Value), "Лицевой счёт добавлен.")),
            SaveAccountButton,
            Ui.Button("Архивировать / включить", () => Run(() => { var account = SelectedAccount();
                service.SetAccountActive(session, account.Id, !account.Active); }, "Статус лицевого счёта изменён."))));
        Page("Лицевые счета", AccountsGrid, edit);
        AccountsGrid.CurrentCellChanged += (_, _) =>
        {
            if (!refreshing) SynchronizeAccountEditor();
        };
    }

    private void BuildReadings() => Page("Показания", ReadingsGrid, Ui.Row(Ui.Field("Счётчик", MeterChoice),
        Ui.Field("Месяц", ReadingMonth), Ui.Field("Показание", ReadingValue), ReadingButton));

    private void BuildTariffs() => Page("Тарифы", tariffsGrid, Ui.Row(Ui.Field("Услуга", tariffService),
        Ui.Field("Начало действия", tariffMonth), Ui.Field("Тариф, ₽", tariffPrice),
        Ui.Button("Сохранить тариф", () => Run(() => service.SetTariff(session, Ui.ChoiceValue<UtilityService>(tariffService).Id,
            Ui.Period(tariffMonth), tariffPrice.Value), "Тариф сохранён. Выпущенные квитанции сохраняют старую цену."))));

    private void BuildInvoices()
    {
        var split = new SplitContainer { Dock = DockStyle.Fill, Size = new Size(1160, 500), Orientation = Orientation.Horizontal,
            SplitterDistance = 180, Panel1MinSize = 100, Panel2MinSize = 80 };
        split.Panel1.Controls.Add(InvoicesGrid); split.Panel2.Padding = new Padding(8); split.Panel2.Controls.Add(receiptDetail);
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false };
        if (staff) toolbar.Controls.Add(Ui.Row(Ui.Field("Лицевой счёт", InvoiceAccount), Ui.Field("Расчётный месяц", InvoiceMonth), GenerateButton));
        toolbar.Controls.Add(Ui.Row(Ui.Button("Сохранить PDF", SavePdf), Ui.Button("Открыть PDF для печати", PrintPdf),
            Ui.Button("Обновить", () => Run(() => { }, "Данные обновлены."))));
        toolbar.Controls.Add(Ui.Row(Ui.Field("Сумма, ₽", PaymentAmount), Ui.Field("Номер учебной операции", TransactionInput), PaymentButton));
        Page("Квитанции", split, toolbar);
        InvoicesGrid.CurrentCellChanged += (_, _) => { if (!refreshing) ShowReceipt(); };
    }

    private void BuildPayments() => Page("Платежи", paymentsGrid, new Label { Dock = DockStyle.Top, Height = 42,
        Text = "Локальные подтверждённые учебные платежи. Реальные деньги не списываются.", Padding = new Padding(5, 8, 0, 0) });

    private void BuildReport()
    {
        Page("Отчёт", reportText, Ui.Row(Ui.Field("Месяц", reportMonth),
            Ui.Button("Показать отчёт", () => Run(ShowReport, "Отчёт обновлён."))));
    }

    private void BuildUsers()
    {
        newPassword.UseSystemPasswordChar = true; newPassword.MaxLength = 128; newLogin.MaxLength = 40; newName.MaxLength = 120;
        newRole.DataSource = new[] { "Жилец", "Бухгалтер", "Администратор" };
        Page("Пользователи", usersGrid, Ui.Row(Ui.Field("Логин", newLogin), Ui.Field("ФИО", newName),
            Ui.Field("Пароль", newPassword), Ui.Field("Роль", newRole),
            Ui.Button("Добавить пользователя", () => Run(() => { var role = newRole.SelectedIndex switch
                { 1 => UserRole.Accountant, 2 => UserRole.Administrator, _ => UserRole.Resident };
                service.AddUser(session, newLogin.Text, newName.Text, newPassword.Text, role); newPassword.Clear(); }, "Пользователь добавлен.")),
            Ui.Button("Отключить / включить", () => Run(() => { var user = Ui.Selected<UserInfo>(usersGrid)
                ?? throw new AppException("Выберите пользователя."); service.SetUserActive(session, user.Id, !user.Active); }, "Статус пользователя изменён."))));
    }

    internal void RefreshData()
    {
        var selectedAccountId = Ui.Selected<AccountInfo>(AccountsGrid)?.Account.Id;
        var selectedInvoiceId = Ui.Selected<InvoiceInfo>(InvoicesGrid)?.Invoice.Id;
        refreshing = true;
        try
        {
            snapshot = service.GetSnapshot(session);
            Ui.Fill(AccountsGrid, snapshot.Accounts, a => [a.Account.Number, a.Account.Address, a.ResidentName,
                a.Account.Area.ToString("0.####"), a.Balance.ToString("N2"), a.Account.Active ? "Да" : "Нет"]);
            Ui.Fill(InvoicesGrid, snapshot.Invoices, i => [i.Invoice.Number, i.Invoice.Period, i.Invoice.TotalCharges.ToString("N2"),
                i.Invoice.ClosingBalance.ToString("N2"), i.Invoice.AmountDue.ToString("N2"), i.Remaining.ToString("N2"), PdfReceiptExporter.StatusText(i.Status)]);
            var accounts = snapshot.Accounts.ToDictionary(a => a.Account.Id);
            var meters = snapshot.Meters.ToDictionary(m => m.Id);
            var services = snapshot.Services.ToDictionary(s => s.Id);
            Ui.Fill(ReadingsGrid, snapshot.Readings.OrderByDescending(r => r.Period).ThenBy(r => meters[r.MeterId].SerialNumber),
                r => [accounts[meters[r.MeterId].AccountId].Account.Number, meters[r.MeterId].SerialNumber,
                    services[meters[r.MeterId].ServiceId].Name, r.Period, r.Value.ToString("0.####")]);
            Ui.Fill(tariffsGrid, snapshot.Tariffs.OrderBy(t => services[t.ServiceId].Name).ThenByDescending(t => t.EffectiveFrom),
                t => [services[t.ServiceId].Name, services[t.ServiceId].Unit, t.Price.ToString("N2"),
                    t.EffectiveFrom.ToString("dd.MM.yyyy"), t.EffectiveTo?.ToString("dd.MM.yyyy") ?? "Без окончания"]);
            var invoiceNumbers = snapshot.Invoices.ToDictionary(i => i.Invoice.Id, i => i.Invoice.Number);
            Ui.Fill(paymentsGrid, snapshot.Payments.OrderByDescending(p => p.ConfirmedAt), p =>
                [invoiceNumbers[p.InvoiceId], p.ConfirmedAt.ToString("dd.MM.yyyy HH:mm"), p.Amount.ToString("N2"), p.TransactionId, "Подтверждён"]);
            Ui.Bind(MeterChoice, snapshot.Meters.Where(m => accounts[m.AccountId].Account.Active),
                m => $"{accounts[m.AccountId].Account.Number} · {services[m.ServiceId].Name} · {m.SerialNumber}");
            Ui.Bind(InvoiceAccount, snapshot.Accounts.Where(a => a.Account.Active).Select(a => a.Account), a => $"{a.Number} · {a.Address}");
            Ui.Bind(tariffService, snapshot.Services, s => s.Name);
            Ui.Bind(accountResident, snapshot.Residents, r => r.FullName + (r.Active ? "" : " (отключён)"));
            RestoreGridSelection<AccountInfo>(AccountsGrid, a => a.Account.Id == selectedAccountId);
            RestoreGridSelection<InvoiceInfo>(InvoicesGrid, i => i.Invoice.Id == selectedInvoiceId);
            if (session.Role == UserRole.Administrator)
                Ui.Fill(usersGrid, service.GetUsers(session), u => [u.Login, u.FullName, Ui.Role(u.Role), u.Active ? "Да" : "Нет"]);
            var debt = snapshot.Accounts.Sum(a => Math.Max(0, a.Balance));
            summary.Text = $"Лицевых счетов: {snapshot.Accounts.Count}     Квитанций: {snapshot.Invoices.Count}     Текущий долг: {debt:N2} ₽";
            if (staff) ShowReport();
        }
        finally { refreshing = false; }
        SynchronizeAccountEditor();
        ShowReceipt();
    }

    private void SynchronizeAccountEditor()
    {
        if (!staff || Ui.Selected<AccountInfo>(AccountsGrid) is not { } info) return;
        accountAddress.Text = info.Account.Address;
        accountArea.Value = info.Account.Area;
        SelectCombo<UserInfo>(accountResident, user => user.Id == info.Account.ResidentId);
    }

    private static void RestoreGridSelection<T>(DataGridView grid, Func<T, bool> predicate) where T : class
    {
        foreach (DataGridViewRow row in grid.Rows)
            if (row.Tag is T value && predicate(value)) { grid.CurrentCell = row.Cells[0]; return; }
    }

    private void ShowReceipt()
    {
        var info = Ui.Selected<InvoiceInfo>(InvoicesGrid);
        if (info is null) { receiptDetail.Text = "Квитанций пока нет. Бухгалтер формирует квитанцию после передачи показаний."; return; }
        var invoice = info.Invoice;
        receiptDetail.Text = $"Квитанция {invoice.Number}   ·   {invoice.ResidentName}\r\n{invoice.Address}\r\n\r\n" +
            string.Join("\r\n", invoice.Lines.Select(l => $"{l.ServiceName}: {l.Quantity:0.####} {l.Unit} × {l.Price:N2} ₽ = {l.Amount:N2} ₽")) +
            $"\r\n\r\nНачислено: {invoice.TotalCharges:N2} ₽     Начальное сальдо: {invoice.BeginningBalance:N2} ₽" +
            $"\r\nК оплате при формировании: {invoice.AmountDue:N2} ₽     Остаток: {info.Remaining:N2} ₽     {PdfReceiptExporter.StatusText(info.Status)}";
        PaymentAmount.Value = Math.Min(PaymentAmount.Maximum, info.Remaining);
        TransactionInput.Text = "DEMO-" + Guid.NewGuid().ToString("N")[..12];
    }

    private void ShowReport()
    {
        var report = service.GetReport(session, Ui.Period(reportMonth));
        reportText.Text = $"Отчёт за {report.Period}\r\n\r\nКвитанций за расчётный месяц: {report.InvoiceCount}\r\n" +
            $"Начислено за расчётный месяц: {report.Charges:N2} ₽\r\n" +
            $"Учебные оплаты за календарный месяц: {report.Payments:N2} ₽\r\n\r\n" +
            $"Текущий долг по всем счетам: {report.TotalDebt:N2} ₽\r\nТекущая переплата по всем счетам: {report.TotalCredit:N2} ₽\r\n\r\n" +
            "Долг и переплата рассчитаны на момент просмотра.\r\nПереносимые суммы квитанций не суммируются повторно.";
    }

    private void SavePdf()
    {
        var info = Ui.Selected<InvoiceInfo>(InvoicesGrid);
        if (info is null) { Status.ForeColor = Color.Firebrick; Status.Text = "Выберите квитанцию."; return; }
        using var dialog = new SaveFileDialog { Filter = "PDF|*.pdf", FileName = "Квитанция_" +
            SafeFileName(info.Invoice.Number) + ".pdf", DefaultExt = "pdf", AddExtension = true };
        SaveReceiptToPath(dialog.ShowDialog(this) == DialogResult.OK ? dialog.FileName : null);
    }

    internal void SaveReceiptToPath(string? path)
    {
        // Отмена выбора файла не обновляет форму и не стирает введённые поля оплаты.
        if (path is null) { Status.ForeColor = Color.DimGray; Status.Text = "Сохранение PDF отменено."; return; }
        Run(() => service.ExportReceipt(session, SelectedInvoice().Id, path), "PDF сохранён.");
    }

    private void PrintPdf()
    {
        Run(() =>
        {
            var invoice = SelectedInvoice();
            var path = Path.Combine(Path.GetTempPath(), "TsgReceiptDemo", "Квитанция_" + invoice.Id.ToString("N") + ".pdf");
            service.ExportReceipt(session, invoice.Id, path);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }, "PDF открыт. Для печати используйте команду печати в программе просмотра.");
    }

    internal void Run(Action action, string success)
    {
        try { action(); RefreshData(); Status.ForeColor = Color.FromArgb(20, 110, 65); Status.Text = success; }
        catch (AppException exception) { Status.ForeColor = Color.Firebrick; Status.Text = exception.Message; }
        catch (System.ComponentModel.Win32Exception) { Status.ForeColor = Color.Firebrick; Status.Text = "Не удалось открыть PDF. Сохраните файл и откройте его вручную."; }
    }
    private PersonalAccount SelectedAccount() => Ui.Selected<AccountInfo>(AccountsGrid)?.Account ?? throw new AppException("Выберите лицевой счёт.");
    private Invoice SelectedInvoice() => Ui.Selected<InvoiceInfo>(InvoicesGrid)?.Invoice ?? throw new AppException("Выберите квитанцию.");
    internal static void SelectCombo<T>(ComboBox combo, Func<T, bool> predicate) where T : class
    {
        for (var index = 0; index < combo.Items.Count; index++)
        {
            combo.SelectedIndex = index;
            if (predicate(Ui.ChoiceValue<T>(combo))) return;
        }
        combo.SelectedIndex = -1;
    }
    private static string SafeFileName(string text) => string.Concat(text.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
}
