using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using PeNet.FileParser;
using PeNet.Header.Net.MetaDataTables;

namespace PeNet.Header.Net;

public interface IMetaDataTablesHdr
{
        byte HeapSizes { get; set; }

        List<MetaDataTablesHdr.MetaDataTableInfo> TableDefinitions { get; }
}

public class MetaDataTablesHdr : AbstractStructure, IMetaDataTablesHdr
{
    
    private readonly byte _originalHeapSizes;

    private List<MetaDataTableInfo>? _tableDefinitions;
    private Tables? _tables;

        public MetaDataTablesHdr(IRawFile peFile, long offset)
        : base(peFile, offset)
    {
        _originalHeapSizes = HeapSizes;
    }

        public uint Reserved1
    {
        get => PeFile.ReadUInt(Offset);
        set => PeFile.WriteUInt(Offset, value);
    }

        public byte MajorVersion
    {
        get => PeFile.ReadByte(Offset + 0x4);
        set => PeFile.WriteByte(Offset + 0x4, value);
    }

        public byte MinorVersion
    {
        get => PeFile.ReadByte(Offset + 0x5);
        set => PeFile.WriteByte(Offset + 0x5, value);
    }

        public byte Reserved2
    {
        get => PeFile.ReadByte(Offset + 0x7);
        set => PeFile.WriteByte(Offset + 0x7, value);
    }

        public MaskValidType MaskValid
    {
        get => (MaskValidType)PeFile.ReadULong(Offset + 0x8);
        set => PeFile.WriteULong(Offset + 0x8, (ulong)value);
    }

        public List<string> MaskValidResolved
        => ResolveMaskValid(MaskValid);

        public ulong MaskSorted
    {
        get => PeFile.ReadULong(Offset + 0x10);
        set => PeFile.WriteULong(Offset + 0x10, value);
    }

        public bool HasExtraData => (_originalHeapSizes & 0x40) != 0;

        public uint? ExtraData
    {
        get
        {
            if (HasExtraData)
            {
                var tablesCount = HammingWeight((ulong)MaskValid);
                return PeFile.ReadUInt(Offset + 24 + tablesCount * sizeof(uint));
            }

            return null;
        }
        set
        {
            if (value is { } newValue)
            {
                
                if (!HasExtraData)
                    throw new InvalidOperationException(
                        "Cannot add extra data to a tables stream header that did not originally contain extra data.");

                
                var tablesCount = HammingWeight((ulong)MaskValid);
                PeFile.WriteUInt(Offset + 24 + tablesCount * sizeof(uint), newValue);
            }
            else if (HasExtraData)
            {
                
                throw new InvalidOperationException(
                    "Cannot remove extra data from a tables stream header that originally contained extra data.");
            }
        }
    }

        public Tables Tables
    {
        get
        {
            if (_tables is null) _tables = ParseMetaDataTables();

            return _tables;
        }
    }

        public byte HeapSizes
    {
        get => PeFile.ReadByte(Offset + 0x6);
        set => PeFile.WriteByte(Offset + 0x6, value);
    }

        public List<MetaDataTableInfo> TableDefinitions
    {
        get
        {
            if (_tableDefinitions != null)
                return _tableDefinitions;

            _tableDefinitions = ParseTableDefinitions();
            return _tableDefinitions;
        }
    }

