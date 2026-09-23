using Avalonia.Controls;
using Avalonia.Interactivity;
using camonlinux.Services;

namespace camonlinux.Views;

/// <summary>
/// About dialog: identity, licence, links and a diagnostics block for bug reports.
/// </summary>
public partial class AboutWindow : Window
{
    /// <summary>XAML-loader / design-time constructor.</summary>
    public AboutWindow() : this(string.Empty)
    {
    }

    /// <param name="diagnostics">
    /// Pre-built diagnostics text from <see cref="AppDiagnostics.Build"/> — assembled by
    /// the caller because it needs the live capture service and effect list.
    /// </param>
    public AboutWindow(string diagnostics)
    {
        InitializeComponent();

        VersionText.Text = $"{AppInfo.AppName} {AppInfo.Version}";
        DiagnosticsBox.Text = diagnostics;
    }

    private void OnOpenRepository(object? sender, RoutedEventArgs e)
        => SystemLauncher.Open(AppInfo.RepositoryUrl);

    private void OnReportIssue(object? sender, RoutedEventArgs e)
        => SystemLauncher.Open(AppInfo.IssuesUrl);

    private void OnOpenReleases(object? sender, RoutedEventArgs e)
        => SystemLauncher.Open(AppInfo.ReleasesUrl);

    private void OnOpenLicense(object? sender, RoutedEventArgs e)
        => SystemLauncher.Open(AppInfo.LicenseUrl);

    /// <summary>
    /// Copies the diagnostics to the clipboard. Uses the read-only TextBox's own copy
    /// command rather than <c>IClipboard</c> directly — Avalonia 12 replaced the old
    /// <c>SetTextAsync</c> with an <c>IAsyncDataTransfer</c> API, and the TextBox already
    /// handles that correctly.
    /// </summary>
    private void OnCopyDiagnostics(object? sender, RoutedEventArgs e)
    {
        DiagnosticsBox.SelectAll();
        DiagnosticsBox.Copy();
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
