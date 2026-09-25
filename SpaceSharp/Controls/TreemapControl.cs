using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SpaceSharp.Layout;
using SpaceSharp.Models;
using SpaceSharp.Util;

namespace SpaceSharp.Controls;

public enum ColorMode
{
    ByDepth,
    ByFileType
}

/// <param name="Node">The node drawn in this box (the deepest folder of a collapsed chain).</param>
/// <param name="ChainTop">First folder of a collapsed single-child chain, or null.</param>
/// <summary>How boxes are drawn. Same layout, different visual treatment.</summary>
public enum MapStyle
{
    /// <summary>Title bars, 1 px borders, cushion shading.</summary>
    Classic,
    /// <summary>Flat colors, small gaps, softly rounded, folder names as captions.</summary>
    Tiles,
    /// <summary>Folders as raised cards with a shadow and bold title; files as flat chips.</summary>
    Cards,
    /// <summary>Deep title band, lighter body, light borders, no shading.</summary>
    Bands,
    /// <summary>One hue per top-level folder, darker with depth.</summary>
    Terraces,
    /// <summary>Rounded pastel blocks with gaps.</summary>
    Soft
}

/// <param name="Branch">Index of the top-level folder this item belongs to (0 for the root itself).</param>
public readonly record struct TreemapItem(FsNode Node, Rect Bounds, int Depth, bool HasHeader, FsNode? ChainTop, int Branch);

/// <summary>
/// SpaceMonger-style nested treemap with a zoomable camera.
///
/// The whole scan is always one map. Zooming lays the map out on a larger virtual canvas
/// (window size × zoom) shifted by a pan offset, so small items get real pixels and crisp
/// labels as you zoom in. Only items intersecting the window are laid out and drawn.
///
/// "Focusing" a folder animates the camera so that folder fills the window. The focused
/// folder is the one shown in the breadcrumb; wheel/pan changes it to whatever folder
/// covers most of the window.
/// </summary>
public sealed class TreemapControl : FrameworkElement
{
    public const double MaxZoom = 1_000_000;

    private const double ClassicHeaderHeight = 17;
    private const double MinHeaderWidth = 60;   // folders smaller than this get no title bar
    private const double MinHeaderHeight = 44;
    private const double MinChildSize = 4;       // smaller children are not drawn (the parent's color shows)
    private const double MinFolderContent = 12;
    private const double GroupBelowArea = 30 * 22;  // children smaller than this many pixels are grouped  // don't subdivide folders with less room than this
    private const double TextSize = 11;
    private const double WheelStep = 1.25;
    private const double DragThreshold = 4;
    private const double AnimationSeconds = 0.4;

    private static readonly Typeface NormalFace = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    private static readonly Typeface HeaderFace = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

