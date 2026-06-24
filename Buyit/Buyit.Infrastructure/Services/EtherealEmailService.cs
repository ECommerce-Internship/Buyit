using Buyit.Application.Common;
using Buyit.Application.DTOs;
using Buyit.Application.Interfaces;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Buyit.Infrastructure.Services;

// Dev email service using Ethereal SMTP — emails are caught and visible at ethereal.email/messages
public class EtherealEmailService : IEmailService
{
    private readonly EtherealSettings _settings;
    private readonly ILogger<EtherealEmailService> _logger;

    public EtherealEmailService(
        IOptions<EtherealSettings> settings,
        ILogger<EtherealEmailService> logger)
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

    public async Task SendLowStockAlertAsync(LowStockMessage message)
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

        // Alert goes to the sender address (store admin inbox on Ethereal)
        await SendAsync(_settings.Username, subject, html);
    }

    private async Task SendAsync(string toEmail, string subject, string htmlContent)
    {
        try
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress("Buyit Store", _settings.Username));
            message.To.Add(new MailboxAddress(toEmail, toEmail));
            message.Subject = subject;
            message.Body = new TextPart("html") { Text = htmlContent };

            using var client = new SmtpClient();
            await client.ConnectAsync(_settings.Host, _settings.Port, SecureSocketOptions.StartTls);
            await client.AuthenticateAsync(_settings.Username, _settings.Password);
            await client.SendAsync(message);
            await client.DisconnectAsync(true);

            _logger.LogInformation(
                "Email sent via Ethereal to {Email} — Subject: {Subject}", toEmail, subject);
        }
        catch (Exception ex)
        {
            // Fail-open: email failure must never crash the worker or the order flow
            _logger.LogError(ex,
                "Failed to send email via Ethereal to {Email} — Subject: {Subject}", toEmail, subject);
        }
    }
}