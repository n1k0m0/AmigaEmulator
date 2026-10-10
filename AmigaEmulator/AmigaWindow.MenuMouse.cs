// Copyright 2026 Nils Kopal
// SPDX-License-Identifier: Apache-2.0
using System.Collections.Generic;
using System.Drawing;

namespace AmigaEmulator;

public sealed partial class AmigaWindow
{
    // Hit testing uses the rows actually rendered, including scrolled media/ROM
    // entries. A click never targets stale geometry belonging to another page.
    private readonly List<MenuMouseRow> _overlayMenuMouseRows = new();
    private OverlayMode _overlayMenuMouseMode;
    private Size _overlayMenuMouseClientSize;
    private readonly record struct MenuMouseRow(Rectangle Bounds, int Selection);

    private bool SelectOverlayItemAt(Point point)
    {
        if (_overlayMode == OverlayMode.None || _overlayMode != _overlayMenuMouseMode || ClientSize != _overlayMenuMouseClientSize) return false;
        foreach (MenuMouseRow row in _overlayMenuMouseRows)
        {
            if (!row.Bounds.Contains(point)) continue;
            SetSelection(row.Selection);
            return true;
        }
        return false;
    }
}
