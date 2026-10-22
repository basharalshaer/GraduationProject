namespace Tafaqqad.Api.Services;

public interface IAppEmailSender
{
    Task SendAsync(string to, string subject, string body);
}

/// Development sender: prints the email to the console instead of sending it.
public class ConsoleEmailSender : IAppEmailSender
{
    private readonly ILogger<ConsoleEmailSender> _log;
    public ConsoleEmailSender(ILogger<ConsoleEmailSender> log) => _log = log;

    public Task SendAsync(string to, string subject, string body)
    {
        _log.LogInformation("EMAIL to {To} | {Subject}{NewLine}{Body}", to, subject, Environment.NewLine, body);
        return Task.CompletedTask;
    }
}