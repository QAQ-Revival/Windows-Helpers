using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;

namespace QaqrKeyHelper
{
    internal static class Program
    {
        private const uint KeyUp = 0x0002;
        private static readonly Dictionary<string, int> StaticVk = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            { "VK_BACK", 0x08 }, { "VK_TAB", 0x09 }, { "VK_RETURN", 0x0D },
            { "VK_SHIFT", 0x10 }, { "VK_CONTROL", 0x11 }, { "VK_MENU", 0x12 },
            { "VK_ESCAPE", 0x1B }, { "VK_SPACE", 0x20 },
            { "VK_PRIOR", 0x21 }, { "VK_NEXT", 0x22 }, { "VK_END", 0x23 }, { "VK_HOME", 0x24 },
            { "VK_LEFT", 0x25 }, { "VK_UP", 0x26 }, { "VK_RIGHT", 0x27 }, { "VK_DOWN", 0x28 },
            { "VK_INSERT", 0x2D }, { "VK_DELETE", 0x2E }, { "VK_LWIN", 0x5B }, { "VK_RWIN", 0x5C },
            { "VK_NUMPAD0", 0x60 }, { "VK_NUMPAD1", 0x61 }, { "VK_NUMPAD2", 0x62 },
            { "VK_NUMPAD3", 0x63 }, { "VK_NUMPAD4", 0x64 }, { "VK_NUMPAD5", 0x65 },
            { "VK_NUMPAD6", 0x66 }, { "VK_NUMPAD7", 0x67 }, { "VK_NUMPAD8", 0x68 }, { "VK_NUMPAD9", 0x69 },
            { "VK_MULTIPLY", 0x6A }, { "VK_ADD", 0x6B }, { "VK_SUBTRACT", 0x6D },
            { "VK_DECIMAL", 0x6E }, { "VK_DIVIDE", 0x6F },
            { "VK_LSHIFT", 0xA0 }, { "VK_RSHIFT", 0xA1 }, { "VK_LCONTROL", 0xA2 },
            { "VK_RCONTROL", 0xA3 }, { "VK_LMENU", 0xA4 }, { "VK_RMENU", 0xA5 },
            { "VK_OEM_1", 0xBA }, { "VK_OEM_PLUS", 0xBB }, { "VK_OEM_COMMA", 0xBC },
            { "VK_OEM_MINUS", 0xBD }, { "VK_OEM_PERIOD", 0xBE }, { "VK_OEM_2", 0xBF },
            { "VK_OEM_3", 0xC0 }, { "VK_OEM_4", 0xDB }, { "VK_OEM_5", 0xDC },
            { "VK_OEM_6", 0xDD }, { "VK_OEM_7", 0xDE }
        };

        [DllImport("user32.dll", SetLastError = true)]
        private static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, UIntPtr extraInfo);

        private static int Main(string[] args)
        {
            try
            {
                Run(args, EmitKey, Thread.Sleep);
                return 0;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine(error.Message);
                return 1;
            }
        }

        private static void EmitKey(byte key, uint flags)
        {
            keybd_event(key, 0, flags, UIntPtr.Zero);
        }

        internal static void Run(string[] args, Action<byte, uint> emit, Action<int> sleep)
        {
            if (args.Length == 0) throw new ArgumentException("usage: QaqrKeyHelper.exe <f10|hotkey> [hotkeyBase64]");
            string mode = args[0].Trim().ToLowerInvariant();
            byte[] keys;
            if (mode == "f10") keys = new byte[] { 0x79 };
            else if (mode == "hotkey")
            {
                if (args.Length < 2) throw new ArgumentException("usage: QaqrKeyHelper.exe hotkey <hotkeyBase64>");
                keys = DecodeHotkey(args[1]);
            }
            else throw new ArgumentException("unknown mode: " + args[0]);

            // Keep the timings of the original executable, including reverse key release.
            int keyDelay = mode == "f10" ? 0 : 4;
            int pressed = 0;
            try
            {
                foreach (byte key in keys)
                {
                    emit(key, 0);
                    pressed++;
                    if (keyDelay != 0) sleep(keyDelay);
                }
                sleep(mode == "f10" ? 25 : 18);
            }
            finally
            {
                // Release held modifiers even if sending the remaining keys is interrupted.
                for (int index = pressed - 1; index >= 0; index--)
                {
                    emit(keys[index], KeyUp);
                    if (keyDelay != 0) sleep(keyDelay);
                }
            }
        }

        private static byte[] DecodeHotkey(string encoded)
        {
            string normalized = encoded.Trim().Replace('-', '+').Replace('_', '/');
            int remainder = normalized.Length % 4;
            if (remainder != 0) normalized = normalized.PadRight(normalized.Length + 4 - remainder, '=');
            string json = Encoding.UTF8.GetString(Convert.FromBase64String(normalized));
            HotkeyPayload payload = new JavaScriptSerializer().Deserialize<HotkeyPayload>(json);
            if (payload == null) throw new ArgumentException("invalid hotkey payload");
            if (payload.pressed == null || payload.pressed.Count == 0)
                throw new ArgumentException("no pressed keys in hotkey payload");

            // Validate every token before emitting any input.
            var keys = new List<byte>();
            foreach (HotkeyToken token in payload.pressed)
            {
                if (token == null || !String.Equals(token.device, "keyboard", StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("only keyboard hotkeys are supported");
                keys.Add(checked((byte)ResolveVkCode(token.code)));
            }
            return keys.ToArray();
        }

        internal static int ResolveVkCode(string code)
        {
            string value = (code ?? String.Empty).Trim().ToUpperInvariant();
            if (value.Length == 0) throw new ArgumentException("empty vk code");
            int key;
            if (StaticVk.TryGetValue(value, out key)) return key;
            if (Regex.IsMatch(value, "^VK_[A-Z0-9]$")) return value[value.Length - 1];
            Match function = Regex.Match(value, @"^VK_F([1-9]|1\d|2[0-4])$");
            if (function.Success) return 0x6F + Int32.Parse(function.Groups[1].Value);
            if (Regex.IsMatch(value, "^0X[0-9A-F]+$")) return Convert.ToInt32(value, 16);
            throw new ArgumentException("unsupported vk code: " + code);
        }
    }

    // Property names match the base64url JSON emitted by overlay/helper.cjs.
    internal sealed class HotkeyPayload
    {
        public List<HotkeyToken> pressed { get; set; }
    }

    internal sealed class HotkeyToken
    {
        public string code { get; set; }
        public string device { get; set; }
    }
}
