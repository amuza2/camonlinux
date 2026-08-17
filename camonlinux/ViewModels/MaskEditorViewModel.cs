using System;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace camonlinux.ViewModels;

/// <summary>
/// View model for the Mask Editor window. Exposes the shared pipeline state
/// (enable / mode / invert) and the per-effect settings for direct binding.
/// The settings objects are the same instances the pipeline reads, so slider
/// changes apply live to the next frame.
/// </summary>
public partial class MaskEditorViewModel : ObservableObject
{
    public Masking.MaskPipeline Pipeline { get; }
    public Masking.ShapeMaskSettings Shape { get; }
    public Masking.GradientMaskSettings Gradient { get; }
    public Masking.FeatherMaskSettings Feather { get; }
    public Masking.ColorAdjustmentMaskSettings Adjustment { get; }
    public Masking.ChromaKeyMaskSettings Chroma { get; }

    private readonly Masking.IMaskEffect _shapeEffect;
    private readonly Masking.IMaskEffect _gradientEffect;
    private readonly Masking.IMaskEffect _chromaEffect;
    private readonly Masking.IMaskEffect _featherEffect;
    private readonly Masking.IMaskEffect _adjustmentEffect;

    public string[] MaskModeOptions { get; } = { "Alpha Mask", "Adjustment Mask" };
    public string[] ShapeOptions { get; } =
    {
        "Rectangle", "Circle", "Ellipse", "Regular Polygon", "Star", "Heart", "Superformula"
    };
    public string[] ScaleTypeOptions { get; } = { "Percent", "Width", "Height" };
    public string[] CornerRadiusTypeOptions { get; } = { "Uniform", "Custom" };
    public string[] FeatherTypeOptions { get; } = { "None", "Inner", "Middle", "Outer" };
    public string[] SuperformulaModeOptions { get; } = { "Squircle", "Superellipse", "General" };

    [ObservableProperty] private bool _enabled;
    [ObservableProperty] private int _modeIndex;
    [ObservableProperty] private bool _invert;
    [ObservableProperty] private int _shapeIndex;
    [ObservableProperty] private int _scaleTypeIndex;
    [ObservableProperty] private int _cornerTypeIndex;
    [ObservableProperty] private int _featherTypeIndex;
    [ObservableProperty] private int _superModeIndex;

    // Per-effect enable toggles (each mask type is off until the user turns it on;
    // only the shape mask is on by default so the toolbar toggle shows the shape).
    [ObservableProperty] private bool _shapeEnabled;
    [ObservableProperty] private bool _gradientEnabled;
    [ObservableProperty] private bool _chromaEnabled;
    [ObservableProperty] private bool _featherEnabled;
    [ObservableProperty] private bool _adjustmentEnabled;

    public MaskEditorViewModel(
        Masking.MaskPipeline pipeline,
        Masking.ShapeMaskSettings shape,
        Masking.GradientMaskSettings gradient,
        Masking.FeatherMaskSettings feather,
        Masking.ColorAdjustmentMaskSettings adjustment,
        Masking.ChromaKeyMaskSettings chroma,
        Masking.IMaskEffect shapeEffect,
        Masking.IMaskEffect gradientEffect,
        Masking.IMaskEffect chromaEffect,
        Masking.IMaskEffect featherEffect,
        Masking.IMaskEffect adjustmentEffect)
    {
        Pipeline = pipeline;
        Shape = shape;
        Gradient = gradient;
        Feather = feather;
        Adjustment = adjustment;
        Chroma = chroma;
        _shapeEffect = shapeEffect;
        _gradientEffect = gradientEffect;
        _chromaEffect = chromaEffect;
        _featherEffect = featherEffect;
        _adjustmentEffect = adjustmentEffect;
        _enabled = pipeline.Enabled;
        _modeIndex = pipeline.Mode == Masking.MaskMode.Adjustment ? 1 : 0;
        _invert = pipeline.Invert;
        _shapeIndex = (int)shape.Shape;
        _scaleTypeIndex = (int)shape.ScaleType;
        _cornerTypeIndex = (int)shape.CornerType;
        _featherTypeIndex = (int)shape.Feather;
        _superModeIndex = (int)shape.SuperMode;
        _shapeEnabled = shapeEffect.Enabled;
        _gradientEnabled = gradientEffect.Enabled;
        _chromaEnabled = chromaEffect.Enabled;
        _featherEnabled = featherEffect.Enabled;
        _adjustmentEnabled = adjustmentEffect.Enabled;
        shape.PropertyChanged += OnShapeSettingsChanged;
    }

