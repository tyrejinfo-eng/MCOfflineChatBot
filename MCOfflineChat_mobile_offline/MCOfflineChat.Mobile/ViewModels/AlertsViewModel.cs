using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MCOfflineChat.Mobile.Models;
using MCOfflineChat.Mobile.Services;

namespace MCOfflineChat.Mobile.ViewModels;

public partial class AlertsViewModel : ObservableObject
{
    private readonly ApiClient _apiClient;
    private Timer? _refreshTimer;

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusMessage = "Loading alerts...";
    [ObservableProperty] private int _totalAlerts;
    [ObservableProperty] private int _criticalCount;
    [ObservableProperty] private int _highCount;
    [ObservableProperty] private string _selectedFilter = "All";
    [ObservableProperty] private bool _isConnected;

    public ObservableCollection<SecurityAlertItem> Alerts { get; } = new();

    public List<string> FilterOptions { get; } = new()
    {
        "All", "Critical", "High", "Medium", "Low"
    };

    public AlertsViewModel(ApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    [RelayCommand]
    private async Task RefreshAlertsAsync()
    {
        IsBusy = true;
        try
        {
            var alertElements = await _apiClient.GetAlertsAsync(50);
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

            var alerts = new List<SecurityAlertItem>();
            foreach (var el in alertElements)
            {
                try
                {
                    var alert = new SecurityAlertItem
                    {
                        AlertId = el.TryGetProperty("alertId", out var id) ? id.GetString() ?? "" : Guid.NewGuid().ToString("N")[..8],
                        Severity = el.TryGetProperty("severity", out var sev) ? sev.GetString() ?? "Low" : "Low",
                        Title = el.TryGetProperty("title", out var title) ? title.GetString() ?? "Security Alert" : "Security Alert",
                        Source = el.TryGetProperty("source", out var src) ? src.GetString() ?? "" : "",
                        HostId = el.TryGetProperty("hostId", out var host) ? host.GetString() ?? "" : "",
                        EventType = el.TryGetProperty("eventType", out var evt) ? evt.GetString() ?? "" : "",
                        FinalScore = el.TryGetProperty("finalScore", out var score) ? score.GetDouble() : 0,
                        ResponseAction = el.TryGetProperty("responseAction", out var action) ? action.GetString() ?? "None" : "None",
                        IsResolved = el.TryGetProperty("isResolved", out var resolved) && resolved.GetBoolean()
                    };

                    if (el.TryGetProperty("timestamp", out var ts) && DateTime.TryParse(ts.GetString(), out var dt))
                        alert.Timestamp = dt;

                    alerts.Add(alert);
                }
                catch { }
            }

            // Apply filter
            var filtered = SelectedFilter == "All"
                ? alerts
                : alerts.Where(a => a.Severity == SelectedFilter).ToList();

            MainThread.BeginInvokeOnMainThread(() =>
            {
                Alerts.Clear();
                foreach (var alert in filtered.OrderByDescending(a => a.Timestamp))
                    Alerts.Add(alert);

                TotalAlerts = alerts.Count;
                CriticalCount = alerts.Count(a => a.Severity == "Critical");
                HighCount = alerts.Count(a => a.Severity == "High");
                IsConnected = true;
                StatusMessage = alerts.Count > 0
                    ? $"{alerts.Count} alert(s) — {CriticalCount} critical, {HighCount} high"
                    : "No active alerts. All clear.";
            });
        }
        catch (Exception ex)
        {
            IsConnected = false;
            StatusMessage = $"Could not load alerts: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnSelectedFilterChanged(string value)
    {
        _ = RefreshAlertsAsync();
    }

    [RelayCommand]
    private async Task ViewAlertDetailAsync(SecurityAlertItem? alert)
    {
        if (alert == null) return;
        await Shell.Current.DisplayAlert(
            $"{alert.Severity} Alert",
            $"Title: {alert.Title}\n" +
            $"Score: {alert.ScoreDisplay}\n" +
            $"Source: {alert.Source}\n" +
            $"Host: {alert.HostId}\n" +
            $"Event: {alert.EventType}\n" +
            $"Response: {alert.ResponseAction}\n" +
            $"Time: {alert.TimestampDisplay}\n" +
            $"Resolved: {(alert.IsResolved ? "Yes" : "No")}",
            "OK");
    }

    public void OnAppearing()
    {
        _ = RefreshAlertsAsync();
        _refreshTimer?.Dispose();
        _refreshTimer = new Timer(async _ =>
        {
            await MainThread.InvokeOnMainThreadAsync(async () => await RefreshAlertsAsync());
        }, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    }

    public void OnDisappearing()
    {
        _refreshTimer?.Dispose();
        _refreshTimer = null;
    }
}