    private List<MetaDataTableInfo> ParseTableDefinitions()
    {
        var heapSizes = new HeapSizes(HeapSizes);


        var tables = new MetaDataTableInfo[64];

        var startOfTableDefinitions = Offset + 24;
        var names = ResolveMaskValid(MaskValid);

        
        var cnt = 0;
        for (var i = 0; i < tables.Length; ++i)
            if (((ulong)MaskValid & (1UL << i)) != 0)
            {
                tables[i].RowCount = PeFile.ReadUInt(startOfTableDefinitions + (uint)cnt * 4);
                if (cnt < names.Count)
                {
                    tables[i].Name = names[cnt];
                    ;
                }

                cnt++;

                
                
                if (tables[i].RowCount > PeFile.Length - startOfTableDefinitions)
                {
                    tables[i].RowCount = 0;
                    
                    tables[i].IsInvalid = true;
                }
            }

        var indexSizes = new IndexSize(tables);

        
        tables[(int)MetadataToken.Module].BytesPerRow = 2 + heapSizes.String + heapSizes.Guid * 3;
        tables[(int)MetadataToken.TypeReference].BytesPerRow = indexSizes[Index.ResolutionScope] + heapSizes.String * 2;
        tables[(int)MetadataToken.TypeDef].BytesPerRow = 4 + heapSizes.String * 2 + indexSizes[Index.TypeDefOrRef] +
                                                         indexSizes[Index.Field] + indexSizes[Index.MethodDef];
        tables[(int)MetadataToken.Field].BytesPerRow = 2 + heapSizes.String + heapSizes.Blob;
        tables[(int)MetadataToken.MethodDef].BytesPerRow =
            8 + heapSizes.String + heapSizes.Blob + GetIndexSize(MetadataToken.Parameter, tables);
        tables[(int)MetadataToken.Parameter].BytesPerRow = 4 + heapSizes.String;
        tables[(int)MetadataToken.InterfaceImplementation].BytesPerRow =
            GetIndexSize(MetadataToken.TypeDef, tables) + indexSizes[Index.TypeDefOrRef];
        tables[(int)MetadataToken.MemberReference].BytesPerRow =
            indexSizes[Index.MemberRefParent] + heapSizes.String + heapSizes.Blob;
        tables[(int)MetadataToken.Constant].BytesPerRow = 2 + indexSizes[Index.HasConstant] + heapSizes.Blob;
        tables[(int)MetadataToken.CustomAttribute].BytesPerRow = indexSizes[Index.HasCustomAttribute] +
                                                                 indexSizes[Index.CustomAttributeType] + heapSizes.Blob;
        tables[(int)MetadataToken.FieldMarshal].BytesPerRow = indexSizes[Index.HasFieldMarshal] + heapSizes.Blob;
        tables[(int)MetadataToken.DeclarativeSecurity].BytesPerRow =
            2 + indexSizes[Index.HasDeclSecurity] + heapSizes.Blob;
        tables[(int)MetadataToken.ClassLayout].BytesPerRow = 6 + GetIndexSize(MetadataToken.TypeDef, tables);
        tables[(int)MetadataToken.FieldLayout].BytesPerRow = 4 + GetIndexSize(MetadataToken.Field, tables);
        tables[(int)MetadataToken.StandAloneSignature].BytesPerRow = heapSizes.Blob;
        tables[(int)MetadataToken.EventMap].BytesPerRow = GetIndexSize(MetadataToken.TypeDef, tables) +
                                                          GetIndexSize(MetadataToken.Event, tables);
        tables[(int)MetadataToken.Event].BytesPerRow = 2 + heapSizes.String + indexSizes[Index.TypeDefOrRef];
        tables[(int)MetadataToken.PropertyMap].BytesPerRow = GetIndexSize(MetadataToken.TypeDef, tables) +
                                                             GetIndexSize(MetadataToken.Property, tables);
        tables[(int)MetadataToken.Property].BytesPerRow = 2 + heapSizes.String + heapSizes.Blob;
        tables[(int)MetadataToken.MethodSemantics].BytesPerRow =
            2 + GetIndexSize(MetadataToken.MethodDef, tables) + indexSizes[Index.HasSemantics];
        tables[(int)MetadataToken.MethodImplementation].BytesPerRow =
            GetIndexSize(MetadataToken.TypeDef, tables) + indexSizes[Index.MethodDefOrRef] * 2;
        tables[(int)MetadataToken.ModuleReference].BytesPerRow = heapSizes.String;
        tables[(int)MetadataToken.TypeSpecification].BytesPerRow = heapSizes.Blob;
        tables[(int)MetadataToken.ImplementationMap].BytesPerRow = 2 + indexSizes[Index.MemberForwarded] +
                                                                   heapSizes.String +
                                                                   GetIndexSize(MetadataToken.ModuleReference, tables);
        tables[(int)MetadataToken.FieldRVA].BytesPerRow = 4 + GetIndexSize(MetadataToken.Field, tables);
        tables[(int)MetadataToken.Assembly].BytesPerRow = 16 + heapSizes.Blob + heapSizes.String * 2;
        tables[(int)MetadataToken.AssemblyProcessor].BytesPerRow = 4;
        tables[(int)MetadataToken.AssemblyOS].BytesPerRow = 12;
        tables[(int)MetadataToken.AssemblyReference].BytesPerRow = 12 + heapSizes.Blob * 2 + heapSizes.String * 2;
        tables[(int)MetadataToken.AssemblyReferenceProcessor].BytesPerRow =
            4 + GetIndexSize(MetadataToken.AssemblyReference, tables);
        tables[(int)MetadataToken.AssemblyReferenceOS].BytesPerRow =
            12 + GetIndexSize(MetadataToken.AssemblyReference, tables);
        tables[(int)MetadataToken.File].BytesPerRow = 4 + heapSizes.String + heapSizes.Blob;
        tables[(int)MetadataToken.ExportedType].BytesPerRow =
            8 + heapSizes.String * 2 + indexSizes[Index.Implementation];
        tables[(int)MetadataToken.ManifestResource].BytesPerRow =
            8 + heapSizes.String + indexSizes[Index.Implementation];
        tables[(int)MetadataToken.NestedClass].BytesPerRow = GetIndexSize(MetadataToken.NestedClass, tables) * 2;
        tables[(int)MetadataToken.GenericParameter].BytesPerRow =
            4 + indexSizes[Index.TypeOrMethodDef] + heapSizes.String;
        tables[(int)MetadataToken.MethodSpecification].BytesPerRow = indexSizes[Index.MethodDefOrRef] + heapSizes.Blob;
        tables[(int)MetadataToken.GenericParameterConstraint].BytesPerRow =
            GetIndexSize(MetadataToken.GenericParameter, tables) + indexSizes[Index.TypeDefOrRef];


        
        uint offset = 0;
        for (var i = 0; i < tables.Length; ++i)
        {
            tables[i].Offset = offset;
            offset += tables[i].BytesPerRow * tables[i].RowCount;
        }

        return tables.ToList();
    }

