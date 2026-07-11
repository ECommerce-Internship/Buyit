using Buyit.Application.Common;
using Buyit.Application.DTOs;
using Buyit.Application.Interfaces;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Gmail.v1;
using Google.Apis.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Buyit.Infrastructure.Services;

// Real Gmail API implementation — sends via Google's HTTPS API (not raw SMTP), so unlike
// EtherealEmailService this isn't blocked by Render's outbound SMTP port restrictions.
public class GmailApiEmailService : IEmailService
{
    private readonly GmailApiSettings _settings;
    private readonly ILogger<GmailApiEmailService> _logger;

    public GmailApiEmailService(
        IOptions<GmailApiSettings> settings,
        ILogger<GmailApiEmailService> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task SendOrderConfirmationAsync(int orderId, string userEmail, decimal totalAmount)
    {
        var subject = $"Order #{orderId} Confirmed — Buyit";
        var html = $"""
            <h2>Thank you for your order!</h2>
            <p>Your order <strong>#{orderId}</strong> has been placed successfully.</p>
            <p>Total amount: <strong>${totalAmount:F2}</strong></p>
            <p>We will notify you when your order ships.</p>
            <br/>
            <p>The Buyit Team</p>
            """;

        await SendAsync(userEmail, subject, html);
    }

    public async Task SendLowStockAlertAsync(LowStockMessage message, string recipientEmail)
    {
        var subject = $"Low Stock Alert — {message.ProductName}";
        var html = $"""
            <h2>Low Stock Alert</h2>
            <p>Product <strong>{message.ProductName}</strong> (ID: {message.ProductId}) is running low.</p>
            <p>Current quantity: <strong>{message.Quantity}</strong></p>
            <p>Threshold: <strong>{message.Threshold}</strong></p>
            <p>Triggered at: {message.TriggeredAt:yyyy-MM-dd HH:mm:ss} UTC</p>
            <br/>
            <p>Please restock as soon as possible.</p>
            """;

        await SendAsync(recipientEmail, subject, html);
    }

    public async Task SendPasswordResetCodeAsync(string recipientEmail, string code)
    {
        var subject = "Reset your Buyit password";
        var html = $"""
            <h2>Reset your password</h2>
            <p>Use the code below to reset your Buyit password. It expires in 15 minutes and can only be used once.</p>
            <p style="font-size: 28px; font-weight: bold; letter-spacing: 4px;">{code}</p>
            <p>If you didn't request this, you can safely ignore this email — your password will not change.</p>
            <br/>
            <p>The Buyit Team</p>
            """;

        await SendAsync(recipientEmail, subject, html);
    }

    private async Task SendAsync(string toEmail, string subject, string htmlContent)
    {
        try
        {
            var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
            {
                ClientSecrets = new ClientSecrets
                {
                    ClientId = _settings.ClientId,
                    ClientSecret = _settings.ClientSecret
                },
                Scopes = new[] { GmailService.Scope.GmailSend }
            });

            var token = new TokenResponse { RefreshToken = _settings.RefreshToken };
            var credential = new UserCredential(flow, "buyit-mailer", token);

            using var gmailService = new GmailService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = "Buyit"
            });

            var mimeMessage = new MimeMessage();
            mimeMessage.From.Add(new MailboxAddress(_settings.SenderName, _settings.SenderEmail));
            mimeMessage.To.Add(new MailboxAddress(toEmail, toEmail));
            mimeMessage.Subject = subject;
            mimeMessage.Body = new TextPart("html") { Text = htmlContent };

            using var stream = new MemoryStream();
            await mimeMessage.WriteToAsync(stream);
            var rawMessage = Convert.ToBase64String(stream.ToArray())
                .Replace('+', '-')
                .Replace('/', '_')
                .Replace("=", "");

            var gmailMessage = new Google.Apis.Gmail.v1.Data.Message { Raw = rawMessage };
            await gmailService.Users.Messages.Send(gmailMessage, "me").ExecuteAsync();

            _logger.LogInformation("Email sent via Gmail API to {Email} — Subject: {Subject}", toEmail, subject);
        }
        catch (Exception ex)
        {
            // Fail-open: email failure must never crash the worker or the order flow
            _logger.LogError(ex, "Failed to send email via Gmail API to {Email} — Subject: {Subject}", toEmail, subject);
        }
    }
}
