/*
<copyright file="ConfigEntity.cs" company="Round-Studio">
Copyright (c) Round-Studio. All rights reserved.
This file is part of the Round.SDK project and is licensed under the GNU General Public License v3.0.
See the LICENSE file in the project root for full license information.

This program is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
GNU General Public License for more details.

You should have received a copy of the GNU General Public License
along with this program. If not, see <https://www.gnu.org/licenses/>.
</copyright>

<summary>
提供泛型配置文件的加载、保存及生命周期回调管理功能。
支持 JSON 序列化/反序列化，并通过事件和回调机制允许在保存前后执行自定义逻辑。
</summary>
*/

using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Portal.Core.Entity;

public class ConfigEntity<T> where T : new()
{
    private readonly JsonTypeInfo<T>? TypeInfo;

    public ConfigEntity(string configFile, bool isSave = true, JsonTypeInfo<T>? typeInfo = default)
    {
        Path = configFile;
        TypeInfo = typeInfo;
        IsSave = isSave;
        Load();
    }

    public T Data { get; set; }
    public string Path { get; }
    public bool IsSave { get; set; } = true;
    public static JsonSerializerOptions Options = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true
    };

    public void Load()
    {
        if (!File.Exists(Path))
        {
            var directory = System.IO.Path.GetDirectoryName(Path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            Save();
            return;
        }

        var json = File.ReadAllText(Path);
        if (string.IsNullOrEmpty(json))
            Save();
        else
            try
            {
                Data = TypeInfo != null
                    ? JsonSerializer.Deserialize<T>(json, TypeInfo)
                    : JsonSerializer.Deserialize<T>(json, Options);
            }
            catch
            {
                Save();
            }
    }

    public void Save()
    {
        if (!IsSave)
            return;

        if (Data == null) Data = new T();

        var jsresult = TypeInfo != null
            ? JsonSerializer.Serialize(Data, TypeInfo)
            : JsonSerializer.Serialize(Data, Options);
        File.WriteAllText(Path, jsresult);
    }
}
