using System.Diagnostics;
using Microsoft.Win32;

namespace VrcPhotoPost;
public static class Association
{
    public static readonly string[] Extensions = [".png", ".jpg", ".jpeg", ".bmp", ".tif", ".tiff"];
    public static void Register()
    {
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("実行ファイルが見つかりません。");
        using (var name = Registry.CurrentUser.CreateSubKey("Software\\Classes\\VrcPhotoPost.Image")) name.SetValue("", "VRC Photo Post Image");
        using (var command = Registry.CurrentUser.CreateSubKey("Software\\Classes\\VrcPhotoPost.Image\\shell\\open\\command")) command.SetValue("", $"\"{executable}\" \"%1\"");
        using (var icon = Registry.CurrentUser.CreateSubKey("Software\\Classes\\VrcPhotoPost.Image\\DefaultIcon")) icon.SetValue("", $"\"{executable}\",0");
        foreach (var extension in Extensions) {
            using var open = Registry.CurrentUser.CreateSubKey($"Software\\Classes\\{extension}\\OpenWithProgids"); open.SetValue("VrcPhotoPost.Image", "");
            using var association = Registry.CurrentUser.CreateSubKey("Software\\VrcPhotoPost\\Capabilities\\FileAssociations"); association.SetValue(extension, "VrcPhotoPost.Image");
        }
        using (var capabilities = Registry.CurrentUser.CreateSubKey("Software\\VrcPhotoPost\\Capabilities")) {
            capabilities.SetValue("ApplicationName", "VRC Photo Post"); capabilities.SetValue("ApplicationDescription", "Photo library and X composer");
        }
        using (var registered = Registry.CurrentUser.CreateSubKey("Software\\RegisteredApplications")) registered.SetValue("VRC Photo Post", "Software\\VrcPhotoPost\\Capabilities");
        Process.Start(new ProcessStartInfo("ms-settings:defaultapps") { UseShellExecute = true });
    }
}
