using System.Diagnostics;
using System.Reflection.Metadata.Ecma335;
using Microsoft.CodeAnalysis.Operations;
using static Terminal.Gui.FakeDriver;

namespace Terminal.Gui;

public partial class View // Focus and cross-view navigation management (TabStop, TabIndex, etc...)
{
    #region HasFocus

    // Backs `HasFocus` and is the ultimate source of truth whether a View has focus or not.
    private bool _hasFocus;

    /// <summary>
    ///     Gets or sets whether this view has focus.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Only Views that are visible, enabled, and have <see cref="CanFocus"/> set to <see langword="true"/> are focusable. If
    ///         these conditions are not met when this property is set to <see langword="true"/> <see cref="HasFocus"/> will not change.
    ///     </para>
    ///     <para>
    ///         Setting this property causes the <see cref="OnHasFocusChanging"/> and <see cref="OnHasFocusChanged"/> virtual methods (and <see cref="HasFocusChanging"/> and
    ///         <see cref="HasFocusChanged"/> events to be raised). If the event is cancelled, <see cref="HasFocus"/> will not be changed.
    ///     </para>
    ///     <para>
    ///         Setting this property to <see langword="true"/> will recursively set <see cref="HasFocus"/> to
    ///         <see langword="true"/> for all SuperViews up the hierarchy.
    ///     </para>
    ///     <para>
    ///         Setting this property to <see langword="true"/> will cause the subview furthest down the hierarchy that is
    ///         focusable to also gain focus (as long as <see cref="TabStop"/>
    ///     </para>
    ///     <para>
    ///         Setting this property to <see langword="false"/> will cause <see cref="ApplicationNavigation.MoveNextView"/> to set
    ///         the focus on the next view to be focused.
    ///     </para>
    /// </remarks>
    public bool HasFocus
    {
        set
        {
            if (HasFocus != value)
            {
                if (value)
                {
                    // NOTE: If Application.Navigation is null, we pass null to FocusChanging. For unit tests.
                    (bool focusSet, bool _) = SetHasFocusTrue (Application.Navigation?.GetFocused ());
                    if (focusSet)
                    {
                        // The change happened
                        // HasFocus is now true
                    }
                }
                else
                {
                    SetHasFocusFalse (null);
                }
            }
        }
        get
        {
            return _hasFocus;
        }
    }

    /// <summary>
    ///     Causes this view to be focused. Calling this method has the same effect as setting <see cref="HasFocus"/> to
    ///     <see langword="true"/> but with the added benefit of returning a value indicating whether the focus was set.
    /// </summary>
    public bool SetFocus ()
    {
        (bool focusSet, bool _) = SetHasFocusTrue (Application.Navigation?.GetFocused ());
        return focusSet;
    }

