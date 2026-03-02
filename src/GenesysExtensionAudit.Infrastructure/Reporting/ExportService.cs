// File: ExportService.cs
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using GenesysExtensionAudit.Domain.Services;

namespace GenesysExtensionAudit.Infrastructure.Reporting
{
    /// <summary>
    /// Exports audit findings to Excel-friendly CSV files.
    /// Writes one CSV per report section + a summary CSV.
    /// </summary>
    public sealed class ExportService
    {
        public sealed class ExportOptions
        {
            /// <summary>
            /// Output directory for all CSVs.
            /// Directory will be created if it does not exist.
            /// </summary>
            public string OutputDirectory { get; init; } = Environment.CurrentDirectory;

            /// <summary>
            /// Optional file name prefix applied to all output files.
            /// If null/empty, a timestamp-based prefix is used.
            /// </summary>
            public string? FilePrefix { get; init; }

            /// <summary>
            /// If true, overwrite existing files with the same name.
            /// If false, throws IOException on collisions.
            /// </summary>
            public bool Overwrite { get; init; } = true;

            /// <summary>
            /// If true, write UTF-8 BOM (recommended for Excel).
            /// </summary>
            public bool IncludeUtf8Bom { get; init; } = true;
        }

        public sealed class ExportResult
        {
            public string OutputDirectory { get; init; } = "";
            public string FilePrefix { get; init; } = "";
            public IReadOnlyDictionary<string, string> FilesByReport { get; init; }
                = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Export all report sections to CSV files.
        /// </summary>
        public ExportResult ExportAll(AuditEngine.AuditReport report, ExportOptions? options = null)
        {
            if (report is null) throw new ArgumentNullException(nameof(report));
            options ??= new ExportOptions();

            Directory.CreateDirectory(options.OutputDirectory);

            var prefix = string.IsNullOrWhiteSpace(options.FilePrefix)
                ? DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture)
                : SanitizeFileComponent(options.FilePrefix!.Trim());

            if (string.IsNullOrWhiteSpace(prefix))
                prefix = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);

            var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // Summary (always)
            files["Summary"] = WriteSummaryCsv(report, options, prefix);

            // Findings (write when present; still create file if section exists but empty? keep consistent: write file always)
            files["DuplicateProfileExtensions"] = WriteDuplicateProfileExtensionsCsv(report, options, prefix);
            files["ProfileExtensionsNotAssigned"] = WriteProfileExtensionsNotAssignedCsv(report, options, prefix);
            files["DuplicateAssignedExtensions"] = WriteDuplicateAssignedExtensionsCsv(report, options, prefix);
            files["AssignedExtensionsMissingFromProfiles"] = WriteAssignedExtensionsMissingFromProfilesCsv(report, options, prefix);
            files["InvalidProfileExtensions"] = WriteInvalidProfileExtensionsCsv(report, options, prefix);
            files["InvalidAssignedExtensions"] = WriteInvalidAssignedExtensionsCsv(report, options, prefix);

            return new ExportResult
            {
                OutputDirectory = options.OutputDirectory,
                FilePrefix = prefix,
                FilesByReport = files
            };
        }

        // -------------------------
        // Writers
        // -------------------------

