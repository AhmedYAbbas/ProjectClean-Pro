using System.Collections.Generic;
using NUnit.Framework;
using ProjectCleanPro.Editor;

namespace ProjectCleanPro.Tests.Editor
{
    /// <summary>
    /// Tests for <see cref="PCPPackageAuditEntry"/>, <see cref="PCPPackageStatus"/>.
    /// </summary>
    [TestFixture]
    public sealed class PCPPackageAuditEntryTests
    {
        // ================================================================
        // 1. TOTAL REFERENCE COUNT
        // ================================================================

        [Test]
        public void TotalReferenceCount_SumsDirectAndCode()
        {
            var entry = new PCPPackageAuditEntry
            {
                directReferenceCount = 3,
                codeReferenceCount = 7
            };
            Assert.AreEqual(10, entry.TotalReferenceCount);
        }

        [Test]
        public void TotalReferenceCount_BothZero_ReturnsZero()
        {
            var entry = new PCPPackageAuditEntry();
            Assert.AreEqual(0, entry.TotalReferenceCount);
        }

        // ================================================================
        // 2. IS TRANSITIVE
        // ================================================================

        [Test]
        public void IsTransitive_TransitiveOnly_ReturnsTrue()
        {
            var entry = new PCPPackageAuditEntry { status = PCPPackageStatus.TransitiveOnly };
            Assert.IsTrue(entry.IsTransitive);
        }

        [Test]
        public void IsTransitive_Used_ReturnsFalse()
        {
            var entry = new PCPPackageAuditEntry { status = PCPPackageStatus.Used };
            Assert.IsFalse(entry.IsTransitive);
        }

        [Test]
        public void IsTransitive_Unused_ReturnsFalse()
        {
            var entry = new PCPPackageAuditEntry { status = PCPPackageStatus.Unused };
            Assert.IsFalse(entry.IsTransitive);
        }

        [Test]
        public void IsTransitive_Unknown_ReturnsFalse()
        {
            var entry = new PCPPackageAuditEntry { status = PCPPackageStatus.Unknown };
            Assert.IsFalse(entry.IsTransitive);
        }

        // ================================================================
        // 3. GET SUMMARY
        // ================================================================

        [Test]
        public void GetSummary_ContainsDisplayName()
        {
            var entry = MakeEntry("com.unity.textmeshpro", "TextMeshPro", "3.0.6", PCPPackageStatus.Used);
            Assert.IsTrue(entry.GetSummary().Contains("TextMeshPro"));
        }

        [Test]
        public void GetSummary_ContainsPackageName()
        {
            var entry = MakeEntry("com.unity.textmeshpro", "TextMeshPro", "3.0.6", PCPPackageStatus.Used);
            Assert.IsTrue(entry.GetSummary().Contains("com.unity.textmeshpro"));
        }

        [Test]
        public void GetSummary_ContainsVersion()
        {
            var entry = MakeEntry("com.unity.test", "Test", "2.1.0", PCPPackageStatus.Used);
            Assert.IsTrue(entry.GetSummary().Contains("2.1.0"));
        }

        [Test]
        public void GetSummary_ContainsStatus()
        {
            var entry = MakeEntry("com.unity.test", "Test", "1.0.0", PCPPackageStatus.Unused);
            Assert.IsTrue(entry.GetSummary().Contains("[Unused]"));
        }

        [Test]
        public void GetSummary_WithDependedOnBy_ShowsDependencies()
        {
            var entry = MakeEntry("com.unity.math", "Mathematics", "1.0.0", PCPPackageStatus.TransitiveOnly);
            entry.dependedOnBy.Add("com.unity.burst");
            entry.dependedOnBy.Add("com.unity.collections");

            string summary = entry.GetSummary();
            Assert.IsTrue(summary.Contains("depended on by"));
            Assert.IsTrue(summary.Contains("com.unity.burst"));
            Assert.IsTrue(summary.Contains("com.unity.collections"));
        }

        [Test]
        public void GetSummary_NoDependedOnBy_NoDependencyText()
        {
            var entry = MakeEntry("com.unity.test", "Test", "1.0.0", PCPPackageStatus.Used);
            Assert.IsFalse(entry.GetSummary().Contains("depended on by"));
        }

        [Test]
        public void GetSummary_ContainsTotalRefs()
        {
            var entry = MakeEntry("com.unity.test", "Test", "1.0.0", PCPPackageStatus.Used);
            entry.directReferenceCount = 5;
            entry.codeReferenceCount = 3;
            Assert.IsTrue(entry.GetSummary().Contains("refs: 8"));
        }

        // ================================================================
        // 4. TOSTRING
        // ================================================================

        [Test]
        public void ToString_EqualsGetSummary()
        {
            var entry = MakeEntry("com.unity.test", "Test", "1.0.0", PCPPackageStatus.Used);
            Assert.AreEqual(entry.GetSummary(), entry.ToString());
        }

        // ================================================================
        // 5. PACKAGE STATUS ENUM
        // ================================================================

        [Test]
        public void PackageStatus_AllValuesExist()
        {
            Assert.IsTrue(System.Enum.IsDefined(typeof(PCPPackageStatus), PCPPackageStatus.Used));
            Assert.IsTrue(System.Enum.IsDefined(typeof(PCPPackageStatus), PCPPackageStatus.Unused));
            Assert.IsTrue(System.Enum.IsDefined(typeof(PCPPackageStatus), PCPPackageStatus.TransitiveOnly));
            Assert.IsTrue(System.Enum.IsDefined(typeof(PCPPackageStatus), PCPPackageStatus.Unknown));
        }

        // ================================================================
        // Helpers
        // ================================================================

        private static PCPPackageAuditEntry MakeEntry(string name, string display, string version, PCPPackageStatus status)
        {
            return new PCPPackageAuditEntry
            {
                packageName = name,
                displayName = display,
                version = version,
                status = status,
                description = "Test package",
                dependedOnBy = new List<string>()
            };
        }
    }
}
