using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MCOfflineChat.Mobile.Models;
using MCOfflineChat.Mobile.Services;

namespace MCOfflineChat.Mobile.ViewModels;

public partial class EngineStatusViewModel : ObservableObject
{
    private readonly ApiClient _apiClient;

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusMessage = "Loading engine status...";
    [ObservableProperty] private int _totalEngines;
    [ObservableProperty] private int _runningEngines;
    [ObservableProperty] private bool _isConnected;

    public ObservableCollection<EngineStatusItem> Engines { get; } = new();

    public EngineStatusViewModel(ApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    [RelayCommand]
    private async Task RefreshStatusAsync()
    {
        IsBusy = true;
        try
        {
            var engineElements = await _apiClient.GetEngineStatusesAsync();
            var engines = new List<EngineStatusItem>();

            foreach (var el in engineElements)
            {
                try
                {
                    engines.Add(new EngineStatusItem
                    {
                        Name = el.TryGetProperty("name", out var name) ? name.GetString() ?? "Unknown" : "Unknown",
                        IsRunning = el.TryGetProperty("isRunning", out var running) && running.GetBoolean(),
                        Type = el.TryGetProperty("type", out var type) ? type.GetString() ?? "" : "",
                        EventsProcessed = el.TryGetProperty("eventsProcessed", out var events) ? events.GetInt64() : 0,
                        Uptime = el.TryGetProperty("uptime", out var uptime) ? uptime.GetString() ?? "N/A" : "N/A"
                    });
                }
                catch { }
            }

            MainThread.BeginInvokeOnMainThread(() =>
            {
                Engines.Clear();
                foreach (var engine in engines.OrderByDescending(e => e.IsRunning).ThenBy(e => e.Name))
                    Engines.Add(engine);

                TotalEngines = engines.Count;
                RunningEngines = engines.Count(e => e.IsRunning);
                IsConnected = true;
                StatusMessage = engines.Count > 0
                    ? $"{RunningEngines}/{TotalEngines} engines running"
                    : "No engine data available.";
            });
        }
        catch (Exception ex)
        {
            IsConnected = false;
            StatusMessage = $"Could not load engines: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void OnAppearing()
    {
        _ = RefreshStatusAsync();
    }
}
