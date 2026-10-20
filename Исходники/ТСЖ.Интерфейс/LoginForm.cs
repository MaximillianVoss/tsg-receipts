using Tsg.Application;
using Tsg.Domain;

namespace Tsg.Desktop;

internal sealed class LoginForm : Form
{
    private readonly AuthenticationService authentication;
    internal TextBox LoginInput { get; } = Ui.Text(315);
    internal TextBox PasswordInput { get; } = Ui.Text(315);
    internal Button LoginButton { get; }
    internal Label Error { get; } = new() { ForeColor = Color.Firebrick, Width = 355, Height = 45 };
    public UserSession? Session { get; private set; }

    public LoginForm(AuthenticationService service, string dataPath)
    {
        authentication = service;
        Text = "Квитанции ТСЖ — вход"; ClientSize = new Size(450, 515);
        StartPosition = FormStartPosition.CenterScreen; FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; Font = new Font("Segoe UI", 10); BackColor = Color.White;
        var title = new Label { Text = "Квитанции ТСЖ", Font = new Font("Segoe UI", 23, FontStyle.Bold),
            ForeColor = Ui.Navy, Dock = DockStyle.Top, Height = 80, Padding = new Padding(35, 20, 0, 0) };
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(35, 8, 25, 8),
            FlowDirection = FlowDirection.TopDown, WrapContents = false };
        panel.Controls.Add(new Label { Text = "Учебное приложение · тема 11", AutoSize = true, Margin = new Padding(5, 0, 5, 20) });
        PasswordInput.UseSystemPasswordChar = true;
        LoginInput.MaxLength = 40; PasswordInput.MaxLength = 128;
        LoginButton = Ui.Button("Войти", TryLogin); LoginButton.Width = 315;
        panel.Controls.Add(Ui.Field("Логин", LoginInput)); panel.Controls.Add(Ui.Field("Пароль", PasswordInput));
        panel.Controls.Add(LoginButton); panel.Controls.Add(Error);
        panel.Controls.Add(new Label { Text = "Демо логины: admin, accountant, resident, resident2\nОбщий учебный пароль: Demo123!",
            Width = 360, Height = 56, ForeColor = Color.FromArgb(70, 80, 94) });
        panel.Controls.Add(new Label { Text = "Файл данных:\n" + dataPath, Width = 355, Height = 90,
            Font = new Font("Segoe UI", 8), ForeColor = Color.DimGray });
        Controls.Add(panel); Controls.Add(title); AcceptButton = LoginButton;
    }

    private void TryLogin()
    {
        try
        {
            Session = authentication.Login(LoginInput.Text, PasswordInput.Text);
            DialogResult = DialogResult.OK; Close();
        }
        catch (AppException exception) { Error.Text = exception.Message; PasswordInput.Clear(); PasswordInput.Focus(); }
    }
}
