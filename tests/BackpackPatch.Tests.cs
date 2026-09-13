using System;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace RiftRevival.Launcher
{
    internal static class BackpackPatchTests
    {
        private static void Need(bool value, string message) { if (!value) throw new Exception(message); }
        public static int Main(string[] args)
        {
            try
            {
                byte[] output;
                using (ModuleDefinition module = ModuleDefinition.ReadModule(args[0]))
                {
                    BackpackPatch.Apply(module);
                    TypeDefinition personal = module.GetTypes().Single(t => t.FullName == "Game.ClientServer.Classes.ClSvPlayerState_Personal");
                    TypeDefinition equipmentTool = module.GetTypes().Single(t => t.FullName == "Game.ClientServer.Classes.Tools.ClSvPlayerTool_PlayerEquipment");
                    Need(personal.Fields.Count(f => f.Name == "RiftRevivalBackpack") == 1, "Saved equipped-backpack field missing.");
                    Need(equipmentTool.Fields.Count(f => f.Name == "RiftRevivalBackpackTier") == 1, "Backpack-owned tier missing.");
                    Need(equipmentTool.Fields.Count(f => f.Name == "RiftRevivalBackpackInventory") == 1, "Backpack-owned inventory missing.");
                    Need(equipmentTool.Fields.Single(f => f.Name == "RiftRevivalBackpackInventory").FieldType.FullName == personal.Fields.Single(f => f.Name == "ToolInventory").FieldType.FullName, "Backpack does not store real tool objects.");
                    Need(equipmentTool.Fields.Single(f => f.Name == "RiftRevivalBackpackResources").FieldType.FullName == personal.Fields.Single(f => f.Name == "ResourceInventory").FieldType.FullName, "Backpack does not preserve real resource crates.");
                    Need(equipmentTool.Methods.Single(m => m.Name == "RiftRevivalGetBackpackItemCount").Body.Instructions.Any(i => i.Operand is FieldReference && ((FieldReference)i.Operand).Name == "RiftRevivalBackpackResources"), "Backpack capacity does not count stored resource crates.");
                    MethodDefinition capacity = equipmentTool.Methods.Single(m => m.Name == "RiftRevivalGetBackpackCapacity");
                    int[] values = capacity.Body.Instructions.Where(i => i.OpCode == OpCodes.Ldc_I4 || i.OpCode == OpCodes.Ldc_I4_4 || i.OpCode == OpCodes.Ldc_I4_8).Select(i => i.OpCode == OpCodes.Ldc_I4_4 ? 4 : i.OpCode == OpCodes.Ldc_I4_8 ? 8 : Convert.ToInt32(i.Operand)).ToArray();
                    foreach (int expected in new[] { 4, 8, 12, 16 }) Need(values.Contains(expected), "Missing Tier capacity " + expected + ".");
                    MethodDefinition price = equipmentTool.Methods.Single(m => m.Name == "RiftRevivalGetBackpackShopPrice");
                    long[] prices = price.Body.Instructions.Where(i => i.OpCode == OpCodes.Ldc_I4).Select(i => Convert.ToInt64(i.Operand)).ToArray();
                    foreach (long expected in new long[] { 250000, 1000000, 7500000, 30000000 }) Need(prices.Contains(expected), "Missing approved shop price " + expected + ".");
                    MethodDefinition modifyPrice = equipmentTool.Methods.Single(m => m.Name == "ModifyPrice");
                    Need(modifyPrice.Overrides.Count == 1 && modifyPrice.Overrides[0].Name == "ModifyPrice", "Backpack prices do not override the stock tool-store base price.");
                    Need(modifyPrice.Body.Instructions.Any(i => i.Operand == price), "Tool-store pricing does not use the approved backpack tier prices.");
                    Need(modifyPrice.Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).FullName == "Game.Framework.FP Game.Framework.FP::CI(System.Int64)"), "Backpack prices are not converted to the game's fixed-point currency.");
                    TypeDefinition mountPoint = module.GetTypes().Single(t => t.FullName.EndsWith("PlayerEquipmentMountPoint"));
                    TypeDefinition equipmentType = module.GetTypes().Single(t => t.FullName.EndsWith("PlayerEquipmentType"));
                    Need(Convert.ToInt32(mountPoint.Fields.Single(f => f.Name == "PEMP_Backpack").Constant) == 11, "Backpack mount identity missing.");
                    for (int tierIndex = 0; tierIndex < 4; tierIndex++) Need(Convert.ToInt32(equipmentType.Fields.Single(f => f.Name == "PET_BackpackTier" + tierIndex).Constant) == 16 + tierIndex, "Backpack tier identity missing.");
                    TypeDefinition helpers = module.GetTypes().Single(t => t.FullName == "Game.ClientServer.Classes.Tools.PlayerEquipmentHelpers");
                    MethodDefinition recipeBuilder = helpers.Methods.Single(m => m.Name == "RiftRevivalAddBackpackRecipes");
                    MethodDefinition metadataBuilder = helpers.Methods.Single(m => m.Name == "RiftRevivalAddBackpackMetadata");
                    int[] recipeNumbers = recipeBuilder.Body.Instructions.Where(i => i.OpCode == OpCodes.Ldc_I4).Select(i => Convert.ToInt32(i.Operand)).ToArray();
                    foreach (int amount in new[] { 15, 20, 30, 35, 40, 50 }) Need(recipeNumbers.Contains(amount), "Approved recipe amount missing: " + amount + ".");
                    Need(helpers.Methods.Single(m => m.IsConstructor && m.IsStatic).Body.Instructions.Any(i => i.Operand == recipeBuilder), "Backpack recipes are not registered at startup.");
                    Need(helpers.Methods.Single(m => m.IsConstructor && m.IsStatic).Body.Instructions.Any(i => i.Operand == metadataBuilder), "Backpack names and icons are not registered at startup.");
                    string[] metadataStrings = metadataBuilder.Body.Instructions.Where(i => i.OpCode == OpCodes.Ldstr).Select(i => (string)i.Operand).ToArray();
                    foreach (string expected in new[] { "Backpack Tier 0", "Backpack Tier 1", "Backpack Tier 2", "Backpack Tier 3" }) Need(metadataStrings.Contains(expected), "Backpack name missing: " + expected);
                    Need(metadataBuilder.Body.Instructions.Count(i => i.Operand is FieldReference && ((FieldReference)i.Operand).Name == "IconCode") == 4, "Each backpack tier must register a native icon.");
                    Need(metadataBuilder.Body.Instructions.Count(i => i.Operand is FieldReference && ((FieldReference)i.Operand).Name == "EquipmentEffects") == 4, "Backpacks must register effect-free equipment properties.");
                    Need(metadataBuilder.Body.Instructions.Count(i => i.Operand is FieldReference && ((FieldReference)i.Operand).Name == "EquipmentScenePrint") == 4, "Each backpack tier must create a physical printer pickup.");
                    Need(metadataBuilder.Body.Instructions.Count(i => i.Operand is FieldReference && ((FieldReference)i.Operand).Name == "EquipmentSceneHand") == 4, "Each backpack tier must have a held pickup scene.");
                    TypeDefinition toolFactoryScreen = module.GetTypes().Single(t => t.FullName == "Game.Client.AgosStateToolFactory");
                    Need(toolFactoryScreen.Methods.Single(m => m.IsConstructor).Body.Instructions.Any(i => i.OpCode == OpCodes.Ldc_R4 && Convert.ToSingle(i.Operand) == 960f), "Printer recipe cost column was not widened for four ingredients.");
                    TypeDefinition player = module.GetTypes().Single(t => t.FullName == "Game.Server.Player");
                    MethodDefinition equip = player.Methods.Single(m => m.Name == "RiftRevivalEquipBackpack");
                    MethodDefinition remove = player.Methods.Single(m => m.Name == "RiftRevivalRemoveBackpack");
                    Need(equip.Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "RiftRevivalGetBackpackCapacity"), "Tier swap does not check destination capacity.");
                    Need(equip.Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "Clear"), "Tier swap does not atomically clear the old backpack after transfer.");
                    Need(remove.Body.Instructions.Any(i => i.Operand is FieldReference && ((FieldReference)i.Operand).Name == "ToolInventoryMax"), "Removal does not refuse a full hotbar.");
                    MethodDefinition store = player.Methods.Single(m => m.Name == "RiftRevivalStoreToolInBackpack");
                    MethodDefinition retrieve = player.Methods.Single(m => m.Name == "RiftRevivalRetrieveToolFromBackpack");
                    Need(store.Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "RiftRevivalGetBackpackCapacity"), "Storage does not enforce backpack capacity.");
                    Need(store.Body.Instructions.Any(i => i.Operand is FieldReference && ((FieldReference)i.Operand).Name == "MountPoint"), "Storage does not reject nested backpacks.");
                    Need(retrieve.Body.Instructions.Any(i => i.Operand is FieldReference && ((FieldReference)i.Operand).Name == "ToolInventoryMax"), "Retrieval does not enforce hotbar capacity.");
                    MethodDefinition overflow = player.Methods.Single(m => m.Name == "RiftRevivalAddOverflowToolToBackpack");
                    Need(overflow.Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "RiftRevivalGetBackpackCapacity"), "Pickup overflow does not enforce backpack capacity.");
                    MethodDefinition resourceOverflow = player.Methods.Single(m => m.Name == "RiftRevivalAddOverflowResourceToBackpack");
                    Need(resourceOverflow.Body.Instructions.Any(i => i.Operand is FieldReference && ((FieldReference)i.Operand).Name == "RiftRevivalBackpackResources"), "Resource overflow does not preserve the real resource crate.");
                    Need(personal.Methods.Single(m => m.Name == "sv_resourceInventory_AddOrMergeCrate").Body.Instructions.Any(i => i.Operand == resourceOverflow), "Full resource inventory does not overflow into the backpack.");
                    Need(overflow.Body.Instructions.Any(i => i.Operand is FieldReference && ((FieldReference)i.Operand).Name == "MountPoint"), "Pickup overflow does not reject nested backpacks.");
                    MethodDefinition addInventoryTool = player.Methods.Single(m => m.Name == "AddToolItemToInventory");
                    Need(addInventoryTool.Body.Instructions.Any(i => i.Operand == overflow), "Full hotbar does not route ordinary acquisitions into the backpack.");
                    Need(addInventoryTool.Body.Instructions.Any(i => i.OpCode == OpCodes.Ldarg_2), "Internal inventory transfers are not separated from ordinary pickup overflow.");
                    MethodDefinition drop = player.Methods.Single(m => m.Name == "RiftRevivalDropBackpackOnDeath");
                    Need(drop.Body.Instructions.Any(i => i.Operand is FieldReference && ((FieldReference)i.Operand).Name == "RiftRevivalBackpack"), "Death drop does not move the equipped physical backpack.");
                    Need(player.Methods.Single(m => m.Name == "Kill").Body.Instructions.Any(i => i.Operand == drop), "Backpack death drop is not attached to the stock armour-drop branch.");
                    MethodDefinition finish = player.Methods.Single(m => m.Name == "RiftRevivalFinishBackpackAction");
                    Need(finish.Body.Instructions.Any(i => i.Operand is FieldReference && ((FieldReference)i.Operand).Name == "RiftRevivalBackpackResult"), "Action results are not retained for client feedback.");
                    string[] feedback = player.Methods.Where(m => m.Name.StartsWith("RiftRevival") && m.HasBody).SelectMany(m => m.Body.Instructions).Where(i => i.OpCode == OpCodes.Ldstr).Select(i => (string)i.Operand).ToArray();
                    foreach (string expected in new[] { "The backpack is full.", "The tool inventory is full.", "The replacement backpack is too small for the stored items.", "A backpack cannot be stored inside another backpack." })
                        Need(feedback.Contains(expected), "Missing explicit feedback: " + expected);
                    TypeDefinition glove = module.GetTypes().Single(t => t.FullName == "Game.ClientServer.Classes.Tools.ClSvPlayerTool_PowerGlove");
                    Instruction[] commands = (Instruction[])glove.Methods.Single(m => m.Name == "Server_OnData").Body.Instructions.Single(i => i.OpCode == OpCodes.Switch).Operand;
                    Need(commands.Length == 22, "Backpack command table does not reserve commands 16-21.");
                    Need(player.Methods.Any(m => m.Name == "RiftRevivalStoreResourceInBackpack"), "Backpack resource storage is missing.");
                    Need(player.Methods.Any(m => m.Name == "RiftRevivalRetrieveResourceFromBackpack"), "Backpack resource retrieval is missing.");
                    Need(glove.Methods.Any(m => m.Name == "RiftRevivalSendBackpackResult"), "Server backpack-result packet sender missing.");
                    Need(glove.Methods.Any(m => m.Name == "RiftRevivalReceiveBackpackResult"), "Client backpack-result packet receiver missing.");
                    Need(glove.Methods.Single(m => m.Name == "RiftRevivalReceiveBackpackResult").Body.Instructions.Any(i => i.Operand is TypeReference && ((TypeReference)i.Operand).FullName == "Game.ClientServer.Packets.NetTupleIntString"), "Backpack replies do not use their distinct packet type.");
                    TypeDefinition equipmentScreen = module.GetTypes().Single(t => t.FullName == "Game.Client.AgosStateCharacterCustomizationMenu");
                    Need(equipmentScreen.Fields.Any(f => f.Name == "RiftRevivalBackpackSlots"), "Backpack inventory-row mapping missing.");
                    Need(equipmentScreen.Fields.Any(f => f.Name == "RiftRevivalBackpackEquippedRow"), "Equipped backpack row mapping missing.");
                    MethodDefinition fillRows = equipmentScreen.Methods.Single(m => m.Name == "RiftRevivalFillBackpackRows");
                    Need(fillRows.Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "RiftRevivalGetBackpackItemCount"), "Equipment row does not show combined stored item count.");
                    Need(fillRows.Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "RiftRevivalGetBackpackCapacity"), "Equipment row does not show backpack capacity.");
                    MethodDefinition handleRows = equipmentScreen.Methods.Single(m => m.Name == "RiftRevivalHandleBackpackClick");
                    Need(handleRows.Body.Instructions.Any(i => i.Operand is FieldReference && ((FieldReference)i.Operand).Name == "ToolItem"), "Stock equipment-list backpack rows are not identified before the armour click path.");
                    Need(handleRows.Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "IndexOf"), "Stock backpack rows do not resolve their real tool-inventory slot.");
                    int[] uiCommands = handleRows.Body.Instructions.Where(i => i.OpCode == OpCodes.Ldc_I4).Select(i => Convert.ToInt32(i.Operand)).ToArray();
                    Need(uiCommands.Contains(16) && uiCommands.Contains(17), "Equipment rows are not wired to server equip/remove commands.");
                    Need(equipmentScreen.Methods.Single(m => m.Name == "Update").Body.Instructions.Any(i => i.Operand == fillRows), "Equipment page does not refresh backpack rows.");
                    Need(equipmentScreen.Methods.Single(m => m.Name == "OnHandleButtonEvents").Body.Instructions.Any(i => i.Operand == handleRows), "Equipment page does not route backpack row clicks.");
                    MethodDefinition refreshGrid = equipmentScreen.Methods.Single(m => m.Name == "RiftRevivalRefreshBackpackGrid");
                    Need(refreshGrid.Body.Instructions.Count(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "Initialize") == 1, "Every backpack cell, including an empty one, must be initialized for rendering and clicks.");
                    Need(equipmentScreen.Fields.Any(f => f.Name == "RiftRevivalBackpackWindows"), "Backpack grid window collection missing.");
                    Need(refreshGrid.Body.Instructions.Any(i => i.OpCode == OpCodes.Newobj && i.Operand is MethodReference && ((MethodReference)i.Operand).DeclaringType.FullName == "Game.Client.EncyclopediaItemWindow"), "Grid does not construct native Encyclopedia cells.");
                    foreach (string helper in new[] { "GetToolNameString", "GetToolIconCode", "GetToolColorString" }) Need(refreshGrid.Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == helper), "Grid does not use native tool metadata: " + helper);
                    Need(refreshGrid.Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "SetPosition"), "Grid cells are not positioned.");
                    Need(equipmentScreen.Methods.Single(m => m.Name == "Update").Body.Instructions.Any(i => i.Operand == refreshGrid), "Equipment page does not refresh the backpack grid.");
                    MethodDefinition cellClick = equipmentScreen.Methods.Single(m => m.Name == "RiftRevivalOnBackpackCellClicked");
                    int[] gridCommands = cellClick.Body.Instructions.Where(i => i.OpCode == OpCodes.Ldc_I4).Select(i => Convert.ToInt32(i.Operand)).ToArray();
                    Need(gridCommands.Contains(18) && gridCommands.Contains(19), "Grid cells do not route store and retrieve actions through the server.");
                    Need(cellClick.Body.Instructions.Any(i => i.Operand is FieldReference && ((FieldReference)i.Operand).Name == "RiftRevivalBackpackSlot"), "Grid click does not preserve the selected backpack slot.");
                    Need(refreshGrid.Body.Instructions.Any(i => i.Operand is FieldReference && ((FieldReference)i.Operand).Name == "OnClicked"), "Occupied cells do not attach their click handler.");
                    MethodDefinition clearGrid = equipmentScreen.Methods.Single(m => m.Name == "RiftRevivalClearBackpackGrid");
                    Need(clearGrid.Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "Remove"), "Backpack grid cleanup does not remove native windows.");
                    Need(equipmentScreen.Methods.Single(m => m.Name == "Unload").Body.Instructions.Any(i => i.Operand == clearGrid), "Equipment screen does not clean up the backpack grid on close.");
                    using (MemoryStream stream = new MemoryStream()) { module.Write(stream, new WriterParameters { Timestamp = 0 }); output = stream.ToArray(); }
                }
                using (MemoryStream stream = new MemoryStream(output, false))
                using (ModuleDefinition check = ModuleDefinition.ReadModule(stream))
                    Need(check.GetTypes().Single(t => t.FullName == "Game.ClientServer.Classes.Tools.ClSvPlayerTool_PlayerEquipment").Fields.Any(f => f.Name == "RiftRevivalBackpackInventory"), "Written assembly lost backpack-owned storage.");
                if (args.Length > 1) File.WriteAllBytes(args[1], output);
                Console.WriteLine("PASS: dedicated saved backpack storage, capacities 4/8/12/16, and approved shop prices.");
                return 0;
            }
            catch (Exception error) { Console.Error.WriteLine("FAIL: " + error); return 1; }
        }
    }
}
