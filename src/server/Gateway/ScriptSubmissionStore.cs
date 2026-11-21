using System;
using System.IO;
using Newtonsoft.Json.Linq;
using RvtMcp.Plugin;

namespace RvtMcp.Server
{
    internal static class ScriptSubmissionStore
    {
        // A reservation precedes any possible wire write. Keep it even when delivery cannot be confirmed.
        internal static bool TryReserve(string operationId) =>
            !WasNotStarted(operationId) && TryWriteMarker(MarkerPath(operationId, ".claim"));

        internal static bool IsReserved(string operationId) => HasMarker(operationId, ".claim");

        internal static bool WasNotStarted(string operationId) => HasMarker(operationId, ".not-started");

        internal static void RecordNotStarted(string operationId, JObject receipt)
        {
            if (receipt?.Value<string>("operationId") != operationId || receipt.Value<string>("execution") != "completed"
                || receipt.Value<string>("outcome") != "not_started" || receipt.Value<bool?>("complete") != true
                || receipt.Value<bool?>("mutationApplied") != false) return;

            // Store only the proven outcome. A separate marker leaves the exclusive submission claim intact.
            try { TryWriteMarker(MarkerPath(operationId, ".not-started")); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                DiagnosticLog.Write("ScriptSubmissionStore", "Could not persist the not_started receipt for " + operationId);
            }
        }

        private static bool TryWriteMarker(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            FileStream claim;
            try
            {
                claim = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            }
            catch (IOException) when (File.Exists(path))
            {
                return false;
            }

            using (claim)
            {
                claim.WriteByte(1);
                claim.Flush(flushToDisk: true);
            }
            return true;
        }

        private static bool HasMarker(string operationId, string suffix) =>
            Guid.TryParseExact(operationId, "N", out var guid) && File.Exists(MarkerPath(guid.ToString("N"), suffix));

        private static string MarkerPath(string operationId, string suffix) =>
            Path.Combine(AuthToken.DiscoveryDir(), "script-submissions", operationId + suffix);
    }
}