    /// <summary>
    ///     INTERNAL: Called when focus is going to change to this view. This method is called by <see cref="SetFocus"/> and other methods that
    ///     set or remove focus from a view.
    /// </summary>
    /// <param name="previousFocusedView">The previously focused view. If <see langword="null"/> there is no previously focused view.</param>
    /// <param name="traversingUp"></param>
    /// <returns><see langword="true"/> if <see cref="HasFocus"/> was changed to <see langword="true"/>.</returns>
    /// <exception cref="InvalidOperationException"></exception>
    private (bool focusSet, bool cancelled) SetHasFocusTrue ([CanBeNull] View previousFocusedView, bool traversingUp = false)
    {
        Debug.Assert (ApplicationNavigation.IsInHierarchy (SuperView, this));

        // Pre-conditions
        if (_hasFocus)
        {
            return (false, false);
        }

        if (CanFocus && SuperView is { CanFocus: false })
        {
            Debug.WriteLine ($@"WARNING: Attempt to FocusChanging where SuperView.CanFocus == false. {this}");
            return (false, false);
        }

        if (!CanBeVisible (this) || !Enabled)
        {
            return (false, false);
        }

        if (!CanFocus)
        {
            return (false, false);
        }

        bool previousValue = HasFocus;

        bool cancelled = NotifyFocusChanging (false, true, previousFocusedView, this);

        if (cancelled)
        {
            return (false, true);
        }

        //// If we previously had a subview with focus (`Focused = subview`), we need to make sure that all subviews down the `subview`-hierarchy LeaveFocus.
        //// LeaveFocus will recurse down the subview hierarchy and will also set PreviouslyMostFocused
        //View focused = Focused;
        //focused?.SetHasFocusFalse (this, true);

        // Make sure superviews up the superview hierarchy have focus.
        // Any of them may cancel gaining focus. In which case we need to back out.
        if (SuperView is { HasFocus: false } sv)
        {
            (bool focusSet, bool svCancelled) = sv.SetHasFocusTrue (previousFocusedView, true);
            if (!focusSet)
            {
                return (false, svCancelled);
            }
        }

        if (_hasFocus)
        {
            // Something else beat us to the change (likely a FocusChanged handler).
            return (true, false);
        }

        // By setting _hasFocus to true we definitively change HasFocus for this view.

        // Get whatever peer has focus, if any
        View focusedPeer = SuperView?.Focused;

        _hasFocus = true;

        // Ensure that the peer loses focus
        focusedPeer?.SetHasFocusFalse (this, true);

        if (!traversingUp)
        {
            // Restore focus to the previously most focused subview in the subview-hierarchy
            if (!RestoreFocus (TabStop))
            {
                // Couldn't restore focus, so use Advance to navigate to the next focusable subview
                if (!AdvanceFocus (NavigationDirection.Forward, null))
                {
                    // Couldn't advance, so we're the most focused view in the application
                    _previouslyMostFocused = null;
                    Application.Navigation?.SetFocused (this);
                    //NotifyFocusChanged (HasFocus, previousFocusedView, this);
                }
            }
        }

        if (previousFocusedView is { HasFocus: true } && Subviews.Contains (previousFocusedView))
        {
            previousFocusedView.SetHasFocusFalse (this, false);
        }
        NotifyFocusChanged (HasFocus, previousFocusedView, this);
        SetNeedsDisplay ();

        // Post-conditions - prove correctness
        if (HasFocus == previousValue)
        {
            throw new InvalidOperationException ($"NotifyFocusChanging was not cancelled and the HasFocus value did not change.");
        }

        return (true, false);
    }


    private bool NotifyFocusChanging (bool currentHasFocus, bool newHasFocus, [CanBeNull] View currentFocused, [CanBeNull] View newFocused)
    {
        // Call the virtual method
        if (OnHasFocusChanging (currentHasFocus, newHasFocus, currentFocused, newFocused))
        {
            // The event was cancelled
            return true;
        }

        var args = new HasFocusEventArgs (currentHasFocus, newHasFocus, currentFocused, newFocused);
        HasFocusChanging?.Invoke (this, args);

        if (args.Cancel)
        {
            // The event was cancelled
            return true;
        }

        return false;
    }

    /// <summary>
    ///     Invoked when <see cref="View.HasFocus"/> is about to change. This method is called before the <see cref="HasFocusChanging"/> event is raised.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Use <see cref="OnHasFocusChanged"/> to be notified after the focus has changed.
    ///     </para>
    /// </remarks>
    /// <param name="currentHasFocus">The current value of <see cref="View.HasFocus"/>.</param>
    /// <param name="newHasFocus">The value <see cref="View.HasFocus"/> will have if the focus change happens.</param>
    /// <param name="currentFocused">The view that is currently Focused. May be <see langword="null"/>.</param>
    /// <param name="newFocused">The view that will be focused. May be <see langword="null"/>.</param>
    /// <returns> <see langword="true"/>, if the change to <see cref="View.HasFocus"/> is to be cancelled, <see langword="false"/> otherwise.</returns>
    protected virtual bool OnHasFocusChanging (bool currentHasFocus, bool newHasFocus, [CanBeNull] View currentFocused, [CanBeNull] View newFocused)
    {
        return false;
    }

