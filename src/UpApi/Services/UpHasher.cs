using System.Security.Cryptography;
using System.Text;

namespace UpApi.Services;

internal static class UpHasher
{
    public static string HashPassword(string password)
    {
        using var sha = SHA256.Create();
        return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(password)));
    }
}
