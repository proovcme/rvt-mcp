using System;
using System.IO;

namespace RvtMcp.Server
{
    internal static class ScriptSubmissionStore
    {
        // A reservation precedes any possible wire write. Keep it even when delivery cannot be confirmed.
        internal static bool TryReserve(string operationId)
        {
            var path = ClaimPath(operationId);
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

        internal static bool IsReserved(string operationId) =>
            Guid.TryParseExact(operationId, "N", out var guid) && File.Exists(ClaimPath(guid.ToString("N")));

        private static string ClaimPath(string operationId) =>
            Path.Combine(AuthToken.DiscoveryDir(), "script-submissions", operationId + ".claim");
    }
}