    /// <summary>
    ///     Raised when <see cref="View.HasFocus"/> is about to change.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Cancel the event to prevent the focus from changing.
    ///     </para>
    ///     <para>
    ///         Use <see cref="HasFocusChanged"/> to be notified after the focus has changed.
    ///     </para>
    /// </remarks>
    public event EventHandler<HasFocusEventArgs> HasFocusChanging;

    /// <summary>
    ///     Called when this view should stop being focused.
    /// </summary>
    /// <param name="newFocusedVew">The new focused view. If <see langword="null"/> it is not known which view will be focused.</param>
    /// <exception cref="InvalidOperationException"></exception>
    private void SetHasFocusFalse ([CanBeNull] View newFocusedVew, bool traversingDown = false)
    {
        // Pre-conditions
        if (!_hasFocus)
        {
            throw new InvalidOperationException ($"SetHasFocusFalse should not be called if the view does not have focus.");
        }

        // If newFocusedVew is null, we need to find the view that should get focus, and SetFocus on it.
        if (!traversingDown && newFocusedVew is null)
        {
            if (SuperView?._previouslyMostFocused is { } && SuperView?._previouslyMostFocused != this)
            {
                SuperView?._previouslyMostFocused?.SetFocus ();

                // The above will cause SetHasFocusFalse, so we can return
                return;
            }

            if (SuperView is { } && SuperView.AdvanceFocus (NavigationDirection.Forward, TabStop))
            {
                // The above will cause SetHasFocusFalse, so we can return
                return;
            }

            if (Application.Navigation is { } && Application.Current is { })
            {
                // Temporarily ensure this view can't get focus
                bool prevCanFocus = _canFocus;
                _canFocus = false;
                bool restoredFocus = Application.Current!.RestoreFocus (null);
                _canFocus = prevCanFocus;

                if (restoredFocus)
                {
                    // The above caused SetHasFocusFalse, so we can return
                    return;
                }
            }

            // No other focusable view to be found. Just "leave" us...
        }

        // Before we can leave focus, we need to make sure that all views down the subview-hierarchy have left focus.
        View mostFocused = MostFocused;
        if (mostFocused is { } && (newFocusedVew is null || mostFocused != newFocusedVew))
        {
            // Start at the bottom and work our way up to us
            View bottom = mostFocused;

            while (bottom is { } && bottom != this)
            {
                if (bottom.HasFocus)
                {
                    bottom.SetHasFocusFalse (newFocusedVew, true);
                }
                bottom = bottom.SuperView;
            }
            _previouslyMostFocused = mostFocused;
        }

        bool previousValue = HasFocus;

        // Note, can't be cancelled.
        NotifyFocusChanging (HasFocus, !HasFocus, newFocusedVew, this);

        // Get whatever peer has focus, if any
        View focusedPeer = SuperView?.Focused;
        _hasFocus = false;

        NotifyFocusChanged (HasFocus, this, newFocusedVew);

        if (_hasFocus)
        {
            // Notify caused HasFocus to change to true.
            return;
        }

        if (SuperView is { })
        {
            SuperView._previouslyMostFocused = focusedPeer;
        }

        // Post-conditions - prove correctness
        if (HasFocus == previousValue)
        {
            throw new InvalidOperationException ($"SetHasFocusFalse and the HasFocus value did not change.");
        }

        SetNeedsDisplay ();
    }

    /// <summary>
    ///     Caches the most focused subview when this view is losing focus. This is used by <see cref="RestoreFocus"/>.
    /// </summary>
    [CanBeNull]
    private View _previouslyMostFocused;

