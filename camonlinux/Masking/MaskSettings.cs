using CommunityToolkit.Mvvm.ComponentModel;

namespace camonlinux.Masking;

public enum ShapeKind { Rectangle, Circle, Ellipse, RegularPolygon, Star, Heart, Superformula }

public enum ScaleType { Percent, Width, Height }

public enum CornerRadiusType { Uniform, Custom }

public enum FeatherType { None, Inner, Middle, Outer }

public enum SuperformulaMode { Squircle, Superellipse, General }

/// <summary>
/// All parameters for the Shape mask. Observable so the editor UI binds directly;
/// the pipeline reads the fields on the streaming thread (minor races on a slider
/// drag are acceptable for real-time masking). "Scene View Transformation" is the
/// mask's position/scale overlay on the frame (there is no OBS scene concept here).
/// </summary>
public partial class ShapeMaskSettings : ObservableObject
{
    [ObservableProperty] private ShapeKind _shape = ShapeKind.Rectangle;

    // --- shared ---
    [ObservableProperty] private double _centerX = 50;  // percent of frame width
    [ObservableProperty] private double _centerY = 50;  // percent of frame height
    [ObservableProperty] private ScaleType _scaleType = ScaleType.Percent;
    [ObservableProperty] private double _scale = 50;    // percent of min(W,H) when ScaleType == Percent
    [ObservableProperty] private double _widthPx = 640; // when ScaleType == Width
    [ObservableProperty] private double _heightPx = 360;// when ScaleType == Height

    // --- scene-view (mask position/scale overlay) ---
    [ObservableProperty] private double _positionX;
    [ObservableProperty] private double _positionY;
    [ObservableProperty] private double _sceneScale = 1.0;
    [ObservableProperty] private bool _frameCheck;

    // --- rectangle ---
    [ObservableProperty] private double _cornerRadius;
    [ObservableProperty] private CornerRadiusType _cornerType = CornerRadiusType.Uniform;
    [ObservableProperty] private double _cornerTl;
    [ObservableProperty] private double _cornerTr;
    [ObservableProperty] private double _cornerBl;
    [ObservableProperty] private double _cornerBr;

    // --- circle ---
    [ObservableProperty] private double _radius = 100;

    // --- ellipse ---
    [ObservableProperty] private double _ellipseWidth = 200;
    [ObservableProperty] private double _ellipseHeight = 120;
    [ObservableProperty] private double _ellipseRotation;

    // --- regular polygon ---
    [ObservableProperty] private int _sides = 6;
    [ObservableProperty] private double _polygonRadius = 100;
    [ObservableProperty] private double _polygonCornerRadius;
    [ObservableProperty] private double _polygonRotation;

    // --- star ---
    [ObservableProperty] private int _starPoints = 5;
    [ObservableProperty] private double _starOuter = 100;
    [ObservableProperty] private double _starInner = 50;
    [ObservableProperty] private double _starCornerRadius;
    [ObservableProperty] private double _starRotation;

    // --- heart ---
    [ObservableProperty] private double _heartSize = 100;
    [ObservableProperty] private double _heartRotation;

    // --- superformula ---
    [ObservableProperty] private SuperformulaMode _superMode = SuperformulaMode.General;
    [ObservableProperty] private double _a = 1;
    [ObservableProperty] private double _b = 1;
    [ObservableProperty] private double _m = 4;
    [ObservableProperty] private double _n1 = 1;
    [ObservableProperty] private double _n2 = 1;
    [ObservableProperty] private double _n3 = 1;

    // --- feathering ---
    [ObservableProperty] private FeatherType _feather = FeatherType.None;
    [ObservableProperty] private double _featherAmount = 10;
    [ObservableProperty] private bool _invert;

