using Syuas.Core.Models;

namespace Syuas.Core.ViewModels;

public sealed class SettingsViewModel(AppSettings settings) : ObservableObject
{
    private bool recoveryEnabled = settings.RecoveryEnabled;
    private PreviewDataMode previewData = settings.PreviewData;
    public bool RecoveryEnabled { get => recoveryEnabled; set { recoveryEnabled = value; Changed(); } }
    public PreviewDataMode PreviewData { get => previewData; set { previewData = value; Changed(); } }
    public AppSettings ToSettings() => new(RecoveryEnabled, PreviewData);
}
