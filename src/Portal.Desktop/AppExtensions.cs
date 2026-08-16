using Avalonia;
using Avalonia.Dialogs;
using Portal.Classes.Enums;
using Portal.Const;

namespace Portal.Desktop;

public static class AppExtensions
{
    public static AppBuilder WithManagedSystemDialogs(this AppBuilder builder)
    {
        try
        {
            var data = File.ReadAllText(Path.Combine(ConfigPath.UserDataRootPath, "ManagedSystemDialogs.portal"));
            if (data == "true")
                builder.UseManagedSystemDialogs();
        }
        catch
        {
            
        }
        return builder;
    }

#if WINDOWS
    public static AppBuilder WithWindowsJumpList(this AppBuilder builder, string[] args)
    {
        WindowsJumpListService.Initialize(args);
        return builder;
    }
#endif
}
