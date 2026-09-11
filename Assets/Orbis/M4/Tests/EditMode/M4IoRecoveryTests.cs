using System;
using System.IO;
using NUnit.Framework;

namespace Orbis.M4.Tests
{
    public sealed class M4IoRecoveryTests
    {
        [Test]
        public void LockedLatestMainCannotFallBackToOlderBackupAndOverwriteProgress()
        {
            string directory = Path.Combine(Path.GetTempPath(), "Orbis-M4-Io-Tests-" + Guid.NewGuid().ToString("N"));
            string path = Path.Combine(directory, "progress.json");
            var now = new DateTime(2026, 9, 11, 3, 0, 0, DateTimeKind.Utc);
            try
            {
                var current = new M4ProgressService(path, () => now);
                Assert.That(current.RecordBossDefeated(M4RegionId.Agnia), Is.True);
                Assert.That(current.RecordBossDefeated(M4RegionId.Teluna), Is.True);
                Assert.That(current.RecordBossDefeated(M4RegionId.Zephyr), Is.True);
                Assert.That(current.Coins, Is.EqualTo(150));
                byte[] mainBytes = File.ReadAllBytes(path);
                byte[] backupBytes = File.ReadAllBytes(path + ".bak");
                // Each kill saves 50 coins; the verified backup contains the previous 100-coin revision.
                var old = new M4ProgressService(path + ".bak", () => now);
                Assert.That(old.Coins, Is.EqualTo(100));

                M4ProgressService blocked;
                using (var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    blocked = new M4ProgressService(path, () => now);
                    Assert.That(blocked.LoadStatus, Is.EqualTo(M4LoadStatus.IoErrorReadOnly));
                    Assert.That(blocked.IsReadOnly, Is.True);
                    Assert.That(blocked.LoadMessage, Is.Not.Empty);
                    Assert.That(blocked.Save(), Is.False);
                    Assert.That(blocked.RecordBossDefeated(M4RegionId.Granite), Is.False);
                    Assert.That(current.Reload(), Is.False);
                    Assert.That(current.LoadStatus, Is.EqualTo(M4LoadStatus.IoErrorReadOnly));
                    Assert.That(current.Coins, Is.EqualTo(150), "An unsuccessful reload keeps the in-memory snapshot.");
                }

                Assert.That(File.ReadAllBytes(path), Is.EqualTo(mainBytes));
                Assert.That(File.ReadAllBytes(path + ".bak"), Is.EqualTo(backupBytes));
                Assert.That(blocked.Save(), Is.False, "Unlocking the file alone cannot authorize a stale save.");
                Assert.That(blocked.Reload(), Is.True);
                Assert.That(blocked.LoadStatus, Is.EqualTo(M4LoadStatus.Loaded));
                Assert.That(blocked.Coins, Is.EqualTo(150));
                Assert.That(blocked.IsBossDefeated(M4RegionId.Zephyr), Is.True);
                Assert.That(blocked.RecordBossDefeated(M4RegionId.Granite), Is.True);
                Assert.That(blocked.Coins, Is.EqualTo(200));
                Assert.That(new M4ProgressService(path, () => now).Coins, Is.EqualTo(200));
                Assert.That(Directory.GetFiles(directory, "*.corrupt-*"), Is.Empty,
                    "A temporary lock must never quarantine a valid current save as corruption.");
            }
            finally
            {
                string resolved = Path.GetFullPath(directory);
                Assert.That(Path.GetDirectoryName(resolved),
                    Is.EqualTo(Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));
                Assert.That(Path.GetFileName(resolved), Does.StartWith("Orbis-M4-Io-Tests-"));
                if (Directory.Exists(resolved)) Directory.Delete(resolved, true);
            }
        }
    }
}
