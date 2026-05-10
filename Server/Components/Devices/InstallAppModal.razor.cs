using BorderLink.Server.Enums;
using BorderLink.Server.Hubs;
using BorderLink.Server.Services;
using BorderLink.Shared;
using BorderLink.Shared.Entities;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.WebUtilities;
using System.Net.Http.Json;

namespace BorderLink.Server.Components.Devices;

public partial class InstallAppModal : ComponentBase, IDisposable
{
    private static readonly TimeSpan _searchDebounce = TimeSpan.FromMilliseconds(400);

    private CancellationTokenSource? _debounceCts;
    private string? _deviceName;
    private string? _selectedSource;
    private string _searchTerm = string.Empty;
    private string? _lastSearched;
    private List<SoftwarePackage> _results = new();
    private string[] _availableSources = Array.Empty<string>();
    private bool _isSearching;
    private bool _isInstalling;
    private string? _errorMessage;

    [Parameter]
    public bool IsOpen { get; set; }

    [Parameter]
    public Device? Device { get; set; }

    [Parameter]
    public EventCallback OnClose { get; set; }

    [Parameter]
    public EventCallback<int> OnInstallQueued { get; set; }

    [Inject]
    private ICircuitConnection CircuitConnection { get; set; } = null!;

    [Inject]
    private IHttpClientFactory HttpClientFactory { get; set; } = null!;

    [Inject]
    private NavigationManager NavManager { get; set; } = null!;

    [Inject]
    private IToastService ToastService { get; set; } = null!;

    [Inject]
    private ILogger<InstallAppModal> Logger { get; set; } = null!;

    protected override void OnParametersSet()
    {
        _deviceName = Device?.DeviceName ?? Device?.Alias;
        _availableSources = ResolveSourcesForPlatform(Device?.Platform);
        if (_selectedSource is null || !_availableSources.Contains(_selectedSource))
        {
            _selectedSource = _availableSources.FirstOrDefault();
        }
    }

    private async Task OnBackdropClick()
    {
        await Close();
    }

    private async Task Close()
    {
        _debounceCts?.Cancel();
        _searchTerm = string.Empty;
        _lastSearched = null;
        _results.Clear();
        _errorMessage = null;
        _isSearching = false;
        if (OnClose.HasDelegate)
        {
            await OnClose.InvokeAsync();
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        // Debounced search: triggered each render after the user changes
        // the search box, kept simple via cancellation.
        if (!IsOpen)
        {
            return;
        }

        var capturedTerm = _searchTerm;
        if (string.Equals(capturedTerm, _lastSearched, StringComparison.Ordinal))
        {
            return;
        }

        _debounceCts?.Cancel();
        var cts = new CancellationTokenSource();
        _debounceCts = cts;

        try
        {
            await Task.Delay(_searchDebounce, cts.Token);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        if (cts.IsCancellationRequested || !IsOpen)
        {
            return;
        }

        await RunSearch(capturedTerm, cts.Token);
    }

    private async Task RunSearch(string term, CancellationToken cancellationToken)
    {
        if (Device is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(term) || term.Trim().Length < 2)
        {
            _lastSearched = term;
            _results = new List<SoftwarePackage>();
            _errorMessage = null;
            await InvokeAsync(StateHasChanged);
            return;
        }

        _isSearching = true;
        _errorMessage = null;
        await InvokeAsync(StateHasChanged);

        try
        {
            var baseUri = NavManager.BaseUri.TrimEnd('/');
            var query = QueryHelpers.AddQueryString(
                $"{baseUri}/api/software-actions/search",
                new Dictionary<string, string?>
                {
                    ["deviceId"] = Device.ID,
                    ["q"] = term.Trim(),
                    ["max"] = "50",
                });

            using var client = HttpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(70);

            using var response = await client.GetAsync(query, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _errorMessage = $"Search failed ({(int)response.StatusCode}).";
                _results = new List<SoftwarePackage>();
            }
            else
            {
                var packages = await response.Content
                    .ReadFromJsonAsync<SoftwarePackage[]>(cancellationToken: cancellationToken);

                _results = packages?
                    .Where(p => string.IsNullOrEmpty(_selectedSource) ||
                                string.Equals(p.Source, _selectedSource, StringComparison.OrdinalIgnoreCase))
                    .ToList() ?? new List<SoftwarePackage>();
            }
        }
        catch (TaskCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Software search failed for {deviceId}.", Device.ID);
            _errorMessage = "Search failed: " + ex.Message;
            _results = new List<SoftwarePackage>();
        }
        finally
        {
            _isSearching = false;
            _lastSearched = term;
            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task Install(SoftwarePackage pkg)
    {
        if (Device is null || _isInstalling)
        {
            return;
        }

        if (!TryParseSource(pkg.Source, out var sourceEnum))
        {
            ToastService.ShowToast2(
                $"Unsupported package source: {pkg.Source}",
                ToastType.Warning);
            return;
        }

        _isInstalling = true;
        await InvokeAsync(StateHasChanged);

        try
        {
            var result = await CircuitConnection.RequestSoftwareInstall(
                Device.ID,
                sourceEnum,
                pkg.Id,
                pkg.Name);

            if (!result.IsSuccess)
            {
                ToastService.ShowToast2(result.Reason, ToastType.Error);
                return;
            }

            ToastService.ShowToast("Install queued. Output will appear under Script History.");
            if (OnInstallQueued.HasDelegate)
            {
                await OnInstallQueued.InvokeAsync(result.Value);
            }
            await Close();
        }
        finally
        {
            _isInstalling = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    private static bool TryParseSource(string source, out SoftwareActionSource result)
    {
        switch (source?.Trim().ToLowerInvariant())
        {
            case "winget": result = SoftwareActionSource.Winget; return true;
            case "choco":  result = SoftwareActionSource.Choco;  return true;
            case "apt":    result = SoftwareActionSource.Apt;    return true;
            case "brew":   result = SoftwareActionSource.Brew;   return true;
            default:       result = SoftwareActionSource.Winget; return false;
        }
    }

    private static string[] ResolveSourcesForPlatform(string? platform)
    {
        if (string.IsNullOrWhiteSpace(platform))
        {
            return new[] { "winget", "choco", "apt", "brew" };
        }

        var p = platform.Trim().ToLowerInvariant();
        if (p.Contains("win"))
        {
            return new[] { "winget", "choco" };
        }
        if (p.Contains("linux"))
        {
            return new[] { "apt" };
        }
        if (p.Contains("mac") || p.Contains("osx") || p.Contains("darwin"))
        {
            return new[] { "brew" };
        }
        return new[] { "winget", "choco", "apt", "brew" };
    }

    public void Dispose()
    {
        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
    }
}
