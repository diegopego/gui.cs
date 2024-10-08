﻿namespace Terminal.Gui;

/// <summary>Describes a change in <see cref="TabView.SelectedTab"/></summary>
public class TabChangedEventArgs : EventArgs
{
    /// <summary>Documents a tab change</summary>
    /// <param name="oldTab"></param>
    /// <param name="newTab"></param>
    public TabChangedEventArgs (Tab oldTab, Tab newTab)
    {
        OldTab = oldTab;
        NewTab = newTab;
    }

    /// <summary>The currently selected tab. May be null</summary>
    public Tab NewTab { get; }

    /// <summary>The previously selected tab. May be null</summary>
    public Tab OldTab { get; }
}
