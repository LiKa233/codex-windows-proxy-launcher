using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;

[assembly: AssemblyTitle("Codex Proxy Launcher")]
[assembly: AssemblyDescription("Per-process proxy launcher for the Windows Microsoft Store OpenAI.Codex client")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyInformationalVersion("1.0.0-rc.1")]

internal static class Program
{
    private const string Version = "1.0.0-rc.1";
    private static readonly string[] Resources = { "Launcher.Core.psm1", "Launcher.ps1" };

    // Windows CommandLineToArgvW-compatible quoting, including quotes and trailing backslashes.
    private static string Quote(string value)
    {
        var builder = new StringBuilder("\"");
        int backslashes = 0;
        foreach (char character in value)
        {
            if (character == '\\') { backslashes++; continue; }
            if (character == '"') builder.Append('\\', backslashes * 2 + 1).Append('"');
            else builder.Append('\\', backslashes).Append(character);
            backslashes = 0;
        }
        return builder.Append('\\', backslashes * 2).Append('"').ToString();
    }

    private static byte[] ReadResource(string name)
    {
        using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
        {
            if (stream == null) throw new InvalidDataException("Embedded launcher resource is missing: " + name);
            using (var buffer = new MemoryStream()) { stream.CopyTo(buffer); return buffer.ToArray(); }
        }
    }

    private static string Hash(byte[] bytes)
    {
        using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
    }

    private static string MaterializeCore()
    {
        byte[][] contents = Resources.Select(ReadResource).ToArray();
        string identity = Hash(contents.SelectMany(bytes => bytes).ToArray());
        string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexProxyLauncher", "runtime", identity);
        Directory.CreateDirectory(root);
        for (int index = 0; index < Resources.Length; index++)
        {
            string path = Path.Combine(root, Resources[index]);
            if (!File.Exists(path) || !File.ReadAllBytes(path).SequenceEqual(contents[index]))
            {
                // Always replace altered cached resources from the embedded payload.
                string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    File.WriteAllBytes(temporary, contents[index]);
                    if (File.Exists(path)) File.Replace(temporary, path, null);
                    else File.Move(temporary, path);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
        }
        return root;
    }

    private static void ShowReport(string report)
    {
        using (var form = new Form())
        using (var text = new TextBox())
        {
            form.Text = "Codex Proxy Launcher - Read-only diagnostic";
            form.Width = 820; form.Height = 620; form.StartPosition = FormStartPosition.CenterScreen;
            text.Multiline = true; text.ReadOnly = true; text.ScrollBars = ScrollBars.Both;
            text.WordWrap = false; text.Dock = DockStyle.Fill; text.Text = report;
            form.Controls.Add(text); form.ShowDialog();
        }
    }

    [STAThread]
    private static int Main(string[] args)
    {
        Application.EnableVisualStyles();
        try
        {
            if (args.Length == 1 && args[0] == "--verify-payload")
            {
                // Packaging/CI check: no extraction, GUI, config write, network or app launch.
                foreach (string name in Resources) if (ReadResource(name).Length == 0) return 10;
                return 0;
            }
            if (args.Length == 1 && args[0] == "--version")
            { MessageBox.Show(Version, "Codex Proxy Launcher"); return 0; }
            bool configure = args.Contains("--configure"), check = args.Contains("--check");
            if (args.Any(arg => arg != "--configure" && arg != "--check") || (configure && check))
                throw new ArgumentException("Supported options: --configure, --check, --version. Configure and check cannot be combined.");

            string executableDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string config = Path.Combine(executableDirectory, "launcher.config.json");
            string root = MaterializeCore();
            string powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
            var info = new ProcessStartInfo(powershell);
            info.UseShellExecute = false; info.CreateNoWindow = true;
            info.WindowStyle = ProcessWindowStyle.Hidden;
            info.RedirectStandardOutput = true; info.RedirectStandardError = true;
            info.StandardOutputEncoding = Encoding.UTF8; info.StandardErrorEncoding = Encoding.UTF8;
            // Console encoding is set before script invocation; paths are UTF-8 base64 values,
            // not interpolated PowerShell syntax. The encoded command contains no secrets.
            string script64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(Path.Combine(root, "Launcher.ps1")));
            string config64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(config));
            string command = "[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false); & ([Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('" + script64 + "'))) -ConfigPath ([Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('" + config64 + "')))";
            if (configure) command += " -Configure";
            if (check) command += " -CheckOnly";
            info.Arguments = "-NoLogo -NoProfile -STA -ExecutionPolicy Bypass -EncodedCommand " + Quote(Convert.ToBase64String(Encoding.Unicode.GetBytes(command)));
            using (Process child = Process.Start(info))
            {
                if (child == null) throw new InvalidOperationException("Could not start Windows PowerShell.");
                // Drain both pipes concurrently so large diagnostics cannot deadlock.
                var outputTask = child.StandardOutput.ReadToEndAsync();
                var errorTask = child.StandardError.ReadToEndAsync();
                child.WaitForExit();
                string output = outputTask.GetAwaiter().GetResult();
                string error = errorTask.GetAwaiter().GetResult();
                if (check) ShowReport(output + (String.IsNullOrWhiteSpace(error) ? "" : "\r\n" + error));
                else if (child.ExitCode != 0 && child.ExitCode != 2 && !String.IsNullOrWhiteSpace(error))
                    MessageBox.Show(error, "Launcher helper failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return child.ExitCode;
            }
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Codex Proxy Launcher", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return 20;
        }
    }
}
