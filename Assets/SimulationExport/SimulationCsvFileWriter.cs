using System;
using System.IO;
using System.Security;
using ProjectNST.GameCore.Sessions;

namespace ProjectNST.SimulationExport
{
    /// <summary>Thin filesystem adapter. CSV generation remains in the Unity-independent GameCore exporter.</summary>
    public static class SimulationCsvFileWriter
    {
        public static SimulationFileWriteResult Write(string path, SimulationExportResult export)
        {
            if (export == null) return SimulationFileWriteResult.Failure("null_export", "Export result is required.");
            if (!export.Succeeded) return SimulationFileWriteResult.Failure(
                export.ErrorCode ?? "export_unavailable", export.ErrorMessage ?? "CSV export is unavailable.");
            if (string.IsNullOrWhiteSpace(path)) return SimulationFileWriteResult.Failure("invalid_path", "A destination file path is required.");

            try
            {
                File.WriteAllBytes(path, export.Utf8Bytes);
                return SimulationFileWriteResult.Success(path);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException ||
                exception is ArgumentException || exception is NotSupportedException || exception is SecurityException)
            {
                return SimulationFileWriteResult.Failure("file_write_failed", exception.Message);
            }
        }
    }

    public sealed class SimulationFileWriteResult
    {
        public bool Succeeded { get; }
        public string Path { get; }
        public string ErrorCode { get; }
        public string ErrorMessage { get; }

        private SimulationFileWriteResult(bool succeeded, string path, string errorCode, string errorMessage)
        { Succeeded = succeeded; Path = path; ErrorCode = errorCode; ErrorMessage = errorMessage; }

        internal static SimulationFileWriteResult Success(string path) => new SimulationFileWriteResult(true, path, null, null);
        internal static SimulationFileWriteResult Failure(string code, string message) => new SimulationFileWriteResult(false, null, code, message);
    }
}
