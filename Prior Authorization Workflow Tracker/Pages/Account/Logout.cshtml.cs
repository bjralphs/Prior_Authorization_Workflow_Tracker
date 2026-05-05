using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Prior_Authorization_Workflow_Tracker.Models;

namespace Prior_Authorization_Workflow_Tracker.Pages.Account;

/// <summary>
/// Logout Razor Page — signs the user out and redirects to the login page.
/// POST-only to prevent CSRF-triggered logouts via image tags or redirects.
/// </summary>
public sealed class LogoutModel : PageModel
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly ILogger<LogoutModel> _logger;

    public LogoutModel(
        SignInManager<ApplicationUser> signInManager,
        ILogger<LogoutModel> logger)
    {
        _signInManager = signInManager;
        _logger        = logger;
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var userName = User.Identity?.Name ?? "unknown";
        await _signInManager.SignOutAsync();
        _logger.LogInformation("User {UserName} signed out.", userName);
        return Redirect("/Account/Login");
    }

    // GET: show a minimal "signing out..." page with auto-POST
    public IActionResult OnGet() => Page();
}
