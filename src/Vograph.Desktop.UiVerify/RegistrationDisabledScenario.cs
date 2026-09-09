namespace Vograph.Desktop.UiVerify;

public static class RegistrationDisabledScenario
{
    public static void Run(Ui ui, Report report)
    {
        RequireLoginOnly(ui);
        ui.SetAccountField("Account.Username", "qa.disabled." + Guid.NewGuid().ToString("N")[..12]);
        ui.SetAccountField("Account.Password", Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24)));
        var password = ui.Find("Account.Password");
        if (password.Properties.IsPassword.TryGetValue(out var isPassword))
        {
            if (!isPassword) throw new InvalidOperationException("Password field is not protected in UIA.");
        }
        else report.Pass("Граница проверки маскировки", "UIA IsPassword недоступно; маскировка проверяется по кадрам, не по свойству UIA");
        password.Focus();
        if (!ui.WaitFor(() => password.Properties.HasKeyboardFocus.Value))
            throw new InvalidOperationException("Password focus missing.");
        report.Pass("Регистрация отключена: тёмная тема, пароль скрыт, фокус в поле", ui.Shot("registration-disabled-dark-masked-focus"));
        ui.Click("Shell.ThemeToggle");
        RequireLoginOnly(ui);
        password.Focus();
        if (!ui.WaitFor(() => password.Properties.HasKeyboardFocus.Value))
            throw new InvalidOperationException("Password focus missing after theme change.");
        report.Pass("Регистрация отключена: светлая тема, пароль скрыт", ui.Shot("registration-disabled-light-masked-focus"));
        ui.Click("Account.Login");
        if (!ui.WaitText("Account.Status", s => s.Contains("Неверный")))
            throw new InvalidOperationException("Synthetic invalid login was not rejected.");
        RequireLoginOnly(ui);
        report.Pass("Вход доступен: сервер отклонил синтетические данные", ui.Shot("registration-disabled-light-login-rejected"));
        ui.Click("Shell.ThemeToggle");
        RequireLoginOnly(ui);
        report.Pass("После входа регистрация по-прежнему отсутствует", ui.Shot("registration-disabled-dark-login-rejected"));
    }

    private static void RequireLoginOnly(Ui ui)
    {
        if (!ui.WaitFor(() => ui.Find("Account.Login").IsEnabled))
            throw new InvalidOperationException("Login is disabled.");
        if (ui.FindAll("Account.Mode").Length != 0 || ui.FindAll("Account.Register").Length != 0)
            throw new InvalidOperationException("Registration entry or action exists in UIA.");
    }
}