        private string WriteSummaryCsv(AuditEngine.AuditReport report, ExportOptions options, string prefix)
        {
            var path = BuildPath(options.OutputDirectory, prefix, "Summary.csv");

            var rows = new List<(string Key, string Value)>
            {
                ("GeneratedAtLocal", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)),
                ("TotalUsersConsidered", report.TotalUsersConsidered.ToString(CultureInfo.InvariantCulture)),
                ("TotalAssignmentsConsidered", report.TotalAssignmentsConsidered.ToString(CultureInfo.InvariantCulture)),

                ("DuplicateProfileExtensions_Count", report.DuplicateProfileExtensions.Count.ToString(CultureInfo.InvariantCulture)),
                ("DuplicateProfileExtensions_TotalUsersInFindings", report.DuplicateProfileExtensions.Sum(f => f.Users?.Count ?? 0).ToString(CultureInfo.InvariantCulture)),

                ("ProfileExtensionsNotAssigned_Count", report.ProfileExtensionsNotAssigned.Count.ToString(CultureInfo.InvariantCulture)),
                ("ProfileExtensionsNotAssigned_TotalUsersInFindings", report.ProfileExtensionsNotAssigned.Sum(f => f.Users?.Count ?? 0).ToString(CultureInfo.InvariantCulture)),

                ("DuplicateAssignedExtensions_Count", report.DuplicateAssignedExtensions.Count.ToString(CultureInfo.InvariantCulture)),
                ("DuplicateAssignedExtensions_TotalAssignmentsInFindings", report.DuplicateAssignedExtensions.Sum(f => f.Assignments?.Count ?? 0).ToString(CultureInfo.InvariantCulture)),

                ("AssignedExtensionsMissingFromProfiles_Count", report.AssignedExtensionsMissingFromProfiles.Count.ToString(CultureInfo.InvariantCulture)),
                ("AssignedExtensionsMissingFromProfiles_TotalAssignmentsInFindings", report.AssignedExtensionsMissingFromProfiles.Sum(f => f.Assignments?.Count ?? 0).ToString(CultureInfo.InvariantCulture)),

                ("InvalidProfileExtensions_Count", report.InvalidProfileExtensions.Count.ToString(CultureInfo.InvariantCulture)),
                ("InvalidAssignedExtensions_Count", report.InvalidAssignedExtensions.Count.ToString(CultureInfo.InvariantCulture)),
            };

            WriteCsv(
                path,
                options,
                header: new[] { "Key", "Value" },
                rows: rows.Select(r => new[] { r.Key, r.Value })
            );

