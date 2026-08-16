using System.Collections.Generic;
using System.Text;
using PeNet.Crypto;
using PeNet.Header.Pe;

namespace PeNet.Header.ImpHash;

public class ImportHash
{
        public ImportHash(ICollection<ImportFunction>? importedFunctions)
    {
        ImpHash = importedFunctions is null ? null : ComputeImpHash(importedFunctions);
    }

        public string? ImpHash { get; }


    private static string? ComputeImpHash(ICollection<ImportFunction> importedFunctions)
    {
        if (importedFunctions == null || importedFunctions.Count == 0)
            return null;

        var list = new List<string>();
        foreach (var impFunc in importedFunctions)
        {
            var tmp = FormatLibraryName(impFunc.DLL);
            tmp += FormatFunctionName(impFunc);

            list.Add(tmp);
        }

        
        var imports = string.Join(",", list);

        
        var inputBytes = Encoding.ASCII.GetBytes(imports);
        return Hash.ComputeHash(inputBytes, Algorithm.Md5);
    }

    private static string FormatLibraryName(string libraryName)
    {
        var exts = new List<string> { "ocx", "sys", "dll" };
        var parts = libraryName.ToLower().Split('.');
        var libName = "";

#if NET48 || NETSTANDARD2_0
            if (parts.Length > 1 && exts.Contains(parts[parts.Length - 1]))
#else
        if (parts.Length > 1 && exts.Contains(parts[^1]))
#endif
            for (var i = 0; i < parts.Length - 1; i++)
            {
                libName += parts[i];
                libName += ".";
            }
        else
            foreach (var p in parts)
            {
                libName += p;
                libName += ".";
            }

        return libName;
    }

    private static string FormatFunctionName(ImportFunction impFunc)
    {
        var tmp = "";
        if (impFunc.Name == null) 
        {
            if (impFunc.DLL.ToLower() == "oleaut32.dll")
            {
                tmp += OrdinalSymbolMapping.Lookup(OrdinalSymbolMapping.Module.Oleaut32, impFunc.Hint);
            }
            else if (impFunc.DLL.ToLower() == "ws2_32.dll")
            {
                tmp += OrdinalSymbolMapping.Lookup(OrdinalSymbolMapping.Module.Ws2_32, impFunc.Hint);
            }
            else if (impFunc.DLL.ToLower() == "wsock32.dll")
            {
                tmp += OrdinalSymbolMapping.Lookup(OrdinalSymbolMapping.Module.Wsock32, impFunc.Hint);
            }
            else 
            {
                tmp += "ord";
                tmp += impFunc.Hint.ToString();
            }
        }
        else 
        {
            tmp += impFunc.Name;
        }

        return tmp.ToLower();
    }
}