// ResearchTree.cs
// Copyright Karel Kroeze, 2020-2020

using System.Reflection;
using HarmonyLib;
//using Multiplayer.API;

namespace FluffyResearchTree
{
    using System.Linq;
    using Verse;

    public class ResearchTree : Mod
    {
        public ResearchTree( ModContentPack content ) : base( content )
        {
            var loadedMods = ModsConfig.ActiveModsInLoadOrder.ToList();

            if (loadedMods.Exists(m => m.PackageId == this.Content.PackageId && m.OnSteamWorkshop == true) )
            {
                Log.Error("This mod is illegaly uploaded to steam",true);
                return;
            }

            if (!ModsConfig.IsActive("Fluffy.ResearchTree"))
            {
                Log.Error("Fluffy's Research Tab isn't active", true);
                return;
            }

            var harmony = new Harmony( "Fluffy.ResearchTree.Tabbed" );

            if (ModsConfig.IsActive("Fluffy.ResearchTree"))
            {
                Log.Message( "Unpatching Fluffy's Research Tab");
                harmony.UnpatchAll("Fluffy.ResearchTree");
            }

            Log.Message( "Patching Tabbed Fluffy's Research Tab");
            harmony.PatchAll( Assembly.GetExecutingAssembly() );

        }
    }
}