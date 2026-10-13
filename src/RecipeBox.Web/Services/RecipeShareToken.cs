using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace RecipeBox.Web.Services;

public static class RecipeShareToken
{
    private const int TokenByteCount = 32;
    private const int TokenCharacterCount = 43;

    public static string Generate() =>
        WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(TokenByteCount));

    public static bool TryGetHash(string? token, out string hash)
    {
        hash = string.Empty;
        if (token is null
            || token.Length != TokenCharacterCount
            || token.Any(character =>
                !(character is >= 'A' and <= 'Z'
                    or >= 'a' and <= 'z'
                    or >= '0' and <= '9'
                    or '-'
                    or '_')))
        {
            return false;
        }

        hash = Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(token)));
        return true;
    }
}
