using System.Runtime.InteropServices;
using System.Windows.Threading;
using AutoCorrectTray.Native;
using AutoCorrectTray.Punctuation;
using AutoCorrectTray.Settings;

namespace AutoCorrectTray.Hooks;

/// <summary>
/// Global WH_KEYBOARD_LL keyboard hook.
/// Maintains the typed-text buffer, performs autopunctuation (suppression +
/// re-sending keys), runs spell checking on word completion. Heavy work only
/// via BeginInvoke.
/// </summary>
public sealed class KeyboardHookService : IDisposable
{
    private readonly AppServices _svc;
    private readonly NativeMethods.LowLevelHookProc _proc;
    private IntPtr _hook;
    private readonly char[] _charBuffer = new char[16];
    private readonly byte[] _keyState = new byte[256];

    public KeyboardHookService(AppServices svc)
    {
        _svc = svc;
        _proc = HookProc;
        _hook = NativeMethods.SetWindowsHookEx(
            NativeMethods.WH_KEYBOARD_LL, _proc, NativeMethods.GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero)
            _svc.Log.Error($"Failed to install the keyboard hook: {Marshal.GetLastWin32Error()}");
        else
            _svc.Log.Info("Keyboard hook installed");
    }

    private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            try
            {
                var info = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
                if ((info.flags & NativeMethods.LLKHF_INJECTED) == 0 &&
                    wParam.ToInt32() is NativeMethods.WM_KEYDOWN or NativeMethods.WM_SYSKEYDOWN)
                {
                    if (HandleKeyDown(info))
                        return (IntPtr)1; // suppress - we re-send the keys ourselves
                }
            }
            catch (Exception ex)
            {
                _svc.Log.Error("Keyboard hook error", ex);
            }
        }
        return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private bool HandleKeyDown(NativeMethods.KBDLLHOOKSTRUCT info)
    {
        bool ctrl = NativeMethods.GetAsyncKeyState(NativeMethods.VK_CONTROL) < 0;
        bool alt = (info.flags & NativeMethods.LLKHF_ALTDOWN) != 0 ||
                   NativeMethods.GetAsyncKeyState(NativeMethods.VK_MENU) < 0;
        bool win = NativeMethods.GetAsyncKeyState(NativeMethods.VK_LWIN) < 0 ||
                   NativeMethods.GetAsyncKeyState(NativeMethods.VK_RWIN) < 0;

        if (ctrl || alt || win)
        {
            // Combos modify the field outside our control - reset the buffer
            if (ctrl && !alt && !win)
            {
                switch (info.vkCode)
                {
                    case (uint)'V' or (uint)'A' or (uint)'Z' or (uint)'X' or (uint)'Y':
                        _svc.Buffer.Reset();
                        break;
                }
                // Ctrl+A selects everything: the translate window may update
                if (info.vkCode == (uint)'A')
                    _svc.Dispatcher.BeginInvoke(new Action(() => _svc.HandleSelectionMaybeChanged(null)));
            }
            return false;
        }

        var buffer = _svc.Buffer;
        switch (info.vkCode)
        {
            case NativeMethods.VK_RETURN:
                return HandleEnter();

            case NativeMethods.VK_BACK:
                buffer.Backspace();
                _svc.Popups.HideHintIfVisible();
                _svc.RaiseTypedLive();
                return false;

            // 0x21..0x28: PgUp/PgDn/End/Home/arrows
            case >= NativeMethods.VK_PRIOR and <= NativeMethods.VK_DOWN:
            case NativeMethods.VK_DELETE or NativeMethods.VK_TAB or NativeMethods.VK_ESCAPE:
                buffer.Reset();
                _svc.Popups.HideHintIfVisible();
                if (info.vkCode == NativeMethods.VK_ESCAPE)
                    _svc.Dispatcher.BeginInvoke(new Action(() => _svc.Popups.HideOverlay()));
                // Shift+arrows/End/Home - keyboard selection: the translate window
                // may update from the new selection
                if (info.vkCode is >= NativeMethods.VK_PRIOR and <= NativeMethods.VK_DOWN &&
                    NativeMethods.GetAsyncKeyState(NativeMethods.VK_SHIFT) < 0)
                {
                    _svc.Dispatcher.BeginInvoke(new Action(() => _svc.HandleSelectionMaybeChanged(null)));
                }
                return false;

            case NativeMethods.VK_SPACE:
            {
                var completed = buffer.CurrentWord;
                buffer.Append(' ');
                _svc.Popups.HideHintIfVisible();
                if (completed.Length > 0)
                    _svc.OnWordCompleted(completed, " ");
                _svc.RaiseTypedLive();
                return false;
            }

            default:
            {
                var ch = TryGetChar(info);
                if (ch is null or ' ') return false;
                return HandlePrintable(ch.Value);
            }
        }
    }

    /// <summary>Enter: period at the end of the sentence (Enter is suppressed; ". Enter" is sent manually).</summary>
    private bool HandleEnter()
    {
        var settings = _svc.Settings;
        if (_svc.IsFeatureAllowedHere() && settings.AutoPeriod)
        {
            var plan = PunctuationEngine.PeriodOnEnter(_svc.Buffer.Text);
            if (plan is { } p)
            {
                _svc.Dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(() =>
                {
                    try
                    {
                        if (p.Backspaces > 0) _svc.Injector.SendBackspaces(p.Backspaces);
                        if (p.Insert.Length > 0) _svc.Injector.TypeText(p.Insert);
                        _svc.Injector.TapKey(NativeMethods.VK_RETURN);
                    }
                    catch (Exception ex)
                    {
                        _svc.Log.Error("Inserting period before Enter", ex);
                    }
                }));
                _svc.Buffer.ApplyPlan(p.Backspaces, p.Insert);
                _svc.Buffer.CommitSentence();
                _svc.RaiseTypedLive();
                return true;
            }
        }
        _svc.Buffer.CommitSentence();
        _svc.RaiseTypedLive();
        return false;
    }

    private bool HandlePrintable(char c)
    {
        if (!_svc.IsFeatureAllowedHere()) return false;

        // Suggestion popup open: 1-9 picks a suggestion,
        // any other letter keeps typing without picking
        if (c is >= '1' and <= '9' && _svc.Popups.TryPickSuggestionByDigit(c))
            return true;
        _svc.Popups.HideHintIfVisible();

        var settings = _svc.Settings;
        var buffer = _svc.Buffer;
        var action = PunctuationEngine.PlanIncoming(buffer.Text, c, new PunctuationSettings
        {
            AutoPeriod = settings.AutoPeriod,
            AutoCapitalize = settings.AutoCapitalize,
            SpaceAfterPunct = settings.SpaceAfterPunct,
        });

        switch (action)
        {
            case IncomingAction.Capitalize:
            {
                var upper = char.ToUpperInvariant(c);
                _svc.Dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(() =>
                {
                    try { _svc.Injector.TypeChar(upper); }
                    catch (Exception ex) { _svc.Log.Error("Inserting capital letter", ex); }
                }));
                buffer.Append(upper);
                _svc.RaiseTypedLive();
                return true; // the original lowercase key was suppressed
            }

            case IncomingAction.SpaceAndType:
            {
                _svc.Dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(() =>
                {
                    try { _svc.Injector.TypeText(" " + c); }
                    catch (Exception ex) { _svc.Log.Error("Inserting space", ex); }
                }));
                buffer.Append(' ');
                buffer.Append(c);
                _svc.RaiseTypedLive();
                return true;
            }

            default:
            {
                var completed = buffer.CurrentWord;
                buffer.Append(c);
                _svc.RaiseTypedLive();
                if (c is '.' or '!' or '?' or '…' or ',' or ';' or ':')
                {
                    _svc.Popups.HideHintIfVisible();
                    if (completed.Length > 0)
                        _svc.OnWordCompleted(completed, c.ToString());
                }

                // Auto-replace (live translation): sentence finished with a terminator -
                // translate everything typed so far and print it in place of the original
                if ((c is '.' or '!' or '?' or '…') &&
                    _svc.IsFeatureAllowedHere() &&
                    _svc.Settings.AutoReplaceLive)
                {
                    _svc.OnTypedSentenceCompleted(buffer.Text);
                }
                return false;
            }
        }
    }

    /// <summary>Maps vk+scan to a character using the current layout.</summary>
    private char? TryGetChar(NativeMethods.KBDLLHOOKSTRUCT info)
    {
        Array.Clear(_keyState);
        if (NativeMethods.GetAsyncKeyState(NativeMethods.VK_SHIFT) < 0)
        {
            _keyState[NativeMethods.VK_SHIFT] = 0x80;
            _keyState[NativeMethods.VK_LSHIFT] = 0x80;
            _keyState[NativeMethods.VK_RSHIFT] = 0x80;
        }
        if ((NativeMethods.GetKeyState(NativeMethods.VK_CAPITAL) & 1) != 0)
            _keyState[NativeMethods.VK_CAPITAL] = 0x01;

        // Decode with the FOREGROUND window layout: the buffer must mirror what the
        // target app receives, not what our own thread layout would produce
        var hkl = IntPtr.Zero;
        try
        {
            var fg = NativeMethods.GetForegroundWindow();
            if (fg != IntPtr.Zero)
            {
                uint fgThread = NativeMethods.GetWindowThreadProcessId(fg, out _);
                if (fgThread != 0) hkl = NativeMethods.GetKeyboardLayout(fgThread);
            }
        }
        catch { }

        int length = hkl != IntPtr.Zero
            ? NativeMethods.ToUnicodeEx(info.vkCode, info.scanCode, _keyState, _charBuffer, _charBuffer.Length, 0, hkl)
            : NativeMethods.ToUnicode(info.vkCode, info.scanCode, _keyState, _charBuffer, _charBuffer.Length, 0);
        if (length <= 0) return null; // dead key or no character
        return _charBuffer[0];
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }
}
