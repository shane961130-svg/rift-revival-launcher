using System;
using System.IO;
using System.Linq;
using Mono.Cecil;

namespace RiftRevival.Launcher
{
    internal static class GloveBatteryPatchTests
    {
        private static void Need(bool value, string message) { if (!value) throw new Exception(message); }

        public static int Main(string[] args)
        {
            try { return Run(args); }
            catch (Exception error) { Console.Error.WriteLine("FAIL: " + error); return 1; }
        }

        private static int Run(string[] args)
        {
            if (args.Length != 1) throw new Exception("Supply the supported baseline executable.");
            byte[] output;
            using (ModuleDefinition module = ModuleDefinition.ReadModule(args[0]))
            {
                GloveBatteryPatch.Apply(module);
                TypeDefinition glove = module.GetTypes().Single(t => t.FullName == "Game.ClientServer.Classes.Tools.ClSvPlayerTool_PowerGlove");
                Need(glove.Fields.Count(f => f.Name == "RiftRevivalBattery") == 1, "Dedicated battery field missing.");
                Need(glove.Fields.Single(f => f.Name == "RiftRevivalBattery").FieldType.FullName == "Game.ClientServer.Classes.Tools.ClSvPlayerTool_ToolPortableBattery", "Battery field has the wrong type.");
                Need(glove.Fields.Count(f => f.Name == "RiftRevivalReserve") == 1, "Emergency reserve field missing.");
                Need(glove.Methods.Count(m => m.Name == "RiftRevivalSyncBatteryEnergy") == 1, "Energy reconciliation method missing.");
                Need(glove.Methods.Count(m => m.Name == "RiftRevivalInstallBattery") == 1, "Battery install method missing.");
                Need(glove.Methods.Count(m => m.Name == "RiftRevivalRemoveBattery") == 1, "Battery remove method missing.");
                Need(((Mono.Cecil.Cil.Instruction[])glove.Methods.Single(m => m.Name == "Server_OnData").Body.Instructions.Single(i => i.OpCode == Mono.Cecil.Cil.OpCodes.Switch).Operand).Length == 16, "Install/remove/state commands missing.");
                Need(glove.Methods.Count(m => m.Name == "RiftRevivalSendBatteryState") == 1, "Server battery-state reply missing.");
                Need(glove.Methods.Count(m => m.Name == "RiftRevivalReceiveBatteryState") == 1, "Client battery-state receiver missing.");
                Need(glove.Methods.Count(m => m.Name == "RiftRevivalRefillAfterRespawn") == 1, "Respawn refill method missing.");
                Need(glove.Methods.Count(m => m.Name == "RiftRevivalDropBatteryOnDeath") == 1, "Death-drop method missing.");
                TypeDefinition player = module.GetTypes().Single(t => t.FullName == "Game.Server.Player");
                Need(player.Methods.Single(m => m.Name == "Kill").Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "RiftRevivalDropBatteryOnDeath"), "Player death does not route the equipped battery into the corpse tools.");
                TypeDefinition respawnClosure = module.GetTypes().Single(t => t.FullName == "Game.Server.Player/<>c__DisplayClass119_0");
                Need(respawnClosure.Methods.Single(m => m.Name == "<Respawn>b__0").Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "RiftRevivalRefillAfterRespawn"), "Completed respawn does not refill the glove battery.");
                Need(glove.Methods.Count(m => m.Name == "RiftRevivalGetGloveBatteryCapacity") == 1, "Glove-only tier capacities missing.");
                float[] capacities = glove.Methods.Single(m => m.Name == "RiftRevivalGetGloveBatteryCapacity").Body.Instructions.Where(i => i.OpCode == Mono.Cecil.Cil.OpCodes.Ldc_R4).Select(i => Convert.ToSingle(i.Operand)).ToArray();
                foreach (float capacity in new[] { 1000f, 2500f, 5000f, 10000f }) Need(capacities.Contains(capacity), "Missing approved glove capacity " + capacity + ".");
                string[] rowNames = glove.Methods.Single(m => m.Name == "RiftRevivalGetBatteryDisplayName").Body.Instructions.Where(i => i.OpCode == Mono.Cecil.Cil.OpCodes.Ldstr).Select(i => (string)i.Operand).ToArray();
                foreach (string name in new[] { "Battery Tier 0", "Battery Tier 1", "Battery Tier 2", "Battery Tier 3" }) Need(rowNames.Any(s => s.Contains(name)), "Missing battery row name " + name + ".");
                Need(rowNames.All(s => s.StartsWith("~-Cffffff", StringComparison.Ordinal)), "Battery rows do not use native icon formatting.");
                Need(glove.Fields.Any(f => f.Name == "RiftRevivalClientBatteryInstalled" && f.IsNotSerialized), "Client installed-state mirror missing.");
                Need(glove.Methods.Single(m => m.Name == "Client_OnActivate").Body.Instructions.Any(i => i.OpCode == Mono.Cecil.Cil.OpCodes.Ldc_I4 && Convert.ToInt32(i.Operand) == 15), "Opening G.R.I.P does not request saved battery state.");
                foreach (string name in new[] { "Server_OnUpdate", "TakePower" })
                    Need(glove.Methods.Single(m => m.Name == name).Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "RiftRevivalSyncBatteryEnergy"), name + " is not connected to battery reconciliation.");
                foreach (string name in new[] { "Client_OnUpdate", "ReceiveServerState" })
                    Need(!glove.Methods.Single(m => m.Name == name).Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "RiftRevivalSyncBatteryEnergy"), name + " must not overwrite the authoritative gauge state.");
                TypeDefinition screen = module.GetTypes().Single(t => t.FullName == "Game.Client.AgosStatePowerGlove");
                Need(!screen.Methods.Single(m => m.Name == "OnHandleButtonEvents").Body.Instructions.Any(i => i.OpCode == Mono.Cecil.Cil.OpCodes.Ldc_I4 && Convert.ToInt32(i.Operand) == 900), "Old device-manager battery button is still connected.");
                TypeDefinition equipment = module.GetTypes().Single(t => t.FullName == "Game.Client.AgosStateCharacterCustomizationMenu");
                Need(equipment.Fields.Any(f => f.Name == "RiftRevivalBatterySlots"), "Equipment-page battery slot mapping missing.");
                Need(equipment.Methods.Any(m => m.Name == "RiftRevivalFillBatteryRows"), "Equipment-page battery rows missing.");
                Need(equipment.Methods.Any(m => m.Name == "RiftRevivalHandleBatteryClick"), "Equipment-page battery action missing.");
                using (MemoryStream stream = new MemoryStream()) { module.Write(stream, new WriterParameters { Timestamp = 0 }); output = stream.ToArray(); }
            }
            using (MemoryStream stream = new MemoryStream(output, false))
            using (ModuleDefinition check = ModuleDefinition.ReadModule(stream))
                Need(check.GetTypes().Single(t => t.FullName == "Game.ClientServer.Classes.Tools.ClSvPlayerTool_PowerGlove").Fields.Any(f => f.Name == "RiftRevivalBattery"), "Written assembly did not retain the battery field.");
            Console.WriteLine("PASS: dedicated saved battery; approved glove-only tier scale; native tier icons and names; install/swap/remove commands; battery-first reconciliation hooks; passive emergency reserve; assembly write/read.");
            return 0;
        }
    }
}
