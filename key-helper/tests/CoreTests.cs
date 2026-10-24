using System;
using System.Collections.Generic;
using System.Text;

namespace QaqrKeyHelper
{
    internal static class CoreTests
    {
        private static string Encode(string json)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(json)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        private static List<string> Trace(string[] args)
        {
            var trace = new List<string>();
            Program.Run(args, (key, flags) => trace.Add(key + ":" + flags), delay => trace.Add("wait:" + delay));
            return trace;
        }

        private static void Equal(string expected, List<string> trace)
        {
            string actual = String.Join(",", trace);
            if (actual != expected) throw new Exception("Expected " + expected + "; got " + actual);
        }

        private static int Main()
        {
            try
            {
                Equal("121:0,wait:25,121:2", Trace(new[] { " F10 " }));
                string chord = "{\"pressed\":[{\"device\":\"keyboard\",\"code\":\"VK_CONTROL\"},{\"device\":\"keyboard\",\"code\":\"VK_F10\"}],\"displayKeys\":[\"Ctrl\",\"F10\"]}";
                Equal("17:0,wait:4,121:0,wait:4,wait:18,121:2,wait:4,17:2,wait:4", Trace(new[] { "hotkey", Encode(chord) }));
                Console.WriteLine("PASS F10 and base64url chord timing and release order");

                string[] codes = { " vk_a ", "VK_9", "VK_F1", "VK_F24", "VK_NUMPAD0", "VK_RCONTROL", "VK_OEM_7", "0xff" };
                int[] values = { 65, 57, 112, 135, 96, 163, 222, 255 };
                for (int index = 0; index < codes.Length; index++)
                    if (Program.ResolveVkCode(codes[index]) != values[index]) throw new Exception("Wrong key mapping: " + codes[index]);
                Console.WriteLine("PASS virtual-key mapping");

                var invalid = new List<string[]> { new string[0], new[] { "unknown" }, new[] { "hotkey" }, new[] { "hotkey", "!bad-base64!" } };
                foreach (string payload in new[] { "{", "null", "{}", "{\"pressed\":null}", "{\"pressed\":[]}", "{\"pressed\":[null]}" })
                    invalid.Add(new[] { "hotkey", Encode(payload) });
                foreach (string token in new[] { "{\"device\":\"mouse\",\"code\":\"VK_A\"}", "{\"device\":\"keyboard\",\"code\":\"VK_F25\"}", "{\"device\":\"keyboard\",\"code\":\"0x100\"}" })
                    invalid.Add(new[] { "hotkey", Encode("{\"pressed\":[{\"device\":\"keyboard\",\"code\":\"VK_CONTROL\"}," + token + "]}") });
                foreach (string[] args in invalid)
                {
                    bool failed = false;
                    int emitted = 0;
                    try { Program.Run(args, (key, flags) => emitted++, delay => { }); }
                    catch (Exception) { failed = true; }
                    if (!failed || emitted != 0) throw new Exception("Invalid input was not rejected before key emission");
                }
                Console.WriteLine("PASS invalid commands and payloads emit no input");

                var interrupted = new List<string>();
                bool interruptedOnce = false;
                try
                {
                    Program.Run(new[] { "hotkey", Encode(chord) }, (key, flags) => interrupted.Add(key + ":" + flags), delay =>
                    {
                        if (!interruptedOnce) { interruptedOnce = true; throw new OperationCanceledException(); }
                    });
                    throw new Exception("Expected interruption");
                }
                catch (OperationCanceledException) { }
                Equal("17:0,17:2", interrupted);
                Console.WriteLine("PASS interrupted key sequence releases held modifiers");
                return 0;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine(error);
                return 1;
            }
        }
    }
}
