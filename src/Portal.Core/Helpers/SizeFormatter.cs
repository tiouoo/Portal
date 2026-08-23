namespace Portal.Core.Helpers;

public static class SizeFormatter
{
    public static string FormatSize(double bytes, bool includePerSecond = false)
    {
        const double kilobyte = 1024;
        const double megabyte = 1024 * 1024;
        const double gigabyte = 1024 * 1024 * 1024;

        string suffix;
        if (bytes < kilobyte)
            suffix = "B";
        else if (bytes < megabyte)
        {
            bytes /= kilobyte;
            suffix = "KB";
        }
        else if (bytes < gigabyte)
        {
            bytes /= megabyte;
            suffix = "MB";
        }
        else
        {
            bytes /= gigabyte;
            suffix = "GB";
        }

        var format = bytes < 100 ? "0.00" : "0.0";
        var result = bytes.ToString(format) + " " + suffix;
        return includePerSecond ? result + "/s" : result;
    }
}
