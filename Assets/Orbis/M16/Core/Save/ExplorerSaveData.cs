using System;
using Orbis.M1;

namespace Orbis.M16
{
    public enum ExplorerChoice { Unselected = 0, Stella = 1, Polaris = 2 }

    /// <summary>The story protagonist is separate from gacha ownership; element changes retain the same identity.</summary>
    [Serializable]
    public sealed class ExplorerSaveData
    {
        public ExplorerChoice choice;
        public ElementType element;

        public ExplorerSaveData Clone() => new ExplorerSaveData { choice = choice, element = element };

        public bool Validate(out string error)
        {
            if (choice == ExplorerChoice.Unselected)
            {
                if (element != ElementType.None)
                {
                    error = "An unselected explorer must not have an active element.";
                    return false;
                }
                error = null;
                return true;
            }
            if (choice != ExplorerChoice.Stella && choice != ExplorerChoice.Polaris)
            {
                error = "Unknown explorer choice.";
                return false;
            }
            switch (element)
            {
                case ElementType.Fire:
                case ElementType.Water:
                case ElementType.Wind:
                case ElementType.Rock:
                case ElementType.Lightning:
                    error = null;
                    return true;
                default:
                    error = "A selected explorer requires one of the five playable elements.";
                    return false;
            }
        }
    }
}
