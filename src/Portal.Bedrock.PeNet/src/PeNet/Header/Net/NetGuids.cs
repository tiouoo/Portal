using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PeNet.Header.Net;

internal class NetGuids
{
    public NetGuids(PeFile peFile)
    {
        ModuleVersionIds = ParseModuleVersionIds(peFile);
        ComTypeLibId = ParseComTypeLibId(peFile);
    }

        public List<Guid>? ModuleVersionIds { get; }

        public Guid? ComTypeLibId { get; }

    private List<Guid>? ParseModuleVersionIds(PeFile peFile)
    {
        try
        {
            return peFile.MetaDataStreamTablesHeader?.Tables.Module?.Select(m =>
                    peFile.MetaDataStreamGuid?.GetGuidAtIndex(m.Mvid) ?? Guid.Empty)
                .Where(g => g != Guid.Empty)
                .ToList() ?? new List<Guid>();
        }
        catch (Exception)
        {
            return null;
        }
    }

        public Guid? ParseComTypeLibId(PeFile peFile)
    {
        try
        {
            

            
            if (peFile.MetaDataStreamTablesHeader is null)
                throw new ArgumentException("Meta Data Stream Tables Header must not be null.",
                    nameof(peFile.MetaDataStreamTablesHeader));
            var blobIndexSize = new HeapSizes(peFile.MetaDataStreamTablesHeader.HeapSizes).Blob;

            
            var typeRefTable = peFile.MetaDataStreamTablesHeader?.Tables.TypeRef;
            var stringsStream = peFile.MetaDataStreamString;

            var typeRefTableIndex = 1; 
            for (; typeRefTableIndex <= typeRefTable?.Count; typeRefTableIndex++)
            {
                var typeRefTableRow = typeRefTable?[typeRefTableIndex - 1]; 
                if (typeRefTableRow != null &&
                    "GuidAttribute" == stringsStream?.GetStringAtIndex(typeRefTableRow.TypeName) &&
                    "System.Runtime.InteropServices" ==
                    stringsStream?.GetStringAtIndex(typeRefTableRow.TypeNamespace))
                    break;
            }

            if (typeRefTableIndex <= typeRefTable?.Count)
            {
                

                
                var memberRefTable = peFile?.MetaDataStreamTablesHeader?.Tables.MemberRef;
                var memberRefTableIndex = 1;
                for (; memberRefTableIndex <= memberRefTable?.Count; memberRefTableIndex++)
                {
                    var memberRefTableRow = memberRefTable?[memberRefTableIndex - 1];
                    if ((memberRefTableRow?.Class & 0x7) == 0x1 
                        && memberRefTableRow?.Class >> 3 == typeRefTableIndex)
                        break;
                }

                if (memberRefTableIndex <= memberRefTable?.Count)
                {
                    

                    
                    var customAttributeTable = peFile?.MetaDataStreamTablesHeader?.Tables?.CustomAttribute;
                    if (customAttributeTable is null)
                        return null;

                    foreach (var row in customAttributeTable)
                        if ((row.Type & 0x7) == 0x3 
                            && row.Type >> 3 == memberRefTableIndex)
                        {
                            
                            
                            var blobIndex = row.Value;

                            var guidStart = blobIndex + blobIndexSize + 2; 
                            var guidLength = 36;
                            if (guidStart + guidLength < peFile?.MetaDataStreamBlob?.Length
                                && peFile?.MetaDataStreamBlob?[guidStart - 1] == guidLength) 
                            {
                                var guidBytes = peFile.MetaDataStreamBlob.Skip((int)guidStart).Take(guidLength)
                                    .ToArray();
                                var s = Encoding.ASCII.GetString(guidBytes);
                                return new Guid(s);
                            }
                        }
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }
}