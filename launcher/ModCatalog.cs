using System;
using System.Collections.Generic;
using System.Linq;

namespace RiftRevival.Launcher
{
    public sealed class ModDefinition
    {
        public string Id, Name, Summary, Description, Selector;
        public int Bit, Ticks;
        public string[] Effects;
        public double[] Values;
    }
    public static class ModCatalog
    {
        public static readonly ModDefinition[] All = {
            new ModDefinition { Id=RecipePatch.ModId, Name="Focusing Crystal", Summary="Tier III crystal recipe adjustment.", Description="Tier III focusing crystals require 50 Carbon and 50 Quartz.", Bit=1 },
            new ModDefinition { Id="solar-explorer-v1", Name="Solar Explorer", Summary="Battery capacity and charging upgrade.", Bit=2, Selector="UT_PowerBattery_Capacity", Ticks=12000,
                Effects=new[]{"UT_PowerBattery_Capacity","UT_PowerBattery_ChargeSpeed","UT_Engine_Thrust"}, Values=new[]{.25,.15,-.05}, Description="Battery capacity +25%\nBattery charge speed +15%\nEngine thrust -5%" },
            new ModDefinition { Id="thermal-engineer-v1", Name="Thermal Engineer", Summary="Heat management with an engine trade-off.", Bit=4, Selector="UT_Temperature_VentHeatBufferSize", Ticks=12000,
                Effects=new[]{"UT_Temperature_VentHeatBufferSize","UT_Temperature_ThermalExtractorMaxHeat","UT_Engine_Consumption"}, Values=new[]{.25,.15,.05}, Description="Vent heat buffer +25%\nThermal extractor heat limit +15%\nEngine consumption +5%" },
            new ModDefinition { Id="prospector-v1", Name="Prospector", Summary="Mining range and power efficiency.", Bit=8, Selector="UT_Extractor_MineDistance", Ticks=9600,
                Effects=new[]{"UT_Extractor_MineDistance","UT_Extractor_PowerUsage","UT_Extractor_ExtractionSpeed"}, Values=new[]{.25,-.15,-.05}, Description="Mining distance +25%\nExtractor power use -15%\nExtraction speed -5%" },
            new ModDefinition { Id="efficient-factory-v1", Name="Efficient Factory", Summary="Lower production power consumption.", Bit=16, Selector="UT_Refinery_PowerUsed", Ticks=12000,
                Effects=new[]{"UT_Refinery_PowerUsed","UT_Assembler_PowerUsed","UT_Refinery_RefineSpeed","UT_Assembler_AssembleSpeed"}, Values=new[]{-.20,-.20,-.05,-.05}, Description="Refinery power use -20%\nAssembler power use -20%\nRefining speed -5%\nAssembly speed -5%" },
            new ModDefinition { Id="independent-hauler-v1", Name="Independent Hauler", Summary="Efficient engines and stronger shields.", Bit=32, Selector="UT_Shields_MaxStrength", Ticks=18000,
                Effects=new[]{"UT_Engine_Consumption","UT_Shields_MaxStrength","UT_Turret_Damage"}, Values=new[]{-.15,.15,-.10}, Description="Engine consumption -15%\nMaximum shield strength +15%\nTurret damage -10%" },
            new ModDefinition { Id="power-glove-battery-v1", Name="Power Glove Battery", Summary="Equip a rechargeable battery in the power glove.", Bit=64,
                Effects=new string[0], Values=new double[0], Description="Dedicated glove battery slot\nSupports all four portable-battery tiers\nPassive emergency reserve" },
            new ModDefinition { Id="backpack-storage-v1", Name="Backpack Storage", Summary="Equip a backpack with its own storage grid.", Bit=128,
                Effects=new string[0], Values=new double[0], Description="Four backpack tiers with 4, 8, 12, or 16 slots\nHotbar overflow and saved contents\nPrinter recipes, shop prices, and admin creation" }
        };
        public static int Mask(IEnumerable<string> ids)
        {
            if(ids==null) throw new ArgumentException("Missing mod selection.");
            int mask=0;
            foreach(string id in ids) { ModDefinition mod=All.SingleOrDefault(m=>m.Id==id); if(mod==null || (mask & mod.Bit)!=0) throw new ArgumentException("Unknown or duplicate mod selection."); mask |= mod.Bit; }
            return mask;
        }
        public static List<string> Ids(int mask) { if(mask<0 || mask>255) throw new ArgumentException("Invalid mod selection."); return All.Where(m=>(mask&m.Bit)!=0).Select(m=>m.Id).ToList(); }
    }
}
