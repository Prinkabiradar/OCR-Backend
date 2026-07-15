using OCR_BACKEND.Modals;
using System.Net.Http.Headers;
using System.Text.Json;

namespace OCR_BACKEND.Services
{
    public interface IProductivityReportService
    {
        Task SendDailyReportAsync(DateOnly reportDate, CancellationToken cancellationToken);
        Task SendReportEmailAsync(
            DateOnly reportDate,
            IEnumerable<string> recipients,
            IEnumerable<string> ccRecipients,
            CancellationToken cancellationToken);
    }

    public class ProductivityReportService : IProductivityReportService
    {
        private readonly UserSessionDBHelper _db;
        private readonly IEmailService _emailService;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _config;
        private readonly ILogger<ProductivityReportService> _logger;

        public ProductivityReportService(
            UserSessionDBHelper db,
            IEmailService emailService,
            IHttpClientFactory httpClientFactory,
            IConfiguration config,
            ILogger<ProductivityReportService> logger)
        {
            _db = db;
            _emailService = emailService;
            _httpClientFactory = httpClientFactory;
            _config = config;
            _logger = logger;
        }

        public async Task SendDailyReportAsync(DateOnly reportDate, CancellationToken cancellationToken)
        {
            var emails = _config.GetSection("ProductivityReport:EmailRecipients").Get<string[]>() ?? Array.Empty<string>();
            var ccEmails = _config.GetSection("ProductivityReport:EmailCcRecipients").Get<string[]>() ?? Array.Empty<string>();

            await SendReportEmailAsync(reportDate, emails, ccEmails, cancellationToken);
        }

        public async Task SendReportEmailAsync(
            DateOnly reportDate,
            IEnumerable<string> recipients,
            IEnumerable<string> ccRecipients,
            CancellationToken cancellationToken)
        {
            var statusIds = _config.GetSection("ProductivityReport:CompletedStatusIds").Get<int[]>() ?? Array.Empty<int>();
            var report = await _db.GetProductivityReportAsync(reportDate, statusIds);
            var subject = $"OCR Productivity Summary - {reportDate:dd MMM yyyy}";
            var html = BuildHtmlReport(report);
            var text = BuildTextReport(report);

            var ccEmails = ccRecipients.Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
            foreach (var email in recipients.Where(x => !string.IsNullOrWhiteSpace(x)))
            {
                await _emailService.SendEmailAsync(email, subject, html, ccEmails);
            }

            if (_config.GetValue<bool>("ProductivityReport:WhatsApp:Enabled"))
                await SendWhatsAppAsync(report, text, cancellationToken);

            _logger.LogInformation("Daily productivity report sent for {ReportDate}", reportDate);
        }

        private async Task SendWhatsAppAsync(ProductivityReport report, string body, CancellationToken cancellationToken)
        {
            var phoneNumberId = _config["ProductivityReport:WhatsApp:PhoneNumberId"];
            var accessToken = _config["ProductivityReport:WhatsApp:AccessToken"];
            var templateName = _config["ProductivityReport:WhatsApp:TemplateName"];
            var languageCode = _config["ProductivityReport:WhatsApp:LanguageCode"] ?? "en";
            var recipients = _config.GetSection("ProductivityReport:WhatsApp:Recipients").Get<string[]>() ?? Array.Empty<string>();

            if (string.IsNullOrWhiteSpace(phoneNumberId) ||
                string.IsNullOrWhiteSpace(accessToken) ||
                string.IsNullOrWhiteSpace(templateName))
            {
                _logger.LogWarning("WhatsApp report is enabled, but Meta WhatsApp settings are incomplete.");
                return;
            }

            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            var endpoint = $"https://graph.facebook.com/v23.0/{phoneNumberId}/messages";
            var parameters = BuildWhatsAppTemplateParameters(report, body);

            foreach (var recipient in recipients.Where(x => !string.IsNullOrWhiteSpace(x)))
            {
                var payload = new
                {
                    messaging_product = "whatsapp",
                    to = NormalizeWhatsAppRecipient(recipient),
                    type = "template",
                    template = new
                    {
                        name = templateName,
                        language = new
                        {
                            code = languageCode
                        },
                        components = new[]
                        {
                            new
                            {
                                type = "body",
                                parameters
                            }
                        }
                    }
                };

                using var content = new StringContent(
                    JsonSerializer.Serialize(payload),
                    System.Text.Encoding.UTF8,
                    "application/json");

                var response = await client.PostAsync(endpoint, content, cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    var error = await response.Content.ReadAsStringAsync(cancellationToken);
                    _logger.LogWarning(
                        "WhatsApp template report failed for {Recipient}: {StatusCode}. {Error}",
                        recipient,
                        response.StatusCode,
                        error);
                }
            }
        }