    private void NotifyFocusChanged (bool newHasFocus, [CanBeNull] View previousFocusedView, [CanBeNull] View focusedVew)
    {
        // Call the virtual method
        OnHasFocusChanged (newHasFocus, previousFocusedView, focusedVew);

        // Raise the event
        var args = new HasFocusEventArgs (newHasFocus, newHasFocus, previousFocusedView, focusedVew);
        HasFocusChanged?.Invoke (this, args);
    }

    /// <summary>
    ///     Invoked after <see cref="HasFocus"/> has changed. This method is called before the <see cref="HasFocusChanged"/> event is raised.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         This event cannot be cancelled.
    ///     </para>
    /// </remarks>
    /// <param name="newHasFocus">The new value of <see cref="View.HasFocus"/>.</param>
    /// <param name="previousFocusedView"></param>
    /// <param name="focusedVew">The view that is now focused. May be <see langword="null"/></param>
    protected virtual void OnHasFocusChanged (bool newHasFocus, [CanBeNull] View previousFocusedView, [CanBeNull] View focusedVew)
    {
        return;
    }

    /// <summary>Raised after <see cref="HasFocus"/> has changed.</summary>
    /// <remarks>
    ///     <para>
    ///         This event cannot be cancelled.
    ///     </para>
    /// </remarks>
    public event EventHandler<HasFocusEventArgs> HasFocusChanged;

    #endregion HasFocus

    /// <summary>
    ///     Advances the focus to the next or previous view in <see cref="View.TabIndexes"/>, based on
    ///     <paramref name="direction"/>.
    ///     itself.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         If there is no next/previous view, the focus is set to the view itself.
    ///     </para>
    /// </remarks>
    /// <param name="direction"></param>
    /// <param name="behavior"></param>
    /// <returns>
    ///     <see langword="true"/> if focus was changed to another subview (or stayed on this one), <see langword="false"/>
    ///     otherwise.
    /// </returns>
    public bool AdvanceFocus (NavigationDirection direction, TabBehavior? behavior)
    {
        if (!CanBeVisible (this)) // TODO: is this check needed?
        {
            return false;
        }

        if (TabIndexes is null || TabIndexes.Count == 0)
        {
            return false;
        }

        View focused = Focused;

        if (focused is { } && focused.AdvanceFocus (direction, behavior))
        {
            return true;
        }

        View [] index = GetScopedTabIndexes (direction, behavior);

        if (index.Length == 0)
        {
            return false;
        }

        var focusedIndex = index.IndexOf (Focused);
        int next = 0;

        if (focusedIndex < index.Length - 1)
        {
            // We're moving w/in the subviews
            next = focusedIndex + 1;
        }
        else
        {
            // We're moving beyond the last subview

            // Determine if focus should remain in this focus chain, or move to the superview's focus chain
            if (SuperView is { } && SuperView.GetScopedTabIndexes (direction, behavior).Length > 1)
            {
                // Our superview has an focusable subview in addition to us
                return false;
            }

            // If our superview 
            //if (behavior == TabBehavior.TabGroup && behavior == TabStop && SuperView?.TabStop == TabBehavior.TabGroup)
            {
                next = 0;
                //return 

                //// Go down the subview-hierarchy and leave
                //// BUGBUG: This doesn't seem right
                //Focused.HasFocus = false;

                //// TODO: Should we check the return value of SetHasFocus?

                //return false;
            }
        }

        View view = index [next];

        if (view.HasFocus)
        {
            // We could not advance
            return view == this;
        }

        // The subview does not have focus, but at least one other that can. Can this one be focused?
        (bool focusSet, bool _) = view.SetHasFocusTrue (Focused);
        return focusSet;
    }


