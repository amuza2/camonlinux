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

    public MaskEditorViewModel(
        Masking.MaskPipeline pipeline,
        Masking.ShapeMaskSettings shape,
        Masking.GradientMaskSettings gradient,
        Masking.FeatherMaskSettings feather,
        Masking.ColorAdjustmentMaskSettings adjustment,
        Masking.ChromaKeyMaskSettings chroma)
    {
        Pipeline = pipeline;
        Shape = shape;
        Gradient = gradient;
        Feather = feather;
        Adjustment = adjustment;
        Chroma = chroma;
        _enabled = pipeline.Enabled;
        _modeIndex = pipeline.Mode == Masking.MaskMode.Adjustment ? 1 : 0;
        _invert = pipeline.Invert;
        _shapeIndex = (int)shape.Shape;
        _scaleTypeIndex = (int)shape.ScaleType;
        _cornerTypeIndex = (int)shape.CornerType;
        _featherTypeIndex = (int)shape.Feather;
        _superModeIndex = (int)shape.SuperMode;
        shape.PropertyChanged += OnShapeSettingsChanged;
    }

    partial void OnEnabledChanged(bool value) => Pipeline.Enabled = value;

    partial void OnModeIndexChanged(int value) => Pipeline.Mode = value == 1 ? Masking.MaskMode.Adjustment : Masking.MaskMode.Alpha;

    partial void OnInvertChanged(bool value) => Pipeline.Invert = value;

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
