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
public readonly record struct TreemapItem(FsNode Node, Rect Bounds, int Depth, bool HasHeader, FsNode? ChainTop);

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

    private const double HeaderHeight = 17;
    private const double MinHeaderWidth = 60;   // folders smaller than this get no title bar
    private const double MinHeaderHeight = 44;
    private const double MinChildSize = 4;       // smaller children are not drawn (the parent's color shows)
    private const double MinFolderContent = 12;  // don't subdivide folders with less room than this
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
    private FsNode? _selectedNode;
    private ColorMode _colorMode = ColorMode.ByDepth;
    private ColorScheme _scheme = Palette.Default;
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
        new PropertyMetadata(BackgroundBrush, (d, _) => ((TreemapControl)d).Invalidate()));

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

    public FsNode? SelectedNode
    {
        get => _selectedNode;
        set
        {
            if (ReferenceEquals(_selectedNode, value)) return;
            _selectedNode = value;
            DrawOverlay();
            SelectionChanged?.Invoke(this, value);
        }
    }

    /// <summary>The color palette used to fill boxes.</summary>
    public ColorScheme Scheme
    {
        get => _scheme;
        set
        {
            if (ReferenceEquals(_scheme, value)) return;
            _scheme = value;
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
        if (!animate || visible is null || ActualWidth < 8)
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

        double width = ActualWidth;
        double height = ActualHeight;
        _viewport = new Rect(0, 0, Math.Max(0, width), Math.Max(0, height));
        _drawClip = Rect.Inflate(_viewport, 2, 2);

        if (_root is not null && width >= 8 && height >= 8)
        {
            ClampOffset();
            var (w, h) = BaseSize();
            LayoutNode(_root, new Rect(-_offset.X, -_offset.Y, w * _zoom, h * _zoom), 0);
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

    private void LayoutNode(FsNode node, Rect bounds, int depth)
    {
        if (!bounds.IntersectsWith(_viewport)) return;

        // A folder whose only content is one folder (Users > adria > AppData > ...) is drawn as ONE
        // box with a combined title, instead of a stack of nested frames and title bars.
        FsNode? chainTop = null;
        var shown = node;
        while (SingleSubfolder(shown) is { } only)
        {
            chainTop ??= node;
            shown = only;
        }

        bool hasHeader = shown.IsDirectory && bounds.Width >= MinHeaderWidth && bounds.Height >= MinHeaderHeight;
        var item = new TreemapItem(shown, bounds, depth, hasHeader, chainTop);
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
        double inset = Math.Min(bounds.Width, bounds.Height) >= 40 ? 2 : 1;
        double top = hasHeader ? HeaderHeight : inset;
        double contentWidth = bounds.Width - 2 * inset;
        double contentHeight = bounds.Height - top - inset;
        if (contentWidth < MinFolderContent || contentHeight < MinFolderContent) return;

        var content = new Rect(bounds.X + inset, bounds.Y + top, contentWidth, contentHeight);
        Squarify.Layout(shown.Children, content, (child, rect) =>
        {
            if (rect.Width >= MinChildSize && rect.Height >= MinChildSize)
                LayoutNode(child, rect, depth + 1);
        });
    }

    /// <summary>The folder's only non-empty child, if that child is a folder.</summary>
    private static FsNode? SingleSubfolder(FsNode node)
    {
        if (!node.IsDirectory || node.Children.Count == 0) return null;
        var first = node.Children[0]; // sorted by size, largest first
        if (!first.IsDirectory || first.Size == 0) return null;
        return node.Children.Count == 1 || node.Children[1].Size == 0 ? first : null;
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

    private void DrawItem(DrawingContext dc, TreemapItem item, double pixelsPerDip)
    {
        var full = item.Bounds;
        var visible = Rect.Intersect(full, _drawClip);
        if (visible.IsEmpty) return;

        var node = item.Node;
        var fill = _scheme.Fill(node, item.Depth, _colorMode);
        var textBrush = _scheme.LabelFor(fill);
        dc.DrawRectangle(fill, BorderPen, visible);

        if (node.IsDirectory)
        {
            if (!item.HasHeader) return;
            var header = Rect.Intersect(new Rect(full.X, full.Y, full.Width, HeaderHeight), _drawClip);
            if (header.IsEmpty) return;
            dc.DrawRectangle(HeaderShade, null, header);
            DrawLabel(dc, HeaderText(item, header.Width - 8, pixelsPerDip), HeaderFace, textBrush,
                Math.Max(header.X, 0) + 4, full.Y + 1, header.Width - 8, TextAlignment.Left, pixelsPerDip);
            return;
        }

        if (visible.Width < 44 || visible.Height < 16) return;

        double textWidth = visible.Width - 6;
        if (visible.Height >= 34)
        {
            double y = visible.Y + (visible.Height - 30) / 2;
            DrawLabel(dc, node.Name, NormalFace, textBrush, visible.X + 3, y, textWidth, TextAlignment.Center, pixelsPerDip);
            DrawLabel(dc, SizeFormatter.Format(node.Size), NormalFace, textBrush, visible.X + 3, y + 15, textWidth, TextAlignment.Center, pixelsPerDip);
        }
        else
        {
            DrawLabel(dc, node.Name, NormalFace, textBrush, visible.X + 3, visible.Y + (visible.Height - 15) / 2, textWidth, TextAlignment.Center, pixelsPerDip);
        }
    }

    /// <summary>
    /// "Users › adria › AppData — 12 GB" for a collapsed chain. When that doesn't fit, leading folders
    /// are dropped ("… › AppData — 12 GB") so the deepest name, the one that matters, stays readable.
    /// </summary>
    private static string HeaderText(TreemapItem item, double maxWidth, double pixelsPerDip)
    {
        var node = item.Node;
        string size = SizeFormatter.Format(node.Size);
        if (item.ChainTop is null) return $"{node.Name}  —  {size}";

        var names = new List<string>();
        for (var n = node; n is not null; n = n.Parent)
        {
            names.Add(n.Name);
            if (ReferenceEquals(n, item.ChainTop)) break;
        }
        names.Reverse();

        for (int skip = 0; skip < names.Count; skip++)
        {
            string path = string.Join("  ›  ", names.Skip(skip));
            string text = (skip > 0 ? "…  ›  " : "") + $"{path}  —  {size}";
            if (skip == names.Count - 1 || MeasureWidth(text, pixelsPerDip) <= maxWidth) return text;
        }

        return $"{node.Name}  —  {size}";
    }

    private static double MeasureWidth(string text, double pixelsPerDip) =>
        new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            HeaderFace, TextSize, TextBrush, pixelsPerDip).WidthIncludingTrailingWhitespace;

    private static void DrawLabel(DrawingContext dc, string text, Typeface face, Brush brush, double x, double y,
        double maxWidth, TextAlignment alignment, double pixelsPerDip)
    {
        if (maxWidth < 8) return;

        var formatted = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            face, TextSize, brush, pixelsPerDip)
        {
            MaxTextWidth = maxWidth,
            MaxLineCount = 1,
            Trimming = TextTrimming.CharacterEllipsis,
            TextAlignment = alignment
        };
        dc.DrawText(formatted, new Point(x, y));
    }

    private void DrawOverlay()
    {
        using var dc = _overlayVisual.RenderOpen();

        if (_hoveredNode is not null && !ReferenceEquals(_hoveredNode, _selectedNode) &&
            _index.TryGetValue(_hoveredNode, out var hovered))
        {
            var r = Rect.Intersect(hovered.Bounds, _drawClip);
            if (!r.IsEmpty)
            {
                dc.DrawRectangle(HoverFill, HoverOutlinePen, r);
                dc.DrawRectangle(null, HoverPen, r);
            }
        }

        if (_selectedNode is not null && _index.TryGetValue(_selectedNode, out var selected))
        {
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
            SelectedNode = node;

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
        if (node is not null)
            SelectedNode = node;
    }

    private static T Frozen<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