    /// <summary>
    /// INTERNAL API to restore focus to the subview that had focus before this view lost focus.
    /// </summary>
    /// <returns>
    ///     Returns true if focus was restored to a subview, false otherwise.
    /// </returns>
    internal bool RestoreFocus (TabBehavior? behavior)
    {
        if (Focused is null && _subviews?.Count > 0)
        {
            if (_previouslyMostFocused is { }/* && (behavior is null || _previouslyMostFocused.TabStop == behavior)*/)
            {
                return _previouslyMostFocused.SetFocus ();
            }
            return false;
        }

        return false;
    }

    /// <summary>
    ///     Returns the most focused Subview down the subview-hierarchy.
    /// </summary>
    /// <value>The most focused Subview, or <see langword="null"/> if no Subview is focused.</value>
    public View MostFocused
    {
        get
        {
            // TODO: Remove this API. It's duplicative of Application.Navigation.GetFocused.
            if (Focused is null)
            {
                return null;
            }

            View most = Focused!.MostFocused;

            if (most is { })
            {
                return most;
            }

            return Focused;
        }
    }

    ///// <summary>
    /////     Internal API that causes <paramref name="viewToEnterFocus"/> to enter focus.
    /////     <paramref name="viewToEnterFocus"/> must be a subview.
    /////     Recursively sets focus up the superview hierarchy.
    ///// </summary>
    ///// <param name="viewToEnterFocus"></param>
    ///// <returns><see langword="true"/> if <paramref name="viewToEnterFocus"/> got focus.</returns>
    //private bool SetFocus (View viewToEnterFocus)
    //{
    //    if (viewToEnterFocus is null)
    //    {
    //        return false;
    //    }

    //    if (!viewToEnterFocus.CanFocus || !viewToEnterFocus.Visible || !viewToEnterFocus.Enabled)
    //    {
    //        return false;
    //    }

    //    // If viewToEnterFocus is already the focused view, don't do anything
    //    if (Focused?._hasFocus == true && Focused == viewToEnterFocus)
    //    {
    //        return false;
    //    }

    //    // If a subview has focus and viewToEnterFocus is the focused view's superview OR viewToEnterFocus is this view,
    //    // then make viewToEnterFocus.HasFocus = true and return
    //    if ((Focused?._hasFocus == true && Focused?.SuperView == viewToEnterFocus) || viewToEnterFocus == this)
    //    {
    //        if (!viewToEnterFocus._hasFocus)
    //        {
    //            viewToEnterFocus._hasFocus = true;
    //        }

    //        // viewToEnterFocus is already focused
    //        return true;
    //    }

    //    // Make sure that viewToEnterFocus is a subview of this view
    //    View c;

    //    for (c = viewToEnterFocus._superView; c != null; c = c._superView)
    //    {
    //        if (c == this)
    //        {
    //            break;
    //        }
    //    }

    //    if (c is null)
    //    {
    //        throw new ArgumentException (@$"The specified view {viewToEnterFocus} is not part of the hierarchy of {this}.");
    //    }

    //    // If a subview has focus, make it leave focus. This will leave focus up the hierarchy.
    //    Focused?.SetHasFocus (false, viewToEnterFocus);

    //    // make viewToEnterFocus Focused and enter focus
    //    View f = Focused;
    //    Focused = viewToEnterFocus;
    //    Focused?.SetHasFocus (true, f, true);
    //    Focused?.FocusDeepest (null, NavigationDirection.Forward);

    //    // Recursively set focus up the superview hierarchy
    //    if (SuperView is { })
    //    {
    //        // BUGBUG: If focus is cancelled at any point, we should stop and restore focus to the previous focused view
    //        SuperView.SetFocus (this);
    //    }
    //    else
    //    {
    //        // BUGBUG: this makes no sense in the new design
    //        // If there is no SuperView, then this is a top-level view
    //        SetFocus (this);

    //    }

    //    // TODO: Temporary hack to make Application.Navigation.FocusChanged work
    //    if (HasFocus && Focused.Focused is null)
    //    {
    //        Application.Navigation?.SetFocused (Focused);
    //    }

