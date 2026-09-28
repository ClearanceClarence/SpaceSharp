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

/// <summary>Part of <see cref="TreemapControl"/>. TreemapControl.cs has the overview.</summary>
public sealed partial class TreemapControl
{
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
}
