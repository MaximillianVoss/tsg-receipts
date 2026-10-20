using System.Globalization;
using Tsg.Application;
using Tsg.Domain;
using Tsg.Infrastructure;

namespace Tsg.Desktop;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ru-RU");
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("ru-RU");
        ApplicationConfiguration.Initialize();
        var verificationIndex = Array.IndexOf(args, "--verify-ui");
        try
        {
            if (verificationIndex >= 0)
            {
                if (verificationIndex + 1 >= args.Length) throw new AppException("Укажите папку для --verify-ui.");
                return UiVerification.Run(Path.GetFullPath(args[verificationIndex + 1]));
            }
            var dataIndex = Array.IndexOf(args, "--data");
            var dataPath = dataIndex >= 0 && dataIndex + 1 < args.Length ? Path.GetFullPath(args[dataIndex + 1])
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TsgReceiptDemo", "state.json");
            using var repository = new JsonRepository(dataPath);
            var hasher = new Pbkdf2PasswordHasher();
            repository.Initialize(() => DemoData.Create(hasher));
            var authentication = new AuthenticationService(repository, hasher);
            var housing = new HousingService(repository, hasher, new PdfReceiptExporter());
            while (true)
            {
                using var login = new LoginForm(authentication, dataPath);
                if (login.ShowDialog() != DialogResult.OK || login.Session is null) return 0;
                using var main = new MainForm(housing, login.Session, dataPath);
                if (main.ShowDialog() != DialogResult.Retry) return 0;
            }
        }
        catch (Exception exception)
        {
            if (verificationIndex >= 0 && verificationIndex + 1 < args.Length)
            {
                var directory = Path.GetFullPath(args[verificationIndex + 1]);
                Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory, "failure.txt"), exception.ToString());
            }
            else MessageBox.Show(exception.Message, "Квитанции ТСЖ — ошибка запуска", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }
}
