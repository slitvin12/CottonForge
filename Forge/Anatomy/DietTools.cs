using Il2Cpp;

namespace CottonLibrary.Forge
{
    /// <summary>Diet queries for behaviours (what a slime would eat, what it produces).</summary>
    public static class DietTools
    {
        /// <summary>True when <paramref name="def"/>'s diet contains <paramref name="food"/> (food groups, extra foods, favourites).</summary>
        public static bool Eats(SlimeDefinition def, IdentifiableType food)
        {
            var d = def?.Diet;
            if (d == null || food == null) return false;
            if (d.MajorFoodIdentifiableTypeGroups != null)
                foreach (var g in d.MajorFoodIdentifiableTypeGroups) if (g != null && Idents.InGroup(g, food)) return true;
            if (d.AdditionalFoodIdents != null) foreach (var t in d.AdditionalFoodIdents) if (t != null && t.Pointer == food.Pointer) return true;
            if (d.FavoriteIdents != null) foreach (var t in d.FavoriteIdents) if (t != null && t.Pointer == food.Pointer) return true;
            return false;
        }

        /// <summary>First produce (usually the plort) of the slime, or null.</summary>
        public static IdentifiableType Produce(SlimeDefinition def)
        {
            var p = def?.Diet?.ProduceIdents;
            return p != null && p.Length > 0 ? p[0] : null;
        }

        /// <summary>True for slime definitions (largos included).</summary>
        public static bool IsSlime(IdentifiableType t) => t != null && t.TryCast<SlimeDefinition>() != null;

        /// <summary>True for chickens and other meat.</summary>
        public static bool IsMeat(IdentifiableType t) => t != null && Library.meat != null && Idents.InGroup(Library.meat, t);

        /// <summary>True for plorts (name ends with "Plort").</summary>
        public static bool IsPlort(IdentifiableType t) => t != null && t.name != null && t.name.EndsWith("Plort", System.StringComparison.Ordinal);
    }
}
