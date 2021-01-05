using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Neo.BuildTasks
{
    // Parse Manifest ABI JSON manually to avoid taking dependency on neo.dll
    class NeoManifest
    {
        public class Method
        {
            public string Name { get; set; } = "";
            public string ReturnType { get; set; } = "";
            public IReadOnlyList<(string Name, string Type)> Parameters { get; set; } = Array.Empty<(string Name, string Type)>();
        }

        public class Event
        {
            public string Name { get; set; } = "";
            public IReadOnlyList<(string Name, string Type)> Parameters { get; set; } = Array.Empty<(string Name, string Type)>();
        }

        public string Name { get; set; } = "";
        public IReadOnlyList<Method> Methods { get; set; } = Array.Empty<Method>();
        public IReadOnlyList<Event> Events { get; set; } = Array.Empty<Event>();

        public string GenerateContractInterface(string @namespace = "")
        {
            var contractName = Regex.Replace(Name, "^.*\\.", string.Empty);

            var builder = new IndentedStringBuilder();
            if (@namespace.Length > 0)
            {
                builder.AppendLine($"namespace {@namespace} {{");
                builder.IncrementIndent();
            }
            builder.AppendLine($"[NeoTestHarness.Contract(\"{Name}\")]");
            builder.AppendLine($"interface {contractName} {{");
            builder.IncrementIndent();
            for (int i = 0; i < Methods.Count; i++)
            {
                Method? method = Methods[i];
                if (method.Name.StartsWith("_")) continue;

                builder.Append($"{ToDotNetType(method.ReturnType)} {method.Name}(");
                builder.Append(string.Join(", ", method.Parameters.Select(p => $"{ToDotNetType(p.Type)} {p.Name}")));
                builder.AppendLine(");");
            }

            if (Events.Count > 0)
            {
                builder.AppendLine("interface Events {");
                builder.IncrementIndent();
                for (int i = 0; i < Events.Count; i++)
                {
                    Event? @event = Events[i];
                    builder.Append($"void {@event.Name}(");
                    builder.Append(string.Join(", ", @event.Parameters.Select(p => $"{ToDotNetType(p.Type)} {p.Name}")));
                    builder.AppendLine($");");
                }
                builder.DecrementIndent();
                builder.AppendLine("}");
            }

            builder.DecrementIndent();
            builder.AppendLine("}");

            if (@namespace.Length > 0)
            {
                builder.DecrementIndent();
                builder.AppendLine("}");
            }

            return builder.ToString();

            static string ToDotNetType(string paramType) => paramType switch
            {
                "Any" => "object?",
                "Array" => "object[]",
                "Boolean" => "bool",
                "ByteArray" => "byte[]",
                "Hash160" => "Neo.UInt160",
                "Hash256" => "Neo.UInt256",
                "Integer" => "System.Numerics.BigInteger",
                "PublicKey" => "Neo.Cryptography.ECC.ECPoint",
                "String" => "string",
                "Void" => "void",
                _ => throw new NotImplementedException(),
            };
        }

        public static NeoManifest Load(string manifestPath)
        {
            var manifestBytes = File.ReadAllBytes(manifestPath);
            using var manifestJson = JsonDocument.Parse(manifestBytes);
            return NeoManifest.FromManifestJson(manifestJson);
        }

        public static NeoManifest FromManifestJson(JsonDocument json)
        {
            var contractName = json.RootElement.GetProperty("name").GetString() ?? throw new Exception("invalid contract name");
            var abi = json.RootElement.GetProperty("abi");
            var methods = abi.GetProperty("methods").EnumerateArray().Select(MethodFromJson);
            var events = abi.GetProperty("events").EnumerateArray().Select(EventFromJson);

            return new NeoManifest
            {
                Name = contractName,
                Methods = methods.ToArray(),
                Events = events.ToArray()
            };

            static (string Name, string Type) ParamFromJson(JsonElement json)
            {
                var name = json.GetProperty("name").GetString() ?? throw new Exception("invalid parameter name");
                var type = json.GetProperty("type").GetString() ?? throw new Exception("invalid type name");
                return (name, type);
            }

            static Method MethodFromJson(JsonElement json)
            {
                var name = json.GetProperty("name").GetString() ?? throw new Exception("invalid method name");;
                var returnType = json.GetProperty("returntype").GetString() ?? throw new Exception("invalid method return type");;
                var @params = json.GetProperty("parameters").EnumerateArray().Select(ParamFromJson);
                return new Method
                {
                    Name = name,
                    ReturnType = returnType,
                    Parameters = @params.ToArray()
                };
            }

            static Event EventFromJson(JsonElement json)
            {
                var name = json.GetProperty("name").GetString() ?? throw new Exception("invalid event name");;
                var @params = json.GetProperty("parameters").EnumerateArray().Select(ParamFromJson);
                return new Event
                {
                    Name = name,
                    Parameters = @params.ToArray()
                };
            }
        }
    }
}