using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Tafaqqad.Api.Data;
using Tafaqqad.Api.Dtos;
using Tafaqqad.Api.Infrastructure;
using Tafaqqad.Api.Models.Entities;
using Tafaqqad.Api.Models.Enums;
using Tafaqqad.Api.Services;

namespace Tafaqqad.Api.Controllers.Clients;

[Route("api/client/bookings")]
public class ClientBookingsController : ClientApiController
{
    private const int MinDaysAhead = 1;      // no same-day bookings: the office needs time to reply
    private const int MaxDaysAhead = 60;
    private const int MaxOpenBookings = 5;   // unpaid bookings a client may have at the same time
    private const int SlotLockTimeoutMs = 10_000;

    /// Bookings that are still waiting for the office or the client (not paid yet).
    private static readonly BookingStatus[] OpenStatuses =
    {
        BookingStatus.Requested, BookingStatus.Accepted,
        BookingStatus.PriceProposed, BookingStatus.PriceApproved
    };

    private readonly IAppEmailSender _email;
    private readonly ILogger<ClientBookingsController> _logger;

    public ClientBookingsController(AppDbContext db, IAppEmailSender email,
        ILogger<ClientBookingsController> logger) : base(db)
    {
        _email = email;
        _logger = logger;
    }

    /// <summary>Creates a booking request for an Active office.</summary>
    /// <remarks>
    /// The property type and the starting price come from the chosen price row.
    /// Error codes: PRICE_NOT_FOUND, LOCATION_INCOMPLETE, DATE_TOO_SOON, DATE_TOO_FAR,
    /// OFFICE_CLOSED, TOO_MANY_OPEN_BOOKINGS, DUPLICATE_BOOKING, FULLY_BOOKED, SLOT_BUSY, ACCOUNT_NOT_ACTIVE.
    /// </remarks>
    [HttpPost]
    [EnableRateLimiting("create-booking")]
    public async Task<IActionResult> Create(CreateBookingRequest req)
    {
        var clientId = CurrentUserId;
        if (clientId is null) return Unauthorized();

        // The token can outlive a suspension, so check the account itself
        var client = await Db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == clientId);
        if (client is null || client.Status != UserStatus.Active)
            return StatusCode(StatusCodes.Status403Forbidden,
                new { message = "Your account is not active.", code = "ACCOUNT_NOT_ACTIVE" });

        var p = req.Property!;
        if ((p.Lat is null) != (p.Lng is null))
            return BadRequest(new { message = "Send both latitude and longitude, or neither.", code = "LOCATION_INCOMPLETE" });

        // 1) The office must be Active. Incomplete, Suspended or unknown all look the same.
        var office = await Db.Offices.AsNoTracking()
            .Include(o => o.OwnerUser)
            .FirstOrDefaultAsync(o => o.Id == req.OfficeId!.Value && o.Status == OfficeStatus.Active);
        if (office is null) return NotFound(new { message = "Office not found." });

