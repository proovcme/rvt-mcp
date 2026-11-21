using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RvtMcp.Plugin;
using RvtMcp.Scripting;
using RvtMcp.Server.Bake;

namespace RvtMcp.Server
{
    internal static class SendCodeCore
    {
        internal static readonly string[] EnhancedArguments = { "data", "modules", "compile_only", "result_contract", "usage_context", "timeout_seconds", "request_id" };
        internal const int DefaultTimeoutSeconds = 60;
        internal static Func<BakeDb> StoreFactory = () => new BakeDb(new BakePaths());

        internal static void ValidateOriginalRequest(JsonNode parameters)
        {
            if (parameters == null) return;
            using var document = JsonDocument.Parse(parameters.ToJsonString(), new JsonDocumentOptions { MaxDepth = ScriptLimits.JsonDepth + 4 });
            void Visit(JsonElement value)
            {
                if (value.ValueKind == JsonValueKind.Object)
                {
                    var names = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var property in value.EnumerateObject())
                    {
                        if (!names.Add(property.Name))
                            throw new ScriptContractException(ScriptErrorCodes.InputInvalid, "Duplicate script request properties are not allowed.");
                        Visit(property.Value);
                    }
                }
                else if (value.ValueKind == JsonValueKind.Array)
                    foreach (var item in value.EnumerateArray()) Visit(item);
            }
            Visit(document.RootElement);
        }

        internal static ScriptInput ReadArguments(IDictionary<string, JsonElement> args)
        {
            string Text(string key, string fallback = null)
            {
                if (!args.TryGetValue(key, out var value)) return fallback;
                if (value.ValueKind != JsonValueKind.String)
                    throw new ScriptContractException(ScriptErrorCodes.InputInvalid, "Script argument requires a string: " + key);
                return value.GetString();
            }
            var data = args.TryGetValue("data", out var raw) ? ScriptData.FromJson(raw.GetRawText()) : ScriptData.Absent;
            var roots = Array.Empty<string>();
            if (args.TryGetValue("modules", out var modules) && modules.ValueKind != JsonValueKind.Null)
            {
                if (modules.ValueKind != JsonValueKind.Array || modules.EnumerateArray().Any(m => m.ValueKind != JsonValueKind.String))
                    throw new ScriptContractException(ScriptErrorCodes.InputInvalid, "Modules must be a string array.");
                roots = modules.EnumerateArray().Select(m => m.GetString()).ToArray();
            }
            var compileOnly = false;
            if (args.TryGetValue("compile_only", out var compile))
            {
                if (compile.ValueKind != JsonValueKind.True && compile.ValueKind != JsonValueKind.False)
                    throw new ScriptContractException(ScriptErrorCodes.InputInvalid, "compile_only must be a boolean.");
                compileOnly = compile.GetBoolean();
            }
            return new ScriptInput(Text("code"), data, roots, compileOnly, Text("result_contract", ScriptModes.Legacy), Text("usage_context", ScriptModes.Unknown));
        }

        internal static int? ReadTimeoutSeconds(IDictionary<string, JsonElement> args)
        {
            if (!args.TryGetValue("timeout_seconds", out var value) || value.ValueKind == JsonValueKind.Null) return null;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int seconds)) return seconds;
            throw new ScriptContractException(ScriptErrorCodes.InputInvalid, "timeout_seconds must be an integer number of seconds.");
        }

        internal static string ReadRequestId(IDictionary<string, JsonElement> args)
        {
            if (!args.TryGetValue("request_id", out var value) || value.ValueKind == JsonValueKind.Null) return null;
            if (value.ValueKind == JsonValueKind.String) return value.GetString();
            throw new ScriptContractException(ScriptErrorCodes.InputInvalid, "request_id must be a UUID string in 32-hex form.");
        }

        internal static async Task<string> SendAsync(ScriptInput input, IReadOnlyList<ScriptModuleRevision> modules = null, int? timeoutSeconds = null,
            string requestId = null)
        {
            if (timeoutSeconds is int seconds && ToolGateway.ValidateTimeoutSeconds(seconds, DefaultTimeoutSeconds) is string invalid)
                return invalid;
            Guid? scriptRequestId = null;
            if (requestId != null)
            {
                if (requestId.Length != 32 || !Guid.TryParseExact(requestId, "N", out var parsed) || parsed == Guid.Empty)
                    return "Error: " + ScriptErrorCodes.InputInvalid + ": request_id must be a nonzero UUID in 32-hex form.";
                scriptRequestId = parsed;
            }
            if (!input.RequiresCapability) return await ToolGateway.CallAsync("send_code_to_revit", new { code = input.Code }, timeoutSeconds, scriptRequestId);
            try
            {
                if (modules == null && input.Modules.Count != 0)
                {
                    using var db = StoreFactory();
                    modules = new CodeModuleStore(db).Resolve(input.Modules).Modules;
                }
                var result = await SendResolvedAsync(input, modules, metadata: null, timeoutSeconds, scriptRequestId);
                return result.ToString(Formatting.Indented);
            }
            catch (ScriptContractException ex) { return "Error: " + ex.ErrorCode + ": " + ex.Message; }
            catch (Exception ex) { return "Error: " + ex.Message; }
        }

        internal static Task<JObject> SendResolvedAsync(ScriptInput input, IReadOnlyList<ScriptModuleRevision> modules, ScriptRuntimeMetadata metadata = null)
            => SendResolvedAsync(input, modules, metadata, timeoutSeconds: null, scriptRequestId: null);

        private static Task<JObject> SendResolvedAsync(ScriptInput input, IReadOnlyList<ScriptModuleRevision> modules, ScriptRuntimeMetadata metadata,
            int? timeoutSeconds, Guid? scriptRequestId)
        {
            var request = new ScriptWireRequest(input, modules);
            return ToolGateway.SendToRevit("send_code_to_revit", request.Parameters(), timeoutSeconds,
                script: metadata ?? request.Metadata(), scriptRequestId: scriptRequestId);
        }

        internal static string SafeLogArguments(ScriptInput input)
            => new JObject {
                ["code_hash"] = BakeRedactor.HashBody(input.Code), ["code_length"] = input.Code.Length,
                ["script_code_sha256"] = ScriptIdentity.CodeSha256(input.Code), ["data_hash"] = input.Data.Hash,
                ["data_bytes"] = input.Data.ByteCount, ["modules"] = new JArray(input.Modules),
                ["result_contract"] = input.ResultContract, ["usage_context"] = input.UsageContext
            }.ToString(Formatting.None);
    }
}