    private Tables ParseMetaDataTables()
    {
        var tables = new Tables
        {
            Module = ParseTable<Module>(MetadataToken.Module),
            TypeRef = ParseTable<TypeRef>(MetadataToken.TypeReference),
            TypeDef = ParseTable<TypeDef>(MetadataToken.TypeDef),
            Field = ParseTable<Field>(MetadataToken.Field),
            MethodDef = ParseTable<MethodDef>(MetadataToken.MethodDef),
            Param = ParseTable<Param>(MetadataToken.Parameter),
            InterfaceImpl = ParseTable<InterfaceImpl>(MetadataToken.InterfaceImplementation),
            MemberRef = ParseTable<MemberRef>(MetadataToken.MemberReference),
            Constant = ParseTable<Constant>(MetadataToken.Constant),
            CustomAttribute = ParseTable<CustomAttribute>(MetadataToken.CustomAttribute),
            FieldMarshal = ParseTable<FieldMarshal>(MetadataToken.FieldMarshal),
            DeclSecurity = ParseTable<DeclSecurity>(MetadataToken.DeclarativeSecurity),
            ClassLayout = ParseTable<ClassLayout>(MetadataToken.ClassLayout),
            FieldLayout = ParseTable<FieldLayout>(MetadataToken.FieldLayout),
            StandAloneSig = ParseTable<StandAloneSig>(MetadataToken.StandAloneSignature),
            EventMap = ParseTable<EventMap>(MetadataToken.EventMap),
            Event = ParseTable<Event>(MetadataToken.Event),
            PropertyMap = ParseTable<PropertyMap>(MetadataToken.PropertyMap),
            Property = ParseTable<Property>(MetadataToken.Property),
            MethodSemantic = ParseTable<MethodSemantics>(MetadataToken.MethodSemantics),
            MethodImpl = ParseTable<MethodImpl>(MetadataToken.MethodImplementation),
            ModuleRef = ParseTable<ModuleRef>(MetadataToken.ModuleReference),
            TypeSpec = ParseTable<TypeSpec>(MetadataToken.TypeSpecification),
            ImplMap = ParseTable<ImplMap>(MetadataToken.ImplementationMap),
            FieldRVA = ParseTable<FieldRVA>(MetadataToken.FieldRVA),
            Assembly = ParseTable<Assembly>(MetadataToken.Assembly),
            AssemblyProcessor = ParseTable<AssemblyProcessor>(MetadataToken.AssemblyProcessor),
            AssemblyOS = ParseTable<AssemblyOS>(MetadataToken.AssemblyOS),
            AssemblyRef = ParseTable<AssemblyRef>(MetadataToken.AssemblyReference),
            AssemblyRefProcessor = ParseTable<AssemblyRefProcessor>(MetadataToken.AssemblyReferenceProcessor),
            AssemblyRefOS = ParseTable<AssemblyRefOS>(MetadataToken.AssemblyReferenceOS),
            File = ParseTable<MetaDataTables.File>(MetadataToken.File),
            ExportedType = ParseTable<ExportedType>(MetadataToken.ExportedType),
            ManifestResource = ParseTable<ManifestResource>(MetadataToken.ManifestResource),
            NestedClass = ParseTable<NestedClass>(MetadataToken.NestedClass),
            GenericParam = ParseTable<GenericParam>(MetadataToken.GenericParameter),
            GenericParamConstraints = ParseTable<GenericParamConstraint>(MetadataToken.GenericParameterConstraint)
        };

        return tables;
    }

