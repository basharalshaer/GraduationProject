using Microsoft.EntityFrameworkCore;
using Tafaqqad.Api.Models.Entities;
using Tafaqqad.Api.Models.Enums;

namespace Tafaqqad.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<AuthToken> AuthTokens => Set<AuthToken>();
    public DbSet<OfficeAccountRequest> OfficeAccountRequests => Set<OfficeAccountRequest>();
    public DbSet<RequestDocument> RequestDocuments => Set<RequestDocument>();
    public DbSet<Office> Offices => Set<Office>();
    public DbSet<OfficeWorkingHour> OfficeWorkingHours => Set<OfficeWorkingHour>();
    public DbSet<OfficePrice> OfficePrices => Set<OfficePrice>();
    public DbSet<Engineer> Engineers => Set<Engineer>();
    public DbSet<Property> Properties => Set<Property>();
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<BookingStatusHistory> BookingStatusHistories => Set<BookingStatusHistory>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<SectionType> SectionTypes => Set<SectionType>();
    public DbSet<InspectionReport> InspectionReports => Set<InspectionReport>();
    public DbSet<ReportSection> ReportSections => Set<ReportSection>();
    public DbSet<ReportPhoto> ReportPhotos => Set<ReportPhoto>();
    public DbSet<Review> Reviews => Set<Review>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void ConfigureConventions(ModelConfigurationBuilder cb)
    {
        // Enums are stored as readable text in SQL Server
        cb.Properties<UserRole>().HaveConversion<string>().HaveMaxLength(30);
        cb.Properties<UserStatus>().HaveConversion<string>().HaveMaxLength(30);
        cb.Properties<TokenType>().HaveConversion<string>().HaveMaxLength(30);
        cb.Properties<RequestStatus>().HaveConversion<string>().HaveMaxLength(30);
        cb.Properties<DocumentType>().HaveConversion<string>().HaveMaxLength(30);
        cb.Properties<OfficeStatus>().HaveConversion<string>().HaveMaxLength(30);
        cb.Properties<EngineerStatus>().HaveConversion<string>().HaveMaxLength(30);
        cb.Properties<PropertyType>().HaveConversion<string>().HaveMaxLength(30);
        cb.Properties<BookingStatus>().HaveConversion<string>().HaveMaxLength(30);
        cb.Properties<PaymentStatus>().HaveConversion<string>().HaveMaxLength(30);
        cb.Properties<ReportStatus>().HaveConversion<string>().HaveMaxLength(30);
        cb.Properties<SectionRating>().HaveConversion<string>().HaveMaxLength(30);

        cb.Properties<decimal>().HavePrecision(18, 2);   // money and areas
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        // Default: no cascade deletes anywhere (specific exceptions are below)
        foreach (var fk in b.Model.GetEntityTypes().SelectMany(e => e.GetForeignKeys()))
            fk.DeleteBehavior = DeleteBehavior.Restrict;

        // ---- Users and tokens ----
        b.Entity<User>(e =>
        {
            e.Property(x => x.Email).HasMaxLength(256);
            e.HasIndex(x => x.Email).IsUnique();
            e.Property(x => x.FullName).HasMaxLength(150);
            e.Property(x => x.Phone).HasMaxLength(30);
        });

        b.Entity<AuthToken>(e =>
        {
            e.Property(x => x.TokenHash).HasMaxLength(128);
            e.HasIndex(x => x.TokenHash);
            e.HasOne(x => x.User).WithMany(u => u.AuthTokens).HasForeignKey(x => x.UserId);
        });

        // ---- Office account requests ----
        b.Entity<OfficeAccountRequest>(e =>
        {
            e.Property(x => x.ReferenceNo).HasMaxLength(30);
            e.HasIndex(x => x.ReferenceNo).IsUnique();
            e.Property(x => x.OfficeName).HasMaxLength(200);
            e.Property(x => x.CommercialRegNo).HasMaxLength(50);
            e.Property(x => x.LicenseNo).HasMaxLength(50);
            e.HasIndex(x => x.LicenseNo);
            e.Property(x => x.City).HasMaxLength(80);
            e.Property(x => x.ContactName).HasMaxLength(150);
            e.Property(x => x.Email).HasMaxLength(256);
            e.HasIndex(x => x.Email);
            e.Property(x => x.Phone).HasMaxLength(30);
            e.HasOne(x => x.ReviewedByUser).WithMany().HasForeignKey(x => x.ReviewedByUserId);
            e.HasOne(x => x.PreviousRequest).WithMany().HasForeignKey(x => x.PreviousRequestId);
        });

        b.Entity<RequestDocument>(e =>
        {
            e.Property(x => x.FileUrl).HasMaxLength(500);
            e.HasOne(x => x.Request).WithMany(r => r.Documents).HasForeignKey(x => x.RequestId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // ---- Offices ----
        b.Entity<Office>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.LicenseNo).HasMaxLength(50);
            e.HasIndex(x => x.LicenseNo).IsUnique();
            e.Property(x => x.City).HasMaxLength(80);
            e.Property(x => x.Phone).HasMaxLength(30);
            e.Property(x => x.LogoUrl).HasMaxLength(500);
            e.Property(x => x.RatingAvg).HasPrecision(3, 2);
            e.HasOne(x => x.OwnerUser).WithOne().HasForeignKey<Office>(x => x.OwnerUserId);
            e.HasIndex(x => x.OwnerUserId).IsUnique();
        });

        b.Entity<OfficeWorkingHour>(e =>
        {
            e.HasIndex(x => new { x.OfficeId, x.Weekday }).IsUnique();
            e.HasOne(x => x.Office).WithMany(o => o.WorkingHours).HasForeignKey(x => x.OfficeId);
        });

        b.Entity<OfficePrice>(e =>
        {
            e.Property(x => x.SizeBand).HasMaxLength(60);
            e.HasOne(x => x.Office).WithMany(o => o.Prices).HasForeignKey(x => x.OfficeId);
        });

        b.Entity<Engineer>(e =>
        {
            e.Property(x => x.Specialty).HasMaxLength(80);
            e.Property(x => x.SyndicateNo).HasMaxLength(50);
            e.HasOne(x => x.User).WithOne().HasForeignKey<Engineer>(x => x.UserId);
            e.HasIndex(x => x.UserId).IsUnique();
            e.HasOne(x => x.Office).WithMany(o => o.Engineers).HasForeignKey(x => x.OfficeId);
        });

        // ---- Properties and bookings ----
        b.Entity<Property>(e =>
        {
            e.Property(x => x.Address).HasMaxLength(300);
            e.Property(x => x.Lat).HasPrecision(9, 6);
            e.Property(x => x.Lng).HasPrecision(9, 6);
            e.HasOne(x => x.Client).WithMany().HasForeignKey(x => x.ClientId);
        });

        b.Entity<Booking>(e =>
        {
            e.Property(x => x.PriceReason).HasMaxLength(500);
            e.Property(x => x.RejectionReason).HasMaxLength(500);
            e.HasIndex(x => new { x.OfficeId, x.ScheduledDate });
            e.HasOne(x => x.Client).WithMany().HasForeignKey(x => x.ClientId);
            e.HasOne(x => x.Office).WithMany().HasForeignKey(x => x.OfficeId);
            e.HasOne(x => x.Property).WithMany(p => p.Bookings).HasForeignKey(x => x.PropertyId);
            e.HasOne(x => x.Engineer).WithMany().HasForeignKey(x => x.EngineerId);
        });

        b.Entity<BookingStatusHistory>(e =>
        {
            e.Property(x => x.Note).HasMaxLength(500);
            e.HasOne(x => x.Booking).WithMany(x => x.StatusHistory).HasForeignKey(x => x.BookingId);
            e.HasOne(x => x.ChangedByUser).WithMany().HasForeignKey(x => x.ChangedByUserId);
        });

        b.Entity<Payment>(e =>
        {
            e.HasOne(x => x.Booking).WithOne(x => x.Payment).HasForeignKey<Payment>(x => x.BookingId);
            e.HasIndex(x => x.BookingId).IsUnique();
        });

        // ---- Reports ----
        b.Entity<SectionType>(e =>
        {
            e.Property(x => x.NameAr).HasMaxLength(100);
            e.Property(x => x.Weight).HasPrecision(5, 2);
            e.HasIndex(x => x.NameAr).IsUnique();
        });

        b.Entity<InspectionReport>(e =>
        {
            e.Property(x => x.ReportNo).HasMaxLength(30);
            e.HasIndex(x => x.ReportNo).IsUnique();
            e.Property(x => x.PdfUrl).HasMaxLength(500);
            e.HasOne(x => x.Booking).WithOne(x => x.Report).HasForeignKey<InspectionReport>(x => x.BookingId);
            e.HasIndex(x => x.BookingId).IsUnique();
            e.HasOne(x => x.Engineer).WithMany().HasForeignKey(x => x.EngineerId);
        });

        b.Entity<ReportSection>(e =>
        {
            e.HasIndex(x => new { x.ReportId, x.SectionTypeId }).IsUnique();
            e.HasOne(x => x.Report).WithMany(r => r.Sections).HasForeignKey(x => x.ReportId)
             .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.SectionType).WithMany().HasForeignKey(x => x.SectionTypeId);
        });

        b.Entity<ReportPhoto>(e =>
        {
            e.Property(x => x.FileUrl).HasMaxLength(500);
            e.Property(x => x.Caption).HasMaxLength(200);
            e.HasOne(x => x.ReportSection).WithMany(s => s.Photos).HasForeignKey(x => x.ReportSectionId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // ---- Reviews and audit ----
        b.Entity<Review>(e =>
        {
            e.ToTable(t => t.HasCheckConstraint("CK_Review_Rating", "[Rating] BETWEEN 1 AND 5"));
            e.HasOne(x => x.Booking).WithOne(x => x.Review).HasForeignKey<Review>(x => x.BookingId);
            e.HasIndex(x => x.BookingId).IsUnique();
            e.HasOne(x => x.Client).WithMany().HasForeignKey(x => x.ClientId);
            e.HasOne(x => x.Office).WithMany().HasForeignKey(x => x.OfficeId);
        });

        b.Entity<AuditLog>(e =>
        {
            e.Property(x => x.Action).HasMaxLength(100);
            e.HasOne(x => x.ActorUser).WithMany().HasForeignKey(x => x.ActorUserId);
            e.HasOne(x => x.Office).WithMany().HasForeignKey(x => x.OfficeId);
        });
    }
}