    public void Reset()
    {
        Shape = ShapeKind.Rectangle;
        CenterX = 50; CenterY = 50;
        ScaleType = ScaleType.Percent; Scale = 50;
        WidthPx = 640; HeightPx = 360;
        PositionX = 0; PositionY = 0; SceneScale = 1.0; FrameCheck = false;
        CornerRadius = 0; CornerType = CornerRadiusType.Uniform;
        CornerTl = CornerTr = CornerBl = CornerBr = 0;
        Radius = 100;
        EllipseWidth = 200; EllipseHeight = 120; EllipseRotation = 0;
        Sides = 6; PolygonRadius = 100; PolygonCornerRadius = 0; PolygonRotation = 0;
        StarPoints = 5; StarOuter = 100; StarInner = 50; StarCornerRadius = 0; StarRotation = 0;
        HeartSize = 100; HeartRotation = 0;
        SuperMode = SuperformulaMode.General; A = 1; B = 1; M = 4; N1 = 1; N2 = 1; N3 = 1;
        Feather = FeatherType.None; FeatherAmount = 10; Invert = false;
    }
}

public partial class GradientMaskSettings : ObservableObject
{
    [ObservableProperty] private double _width = 100;
    [ObservableProperty] private double _position = 50;
    [ObservableProperty] private double _rotation;
    [ObservableProperty] private bool _invert;
    [ObservableProperty] private bool _debugLines;

    public void Reset()
    {
        Width = 100; Position = 50; Rotation = 0; Invert = false; DebugLines = false;
    }
}

public partial class FeatherMaskSettings : ObservableObject
{
    [ObservableProperty] private double _size = 10;

    public void Reset() => Size = 10;
}

public partial class ColorAdjustmentMaskSettings : ObservableObject
{
    [ObservableProperty] private double _brightnessMin = -50;
    [ObservableProperty] private double _brightnessMax = 50;
    [ObservableProperty] private double _contrastMin = -50;
    [ObservableProperty] private double _contrastMax = 50;
    [ObservableProperty] private double _saturationMin = -100;
    [ObservableProperty] private double _saturationMax = 100;
    [ObservableProperty] private double _hueShiftMin = -180;
    [ObservableProperty] private double _hueShiftMax = 180;

    public void Reset()
    {
        BrightnessMin = -50; BrightnessMax = 50;
        ContrastMin = -50; ContrastMax = 50;
        SaturationMin = -100; SaturationMax = 100;
        HueShiftMin = -180; HueShiftMax = 180;
    }
}

public partial class ChromaKeyMaskSettings : ObservableObject
{
    [ObservableProperty] private bool _showMatte;
    [ObservableProperty] private bool _doubleColor;
    [ObservableProperty] private double _keyR;
    [ObservableProperty] private double _keyG = 255;
    [ObservableProperty] private double _keyB;
    [ObservableProperty] private double _keyR2;
    [ObservableProperty] private double _keyG2;
    [ObservableProperty] private double _keyB2 = 255;
    [ObservableProperty] private double _similarity = 80;
    [ObservableProperty] private double _smoothness = 10;
    [ObservableProperty] private double _spillReduction = 20;
    [ObservableProperty] private double _opacity = 100;
    [ObservableProperty] private double _contrast;
    [ObservableProperty] private double _brightness;
    [ObservableProperty] private double _gamma = 100;
    [ObservableProperty] private bool _superKey;
    [ObservableProperty] private double _k = 1;
    [ObservableProperty] private double _k2 = 1;
    [ObservableProperty] private double _veil = 30;
    [ObservableProperty] private bool _invert;

    public void Reset()
    {
        ShowMatte = false; DoubleColor = false;
        KeyR = 0; KeyG = 255; KeyB = 0;
        KeyR2 = 0; KeyG2 = 0; KeyB2 = 255;
        Similarity = 80; Smoothness = 10; SpillReduction = 20;
        Opacity = 100; Contrast = 0; Brightness = 0; Gamma = 100;
        SuperKey = false; K = 1; K2 = 1; Veil = 30; Invert = false;
    }
}

public enum SvgScaleBy { Width, Height, Both }

