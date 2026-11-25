using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Tafaqqad.Api.Data;

namespace Tafaqqad.Api.Controllers.Clients;

[ApiController]
[Authorize(Roles = "Client")]
public abstract class ClientApiController : ControllerBase
{
    protected readonly AppDbContext Db;

    protected ClientApiController(AppDbContext db) => Db = db;

    /// The logged-in client's user id, taken from the token (never from the request).
    protected Guid? CurrentUserId =>
        Guid.TryParse(User.FindFirst("sub")?.Value, out var id) ? id : null;
}