        private object[] BuildWhatsAppTemplateParameters(ProductivityReport report, string body)
        {
            var parameterMode = _config["ProductivityReport:WhatsApp:TemplateParameterMode"] ?? "FullReport";
            if (parameterMode.Equals("Summary", StringComparison.OrdinalIgnoreCase))
            {
                return new[]
                {
                    CreateTextParameter(report.ReportDate.ToString("dd MMM yyyy")),
                    CreateTextParameter(report.TotalDocumentsProcessedToday.ToString()),
                    CreateTextParameter(report.TotalPagesCompletedToday.ToString()),
                    CreateTextParameter(report.TotalDocumentsProcessedOverall.ToString()),
                    CreateTextParameter(report.TotalPagesCompletedOverall.ToString()),
                    CreateTextParameter(BuildWhatsAppUserDetails(report))
                };
            }

            return new[]
            {
                CreateTextParameter(body.Length > 3000 ? body[..3000] : body)
            };
        }

        private static object CreateTextParameter(string text)
        {
            return new
            {
                type = "text",
                text
            };
        }

        private static string BuildWhatsAppUserDetails(ProductivityReport report)
        {
            var userLines = report.Users.Select(user =>
                $"{user.UserName}: Login {FormatTime(user.FirstLoginTime)}, Logout {FormatTime(user.LastLogoutTime)}, Today {user.DocumentsProcessedToday} docs/{user.PagesCompletedToday} pages");

            var details = string.Join(Environment.NewLine, userLines);
            return details.Length > 2000 ? details[..2000] : details;
        }

        private static string NormalizeWhatsAppRecipient(string recipient)
        {
            return recipient
                .Replace("whatsapp:", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("+", string.Empty)
                .Replace(" ", string.Empty)
                .Replace("-", string.Empty);
        }

        private static string BuildTextReport(ProductivityReport report)
        {
            var lines = new List<string>
            {
                $"OCR Productivity Summary - {report.ReportDate:dd MMM yyyy}",
                $"Today: {report.TotalDocumentsProcessedToday} documents, {report.TotalPagesCompletedToday} pages/images",
                $"Overall: {report.TotalDocumentsProcessedOverall} documents, {report.TotalPagesCompletedOverall} pages/images",
                ""
            };

            lines.AddRange(report.Users.Select(user =>
                $"{user.UserName}: Login {FormatTime(user.FirstLoginTime)}, Logout {FormatTime(user.LastLogoutTime)}, Today {user.DocumentsProcessedToday} docs/{user.PagesCompletedToday} pages, Overall {user.DocumentsProcessedOverall} docs/{user.PagesCompletedOverall} pages"));

            return string.Join(Environment.NewLine, lines);
        }

        private static string BuildHtmlReport(ProductivityReport report)
        {
            var rows = string.Join("", report.Users.Select(user => $@"
                <tr>
                    <td>{Escape(user.UserName)}</td>
                    <td>{FormatTime(user.FirstLoginTime)}</td>
                    <td>{FormatTime(user.LastLogoutTime)}</td>
                    <td>{user.DocumentsProcessedToday}</td>
                    <td>{user.PagesCompletedToday}</td>
                    <td>{user.DocumentsProcessedOverall}</td>
                    <td>{user.PagesCompletedOverall}</td>
                </tr>"));

            return $@"
                <div style='font-family:Arial,sans-serif;max-width:900px;'>
                    <h2>OCR Productivity Summary - {report.ReportDate:dd MMM yyyy}</h2>
                    <p><strong>Today:</strong> {report.TotalDocumentsProcessedToday} documents, {report.TotalPagesCompletedToday} pages/images completed</p>
                    <p><strong>Overall:</strong> {report.TotalDocumentsProcessedOverall} documents, {report.TotalPagesCompletedOverall} pages/images completed</p>
                    <table cellpadding='8' cellspacing='0' border='1' style='border-collapse:collapse;width:100%;'>
                        <thead>
                            <tr style='background:#f4f4f4;'>
                                <th align='left'>User</th>
                                <th align='left'>Login</th>
                                <th align='left'>Logout</th>
                                <th align='right'>Docs Today</th>
                                <th align='right'>Pages Today</th>
                                <th align='right'>Docs Overall</th>
                                <th align='right'>Pages Overall</th>
                            </tr>
                        </thead>
                        <tbody>{rows}</tbody>
                    </table>
                </div>";
        }

        private static string FormatTime(DateTime? value)
        {
            return value.HasValue ? value.Value.ToString("hh:mm tt") : "Not recorded";
        }

        private static string Escape(string value)
        {
            return System.Net.WebUtility.HtmlEncode(value);
        }
    }
}
