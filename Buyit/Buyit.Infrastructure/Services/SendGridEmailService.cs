using Buyit.Application.Common;
using Buyit.Application.DTOs;
using Buyit.Application.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SendGrid;
using SendGrid.Helpers.Mail;

namespace Buyit.Infrastructure.Services;

// Real SendGrid implementation — used when ApiKey is configured
public class SendGridEmailService : IEmailService
{
    private readonly SendGridSettings _settings;
    private readonly ILogger<SendGridEmailService> _logger;

    public SendGridEmailService(
        IOptions<SendGridSettings> settings,
        ILogger<SendGridEmailService> logger)
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

    private async Task SendAsync(string toEmail, string subject, string htmlContent)
    {
        try
        {
            var client = new SendGridClient(_settings.ApiKey);
            var from = new EmailAddress(_settings.FromEmail, _settings.FromName);
            var to = new EmailAddress(toEmail);
            var plainText = System.Text.RegularExpressions.Regex.Replace(htmlContent, "<.*?>", string.Empty);
            var msg = MailHelper.CreateSingleEmail(from, to, subject, plainText, htmlContent);

            var response = await client.SendEmailAsync(msg);

            if (response.IsSuccessStatusCode)
                _logger.LogInformation("Email sent to {Email} — Subject: {Subject}", toEmail, subject);
            else
                _logger.LogWarning("SendGrid returned {StatusCode} for email to {Email}", response.StatusCode, toEmail);
        }
        catch (Exception ex)
        {
            // Fail-open: email failure must never crash the worker or the order flow
            _logger.LogError(ex, "Failed to send email to {Email} — Subject: {Subject}", toEmail, subject);
        }
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
}