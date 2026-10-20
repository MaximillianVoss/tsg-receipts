using System.Text.Json;
using Tsg.Application;
using Tsg.Domain;
using Tsg.Infrastructure;

namespace Tsg.Desktop;

/// <summary>Прогон настоящих WinForms-обработчиков в отдельной демобазе, без реальных платежей.</summary>
internal static class UiVerification
{
    public static int Run(string directory)
    {
        Directory.CreateDirectory(directory);
        var dataPath = Path.Combine(directory, "state.json");
        if (File.Exists(dataPath)) throw new AppException("Для проверки интерфейса выберите новую пустую папку.");
        var results = new List<string>();
        void Check(bool condition, string scenario)
        {
            if (!condition) throw new InvalidOperationException("Не пройдена проверка: " + scenario);
            results.Add(scenario);
        }
        var hasher = new Pbkdf2PasswordHasher();
        Guid invoiceId;
        using (var repository = new JsonRepository(dataPath))
        {
            repository.Initialize(() => DemoData.Create(hasher));
            var authentication = new AuthenticationService(repository, hasher);
            var housing = new HousingService(repository, hasher, new PdfReceiptExporter());
            using (var login = new LoginForm(authentication, dataPath))
            {
                Prepare(login); Capture(login, Path.Combine(directory, "01_login.png"));
                login.LoginInput.Text = "accountant"; login.PasswordInput.Text = "wrongpass"; login.LoginButton.PerformClick();
                Check(login.Session is null && login.Error.Text.Contains("Неверный"), "UI отклоняет неверный пароль");
                login.PasswordInput.Text = DemoData.Password; login.LoginButton.PerformClick();
                Check(login.Session?.Role == UserRole.Accountant, "UI вход бухгалтера");
            }
            var accountant = authentication.Login("accountant", DemoData.Password);
            using (var form = new MainForm(housing, accountant, dataPath))
            {
                Prepare(form); Capture(form, Path.Combine(directory, "02_accounts.png"));
                Check(form.AccountsGrid.Rows.Count == 2, "UI отображает два демосчёта");
                Check(form.LogoutButton.Bounds.Right <= form.LogoutButton.Parent!.ClientSize.Width,
                    "UI кнопка Выйти находится внутри видимой области");
                var firstAccountBefore = repository.Load().Accounts.Single(a => a.Number == "1001");
                var secondAccountBefore = repository.Load().Accounts.Single(a => a.Number == "1002");
                form.AccountsGrid.CurrentCell = form.AccountsGrid.Rows[1].Cells[0];
                Check(form.AccountAddressInput.Text == secondAccountBefore.Address,
                    "UI поля редактирования относятся к выбранному второму счёту");
                form.AccountAddressInput.Text = "Исправленный адрес второго счёта";
                form.SaveAccountButton.PerformClick(); form.SaveAccountButton.PerformClick();
                Check((form.AccountsGrid.CurrentRow!.Tag as AccountInfo)?.Account.Number == "1002" &&
                    repository.Load().Accounts.Single(a => a.Number == "1001") == firstAccountBefore &&
                    repository.Load().Accounts.Single(a => a.Number == "1002") ==
                        secondAccountBefore with { Address = "Исправленный адрес второго счёта" },
                    "UI повторное сохранение второго счёта сохраняет выбор и не меняет первый счёт");
                housing.SetAccountActive(accountant, secondAccountBefore.Id, false);
                housing.SetUserActive(authentication.Login("admin", DemoData.Password), secondAccountBefore.ResidentId, false);
                form.RefreshData();
                form.AccountAddressInput.Text = "Архивный адрес второго счёта"; form.SaveAccountButton.PerformClick();
                Check(form.Status.Text == "Реквизиты счёта обновлены." &&
                    repository.Load().Accounts.Single(a => a.Number == "1002").ResidentId == secondAccountBefore.ResidentId,
                    "UI редактирование архивного счёта сохраняет отключённого владельца");
                housing.SetUserActive(authentication.Login("admin", DemoData.Password), secondAccountBefore.ResidentId, true);
                housing.SetAccountActive(accountant, secondAccountBefore.Id, true); form.RefreshData();
                Check(form.Tabs.TabPages.Cast<TabPage>().All(p => p.Text != "Пользователи"), "UI бухгалтер не видит управление пользователями");
                form.Tabs.SelectedTab = form.Tabs.TabPages.Cast<TabPage>().Single(p => p.Text == "Квитанции");
                form.GenerateButton.PerformClick();
                Check(form.InvoicesGrid.Rows.Count == 1 && form.Status.Text == "Квитанция сформирована.", "UI формирует октябрьскую квитанцию");
                var invoice = repository.Load().Invoices.Single(); invoiceId = invoice.Id;
                Check(invoice.TotalCharges == 2700 && invoice.AmountDue == 3200, "UI квитанция имеет начисления2700 и долг3200");
                Capture(form, Path.Combine(directory, "03_invoice.png"));
                form.PaymentAmount.Value = 123;
                form.TransactionInput.Text = "CANCEL-PRESERVE";
                form.SaveReceiptToPath(null);
                Check(form.Status.Text == "Сохранение PDF отменено." && form.PaymentAmount.Value == 123 &&
                    form.TransactionInput.Text == "CANCEL-PRESERVE",
                    "UI отмена сохранения PDF не сообщает успех и сохраняет поля оплаты");
                form.GenerateButton.PerformClick();
                Check(form.Status.Text.Contains("уже существует") && repository.Load().Invoices.Count == 1, "UI отклоняет дубликат квитанции");
                housing.ExportReceipt(accountant, invoiceId, Path.Combine(directory, "Квитанция_1001_2026-10.pdf"));
                var waterService = repository.Load().Services.Single(s => s.Name == "Холодная вода");
                var boundaryInvoice = invoice with { Lines = [BillingCalculator.Calculate(waterService, 1_000_000_000m, 1_000_000m)] };
                new PdfReceiptExporter().Export(new ReceiptData(boundaryInvoice, 0, boundaryInvoice.AmountDue, InvoiceStatus.Unpaid),
                    Path.Combine(directory, "Квитанция_граничные_значения.pdf"));
                Check(File.Exists(Path.Combine(directory, "Квитанция_граничные_значения.pdf")),
                    "PDF формируется с максимальными допустимыми объёмом и тарифом");
                form.LogoutButton.PerformClick();
                Check(form.DialogResult == DialogResult.Retry, "UI выход возвращает к входу");
            }
            var resident = authentication.Login("resident", DemoData.Password);
            using (var form = new MainForm(housing, resident, dataPath))
            {
                Prepare(form);
                Check(form.AccountsGrid.Rows.Count == 1 && form.InvoicesGrid.Rows.Count == 1, "UI жилец видит только свой счёт и квитанцию");
                Check(form.Tabs.TabPages.Count == 4, "UI жилец имеет только разрешённые вкладки");
                form.Tabs.SelectedTab = form.Tabs.TabPages.Cast<TabPage>().Single(p => p.Text == "Показания");
                form.ReadingMonth.Value = new DateTime(2026, 11, 1);
                MainForm.SelectCombo<Meter>(form.MeterChoice, m => m.SerialNumber == "В-1001");
                form.ReadingValue.Value = 120; form.ReadingButton.PerformClick();
                Check(form.Status.Text == "Показание сохранено." && form.ReadingsGrid.Rows.Count == 5, "UI сохраняет ноябрьское показание");
                Capture(form, Path.Combine(directory, "04_readings.png"));
                form.Tabs.SelectedTab = form.Tabs.TabPages.Cast<TabPage>().Single(p => p.Text == "Квитанции");
                form.PaymentAmount.Value = 1000; form.TransactionInput.Text = "UI-1000"; form.PaymentButton.PerformClick();
                Check(form.Status.Text == "Учебный платёж подтверждён." && housing.GetReceipt(resident, invoiceId).Remaining == 2200,
                    "UI частичная учебная оплата уменьшает долг до2200");
                Capture(form, Path.Combine(directory, "05_partial_payment.png"));
                form.PaymentAmount.Value = 2200; form.TransactionInput.Text = "UI-2200"; form.PaymentButton.PerformClick();
                Check(housing.GetReceipt(resident, invoiceId).Status == InvoiceStatus.Paid, "UI полная учебная оплата устанавливает Оплачена");
                Capture(form, Path.Combine(directory, "06_paid.png"));
            }
            var admin = authentication.Login("admin", DemoData.Password);
            using var administration = new MainForm(housing, admin, dataPath);
            Prepare(administration);
            Check(administration.Tabs.TabPages.Cast<TabPage>().Any(p => p.Text == "Пользователи"), "UI администратор видит управление пользователями");
            administration.Tabs.SelectedTab = administration.Tabs.TabPages.Cast<TabPage>().Single(p => p.Text == "Пользователи");
            Capture(administration, Path.Combine(directory, "07_users.png"));
        }
        using (var reopened = new JsonRepository(dataPath))
        {
            var auth = new AuthenticationService(reopened, hasher);
            var housing = new HousingService(reopened, hasher, new PdfReceiptExporter());
            var resident = auth.Login("resident", DemoData.Password);
            Check(housing.GetReceipt(resident, invoiceId).Remaining == 0 && reopened.Load().Payments.Count == 2,
                "Повторное открытие JSON сохраняет квитанцию и оба платежа");
        }
        File.WriteAllText(Path.Combine(directory, "ui_verification.json"), JsonSerializer.Serialize(new
        { Passed = true, Count = results.Count, Scenarios = results, Environment = Environment.OSVersion.ToString(),
            Runtime = Environment.Version.ToString(), VerifiedAt = DateTimeOffset.Now }, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }

    private static void Prepare(Form form)
    {
        form.ShowInTaskbar = false; form.Opacity = 0; form.Show();
        System.Windows.Forms.Application.DoEvents();
    }
    private static void Capture(Form form, string path)
    {
        System.Windows.Forms.Application.DoEvents();
        using var bitmap = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(path);
    }
}
