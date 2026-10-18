using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Tafaqqad.Api.Models.Entities;
using Tafaqqad.Api.Models.Enums;

namespace Tafaqqad.Api.Data;

public static class DbSeeder
{
    public static async Task SeedAsync(AppDbContext db, IConfiguration config)
    {
        // 1. Section types (used by the report score)
        if (!await db.SectionTypes.AnyAsync())
        {
            db.SectionTypes.AddRange(
                new SectionType { Id = Guid.NewGuid(), NameAr = "الحالة الإنشائية", Weight = 30, SortOrder = 1 },
                new SectionType { Id = Guid.NewGuid(), NameAr = "الكهرباء", Weight = 20, SortOrder = 2 },
                new SectionType { Id = Guid.NewGuid(), NameAr = "السباكة", Weight = 15, SortOrder = 3 },
                new SectionType { Id = Guid.NewGuid(), NameAr = "العزل والرطوبة", Weight = 20, SortOrder = 4 },
                new SectionType { Id = Guid.NewGuid(), NameAr = "التشطيبات", Weight = 15, SortOrder = 5 });
            await db.SaveChangesAsync();
        }

        // 2. First admin account (only if no admin exists yet)
        var email = config["Seed:AdminEmail"];
        var password = config["Seed:AdminPassword"];
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            return;   // no secrets configured: skip quietly

        if (!await db.Users.AnyAsync(u => u.Role == UserRole.Admin))
        {
            var admin = new User
            {
                Id = Guid.NewGuid(),
                Role = UserRole.Admin,
                Email = email.Trim().ToLowerInvariant(),
                FullName = "الأدمن",
                Status = UserStatus.Active,
                EmailVerifiedAt = DateTime.UtcNow
            };
            admin.PasswordHash = new PasswordHasher<User>().HashPassword(admin, password);
            db.Users.Add(admin);
            await db.SaveChangesAsync();
        }
    }
}