    //    // TODO: This is a temporary hack to make overlapped non-Toplevels have a zorder. See also: View.OnDrawContent.
    //    if (viewToEnterFocus is { } && (viewToEnterFocus.TabStop == TabBehavior.TabGroup && viewToEnterFocus.Arrangement.HasFlag (ViewArrangement.Overlapped)))
    //    {
    //        viewToEnterFocus.TabIndex = 0;
    //    }

    //    return true;
    //}


#if AUTO_CANFOCUS
    // BUGBUG: This is a poor API design. Automatic behavior like this is non-obvious and should be avoided. Instead, callers to Add should be explicit about what they want.
    // Set to true in Add() to indicate that the view being added to a SuperView has CanFocus=true.
    // Makes it so CanFocus will update the SuperView's CanFocus property.
    internal bool _addingViewSoCanFocusAlsoUpdatesSuperView;

    // Used to cache CanFocus on subviews when CanFocus is set to false so that it can be restored when CanFocus is changed back to true
    private bool _oldCanFocus;
#endif

    private bool _canFocus;

    /// <summary>Gets or sets a value indicating whether this <see cref="View"/> can be focused.</summary>
    /// <remarks>
    ///     <para>
    ///         <see cref="SuperView"/> must also have <see cref="CanFocus"/> set to <see langword="true"/>.
    ///     </para>
    ///     <para>
    ///         When set to <see langword="false"/>, if an attempt is made to make this view focused, the focus will be set to
    ///         the next focusable view.
    ///     </para>
    ///     <para>
    ///         When set to <see langword="false"/>, the <see cref="TabIndex"/> will be set to -1.
    ///     </para>
    ///     <para>
    ///         When set to <see langword="false"/>, the values of <see cref="CanFocus"/> and <see cref="TabIndex"/> for all
    ///         subviews will be cached so that when <see cref="CanFocus"/> is set back to <see langword="true"/>, the subviews
    ///         will be restored to their previous values.
    ///     </para>
    ///     <para>
    ///         Changing this property to <see langword="true"/> will cause <see cref="TabStop"/> to be set to
    ///         <see cref="TabBehavior.TabStop"/>" as a convenience. Changing this property to
    ///         <see langword="false"/> will have no effect on <see cref="TabStop"/>.
    ///     </para>
    /// </remarks>
    public bool CanFocus
    {
        get => _canFocus;
        set
        {
            if (_canFocus == value)
            {
                return;
            }

            _canFocus = value;

            if (TabStop is null && _canFocus)
            {
                TabStop = TabBehavior.TabStop;
            }

            if (!_canFocus && HasFocus)
            {
                // If CanFocus is set to false and this view has focus, make it leave focus
                HasFocus = false;
            }

            if (_canFocus && !HasFocus && Visible && SuperView is { } && SuperView.Focused is null)
            {
                // If CanFocus is set to true and this view does not have focus, make it enter focus
                SetFocus ();
            }

            OnCanFocusChanged ();
        }
    }

    /// <summary>Raised when <see cref="CanFocus"/> has been changed.</summary>
    /// <remarks>
    ///     Raised by the <see cref="OnCanFocusChanged"/> virtual method.
    /// </remarks>
    public event EventHandler CanFocusChanged;

    /// <summary>Gets the currently focused Subview of this view, or <see langword="null"/> if nothing is focused.</summary>
    [CanBeNull]
    public View Focused
    {
        get { return Subviews.FirstOrDefault (v => v.HasFocus); }
    }

    /// <summary>
    ///     Focuses the deepest focusable view in <see cref="View.TabIndexes"/> if one exists. If there are no views in
    ///     <see cref="View.TabIndexes"/> then the focus is set to the view itself.
    /// </summary>
    /// <param name="direction"></param>
    /// <param name="behavior"></param>
    /// <returns><see langword="true"/> if a subview other than this was focused.</returns>
    public bool FocusDeepest (NavigationDirection direction, TabBehavior? behavior)
    {
        View deepest = FindDeepestFocusableView (direction, behavior);

        if (deepest is { })
        {
            return deepest.SetFocus ();
        }

        return SetFocus ();
    }

