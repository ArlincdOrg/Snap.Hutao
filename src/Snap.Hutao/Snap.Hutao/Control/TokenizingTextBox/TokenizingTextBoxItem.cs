// Copyright (c) DGP Studio. All rights reserved.
// Licensed under the MIT license.

using CommunityToolkit.WinUI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;
using Windows.System;

namespace Snap.Hutao.Control.TokenizingTextBox;

[DependencyProperty("ClearButtonStyle", typeof(Style))]
[DependencyProperty("Owner", typeof(TokenizingTextBox))]
[TemplatePart(Name = PART_ClearButton, Type = typeof(ButtonBase))] //// Token case
[TemplatePart(Name = PART_AutoSuggestBox, Type = typeof(Microsoft.UI.Xaml.Controls.AutoSuggestBox))] //// String case
[TemplatePart(Name = PART_TokensCounter, Type = typeof(TextBlock))]
[SuppressMessage("", "SA1124")]
internal partial class TokenizingTextBoxItem : ListViewItem
{
    private const string PART_ClearButton = "PART_RemoveButton";
    private const string PART_AutoSuggestBox = "PART_AutoSuggestBox";
    private const string PART_TokensCounter = "PART_TokensCounter";
    private const string QueryButton = "QueryButton";

    private Microsoft.UI.Xaml.Controls.AutoSuggestBox autoSuggestBox;
    private TextBox autoSuggestTextBox;
    private Button clearButton;
    private bool isSelectedFocusOnFirstCharacter;
    private bool isSelectedFocusOnLastCharacter;

    public TokenizingTextBoxItem()
    {
        DefaultStyleKey = typeof(TokenizingTextBoxItem);

        // TODO: only add these if token?
        RightTapped += TokenizingTextBoxItem_RightTapped;
        KeyDown += TokenizingTextBoxItem_KeyDown;
    }

    public event TypedEventHandler<TokenizingTextBoxItem, RoutedEventArgs>? AutoSuggestTextBoxLoaded;

    public event TypedEventHandler<TokenizingTextBoxItem, RoutedEventArgs>? ClearClicked;

    public event TypedEventHandler<TokenizingTextBoxItem, RoutedEventArgs>? ClearAllAction;

    public TextBox AutoSuggestTextBox { get => autoSuggestTextBox; }

    public bool UseCharacterAsUser { get; set; }

    private bool IsCaretAtStart
    {
        get => autoSuggestTextBox?.SelectionStart is 0;
    }

    private bool IsCaretAtEnd
    {
        get => autoSuggestTextBox?.SelectionStart == autoSuggestTextBox?.Text.Length || autoSuggestTextBox?.SelectionStart + autoSuggestTextBox?.SelectionLength == autoSuggestTextBox?.Text.Length;
    }

    private bool IsAllSelected
    {
        get => autoSuggestTextBox?.SelectedText == autoSuggestTextBox?.Text && !string.IsNullOrEmpty(autoSuggestTextBox?.Text);
    }

    // Called to update text by link:TokenizingTextBox.Properties.cs:TextPropertyChanged
    public void UpdateText(string text)
    {
        if (autoSuggestBox is not null)
        {
            autoSuggestBox.Text = text;
        }
        else
        {
            void WaitForLoad(object s, RoutedEventArgs eargs)
            {
                if (autoSuggestTextBox is not null)
                {
                    autoSuggestTextBox.Text = text;
                }

                AutoSuggestTextBoxLoaded -= WaitForLoad;
            }

            AutoSuggestTextBoxLoaded += WaitForLoad;
        }
    }

    /// <inheritdoc/>
    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        if (GetTemplateChild(PART_AutoSuggestBox) is Microsoft.UI.Xaml.Controls.AutoSuggestBox suggestbox)
        {
            OnApplyTemplateAutoSuggestBox(suggestbox);
        }

        if (clearButton is not null)
        {
            clearButton.Click -= ClearButton_Click;
        }

        clearButton = (Button)GetTemplateChild(PART_ClearButton);

