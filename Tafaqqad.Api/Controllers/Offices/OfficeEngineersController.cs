using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tafaqqad.Api.Data;
using Tafaqqad.Api.Dtos;
using Tafaqqad.Api.Models.Entities;
using Tafaqqad.Api.Models.Enums;
using Tafaqqad.Api.Services;

namespace Tafaqqad.Api.Controllers.Offices;

[Route("api/office/engineers")]
public class OfficeEngineersController : OfficeApiController
{
    private static readonly TimeSpan ActivationLifetime = TimeSpan.FromHours(48);

    private readonly IAuthTokenService _tokens;
    private readonly IAppEmailSender _email;
    private readonly IConfiguration _config;
    private readonly ILogger<OfficeEngineersController> _logger;

    public OfficeEngineersController(AppDbContext db, IAuthTokenService tokens, IAppEmailSender email,
        IConfiguration config, ILogger<OfficeEngineersController> logger) : base(db)
    {
        _tokens = tokens; _email = email; _config = config; _logger = logger;
    }

    // POST api/office/engineers
    [HttpPost]
    public async Task<IActionResult> Create(CreateEngineerRequest req)
    {
        var office = await GetMyOfficeAsync();
        if (office is null) return NotFound(new { message = "No office is linked to this account." });
        if (office.Status == OfficeStatus.Suspended)
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "This office is suspended. Please contact support." });

        var email = req.Email.Trim().ToLowerInvariant();
        var syndicateNo = req.SyndicateNo.Trim();

        // One account per email across the whole platform (clients, offices, engineers, admin)
        if (await Db.Users.AnyAsync(u => u.Email == email))
            return Conflict(new { message = "An account with this email already exists." });

        // The same syndicate number can't be added twice to the same office
        if (await Db.Engineers.AnyAsync(e => e.OfficeId == office.Id && e.SyndicateNo == syndicateNo))
            return Conflict(new { message = "An engineer with this syndicate number already exists in your office." });

        var user = new User
        {
            Id = Guid.NewGuid(),
            Role = UserRole.Engineer,
            Email = email,
            PasswordHash = null,                      // the engineer sets it from the email link
            FullName = req.FullName.Trim(),
            Phone = req.Phone.Trim(),
            Status = UserStatus.PendingActivation,
            CreatedAt = DateTime.UtcNow
        };
        var engineer = new Engineer
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            OfficeId = office.Id,
            Specialty = req.Specialty.Trim(),
            YearsExperience = req.YearsExperience,
            SyndicateNo = syndicateNo,
            Status = EngineerStatus.Active
        };
        Db.Users.Add(user);
        Db.Engineers.Add(engineer);

        var rawToken = await _tokens.CreateAsync(user, TokenType.SetPassword, ActivationLifetime);

        Db.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            ActorUserId = CurrentUserId!.Value,
            OfficeId = office.Id,
            Action = "EngineerAdded",
            NewValue = $"{user.FullName} ({engineer.Specialty})",
            CreatedAt = DateTime.UtcNow
        });

        // User + Engineer + token + audit row: one SaveChanges, so all or nothing
        await Db.SaveChangesAsync();

        // Email AFTER the save; if it fails, the office can use "resend" (6e)
        var emailSent = await SendActivationAsync(user, office.Name, rawToken);

        return StatusCode(StatusCodes.Status201Created, new CreateEngineerResponse
        {
            Engineer = ToDto(engineer, user),
            EmailSent = emailSent,
            Message = emailSent
                ? "Engineer created. An activation link was sent to their email."
                : "Engineer created, but the email could not be sent. Use resend link."
        });
    }

    private async Task<bool> SendActivationAsync(User user, string officeName, string rawToken)
    {
        var link = $"{_config["App:FrontendBaseUrl"]!.TrimEnd('/')}/set-password?token={Uri.EscapeDataString(rawToken)}";
        try
        {
            await _email.SendAsync(user.Email, "تفعيل حساب المهندس في تفقّد",
                $"مرحباً {user.FullName}،\n\nأضافك {officeName} كمهندس على منصة تفقّد.\n" +
                $"اضغط على الرابط لتعيين كلمة المرور (صالح لمدة 48 ساعة):\n{link}");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send engineer activation email to {Email}", user.Email);
            return false;
        }
    }

    private static EngineerDto ToDto(Engineer e, User u) => new()
    {
        Id = e.Id,
        UserId = u.Id,
        FullName = u.FullName,
        Email = u.Email,
        Phone = u.Phone,
        Specialty = e.Specialty,
        YearsExperience = e.YearsExperience,
        SyndicateNo = e.SyndicateNo,
        Status = e.Status.ToString(),
        IsActivated = u.Status == UserStatus.Active && u.PasswordHash != null
    };
}