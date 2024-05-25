// Copyright (c) DGP Studio. All rights reserved.
// Licensed under the MIT license.

using CommunityToolkit.WinUI;
using CommunityToolkit.WinUI.Deferred;
using CommunityToolkit.WinUI.Helpers;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System.Collections.ObjectModel;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Foundation.Metadata;
using Windows.System;
using Windows.UI.Core;
using DispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue;
using DispatcherQueuePriority = Microsoft.UI.Dispatching.DispatcherQueuePriority;

namespace Snap.Hutao.Control.TokenizingTextBox;

[DependencyProperty("AutoSuggestBoxStyle", typeof(Style))]
[DependencyProperty("AutoSuggestBoxTextBoxStyle", typeof(Style))]
[DependencyProperty("MaximumTokens", typeof(int), -1, nameof(OnMaximumTokensChanged))]
[DependencyProperty("PlaceholderText", typeof(string))]
[DependencyProperty("QueryIcon", typeof(IconSource))]
[DependencyProperty("SuggestedItemsSource", typeof(object))]
[DependencyProperty("SuggestedItemTemplate", typeof(DataTemplate))]
[DependencyProperty("SuggestedItemTemplateSelector", typeof(DataTemplateSelector))]
[DependencyProperty("SuggestedItemContainerStyle", typeof(Style))]
[DependencyProperty("TabNavigateBackOnArrow", typeof(bool), false)]
[DependencyProperty("Text", typeof(string), default, nameof(TextPropertyChanged))]
[DependencyProperty("TextMemberPath", typeof(string))]
[DependencyProperty("TokenItemTemplate", typeof(DataTemplate))]
[DependencyProperty("TokenItemTemplateSelector", typeof(DataTemplateSelector))]
[DependencyProperty("TokenDelimiter", typeof(string), " ")]
[DependencyProperty("TokenSpacing", typeof(double))]
[TemplatePart(Name = NormalState, Type = typeof(VisualState))]
[TemplatePart(Name = PointerOverState, Type = typeof(VisualState))]
[TemplatePart(Name = FocusedState, Type = typeof(VisualState))]
[TemplatePart(Name = UnfocusedState, Type = typeof(VisualState))]
[TemplatePart(Name = MaxReachedState, Type = typeof(VisualState))]
[TemplatePart(Name = MaxUnreachedState, Type = typeof(VisualState))]
[SuppressMessage("", "SA1124")]
internal partial class TokenizingTextBox : ListViewBase
{
    public const string NormalState = "Normal";
    public const string PointerOverState = "PointerOver";
    public const string FocusedState = "Focused";
    public const string UnfocusedState = "Unfocused";
    public const string MaxReachedState = "MaxReachedState";
    public const string MaxUnreachedState = "MaxUnreachedState";

    private DispatcherQueue dispatcherQueue;
    private InterspersedObservableCollection innerItemsSource;
    private ITokenStringContainer currentTextEdit; // Don't update this directly outside of initialization, use UpdateCurrentTextEdit Method
    private ITokenStringContainer lastTextEdit;

    public TokenizingTextBox()
    {
        // Setup our base state of our collection
        innerItemsSource = new InterspersedObservableCollection(new ObservableCollection<object>()); // TODO: Test this still will let us bind to ItemsSource in XAML?
        currentTextEdit = lastTextEdit = new PretokenStringContainer(true);
        innerItemsSource.Insert(innerItemsSource.Count, currentTextEdit);
        ItemsSource = innerItemsSource;
        //// TODO: Consolidate with callback below for ItemsSourceProperty changed?

        DefaultStyleKey = typeof(TokenizingTextBox);

        // TODO: Do we want to support ItemsSource better? Need to investigate how that works with adding...
        RegisterPropertyChangedCallback(ItemsSourceProperty, ItemsSource_PropertyChanged);
        PreviewKeyDown += TokenizingTextBox_PreviewKeyDown;
        PreviewKeyUp += TokenizingTextBox_PreviewKeyUp;
        CharacterReceived += TokenizingTextBox_CharacterReceived;
        ItemClick += TokenizingTextBox_ItemClick;

        dispatcherQueue = DispatcherQueue;
    }

