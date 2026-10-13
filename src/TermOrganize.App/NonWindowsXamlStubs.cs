// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

// Compiled only for -p:EnableWindowsTargeting=true on a non-Windows host.
// The WinUI markup compiler is a Windows executable, so it cannot emit
// InitializeComponent or the x:Name fields here. Windows builds exclude this
// file and use the generated partials instead.

using H.NotifyIcon;
using Microsoft.UI.Xaml.Controls;

namespace TermOrganize;

public partial class App
{
    public void InitializeComponent()
    {
    }
}

public sealed partial class MainWindow
{
    internal Grid TitleBar = null!;
    internal ColumnDefinition CaptionSpacer = null!;
    internal ComboBox TermPicker = null!;
    internal NavigationView Nav = null!;
    internal Grid Host = null!;
    internal TextBlock Disclaimer = null!;
    internal TaskbarIcon Tray = null!;
    internal TextBlock UpNextText = null!;

    public void InitializeComponent()
    {
    }
}
