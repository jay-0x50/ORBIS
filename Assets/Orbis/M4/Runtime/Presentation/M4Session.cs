using System.IO;
using Orbis.M1;
using UnityEngine;

namespace Orbis.M4
{
    public static class M4PrototypeSettings
    {
        // Unspecified design choices, exposed as one policy point. Recommended defaults were
        // announced while awaiting the user's preference; they are not claimed as confirmed.
        public const bool UseCoreExposure = true;
        public const int ResetHourKst = 4;
    }

    public static class M4Session
    {
        private static M4ProgressService progress;
        public static M4ProgressService Progress => progress ?? (progress = new M4ProgressService(
            Path.Combine(Application.persistentDataPath, "Orbis", "m4-progress.json"),
            resetHourKst: M4PrototypeSettings.ResetHourKst));
        public static bool HasTraveler { get; private set; }
        public static float Health { get; private set; } = 100f;
        public static float Stamina { get; private set; } = 100f;
        public static string ActiveMemberId { get; private set; }
        public static ElementType ActiveElement { get; private set; } = ElementType.Fire;

        public static void CaptureTraveler(float health, float stamina, ElementType activeElement, string activeMemberId = null)
        {
            HasTraveler = true; Health = health; Stamina = stamina; ActiveElement = activeElement;
            ActiveMemberId = activeMemberId;
        }

        /// <summary>Inject a temporary save/clock in tests; never alter a user's actual daily calendar.</summary>
        public static void UseProgressForTests(M4ProgressService service)
        {
            progress = service;
            HasTraveler = false; ActiveMemberId = null;
            Health = Stamina = 100f; ActiveElement = ElementType.Fire;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void BeginSession() => UseProgressForTests(null);
    }
}
