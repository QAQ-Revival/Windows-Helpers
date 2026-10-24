using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32.SafeHandles;

namespace QaqrTrayHost
{
    // The icon keeps its executable path and ID while the manager changes name.
    // Windows owns the user's visible/overflow preference.
    internal sealed class TrayContext : ApplicationContext
    {
        private readonly Control dispatcher = new Control();
        private readonly NotifyIcon tray;
        private readonly Icon icon;
        private readonly ContextMenuStrip menu = new ContextMenuStrip();
        private readonly JavaScriptSerializer json = new JavaScriptSerializer();

        public TrayContext(Dictionary<string, string> options)
        {
            IntPtr handle = dispatcher.Handle;
            icon = new Icon(options["icon"]);
            menu.Items.Add(options["show"], null, delegate { Send("show"); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(options["quit"], null, delegate { Send("quit"); });
            tray = new NotifyIcon { Icon = icon, Text = "QAQ-Revival", ContextMenuStrip = menu };
            tray.DoubleClick += delegate { Send("show"); };
            tray.Visible = true;
            new Thread(ReadCommands) { IsBackground = true }.Start();
            Send("ready");
        }

        private void Send(string message)
        {
            try
            {
                Console.WriteLine(json.Serialize(new { type = message }));
                Console.Out.Flush();
            }
            catch { ExitThread(); }
        }

        private void ReadCommands()
        {
            // EOF also arrives when the manager crashes: leave no orphan helper.
            try { while (Console.ReadLine() != null) { } }
            catch { }
            try { dispatcher.BeginInvoke(new Action(ExitThread)); }
            catch (InvalidOperationException) { }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                tray.Visible = false;
                tray.Dispose();
                menu.Dispose();
                icon.Dispose();
                dispatcher.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    internal static class Program
    {
        [DllImport("kernel32.dll")]
        private static extern IntPtr GetStdHandle(int kind);

        [STAThread]
        private static void Main()
        {
            try
            {
                // A GUI executable has no console. Use only its inherited pipes;
                // changing Console.InputEncoding would require a console handle.
                using (var input = new FileStream(new SafeFileHandle(GetStdHandle(-10), false), FileAccess.Read))
                using (var output = new FileStream(new SafeFileHandle(GetStdHandle(-11), false), FileAccess.Write))
                using (var reader = new StreamReader(input, new UTF8Encoding(false)))
                using (var writer = new StreamWriter(output, new UTF8Encoding(false)) { AutoFlush = true })
                {
                    Console.SetIn(reader);
                    Console.SetOut(writer);
                    var line = Console.ReadLine();
                    if (line == null) return;
                    var options = new JavaScriptSerializer().Deserialize<Dictionary<string, string>>(line);
                    Application.EnableVisualStyles();
                    using (var context = new TrayContext(options)) Application.Run(context);
                }
            }
            catch
            {
                try { Console.WriteLine("{\"type\":\"error\"}"); } catch { }
                Environment.ExitCode = 1;
            }
        }
    }
}
