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

    // GET api/office/engineers
    [HttpGet]
    public async Task<IActionResult> List()
    {
        var office = await GetMyOfficeAsync(track: false);
        if (office is null) return NotFound(new { message = "No office is linked to this account." });

        var list = await Db.Engineers.AsNoTracking().Include(e => e.User)
            .Where(e => e.OfficeId == office.Id)
            .ToListAsync();

        return Ok(list
            .OrderBy(e => e.Status == EngineerStatus.Active ? 0 : 1)
            .ThenBy(e => e.User.FullName)
            .Select(e => ToDto(e, e.User)));
    }

    // PUT api/office/engineers/{id}
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, UpdateEngineerRequest req)
    {
        var (office, engineer, error) = await LoadMineAsync(id);
        if (error is not null) return error;

        var syndicateNo = req.SyndicateNo.Trim();
        if (await Db.Engineers.AnyAsync(e => e.OfficeId == office!.Id && e.Id != id && e.SyndicateNo == syndicateNo))
            return Conflict(new { message = "Another engineer in your office has this syndicate number." });

        engineer!.User.FullName = req.FullName.Trim();
        engineer.User.Phone = req.Phone.Trim();
        engineer.Specialty = req.Specialty.Trim();
        engineer.YearsExperience = req.YearsExperience;
        engineer.SyndicateNo = syndicateNo;

        await Db.SaveChangesAsync();
        return Ok(ToDto(engineer, engineer.User));
    }

    // POST api/office/engineers/{id}/deactivate
    [HttpPost("{id:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid id)
    {
        var (office, engineer, error) = await LoadMineAsync(id);
        if (error is not null) return error;
        if (engineer!.Status == EngineerStatus.Deactivated)
            return Conflict(new { message = "This engineer is already deactivated." });

        engineer.Status = EngineerStatus.Deactivated;
        engineer.User.Status = UserStatus.Suspended;          // blocks login
        Log(office!.Id, "EngineerDeactivated", engineer.User.FullName);

        await Db.SaveChangesAsync();
        return Ok(ToDto(engineer, engineer.User));
    }

    // POST api/office/engineers/{id}/reactivate
    [HttpPost("{id:guid}/reactivate")]
    public async Task<IActionResult> Reactivate(Guid id)
    {
        var (office, engineer, error) = await LoadMineAsync(id);
        if (error is not null) return error;
        if (engineer!.Status == EngineerStatus.Active)
            return Conflict(new { message = "This engineer is already active." });

        engineer.Status = EngineerStatus.Active;
        // If they never set a password, they go back to "waiting for activation"
        engineer.User.Status = string.IsNullOrEmpty(engineer.User.PasswordHash) ? UserStatus.PendingActivation : UserStatus.Active;
        Log(office!.Id, "EngineerReactivated", engineer.User.FullName);

        await Db.SaveChangesAsync();
        return Ok(ToDto(engineer, engineer.User));
    }

    // POST api/office/engineers/{id}/resend-activation
    [HttpPost("{id:guid}/resend-activation")]
    public async Task<IActionResult> ResendActivation(Guid id)
    {
        var (office, engineer, error) = await LoadMineAsync(id);
        if (error is not null) return error;
        if (engineer!.Status != EngineerStatus.Active)
            return Conflict(new { message = "Reactivate the engineer first." });
        if (engineer.User.Status != UserStatus.PendingActivation)
            return Conflict(new { message = "This engineer has already activated their account." });

        // A new token cancels the old one (see AuthTokenService)
        var rawToken = await _tokens.CreateAsync(engineer.User, TokenType.SetPassword, ActivationLifetime);
        await Db.SaveChangesAsync();

        var sent = await SendActivationAsync(engineer.User, office!.Name, rawToken);
        return Ok(new { emailSent = sent, message = sent ? "A new activation link was sent." : "The email could not be sent. Try again later." });
    }

    // Finds an engineer that belongs to the logged-in office; 404 for anyone else's engineer
    private async Task<(Office? office, Engineer? engineer, IActionResult? error)> LoadMineAsync(Guid id)
    {
        var office = await GetMyOfficeAsync();
        if (office is null) return (null, null, NotFound(new { message = "No office is linked to this account." }));
        if (office.Status == OfficeStatus.Suspended)
            return (office, null, StatusCode(StatusCodes.Status403Forbidden, new { message = "This office is suspended. Please contact support." }));

        var engineer = await Db.Engineers.Include(e => e.User)
            .FirstOrDefaultAsync(e => e.Id == id && e.OfficeId == office.Id);
        if (engineer is null) return (office, null, NotFound(new { message = "Engineer not found." }));

        return (office, engineer, null);
    }

    private void Log(Guid officeId, string action, string value) =>
        Db.AuditLogs.Add(new AuditLog { Id = Guid.NewGuid(), ActorUserId = CurrentUserId!.Value, OfficeId = officeId, Action = action, NewValue = value, CreatedAt = DateTime.UtcNow });

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