    private static readonly Brush BackgroundBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x17, 0x17, 0x1C)));
    private static readonly Brush HeaderShade = Frozen(new SolidColorBrush(Color.FromArgb(0x30, 0x00, 0x00, 0x00)));
    private static readonly Brush TextBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x18)));
    private static readonly Brush HoverFill = Frozen(new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)));
    private static readonly Pen BorderPen = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(0xB0, 0x10, 0x10, 0x14)), 1));
    private static readonly Pen HoverPen = Frozen(new Pen(Brushes.White, 2));
    private static readonly Pen HoverOutlinePen = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(0x99, 0, 0, 0)), 4)); // keeps the hover frame visible on light fills
    private static readonly Brush FreeSpaceBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x4E, 0x4E, 0x5A)));
    // One relative-coordinate gradient works for every box: WPF stretches it to each rectangle's bounds.
    private static readonly Brush CushionBrush = Frozen(new LinearGradientBrush(
        new GradientStopCollection
        {
            new(Color.FromArgb(0x48, 0xFF, 0xFF, 0xFF), 0.0),
            new(Color.FromArgb(0x00, 0x80, 0x80, 0x80), 0.55),
            new(Color.FromArgb(0x50, 0x00, 0x00, 0x00), 1.0)
        }, new Point(0, 0), new Point(1, 1)));
    private static readonly Pen SelectionPen = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0xF5, 0xB8, 0x2E)), 3));

    private readonly DrawingVisual _mapVisual = new();
    private readonly DrawingVisual _overlayVisual = new();
    private readonly List<TreemapItem> _items = new();
    private readonly Dictionary<FsNode, TreemapItem> _index = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    private FsNode? _root;
    private FsNode? _focus;
    private FsNode? _pinnedFocus;   // folder explicitly focused via FocusOn, until the user wheels/pans
    private FsNode? _hoveredNode;
    private FsNode? _selectedNode;                       // anchor of the selection (last clicked)
    private readonly HashSet<FsNode> _selection = new();
    private HashSet<FsNode>? _filterMatches;             // null = no filter active
    private readonly HashSet<FsNode> _matchedGroups = new(); // transient group boxes that contain a match (rebuilt each layout)
    private readonly Dictionary<Brush, Brush> _dimmed = new();
    private readonly Dictionary<(Brush, int), Brush> _tints = new();   // style-specific shades of palette brushes
    private MapStyle _mapStyle = MapStyle.Classic;
    private double _labelScale = 1.0;
    private bool _labelHalo;
    private static readonly Pen LightHaloPen = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(0xB8, 0xFF, 0xFF, 0xFF)), 3) { LineJoin = PenLineJoin.Round });
    private static readonly Pen DarkHaloPen = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(0xB8, 0x00, 0x00, 0x00)), 3) { LineJoin = PenLineJoin.Round });
    private ColorMode _colorMode = ColorMode.ByDepth;
    private ColorScheme _scheme = Palette.Default;
    private SizeMeasure _measure = SizeMeasure.FileSize;
    private bool _cushion = true;
    private bool _mergeChains = true;
    private bool _groupSmall = true;
    private bool _rebuildPending;

    // Camera. _offset is the window's top-left on the zoomed canvas, in pixels.
    private double _zoom = 1;
    private Vector _offset;
    private Rect _viewport;
    private Rect _drawClip;

    private CameraAnimation? _animation;
    private bool _renderingHooked;

    private bool _panArmed;
    private bool _panning;
    private Point _panStart;
    private Vector _panStartOffset;

    private sealed class CameraAnimation
    {
        public FsNode? Target;
        public double StartZoom;
        public double EndZoom;
        public Point StartCenter;   // normalized canvas coordinates (0..1)
        public Point EndCenter;
        public double StartTime;
    }

    public TreemapControl()
    {
        AddVisualChild(_mapVisual);
        AddVisualChild(_overlayVisual);
        RenderOptions.SetEdgeMode(_mapVisual, EdgeMode.Aliased);
        RenderOptions.SetEdgeMode(_overlayVisual, EdgeMode.Aliased);
        ClipToBounds = true;
        Focusable = true;
    }

    /// <summary>Color shown behind and between the boxes (follows the light/dark theme).</summary>
    public static readonly DependencyProperty MapBackgroundProperty = DependencyProperty.Register(
        nameof(MapBackground), typeof(Brush), typeof(TreemapControl),
        new PropertyMetadata(BackgroundBrush, (d, _) =>
        {
            var map = (TreemapControl)d;
            map._dimmed.Clear();
            map._tints.Clear();
            map.Invalidate();
        }));

    public Brush MapBackground
    {
        get => (Brush)GetValue(MapBackgroundProperty);
        set => SetValue(MapBackgroundProperty, value);
    }

    public event EventHandler<FsNode?>? HoveredNodeChanged;
    public event EventHandler<FsNode?>? SelectionChanged;
    public event EventHandler<FsNode>? NodeActivated;
    public event EventHandler? FocusChanged;
    public event EventHandler? ZoomChanged;

    /// <summary>The scanned tree. Setting it shows the whole map.</summary>
    public FsNode? Root
    {
        get => _root;
        set
        {
            StopAnimation();
            _root = value;
            _root?.SortBy(_measure);
            _hoveredNode = null;
            _pinnedFocus = null;
            _zoom = 1;
            _offset = default;
            Invalidate();
            ZoomChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>The folder the camera is on (shown in the breadcrumb).</summary>
    public FsNode? FocusedFolder => _focus;

    /// <summary>The last clicked item. Setting it replaces the whole selection with that one item.</summary>
    public FsNode? SelectedNode
    {
        get => _selectedNode;
        set
        {
            if (ReferenceEquals(_selectedNode, value) && _selection.Count <= 1) return;
            _selection.Clear();
            if (value is not null) _selection.Add(value);
            _selectedNode = value;
            DrawOverlay();
            SelectionChanged?.Invoke(this, value);
        }
    }

    /// <summary>Every selected item (Ctrl+click and Shift+click add to it).</summary>
    public IReadOnlyCollection<FsNode> SelectedNodes => _selection;

    public void ToggleSelected(FsNode node)
    {
        if (!_selection.Remove(node)) _selection.Add(node);
        _selectedNode = _selection.Contains(node) ? node : _selection.FirstOrDefault();
        DrawOverlay();
        SelectionChanged?.Invoke(this, _selectedNode);
    }

    /// <summary>Selects a range of siblings between the anchor and the node, like Shift+click in Explorer.</summary>
    public void SelectRangeTo(FsNode node)
    {
        var anchor = _selectedNode;
        if (anchor is null || !ReferenceEquals(anchor.Parent, node.Parent) || node.Parent is null)
        {
            ToggleSelected(node);
            return;
        }

        var siblings = node.Parent.Children;
        int a = siblings.IndexOf(anchor), b = siblings.IndexOf(node);
        if (a < 0 || b < 0)
        {
            ToggleSelected(node);
            return;
        }

        for (int i = Math.Min(a, b); i <= Math.Max(a, b); i++)
            if (siblings[i].IsReal) _selection.Add(siblings[i]);
        DrawOverlay();
        SelectionChanged?.Invoke(this, _selectedNode);
    }

    /// <summary>Replaces the selection with the given items.</summary>
    public void SelectMany(IEnumerable<FsNode> nodes)
    {
        _selection.Clear();
        foreach (var n in nodes) if (n.IsReal) _selection.Add(n);
        _selectedNode = _selection.FirstOrDefault();
        DrawOverlay();
        SelectionChanged?.Invoke(this, _selectedNode);
    }

    public void ClearSelection() => SelectedNode = null;

    /// <summary>
    /// Items that match the current filter (files and the folders containing them). Everything else is
    /// drawn dimmed. Null turns the filter off.
    /// </summary>
    public void SetFilterMatches(HashSet<FsNode>? matches)
    {
        _filterMatches = matches;
        Invalidate();
    }

    public bool HasFilter => _filterMatches is not null;

    /// <summary>The color palette used to fill boxes.</summary>
    public ColorScheme Scheme
    {
        get => _scheme;
        set
        {
            if (ReferenceEquals(_scheme, value)) return;
            _scheme = value;
            _tints.Clear();
            Invalidate();
        }
    }

    /// <summary>Which size the boxes represent. Re-sorts the tree when changed.</summary>
    public SizeMeasure SizeMode
    {
        get => _measure;
        set
        {
            if (_measure == value) return;
            _measure = value;
            _root?.SortBy(value);
            Invalidate();
        }
    }

    /// <summary>Soft light-to-dark shading on every box, which makes sizes easier to read than flat fills.</summary>
    public bool Cushion
    {
        get => _cushion;
        set
        {
            if (_cushion == value) return;
            _cushion = value;
            Invalidate();
        }
    }

    /// <summary>Text size multiplier for all labels (1 = 11 px). Title bars grow with it.</summary>
    public double LabelScale
    {
        get => _labelScale;
        set
        {
            value = Math.Clamp(value, 0.8, 1.8);
            if (Math.Abs(_labelScale - value) < 0.001) return;
            _labelScale = value;
            Invalidate();
        }
    }

    /// <summary>Draw a contrasting outline around every label so text stays readable on any color.</summary>
    public bool LabelHalo
    {
        get => _labelHalo;
        set
        {
            if (_labelHalo == value) return;
            _labelHalo = value;
            Invalidate();
        }
    }

    /// <summary>The visual treatment of the boxes.</summary>
    public MapStyle MapStyle
    {
        get => _mapStyle;
        set
        {
            if (_mapStyle == value) return;
            _mapStyle = value;
            Invalidate();
        }
    }

    // Per-style geometry. Gap is taken off each box before drawing; inset is the space folders keep
    // around their children; the header height is the room reserved for the folder title.
    private double HeaderHeight => Math.Round((_mapStyle switch
    {
        MapStyle.Tiles => 16, MapStyle.Cards => 22, MapStyle.Bands => 18, MapStyle.Terraces => 16, MapStyle.Soft => 20, _ => ClassicHeaderHeight
    }) * _labelScale);

    private double InsetFor(Rect bounds) => _mapStyle switch
    {
        MapStyle.Tiles => 2, MapStyle.Cards => 4, MapStyle.Soft => 3, MapStyle.Bands or MapStyle.Terraces => 2,
        _ => Math.Min(bounds.Width, bounds.Height) >= 40 ? 2 : 1
    };

    private double Gap => _mapStyle switch { MapStyle.Tiles => 3, MapStyle.Cards => 4, MapStyle.Soft => 5, _ => 0 };
    private double Radius => _mapStyle switch { MapStyle.Tiles => 4, MapStyle.Cards => 6, MapStyle.Soft => 8, _ => 0 };

    /// <summary>Fly to folders instead of jumping (FocusOn with animate: true).</summary>
    public bool AnimateZoom { get; set; } = true;

    /// <summary>Draw folders that only contain one folder as a single box with a combined title.</summary>
    public bool MergeSingleFolderChains
    {
        get => _mergeChains;
        set
        {
            if (_mergeChains == value) return;
            _mergeChains = value;
            Invalidate();
        }
    }

    /// <summary>Replace children too small to see with a single "N files" box.</summary>
    public bool GroupSmallItems
    {
        get => _groupSmall;
        set
        {
            if (_groupSmall == value) return;
            _groupSmall = value;
            Invalidate();
        }
    }

    public ColorMode ColorMode
    {
        get => _colorMode;
        set
        {
            if (_colorMode == value) return;
            _colorMode = value;
            Invalidate();
        }
    }

    /// <summary>1 = the whole map fits the window.</summary>
    public double Zoom => _zoom;

    /// <summary>Moves the camera so the folder (or a file's folder) fills the window.</summary>
    public void FocusOn(FsNode node, bool animate = true)
    {
        if (_root is null) return;
        var folder = node.IsDirectory ? node : node.Parent ?? node;
        _pinnedFocus = folder;
        EnsureLayout();

        var visible = DeepestLaidOut(folder);
        if (!animate || !AnimateZoom || visible is null || ActualWidth < 8)
        {
            StopAnimation();
            SnapTo(folder);
            return;
        }

        var (zoom, center) = FitView(_index[visible].Bounds);
        StartAnimation(zoom, center, folder);
        UpdateFocus();
    }

    /// <summary>Smoothly zooms around the window center.</summary>
    public void ZoomBy(double factor)
    {
        if (_root is null) return;
        _pinnedFocus = null;
        double zoom = Math.Clamp(_zoom * factor, 1, MaxZoom);
        StartAnimation(zoom, ClampCenter(CurrentCenter(), zoom), null);
    }

    /// <summary>Shows the whole map.</summary>
    public void ShowAll()
    {
        if (_root is not null) FocusOn(_root);
    }

    /// <summary>Re-layouts and redraws (call after the tree was modified).</summary>
    public void Refresh() => Invalidate();

    protected override int VisualChildrenCount => 2;

    protected override Visual GetVisualChild(int index) => index == 0 ? _mapVisual : _overlayVisual;

    protected override HitTestResult HitTestCore(PointHitTestParameters hitTestParameters) =>
        new PointHitTestResult(this, hitTestParameters.HitPoint);

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        var previous = sizeInfo.PreviousSize;
        var current = sizeInfo.NewSize;
        if (previous.Width > 0 && previous.Height > 0)
            _offset = new Vector(_offset.X * current.Width / previous.Width, _offset.Y * current.Height / previous.Height);
        Invalidate();
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        Invalidate();
    }

    // ================================================================ camera

    private (double Width, double Height) BaseSize() =>
        (Math.Max(1, Math.Floor(ActualWidth) - 1), Math.Max(1, Math.Floor(ActualHeight) - 1));

    private Point CurrentCenter()
    {
        var (w, h) = BaseSize();
        return new Point((_offset.X + w / 2) / (w * _zoom), (_offset.Y + h / 2) / (h * _zoom));
    }

    private static Point ClampCenter(Point center, double zoom)
    {
        double half = 0.5 / zoom;
        return new Point(Math.Clamp(center.X, half, 1 - half), Math.Clamp(center.Y, half, 1 - half));
    }

    private void SetView(double zoom, Point center)
    {
        var (w, h) = BaseSize();
        _zoom = Math.Clamp(zoom, 1, MaxZoom);
        center = ClampCenter(center, _zoom);
        _offset = new Vector(center.X * w * _zoom - w / 2, center.Y * h * _zoom - h / 2);
    }

    private void ClampOffset()
    {
        var (w, h) = BaseSize();
        _offset = new Vector(
            Math.Clamp(_offset.X, 0, w * (_zoom - 1)),
            Math.Clamp(_offset.Y, 0, h * (_zoom - 1)));
    }

    /// <summary>Camera (zoom, normalized center) that makes a laid-out rectangle fill the window.</summary>
    private (double Zoom, Point Center) FitView(Rect bounds)
    {
        var (w, h) = BaseSize();
        double nx = (bounds.X + _offset.X) / (w * _zoom);
        double ny = (bounds.Y + _offset.Y) / (h * _zoom);
        double nw = Math.Max(1e-12, bounds.Width / (w * _zoom));
        double nh = Math.Max(1e-12, bounds.Height / (h * _zoom));
        double zoom = Math.Clamp(Math.Min(1 / nw, 1 / nh), 1, MaxZoom);
        return (zoom, ClampCenter(new Point(nx + nw / 2, ny + nh / 2), zoom));
    }

    private bool IsAt(double zoom, Point center)
    {
        var current = CurrentCenter();
        return Math.Abs(zoom / _zoom - 1) < 1e-3 &&
               Math.Abs(center.X - current.X) * _zoom < 1e-3 &&
               Math.Abs(center.Y - current.Y) * _zoom < 1e-3;
    }

    private FsNode? DeepestLaidOut(FsNode node)
    {
        for (var n = node; n is not null; n = n.Parent)
            if (_index.ContainsKey(n)) return n;
        return null;
    }

    /// <summary>
    /// Jumps to a folder. Header bars have a fixed pixel height, so a folder's relative position
    /// shifts slightly with zoom; a few fit/re-layout passes converge on the exact framing.
    /// </summary>
    private void SnapTo(FsNode folder)
    {
        for (int i = 0; i < 40; i++)
        {
            Rebuild();
            var visible = DeepestLaidOut(folder);
            if (visible is null) break;
            var (zoom, center) = FitView(_index[visible].Bounds);
            if (IsAt(zoom, center)) break; // either done, or the folder is too small to ever appear
            SetView(zoom, center);
        }

        Rebuild();
        ZoomChanged?.Invoke(this, EventArgs.Empty);
    }

    private void StartAnimation(double zoom, Point center, FsNode? target)
    {
        _animation = new CameraAnimation
        {
            Target = target,
            StartZoom = _zoom,
            EndZoom = zoom,
            StartCenter = CurrentCenter(),
            EndCenter = center,
            StartTime = _clock.Elapsed.TotalSeconds
        };

        if (!_renderingHooked)
        {
            CompositionTarget.Rendering += OnRenderingFrame;
            _renderingHooked = true;
        }
    }

    private void StopAnimation()
    {
        _animation = null;
        if (_renderingHooked)
        {
            CompositionTarget.Rendering -= OnRenderingFrame;
            _renderingHooked = false;
        }
    }

    private void OnRenderingFrame(object? sender, EventArgs e)
    {
        var a = _animation;
        if (a is null)
        {
            StopAnimation();
            return;
        }

        double t = Math.Clamp((_clock.Elapsed.TotalSeconds - a.StartTime) / AnimationSeconds, 0, 1);
        double eased = 1 - Math.Pow(1 - t, 3); // ease-out cubic
        double zoom = Math.Exp(Lerp(Math.Log(a.StartZoom), Math.Log(a.EndZoom), eased));

        SetView(zoom, InterpolateCenter(a, zoom, eased));
        Rebuild();

        // The target's exact position is only known once it's laid out at the current zoom; keep refining.
        if (a.Target is not null && _index.TryGetValue(a.Target, out var item))
            (a.EndZoom, a.EndCenter) = FitView(item.Bounds);

        ZoomChanged?.Invoke(this, EventArgs.Empty);

        if (t >= 1)
        {
            StopAnimation();
            if (a.Target is not null) SnapTo(a.Target);
        }
    }

    /// <summary>
    /// Moves the center so the camera appears to zoom toward (or away from) one fixed point,
    /// which looks natural instead of sliding sideways while zooming.
    /// </summary>
    private static Point InterpolateCenter(CameraAnimation a, double zoom, double eased)
    {
        double z0 = a.StartZoom, z1 = a.EndZoom;
        if (Math.Abs(z1 / z0 - 1) < 1e-3)
            return new Point(Lerp(a.StartCenter.X, a.EndCenter.X, eased), Lerp(a.StartCenter.Y, a.EndCenter.Y, eased));

        double px = (a.EndCenter.X * z1 - a.StartCenter.X * z0) / (z1 - z0);
        double py = (a.EndCenter.Y * z1 - a.StartCenter.Y * z0) / (z1 - z0);
        return new Point(px + (a.StartCenter.X - px) * z0 / zoom, py + (a.StartCenter.Y - py) * z0 / zoom);
    }

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;

    /// <summary>Immediate zoom around a screen point (mouse wheel).</summary>
    private void ZoomAt(double newZoom, Point anchor)
    {
        newZoom = Math.Clamp(newZoom, 1, MaxZoom);
        if (Math.Abs(newZoom - _zoom) < 1e-9) return;

        double canvasX = (anchor.X + _offset.X) / _zoom;
        double canvasY = (anchor.Y + _offset.Y) / _zoom;
        _zoom = newZoom;
        _offset = new Vector(canvasX * newZoom - anchor.X, canvasY * newZoom - anchor.Y);
        ClampOffset();

        Invalidate();
        ZoomChanged?.Invoke(this, EventArgs.Empty);
    }

    private void BeginPan(Point position, bool immediately)
    {
        _panArmed = true;
        _panning = immediately;
        _panStart = position;
        _panStartOffset = _offset;
        CaptureMouse();
        if (immediately) StartPanning();
    }

    private void StartPanning()
    {
        _panning = true;
        StopAnimation();
        _pinnedFocus = null;
        _panStartOffset = _offset;
        Cursor = Cursors.SizeAll;
    }

    private void EndPan()
    {
        if (!_panArmed) return;
        _panArmed = false;
        _panning = false;
        Cursor = null;
        if (IsMouseCaptured) ReleaseMouseCapture();
    }

    // ================================================================ layout

    private void Invalidate()
    {
        if (_rebuildPending) return;
        _rebuildPending = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
        {
            if (_rebuildPending) Rebuild();
        }));
    }

    private void EnsureLayout()
    {
        if (_rebuildPending) Rebuild();
    }

    private void Rebuild()
    {
        _rebuildPending = false;
        _items.Clear();
        _index.Clear();
        _matchedGroups.Clear();

        double width = ActualWidth;
        double height = ActualHeight;
        _viewport = new Rect(0, 0, Math.Max(0, width), Math.Max(0, height));
        _drawClip = Rect.Inflate(_viewport, 2, 2);

        if (_root is not null && width >= 8 && height >= 8)
        {
            ClampOffset();
            var (w, h) = BaseSize();
            LayoutNode(_root, new Rect(-_offset.X, -_offset.Y, w * _zoom, h * _zoom), 0, 0);
        }

        double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        using (var dc = _mapVisual.RenderOpen())
        {
            dc.DrawRectangle(MapBackground ?? BackgroundBrush, null, _viewport);
            foreach (var item in _items)
                DrawItem(dc, item, pixelsPerDip);
        }

        if (_hoveredNode is not null && !_index.ContainsKey(_hoveredNode))
            _hoveredNode = null;

        DrawOverlay();
        UpdateFocus();
    }

    private void LayoutNode(FsNode node, Rect bounds, int depth, int branch)
    {
        if (!bounds.IntersectsWith(_viewport)) return;

        // A folder whose only content is one folder (Users > adria > AppData > ...) is drawn as ONE
        // box with a combined title, instead of a stack of nested frames and title bars.
        FsNode? chainTop = null;
        var shown = node;
        while (_mergeChains && SingleSubfolder(shown) is { } only)
        {
            chainTop ??= node;
            shown = only;
        }

        bool hasHeader = shown.IsDirectory && bounds.Width >= MinHeaderWidth * _labelScale && bounds.Height >= MinHeaderHeight * _labelScale;
        var item = new TreemapItem(shown, bounds, depth, hasHeader, chainTop, branch);
        _items.Add(item);

        // Every folder of the chain maps to the same box, so focus/selection/zoom still work for each.
        for (var n = shown; n is not null; n = n.Parent)
        {
            _index[n] = item;
            if (ReferenceEquals(n, node)) break;
        }

        if (!shown.IsDirectory || shown.Children.Count == 0) return;

        // Small boxes get thinner frames; tiny ones aren't subdivided at all, so narrow folders
        // don't turn into a pile of nested outlines.
        double inset = InsetFor(bounds);
        double top = hasHeader ? HeaderHeight : inset;
        double contentWidth = bounds.Width - 2 * inset;
        double contentHeight = bounds.Height - top - inset;
        if (contentWidth < MinFolderContent || contentHeight < MinFolderContent) return;

        var content = new Rect(bounds.X + inset, bounds.Y + top, contentWidth, contentHeight);
        var children = _groupSmall ? GroupSmallChildren(shown, content) : shown.Children;
        int childIndex = 0;
        Squarify.Layout(children, content, _measure, (child, rect) =>
        {
            // Children of the root define the branches; everything below inherits its branch.
            int childBranch = depth == 0 ? childIndex : branch;
            childIndex++;
            if (rect.Width >= MinChildSize && rect.Height >= MinChildSize)
                LayoutNode(child, rect, depth + 1, childBranch);
        });
    }

    /// <summary>
    /// A folder with hundreds of similar files (a photo shoot, a cache) would become a grid of tiny
    /// boxes. Children that would get less than <see cref="GroupBelowArea"/> pixels are replaced by one
    /// "312 files" box. Zooming in gives them more pixels, so they appear individually again.
    /// </summary>
    private IReadOnlyList<FsNode> GroupSmallChildren(FsNode folder, Rect content)
    {
        var children = folder.Children;
        double total = 0;
        foreach (var c in children) total += c.SizeFor(_measure);
        if (total <= 0) return children;

        double pixelsPerByte = content.Width * content.Height / total;
        int cutoff = children.Count;
        for (int i = 0; i < children.Count; i++)
        {
            if (children[i].SizeFor(_measure) * pixelsPerByte < GroupBelowArea)
            {
                cutoff = i;
                break;
            }
        }

        long size = 0, allocated = 0;
        int files = 0, count = 0, folders = 0;
        for (int i = cutoff; i < children.Count; i++)
        {
            var c = children[i];
            if (c.SizeFor(_measure) <= 0) continue;
            size += c.Size;
            allocated += c.Allocated;
            files += c.FileCount;
            count++;
            if (c.IsDirectory) folders++;
        }
        if (count < 2) return children;

        string kind = folders == 0 ? "files" : folders == count ? "folders" : "items";
        var group = new FsNode($"{count:N0} {kind}", folder.FullPath, NodeKind.Group, folder)
        {
            Size = size,
            Allocated = allocated,
            FileCount = files
        };
        if (_filterMatches is not null)
        {
            for (int i = cutoff; i < children.Count; i++)
            {
                if (_filterMatches.Contains(children[i]))
                {
                    _matchedGroups.Add(group);
                    break;
                }
            }
        }

        var list = new List<FsNode>(cutoff + 1);
        for (int i = 0; i < cutoff; i++) list.Add(children[i]);
        list.Add(group);
        list.Sort((a, b) => b.SizeFor(_measure).CompareTo(a.SizeFor(_measure)));
        return list;
    }

    /// <summary>
    /// The folder's dominant child, if it is a folder holding at least 97% of the size. Steam › steamapps › common
    /// still merges when Steam has a few small files next to the big folder.
    /// </summary>
    private FsNode? SingleSubfolder(FsNode node)
    {
        if (!node.IsDirectory || node.Children.Count == 0) return null;
        var first = node.Children[0]; // sorted by size, largest first
        long total = node.SizeFor(_measure);
        if (!first.IsDirectory || total <= 0) return null;
        return first.SizeFor(_measure) * 100 >= total * 97 ? first : null;
    }

    private void UpdateFocus()
    {
        FsNode? focus = null;

        if (_pinnedFocus is not null && _index.ContainsKey(_pinnedFocus))
        {
            focus = _pinnedFocus;
        }
        else if (_root is not null)
        {
            // Deepest folder under the window center that covers at least half the window.
            var center = new Point(_viewport.Width / 2, _viewport.Height / 2);
            double viewArea = _viewport.Width * _viewport.Height;
            foreach (var item in _items)
            {
                if (!item.Node.IsDirectory || !item.Bounds.Contains(center)) continue;
                var visible = Rect.Intersect(item.Bounds, _viewport);
                if (!visible.IsEmpty && visible.Width * visible.Height >= 0.5 * viewArea)
                    focus = item.Node;
            }
            focus ??= _root;
        }

        if (ReferenceEquals(focus, _focus)) return;
        _focus = focus;
        FocusChanged?.Invoke(this, EventArgs.Empty);
    }

    // =============================================================== drawing

    private static readonly Pen LightPen = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(0x8C, 0xFF, 0xFF, 0xFF)), 1));
    private static readonly Pen DarkPen = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(0x59, 0x00, 0x00, 0x00)), 1));
    private static readonly Brush CardShadowNear = Frozen(new SolidColorBrush(Color.FromArgb(0x30, 0x00, 0x00, 0x00)));
    private static readonly Brush CardShadowFar = Frozen(new SolidColorBrush(Color.FromArgb(0x16, 0x00, 0x00, 0x00)));
    private static readonly Brush SoftSheen = Frozen(new LinearGradientBrush(
        new GradientStopCollection
        {
            new(Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF), 0.0),
            new(Color.FromArgb(0x1A, 0x00, 0x00, 0x00), 1.0)
        }, new Point(0, 0), new Point(0, 1)));
    private static readonly Typeface CaptionFace = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
    private static readonly Typeface BoldFace = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

    private void DrawItem(DrawingContext dc, TreemapItem item, double pixelsPerDip)
    {
        var node = item.Node;
        double gap = Gap;
        var full = item.Bounds;
        if (gap > 0)
        {
            if (full.Width <= gap || full.Height <= gap) return;
            full = new Rect(full.X + gap / 2, full.Y + gap / 2, full.Width - gap, full.Height - gap);
        }
        var box = Rect.Intersect(full, _drawClip);
        if (box.IsEmpty) return;

        // ---- fill
        Brush fill;
        if (node.IsFreeSpace) fill = FreeSpaceBrush;
        else if (_mapStyle == MapStyle.Terraces && node.IsDirectory)
            fill = Tint(_scheme.Fill(node, item.Branch, _colorMode), Colors.Black, Math.Min(0.6, item.Depth * 0.12), 10 + Math.Min(item.Depth, 9));
        else if (_mapStyle == MapStyle.Terraces)
            fill = Tint(_scheme.Fill(node, item.Branch, _colorMode), Colors.Black, Math.Min(0.45, item.Depth * 0.08), 20 + Math.Min(item.Depth, 9));
        else fill = _scheme.Fill(node, item.Depth, _colorMode);

        if (node.IsDirectory && !node.IsFreeSpace)
        {
            fill = _mapStyle switch
            {
                MapStyle.Cards => Tint(fill, MapBackground is SolidColorBrush bg ? bg.Color : Color.FromRgb(0x17, 0x17, 0x1C), 0.35, 1),
                MapStyle.Bands => Tint(fill, Colors.White, 0.35, 2),
                MapStyle.Soft => Tint(fill, Colors.White, 0.15, 3),
                _ => fill
            };
        }
        else if (_mapStyle == MapStyle.Soft && !node.IsFreeSpace)
        {
            fill = Tint(fill, Colors.White, 0.10, 4);
        }

        bool dimmed = _filterMatches is not null && !_filterMatches.Contains(node) && !_matchedGroups.Contains(node);
        if (dimmed) fill = Dim(fill);
        var textBrush = dimmed ? DimText : _scheme.LabelFor(fill);
        double radius = Radius;

        // ---- body
        if (_mapStyle == MapStyle.Cards && node.IsDirectory && !dimmed)
        {
            // Two soft layers read as a blur without the cost of a real one.
            dc.DrawRoundedRectangle(CardShadowFar, null, new Rect(box.X - 2, box.Y + 1, box.Width + 4, box.Height + 4), radius + 2, radius + 2);
            dc.DrawRoundedRectangle(CardShadowNear, null, new Rect(box.X - 1, box.Y + 1, box.Width + 2, box.Height + 2), radius + 1, radius + 1);
        }

        if (radius > 0) dc.DrawRoundedRectangle(fill, null, box, radius, radius);
        else dc.DrawRectangle(fill, null, box);

        if (!dimmed)
        {
            if (_mapStyle == MapStyle.Classic && _cushion) dc.DrawRectangle(CushionBrush, null, box);
            if (_mapStyle == MapStyle.Soft) dc.DrawRoundedRectangle(SoftSheen, null, box, radius, radius);
        }
        if (node.IsGroup)
        {
            if (radius > 0) dc.DrawRoundedRectangle(HeaderShade, null, box, radius, radius);
            else dc.DrawRectangle(HeaderShade, null, box);
        }

        var pen = _mapStyle switch { MapStyle.Classic => BorderPen, MapStyle.Bands => LightPen, MapStyle.Terraces => DarkPen, _ => null };
        if (pen is not null) dc.DrawRectangle(null, pen, box);

        // ---- folder title
        if (node.IsDirectory)
        {
            if (!item.HasHeader) return;
            double headerHeight = HeaderHeight;
            var header = Rect.Intersect(new Rect(full.X, full.Y, full.Width, headerHeight), _drawClip);
            if (header.IsEmpty) return;
            double x = Math.Max(header.X, 0);

            switch (_mapStyle)
            {
                case MapStyle.Classic:
                    dc.DrawRectangle(HeaderShade, null, header);
                    DrawLabel(dc, HeaderText(item, header.Width - 8, pixelsPerDip, "  —  "), HeaderFace, textBrush, x + 4, full.Y + 1, header.Width - 8, TextAlignment.Left, pixelsPerDip);
                    break;
                case MapStyle.Bands:
                {
                    var band = dimmed ? fill : Tint(fill, Colors.Black, 0.35, 5);
                    dc.DrawRectangle(band, null, header);
                    DrawLabel(dc, HeaderText(item, header.Width - 8, pixelsPerDip, "  —  "), BoldFace, dimmed ? DimText : _scheme.LabelFor(band), x + 5, full.Y + 2, header.Width - 8, TextAlignment.Left, pixelsPerDip);
                    break;
                }
                case MapStyle.Cards:
                    DrawLabel(dc, HeaderText(item, header.Width - 18, pixelsPerDip, "  ·  "), BoldFace, textBrush, x + 9, full.Y + 4, header.Width - 18, TextAlignment.Left, pixelsPerDip);
                    break;
                case MapStyle.Soft:
                    DrawLabel(dc, HeaderText(item, header.Width - 18, pixelsPerDip, "  ·  "), HeaderFace, textBrush, x + 9, full.Y + 4, header.Width - 18, TextAlignment.Left, pixelsPerDip);
                    break;
                default: // Tiles, Terraces: a small caption, no strip
                    DrawLabel(dc, HeaderText(item, header.Width - 10, pixelsPerDip, "   ", upper: _mapStyle == MapStyle.Tiles), CaptionFace, textBrush, x + 6, full.Y + 2, header.Width - 10, TextAlignment.Left, pixelsPerDip, 10.5);
                    break;
            }
            return;
        }

        // ---- file label
        double line = 15 * _labelScale;
        if (box.Width < 44 * _labelScale || box.Height < line + 1) return;
        double textWidth = box.Width - 6;
        if (box.Height >= line * 2 + 4)
        {
            double y = box.Y + (box.Height - line * 2) / 2;
            DrawLabel(dc, node.Name, NormalFace, textBrush, box.X + 3, y, textWidth, TextAlignment.Center, pixelsPerDip);
            DrawLabel(dc, SizeFormatter.Format(node.SizeFor(_measure)), NormalFace, textBrush, box.X + 3, y + line, textWidth, TextAlignment.Center, pixelsPerDip);
        }
        else
        {
            DrawLabel(dc, node.Name, NormalFace, textBrush, box.X + 3, box.Y + (box.Height - line) / 2, textWidth, TextAlignment.Center, pixelsPerDip);
        }
    }

    /// <summary>A cached blend of a palette brush toward a color; the key separates different blends of the same brush.</summary>
    private Brush Tint(Brush fill, Color toward, double t, int key)
    {
        if (t <= 0) return fill;
        if (_tints.TryGetValue((fill, key), out var tinted)) return tinted;
        var c = fill is SolidColorBrush s ? s.Color : Colors.Gray;
        tinted = Frozen(new SolidColorBrush(Color.FromRgb(
            (byte)Math.Round(c.R + (toward.R - c.R) * t),
            (byte)Math.Round(c.G + (toward.G - c.G) * t),
            (byte)Math.Round(c.B + (toward.B - c.B) * t))));
        _tints[(fill, key)] = tinted;
        return tinted;
    }

    /// <summary>
    /// "Users › adria › AppData — 12 GB" for a collapsed chain. When that doesn't fit, leading folders
    /// are dropped ("… › AppData — 12 GB") so the deepest name, the one that matters, stays readable.
    /// </summary>
    private string HeaderText(TreemapItem item, double maxWidth, double pixelsPerDip, string separator = "  —  ", bool upper = false)
    {
        var node = item.Node;
        string size = SizeFormatter.Format(node.SizeFor(_measure));
        string Case(string text) => upper ? text.ToUpperInvariant() : text;
        if (item.ChainTop is null) return $"{Case(node.Name)}{separator}{size}";

        var names = new List<string>();
        for (var n = node; n is not null; n = n.Parent)
        {
            names.Add(n.Name);
            if (ReferenceEquals(n, item.ChainTop)) break;
        }
        names.Reverse();

        for (int skip = 0; skip < names.Count; skip++)
        {
            string path = Case(string.Join("  ›  ", names.Skip(skip)));
            string text = (skip > 0 ? "…  ›  " : "") + $"{path}{separator}{size}";
            if (skip == names.Count - 1 || MeasureWidth(text, pixelsPerDip) <= maxWidth) return text;
        }

        return $"{Case(node.Name)}{separator}{size}";
    }

    private double MeasureWidth(string text, double pixelsPerDip) =>
        new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            HeaderFace, TextSize * _labelScale, TextBrush, pixelsPerDip).WidthIncludingTrailingWhitespace;

    private void DrawLabel(DrawingContext dc, string text, Typeface face, Brush brush, double x, double y,
        double maxWidth, TextAlignment alignment, double pixelsPerDip, double size = TextSize)
    {
        if (maxWidth < 8) return;

        var formatted = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            face, size * _labelScale, brush, pixelsPerDip)
        {
            MaxTextWidth = maxWidth,
            MaxLineCount = 1,
            Trimming = TextTrimming.CharacterEllipsis,
            TextAlignment = alignment
        };
        var origin = new Point(x, y);

        if (_labelHalo)
        {
            // Outline in the opposite tone of the text, so it reads on any fill.
            bool darkText = brush is SolidColorBrush b && (0.299 * b.Color.R + 0.587 * b.Color.G + 0.114 * b.Color.B) < 128;
            dc.DrawGeometry(null, darkText ? LightHaloPen : DarkHaloPen, formatted.BuildGeometry(origin));
        }
        dc.DrawText(formatted, origin);
    }

    private static readonly Brush DimText = Frozen(new SolidColorBrush(Color.FromArgb(0x70, 0x80, 0x80, 0x88)));

    /// <summary>A washed-out version of a fill, blended toward the map background, for non-matching items.</summary>
    private Brush Dim(Brush fill)
    {
        if (_dimmed.TryGetValue(fill, out var dim)) return dim;

        var c = fill is SolidColorBrush s ? s.Color : Colors.Gray;
        var bg = MapBackground is SolidColorBrush b ? b.Color : Color.FromRgb(0x17, 0x17, 0x1C);
        // Desaturate toward gray first, then blend 65% into the background.
        byte gray = (byte)Math.Round(0.299 * c.R + 0.587 * c.G + 0.114 * c.B);
        Color faded = Color.FromRgb(
            (byte)Math.Round((gray * 0.6 + c.R * 0.4) * 0.35 + bg.R * 0.65),
            (byte)Math.Round((gray * 0.6 + c.G * 0.4) * 0.35 + bg.G * 0.65),
            (byte)Math.Round((gray * 0.6 + c.B * 0.4) * 0.35 + bg.B * 0.65));
        dim = Frozen(new SolidColorBrush(faded));
        _dimmed[fill] = dim;
        return dim;
    }

    private void DrawOverlay()
    {
        using var dc = _overlayVisual.RenderOpen();

        if (_hoveredNode is not null && !_selection.Contains(_hoveredNode) &&
            _index.TryGetValue(_hoveredNode, out var hovered))
        {
            var r = Rect.Intersect(hovered.Bounds, _drawClip);
            if (!r.IsEmpty)
            {
                dc.DrawRectangle(HoverFill, HoverOutlinePen, r);
                dc.DrawRectangle(null, HoverPen, r);
            }
        }

        foreach (var node in _selection)
        {
            if (!_index.TryGetValue(node, out var selected)) continue;
            var r = Rect.Intersect(selected.Bounds, _drawClip);
            if (!r.IsEmpty) dc.DrawRectangle(null, SelectionPen, r);
        }
    }

    // ================================================================= input

    /// <summary>Returns the deepest node under the point.</summary>
    public FsNode? NodeAt(Point point)
    {
        for (int i = _items.Count - 1; i >= 0; i--)
        {
            if (_items[i].Bounds.Contains(point))
                return _items[i].Node;
        }
        return null;
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (_root is null) return;
        StopAnimation();
        _pinnedFocus = null;
        ZoomAt(_zoom * Math.Pow(WheelStep, e.Delta / 120.0), e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var position = e.GetPosition(this);

        if (_panArmed && !_panning && (position - _panStart).Length >= DragThreshold)
        {
            StartPanning();
            _panStartOffset = _offset;
            _panStart = position;
        }

        if (_panning)
        {
            _offset = _panStartOffset - (position - _panStart);
            ClampOffset();
            Invalidate();
            return;
        }

        var node = NodeAt(position);
        if (ReferenceEquals(node, _hoveredNode)) return;
        _hoveredNode = node;
        DrawOverlay();
        HoveredNodeChanged?.Invoke(this, node);
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (_panning || _hoveredNode is null) return;
        _hoveredNode = null;
        DrawOverlay();
        HoveredNodeChanged?.Invoke(this, null);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        var position = e.GetPosition(this);
        var node = NodeAt(position);
        if (node is not null)
        {
            var modifiers = Keyboard.Modifiers;
            if (e.ClickCount == 1 && modifiers.HasFlag(ModifierKeys.Control)) ToggleSelected(node);
            else if (e.ClickCount == 1 && modifiers.HasFlag(ModifierKeys.Shift)) SelectRangeTo(node);
            else SelectedNode = node;
        }

        if (e.ClickCount == 2)
        {
            EndPan();
            if (node is not null) NodeActivated?.Invoke(this, node);
        }
        else if (_zoom > 1)
        {
            BeginPan(position, immediately: false);
        }

        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        EndPan();
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.ChangedButton == MouseButton.Middle && _zoom > 1)
        {
            BeginPan(e.GetPosition(this), immediately: true);
            e.Handled = true;
        }
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.ChangedButton == MouseButton.Middle) EndPan();
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        _panArmed = false;
        _panning = false;
        Cursor = null;
    }

    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonDown(e);
        Focus();
        var node = NodeAt(e.GetPosition(this));
        // Right-clicking something outside the selection selects just that; inside keeps the selection.
        if (node is not null && !_selection.Contains(node))
            SelectedNode = node;
    }

    private static T Frozen<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