            return path;
        }

        private string WriteDuplicateProfileExtensionsCsv(AuditEngine.AuditReport report, ExportOptions options, string prefix)
        {
            var path = BuildPath(options.OutputDirectory, prefix, "DuplicateProfileExtensions.csv");

            var header = new[]
            {
                "ExtensionKey",
                "UserName",
                "UserId",
                "State",
                "ExtensionRaw"
            };

            var rows =
                report.DuplicateProfileExtensions
                    .SelectMany(f => (f.Users ?? Array.Empty<AuditEngine.ProfileExtensionDetail>())
                        .Select(u => new[]
                        {
                            f.ExtensionKey ?? "",
                            u.UserName ?? "",
                            u.UserId ?? "",
                            u.State ?? "",
                            u.ExtensionRaw ?? ""
                        }));

            WriteCsv(path, options, header, rows);
            return path;
        }

        private string WriteProfileExtensionsNotAssignedCsv(AuditEngine.AuditReport report, ExportOptions options, string prefix)
        {
            var path = BuildPath(options.OutputDirectory, prefix, "ProfileExtensionsNotAssigned.csv");

            var header = new[]
            {
                "ExtensionKey",
                "UserName",
                "UserId",
                "State",
                "ExtensionRaw"
            };

            var rows =
                report.ProfileExtensionsNotAssigned
                    .SelectMany(f => (f.Users ?? Array.Empty<AuditEngine.ProfileExtensionDetail>())
                        .Select(u => new[]
                        {
                            f.ExtensionKey ?? "",
                            u.UserName ?? "",
                            u.UserId ?? "",
                            u.State ?? "",
                            u.ExtensionRaw ?? ""
                        }));

            WriteCsv(path, options, header, rows);
            return path;
        }

        private string WriteDuplicateAssignedExtensionsCsv(AuditEngine.AuditReport report, ExportOptions options, string prefix)
        {
            var path = BuildPath(options.OutputDirectory, prefix, "DuplicateAssignedExtensions.csv");

            var header = new[]
            {
                "ExtensionKey",
                "AssignmentId",
                "ExtensionRaw",
                "TargetType",
                "TargetId"
            };

            var rows =
                report.DuplicateAssignedExtensions
                    .SelectMany(f => (f.Assignments ?? Array.Empty<AuditEngine.AssignmentExtensionDetail>())
                        .Select(a => new[]
                        {
                            f.ExtensionKey ?? "",
                            a.AssignmentId ?? "",
                            a.ExtensionRaw ?? "",
                            a.TargetType ?? "",
                            a.TargetId ?? ""
                        }));

            WriteCsv(path, options, header, rows);
            return path;
        }

        private string WriteAssignedExtensionsMissingFromProfilesCsv(AuditEngine.AuditReport report, ExportOptions options, string prefix)
        {
            var path = BuildPath(options.OutputDirectory, prefix, "AssignedExtensionsMissingFromProfiles.csv");

            var header = new[]
            {
                "ExtensionKey",
                "AssignmentId",
                "ExtensionRaw",
                "TargetType",
                "TargetId"
            };

            var rows =
                report.AssignedExtensionsMissingFromProfiles
                    .SelectMany(f => (f.Assignments ?? Array.Empty<AuditEngine.AssignmentExtensionDetail>())
                        .Select(a => new[]
                        {
                            f.ExtensionKey ?? "",
                            a.AssignmentId ?? "",
                            a.ExtensionRaw ?? "",
                            a.TargetType ?? "",
                            a.TargetId ?? ""
                        }));

            WriteCsv(path, options, header, rows);
            return path;
        }

        private string WriteInvalidProfileExtensionsCsv(AuditEngine.AuditReport report, ExportOptions options, string prefix)
        {
            var path = BuildPath(options.OutputDirectory, prefix, "InvalidProfileExtensions.csv");

            var header = new[]
            {
                "UserName",
                "UserId",
                "State",
                "ExtensionRaw",
                "Status",
                "Notes"
            };

            var rows =
                report.InvalidProfileExtensions.Select(i => new[]
                {
                    i.UserName ?? "",
                    i.UserId ?? "",
                    i.State ?? "",
                    i.ExtensionRaw ?? "",
                    i.Status.ToString(),
                    i.Notes ?? ""
                });

            WriteCsv(path, options, header, rows);
            return path;
        }

        private string WriteInvalidAssignedExtensionsCsv(AuditEngine.AuditReport report, ExportOptions options, string prefix)
        {
            var path = BuildPath(options.OutputDirectory, prefix, "InvalidAssignedExtensions.csv");

            var header = new[]
            {
                "AssignmentId",
                "ExtensionRaw",
                "Status",
                "Notes"
            };

            var rows =
                report.InvalidAssignedExtensions.Select(i => new[]
                {
                    i.AssignmentId ?? "",
                    i.ExtensionRaw ?? "",
                    i.Status.ToString(),
                    i.Notes ?? ""
                });

            WriteCsv(path, options, header, rows);
            return path;
        }

        // -------------------------
        // CSV helpers
        // -------------------------

        private static void WriteCsv(
            string path,
            ExportOptions options,
            IEnumerable<string> header,
            IEnumerable<string[]> rows)
        {
            var encoding = options.IncludeUtf8Bom ? new UTF8Encoding(encoderShouldEmitUTF8Identifier: true) : new UTF8Encoding(false);

            var fileMode = options.Overwrite ? FileMode.Create : FileMode.CreateNew;
            using var fs = new FileStream(path, fileMode, FileAccess.Write, FileShare.Read);
            using var writer = new StreamWriter(fs, encoding);

            writer.WriteLine(string.Join(",", header.Select(EscapeCsv)));

            foreach (var row in rows ?? Enumerable.Empty<string[]>())
            {
                var safe = row ?? Array.Empty<string>();
                writer.WriteLine(string.Join(",", safe.Select(EscapeCsv)));
            }
        }

        private static string EscapeCsv(string? value)
        {
            value ??= string.Empty;

            // Excel-friendly: quote fields containing comma, quote, CR, LF, or leading/trailing whitespace.
            var mustQuote =
                value.Contains(',') ||
                value.Contains('"') ||
                value.Contains('\r') ||
                value.Contains('\n') ||
                (value.Length > 0 && (char.IsWhiteSpace(value[0]) || char.IsWhiteSpace(value[^1])));

            if (!mustQuote) return value;

            var escaped = value.Replace("\"", "\"\"");
            return "\"" + escaped + "\"";
        }

        private static string BuildPath(string outputDirectory, string prefix, string fileName)
        {
            var safePrefix = SanitizeFileComponent(prefix);
            var safeName = SanitizeFileComponent(fileName);

            // ensure file extension remains .csv if it was provided
            if (!safeName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                safeName += ".csv";

            var fullName = $"{safePrefix}_{safeName}";
            return Path.Combine(outputDirectory, fullName);
        }

        private static string SanitizeFileComponent(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return string.Empty;

            var invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(input.Length);
            foreach (var ch in input)
            {
                if (invalid.Contains(ch))
                    sb.Append('_');
                else
                    sb.Append(ch);
            }

            // Avoid trailing dots/spaces (Windows)
            var s = sb.ToString().Trim().TrimEnd('.');
            return s;
        }
    }
}
