using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using ServerBridge.LicensingApi.Services;

namespace ServerBridge.LicensingApi.Functions;

/// <summary>Backs the Stripe Payment Link's success_url (.../api/license/issue?session_id={CHECKOUT_SESSION_ID}).
/// The license key is created by <see cref="StripeWebhookFunction"/>; this looks it up and displays it.</summary>
public sealed class StripeIssueLicenseFunction
{
    private readonly ILicenseRepository _repository;

    public StripeIssueLicenseFunction(ILicenseRepository repository) => _repository = repository;

    [Function("StripeIssueLicense")]
    public async Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "license/issue")] HttpRequestData req,
        CancellationToken cancellationToken)
    {
        var sessionId = ParseQueryParam(req.Url.Query, "session_id");
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteStringAsync("Missing session_id.", cancellationToken);
            return bad;
        }

        var licenseKey = await _repository.GetLicenseKeyForSessionAsync(sessionId, cancellationToken);
        var response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "text/html; charset=utf-8");

        var html = licenseKey is null ? BuildPendingPage() : BuildSuccessPage(licenseKey);
        await response.WriteStringAsync(html, cancellationToken);
        return response;
    }

    private static string BuildSuccessPage(string licenseKey) => $"""
        <!DOCTYPE html>
        <html lang="en">
        <head>
          <meta charset="utf-8" />
          <meta name="viewport" content="width=device-width, initial-scale=1" />
          <title>Thank you — ServerBridge</title>
          <link rel="stylesheet" href="/styles.css" />
          <style>
            .key-box {{ background: #1a1f2e; border: 1px solid #3b4a6b; border-radius: 8px;
                        padding: 16px 20px; font-family: monospace; font-size: 1.1rem;
                        color: #a5b4fc; letter-spacing: .04em; word-break: break-all;
                        display: flex; align-items: center; justify-content: space-between; gap: 12px; }}
            .copy-btn {{ background: #5b6af0; color: #fff; border: none; border-radius: 6px;
                         padding: 8px 16px; cursor: pointer; font-size: .85rem; white-space: nowrap; }}
            .copy-btn:hover {{ background: #4a58e0; }}
            .steps {{ margin: 28px 0 0; padding: 0; list-style: none; counter-reset: steps; }}
            .steps li {{ counter-increment: steps; display: flex; align-items: flex-start;
                          gap: 14px; margin-bottom: 14px; }}
            .steps li::before {{ content: counter(steps); background: #5b6af0; color: #fff;
                                   border-radius: 50%; width: 28px; height: 28px; display: flex;
                                   align-items: center; justify-content: center;
                                   font-size: .85rem; flex-shrink: 0; margin-top: 2px; }}
          </style>
        </head>
        <body>
          <header class="wrap topbar">
            <div class="brand"><span class="mark">S</span> ServerBridge</div>
            <a class="home" href="index.html">Home</a>
          </header>
          <main>
            <section class="wrap" style="max-width:640px; padding-top:48px">
              <div class="card" style="padding:36px">
                <div style="font-size:2.5rem; margin-bottom:12px">🎉</div>
                <h2 style="margin-bottom:8px">You're all set!</h2>
                <p style="margin-bottom:24px">Thanks for purchasing ServerBridge Pro. Here's your license key — copy it and keep it somewhere safe.</p>

                <div class="key-box">
                  <span id="key">{System.Net.WebUtility.HtmlEncode(licenseKey)}</span>
                  <button class="copy-btn" onclick="copyKey()">Copy</button>
                </div>

                <ol class="steps" style="margin-top:32px">
                  <li><span>Download and open ServerBridge on your PC or Mac.</span></li>
                  <li><span>In the sidebar, paste your license key into the <strong>License key</strong> field and click <strong>Activate</strong>.</span></li>
                  <li><span>You're ready to start migrating. Questions? Email <a href="mailto:hello@leecrowesoftware.com">hello@leecrowesoftware.com</a>.</span></li>
                </ol>

                <p style="margin-top:32px; font-size:.85rem; color:#6b7280">
                  A receipt has been sent to your email address by Stripe. Your license key is tied to this purchase — keep it safe.
                </p>
              </div>
            </section>
          </main>
          <footer>
            <div class="wrap">
              <span>&copy; <span id="yr"></span> Lee Crowe Software</span>
              <span class="legal-links">
                <a href="terms.html">Terms</a> ·
                <a href="privacy.html">Privacy</a> ·
                <a href="refund.html">Refunds</a>
              </span>
            </div>
          </footer>
          <script>
            document.getElementById('yr').textContent = new Date().getFullYear();
            function copyKey() {{
              navigator.clipboard.writeText('{System.Net.WebUtility.HtmlEncode(licenseKey)}')
                .then(function() {{
                  var btn = document.querySelector('.copy-btn');
                  btn.textContent = 'Copied!';
                  setTimeout(function() {{ btn.textContent = 'Copy'; }}, 2000);
                }});
            }}
          </script>
        </body>
        </html>
        """;

    private static string BuildPendingPage() => """
        <!DOCTYPE html>
        <html lang="en">
        <head>
          <meta charset="utf-8" />
          <meta name="viewport" content="width=device-width, initial-scale=1" />
          <meta http-equiv="refresh" content="4" />
          <title>Finishing setup — ServerBridge</title>
          <link rel="stylesheet" href="/styles.css" />
        </head>
        <body>
          <header class="wrap topbar">
            <div class="brand"><span class="mark">S</span> ServerBridge</div>
          </header>
          <main>
            <section class="wrap" style="max-width:640px; padding-top:48px">
              <div class="card" style="padding:36px; text-align:center">
                <p style="font-size:1.1rem; margin-bottom:8px">Finishing setup&hellip;</p>
                <p style="color:#6b7280">This page will refresh automatically. If it takes more than 30 seconds,
                  <a href="mailto:hello@leecrowesoftware.com">contact support</a>.</p>
              </div>
            </section>
          </main>
        </body>
        </html>
        """;

    private static string? ParseQueryParam(string query, string name)
    {
        var trimmed = query.TrimStart('?');
        foreach (var pair in trimmed.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length == 2 && Uri.UnescapeDataString(parts[0]) == name)
                return Uri.UnescapeDataString(parts[1]);
        }
        return null;
    }
}