    [CanBeNull]
    private View FindDeepestFocusableView (NavigationDirection direction, TabBehavior? behavior)
    {
        var indicies = GetScopedTabIndexes (direction, behavior);

        foreach (View v in indicies)
        {
            if (v.TabIndexes.Count == 0)
            {
                return v;
            }

            return v.FindDeepestFocusableView (direction, behavior);
        }

        return null;
    }

    /// <summary>Returns a value indicating if this View is currently on Top (Active)</summary>
    public bool IsCurrentTop => Application.Current == this;


    /// <summary>Invoked when the <see cref="CanFocus"/> property from a view is changed.</summary>
    /// <remarks>
    ///     Raises the <see cref="CanFocusChanged"/> event.
    /// </remarks>
    public virtual void OnCanFocusChanged () { CanFocusChanged?.Invoke (this, EventArgs.Empty); }

    #region Tab/Focus Handling

#nullable enable

    private List<View>? _tabIndexes;

    // TODO: This should be a get-only property?
    // BUGBUG: This returns an AsReadOnly list, but isn't declared as such.
    /// <summary>Gets a list of the subviews that are a <see cref="TabStop"/>.</summary>
    /// <value>The tabIndexes.</value>
    public IList<View> TabIndexes => _tabIndexes?.AsReadOnly () ?? _empty;

    /// <summary>
    /// Gets TabIndexes that are scoped to the specified behavior and direction. If behavior is null, all TabIndexes are returned.
    /// </summary>
    /// <param name="direction"></param>
    /// <param name="behavior"></param>
    /// <returns></returns>GetScopedTabIndexes
    private View [] GetScopedTabIndexes (NavigationDirection direction, TabBehavior? behavior)
    {
        IEnumerable<View>? indicies;

        if (behavior.HasValue)
        {
            indicies = _tabIndexes?.Where (v => v.TabStop == behavior && v is { CanFocus: true, Visible: true, Enabled: true });
        }
        else
        {
            indicies = _tabIndexes?.Where (v => v is { CanFocus: true, Visible: true, Enabled: true });
        }

        if (direction == NavigationDirection.Backward)
        {
            indicies = indicies?.Reverse ();
        }

        return indicies?.ToArray () ?? Array.Empty<View> ();

    }

    private int? _tabIndex; // null indicates the view has not yet been added to TabIndexes
    private int? _oldTabIndex;

