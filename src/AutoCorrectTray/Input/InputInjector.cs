using AutoCorrectTray.Native;

namespace AutoCorrectTray.Input;

/// <summary>
/// Injects text and keys into the active window via SendInput.
/// Works with any application because it mimics real keyboard input.
/// Call only from the UI thread and only when focus is in the target field.
/// </summary>
public sealed class InputInjector
{
    public void TypeText(string text)
    {
        foreach (var ch in text)
        {
            if (ch == '\n' || ch == '\r')
                TapKey(NativeMethods.VK_RETURN);
            else if (ch == '\t')
                TapKey(NativeMethods.VK_TAB);
            else
                TypeChar(ch);
        }
    }

    public void TypeChar(char ch)
    {
        // With KEYEVENTF_UNICODE the character goes in wScan and wVk must be 0:
        // otherwise apps read wVk as a virtual key code, and Cyrillic
        // (U+0430+, low byte 0x30-0x4F) turns into top-row digits.
        SendUnicodeChar(ch, NativeMethods.KEYEVENTF_UNICODE);
        SendUnicodeChar(ch, NativeMethods.KEYEVENTF_UNICODE | NativeMethods.KEYEVENTF_KEYUP);
    }

    private static void SendUnicodeChar(char ch, uint flags)
    {
        var input = new NativeMethods.INPUT { type = NativeMethods.INPUT_KEYBOARD };
        input.U.ki.wVk = 0;
        input.U.ki.wScan = ch;
        input.U.ki.dwFlags = flags;
        SendInputs(new[] { input });
    }

    public void TapKey(int vk)
    {
        SendKey((ushort)vk, 0);
        SendKey((ushort)vk, NativeMethods.KEYEVENTF_KEYUP);
    }

    public void SendBackspaces(int count)
    {
        if (count <= 0) return;
        var inputs = new NativeMethods.INPUT[count * 2];
        for (int i = 0; i < count; i++)
        {
            inputs[i * 2] = KeyInput(NativeMethods.VK_BACK, 0);
            inputs[i * 2 + 1] = KeyInput(NativeMethods.VK_BACK, NativeMethods.KEYEVENTF_KEYUP);
        }
        SendInputs(inputs);
    }

    public void SendCopy()
    {
        // A single 4-event SendInput batch does not trigger Ctrl+C in some apps
        // (reproduced externally with a WinForms target): each event goes as its
        // own SendInput call, matching keybd_event semantics that do work
        SendKey(NativeMethods.VK_CONTROL, 0);
        Thread.Sleep(15);
        SendKey('C', 0);
        Thread.Sleep(15);
        SendKey('C', NativeMethods.KEYEVENTF_KEYUP);
        Thread.Sleep(15);
        SendKey(NativeMethods.VK_CONTROL, NativeMethods.KEYEVENTF_KEYUP);
    }

    public void SendRightClick()
    {
        var down = new NativeMethods.INPUT { type = NativeMethods.INPUT_MOUSE };
        down.U.mi.dwFlags = NativeMethods.MOUSEEVENTF_RIGHTDOWN;
        var up = new NativeMethods.INPUT { type = NativeMethods.INPUT_MOUSE };
        up.U.mi.dwFlags = NativeMethods.MOUSEEVENTF_RIGHTUP;
        SendInputs(new[] { down, up });
    }

    private static void SendKey(ushort code, uint flags)
    {
        var input = KeyInput(code, flags);
        SendInputs(new[] { input });
    }

    private static NativeMethods.INPUT KeyInput(int code, uint flags)
    {
        var input = new NativeMethods.INPUT { type = NativeMethods.INPUT_KEYBOARD };
        input.U.ki.wVk = (ushort)code;
        input.U.ki.dwFlags = flags;
        return input;
    }

    private static void SendInputs(NativeMethods.INPUT[] inputs)
    {
        var sent = NativeMethods.SendInput((uint)inputs.Length, inputs, NativeMethods.INPUT.Size);
        if (sent != inputs.Length)
            throw new InvalidOperationException($"SendInput sent {sent} of {inputs.Length} events");
    }
}
