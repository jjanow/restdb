using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace RestDb;

public static class RestDbApiKeyDefaults
{
    public const string AuthenticationScheme = "RestDbApiKey";
    public const string HeaderName = "X-API-Key";
}

public static class RestDbAuthorizationPolicies
{
    public const string ApiAccess = "RestDbApiAccess";
}

public sealed class ApiKeyAuthenticationOptions : AuthenticationSchemeOptions
{
}

public sealed class ApiKeyAuthenticationHandler : AuthenticationHandler<ApiKeyAuthenticationOptions>
{
    private readonly IConfiguration configuration;

    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<ApiKeyAuthenticationOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IConfiguration configuration)
        : base(options, logger, encoder)
    {
        this.configuration = configuration;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string? configuredApiKey = configuration["RestDb:ApiKey"];
        if (string.IsNullOrWhiteSpace(configuredApiKey))
        {
            return Task.FromResult(AuthenticateResult.Fail("API key authentication is not configured."));
        }

        string? providedApiKey = GetApiKeyFromRequest();
        if (string.IsNullOrWhiteSpace(providedApiKey))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        if (!IsValidApiKey(providedApiKey, configuredApiKey))
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid API key."));
        }

        return Task.FromResult(AuthenticateResult.Success(CreateTicket("api-key-client", "API key client")));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.ContentType = "application/json";
        return Response.WriteAsJsonAsync(new ErrorResponse("A valid API key is required."));
    }

    private AuthenticationTicket CreateTicket(string nameIdentifier, string name)
    {
        Claim[] claims =
        {
            new Claim(ClaimTypes.NameIdentifier, nameIdentifier),
            new Claim(ClaimTypes.Name, name)
        };
        ClaimsIdentity identity = new ClaimsIdentity(claims, Scheme.Name);
        ClaimsPrincipal principal = new ClaimsPrincipal(identity);
        return new AuthenticationTicket(principal, Scheme.Name);
    }

    private string? GetApiKeyFromRequest()
    {
        string? headerApiKey = Request.Headers[RestDbApiKeyDefaults.HeaderName].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(headerApiKey))
        {
            return headerApiKey;
        }

        string? authorization = Request.Headers.Authorization.FirstOrDefault();
        if (AuthenticationHeaderValue.TryParse(authorization, out AuthenticationHeaderValue? header)
            && string.Equals(header.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase))
        {
            return header.Parameter;
        }

        return null;
    }

    private static bool IsValidApiKey(string providedApiKey, string configuredApiKey)
    {
        byte[] expected = Encoding.UTF8.GetBytes(configuredApiKey);
        byte[] actual = Encoding.UTF8.GetBytes(providedApiKey);

        return actual.Length == expected.Length
            && CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}

public static class ApiKeyAuthenticationExtensions
{
    public static IServiceCollection AddRestDbApiKeyAuthentication(this IServiceCollection services)
    {
        services
            .AddAuthentication(RestDbApiKeyDefaults.AuthenticationScheme)
            .AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>(
                RestDbApiKeyDefaults.AuthenticationScheme,
                _ => { });

        services.AddAuthorization(options =>
        {
            options.AddPolicy(RestDbAuthorizationPolicies.ApiAccess, policy =>
            {
                policy.AuthenticationSchemes.Add(RestDbApiKeyDefaults.AuthenticationScheme);
                policy.RequireAuthenticatedUser();
            });
        });

        return services;
    }
}
