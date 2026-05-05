using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Prior_Authorization_Workflow_Tracker.Models;
using Prior_Authorization_Workflow_Tracker.Services;

namespace Prior_Authorization_Workflow_Tracker.Pages.Account;

/// <summary>
/// Login Razor Page — handles the HTTP-based sign-in flow for Blazor Server (T20).
/// Razor Pages are used for login/logout because Blazor Server circuits cannot
/// set authentication cookies via SignalR; authentication must occur over HTTP.
/// </summary>
public sealed class LoginModel : PageModel
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IAuditService _audit;
    private readonly ILogger<LoginModel> _logger;

    public LoginModel(
        SignInManager<ApplicationUser> signInManager,
        IAuditService audit,
        ILogger<LoginModel> logger)
    {
        _signInManager = signInManager;
        _audit         = audit;
        _logger        = logger;
    }

    [BindProperty]
    public LoginInput Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public string? ErrorMessage { get; private set; }

    public async Task OnGetAsync()
    {
        // Clear any existing authentication cookie to avoid session contamination
        await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
            return Page();

        var result = await _signInManager.PasswordSignInAsync(
            Input.Email,
            Input.Password,
            Input.RememberMe,
            lockoutOnFailure: true);

        if (result.Succeeded)
        {
            _logger.LogInformation("User {Email} signed in.", Input.Email);
            // §11: write User.Login event to AuditLogs table (not just Serilog)
            var user = await _signInManager.UserManager.FindByEmailAsync(Input.Email);
            if (user is not null)
            {
                await _audit.LogAsync("User", user.Id, "Login",
                    newValues: $"{{\"email\":\"{Input.Email}\"}}");
            }
            return LocalRedirect(ReturnUrl ?? "/");
        }

        if (result.IsLockedOut)
        {
            _logger.LogWarning("User {Email} account locked out.", Input.Email);
            await _audit.LogAsync("User", Input.Email, "FailedLogin",
                newValues: $"{{\"email\":\"{Input.Email}\",\"reason\":\"LockedOut\"}}");
            ErrorMessage = "Your account has been locked out. Please try again in 5 minutes.";
            return Page();
        }

        _logger.LogWarning("Failed login attempt for {Email}.", Input.Email);
        await _audit.LogAsync("User", Input.Email, "FailedLogin",
            newValues: $"{{\"email\":\"{Input.Email}\",\"reason\":\"InvalidCredentials\"}}");
        ErrorMessage = "Invalid email or password.";
        return Page();
    }

    // ── Input model ────────────────────────────────────────────────────────────

    public sealed class LoginInput
    {
        [Required(ErrorMessage = "Email is required.")]
        [EmailAddress(ErrorMessage = "Enter a valid email address.")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Password is required.")]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        public bool RememberMe { get; set; }
    }
}
