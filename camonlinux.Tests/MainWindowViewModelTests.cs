using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using camonlinux.Services;
using camonlinux.ViewModels;
using Xunit;

namespace camonlinux.Tests;

/// <summary>
/// The main view model exposes computed properties (e.g. <c>RecordButtonText</c>) that
/// the XAML binds to directly. Those only update when the source property raises a
/// change notification for them, which is easy to forget — and the failure is silent:
/// the button simply never changes. These tests pin that wiring down.
/// </summary>
public sealed class MainWindowViewModelTests : IDisposable
{
    private readonly string _configDir = Path.Combine(
        Path.GetTempPath(), "camonlinux_vm_" + Guid.NewGuid().ToString("N"));

    private readonly FakeCaptureService _capture = new();
    private readonly SettingsService _settings;
    private readonly MediaLibraryService _mediaLibrary = new();

    public MainWindowViewModelTests()
    {
        _settings = new SettingsService(_configDir);
    }

    public void Dispose()
    {
        _settings.Dispose();
        _mediaLibrary.Dispose();
        try
        {
            if (Directory.Exists(_configDir))
                Directory.Delete(_configDir, recursive: true);
        }
        catch
        {
            // Best-effort cleanup.
        }
    }

    private MainWindowViewModel CreateViewModel() => new(_capture, _settings, _mediaLibrary);

    [Fact]
    public void RecordButtonText_StartsAsRecord()
    {
        var vm = CreateViewModel();

        Assert.False(vm.IsRecording);
        Assert.Equal("Record", vm.RecordButtonText);
    }

    [Fact]
    public void RecordButtonText_TurnsIntoStopWhileRecordingAndBackAgain()
    {
        var vm = CreateViewModel();
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.IsRecording = true;

        Assert.Equal("Stop", vm.RecordButtonText);
        Assert.Contains(nameof(MainWindowViewModel.RecordButtonText), changed);

        changed.Clear();
        vm.IsRecording = false;

        Assert.Equal("Record", vm.RecordButtonText);
        Assert.Contains(nameof(MainWindowViewModel.RecordButtonText), changed);
    }

    [Fact]
    public void IsRecording_ReevaluatesTheToggleRecordingCommand()
    {
        var vm = CreateViewModel();
        var raised = false;
        vm.ToggleRecordingCommand.CanExecuteChanged += (_, _) => raised = true;

        vm.IsRecording = true;

        Assert.True(raised, "The Record/Stop button state must be re-queried when recording starts or stops.");
    }

    [Fact]
    public void RecordButtonText_DoesNotChangeOnUnrelatedProperties()
    {
        // A cheap guard against over-notifying (which would cause needless re-renders).
        var vm = CreateViewModel();
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.StatusMessage = "Something happened";

        Assert.DoesNotContain(nameof(MainWindowViewModel.RecordButtonText), changed);
    }

    [Fact]
    public void Constructor_DoesNotNeedAGraphicalEnvironment()
    {
        // Guards the test suite itself: this view model must stay constructible headless.
        var vm = CreateViewModel();

        Assert.NotNull(vm.MaskPipeline);
        Assert.NotNull(vm.MaskEditor);
        Assert.Equal("Record", vm.RecordButtonText);
    }
}