    public event TypedEventHandler<Microsoft.UI.Xaml.Controls.AutoSuggestBox, AutoSuggestBoxTextChangedEventArgs>? TextChanged;

    public event TypedEventHandler<Microsoft.UI.Xaml.Controls.AutoSuggestBox, AutoSuggestBoxSuggestionChosenEventArgs>? SuggestionChosen;

    public event TypedEventHandler<Microsoft.UI.Xaml.Controls.AutoSuggestBox, AutoSuggestBoxQuerySubmittedEventArgs>? QuerySubmitted;

    public event TypedEventHandler<TokenizingTextBox, TokenItemAddingEventArgs>? TokenItemAdding;

    public event TypedEventHandler<TokenizingTextBox, object>? TokenItemAdded;

    public event TypedEventHandler<TokenizingTextBox, TokenItemRemovingEventArgs>? TokenItemRemoving;

    public event TypedEventHandler<TokenizingTextBox, object>? TokenItemRemoved;

    private enum MoveDirection
    {
        Next,
        Previous,
    }

    public static bool IsShiftPressed => InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift).HasFlag(CoreVirtualKeyStates.Down);

    public static bool IsXamlRootAvailable { get; } = ApiInformation.IsPropertyPresent("Windows.UI.Xaml.UIElement", "XamlRoot");

    public static bool IsControlPressed => InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(CoreVirtualKeyStates.Down);

    public bool PauseTokenClearOnFocus { get; set; }

    public bool IsClearingForClick { get; set; }

    public string SelectedTokenText
    {
        get => PrepareSelectionForClipboard();
    }

    public void RaiseQuerySubmitted(Microsoft.UI.Xaml.Controls.AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        QuerySubmitted?.Invoke(sender, args);
    }

    public void RaiseSuggestionChosen(Microsoft.UI.Xaml.Controls.AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        SuggestionChosen?.Invoke(sender, args);
    }

    public void RaiseTextChanged(Microsoft.UI.Xaml.Controls.AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        TextChanged?.Invoke(sender, args);
    }

    public void AddTokenItem(object data, bool atEnd = false)
    {
        _ = AddTokenAsync(data, atEnd);
    }

    public async ValueTask ClearAsync()
    {
        while (innerItemsSource.Count > 1)
        {
            if (ContainerFromItem(innerItemsSource[0]) is TokenizingTextBoxItem container)
            {
                if (!await RemoveTokenAsync(container, innerItemsSource[0]).ConfigureAwait(true))
                {
                    // if a removal operation fails then stop the clear process
                    break;
                }
            }
        }

        // Clear the active pretoken string.
        // Setting the text property directly avoids a delay when setting the text in the autosuggest box.
        Text = string.Empty;
    }

    public async Task AddTokenAsync(object data, bool? atEnd = default)
    {
        if (MaximumTokens >= 0 && MaximumTokens <= innerItemsSource.ItemsSource.Count)
        {
            // No tokens for you
            return;
        }

        if (data is string str && TokenItemAdding is not null)
        {
            TokenItemAddingEventArgs tiaea = new(str);
            await TokenItemAdding.InvokeAsync(this, tiaea).ConfigureAwait(true);

            if (tiaea.Cancel)
            {
                return;
            }

            if (tiaea.Item is not null)
            {
                data = tiaea.Item; // Transformed by event implementor
            }
        }

        // If we've been typing in the last box, just add this to the end of our collection
        if (atEnd == true || currentTextEdit == lastTextEdit)
        {
            innerItemsSource.InsertAt(innerItemsSource.Count - 1, data);
        }
        else
        {
            // Otherwise, we'll insert before our current box
            ITokenStringContainer edit = currentTextEdit;
            int index = innerItemsSource.IndexOf(edit);

            // Insert our new data item at the location of our textbox
            innerItemsSource.InsertAt(index, data);

            // Remove our textbox
            innerItemsSource.Remove(edit);
        }

        // Focus back to our end box as Outlook does.
        TokenizingTextBoxItem last = (TokenizingTextBoxItem)ContainerFromItem(lastTextEdit);
        last?.AutoSuggestTextBox.Focus(FocusState.Keyboard);

        TokenItemAdded?.Invoke(this, data);

        GuardAgainstPlaceholderTextLayoutIssue();
    }

    public async ValueTask RemoveAllSelectedTokens()
    {
        while (SelectedItems.Count > 0)
        {
            if (ContainerFromItem(SelectedItems[0]) is TokenizingTextBoxItem container)
            {
                if (IndexFromContainer(container) != Items.Count - 1)
                {
                    // if its a text box, remove any selected text, and if its then empty remove the container, unless its focused
                    if (SelectedItems[0] is ITokenStringContainer)
                    {
                        TextBox asb = container.AutoSuggestTextBox;

                        // grab any selected text
                        string tempStr = asb.SelectionStart == 0
                            ? string.Empty
                            : asb.Text[..asb.SelectionStart];
                        tempStr +=
                            asb.SelectionStart +
                            asb.SelectionLength < asb.Text.Length
                                ? asb.Text[(asb.SelectionStart + asb.SelectionLength)..]
                                : string.Empty;

                        if (tempStr.Length is 0)
                        {
                            // Need to be careful not to remove the last item in the list
                            await RemoveTokenAsync(container).ConfigureAwait(true);
                        }
                        else
                        {
                            asb.Text = tempStr;
                        }
                    }
                    else
                    {
                        // if the item is a token just remove it.
                        await RemoveTokenAsync(container).ConfigureAwait(true);
                    }
                }
                else
                {
                    if (SelectedItems.Count == 1)
                    {
                        // at this point we have one selection and its the default textbox.
                        // stop the iteration here
                        break;
                    }
                }
            }
        }
    }

    public bool SelectPreviousItem(TokenizingTextBoxItem item)
    {
        return SelectNewItem(item, -1, i => i > 0);
    }

    public bool SelectNextItem(TokenizingTextBoxItem item)
    {
        return SelectNewItem(item, 1, i => i < Items.Count - 1);
    }

    public void SelectAllTokensAndText()
    {
        void SelectAllTokensAndTextCore()
        {
            this.SelectAllSafe();

            // need to synchronize the select all and the focus behavior on the text box
            // because there is no way to identify that the focus has been set from this point
            // to avoid instantly clearing the selection of tokens
            PauseTokenClearOnFocus = true;

            foreach (object? item in Items)
            {
                if (item is ITokenStringContainer)
                {
                    // grab any selected text
                    if (ContainerFromItem(item) is TokenizingTextBoxItem pretoken)
                    {
                        pretoken.AutoSuggestTextBox.SelectionStart = 0;
                        pretoken.AutoSuggestTextBox.SelectionLength = pretoken.AutoSuggestTextBox.Text.Length;
                    }
                }
            }

            if (ContainerFromIndex(Items.Count - 1) is TokenizingTextBoxItem container)
            {
                container.Focus(FocusState.Programmatic);
            }
        }

        _ = dispatcherQueue.EnqueueAsync(SelectAllTokensAndTextCore, DispatcherQueuePriority.Normal);
    }

    public void DeselectAllTokensAndText(TokenizingTextBoxItem? ignoreItem = default)
    {
        this.DeselectAll();
        ClearAllTextSelections(ignoreItem);
    }

    protected void UpdateCurrentTextEdit(ITokenStringContainer edit)
    {
        currentTextEdit = edit;

        Text = edit.Text; // Update our text property.
    }

    protected override AutomationPeer OnCreateAutomationPeer()
    {
        return new TokenizingTextBoxAutomationPeer(this);
    }

    /// <inheritdoc/>
    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        MenuFlyoutItem selectAllMenuItem = new()
        {
            Text = "Select all",
        };
        selectAllMenuItem.Click += (s, e) => SelectAllTokensAndText();
        MenuFlyout menuFlyout = new();
        menuFlyout.Items.Add(selectAllMenuItem);

        if (IsXamlRootAvailable && XamlRoot is not null)
        {
            menuFlyout.XamlRoot = XamlRoot;
        }

        ContextFlyout = menuFlyout;
    }

    /// <inheritdoc/>
    protected override DependencyObject GetContainerForItemOverride()
    {
        return new TokenizingTextBoxItem();
    }

    /// <inheritdoc/>
    protected override bool IsItemItsOwnContainerOverride(object item)
    {
        return item is TokenizingTextBoxItem;
    }

    /// <inheritdoc/>
    protected override void PrepareContainerForItemOverride(DependencyObject element, object item)
    {
        base.PrepareContainerForItemOverride(element, item);

        if (element is TokenizingTextBoxItem tokenitem)
        {
            tokenitem.Owner = this;

            tokenitem.ContentTemplateSelector = TokenItemTemplateSelector;
            tokenitem.ContentTemplate = TokenItemTemplate;

            tokenitem.ClearClicked -= TokenizingTextBoxItem_ClearClicked;
            tokenitem.ClearClicked += TokenizingTextBoxItem_ClearClicked;

            tokenitem.ClearAllAction -= TokenizingTextBoxItem_ClearAllAction;
            tokenitem.ClearAllAction += TokenizingTextBoxItem_ClearAllAction;

            tokenitem.GotFocus -= TokenizingTextBoxItem_GotFocus;
            tokenitem.GotFocus += TokenizingTextBoxItem_GotFocus;

            tokenitem.LostFocus -= TokenizingTextBoxItem_LostFocus;
            tokenitem.LostFocus += TokenizingTextBoxItem_LostFocus;

            MenuFlyout menuFlyout = new();

            MenuFlyoutItem removeMenuItem = new()
            {
                Text = "Remove",
            };
            removeMenuItem.Click += (s, e) => TokenizingTextBoxItem_ClearClicked(tokenitem, default);

            menuFlyout.Items.Add(removeMenuItem);

            if (IsXamlRootAvailable && XamlRoot is not null)
            {
                menuFlyout.XamlRoot = XamlRoot;
            }

            MenuFlyoutItem selectAllMenuItem = new()
            {
                Text = "Select all",
            };
            selectAllMenuItem.Click += (s, e) => SelectAllTokensAndText();

            menuFlyout.Items.Add(selectAllMenuItem);

            tokenitem.ContextFlyout = menuFlyout;
        }
    }

    private static void TextPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TokenizingTextBox { currentTextEdit: { } } ttb)
        {
            if (e.NewValue is string newValue)
            {
                ttb.currentTextEdit.Text = newValue;

                // Notify inner container of text change, see issue #4749
                TokenizingTextBoxItem item = (TokenizingTextBoxItem)ttb.ContainerFromItem(ttb.currentTextEdit);
                item?.UpdateText(ttb.currentTextEdit.Text);
            }
        }
    }

    private static void OnMaximumTokensChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TokenizingTextBox { MaximumTokens: >= 0 } ttb && e.NewValue is int newMaxTokens)
        {
            int tokenCount = ttb.innerItemsSource.ItemsSource.Count;
            if (tokenCount > 0 && tokenCount > newMaxTokens)
            {
                int tokensToRemove = tokenCount - Math.Max(newMaxTokens, 0);

                // Start at the end, remove any extra tokens.
                for (int i = tokenCount; i > tokenCount - tokensToRemove; --i)
                {
                    object? token = ttb.innerItemsSource.ItemsSource[i - 1];

                    if (token is not null)
                    {
                        // Force remove the items. No warning and no option to cancel.
                        ttb.innerItemsSource.Remove(token);
                        ttb.TokenItemRemoved?.Invoke(ttb, token);
                    }
                }
            }
        }
    }

    private void ItemsSource_PropertyChanged(DependencyObject sender, DependencyProperty dp)
    {
        // If we're given a different ItemsSource, we need to wrap that collection in our helper class.
        if (ItemsSource is { } and not InterspersedObservableCollection)
        {
            innerItemsSource = new(ItemsSource);

            if (MaximumTokens >= 0 && innerItemsSource.ItemsSource.Count >= MaximumTokens)
            {
                // Reduce down to below the max as necessary.
                int endCount = MaximumTokens > 0 ? MaximumTokens : 0;
                for (int i = innerItemsSource.ItemsSource.Count - 1; i >= endCount; --i)
                {
                    innerItemsSource.Remove(innerItemsSource[i]);
                }
            }

            // Add our text box at the end of items and set its default value to our initial text, fix for #4749
            currentTextEdit = lastTextEdit = new PretokenStringContainer(true) { Text = Text };
            innerItemsSource.Insert(innerItemsSource.Count, currentTextEdit);
            ItemsSource = innerItemsSource;
        }
    }

    private void TokenizingTextBox_ItemClick(object sender, ItemClickEventArgs e)
    {
        // If the user taps an item in the list, make sure to clear any text selection as required
        // Note, token selection is cleared by the listview default behavior
        if (!IsControlPressed)
        {
            // Set class state flag to prevent click item being immediately deselected
            IsClearingForClick = true;
            ClearAllTextSelections(default);
        }
    }

    private void TokenizingTextBox_PreviewKeyUp(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Escape:
                // Clear any selection and place the focus back into the text box
                DeselectAllTokensAndText();
                FocusPrimaryAutoSuggestBox();
                break;
        }
    }

    private void FocusPrimaryAutoSuggestBox()
    {
        if (Items?.Count > 0)
        {
            if (ContainerFromIndex(Items.Count - 1) is TokenizingTextBoxItem container)
            {
                container.Focus(FocusState.Programmatic);
            }
        }
    }

    private async void TokenizingTextBox_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.C:
                if (IsControlPressed)
                {
                    CopySelectedToClipboard();
                    e.Handled = true;
                    return;
                }

                break;

            case VirtualKey.X:
                if (IsControlPressed)
                {
                    CopySelectedToClipboard();

                    // now clear all selected tokens and text, or all if none are selected
                    await RemoveAllSelectedTokens().ConfigureAwait(false);
                }

                break;

            // For moving between tokens
            case VirtualKey.Left:
                e.Handled = MoveFocusAndSelection(MoveDirection.Previous);
                return;

            case VirtualKey.Right:
                e.Handled = MoveFocusAndSelection(MoveDirection.Next);
                return;

            case VirtualKey.A:
                // modify the select-all behavior to ensure the text in the edit box gets selected.
                if (IsControlPressed)
                {
                    SelectAllTokensAndText();
                    e.Handled = true;
                    return;
                }

                break;
        }
    }

    private async void TokenizingTextBox_CharacterReceived(UIElement sender, CharacterReceivedRoutedEventArgs args)
    {
        TokenizingTextBoxItem container = (TokenizingTextBoxItem)ContainerFromItem(currentTextEdit);

        if (container is not null && !(GetFocusedElement().Equals(container.AutoSuggestTextBox) || char.IsControl(args.Character)))
        {
            if (SelectedItems.Count > 0)
            {
                int index = innerItemsSource.IndexOf(SelectedItems.First());

                await RemoveAllSelectedTokens().ConfigureAwait(false);

                void RemoveOldItems()
                {
                    // If we're before the last textbox and it's empty, redirect focus to that one instead
                    if (index == innerItemsSource.Count - 1 && string.IsNullOrWhiteSpace(lastTextEdit.Text))
                    {
                        if (ContainerFromItem(lastTextEdit) is TokenizingTextBoxItem lastContainer)
                        {
                            lastContainer.UseCharacterAsUser = true; // Make sure we trigger a refresh of suggested items.

                            lastTextEdit.Text = string.Empty + args.Character;

                            UpdateCurrentTextEdit(lastTextEdit);

                            lastContainer.AutoSuggestTextBox.SelectionStart = 1; // Set position to after our new character inserted

                            lastContainer.AutoSuggestTextBox.Focus(FocusState.Keyboard);
                        }
                    }
                    else
                    {
                        //// Otherwise, create a new textbox for this text.

                        UpdateCurrentTextEdit(new PretokenStringContainer((string.Empty + args.Character).Trim())); // Trim so that 'space' isn't inserted and can be used to insert a new box.

                        innerItemsSource.Insert(index, currentTextEdit);

                        void Containerization()
                        {
                            if (ContainerFromIndex(index) is TokenizingTextBoxItem newContainer) // Should be our last text box
                            {
                                newContainer.UseCharacterAsUser = true; // Make sure we trigger a refresh of suggested items.

                                void WaitForLoad(object s, RoutedEventArgs eargs)
                                {
                                    if (newContainer.AutoSuggestTextBox is not null)
                                    {
                                        newContainer.AutoSuggestTextBox.SelectionStart = 1; // Set position to after our new character inserted

                                        newContainer.AutoSuggestTextBox.Focus(FocusState.Keyboard);
                                    }

                                    newContainer.Loaded -= WaitForLoad;
                                }

                                newContainer.AutoSuggestTextBoxLoaded += WaitForLoad;
                            }
                        }

                        // Need to wait for containerization
                        _ = DispatcherQueue.EnqueueAsync(Containerization, DispatcherQueuePriority.Normal);
                    }
                }

                // Wait for removal of old items
                _ = DispatcherQueue.EnqueueAsync(RemoveOldItems, DispatcherQueuePriority.Normal);
            }
            else
            {
                // If no items are selected, send input to the last active string container.
                // This code is only fires during an edgecase where an item is in the process of being deleted and the user inputs a character before the focus has been redirected to a string container.
                if (innerItemsSource[^1] is ITokenStringContainer textToken)
                {
                    if (ContainerFromIndex(Items.Count - 1) is TokenizingTextBoxItem last) // Should be our last text box
                    {
                        string text = last.AutoSuggestTextBox.Text;
                        int selectionStart = last.AutoSuggestTextBox.SelectionStart;
                        int position = selectionStart > text.Length ? text.Length : selectionStart;
                        textToken.Text = text[..position] + args.Character + text[position..];

                        last.AutoSuggestTextBox.SelectionStart = position + 1; // Set position to after our new character inserted

                        last.AutoSuggestTextBox.Focus(FocusState.Keyboard);
                    }
                }
            }
        }
    }

    private object GetFocusedElement()
    {
        if (IsXamlRootAvailable && XamlRoot is not null)
        {
            return FocusManager.GetFocusedElement(XamlRoot);
        }
        else
        {
            return FocusManager.GetFocusedElement();
        }
    }

    private void TokenizingTextBoxItem_GotFocus(object sender, RoutedEventArgs e)
    {
        // Keep track of our currently focused textbox
        if (sender is TokenizingTextBoxItem { Content: ITokenStringContainer text })
        {
            UpdateCurrentTextEdit(text);
        }
    }

    private void TokenizingTextBoxItem_LostFocus(object sender, RoutedEventArgs e)
    {
        // Keep track of our currently focused textbox
        if (sender is TokenizingTextBoxItem { Content: ITokenStringContainer text } &&
            string.IsNullOrWhiteSpace(text.Text) && text != lastTextEdit)
        {
            // We're leaving an inner textbox that's blank, so we'll remove it
            innerItemsSource.Remove(text);

            UpdateCurrentTextEdit(lastTextEdit);

            GuardAgainstPlaceholderTextLayoutIssue();
        }
    }

    private async ValueTask<bool> RemoveTokenAsync(TokenizingTextBoxItem item, object? data = null)
    {
        data ??= ItemFromContainer(item);

        if (TokenItemRemoving is not null)
        {
            TokenItemRemovingEventArgs tirea = new(data, item);
            await TokenItemRemoving.InvokeAsync(this, tirea).ConfigureAwait(true);

            if (tirea.Cancel)
            {
                return false;
            }
        }

        innerItemsSource.Remove(data);

        TokenItemRemoved?.Invoke(this, data);

        GuardAgainstPlaceholderTextLayoutIssue();

        return true;
    }

    private void GuardAgainstPlaceholderTextLayoutIssue()
    {
        // If the *PlaceholderText is visible* on the last AutoSuggestBox, it can incorrectly layout itself
        // when the *ASB has focus*. We think this is an optimization in the platform, but haven't been able to
        // isolate a straight-reproduction of this issue outside of this control (though we have eliminated
        // most Toolkit influences like ASB/TextBox Style, the InterspersedObservableCollection, etc...).
        // The only Toolkit component involved here should be WrapPanel (which is a straight-forward Panel).
        // We also know the ASB itself is adjusting it's size correctly, it's the inner component.
        //
        // To combat this issue:
        //   We toggle the visibility of the Placeholder ContentControl in order to force it's layout to update properly
        FrameworkElement? placeholder = ContainerFromItem(lastTextEdit)?.FindDescendant("PlaceholderTextContentPresenter");

        if (placeholder?.Visibility == Visibility.Visible)
        {
            placeholder.Visibility = Visibility.Collapsed;

            // After we ensure we've hid the control, make it visible again (this is imperceptible to the user).
            _ = CompositionTargetHelper.ExecuteAfterCompositionRenderingAsync(() =>
            {
                placeholder.Visibility = Visibility.Visible;
            });
        }
    }

    private bool MoveFocusAndSelection(MoveDirection direction)
    {
        bool retVal = false;

        if (GetCurrentContainerItem() is { } currentContainerItem)
        {
            object? currentItem = ItemFromContainer(currentContainerItem);
            int previousIndex = Items.IndexOf(currentItem);
            int index = previousIndex;

            if (direction == MoveDirection.Previous)
            {
                if (previousIndex > 0)
                {
                    index -= 1;
                }
                else
                {
                    if (TabNavigateBackOnArrow)
                    {
                        FocusManager.TryMoveFocus(FocusNavigationDirection.Previous, new FindNextElementOptions
                        {
                            SearchRoot = XamlRoot.Content,
                        });
                    }

                    retVal = true;
                }
            }
            else if (direction == MoveDirection.Next)
            {
                if (previousIndex < Items.Count - 1)
                {
                    index += 1;
                }
            }

            // Only do stuff if the index is actually changing
            if (index != previousIndex)
            {
                if (ContainerFromIndex(index) is TokenizingTextBoxItem newItem)
                {
                    // Check for the new item being a text control.
                    // this must happen before focus is set to avoid seeing the caret
                    // jump in come cases
                    if (Items[index] is ITokenStringContainer && !IsShiftPressed)
                    {
                        newItem.AutoSuggestTextBox.SelectionLength = 0;
                        newItem.AutoSuggestTextBox.SelectionStart = direction == MoveDirection.Next
                            ? 0
                            : newItem.AutoSuggestTextBox.Text.Length;
                    }

                    newItem.Focus(FocusState.Keyboard);

                    // if no control keys are selected then the selection also becomes just this item
                    if (IsShiftPressed)
                    {
                        // What we do here depends on where the selection started
                        // if the previous item is between the start and new position then we add the new item to the selected range
                        // if the new item is between the start and the previous position then we remove the previous position
                        int newDistance = Math.Abs(SelectedIndex - index);
                        int oldDistance = Math.Abs(SelectedIndex - previousIndex);

                        if (newDistance > oldDistance)
                        {
                            SelectedItems.Add(Items[index]);
                        }
                        else
                        {
                            SelectedItems.Remove(Items[previousIndex]);
                        }
                    }
                    else if (!IsControlPressed)
                    {
                        SelectedIndex = index;

                        // This looks like a bug in the underlying ListViewBase control.
                        // Might need to be reviewed if the base behavior is fixed
                        // When two consecutive items are selected and the navigation moves between them,
                        // the first time that happens the old focused item is not unselected
                        if (SelectedItems.Count > 1)
                        {
                            SelectedItems.Clear();
                            SelectedIndex = index;
                        }
                    }

                    retVal = true;
                }
            }
        }

        return retVal;
    }

    private TokenizingTextBoxItem? GetCurrentContainerItem()
    {
        if (IsXamlRootAvailable && XamlRoot is not null)
        {
            return (TokenizingTextBoxItem)FocusManager.GetFocusedElement(XamlRoot);
        }
        else
        {
            return (TokenizingTextBoxItem)FocusManager.GetFocusedElement();
        }
    }

    private void ClearAllTextSelections(TokenizingTextBoxItem? ignoreItem)
    {
        // Clear any selection in the text box
        foreach (object? item in Items)
        {
            if (item is ITokenStringContainer)
            {
                if (ContainerFromItem(item) is TokenizingTextBoxItem container)
                {
                    if (container != ignoreItem)
                    {
                        container.AutoSuggestTextBox.SelectionLength = 0;
                    }
                }
            }
        }
    }

    private bool SelectNewItem(TokenizingTextBoxItem item, int increment, Func<int, bool> testFunc)
    {
        // find the item in the list
        int currentIndex = IndexFromContainer(item);

        // Select previous token item (if there is one).
        if (testFunc(currentIndex))
        {
            if (ContainerFromItem(Items[currentIndex + increment]) is ListViewItem newItem)
            {
                newItem.Focus(FocusState.Keyboard);
                SelectedItems.Add(Items[currentIndex + increment]);
                return true;
            }
        }

        return false;
    }

    private async void TokenizingTextBoxItem_ClearAllAction(TokenizingTextBoxItem sender, RoutedEventArgs args)
    {
        // find the first item selected
        int newSelectedIndex = -1;

        if (SelectedRanges.Count > 0)
        {
            newSelectedIndex = SelectedRanges[0].FirstIndex - 1;
        }

        await RemoveAllSelectedTokens().ConfigureAwait(true);

        SelectedIndex = newSelectedIndex;

        if (newSelectedIndex is -1)
        {
            newSelectedIndex = Items.Count - 1;
        }

        // focus the item prior to the first selected item
        if (ContainerFromIndex(newSelectedIndex) is TokenizingTextBoxItem container)
        {
            container.Focus(FocusState.Keyboard);
        }
    }

    private async void TokenizingTextBoxItem_ClearClicked(TokenizingTextBoxItem sender, RoutedEventArgs? args)
    {
        await RemoveTokenAsync(sender).ConfigureAwait(true);
    }

    private void CopySelectedToClipboard()
    {
        DataPackage dataPackage = new()
        {
            RequestedOperation = DataPackageOperation.Copy,
        };

        string tokenString = PrepareSelectionForClipboard();

        if (!string.IsNullOrEmpty(tokenString))
        {
            dataPackage.SetText(tokenString);
            Clipboard.SetContent(dataPackage);
        }
    }

    private string PrepareSelectionForClipboard()
    {
        string tokenString = string.Empty;
        bool addSeparator = false;

        // Copy all items if none selected (and no text selected)
        foreach (object? item in SelectedItems.Count > 0 ? SelectedItems : Items)
        {
            if (addSeparator)
            {
                tokenString += TokenDelimiter;
            }
            else
            {
                addSeparator = true;
            }

            if (item is ITokenStringContainer)
            {
                // grab any selected text
                if (ContainerFromItem(item) is TokenizingTextBoxItem { AutoSuggestTextBox: { } textBox })
                {
                    tokenString += textBox.Text.Substring(
                        textBox.SelectionStart,
                        textBox.SelectionLength);
                }
            }
            else
            {
                tokenString += item.ToString();
            }
        }

        return tokenString;
    }
}