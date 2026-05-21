using System.Xml.Linq;
using Pumex.Cli;

namespace Pumex.Cli.Tests;

public class DaemonInstallerTests
{
    [Fact]
    public void BuildWindowsTaskXml_emits_valid_xml_with_required_elements()
    {
        var xml = DaemonInstaller.BuildWindowsTaskXml(
            @"C:\Users\test\.pumex\bin\pumex-daemon.exe",
            @"C:\Users\test\.pumex");

        var doc = XDocument.Parse(xml);
        XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";

        var task = doc.Root;
        Assert.NotNull(task);
        Assert.Equal("Task", task!.Name.LocalName);

        // Command and working directory both flow to the <Exec> element.
        var exec = task.Element(ns + "Actions")?.Element(ns + "Exec");
        Assert.NotNull(exec);
        Assert.Equal(@"C:\Users\test\.pumex\bin\pumex-daemon.exe", exec!.Element(ns + "Command")?.Value);
        Assert.Equal(@"C:\Users\test\.pumex", exec.Element(ns + "WorkingDirectory")?.Value);

        // Limited principal — no admin required.
        var principal = task.Element(ns + "Principals")?.Element(ns + "Principal");
        Assert.NotNull(principal);
        Assert.Equal("LeastPrivilege", principal!.Element(ns + "RunLevel")?.Value);
        Assert.Equal("InteractiveToken", principal.Element(ns + "LogonType")?.Value);

        // Logon trigger present and enabled.
        var trigger = task.Element(ns + "Triggers")?.Element(ns + "LogonTrigger");
        Assert.NotNull(trigger);
        Assert.Equal("true", trigger!.Element(ns + "Enabled")?.Value);
    }

    [Fact]
    public void BuildWindowsTaskXml_escapes_xml_special_chars_in_paths()
    {
        var xml = DaemonInstaller.BuildWindowsTaskXml(
            @"C:\with & ampersand\pumex-daemon.exe",
            @"C:\with <angle>\pumex");

        var doc = XDocument.Parse(xml); // would throw if escaping was wrong
        XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
        var exec = doc.Root!.Element(ns + "Actions")!.Element(ns + "Exec")!;

        Assert.Equal(@"C:\with & ampersand\pumex-daemon.exe", exec.Element(ns + "Command")!.Value);
        Assert.Equal(@"C:\with <angle>\pumex", exec.Element(ns + "WorkingDirectory")!.Value);
    }
}
