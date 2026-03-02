```csharp
// AuditEngineTests.cs
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace GenesysCloudExtensionAudit.Tests
{
    public sealed class AuditEngineTests
    {
        private static AuditEngine.UserProfileRecord User(
            string id,
            string? ext,
            string state = "active",
            string? name = null)
            => new AuditEngine.UserProfileRecord(id, name ?? $"User-{id}", state, ext);

        private static AuditEngine.ExtensionAssignmentRecord Assign(
            string id,
            string? ext,
            string? targetType = "user",
            string? targetId = null)
            => new AuditEngine.ExtensionAssignmentRecord(id, ext, targetType, targetId ?? $"T-{id}");

        [Fact]
        public void Run_ProfileDuplicates_CollideAfterNormalization_TrimCasePrefixSeparators()
        {
            var engine = new AuditEngine();

            var users = new[]
            {
                User("u1", "  ext. 1001 "),
                User("u2", "x1001"),
                User("u3", "EXTENSION 1001"),
            };

            var assignments = Array.Empty<AuditEngine.ExtensionAssignmentRecord>();

            var options = new AuditEngine.AuditEngineOptions
            {
                Normalization = new ExtensionNormalizationOptions
                {
                    DigitsOnly = true,
                    PreserveLeadingZeros = true,
                    StripExtensionPrefixes = true,
                    RemoveCommonSeparators = true
                }
            };

            var report = engine.Run(users, assignments, options);

            Assert.Single(report.DuplicateProfileExtensions);
            var dup = report.DuplicateProfileExtensions.Single();

            Assert.Equal("1001", dup.ExtensionKey);
            Assert.Equal(3, dup.Users.Count);
            Assert.True(dup.Users.Select(u => u.UserId).OrderBy(x => x)
                .SequenceEqual(new[] { "u1", "u2", "u3" }));
        }

        [Fact]
        public void Run_AssignedDuplicates_CollideAfterDigitsOnlyNormalization()
        {
            var engine = new AuditEngine();

            var users = Array.Empty<AuditEngine.UserProfileRecord>();

            var assignments = new[]
            {
                Assign("a1", "1002"),
                Assign("a2", "  1-0-0-2 "),
                Assign("a3", "ext 1002"),
            };

            var options = new AuditEngine.AuditEngineOptions
            {
                ComputeDuplicateAssignedExtensions = true,
                Normalization = new ExtensionNormalizationOptions
                {
                    DigitsOnly = true,
                    PreserveLeadingZeros = true,
                    StripExtensionPrefixes = true
                }
            };

            var report = engine.Run(users, assignments, options);

            Assert.Single(report.DuplicateAssignedExtensions);
            var dup = report.DuplicateAssignedExtensions.Single();

            Assert.Equal("1002", dup.ExtensionKey);
            Assert.Equal(3, dup.Assignments.Count);
        }

        [Fact]
        public void Run_NullEmptyWhitespace_ProfileExtensions_AreIgnored_NotInvalid()
        {
            var engine = new AuditEngine();

            var users = new[]
            {
                User("u1", null),
                User("u2", ""),
                User("u3", "   "),
            };

            var report = engine.Run(users, Array.Empty<AuditEngine.ExtensionAssignmentRecord>(),
                new AuditEngine.AuditEngineOptions
                {
                    Normalization = new ExtensionNormalizationOptions { DigitsOnly = true }
                });

            Assert.Empty(report.InvalidProfileExtensions);
            Assert.Empty(report.DuplicateProfileExtensions);
            Assert.Empty(report.ProfileExtensionsNotAssigned);
            Assert.Equal(3, report.TotalUsersConsidered);
        }

        [Fact]
        public void Run_InvalidProfileExtension_IsReported_WhenNonEmptyButInvalid()
        {
            var engine = new AuditEngine();

            var users = new[]
            {
                User("u1", "12#34"),   // invalid in AllowAlphanumeric=false mode (digits required)
            };

            var report = engine.Run(users, Array.Empty<AuditEngine.ExtensionAssignmentRecord>(),
                new AuditEngine.AuditEngineOptions
                {
                    Normalization = new ExtensionNormalizationOptions
                    {
                        DigitsOnly = false,
                        AllowAlphanumeric = false, // enforce digits-only without filtering
                        StripExtensionPrefixes = true,
                        RemoveCommonSeparators = true
                    }
                });

            Assert.Single(report.InvalidProfileExtensions);
            var inv = report.InvalidProfileExtensions.Single();

            Assert.Equal("u1", inv.UserId);
            Assert.Equal(ExtensionNormalizationStatus.InvalidFormat, inv.Status);
        }

        [Fact]
        public void Run_LeadingZerosPolicy_PreserveLeadingZeros_DoesNotCollide()
        {
            var engine = new AuditEngine();

            var users = new[]
            {
                User("u1", "0012"),
                User("u2", "12"),
            };

            var report = engine.Run(users, Array.Empty<AuditEngine.ExtensionAssignmentRecord>(),
                new AuditEngine.AuditEngineOptions
                {
                    Normalization = new ExtensionNormalizationOptions
                    {
                        DigitsOnly = true,
                        PreserveLeadingZeros = true
                    }
                });

            Assert.Empty(report.DuplicateProfileExtensions);
        }

        [Fact]
        public void Run_LeadingZerosPolicy_TrimLeadingZeros_Collides()
        {
            var engine = new AuditEngine();

            var users = new[]
            {
                User("u1", "0012"),
                User("u2", "12"),
            };

            var report = engine.Run(users, Array.Empty<AuditEngine.ExtensionAssignmentRecord>(),
                new AuditEngine.AuditEngineOptions
                {
                    Normalization = new ExtensionNormalizationOptions
                    {
                        DigitsOnly = true,
                        PreserveLeadingZeros = false
                    }
                });

            Assert.Single(report.DuplicateProfileExtensions);
            var dup = report.DuplicateProfileExtensions.Single();

            Assert.Equal("12", dup.ExtensionKey);
            Assert.Equal(2, dup.Users.Count);
        }

        [Fact]
        public void Run_IncludeInactiveUsers_False_ExcludesInactiveFromDuplicatesAndUnassigned()
        {
            var engine = new AuditEngine();

            var users = new[]
            {
                User("u1", "2001", state: "active"),
                User("u2", "2001", state: "inactive"),
            };

            var report = engine.Run(users, Array.Empty<AuditEngine.ExtensionAssignmentRecord>(),
                new AuditEngine.AuditEngineOptions
                {
                    IncludeInactiveUsers = false,
                    Normalization = new ExtensionNormalizationOptions { DigitsOnly = true }
                });

            // Only active user considered for duplicates => no duplicates
            Assert.Empty(report.DuplicateProfileExtensions);

            // Unassigned report contains extension 2001 with only the active user
            Assert.Single(report.ProfileExtensionsNotAssigned);
            var unassigned = report.ProfileExtensionsNotAssigned.Single();
            Assert.Equal("2001", unassigned.ExtensionKey);
            Assert.Single(unassigned.Users);
            Assert.Equal("u1", unassigned.Users.Single().UserId);

            Assert.Equal(1, report.TotalUsersConsidered);
        }

        [Fact]
        public void Run_IncludeInactiveUsers_True_IncludesInactiveInDuplicates()
        {
            var engine = new AuditEngine();

            var users = new[]
            {
                User("u1", "2002", state: "active"),
                User("u2", "2002", state: "inactive"),
            };

            var report = engine.Run(users, Array.Empty<AuditEngine.ExtensionAssignmentRecord>(),
                new AuditEngine.AuditEngineOptions
                {
                    IncludeInactiveUsers = true,
                    Normalization = new ExtensionNormalizationOptions { DigitsOnly = true }
                });

            Assert.Single(report.DuplicateProfileExtensions);
            var dup = report.DuplicateProfileExtensions.Single();
            Assert.Equal("2002", dup.ExtensionKey);
            Assert.Equal(2, dup.Users.Count);

            Assert.Equal(2, report.TotalUsersConsidered);
        }

        [Fact]
        public void Run_ProfileExtensionsNotAssigned_UsesNormalizedJoinKey()
        {
            var engine = new AuditEngine();

            var users = new[]
            {
                User("u1", "ext 3003"),
                User("u2", "3004"),
            };

            var assignments = new[]
            {
                Assign("a1", "3003"),
            };

            var report = engine.Run(users, assignments,
                new AuditEngine.AuditEngineOptions
                {
                    Normalization = new ExtensionNormalizationOptions
                    {
                        DigitsOnly = true,
                        StripExtensionPrefixes = true,
                        PreserveLeadingZeros = true
                    }
                });

            // 3003 is assigned (after normalization), so only 3004 should show as not assigned
            Assert.Single(report.ProfileExtensionsNotAssigned);
            Assert.Equal("3004", report.ProfileExtensionsNotAssigned.Single().ExtensionKey);
            Assert.Single(report.ProfileExtensionsNotAssigned.Single().Users);
            Assert.Equal("u2", report.ProfileExtensionsNotAssigned.Single().Users.Single().UserId);
        }
    }
}
```