/// <summary>Parameters for the SVG mask (rasterized via SkiaSharp).</summary>
public partial class SvgMaskSettings : ObservableObject
{
    [ObservableProperty] private string _svgText = "";
    [ObservableProperty] private SvgScaleBy _scaleBy = SvgScaleBy.Both;
    [ObservableProperty] private double _width = 640;
    [ObservableProperty] private double _height = 360;
    [ObservableProperty] private double _positionX;
    [ObservableProperty] private double _positionY;
    [ObservableProperty] private bool _invert;

    public void Reset()
    {
        SvgText = "";
        ScaleBy = SvgScaleBy.Both;
        Width = 640; Height = 360;
        PositionX = 0; PositionY = 0; Invert = false;
    }
}

/// <summary>
/// Parameters for the BSM (background-subtraction / alpha-wipe) mask.
/// </summary>
public partial class BsmMaskSettings : ObservableObject
{
    [ObservableProperty] private double _fadeOutTime = 0.5;  // seconds to fade a wiped area back
    [ObservableProperty] private double _threshold = 40;      // 0..255 per-channel diff to count as "changed"
    [ObservableProperty] private bool _freezeFrame;
    [ObservableProperty] private bool _captureBackground;     // one-shot trigger, consumed by the effect
    [ObservableProperty] private bool _resetBackground;       // one-shot trigger, consumed by the effect

    public void Reset()
    {
        FadeOutTime = 0.5; Threshold = 40; FreezeFrame = false;
        CaptureBackground = false; ResetBackground = false;
    }
}

public enum SourceChannel { Red, Green, Blue, Alpha }
public enum SourceFilter { Alpha, Grayscale, Luminosity }
public enum SourceCompression { None, Threshold, Range }
public enum SourceScaleBy { Percent, Width, Height, Separate, Stretch, Manual }
public enum SourceBoundary { None, Tile, Mirror, Extend }
public enum SourceAlignment { TL, TC, TR, CL, CC, CR, BL, BC, BR }

/// <summary>Parameters for the Source mask (a second webcam used as the mask).</summary>
public partial class SourceMaskSettings : ObservableObject
{
    [ObservableProperty] private string _device = "";          // e.g. /dev/video2
    [ObservableProperty] private SourceChannel _channel = SourceChannel.Green;
    [ObservableProperty] private SourceFilter _filter = SourceFilter.Grayscale;
    [ObservableProperty] private double _multiplier = 1;
    [ObservableProperty] private bool _useThreshold;
    [ObservableProperty] private double _threshold = 128;
    [ObservableProperty] private SourceCompression _compression = SourceCompression.None;
    [ObservableProperty] private double _rangeMin;
    [ObservableProperty] private double _rangeMax = 255;
    [ObservableProperty] private SourceScaleBy _scaleBy = SourceScaleBy.Percent;
    [ObservableProperty] private double _scale = 100;
    [ObservableProperty] private double _width = 640;
    [ObservableProperty] private double _height = 480;
    [ObservableProperty] private double _offsetX;
    [ObservableProperty] private double _offsetY;
    [ObservableProperty] private SourceBoundary _boundary = SourceBoundary.Extend;
    [ObservableProperty] private int _rotation;                 // 0/90/180/270
    [ObservableProperty] private SourceAlignment _alignment = SourceAlignment.CC;
    [ObservableProperty] private double _positionX;
    [ObservableProperty] private double _positionY;
    [ObservableProperty] private bool _invert;

    public void Reset()
    {
        Device = "";
        Channel = SourceChannel.Green; Filter = SourceFilter.Grayscale;
        Multiplier = 1;
        UseThreshold = false; Threshold = 128;
        Compression = SourceCompression.None; RangeMin = 0; RangeMax = 255;
        ScaleBy = SourceScaleBy.Percent; Scale = 100; Width = 640; Height = 480;
        OffsetX = 0; OffsetY = 0;
        Boundary = SourceBoundary.Extend; Rotation = 0;
        Alignment = SourceAlignment.CC; PositionX = 0; PositionY = 0;
        Invert = false;
    }
}
