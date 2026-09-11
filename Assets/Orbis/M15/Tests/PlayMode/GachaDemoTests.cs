using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Orbis.M4;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Orbis.M15.Tests
{
    public sealed class GachaDemoTests
    {
        string directory;
        M4ProgressService progress;
        M15GachaDemo demo;
        bool cursorVisible;
        CursorLockMode cursorLock;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            cursorVisible = Cursor.visible; cursorLock = Cursor.lockState;
            directory = Path.Combine(Path.GetTempPath(), "Orbis-M15-Play-" + Guid.NewGuid().ToString("N"));
            progress = new M4ProgressService(Path.Combine(directory, "m4-progress.json"));
            M4Session.UseProgressForTests(progress);
            foreach (var existing in Object.FindObjectsByType<CurrencyManager>())
                Object.Destroy(existing.gameObject);
            yield return null;
            yield return SceneManager.LoadSceneAsync("Assets/Orbis/M15/Scenes/M15_GachaDemo.unity");
            demo = Object.FindAnyObjectByType<M15GachaDemo>();
            Assert.That(demo, Is.Not.Null);
            Assert.That(demo.Manager.SavePath, Does.StartWith(directory));
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            var empty = SceneManager.CreateScene("M15 test cleanup");
            var current = SceneManager.GetActiveScene();
            SceneManager.SetActiveScene(empty);
            if (current.IsValid() && current.isLoaded) yield return SceneManager.UnloadSceneAsync(current);
            foreach (var manager in Object.FindObjectsByType<CurrencyManager>())
                Object.Destroy(manager.gameObject);
            yield return null;
            M4Session.UseProgressForTests(null);
            Cursor.visible = cursorVisible; Cursor.lockState = cursorLock;
            // Only this test's GUID directory is removed.
            Assert.That(Path.GetFileName(directory), Does.StartWith("Orbis-M15-Play-"));
            Assert.That(Path.GetDirectoryName(directory), Is.EqualTo(Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar)));
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }

        [UnityTest]
        public IEnumerator DemoButtonsExchangeDrawReloadAndSkipWithoutLosingRewards()
        {
            Assert.That(demo.DebugAdd(CurrencyType.OrbitShard, 1600), Is.True);
            Assert.That(demo.ExchangeTickets(10), Is.True);
            Assert.That(demo.Manager.Balance(CurrencyType.OrbitShard), Is.Zero);
            Assert.That(demo.PullTen(), Is.True);
            Assert.That(demo.LastResults.Count, Is.EqualTo(10));
            Assert.That(demo.LastResults.Any(x => x.Rarity >= CharacterRarity.Four), Is.True);
            Assert.That(demo.PresentationRequestCount, Is.EqualTo(10));
            Assert.That(demo.Manager.Snapshot.owned.Sum(x => x.copies), Is.EqualTo(10));
            string saved = JsonUtility.ToJson(demo.Manager.Snapshot);
            Assert.That(demo.Reload(), Is.True);
            Assert.That(JsonUtility.ToJson(demo.Manager.Snapshot), Is.EqualTo(saved));
            Assert.That(demo.SelectBanner(1), Is.True);
            Assert.That(demo.DebugAdd(CurrencyType.StandardPledge, 1), Is.True);
            demo.SkipPresentation = true;
            Assert.That(demo.PullSingle(), Is.True);
            Assert.That(demo.LastResults.Count, Is.EqualTo(1));
            Assert.That(demo.PresentationRequestCount, Is.EqualTo(10));
            Assert.That(demo.Manager.Snapshot.pities.Length, Is.EqualTo(2));
            Assert.That(demo.Manager.Snapshot.owned.Sum(x => x.copies), Is.EqualTo(11));
            yield return null;
            // OnGUI executes for multiple frames too: tests catch layout/lifecycle exceptions.
            yield return null;
        }

        [UnityTest]
        public IEnumerator SingletonSurvivesDemoReloadAndDuplicateCannotResetInventory()
        {
            var manager = demo.Manager;
            Assert.That(manager.Add(CurrencyType.Lumen, 123), Is.True);
            var duplicate = new GameObject("duplicate economy").AddComponent<CurrencyManager>();
            yield return null;
            Assert.That(duplicate == null, Is.True);
            Assert.That(CurrencyManager.Instance, Is.SameAs(manager));
            yield return SceneManager.LoadSceneAsync("Assets/Orbis/M15/Scenes/M15_GachaDemo.unity");
            demo = Object.FindAnyObjectByType<M15GachaDemo>();
            Assert.That(demo.Manager, Is.SameAs(manager));
            Assert.That(demo.Manager.Balance(CurrencyType.Lumen), Is.EqualTo(123));
            Assert.That(progress.RecordBossDefeated(M4RegionId.Agnia), Is.True);
            Assert.That(demo.Manager.Balance(CurrencyType.Lumen), Is.EqualTo(173));
            Assert.That(Object.FindObjectsByType<CurrencyManager>().Length, Is.EqualTo(1));
        }
    }
}

