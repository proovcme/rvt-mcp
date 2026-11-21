using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Newtonsoft.Json.Linq;

namespace RvtMcp.Plugin.Handlers
{
    internal static class PipingRoleAudit
    {
        internal static JObject Read(IEnumerable<Pipe> pipes, Guid parameterId)
        {
            const int evidenceLimit = 100;
            var rows = pipes.OrderBy(p => RevitCompat.GetId(p.Id)).Select(p =>
            {
                var roleParameter = p.get_Parameter(parameterId);
                var expected = roleParameter?.StorageType == StorageType.String ? roleParameter.AsString()?.Trim() : null;
                var typeId = p.get_Parameter(BuiltInParameter.RBS_PIPING_SYSTEM_TYPE_PARAM)?.AsElementId();
                var type = typeId == null || typeId == ElementId.InvalidElementId ? null : p.Document.GetElement(typeId) as PipingSystemType;
                var actual = type?.Abbreviation?.Trim();
                var status = string.IsNullOrEmpty(expected) ? "missing_project_role"
                    : string.IsNullOrEmpty(actual) ? "missing_native_abbreviation"
                    : string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase) ? "matched" : "mismatch";
                return new JObject { ["element_id"] = RevitCompat.GetId(p.Id),
                    ["expected_role"] = expected, ["native_abbreviation"] = actual, ["status"] = status };
            }).ToArray();
            var findings = rows.Where(row => row.Value<string>("status") != "matched").ToArray();
            return new JObject
            {
                ["project_role_parameter_guid"] = parameterId.ToString("D"),
                ["pipe_count"] = rows.Length,
                ["matched_count"] = rows.Length - findings.Length,
                ["mismatch_count"] = findings.Count(r => r.Value<string>("status") == "mismatch"),
                ["unverified_count"] = findings.Count(r => r.Value<string>("status") != "mismatch"),
                ["findings_truncated"] = findings.Length > evidenceLimit,
                ["findings"] = new JArray(findings.Take(evidenceLimit))
            };
        }
    }
}