    private List<T> ParseTable<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>(
        MetadataToken token)
        where T : AbstractTable
    {
        var heapSizes = new HeapSizes(HeapSizes);
        var indexSizes = new IndexSize(TableDefinitions.ToArray());
        var tablesOffset = (uint)(Offset + 0x18u + HammingWeight((ulong)MaskValid) * 4u);

        var tableInfo = TableDefinitions[(int)token];
        var rows = new List<T?>();

        var extraDataSize = HasExtraData
            ? sizeof(uint)
            : 0;

        if (tableInfo.RowCount != 0)
            for (var i = 0u; i < tableInfo.RowCount; i++)
            {
                var tableOffset = tablesOffset + extraDataSize + tableInfo.Offset + tableInfo.BytesPerRow * i;
                rows.Add(Activator.CreateInstance(typeof(T), PeFile, tableOffset, heapSizes, indexSizes) as T);
            }

        return rows.Count == 0 ? new List<T>(0) : rows!;
    }

    private static int HammingWeight(ulong value)
    {
        var count = 0;
        while (value != 0)
        {
            ++count;
            value &= value - 1;
        }

        return count;
    }

    private static uint GetIndexSize(MetadataToken table, MetaDataTableInfo[] tables)
    {
        return tables[(int)table].RowCount <= ushort.MaxValue ? 2U : 4U;
    }

        public static List<string> ResolveMaskValid(MaskValidType maskValid)
    {
        var st = new List<string>();
#if NET5_0_OR_GREATER
        var values = Enum.GetValues<MaskValidType>();
#else
        var values = (MaskValidType[])Enum.GetValues(typeof(MaskValidType));
#endif
        foreach (var flag in values)
            if ((maskValid & flag) == flag)
                st.Add(flag.ToString());

        return st;
    }

        public struct MetaDataTableInfo
    {
                public uint RowCount { get; set; }

                public string Name { get; set; }

                public uint Offset { get; set; }

                public uint BytesPerRow { get; set; }

                public bool IsInvalid { internal set; get; }
    }
}

[Flags]
public enum MaskValidType : ulong
{
        Module = 0x1,

        TypeRef = 0x2,

        TypeDef = 0x4,

        Field = 0x10,

        MethodDef = 0x40,

        Param = 0x100,

        InterfaceImpl = 0x200,

        MemberRef = 0x400,

        Constant = 0x800,

        CustomAttribute = 0x1000,

        FieldMarshal = 0x2000,

        DeclSecurity = 0x4000,

        ClassLayout = 0x8000,

        FieldLayout = 0x10000,

        StandAloneSig = 0x20000,

        EventMap = 0x40000,

        Event = 0x100000,

        PropertyMap = 0x200000,

        Property = 0x800000,

        MethodSemantics = 0x1000000,

        MethodImpl = 0x2000000,

        ModuleRef = 0x4000000,

        TypeSpec = 0x8000000,

        ImplMap = 0x10000000,

        FieldRva = 0x20000000,

        Assembly = 0x100000000,

        AssemblyProcessor = 0x200000000,

        AssemblyOS = 0x400000000,

        AssemblyRef = 0x800000000,

        AssemblyRefProcessor = 0x1000000000,

        AssemblyRefOS = 0x2000000000,

        File = 0x4000000000,

        ExportedType = 0x8000000000,

        ManifestResource = 0x10000000000,

        NestedClass = 0x20000000000,

        GenericParam = 0x40000000000,

        MethodSpec = 0x80000000000,

        GenericParamConstraint = 0x100000000000
}
