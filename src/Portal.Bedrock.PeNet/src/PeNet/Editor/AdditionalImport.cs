using System.Collections.Generic;

namespace PeNet;

public class AdditionalImport
{
        public AdditionalImport(string module, List<string> funcs)
    {
        Module = module;
        Functions = funcs;
    }

        public string Module { get; }

        public List<string> Functions { get; }
}