    /// <summary>Raised when the user toggles enable (so the main window's toolbar toggle stays in sync).</summary>
    public event Action<bool>? EnabledChanged;

    partial void OnEnabledChanged(bool value)
    {
        Pipeline.Enabled = value;
        EnabledChanged?.Invoke(value);
    }

    partial void OnModeIndexChanged(int value) => Pipeline.Mode = value == 1 ? Masking.MaskMode.Adjustment : Masking.MaskMode.Alpha;

    partial void OnInvertChanged(bool value) => Pipeline.Invert = value;

    // --- per-effect enable (only the shape is on by default) ---

    partial void OnShapeEnabledChanged(bool value) => _shapeEffect.Enabled = value;
    partial void OnGradientEnabledChanged(bool value) => _gradientEffect.Enabled = value;
    partial void OnChromaEnabledChanged(bool value) => _chromaEffect.Enabled = value;
    partial void OnFeatherEnabledChanged(bool value) => _featherEffect.Enabled = value;
    partial void OnAdjustmentEnabledChanged(bool value) => _adjustmentEffect.Enabled = value;

    partial void OnShapeIndexChanged(int value)
    {
        Shape.Shape = (Masking.ShapeKind)value;
        foreach (var p in _visibilityProps) OnPropertyChanged(p);
    }

    partial void OnScaleTypeIndexChanged(int value)
    {
        Shape.ScaleType = (Masking.ScaleType)value;
        foreach (var p in _visibilityProps) OnPropertyChanged(p);
    }

    partial void OnCornerTypeIndexChanged(int value)
    {
        Shape.CornerType = (Masking.CornerRadiusType)value;
        foreach (var p in _visibilityProps) OnPropertyChanged(p);
    }

    partial void OnFeatherTypeIndexChanged(int value) => Shape.Feather = (Masking.FeatherType)value;

    partial void OnSuperModeIndexChanged(int value) => Shape.SuperMode = (Masking.SuperformulaMode)value;

    // --- per-shape control visibility (driven by Shape.Shape / CornerType / ScaleType) ---

    public bool IsRectangle => Shape.Shape == Masking.ShapeKind.Rectangle;
    public bool IsCircle => Shape.Shape == Masking.ShapeKind.Circle;
    public bool IsEllipse => Shape.Shape == Masking.ShapeKind.Ellipse;
    public bool IsPolygon => Shape.Shape == Masking.ShapeKind.RegularPolygon;
    public bool IsStar => Shape.Shape == Masking.ShapeKind.Star;
    public bool IsHeart => Shape.Shape == Masking.ShapeKind.Heart;
    public bool IsSuperformula => Shape.Shape == Masking.ShapeKind.Superformula;
    public bool IsCustomCorners => Shape.CornerType == Masking.CornerRadiusType.Custom;
    public bool IsWidthScale => Shape.ScaleType == Masking.ScaleType.Width;
    public bool IsHeightScale => Shape.ScaleType == Masking.ScaleType.Height;

    private readonly string[] _visibilityProps =
    {
        nameof(IsRectangle), nameof(IsCircle), nameof(IsEllipse), nameof(IsPolygon),
        nameof(IsStar), nameof(IsHeart), nameof(IsSuperformula), nameof(IsCustomCorners),
        nameof(IsWidthScale), nameof(IsHeightScale)
    };

    /// <summary>Keep the per-shape visibility flags in sync when the shape settings change.</summary>
    private void OnShapeSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        foreach (var prop in _visibilityProps)
            OnPropertyChanged(prop);
    }

    [RelayCommand]
    private void ResetShape()
    {
        Shape.Reset();
        foreach (var prop in _visibilityProps)
            OnPropertyChanged(prop);
    }

    [RelayCommand]
    private void RecenterShape()
    {
        Shape.CenterX = 50;
        Shape.CenterY = 50;
        Shape.PositionX = 0;
        Shape.PositionY = 0;
        Shape.SceneScale = 1.0;
    }
}
