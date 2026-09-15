using System.Security.Claims;

namespace Mnemora.Api;

public static class CurrentUser
{
    public static Guid Id(ClaimsPrincipal principal) =>
        Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
}
