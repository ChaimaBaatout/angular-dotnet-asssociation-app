using System.Net;
using System.Net.Mail;
using System.Threading.Channels;

namespace UserAdmin.Api.Email;

public record EmailMessage(string To, string Subject, string Body);

public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}

/// <summary>Development only: prints emails to the API console so their links can be used without a mail server.</summary>
public class ConsoleEmailSender : IEmailSender
{
    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        Console.WriteLine();
        Console.WriteLine($"=== Email to {message.To}: {message.Subject} ===");
        Console.WriteLine(message.Body);
        Console.WriteLine();
        return Task.CompletedTask;
    }
}

public class SmtpEmailSender(IConfiguration config) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        using var client = new SmtpClient(config["Email:SmtpHost"], config.GetValue("Email:SmtpPort", 587))
        {
            EnableSsl = config.GetValue("Email:EnableSsl", true),
        };
        var username = config["Email:Username"];
        if (!string.IsNullOrEmpty(username))
            client.Credentials = new NetworkCredential(username, config["Email:Password"]);

        using var mail = new MailMessage(config["Email:From"]!, message.To, message.Subject, message.Body);
        await client.SendMailAsync(mail, cancellationToken);
    }
}

/// <summary>
/// Emails are sent in the background so that a request takes the same time
/// whether or not it sends one, which would otherwise reveal which addresses have accounts.
/// </summary>
public class EmailQueue
{
    private readonly Channel<EmailMessage> _channel = Channel.CreateBounded<EmailMessage>(1000);

    public ChannelReader<EmailMessage> Reader => _channel.Reader;

    public void Enqueue(EmailMessage message) => _channel.Writer.TryWrite(message);
}

public class EmailDispatcher(EmailQueue queue, IEmailSender sender, ILogger<EmailDispatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var message in queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await sender.SendAsync(message, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Could not send the email \"{Subject}\".", message.Subject);
            }
        }
    }
}

/// <summary>The account emails. Links use the configured address, never the request's Host header, which callers control.</summary>
public class AccountMailer(EmailQueue queue, IConfiguration config)
{
    private string SetPasswordLink(string token) =>
        $"{config["App:BaseUrl"]!.TrimEnd('/')}/reset-password#token={token}";

    public void SendFinishSignUp(string email, string token) => queue.Enqueue(new EmailMessage(
        email,
        "Finish creating your Admin Portal account",
        $"""
        Choose a password to finish creating your account:

        {SetPasswordLink(token)}

        This link expires in 24 hours. If you did not sign up, you can ignore this email.
        """));

    public void SendPasswordReset(string email, string token) => queue.Enqueue(new EmailMessage(
        email,
        "Reset your Admin Portal password",
        $"""
        Choose a new password:

        {SetPasswordLink(token)}

        This link expires in 1 hour. If you did not ask for this, you can ignore this email and your password will not change.
        """));

    public void SendAlreadyRegistered(string email, string token) => queue.Enqueue(new EmailMessage(
        email,
        "You already have an Admin Portal account",
        $"""
        Someone tried to sign up with this email, but it already has an account. You can sign in as usual.

        If you have forgotten your password, choose a new one here:

        {SetPasswordLink(token)}

        This link expires in 1 hour. If this was not you, you can ignore this email.
        """));
}
