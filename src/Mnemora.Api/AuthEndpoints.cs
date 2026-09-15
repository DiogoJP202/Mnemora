using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;

namespace Mnemora.Api;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/api/auth").WithTags("Auth");

        auth.MapGet("/csrf", (IAntiforgery antiforgery, HttpContext context) =>
        {
            var tokens = antiforgery.GetAndStoreTokens(context);
            return Results.Ok(new { token = tokens.RequestToken });
        });

        auth.MapPost("/register", async (
            RegisterRequest request,
            UserManager<IdentityUser<Guid>> users,
            SignInManager<IdentityUser<Guid>> signIn) =>
        {
            var email = request.Email?.Trim();
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(request.Password))
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["credentials"] = ["Informe e-mail e senha."]
                });

            var user = new IdentityUser<Guid> { UserName = email, Email = email };
            var result = await users.CreateAsync(user, request.Password);
            if (!result.Succeeded)
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["credentials"] = result.Errors.Select(error => error.Description).ToArray()
                });

            await signIn.SignInAsync(user, isPersistent: false);
            return Results.Ok(new AuthUser(user.Id, email, false));
        }).RequireRateLimiting("auth");

        auth.MapPost("/login", async (
            LoginRequest request,
            UserManager<IdentityUser<Guid>> users,
            SignInManager<IdentityUser<Guid>> signIn) =>
        {
            var user = await users.FindByEmailAsync(request.Email?.Trim() ?? "");
            if (user is null)
                return Results.Problem("Credenciais inválidas.", statusCode: 401);
            var result = await signIn.PasswordSignInAsync(user, request.Password ?? "",
                isPersistent: false, lockoutOnFailure: true);
            if (!result.Succeeded)
                return Results.Problem("Credenciais inválidas.", statusCode: 401);
            return Results.Ok(new AuthUser(user.Id, user.Email ?? "", await users.IsInRoleAsync(user, "Admin")));
        }).RequireRateLimiting("auth");

        auth.MapPost("/logout", async (SignInManager<IdentityUser<Guid>> signIn) =>
        {
            await signIn.SignOutAsync();
            return Results.NoContent();
        }).RequireAuthorization();

        auth.MapGet("/me", async (
            ClaimsPrincipal principal,
            UserManager<IdentityUser<Guid>> users) =>
        {
            var user = await users.GetUserAsync(principal);
            return user is null ? Results.Unauthorized()
                : Results.Ok(new AuthUser(user.Id, user.Email ?? "", await users.IsInRoleAsync(user, "Admin")));
        }).RequireAuthorization();

        return app;
    }

    private sealed record RegisterRequest(string? Email, string? Password);
    private sealed record LoginRequest(string? Email, string? Password);
    private sealed record AuthUser(Guid Id, string Email, bool IsAdmin);
}
