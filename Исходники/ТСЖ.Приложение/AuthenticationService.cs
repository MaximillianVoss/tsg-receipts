using Tsg.Domain;

namespace Tsg.Application;

public sealed class AuthenticationService(IRepository repository, IPasswordHasher hasher)
{
    public UserSession Login(string login, string password)
    {
        var user = repository.Load().Users.SingleOrDefault(u =>
            u.Active && string.Equals(u.Login, login.Trim(), StringComparison.OrdinalIgnoreCase));
        if (user is null || !hasher.Verify(password, user.PasswordHash))
            throw new AppException("Неверный логин или пароль.");
        return new UserSession(user.Id, user.FullName, user.Role);
    }
}