        if (clearButton is not null)
        {
            clearButton.Click += ClearButton_Click;
        }
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        ClearClicked?.Invoke(this, e);
    }

    private void TokenizingTextBoxItem_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        ContextFlyout.ShowAt(this);
    }

    private void TokenizingTextBoxItem_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (Content is not ITokenStringContainer)
        {
            // We only want to 'remove' our token if we're not a textbox.
            switch (e.Key)
            {
                case VirtualKey.Back:
                case VirtualKey.Delete:
                    {
                        ClearAllAction?.Invoke(this, e);
                        break;
                    }
            }
        }
    }

    /// Called from <see cref="OnApplyTemplate"/>
    private void OnApplyTemplateAutoSuggestBox(Microsoft.UI.Xaml.Controls.AutoSuggestBox auto)
    {
        if (autoSuggestBox is not null)
        {
            autoSuggestBox.Loaded -= OnASBLoaded;

            autoSuggestBox.QuerySubmitted -= AutoSuggestBox_QuerySubmitted;
            autoSuggestBox.SuggestionChosen -= AutoSuggestBox_SuggestionChosen;
            autoSuggestBox.TextChanged -= AutoSuggestBox_TextChanged;
            autoSuggestBox.PointerEntered -= AutoSuggestBox_PointerEntered;
            autoSuggestBox.PointerExited -= AutoSuggestBox_PointerExited;
            autoSuggestBox.PointerCanceled -= AutoSuggestBox_PointerExited;
            autoSuggestBox.PointerCaptureLost -= AutoSuggestBox_PointerExited;
            autoSuggestBox.GotFocus -= AutoSuggestBox_GotFocus;
            autoSuggestBox.LostFocus -= AutoSuggestBox_LostFocus;

            // Remove any previous QueryIcon
            autoSuggestBox.QueryIcon = default;
        }

        autoSuggestBox = auto;

        if (autoSuggestBox is not null)
        {
            autoSuggestBox.Loaded += OnASBLoaded;

            autoSuggestBox.QuerySubmitted += AutoSuggestBox_QuerySubmitted;
            autoSuggestBox.SuggestionChosen += AutoSuggestBox_SuggestionChosen;
            autoSuggestBox.TextChanged += AutoSuggestBox_TextChanged;
            autoSuggestBox.PointerEntered += AutoSuggestBox_PointerEntered;
            autoSuggestBox.PointerExited += AutoSuggestBox_PointerExited;
            autoSuggestBox.PointerCanceled += AutoSuggestBox_PointerExited;
            autoSuggestBox.PointerCaptureLost += AutoSuggestBox_PointerExited;
            autoSuggestBox.GotFocus += AutoSuggestBox_GotFocus;
            autoSuggestBox.LostFocus += AutoSuggestBox_LostFocus;

            // Setup a binding to the QueryIcon of the Parent if we're the last box.
            if (Content is ITokenStringContainer str)
            {
                // We need to set our initial text in all cases.
                autoSuggestBox.Text = str.Text;

                // We only set/bind some properties on the last textbox to mimic the autosuggestbox look
                if (str.IsLast)
                {
                    // Workaround for https://github.com/microsoft/microsoft-ui-xaml/issues/2568
                    if (Owner.QueryIcon is FontIconSource fis &&
                        fis.ReadLocalValue(FontIconSource.FontSizeProperty) == DependencyProperty.UnsetValue)
                    {
                        // This can be expensive, could we optimize?
                        // Also, this is changing the FontSize on the IconSource (which could be shared?)
                        fis.FontSize = Owner.TryFindResource("TokenizingTextBoxIconFontSize") as double? ?? 16;
                    }

                    Binding iconBinding = new()
                    {
                        Source = Owner,
                        Path = new PropertyPath(nameof(Owner.QueryIcon)),
                        RelativeSource = new()
                        {
                            Mode = RelativeSourceMode.TemplatedParent,
                        },
                    };

                    IconSourceElement iconSourceElement = new();
                    iconSourceElement.SetBinding(IconSourceElement.IconSourceProperty, iconBinding);
                    autoSuggestBox.QueryIcon = iconSourceElement;
                }
            }
        }
    }

    private async void AutoSuggestBox_QuerySubmitted(Microsoft.UI.Xaml.Controls.AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        Owner.RaiseQuerySubmitted(sender, args);

        object? chosenItem = default;
        if (args.ChosenSuggestion is not null)
        {
            chosenItem = args.ChosenSuggestion;
        }
        else if (!string.IsNullOrWhiteSpace(args.QueryText))
        {
            chosenItem = args.QueryText;
        }

        if (chosenItem is not null)
        {
            await Owner.AddTokenAsync(chosenItem).ConfigureAwait(true); // TODO: Need to pass index?
            sender.Text = string.Empty;
            Owner.Text = string.Empty;
            sender.Focus(FocusState.Programmatic);
        }
    }

    private void AutoSuggestBox_SuggestionChosen(Microsoft.UI.Xaml.Controls.AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        Owner.RaiseSuggestionChosen(sender, args);
    }

    private void AutoSuggestBox_TextChanged(Microsoft.UI.Xaml.Controls.AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (sender.Text is null)
        {
            return;
        }

        if (!EqualityComparer<string>.Default.Equals(sender.Text, Owner.Text))
        {
            Owner.Text = sender.Text; // Update parent text property, if different
        }

        // Override our programmatic manipulation as we're redirecting input for the user
        if (UseCharacterAsUser)
        {
            UseCharacterAsUser = false;

            args.Reason = AutoSuggestionBoxTextChangeReason.UserInput;
        }

        Owner.RaiseTextChanged(sender, args);

        string t = sender.Text?.Trim() ?? string.Empty;

        // Look for Token Delimiters to create new tokens when text changes.
        if (!string.IsNullOrEmpty(Owner.TokenDelimiter) && t.Contains(Owner.TokenDelimiter, StringComparison.OrdinalIgnoreCase))
        {
            bool lastDelimited = t[^1] == Owner.TokenDelimiter[0];

            string[] tokens = t.Split(Owner.TokenDelimiter);
            int numberToProcess = lastDelimited ? tokens.Length : tokens.Length - 1;
            for (int position = 0; position < numberToProcess; position++)
            {
                string token = tokens[position];
                token = token.Trim();
                if (token.Length > 0)
                {
                    _ = Owner.AddTokenAsync(token); //// TODO: Pass Index?
                }
            }

            if (lastDelimited)
            {
                sender.Text = string.Empty;
            }
            else
            {
                sender.Text = tokens[^1].Trim();
            }
        }
    }

    private void AutoSuggestBox_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        VisualStateManager.GoToState(Owner, TokenizingTextBox.PointerOverState, true);
    }

    private void AutoSuggestBox_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        VisualStateManager.GoToState(Owner, TokenizingTextBox.NormalState, true);
    }

    private void AutoSuggestBox_LostFocus(object sender, RoutedEventArgs e)
    {
        VisualStateManager.GoToState(Owner, TokenizingTextBox.UnfocusedState, true);
    }

    private void AutoSuggestBox_GotFocus(object sender, RoutedEventArgs e)
    {
        // Verify if the usual behavior of clearing token selection is required
        if (Owner.PauseTokenClearOnFocus == false && !TokenizingTextBox.IsShiftPressed)
        {
            // Clear any selected tokens
            Owner.DeselectAll();
        }

        Owner.PauseTokenClearOnFocus = false;

        VisualStateManager.GoToState(Owner, TokenizingTextBox.FocusedState, true);
    }

    private void OnASBLoaded(object sender, RoutedEventArgs e)
    {
        if (autoSuggestTextBox is not null)
        {
            autoSuggestTextBox.PreviewKeyDown -= AutoSuggestTextBox_PreviewKeyDown;
            autoSuggestTextBox.TextChanging -= AutoSuggestTextBox_TextChangingAsync;
            autoSuggestTextBox.SelectionChanged -= AutoSuggestTextBox_SelectionChanged;
            autoSuggestTextBox.SelectionChanging -= AutoSuggestTextBox_SelectionChanging;
        }

        autoSuggestTextBox ??= autoSuggestBox.FindDescendant<TextBox>()!;

        UpdateQueryIconVisibility();
        UpdateTokensCounter(this);

        // Local function for Selection changed
        void AutoSuggestTextBox_SelectionChanged(object box, RoutedEventArgs args)
        {
            if (!(IsAllSelected || TokenizingTextBox.IsShiftPressed || Owner.IsClearingForClick))
            {
                Owner.DeselectAllTokensAndText(this);
            }

            // Ensure flag is always reset
            Owner.IsClearingForClick = false;
        }

        // local function for clearing selection on interaction with text box
        async void AutoSuggestTextBox_TextChangingAsync(TextBox o, TextBoxTextChangingEventArgs args)
        {
            // remove any selected tokens.
            if (Owner.SelectedItems.Count > 1)
            {
                await Owner.RemoveAllSelectedTokens().ConfigureAwait(true);
            }
        }

        if (autoSuggestTextBox is not null)
        {
            autoSuggestTextBox.PreviewKeyDown += AutoSuggestTextBox_PreviewKeyDown;
            autoSuggestTextBox.TextChanging += AutoSuggestTextBox_TextChangingAsync;
            autoSuggestTextBox.SelectionChanged += AutoSuggestTextBox_SelectionChanged;
            autoSuggestTextBox.SelectionChanging += AutoSuggestTextBox_SelectionChanging;

            AutoSuggestTextBoxLoaded?.Invoke(this, e);
        }
    }

    private void AutoSuggestTextBox_SelectionChanging(TextBox sender, TextBoxSelectionChangingEventArgs args)
    {
        isSelectedFocusOnFirstCharacter = args.SelectionLength > 0 && args.SelectionStart is 0 && autoSuggestTextBox.SelectionStart > 0;
        isSelectedFocusOnLastCharacter =
            //// see if we are NOW on the last character.
            //// test if the new selection includes the last character, and the current selection doesn't
            (args.SelectionStart + args.SelectionLength == autoSuggestTextBox.Text.Length) &&
            (autoSuggestTextBox.SelectionStart + autoSuggestTextBox.SelectionLength != autoSuggestTextBox.Text.Length);
    }

    private void AutoSuggestTextBox_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (IsCaretAtStart &&
            (e.Key is VirtualKey.Back ||
             e.Key is VirtualKey.Left))
        {
            // if the back key is pressed and there is any selection in the text box then the text box can handle it
            if ((e.Key is VirtualKey.Left && isSelectedFocusOnFirstCharacter) ||
                autoSuggestTextBox.SelectionLength is 0)
            {
                if (Owner.SelectPreviousItem(this))
                {
                    if (!TokenizingTextBox.IsShiftPressed)
                    {
                        // Clear any text box selection
                        autoSuggestTextBox.SelectionLength = 0;
                    }

                    e.Handled = true;
                }
            }
        }
        else if (IsCaretAtEnd && e.Key is VirtualKey.Right)
        {
            // if the back key is pressed and there is any selection in the text box then the text box can handle it
            if (isSelectedFocusOnLastCharacter || autoSuggestTextBox.SelectionLength is 0)
            {
                if (Owner.SelectNextItem(this))
                {
                    if (!TokenizingTextBox.IsShiftPressed)
                    {
                        // Clear any text box selection
                        autoSuggestTextBox.SelectionLength = 0;
                    }

                    e.Handled = true;
                }
            }
        }
        else if (e.Key is VirtualKey.A && TokenizingTextBox.IsControlPressed)
        {
            // Need to provide this shortcut from the textbox only, as ListViewBase will do it for us on token.
            Owner.SelectAllTokensAndText();
        }
    }

    private void UpdateTokensCounter(TokenizingTextBoxItem ttbi)
    {
        if (autoSuggestBox?.FindDescendant(PART_TokensCounter) is TextBlock maxTokensCounter)
        {
            void OnTokenCountChanged(TokenizingTextBox ttb, object? value = default)
            {
                if (ttb.ItemsSource is InterspersedObservableCollection itemsSource)
                {
                    int currentTokens = itemsSource.ItemsSource.Count;
                    int maxTokens = ttb.MaximumTokens;

                    maxTokensCounter.Text = $"{currentTokens}/{maxTokens}";
                    maxTokensCounter.Visibility = Visibility.Visible;

                    string targetState = (currentTokens >= maxTokens)
                        ? TokenizingTextBox.MaxReachedState
                        : TokenizingTextBox.MaxUnreachedState;

                    VisualStateManager.GoToState(autoSuggestTextBox, targetState, true);
                }
            }

            ttbi.Owner.TokenItemAdded -= OnTokenCountChanged;
            ttbi.Owner.TokenItemRemoved -= OnTokenCountChanged;

            if (Content is ITokenStringContainer { IsLast: true } str && ttbi is { Owner.MaximumTokens: >= 0 })
            {
                ttbi.Owner.TokenItemAdded += OnTokenCountChanged;
                ttbi.Owner.TokenItemRemoved += OnTokenCountChanged;
                OnTokenCountChanged(ttbi.Owner);
            }
            else
            {
                maxTokensCounter.Visibility = Visibility.Collapsed;
                maxTokensCounter.Text = string.Empty;
            }
        }
    }

    private void UpdateQueryIconVisibility()
    {
        if (autoSuggestBox.FindDescendant(QueryButton) is Button queryButton)
        {
            if (Owner.QueryIcon is not null)
            {
                queryButton.Visibility = Visibility.Visible;
            }
            else
            {
                queryButton.Visibility = Visibility.Collapsed;
            }
        }
    }
}
