using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using AutoCorrectTray.Native;

namespace AutoCorrectTray.Ui;

/// <summary>Suggestion bubble for a misspelled word. Does not steal input focus.</summary>
public partial class HintPopup : Window
{
    private readonly AppServices _svc;
    private Action<string>? _onPick;
    private Action? _onAdd;
    private List<string> _currentSuggestions = new();
    private DispatcherTimer? _autoHide;

    public HintPopup(AppServices svc)
    {
        InitializeComponent();
        _svc = svc;
        CloseButton.Click += (_, _) => Hide();
        AddToDictButton.Click += (_, _) =>
        {
            var onAdd = _onAdd;
            Hide();
            onAdd?.Invoke();
        };
        WindowDragHelper.Attach(HeaderBar, this);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        this.SetNoActivate();
        var source = PresentationSource.FromVisual(this) as HwndSource;
        source?.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_MOUSEACTIVATE)
        {
            handled = true;
            return (IntPtr)NativeMethods.MA_NOACTIVATE;
        }
        return IntPtr.Zero;
    }

    public void ShowFor(string word, string[] suggestions, Rect? anchor, Action<string> onPick, Action onAdd)
    {
        _onPick = onPick;
        _onAdd = onAdd;

        WrongWordText.Text = word;
        WrongWordText.Visibility = string.IsNullOrEmpty(word) ? Visibility.Collapsed : Visibility.Visible;

        _currentSuggestions = suggestions.Take(9).ToList();
        SuggestionPanel.Children.Clear();
        for (int i = 0; i < _currentSuggestions.Count; i++)
        {
            var captured = _currentSuggestions[i];
            var button = new Button
            {
                // Number next to the suggestion: pick with keys 1-9
                Content = $"{i + 1}  {captured}",
                Style = (Style)Resources["SuggestionButton"],
            };
            button.Click += (_, _) =>
            {
                Hide();
                _onPick?.Invoke(captured);
            };
            SuggestionPanel.Children.Add(button);
        }

            if (_currentSuggestions.Count == 0)
            {
                _currentSuggestions = new List<string>();
                var empty = new TextBlock
                {
                    Text = L10n.T("hint.none"),
                    FontSize = 12.5,
                    Margin = new Thickness(0, 0, 0, 6),
                };
                empty.SetResourceReference(TextBlock.ForegroundProperty, "TextMuted");
                SuggestionPanel.Children.Add(empty);
            }

        AddToDictButton.Visibility = _currentSuggestions.Count == 0 ? Visibility.Collapsed : Visibility.Visible;

        StartAutoHide();
        PopupPositioner.ShowPositioned(this, anchor, 6);
    }

    /// <summary>Picks a suggestion with key 1-9 while the bubble is open.</summary>
    public bool HandleDigit(char digit)
    {
        int index = digit - '1';
        if (index < 0 || index >= _currentSuggestions.Count) return false;
        var suggestion = _currentSuggestions[index];
        Hide();
        _onPick?.Invoke(suggestion);
        return true;
    }

    private void StartAutoHide()
    {
        _autoHide?.Stop();
        _autoHide = new DispatcherTimer { Interval = TimeSpan.FromSeconds(7) };
        _autoHide.Tick += (_, _) =>
        {
            _autoHide?.Stop();
            Hide();
        };
        _autoHide.Start();
    }
}
