using BorderLink.Shared.Entities;
using BorderLink.Shared.Enums;
using System.Net.Http.Json;
using System.Text.Json;

namespace BorderLink.Server.Services;

public record MonitorFiringContext(
    MonitorRule Rule,
    Device Device,
    double Value,
    DateTimeOffset FiredAt);

public interface IMonitorNotifier
{
    Task NotifyAsync(MonitorFiringContext context, CancellationToken cancellationToken);
}

/// <summary>
/// Routes a firing to the channel implementation chosen by
/// <see cref="MonitorRule.Channel"/>. Failures are swallowed and logged —
/// we never want a notifier hiccup to wedge the evaluator timer.
/// </summary>
internal class MonitorNotifierDispatcher : IMonitorNotifier
{
    private readonly EmailMonitorNotifier _email;
    private readonly WebhookMonitorNotifier _webhook;
    private readonly ILogger<MonitorNotifierDispatcher> _logger;

    public MonitorNotifierDispatcher(
        EmailMonitorNotifier email,
        WebhookMonitorNotifier webhook,
        ILogger<MonitorNotifierDispatcher> logger)
    {
        _email = email;
        _webhook = webhook;
        _logger = logger;
    }

    public async Task NotifyAsync(MonitorFiringContext context, CancellationToken cancellationToken)
    {
        try
        {
            switch (context.Rule.Channel)
            {
                case MonitorChannel.Email:
                    await _email.NotifyAsync(context, cancellationToken);
                    break;
                case MonitorChannel.Webhook:
                    await _webhook.NotifyAsync(context, cancellationToken);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Monitor notifier failed for rule {ruleId} channel {channel}.",
                context.Rule.Id,
                context.Rule.Channel);
        }
    }
}

internal class EmailMonitorNotifier
{
    private readonly IEmailSenderEx _emailSender;
    private readonly ILogger<EmailMonitorNotifier> _logger;

    public EmailMonitorNotifier(IEmailSenderEx emailSender, ILogger<EmailMonitorNotifier> logger)
    {
        _emailSender = emailSender;
        _logger = logger;
    }

    public async Task NotifyAsync(MonitorFiringContext context, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(context.Rule.ChannelTarget))
        {
            _logger.LogDebug(
                "Email notifier skipped for rule {ruleId}: no channel target.",
                context.Rule.Id);
            return;
        }

        var subject = $"[BorderLink] Monitor alert: {context.Rule.Name}";
        var body =
            $"<p>BorderLink monitor rule <strong>{Escape(context.Rule.Name)}</strong> fired.</p>" +
            $"<ul>" +
            $"<li>Device: {Escape(context.Device.DeviceName ?? context.Device.ID)}</li>" +
            $"<li>Metric: {context.Rule.Metric}</li>" +
            $"<li>Operator: {context.Rule.Operator}</li>" +
            $"<li>Threshold: {context.Rule.Threshold}</li>" +
            $"<li>Observed value: {context.Value:F2}</li>" +
            $"<li>Fired at (UTC): {context.FiredAt.UtcDateTime:yyyy-MM-dd HH:mm:ss}</li>" +
            $"</ul>";

        await _emailSender.SendEmailAsync(
            context.Rule.ChannelTarget,
            subject,
            body,
            context.Rule.OrganizationID);

        _ = cancellationToken;
    }

    private static string Escape(string value) =>
        System.Net.WebUtility.HtmlEncode(value ?? string.Empty);
}

internal class WebhookMonitorNotifier
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<WebhookMonitorNotifier> _logger;

    public WebhookMonitorNotifier(IHttpClientFactory httpFactory, ILogger<WebhookMonitorNotifier> logger)
    {
        _httpFactory = httpFactory;
        _logger = logger;
    }

    public async Task NotifyAsync(MonitorFiringContext context, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(context.Rule.ChannelTarget) ||
            !Uri.TryCreate(context.Rule.ChannelTarget, UriKind.Absolute, out var url))
        {
            _logger.LogDebug(
                "Webhook notifier skipped for rule {ruleId}: invalid channel target.",
                context.Rule.Id);
            return;
        }

        var payload = new
        {
            ruleName = context.Rule.Name,
            ruleId = context.Rule.Id,
            deviceName = context.Device.DeviceName,
            deviceId = context.Device.ID,
            metric = context.Rule.Metric.ToString(),
            op = context.Rule.Operator.ToString(),
            value = context.Value,
            threshold = context.Rule.Threshold,
            firedAt = context.FiredAt,
        };

        using var client = _httpFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(10);

        using var response = await client.PostAsJsonAsync(url, payload, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogDebug(
                "Webhook target {url} returned {status} for rule {ruleId}.",
                url,
                (int)response.StatusCode,
                context.Rule.Id);
        }
    }
}
