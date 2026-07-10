using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace Direct2dCad.wpf.Controls;

[TemplatePart(Name = PART_TextBlock, Type = typeof(TextBlock))]
[TemplatePart(Name = PART_TextBox, Type = typeof(TextBox))]
public class EditableTextBlock : System.Windows.Controls.Control
{
    private const string PART_TextBlock = "PART_TextBlock";
    private const string PART_TextBox = "PART_TextBox";

    private TextBlock? _textBlock;
    private TextBox? _textBox;

    private string _originalText = string.Empty;

    static EditableTextBlock()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(EditableTextBlock),
            new FrameworkPropertyMetadata(typeof(EditableTextBlock)));

        FocusableProperty.OverrideMetadata(
            typeof(EditableTextBlock),
            new FrameworkPropertyMetadata(true));
    }

    public event EventHandler? EditStarted;

    public event EventHandler<EditableTextBlockEditEventArgs>? EditCommitted;

    public event EventHandler<EditableTextBlockEditEventArgs>? EditCanceled;

    public event EventHandler<EditableTextBlockTextValidatingEventArgs>? TextValidating;

    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(
            nameof(Text),
            typeof(string),
            typeof(EditableTextBlock),
            new FrameworkPropertyMetadata(
                string.Empty,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnTextChanged));

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public static readonly DependencyProperty IsEditingProperty =
        DependencyProperty.Register(
            nameof(IsEditing),
            typeof(bool),
            typeof(EditableTextBlock),
            new FrameworkPropertyMetadata(false, OnIsEditingChanged));

    public bool IsEditing
    {
        get => (bool)GetValue(IsEditingProperty);
        set => SetValue(IsEditingProperty, value);
    }

    public static readonly DependencyProperty IsReadOnlyProperty =
        DependencyProperty.Register(
            nameof(IsReadOnly),
            typeof(bool),
            typeof(EditableTextBlock),
            new PropertyMetadata(false));

    public bool IsReadOnly
    {
        get => (bool)GetValue(IsReadOnlyProperty);
        set => SetValue(IsReadOnlyProperty, value);
    }

    public static readonly DependencyProperty BeginEditOnDoubleClickProperty =
        DependencyProperty.Register(
            nameof(BeginEditOnDoubleClick),
            typeof(bool),
            typeof(EditableTextBlock),
            new PropertyMetadata(true));

    public bool BeginEditOnDoubleClick
    {
        get => (bool)GetValue(BeginEditOnDoubleClickProperty);
        set => SetValue(BeginEditOnDoubleClickProperty, value);
    }

    public static readonly DependencyProperty BeginEditOnF2Property =
        DependencyProperty.Register(
            nameof(BeginEditOnF2),
            typeof(bool),
            typeof(EditableTextBlock),
            new PropertyMetadata(true));

    public bool BeginEditOnF2
    {
        get => (bool)GetValue(BeginEditOnF2Property);
        set => SetValue(BeginEditOnF2Property, value);
    }

    public static readonly DependencyProperty CommitOnLostFocusProperty =
        DependencyProperty.Register(
            nameof(CommitOnLostFocus),
            typeof(bool),
            typeof(EditableTextBlock),
            new PropertyMetadata(true));

    public bool CommitOnLostFocus
    {
        get => (bool)GetValue(CommitOnLostFocusProperty);
        set => SetValue(CommitOnLostFocusProperty, value);
    }

    public static readonly DependencyProperty SelectAllOnEditProperty =
        DependencyProperty.Register(
            nameof(SelectAllOnEdit),
            typeof(bool),
            typeof(EditableTextBlock),
            new PropertyMetadata(true));

    public bool SelectAllOnEdit
    {
        get => (bool)GetValue(SelectAllOnEditProperty);
        set => SetValue(SelectAllOnEditProperty, value);
    }

    public static readonly DependencyProperty AllowEmptyTextProperty =
        DependencyProperty.Register(
            nameof(AllowEmptyText),
            typeof(bool),
            typeof(EditableTextBlock),
            new PropertyMetadata(false));

    public bool AllowEmptyText
    {
        get => (bool)GetValue(AllowEmptyTextProperty);
        set => SetValue(AllowEmptyTextProperty, value);
    }

    public static readonly DependencyProperty TrimTextOnCommitProperty =
        DependencyProperty.Register(
            nameof(TrimTextOnCommit),
            typeof(bool),
            typeof(EditableTextBlock),
            new PropertyMetadata(true));

    public bool TrimTextOnCommit
    {
        get => (bool)GetValue(TrimTextOnCommitProperty);
        set => SetValue(TrimTextOnCommitProperty, value);
    }

    public static readonly DependencyProperty EmptyTextErrorMessageProperty =
        DependencyProperty.Register(
            nameof(EmptyTextErrorMessage),
            typeof(string),
            typeof(EditableTextBlock),
            new PropertyMetadata("名称不能为空。"));

    public string EmptyTextErrorMessage
    {
        get => (string)GetValue(EmptyTextErrorMessageProperty);
        set => SetValue(EmptyTextErrorMessageProperty, value);
    }

    public static readonly DependencyProperty TextTrimmingProperty =
        DependencyProperty.Register(
            nameof(TextTrimming),
            typeof(TextTrimming),
            typeof(EditableTextBlock),
            new PropertyMetadata(TextTrimming.CharacterEllipsis));

    public TextTrimming TextTrimming
    {
        get => (TextTrimming)GetValue(TextTrimmingProperty);
        set => SetValue(TextTrimmingProperty, value);
    }

    public static readonly DependencyProperty TextWrappingProperty =
        DependencyProperty.Register(
            nameof(TextWrapping),
            typeof(TextWrapping),
            typeof(EditableTextBlock),
            new PropertyMetadata(TextWrapping.NoWrap));

    public TextWrapping TextWrapping
    {
        get => (TextWrapping)GetValue(TextWrappingProperty);
        set => SetValue(TextWrappingProperty, value);
    }

    public static readonly DependencyProperty TextAlignmentProperty =
        DependencyProperty.Register(
            nameof(TextAlignment),
            typeof(TextAlignment),
            typeof(EditableTextBlock),
            new PropertyMetadata(TextAlignment.Left));

    public TextAlignment TextAlignment
    {
        get => (TextAlignment)GetValue(TextAlignmentProperty);
        set => SetValue(TextAlignmentProperty, value);
    }

    public override void OnApplyTemplate()
    {
        if (_textBox != null)
        {
            _textBox.KeyDown -= OnTextBoxKeyDown;
            _textBox.LostFocus -= OnTextBoxLostFocus;
        }

        base.OnApplyTemplate();

        _textBlock = GetTemplateChild(PART_TextBlock) as TextBlock;
        _textBox = GetTemplateChild(PART_TextBox) as TextBox;

        if (_textBox != null)
        {
            _textBox.Text = Text ?? string.Empty;
            _textBox.KeyDown += OnTextBoxKeyDown;
            _textBox.LostFocus += OnTextBoxLostFocus;
        }

        if (IsEditing)
        {
            FocusTextBoxAsync();
        }
    }

    public bool BeginEdit()
    {
        if (IsReadOnly)
        {
            return false;
        }

        if (IsEditing)
        {
            return false;
        }

        _originalText = Text ?? string.Empty;

        if (_textBox != null)
        {
            _textBox.Text = _originalText;
        }

        ClearError();

        IsEditing = true;
        EditStarted?.Invoke(this, EventArgs.Empty);

        FocusTextBoxAsync();

        return true;
    }

    public bool CommitEdit()
    {
        if (!IsEditing)
        {
            return false;
        }

        var oldText = _originalText;
        var newText = _textBox?.Text ?? string.Empty;

        if (TrimTextOnCommit)
        {
            newText = newText.Trim();
        }

        if (!AllowEmptyText && string.IsNullOrWhiteSpace(newText))
        {
            SetError(EmptyTextErrorMessage);
            FocusTextBoxAsync();
            return false;
        }

        var validatingArgs = new EditableTextBlockTextValidatingEventArgs(oldText, newText);
        TextValidating?.Invoke(this, validatingArgs);

        if (!validatingArgs.IsValid)
        {
            SetError(validatingArgs.ErrorMessage ?? "输入内容无效。");
            FocusTextBoxAsync();
            return false;
        }

        ClearError();

        Text = newText;
        IsEditing = false;

        EditCommitted?.Invoke(
            this,
            new EditableTextBlockEditEventArgs(oldText, newText));

        return true;
    }

    public bool CancelEdit()
    {
        if (!IsEditing)
        {
            return false;
        }

        var editingText = _textBox?.Text ?? string.Empty;

        if (_textBox != null)
        {
            _textBox.Text = _originalText;
        }

        ClearError();

        IsEditing = false;

        EditCanceled?.Invoke(
            this,
            new EditableTextBlockEditEventArgs(_originalText, editingText));

        return true;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);

        if (e.Handled)
        {
            return;
        }

        if (!BeginEditOnDoubleClick)
        {
            return;
        }

        if (e.ClickCount >= 2)
        {
            Focus();
            BeginEdit();
            e.Handled = true;
        }
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);

        if (e.Handled)
        {
            return;
        }

        if (!IsEditing && BeginEditOnF2 && e.Key == Key.F2)
        {
            BeginEdit();
            e.Handled = true;
        }
    }

    private void OnTextBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitEdit();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            CancelEdit();
            e.Handled = true;
        }
    }

    private void OnTextBoxLostFocus(object sender, RoutedEventArgs e)
    {
        if (!IsEditing)
        {
            return;
        }

        if (CommitOnLostFocus)
        {
            if (!CommitEdit())
            {
                CancelEdit();
            }
        }
        else
        {
            CancelEdit();
        }
    }

    private void FocusTextBoxAsync()
    {
        Dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            new Action(() =>
            {
                if (!IsEditing || _textBox == null)
                {
                    return;
                }

                _textBox.Focus();
                Keyboard.Focus(_textBox);

                if (SelectAllOnEdit)
                {
                    _textBox.SelectAll();
                }
                else
                {
                    _textBox.CaretIndex = _textBox.Text.Length;
                }
            }));
    }

    private void SetError(string message)
    {
        if (_textBox != null)
        {
            _textBox.ToolTip = message;
        }
    }

    private void ClearError()
    {
        if (_textBox != null)
        {
            _textBox.ToolTip = null;
        }
    }

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (EditableTextBlock)d;

        if (!control.IsEditing && control._textBox != null)
        {
            control._textBox.Text = control.Text ?? string.Empty;
        }
    }

    private static void OnIsEditingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (EditableTextBlock)d;

        if ((bool)e.NewValue)
        {
            control._originalText = control.Text ?? string.Empty;

            if (control._textBox != null)
            {
                control._textBox.Text = control._originalText;
            }

            control.FocusTextBoxAsync();
        }
        else
        {
            control.ClearError();
        }
    }
}

public sealed class EditableTextBlockEditEventArgs : EventArgs
{
    public EditableTextBlockEditEventArgs(string oldText, string newText)
    {
        OldText = oldText;
        NewText = newText;
    }

    public string OldText { get; }

    public string NewText { get; }
}

public sealed class EditableTextBlockTextValidatingEventArgs : EventArgs
{
    public EditableTextBlockTextValidatingEventArgs(string oldText, string newText)
    {
        OldText = oldText;
        NewText = newText;
        IsValid = true;
    }

    public string OldText { get; }

    public string NewText { get; }

    public bool IsValid { get; set; }

    public string? ErrorMessage { get; set; }
}

