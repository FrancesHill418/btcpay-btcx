using System.Globalization;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Client;
using BTCPayServer.Plugins.BTCX.Rates;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using AuthenticationSchemes = BTCPayServer.Abstractions.Constants.AuthenticationSchemes;

namespace BTCPayServer.Plugins.BTCX.Controllers;

[Area(Plugin.Area)]
[Route("server/btcx")]
[Authorize(Policy = Policies.CanModifyServerSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
public sealed class BtcxSettingsController(
    ISettingsRepository settingsRepository,
    ManualRateSettingsService settingsService,
    ILogger<BtcxSettingsController> logger) : Controller
{
    private readonly ISettingsRepository _settingsRepository = settingsRepository;
    private readonly ManualRateSettingsService _settingsService = settingsService;
    private readonly ILogger<BtcxSettingsController> _logger = logger;

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var settings = await _settingsService.GetAsync();
        return View("Index", new BtcxSettingsViewModel(settings.Enabled, FormatRate(settings.BtcxCnyRate), settings.Source, settings.UpdatedAt));
    }

    [HttpPost("save")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(bool enabled, string? btcxCnyRate)
    {
        decimal? rate = null;
        if (!string.IsNullOrWhiteSpace(btcxCnyRate))
        {
            if (!ManualRateSettingsService.TryParseRate(btcxCnyRate, out var parsed))
            {
                ModelState.AddModelError(nameof(btcxCnyRate), "Enter a positive decimal rate with no more than eight meaningful fractional digits.");
            }
            else
            {
                rate = parsed;
            }
        }
        else if (enabled)
        {
            ModelState.AddModelError(nameof(btcxCnyRate), "A rate is required when BTCX is enabled.");
        }

        if (enabled && rate is null)
            ModelState.AddModelError(nameof(enabled), "BTCX cannot be enabled without a valid BTCX/CNY rate.");

        if (!ModelState.IsValid)
        {
            var current = await _settingsService.GetAsync();
            return View("Index", new BtcxSettingsViewModel(enabled, btcxCnyRate ?? string.Empty, current.Source, current.UpdatedAt));
        }

        var previous = await _settingsService.GetAsync();
        var updated = await _settingsService.SaveAsync(enabled, rate);
        _logger.LogInformation(
            "BTCX manual rate settings changed by {Actor}: enabled {OldEnabled}->{NewEnabled}, rate {OldRate}->{NewRate}, updatedAt {UpdatedAt}",
            User.Identity?.Name ?? "unknown", previous.Enabled, updated.Enabled, previous.BtcxCnyRate, updated.BtcxCnyRate, updated.UpdatedAt);

        TempData["StatusMessage"] = "BTCX manual rate settings saved. New invoices will use this setting; existing invoices keep their saved quote.";
        return RedirectToAction(nameof(Index));
    }

    private static string FormatRate(decimal? rate) => rate?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
}

public sealed record BtcxSettingsViewModel(bool Enabled, string BtcxCnyRate, string Source, DateTimeOffset? UpdatedAt);
