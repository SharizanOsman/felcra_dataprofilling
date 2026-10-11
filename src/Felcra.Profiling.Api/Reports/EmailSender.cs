using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Felcra.Profiling.Api.Reports;

/// <summary>
/// Outgoing e-mail. With <see cref="PickupDirectory"/> set, messages are written there as .eml files
/// instead of being sent (development and tests); otherwise they go through the SMTP server.
/// </summary>
public sealed class EmailOptions
{
    public const string Section = "Email";

    /// <summary>Sender, e.g. "FELCRA Cattle Profiling &lt;no-reply@felcra.com.my&gt;".</summary>
    public string? From { get; set; }

    public string? PickupDirectory { get; set; }
    public SmtpSettings Smtp { get; set; } = new();

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(From) && (!string.IsNullOrWhiteSpace(PickupDirectory) || !string.IsNullOrWhiteSpace(Smtp.Host));

    public sealed class SmtpSettings
    {
        public string? Host { get; set; }
        public int Port { get; set; } = 587;
        public string? UserName { get; set; }
        public string? Password { get; set; }
    }
}

internal sealed record EmailMessage(string To, string ToName, string Subject, string TextBody, string HtmlBody, IReadOnlyList<ReportFile> Attachments);

internal interface IEmailSender
{
    bool IsConfigured { get; }
    Task SendAsync(EmailMessage message, CancellationToken ct);
}

internal sealed class MailKitEmailSender(IOptions<EmailOptions> options, IHostEnvironment env) : IEmailSender
{
    private readonly EmailOptions _options = options.Value;

    public bool IsConfigured => _options.IsConfigured;

    public async Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        if (!IsConfigured) throw new InvalidOperationException("E-mail is not configured (Email:From and Email:Smtp:Host or Email:PickupDirectory).");

        var mime = new MimeMessage();
        mime.From.Add(MailboxAddress.Parse(_options.From!));
        mime.To.Add(new MailboxAddress(message.ToName, message.To));
        mime.Subject = message.Subject;
        var body = new BodyBuilder { TextBody = message.TextBody, HtmlBody = message.HtmlBody };
        foreach (var file in message.Attachments) body.Attachments.Add(file.FileName, file.Content, ContentType.Parse(file.ContentType));
        mime.Body = body.ToMessageBody();

        if (!string.IsNullOrWhiteSpace(_options.PickupDirectory))
        {
            var directory = Path.GetFullPath(_options.PickupDirectory, env.ContentRootPath);
            Directory.CreateDirectory(directory);
            await mime.WriteToAsync(Path.Combine(directory, $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.eml"), ct);
            return;
        }

        using var smtp = new SmtpClient();
        await smtp.ConnectAsync(_options.Smtp.Host!, _options.Smtp.Port, SecureSocketOptions.Auto, ct);
        if (!string.IsNullOrEmpty(_options.Smtp.UserName))
            await smtp.AuthenticateAsync(_options.Smtp.UserName, _options.Smtp.Password ?? "", ct);
        await smtp.SendAsync(mime, ct);
        await smtp.DisconnectAsync(true, ct);
    }
}