    /// <summary>
    ///     Indicates the order of the current <see cref="View"/> in <see cref="TabIndexes"/> list.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         If <see langword="null"/>, the view is not part of the tab order.
    ///     </para>
    ///     <para>
    ///         On set, if <see cref="SuperView"/> is <see langword="null"/> or has not TabStops, <see cref="TabIndex"/> will
    ///         be set to 0.
    ///     </para>
    ///     <para>
    ///         On set, if <see cref="SuperView"/> has only one TabStop, <see cref="TabIndex"/> will be set to 0.
    ///     </para>
    ///     <para>
    ///         See also <seealso cref="TabStop"/>.
    ///     </para>
    /// </remarks>
    public int? TabIndex
    {
        get => _tabIndex;

        // TOOD: This should be a get-only property. Introduce SetTabIndex (int value) (or similar).
        set
        {
            // Once a view is in the tab order, it should not be removed from the tab order; set TabStop to NoStop instead.
            Debug.Assert (value >= 0);
            Debug.Assert (value is { });

            if (SuperView?._tabIndexes is null || SuperView?._tabIndexes.Count == 1)
            {
                // BUGBUG: Property setters should set the property to the value passed in and not have side effects.
                _tabIndex = 0;

                return;
            }

            if (_tabIndex == value && TabIndexes.IndexOf (this) == value)
            {
                return;
            }

            _tabIndex = value > SuperView!.TabIndexes.Count - 1 ? SuperView._tabIndexes.Count - 1 :
                        value < 0 ? 0 : value;
            _tabIndex = GetGreatestTabIndexInSuperView ((int)_tabIndex);

            if (SuperView._tabIndexes.IndexOf (this) != _tabIndex)
            {
                // BUGBUG: we have to use _tabIndexes and not TabIndexes because TabIndexes returns is a read-only version of _tabIndexes
                SuperView._tabIndexes.Remove (this);
                SuperView._tabIndexes.Insert ((int)_tabIndex, this);
                UpdatePeerTabIndexes ();
            }
            return;

            // Updates the <see cref="TabIndex"/>s of the views in the <see cref="SuperView"/>'s to match their order in <see cref="TabIndexes"/>.
            void UpdatePeerTabIndexes ()
            {
                if (SuperView is null)
                {
                    return;
                }

                var i = 0;

                foreach (View superViewTabStop in SuperView._tabIndexes)
                {
                    if (superViewTabStop._tabIndex is null)
                    {
                        continue;
                    }

                    superViewTabStop._tabIndex = i;
                    i++;
                }
            }
        }
    }


    /// <summary>
    ///     Gets the greatest <see cref="TabIndex"/> of the <see cref="SuperView"/>'s <see cref="TabIndexes"/> that is less
    ///     than or equal to <paramref name="idx"/>.
    /// </summary>
    /// <param name="idx"></param>
    /// <returns>The minimum of <paramref name="idx"/> and the <see cref="SuperView"/>'s <see cref="TabIndexes"/>.</returns>
    private int GetGreatestTabIndexInSuperView (int idx)
    {
        if (SuperView is null)
        {
            return 0;
        }

        var i = 0;

        foreach (View superViewTabStop in SuperView._tabIndexes)
        {
            if (superViewTabStop._tabIndex is null || superViewTabStop == this)
            {
                continue;
            }

            i++;
        }

        return Math.Min (i, idx);
    }



    private TabBehavior? _tabStop;

    /// <summary>
    ///     Gets or sets the behavior of <see cref="AdvanceFocus"/> for keyboard navigation.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         If <see langword="null"/> the tab stop has not been set and setting <see cref="CanFocus"/> to true will set it
    ///         to
    ///         <see cref="TabBehavior.TabStop"/>.
    ///     </para>
    ///     <para>
    ///         TabStop is independent of <see cref="CanFocus"/>. If <see cref="CanFocus"/> is <see langword="false"/>, the
    ///         view will not gain
    ///         focus even if this property is set and vice-versa.
    ///     </para>
    ///     <para>
    ///         The default <see cref="TabBehavior.TabStop"/> keys are <see cref="Application.NextTabKey"/> (<c>Key.Tab</c>) and <see cref="Application.PrevTabKey"/> (<c>Key>Tab.WithShift</c>).
    ///     </para>
    ///     <para>
    ///         The default <see cref="TabBehavior.TabGroup"/> keys are <see cref="Application.NextTabGroupKey"/> (<c>Key.F6</c>) and <see cref="Application.PrevTabGroupKey"/> (<c>Key>Key.F6.WithShift</c>).
    ///     </para>
    /// </remarks>
    public TabBehavior? TabStop
    {
        get => _tabStop;
        set
        {
            if (_tabStop == value)
            {
                return;
            }

            Debug.Assert (value is { });

            if (_tabStop is null && TabIndex is null)
            {
                // This view has not yet been added to TabIndexes (TabStop has not been set previously).
                TabIndex = GetGreatestTabIndexInSuperView (SuperView is { } ? SuperView._tabIndexes.Count : 0);
            }

            _tabStop = value;
        }
    }

    #endregion Tab/Focus Handling
}
