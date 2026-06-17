using System.Data;
using Dapper;

namespace PocApi.Auth;

public sealed class Login2UserCredentialValidator(IDbConnection db) : IUserCredentialValidator
{
    public async Task<UserIdentity?> ValidateAsync(string username, string password, CancellationToken ct = default)
    {
        // Parameterized query — safe from SQL injection.
        // NOTE (POC): comparing plain-text passwords as stored in Login2.
        // Before any production use, migrate to a proper password hash (bcrypt / PBKDF2).
        const string sql = """
            SELECT Username
            FROM   Login2
            WHERE  Username = @Username
               AND Password = @Password
            """;

        var found = await db.QuerySingleOrDefaultAsync<string>(sql, new { Username = username, Password = password });
        if (found is null) return null;

        return new UserIdentity(Sub: found, Name: found, Roles: []);
    }
}
