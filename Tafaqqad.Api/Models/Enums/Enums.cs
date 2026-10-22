namespace Tafaqqad.Api.Models.Enums;

public enum UserRole { Client, Office, Engineer, Admin }
public enum UserStatus { Active, PendingActivation, Suspended }
public enum TokenType { EmailVerify, SetPassword, ResetPassword, EmailOtp }
public enum RequestStatus { Pending, InfoRequested, Approved, Rejected }
public enum DocumentType { License, CommercialReg, Logo }
public enum OfficeStatus { Incomplete, Active, Suspended }
public enum EngineerStatus { Active, Deactivated }
public enum BookingStatus
{
    Requested, Accepted, PriceProposed, PriceApproved,
    Paid, InProgress, Completed, Rejected, Cancelled
}
public enum PaymentStatus { Pending, Paid, Released, Refunded }
public enum ReportStatus { Draft, Issued }
public enum SectionRating { Ok, NeedsMaintenance, Bad }
public enum PropertyType { Apartment, Villa, Commercial, Land }