        // 2) The price row must belong to THIS office
        var price = await Db.OfficePrices.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == req.OfficePriceId!.Value && x.OfficeId == office.Id);
        if (price is null)
            return BadRequest(new { message = "This price option is not offered by this office.", code = "PRICE_NOT_FOUND" });

        // 3) The date: from tomorrow (Jordan time) up to MaxDaysAhead
        var date = req.ScheduledDate!.Value;
        var today = JordanClock.Today;
        if (date < today.AddDays(MinDaysAhead))
            return BadRequest(new { message = "Choose a date from tomorrow onwards.", code = "DATE_TOO_SOON" });
        if (date > today.AddDays(MaxDaysAhead))
            return BadRequest(new { message = $"You can book up to {MaxDaysAhead} days ahead.", code = "DATE_TOO_FAR" });

        // 4) The office must be open on that weekday
        var weekday = date.DayOfWeek;
        var isOpen = await Db.OfficeWorkingHours.AnyAsync(h =>
            h.OfficeId == office.Id && h.Weekday == weekday && !h.IsClosed);
        if (!isOpen)
            return BadRequest(new { message = "The office is closed on this day.", code = "OFFICE_CLOSED" });

        // 5) Limit unpaid bookings per client
        var openCount = await Db.Bookings.CountAsync(b => b.ClientId == clientId && OpenStatuses.Contains(b.Status));
        if (openCount >= MaxOpenBookings)
            return Conflict(new
            {
                message = $"You already have {MaxOpenBookings} open requests. Finish or cancel one first.",
                code = "TOO_MANY_OPEN_BOOKINGS"
            });

        // 6) From here on, only one request at a time per office + day (see B1b)
        var now = DateTime.UtcNow;
        Property property;
        Booking booking;

        await using (var tx = await Db.Database.BeginTransactionAsync())
        {
            if (!await TryLockSlotAsync(office.Id, date))
                return Conflict(new { message = "Many people are booking this day right now. Please try again.", code = "SLOT_BUSY" });

            // Inside the lock, these counts can't change under our feet
            var duplicate = await Db.Bookings.AnyAsync(b => b.ClientId == clientId && b.OfficeId == office.Id
                && b.ScheduledDate == date && OpenStatuses.Contains(b.Status));
            if (duplicate)
                return Conflict(new { message = "You already have an open request with this office on this day.", code = "DUPLICATE_BOOKING" });

            var taken = await Db.Bookings.CountAsync(b => b.OfficeId == office.Id && b.ScheduledDate == date
                && b.Status != BookingStatus.Rejected && b.Status != BookingStatus.Cancelled);
            if (taken >= office.DailyCapacity)
                return Conflict(new { message = "The office is fully booked on this day. Choose another date.", code = "FULLY_BOOKED" });

            property = new Property
            {
                Id = Guid.NewGuid(),
                ClientId = clientId.Value,
                Type = price.PropertyType,             // from the price row, so it always matches the price
                AreaM2 = Math.Round(p.AreaM2, 2),
                Floor = p.Floor,
                Bedrooms = p.Bedrooms,
                Bathrooms = p.Bathrooms,
                BuildingAge = p.BuildingAge,
                HasParking = p.HasParking,
                HasElevator = p.HasElevator,
                Address = p.Address.Trim(),
                Lat = p.Lat is null ? null : Math.Round(p.Lat.Value, 6),
                Lng = p.Lng is null ? null : Math.Round(p.Lng.Value, 6)
            };

            booking = new Booking
            {
                Id = Guid.NewGuid(),
                ClientId = clientId.Value,
                OfficeId = office.Id,
                PropertyId = property.Id,
                ScheduledDate = date,
                Status = BookingStatus.Requested,
                InitialPrice = price.Price,            // snapshot: later price changes don't affect it
                CreatedAt = now
            };

            Db.Properties.Add(property);
            Db.Bookings.Add(booking);
            Db.BookingStatusHistories.Add(new BookingStatusHistory
            {
                Id = Guid.NewGuid(),
                BookingId = booking.Id,
                FromStatus = null,                     // the very first entry
                ToStatus = BookingStatus.Requested,
                ChangedByUserId = clientId.Value,
                ChangedAt = now
            });

            await Db.SaveChangesAsync();
            await tx.CommitAsync();                    // releases the lock
        }                                              // any early return or error rolls back and releases it

        // 7) Tell the office (after saving; a failed email never undoes the booking)
        var emailSent = false;
        try
        {
            await _email.SendAsync(office.OwnerUser.Email, "طلب فحص جديد - تفقّد",
                $"مرحباً {office.OwnerUser.FullName}،{Environment.NewLine}" +
                $"وصلكم طلب فحص جديد من {client.FullName}.{Environment.NewLine}" +
                $"نوع العقار: {price.PropertyType}، الموعد: {date:yyyy-MM-dd}، العنوان: {property.Address}.{Environment.NewLine}" +
                "يرجى مراجعة الطلب من لوحة المكتب.");
            emailSent = true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not email office {OfficeId} about booking {BookingId}", office.Id, booking.Id);
        }

        return StatusCode(StatusCodes.Status201Created, new CreateBookingResponse
        {
            Id = booking.Id,
            Status = booking.Status.ToString(),
            ScheduledDate = booking.ScheduledDate,
            InitialPrice = booking.InitialPrice,
            OfficeName = office.Name,
            PropertyType = price.PropertyType.ToString(),
            SizeBand = string.IsNullOrEmpty(price.SizeBand) ? null : price.SizeBand,
            EmailSent = emailSent,
            Message = "Your request was sent. The office will reply with the final price."
        });
    }

    /// Takes an exclusive SQL Server lock named after the office and the day.
    /// It is held until the current transaction ends. Returns false if it waited too long.
    private async Task<bool> TryLockSlotAsync(Guid officeId, DateOnly date)
    {
        var resource = $"booking-slot:{officeId}:{date:yyyy-MM-dd}";

        var result = (await Db.Database.SqlQuery<int>($@"
            DECLARE @result int;
            EXEC @result = sp_getapplock @Resource = {resource}, @LockMode = 'Exclusive',
                                         @LockOwner = 'Transaction', @LockTimeout = {SlotLockTimeoutMs};
            SELECT @result AS [Value];").ToListAsync()).Single();

        return result >= 0;   // 0 = granted at once, 1 = granted after waiting, negative = timeout or error
    }
}