using MailKit.Net.Smtp;
using MailKit.Security;
using MatPhone.Configuration;
using MatPhone.Data;
using MatPhone.Data.Entities;
using MatPhone.Localization;
using Microsoft.EntityFrameworkCore;
using MimeKit;

namespace MatPhone.Services;

/// <summary>
/// Sends the mails of the mailboxes (a new message, with the recording as WAV when the mailbox wants it) and the test mail of the settings, over the configured
/// SMTP server. A mail that fails is written into the telephony log; the message itself stays in the mailbox.
/// </summary>
public sealed class MailService(MpDbContext db, ConfigStore config, SecretProtector secrets, ILogger<MailService> logger)
{
    public bool IsConfigured => config.Current.Mail.IsConfigured;

    /// <summary>A test mail to <paramref name="to"/> with the current settings; null when it went out.</summary>
    public async Task<string?> SendTestAsync(string to, CancellationToken ct = default)
    {
        try
        {
            var message = NewMessage([to], Text.T("Mail.TestSubject"), Text.T("Mail.TestBody"));
            await DeliverAsync(message, ct);
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Describe(ex);
        }
    }

    /// <summary>The mail of a new message: to the address of the mailbox, else to its owner (a group mailbox without an address sends none).</summary>
    public async Task SendVoicemailAsync(long mailboxId, MailboxMessage message, byte[] wav, CancellationToken ct = default)
    {
        var box = await db.Mailbox.AsNoTracking().Include(m => m.User).FirstOrDefaultAsync(m => m.Id == mailboxId, ct);
        if (box is null || !box.MailNotify || !IsConfigured)
        {
            return;
        }

        var to = !string.IsNullOrWhiteSpace(box.MailAddress) ? box.MailAddress : box.User?.Email;
        if (string.IsNullOrWhiteSpace(to))
        {
            return;
        }

        var settings = config.Current.Mail;
        var caller = string.IsNullOrWhiteSpace(message.CallerName) ? message.CallerNumber ?? Text.T("Mail.UnknownCaller") : $"{message.CallerName} ({message.CallerNumber})";
        string Fill(string template) => template
            .Replace("{Mailbox}", box.Name, StringComparison.Ordinal)
            .Replace("{Caller}", caller, StringComparison.Ordinal)
            .Replace("{Date}", Fmt.Moment(message.ReceivedDate), StringComparison.Ordinal)
            .Replace("{Duration}", Text.T("Mail.Seconds", message.DurationSeconds), StringComparison.Ordinal)
            .Replace("{Company}", config.Current.Company.Name, StringComparison.Ordinal);

        try
        {
            var mail = NewMessage(Split(to), Fill(settings.VoicemailSubject), Fill(settings.VoicemailBody));
            if (box.MailAttachAudio)
            {
                var multipart = new Multipart("mixed") { mail.Body! };
                multipart.Add(new MimePart("audio", "wav")
                {
                    Content = new MimeContent(new MemoryStream(wav)),
                    ContentDisposition = new ContentDisposition(ContentDisposition.Attachment),
                    ContentTransferEncoding = ContentEncoding.Base64,
                    FileName = $"Nachricht_{message.ReceivedDate:yyyyMMdd_HHmmss}.wav",
                });
                mail.Body = multipart;
            }

            await DeliverAsync(mail, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "The mail of a new message in the mailbox {Mailbox} could not be sent: {Reason}", box.Name, Describe(ex));
        }
    }

    private MimeMessage NewMessage(IEnumerable<string> to, string subject, string text)
    {
        var settings = config.Current.Mail;
        var message = new MimeMessage();
        var from = string.IsNullOrWhiteSpace(settings.FromName) ? (string.IsNullOrWhiteSpace(config.Current.Company.Name) ? "MatPhone" : config.Current.Company.Name) : settings.FromName;
        message.From.Add(new MailboxAddress(from, settings.FromAddress));
        foreach (var address in to)
        {
            message.To.Add(MailboxAddress.Parse(address));
        }

        message.Subject = subject;
        message.Body = new TextPart("plain") { Text = text };
        return message;
    }

    private async Task DeliverAsync(MimeMessage message, CancellationToken ct)
    {
        var settings = config.Current.Mail;
        if (!settings.IsConfigured)
        {
            throw new InvalidOperationException(Text.T("Mail.NotConfigured"));
        }

        using var client = new SmtpClient { Timeout = 30_000 };
        var security = settings.Security switch
        {
            MailSecurity.StartTls => SecureSocketOptions.StartTls,
            MailSecurity.SslOnConnect => SecureSocketOptions.SslOnConnect,
            MailSecurity.None => SecureSocketOptions.None,
            _ => SecureSocketOptions.Auto,
        };
        await client.ConnectAsync(settings.Host, settings.Port, security, ct);
        if (!string.IsNullOrWhiteSpace(settings.UserName))
        {
            await client.AuthenticateAsync(settings.UserName, secrets.Unprotect(settings.PasswordProtected), ct);
        }

        await client.SendAsync(message, ct);
        await client.DisconnectAsync(true, ct);
    }

    private static IEnumerable<string> Split(string? addresses)
        => (addresses ?? "").Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>The reason in words a person can act on, not a stack trace.</summary>
    public static string Describe(Exception ex) => ex switch
    {
        AuthenticationException => Text.T("Mail.AuthFailed"),
        SmtpCommandException smtp => Text.T("Mail.Refused", smtp.Message),
        SmtpProtocolException => Text.T("Mail.Unexpected"),
        System.Net.Sockets.SocketException => Text.T("Mail.Unreachable"),
        SslHandshakeException => Text.T("Mail.TlsFailed"),
        ParseException => Text.T("Mail.InvalidAddress"),
        TimeoutException or OperationCanceledException => Text.T("Mail.Timeout"),
        _ => ex.Message,
    };
}
