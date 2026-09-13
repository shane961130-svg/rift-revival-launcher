using System;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace RiftRevival.Launcher
{
    // Saved storage foundation for the separately paged backpack inventory.
    internal static class BackpackPatch
    {
        public static void Apply(ModuleDefinition module)
        {
            TypeDefinition[] types = module.GetTypes().ToArray();
            MethodReference[] references = types.SelectMany(t => t.Methods).Where(m => m.HasBody).SelectMany(m => m.Body.Instructions).Select(i => i.Operand as MethodReference).Where(r => r != null).ToArray();
            TypeDefinition personal = types.Single(t => t.FullName == "Game.ClientServer.Classes.ClSvPlayerState_Personal");
            TypeDefinition equipmentTool = types.Single(t => t.FullName == "Game.ClientServer.Classes.Tools.ClSvPlayerTool_PlayerEquipment");
            if (personal.Fields.Any(f => f.Name == "RiftRevivalBackpack"))
                throw new InvalidDataException("The backpack patch is already present.");

            FieldDefinition toolInventory = personal.Fields.Single(f => f.Name == "ToolInventory");
            FieldDefinition resourceInventory = personal.Fields.Single(f => f.Name == "ResourceInventory");
            FieldDefinition tier = new FieldDefinition("RiftRevivalBackpackTier", Mono.Cecil.FieldAttributes.Public, module.TypeSystem.Int32);
            FieldDefinition inventory = new FieldDefinition("RiftRevivalBackpackInventory", Mono.Cecil.FieldAttributes.Public, toolInventory.FieldType);
            FieldDefinition resources = new FieldDefinition("RiftRevivalBackpackResources", Mono.Cecil.FieldAttributes.Public, resourceInventory.FieldType);
            FieldDefinition equipped = new FieldDefinition("RiftRevivalBackpack", Mono.Cecil.FieldAttributes.Public, equipmentTool);
            FieldDefinition result = new FieldDefinition("RiftRevivalBackpackResult", Mono.Cecil.FieldAttributes.Public | Mono.Cecil.FieldAttributes.NotSerialized, module.TypeSystem.String);
            equipmentTool.Fields.Add(tier);
            equipmentTool.Fields.Add(inventory);
            equipmentTool.Fields.Add(resources);
            personal.Fields.Add(equipped);
            personal.Fields.Add(result);

            MethodDefinition personalCtor = personal.Methods.Single(m => m.IsConstructor && !m.IsStatic);
            MethodReference collectionCtor = personalCtor.Body.Instructions
                .Where(i => i.OpCode == OpCodes.Newobj)
                .Select(i => i.Operand as MethodReference)
                .First(r => r != null && r.Name == ".ctor" && r.DeclaringType.FullName == toolInventory.FieldType.FullName);
            MethodReference resourceCollectionCtor = personalCtor.Body.Instructions
                .Where(i => i.OpCode == OpCodes.Newobj)
                .Select(i => i.Operand as MethodReference)
                .First(r => r != null && r.Name == ".ctor" && r.DeclaringType.FullName == resourceInventory.FieldType.FullName);
            MethodDefinition ctor = equipmentTool.Methods.Single(m => m.IsConstructor && !m.IsStatic);
            Instruction ctorRet = ctor.Body.Instructions.Last(i => i.OpCode == OpCodes.Ret);
            ILProcessor ctorIl = ctor.Body.GetILProcessor();
            ctorIl.InsertBefore(ctorRet, Instruction.Create(OpCodes.Ldarg_0));
            ctorIl.InsertBefore(ctorRet, Instruction.Create(OpCodes.Ldc_I4_M1));
            ctorIl.InsertBefore(ctorRet, Instruction.Create(OpCodes.Stfld, tier));
            ctorIl.InsertBefore(ctorRet, Instruction.Create(OpCodes.Ldarg_0));
            ctorIl.InsertBefore(ctorRet, Instruction.Create(OpCodes.Newobj, collectionCtor));
            ctorIl.InsertBefore(ctorRet, Instruction.Create(OpCodes.Stfld, inventory));
            ctorIl.InsertBefore(ctorRet, Instruction.Create(OpCodes.Ldarg_0));
            ctorIl.InsertBefore(ctorRet, Instruction.Create(OpCodes.Newobj, resourceCollectionCtor));
            ctorIl.InsertBefore(ctorRet, Instruction.Create(OpCodes.Stfld, resources));

            MethodDefinition capacity = new MethodDefinition("RiftRevivalGetBackpackCapacity", Mono.Cecil.MethodAttributes.Public, module.TypeSystem.Int32);
            equipmentTool.Methods.Add(capacity);
            ILProcessor il = capacity.Body.GetILProcessor();
            Instruction tierOne = Instruction.Create(OpCodes.Nop);
            Instruction tierTwo = Instruction.Create(OpCodes.Nop);
            Instruction tierThree = Instruction.Create(OpCodes.Nop);
            Instruction none = Instruction.Create(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, tier); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Bne_Un, tierOne); il.Emit(OpCodes.Ldc_I4_4); il.Emit(OpCodes.Ret);
            il.Append(tierOne); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, tier); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Bne_Un, tierTwo); il.Emit(OpCodes.Ldc_I4_8); il.Emit(OpCodes.Ret);
            il.Append(tierTwo); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, tier); il.Emit(OpCodes.Ldc_I4_2); il.Emit(OpCodes.Bne_Un, tierThree); il.Emit(OpCodes.Ldc_I4, 12); il.Emit(OpCodes.Ret);
            il.Append(tierThree); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, tier); il.Emit(OpCodes.Ldc_I4_3); il.Emit(OpCodes.Bne_Un, none); il.Emit(OpCodes.Ldc_I4, 16); il.Emit(OpCodes.Ret);
            il.Append(none); il.Emit(OpCodes.Ret);

            MethodDefinition itemCount = new MethodDefinition("RiftRevivalGetBackpackItemCount", Mono.Cecil.MethodAttributes.Public, module.TypeSystem.Int32);
            equipmentTool.Methods.Add(itemCount); il = itemCount.Body.GetILProcessor();
            MethodReference resourceCount = new MethodReference("get_Count", module.TypeSystem.Int32, resourceInventory.FieldType) { HasThis = true };
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, inventory); il.Emit(OpCodes.Callvirt, new MethodReference("get_Count", module.TypeSystem.Int32, toolInventory.FieldType) { HasThis = true });
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, resources); il.Emit(OpCodes.Callvirt, resourceCount); il.Emit(OpCodes.Add); il.Emit(OpCodes.Ret);

            MethodDefinition price = new MethodDefinition("RiftRevivalGetBackpackShopPrice", Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Static, module.TypeSystem.Int64);
            price.Parameters.Add(new ParameterDefinition("backpackTier", Mono.Cecil.ParameterAttributes.None, module.TypeSystem.Int32));
            equipmentTool.Methods.Add(price); il = price.Body.GetILProcessor();
            Instruction priceOne = Instruction.Create(OpCodes.Nop);
            Instruction priceTwo = Instruction.Create(OpCodes.Nop);
            Instruction priceThree = Instruction.Create(OpCodes.Nop);
            Instruction priceNone = Instruction.Create(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Bne_Un, priceOne); il.Emit(OpCodes.Ldc_I4, 250000); il.Emit(OpCodes.Conv_I8); il.Emit(OpCodes.Ret);
            il.Append(priceOne); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Bne_Un, priceTwo); il.Emit(OpCodes.Ldc_I4, 1000000); il.Emit(OpCodes.Conv_I8); il.Emit(OpCodes.Ret);
            il.Append(priceTwo); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldc_I4_2); il.Emit(OpCodes.Bne_Un, priceThree); il.Emit(OpCodes.Ldc_I4, 7500000); il.Emit(OpCodes.Conv_I8); il.Emit(OpCodes.Ret);
            il.Append(priceThree); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldc_I4_3); il.Emit(OpCodes.Bne_Un, priceNone); il.Emit(OpCodes.Ldc_I4, 30000000); il.Emit(OpCodes.Conv_I8); il.Emit(OpCodes.Ret);
            il.Append(priceNone); il.Emit(OpCodes.Conv_I8); il.Emit(OpCodes.Ret);

            // Player equipment normally inherits the generic tool price. Give
            // backpacks their approved fixed base prices while leaving every
            // stock armour item on the original pricing path. The surrounding
            // store code still applies its normal buy/sell and reputation
            // multipliers to this base value.
            MethodDefinition baseModifyPrice = types.Single(t => t.FullName == "Game.ClientServer.Classes.Tools.ClSvPlayerTool").Methods.Single(m => m.Name == "ModifyPrice");
            MethodReference fpFromLong = references.First(r => r.FullName == "Game.Framework.FP Game.Framework.FP::CI(System.Int64)");
            MethodDefinition modifyPrice = new MethodDefinition("ModifyPrice", Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Virtual | Mono.Cecil.MethodAttributes.HideBySig, baseModifyPrice.ReturnType);
            foreach (ParameterDefinition parameter in baseModifyPrice.Parameters)
                modifyPrice.Parameters.Add(new ParameterDefinition(parameter.Name, parameter.Attributes, parameter.ParameterType));
            modifyPrice.Overrides.Add(baseModifyPrice);
            equipmentTool.Methods.Add(modifyPrice); il = modifyPrice.Body.GetILProcessor();
            Instruction useStockPrice = Instruction.Create(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, tier); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Blt, useStockPrice);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, tier); il.Emit(OpCodes.Ldc_I4_3); il.Emit(OpCodes.Bgt, useStockPrice);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, tier); il.Emit(OpCodes.Call, price); il.Emit(OpCodes.Call, fpFromLong); il.Emit(OpCodes.Ret);
            il.Append(useStockPrice); il.Emit(OpCodes.Ret);

            TypeDefinition player = types.Single(t => t.FullName == "Game.Server.Player");
            MethodDefinition getTool = player.Methods.Single(m => m.Name == "GetToolInInventorySlot");
            MethodDefinition grabTool = player.Methods.Single(m => m.Name == "GrabToolItemFromInventory");
            MethodDefinition addTool = player.Methods.Single(m => m.Name == "AddToolItemToInventory");
            MethodReference getPersonal = player.Methods.Single(m => m.Name == "get_PersonalState" && !m.HasParameters);
            MethodDefinition finishAction = new MethodDefinition("RiftRevivalFinishBackpackAction", Mono.Cecil.MethodAttributes.Private, module.TypeSystem.Boolean);
            finishAction.Parameters.Add(new ParameterDefinition("success", Mono.Cecil.ParameterAttributes.None, module.TypeSystem.Boolean));
            finishAction.Parameters.Add(new ParameterDefinition("message", Mono.Cecil.ParameterAttributes.None, module.TypeSystem.String));
            player.Methods.Add(finishAction); il = finishAction.Body.GetILProcessor();
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, getPersonal); il.Emit(OpCodes.Ldarg_2); il.Emit(OpCodes.Stfld, result);
            il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ret);
            MethodReference collectionCount = (MethodReference)getTool.Body.Instructions.First(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "get_Count").Operand;
            MethodReference collectionItem = (MethodReference)getTool.Body.Instructions.First(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "get_Item").Operand;
            MethodReference collectionAdd = (MethodReference)addTool.Body.Instructions.First(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "Add" && ((MethodReference)i.Operand).DeclaringType.FullName.Contains("ClSvPlayerTool")).Operand;
            MethodReference collectionRemoveAt = (MethodReference)grabTool.Body.Instructions.First(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "RemoveAt").Operand;
            MethodReference collectionClear = new MethodReference("Clear", module.TypeSystem.Void, collectionCount.DeclaringType) { HasThis = true };
            TypeDefinition resourceCrate = types.Single(t => t.FullName == "Game.ClientServer.Classes.ClSvPlayerResourceCrate");
            FieldDefinition resourceAmount = resourceCrate.Fields.Single(f => f.Name == "amount");
            FieldDefinition resourceType = resourceCrate.Fields.Single(f => f.Name == "resourceType");
            MethodReference backpackResourceAdd = references.First(r => r.Name == "Add" && r.DeclaringType.FullName.StartsWith("System.Collections.ObjectModel.Collection`1<Game.ClientServer.Classes.ClSvPlayerResourceCrate>"));
            MethodReference backpackResourceItem = references.First(r => r.Name == "get_Item" && r.DeclaringType.FullName.StartsWith("System.Collections.ObjectModel.Collection`1<Game.ClientServer.Classes.ClSvPlayerResourceCrate>"));
            MethodReference resourceClear = new MethodReference("Clear", module.TypeSystem.Void, resourceInventory.FieldType) { HasThis = true };
            TypeDefinition propertiesType = types.Single(t => t.FullName == "Game.ClientServer.Classes.Tools.PlayerEquipmentHelpers/PlayerEquipmentProperties");
            FieldDefinition properties = equipmentTool.Fields.Single(f => f.Name == "Properties");
            FieldDefinition propertyMount = propertiesType.Fields.Single(f => f.Name == "MountPoint");
            FieldDefinition propertyType = propertiesType.Fields.Single(f => f.Name == "EquipmentType");

            MethodDefinition equipBackpack = new MethodDefinition("RiftRevivalEquipBackpack", Mono.Cecil.MethodAttributes.Public, module.TypeSystem.Boolean);
            equipBackpack.Parameters.Add(new ParameterDefinition("slot", Mono.Cecil.ParameterAttributes.None, module.TypeSystem.Int32));
            VariableDefinition candidate = new VariableDefinition(equipmentTool), current = new VariableDefinition(equipmentTool), moved = new VariableDefinition(module.TypeSystem.Int32);
            equipBackpack.Body.Variables.Add(candidate); equipBackpack.Body.Variables.Add(current); equipBackpack.Body.Variables.Add(moved); equipBackpack.Body.InitLocals = true;
            player.Methods.Add(equipBackpack); il = equipBackpack.Body.GetILProcessor();
            Instruction candidateOk = Instruction.Create(OpCodes.Nop), mountOk = Instruction.Create(OpCodes.Nop), typeOk = Instruction.Create(OpCodes.Nop), capacityOk = Instruction.Create(OpCodes.Nop), noCurrent = Instruction.Create(OpCodes.Nop), moveTest = Instruction.Create(OpCodes.Nop), moveBody = Instruction.Create(OpCodes.Nop), movedAll = Instruction.Create(OpCodes.Nop);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Call, getTool); il.Emit(OpCodes.Isinst, equipmentTool); il.Emit(OpCodes.Stloc, candidate);
            il.Emit(OpCodes.Ldloc, candidate); il.Emit(OpCodes.Brtrue, candidateOk); EmitResult(il, finishAction, false, "Select a backpack from the tool inventory."); il.Append(candidateOk);
            il.Emit(OpCodes.Ldloc, candidate); il.Emit(OpCodes.Ldflda, properties); il.Emit(OpCodes.Ldfld, propertyMount); il.Emit(OpCodes.Ldc_I4, 11); il.Emit(OpCodes.Beq, mountOk); EmitResult(il, finishAction, false, "That item is not a backpack."); il.Append(mountOk);
            il.Emit(OpCodes.Ldloc, candidate); il.Emit(OpCodes.Ldflda, properties); il.Emit(OpCodes.Ldfld, propertyType); il.Emit(OpCodes.Ldc_I4, 16); il.Emit(OpCodes.Blt, typeOk);
            il.Emit(OpCodes.Ldloc, candidate); il.Emit(OpCodes.Ldflda, properties); il.Emit(OpCodes.Ldfld, propertyType); il.Emit(OpCodes.Ldc_I4, 20); il.Emit(OpCodes.Blt, capacityOk); il.Append(typeOk); EmitResult(il, finishAction, false, "That backpack tier is not supported."); il.Append(capacityOk);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, getPersonal); il.Emit(OpCodes.Ldfld, equipped); il.Emit(OpCodes.Stloc, current);
            il.Emit(OpCodes.Ldloc, current); il.Emit(OpCodes.Brfalse, noCurrent);
            il.Emit(OpCodes.Ldloc, current); il.Emit(OpCodes.Call, itemCount);
            il.Emit(OpCodes.Ldloc, candidate); il.Emit(OpCodes.Call, capacity); il.Emit(OpCodes.Ble, noCurrent); EmitResult(il, finishAction, false, "The replacement backpack is too small for the stored items.");
            il.Append(noCurrent);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Call, grabTool); il.Emit(OpCodes.Castclass, equipmentTool); il.Emit(OpCodes.Stloc, candidate);
            il.Emit(OpCodes.Ldloc, current); il.Emit(OpCodes.Brfalse, movedAll); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Stloc, moved); il.Emit(OpCodes.Br, moveTest);
            il.Append(moveBody); il.Emit(OpCodes.Ldloc, candidate); il.Emit(OpCodes.Ldfld, inventory); il.Emit(OpCodes.Ldloc, current); il.Emit(OpCodes.Ldfld, inventory); il.Emit(OpCodes.Ldloc, moved); il.Emit(OpCodes.Callvirt, collectionItem); il.Emit(OpCodes.Callvirt, collectionAdd);
            il.Emit(OpCodes.Ldloc, moved); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Add); il.Emit(OpCodes.Stloc, moved); il.Append(moveTest);
            il.Emit(OpCodes.Ldloc, moved); il.Emit(OpCodes.Ldloc, current); il.Emit(OpCodes.Ldfld, inventory); il.Emit(OpCodes.Callvirt, collectionCount); il.Emit(OpCodes.Blt, moveBody);
            il.Emit(OpCodes.Ldloc, current); il.Emit(OpCodes.Ldfld, inventory); il.Emit(OpCodes.Callvirt, collectionClear);
            Instruction resourceMoveTest = Instruction.Create(OpCodes.Nop), resourceMoveBody = Instruction.Create(OpCodes.Nop), resourcesMoved = Instruction.Create(OpCodes.Nop);
            il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Stloc, moved); il.Emit(OpCodes.Br, resourceMoveTest);
            il.Append(resourceMoveBody); il.Emit(OpCodes.Ldloc, candidate); il.Emit(OpCodes.Ldfld, resources); il.Emit(OpCodes.Ldloc, current); il.Emit(OpCodes.Ldfld, resources); il.Emit(OpCodes.Ldloc, moved); il.Emit(OpCodes.Callvirt, backpackResourceItem); il.Emit(OpCodes.Callvirt, backpackResourceAdd);
            il.Emit(OpCodes.Ldloc, moved); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Add); il.Emit(OpCodes.Stloc, moved); il.Append(resourceMoveTest);
            il.Emit(OpCodes.Ldloc, moved); il.Emit(OpCodes.Ldloc, current); il.Emit(OpCodes.Ldfld, resources); il.Emit(OpCodes.Callvirt, resourceCount); il.Emit(OpCodes.Blt, resourceMoveBody);
            il.Emit(OpCodes.Ldloc, current); il.Emit(OpCodes.Ldfld, resources); il.Emit(OpCodes.Callvirt, resourceClear); il.Append(resourcesMoved);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldloc, current); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Call, addTool); il.Emit(OpCodes.Pop);
            il.Append(movedAll); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, getPersonal); il.Emit(OpCodes.Ldloc, candidate); il.Emit(OpCodes.Stfld, equipped); EmitResult(il, finishAction, true, "Backpack equipped.");

            MethodDefinition removeBackpack = new MethodDefinition("RiftRevivalRemoveBackpack", Mono.Cecil.MethodAttributes.Public, module.TypeSystem.Boolean);
            player.Methods.Add(removeBackpack); il = removeBackpack.Body.GetILProcessor();
            Instruction hasBackpack = Instruction.Create(OpCodes.Nop), hasRoom = Instruction.Create(OpCodes.Nop);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, getPersonal); il.Emit(OpCodes.Ldfld, equipped); il.Emit(OpCodes.Brtrue, hasBackpack); EmitResult(il, finishAction, false, "No backpack is equipped."); il.Append(hasBackpack);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, getPersonal); il.Emit(OpCodes.Ldfld, toolInventory); il.Emit(OpCodes.Callvirt, collectionCount);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, getPersonal); il.Emit(OpCodes.Ldfld, personal.Fields.Single(f => f.Name == "ToolInventoryMax")); il.Emit(OpCodes.Blt, hasRoom); EmitResult(il, finishAction, false, "The tool inventory is full."); il.Append(hasRoom);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, getPersonal); il.Emit(OpCodes.Ldfld, equipped); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Call, addTool); il.Emit(OpCodes.Brtrue, candidateOk = Instruction.Create(OpCodes.Nop)); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ret); il.Append(candidateOk);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, getPersonal); il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Stfld, equipped); EmitResult(il, finishAction, true, "Backpack removed with its contents intact.");

            MethodDefinition storeTool = new MethodDefinition("RiftRevivalStoreToolInBackpack", Mono.Cecil.MethodAttributes.Public, module.TypeSystem.Boolean);
            storeTool.Parameters.Add(new ParameterDefinition("slot", Mono.Cecil.ParameterAttributes.None, module.TypeSystem.Int32));
            VariableDefinition storeCandidate = new VariableDefinition(types.Single(t => t.FullName == "Game.ClientServer.Classes.Tools.ClSvPlayerTool"));
            VariableDefinition storeBag = new VariableDefinition(equipmentTool);
            storeTool.Body.Variables.Add(storeCandidate); storeTool.Body.Variables.Add(storeBag); storeTool.Body.InitLocals = true; player.Methods.Add(storeTool); il = storeTool.Body.GetILProcessor();
            Instruction storeHasBag = Instruction.Create(OpCodes.Nop), storeHasRoom = Instruction.Create(OpCodes.Nop), storeNotBag = Instruction.Create(OpCodes.Nop);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, getPersonal); il.Emit(OpCodes.Ldfld, equipped); il.Emit(OpCodes.Stloc, storeBag); il.Emit(OpCodes.Ldloc, storeBag); il.Emit(OpCodes.Brtrue, storeHasBag); EmitResult(il, finishAction, false, "Equip a backpack before storing items."); il.Append(storeHasBag);
            il.Emit(OpCodes.Ldloc, storeBag); il.Emit(OpCodes.Call, itemCount); il.Emit(OpCodes.Ldloc, storeBag); il.Emit(OpCodes.Call, capacity); il.Emit(OpCodes.Blt, storeHasRoom); EmitResult(il, finishAction, false, "The backpack is full."); il.Append(storeHasRoom);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Call, getTool); il.Emit(OpCodes.Stloc, storeCandidate); il.Emit(OpCodes.Ldloc, storeCandidate); il.Emit(OpCodes.Brtrue, storeNotBag); EmitResult(il, finishAction, false, "That tool-inventory slot is empty."); il.Append(storeNotBag);
            Instruction ordinaryTool = Instruction.Create(OpCodes.Nop); il.Emit(OpCodes.Ldloc, storeCandidate); il.Emit(OpCodes.Isinst, equipmentTool); il.Emit(OpCodes.Brfalse, ordinaryTool);
            il.Emit(OpCodes.Ldloc, storeCandidate); il.Emit(OpCodes.Castclass, equipmentTool); il.Emit(OpCodes.Ldflda, properties); il.Emit(OpCodes.Ldfld, propertyMount); il.Emit(OpCodes.Ldc_I4, 11); il.Emit(OpCodes.Bne_Un, ordinaryTool); EmitResult(il, finishAction, false, "A backpack cannot be stored inside another backpack."); il.Append(ordinaryTool);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Call, grabTool); il.Emit(OpCodes.Stloc, storeCandidate);
            il.Emit(OpCodes.Ldloc, storeBag); il.Emit(OpCodes.Ldfld, inventory); il.Emit(OpCodes.Ldloc, storeCandidate); il.Emit(OpCodes.Callvirt, collectionAdd); EmitResult(il, finishAction, true, "Item stored in backpack.");

            MethodDefinition retrieveTool = new MethodDefinition("RiftRevivalRetrieveToolFromBackpack", Mono.Cecil.MethodAttributes.Public, module.TypeSystem.Boolean);
            retrieveTool.Parameters.Add(new ParameterDefinition("slot", Mono.Cecil.ParameterAttributes.None, module.TypeSystem.Int32));
            VariableDefinition retrieveBag = new VariableDefinition(equipmentTool); VariableDefinition retrieveCandidate = new VariableDefinition(storeCandidate.VariableType);
            retrieveTool.Body.Variables.Add(retrieveBag); retrieveTool.Body.Variables.Add(retrieveCandidate); retrieveTool.Body.InitLocals = true; player.Methods.Add(retrieveTool); il = retrieveTool.Body.GetILProcessor();
            Instruction retrieveHasBag = Instruction.Create(OpCodes.Nop), retrieveHasRoom = Instruction.Create(OpCodes.Nop), retrieveValid = Instruction.Create(OpCodes.Nop), retrieveAdded = Instruction.Create(OpCodes.Nop);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, getPersonal); il.Emit(OpCodes.Ldfld, equipped); il.Emit(OpCodes.Stloc, retrieveBag); il.Emit(OpCodes.Ldloc, retrieveBag); il.Emit(OpCodes.Brtrue, retrieveHasBag); EmitResult(il, finishAction, false, "No backpack is equipped."); il.Append(retrieveHasBag);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, getPersonal); il.Emit(OpCodes.Ldfld, toolInventory); il.Emit(OpCodes.Callvirt, collectionCount); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, getPersonal); il.Emit(OpCodes.Ldfld, personal.Fields.Single(f => f.Name == "ToolInventoryMax")); il.Emit(OpCodes.Blt, retrieveHasRoom); EmitResult(il, finishAction, false, "The tool inventory is full."); il.Append(retrieveHasRoom);
            il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Blt, retrieveValid); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldloc, retrieveBag); il.Emit(OpCodes.Ldfld, inventory); il.Emit(OpCodes.Callvirt, collectionCount); il.Emit(OpCodes.Blt, retrieveAdded); il.Append(retrieveValid); EmitResult(il, finishAction, false, "That backpack slot is empty."); il.Append(retrieveAdded);
            il.Emit(OpCodes.Ldloc, retrieveBag); il.Emit(OpCodes.Ldfld, inventory); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Callvirt, collectionItem); il.Emit(OpCodes.Stloc, retrieveCandidate);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldloc, retrieveCandidate); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Call, addTool); il.Emit(OpCodes.Brtrue, candidateOk = Instruction.Create(OpCodes.Nop)); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ret); il.Append(candidateOk);
            il.Emit(OpCodes.Ldloc, retrieveBag); il.Emit(OpCodes.Ldfld, inventory); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Callvirt, collectionRemoveAt); EmitResult(il, finishAction, true, "Item moved to the tool inventory.");

            MethodDefinition overflowTool = new MethodDefinition("RiftRevivalAddOverflowToolToBackpack", Mono.Cecil.MethodAttributes.Public, module.TypeSystem.Boolean);
            overflowTool.Parameters.Add(new ParameterDefinition("tool", Mono.Cecil.ParameterAttributes.None, storeCandidate.VariableType));
            VariableDefinition overflowBag = new VariableDefinition(equipmentTool); overflowTool.Body.Variables.Add(overflowBag); overflowTool.Body.InitLocals = true; player.Methods.Add(overflowTool); il = overflowTool.Body.GetILProcessor();
            Instruction overflowHasBag = Instruction.Create(OpCodes.Nop), overflowHasRoom = Instruction.Create(OpCodes.Nop), overflowOrdinary = Instruction.Create(OpCodes.Nop);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, getPersonal); il.Emit(OpCodes.Ldfld, equipped); il.Emit(OpCodes.Stloc, overflowBag); il.Emit(OpCodes.Ldloc, overflowBag); il.Emit(OpCodes.Brtrue, overflowHasBag); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ret); il.Append(overflowHasBag);
            il.Emit(OpCodes.Ldloc, overflowBag); il.Emit(OpCodes.Call, itemCount); il.Emit(OpCodes.Ldloc, overflowBag); il.Emit(OpCodes.Call, capacity); il.Emit(OpCodes.Blt, overflowHasRoom); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ret); il.Append(overflowHasRoom);
            il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Isinst, equipmentTool); il.Emit(OpCodes.Brfalse, overflowOrdinary); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Castclass, equipmentTool); il.Emit(OpCodes.Ldflda, properties); il.Emit(OpCodes.Ldfld, propertyMount); il.Emit(OpCodes.Ldc_I4, 11); il.Emit(OpCodes.Bne_Un, overflowOrdinary); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ret); il.Append(overflowOrdinary);
            il.Emit(OpCodes.Ldloc, overflowBag); il.Emit(OpCodes.Ldfld, inventory); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Callvirt, collectionAdd); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Ret);

            MethodDefinition overflowResource = new MethodDefinition("RiftRevivalAddOverflowResourceToBackpack", Mono.Cecil.MethodAttributes.Public, resourceCrate);
            overflowResource.Parameters.Add(new ParameterDefinition("crate", Mono.Cecil.ParameterAttributes.None, resourceCrate));
            VariableDefinition resourceBag = new VariableDefinition(equipmentTool); overflowResource.Body.Variables.Add(resourceBag); overflowResource.Body.InitLocals = true; player.Methods.Add(overflowResource); il = overflowResource.Body.GetILProcessor();
            Instruction resourceHasBag = Instruction.Create(OpCodes.Nop), resourceHasRoom = Instruction.Create(OpCodes.Nop), resourceDone = Instruction.Create(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldfld, resourceAmount); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ble, resourceDone);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, getPersonal); il.Emit(OpCodes.Ldfld, equipped); il.Emit(OpCodes.Stloc, resourceBag); il.Emit(OpCodes.Ldloc, resourceBag); il.Emit(OpCodes.Brtrue, resourceHasBag); il.Emit(OpCodes.Br, resourceDone);
            il.Append(resourceHasBag); il.Emit(OpCodes.Ldloc, resourceBag); il.Emit(OpCodes.Call, itemCount); il.Emit(OpCodes.Ldloc, resourceBag); il.Emit(OpCodes.Call, capacity); il.Emit(OpCodes.Blt, resourceHasRoom); il.Emit(OpCodes.Br, resourceDone);
            il.Append(resourceHasRoom); il.Emit(OpCodes.Ldloc, resourceBag); il.Emit(OpCodes.Ldfld, resources); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Callvirt, backpackResourceAdd);
            il.Emit(OpCodes.Ldarga, overflowResource.Parameters[0]); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Stfld, resourceAmount);
            il.Append(resourceDone); il.Emit(OpCodes.Ret);

            MethodDefinition addResourceCrate = personal.Methods.Single(m => m.Name == "resourceInventory_AddCrate");
            MethodDefinition removeResourceCrate = personal.Methods.Single(m => m.Name == "resourceInventory_RemoveCrate");
            MethodDefinition storeResource = new MethodDefinition("RiftRevivalStoreResourceInBackpack", Mono.Cecil.MethodAttributes.Public, module.TypeSystem.Boolean);
            storeResource.Parameters.Add(new ParameterDefinition("slot", Mono.Cecil.ParameterAttributes.None, module.TypeSystem.Int32));
            VariableDefinition storeResourceBag = new VariableDefinition(equipmentTool); VariableDefinition storeCrate = new VariableDefinition(resourceCrate);
            storeResource.Body.Variables.Add(storeResourceBag); storeResource.Body.Variables.Add(storeCrate); storeResource.Body.InitLocals = true; player.Methods.Add(storeResource); il = storeResource.Body.GetILProcessor();
            Instruction srHasBag = Instruction.Create(OpCodes.Nop), srHasRoom = Instruction.Create(OpCodes.Nop), srInvalid = Instruction.Create(OpCodes.Nop), srReady = Instruction.Create(OpCodes.Nop);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, getPersonal); il.Emit(OpCodes.Ldfld, equipped); il.Emit(OpCodes.Stloc, storeResourceBag); il.Emit(OpCodes.Ldloc, storeResourceBag); il.Emit(OpCodes.Brtrue, srHasBag); EmitResult(il, finishAction, false, "Equip a backpack before storing resources."); il.Append(srHasBag);
            il.Emit(OpCodes.Ldloc, storeResourceBag); il.Emit(OpCodes.Call, itemCount); il.Emit(OpCodes.Ldloc, storeResourceBag); il.Emit(OpCodes.Call, capacity); il.Emit(OpCodes.Blt, srHasRoom); EmitResult(il, finishAction, false, "The backpack is full."); il.Append(srHasRoom);
            il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Blt, srInvalid); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, getPersonal); il.Emit(OpCodes.Ldfld, resourceInventory); il.Emit(OpCodes.Callvirt, resourceCount); il.Emit(OpCodes.Blt, srReady); il.Append(srInvalid); EmitResult(il, finishAction, false, "That resource-inventory slot is empty."); il.Append(srReady);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, getPersonal); il.Emit(OpCodes.Ldfld, resourceInventory); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Callvirt, backpackResourceItem); il.Emit(OpCodes.Stloc, storeCrate);
            il.Emit(OpCodes.Ldloc, storeResourceBag); il.Emit(OpCodes.Ldfld, resources); il.Emit(OpCodes.Ldloc, storeCrate); il.Emit(OpCodes.Callvirt, backpackResourceAdd);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, getPersonal); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Call, removeResourceCrate); EmitResult(il, finishAction, true, "Resource crate stored in backpack.");

            MethodDefinition retrieveResource = new MethodDefinition("RiftRevivalRetrieveResourceFromBackpack", Mono.Cecil.MethodAttributes.Public, module.TypeSystem.Boolean);
            retrieveResource.Parameters.Add(new ParameterDefinition("slot", Mono.Cecil.ParameterAttributes.None, module.TypeSystem.Int32));
            VariableDefinition retrieveResourceBag = new VariableDefinition(equipmentTool); VariableDefinition retrieveCrate = new VariableDefinition(resourceCrate);
            retrieveResource.Body.Variables.Add(retrieveResourceBag); retrieveResource.Body.Variables.Add(retrieveCrate); retrieveResource.Body.InitLocals = true; player.Methods.Add(retrieveResource); il = retrieveResource.Body.GetILProcessor();
            Instruction rrHasBag = Instruction.Create(OpCodes.Nop), rrHasRoom = Instruction.Create(OpCodes.Nop), rrInvalid = Instruction.Create(OpCodes.Nop), rrReady = Instruction.Create(OpCodes.Nop);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, getPersonal); il.Emit(OpCodes.Ldfld, equipped); il.Emit(OpCodes.Stloc, retrieveResourceBag); il.Emit(OpCodes.Ldloc, retrieveResourceBag); il.Emit(OpCodes.Brtrue, rrHasBag); EmitResult(il, finishAction, false, "No backpack is equipped."); il.Append(rrHasBag);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, getPersonal); il.Emit(OpCodes.Ldfld, resourceInventory); il.Emit(OpCodes.Callvirt, resourceCount); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, getPersonal); il.Emit(OpCodes.Ldfld, personal.Fields.Single(f => f.Name == "ResourceInventoryMax")); il.Emit(OpCodes.Blt, rrHasRoom); EmitResult(il, finishAction, false, "The resource inventory is full."); il.Append(rrHasRoom);
            il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Blt, rrInvalid); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldloc, retrieveResourceBag); il.Emit(OpCodes.Ldfld, resources); il.Emit(OpCodes.Callvirt, resourceCount); il.Emit(OpCodes.Blt, rrReady); il.Append(rrInvalid); EmitResult(il, finishAction, false, "That backpack resource slot is empty."); il.Append(rrReady);
            il.Emit(OpCodes.Ldloc, retrieveResourceBag); il.Emit(OpCodes.Ldfld, resources); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Callvirt, backpackResourceItem); il.Emit(OpCodes.Stloc, retrieveCrate);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, getPersonal); il.Emit(OpCodes.Ldloc, retrieveCrate); il.Emit(OpCodes.Call, addResourceCrate);
            il.Emit(OpCodes.Ldloc, retrieveResourceBag); il.Emit(OpCodes.Ldfld, resources); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Callvirt, collectionRemoveAt); EmitResult(il, finishAction, true, "Resource crate moved to the resource inventory.");

            MethodDefinition addOrMergeResource = personal.Methods.Single(m => m.Name == "sv_resourceInventory_AddOrMergeCrate");
            Instruction finalResourceLoad = addOrMergeResource.Body.Instructions.Last(i => i.OpCode == OpCodes.Ldarg_3);
            ILProcessor resourceOverflowIl = addOrMergeResource.Body.GetILProcessor();
            finalResourceLoad.OpCode = OpCodes.Ldarg_2;
            resourceOverflowIl.InsertAfter(finalResourceLoad, Instruction.Create(OpCodes.Ldarg_3));
            resourceOverflowIl.InsertAfter(finalResourceLoad.Next, Instruction.Create(OpCodes.Call, overflowResource));

            // Preserve the stock hotbar-first rule. Only ordinary acquisitions
            // (selectTool=true) overflow; internal equipment transfers continue
            // using their existing explicit capacity checks.
            Instruction stockFullReturn = addTool.Body.Instructions.First(i => i.OpCode == OpCodes.Ldc_I4_0 && i.Next != null && i.Next.OpCode == OpCodes.Ret);
            ILProcessor addToolIl = addTool.Body.GetILProcessor();
            addToolIl.InsertBefore(stockFullReturn, Instruction.Create(OpCodes.Ldarg_2));
            addToolIl.InsertBefore(stockFullReturn, Instruction.Create(OpCodes.Brfalse, stockFullReturn));
            addToolIl.InsertBefore(stockFullReturn, Instruction.Create(OpCodes.Ldarg_0));
            addToolIl.InsertBefore(stockFullReturn, Instruction.Create(OpCodes.Ldarg_1));
            addToolIl.InsertBefore(stockFullReturn, Instruction.Create(OpCodes.Call, overflowTool));
            addToolIl.InsertBefore(stockFullReturn, Instruction.Create(OpCodes.Brfalse, stockFullReturn));
            addToolIl.InsertBefore(stockFullReturn, Instruction.Create(OpCodes.Ldc_I4_1));
            addToolIl.InsertBefore(stockFullReturn, Instruction.Create(OpCodes.Ret));

            MethodDefinition killPlayer = player.Methods.Single(m => m.Name == "Kill");
            TypeReference deathToolsType = killPlayer.Body.Variables[3].VariableType;
            MethodReference deathToolsAdd = killPlayer.Body.Instructions.Select(i => i.Operand as MethodReference).First(r => r != null && r.Name == "Add" && r.DeclaringType.FullName.StartsWith("System.Collections.Generic.List`1<Game.ClientServer.Classes.Tools.ClSvPlayerTool>"));
            MethodDefinition dropBackpack = new MethodDefinition("RiftRevivalDropBackpackOnDeath", Mono.Cecil.MethodAttributes.Public, module.TypeSystem.Void);
            dropBackpack.Parameters.Add(new ParameterDefinition("tools", Mono.Cecil.ParameterAttributes.None, deathToolsType)); player.Methods.Add(dropBackpack); il = dropBackpack.Body.GetILProcessor();
            Instruction noDeathBackpack = Instruction.Create(OpCodes.Ret);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, getPersonal); il.Emit(OpCodes.Ldfld, equipped); il.Emit(OpCodes.Brfalse, noDeathBackpack);
            il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, getPersonal); il.Emit(OpCodes.Ldfld, equipped); il.Emit(OpCodes.Callvirt, deathToolsAdd);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, getPersonal); il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Stfld, equipped); il.Append(noDeathBackpack);
            Instruction inventoryToolsCaptured = killPlayer.Body.Instructions.First(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "AddRange" && ((MethodReference)i.Operand).DeclaringType.FullName.StartsWith("System.Collections.Generic.List`1<Game.ClientServer.Classes.Tools.ClSvPlayerTool>"));
            Instruction afterInventoryToolsCaptured = inventoryToolsCaptured.Next; ILProcessor killIl = killPlayer.Body.GetILProcessor();
            killIl.InsertBefore(afterInventoryToolsCaptured, Instruction.Create(OpCodes.Ldarg_0)); killIl.InsertBefore(afterInventoryToolsCaptured, Instruction.Create(OpCodes.Ldloc_3)); killIl.InsertBefore(afterInventoryToolsCaptured, Instruction.Create(OpCodes.Call, dropBackpack));
            // The added call can push an existing short death-path branch past
            // its signed-byte range. Expand branches in this method so the
            // backpack-only build remains valid as well as the glove+backpack
            // build (the glove patch happened to expand these independently).
            foreach (Instruction branch in killPlayer.Body.Instructions)
            {
                if (branch.OpCode == OpCodes.Br_S) branch.OpCode = OpCodes.Br;
                else if (branch.OpCode == OpCodes.Brfalse_S) branch.OpCode = OpCodes.Brfalse;
                else if (branch.OpCode == OpCodes.Brtrue_S) branch.OpCode = OpCodes.Brtrue;
                else if (branch.OpCode == OpCodes.Beq_S) branch.OpCode = OpCodes.Beq;
                else if (branch.OpCode == OpCodes.Bge_S) branch.OpCode = OpCodes.Bge;
                else if (branch.OpCode == OpCodes.Bge_Un_S) branch.OpCode = OpCodes.Bge_Un;
                else if (branch.OpCode == OpCodes.Bgt_S) branch.OpCode = OpCodes.Bgt;
                else if (branch.OpCode == OpCodes.Bgt_Un_S) branch.OpCode = OpCodes.Bgt_Un;
                else if (branch.OpCode == OpCodes.Ble_S) branch.OpCode = OpCodes.Ble;
                else if (branch.OpCode == OpCodes.Ble_Un_S) branch.OpCode = OpCodes.Ble_Un;
                else if (branch.OpCode == OpCodes.Blt_S) branch.OpCode = OpCodes.Blt;
                else if (branch.OpCode == OpCodes.Blt_Un_S) branch.OpCode = OpCodes.Blt_Un;
                else if (branch.OpCode == OpCodes.Bne_Un_S) branch.OpCode = OpCodes.Bne_Un;
                else if (branch.OpCode == OpCodes.Leave_S) branch.OpCode = OpCodes.Leave;
            }

            // Reuse the established power-glove transport, reserving commands
            // 16-19 after the battery feature's 13-15 commands. A distinct tuple
            // type keeps backpack replies separate from battery state packets.
            TypeDefinition glove = types.Single(t => t.FullName == "Game.ClientServer.Classes.Tools.ClSvPlayerTool_PowerGlove");
            TypeDefinition packetType = types.Single(t => t.FullName == "Game.ClientServer.Packets.ServerPlayerToolState");
            TypeDefinition resultTupleType = types.Single(t => t.FullName == "Game.ClientServer.Packets.NetTupleIntString");
            FieldDefinition packetToolId = packetType.Fields.Single(f => f.Name == "toolID");
            FieldDefinition packetData = packetType.Fields.Single(f => f.Name == "Data");
            FieldDefinition resultCode = resultTupleType.Fields.Single(f => f.Name == "Item1");
            FieldDefinition resultText = resultTupleType.Fields.Single(f => f.Name == "Item2");
            FieldDefinition connectionId = player.Fields.Single(f => f.Name == "ConnectionId");
            MethodReference serverNetwork = references.First(r => r.FullName == "Game.Server.NetworkController Game.Server.ControllerManager::get_Network()");
            MethodReference serverNet = references.First(r => r.FullName == "Game.Networking.NetServer Game.Server.NetworkController::get_Net()");
            MethodReference sendRpc = references.First(r => r.FullName == "System.Void Game.Networking.NetClientServer::SendRPC(System.Object,System.Int64,Game.Networking.NetWrapOrdering,System.Byte)");

            MethodDefinition sendResult = new MethodDefinition("RiftRevivalSendBackpackResult", Mono.Cecil.MethodAttributes.Public, module.TypeSystem.Void);
            sendResult.Parameters.Add(new ParameterDefinition("controllers", Mono.Cecil.ParameterAttributes.None, types.Single(t => t.FullName == "Game.Server.ControllerManager")));
            sendResult.Parameters.Add(new ParameterDefinition("player", Mono.Cecil.ParameterAttributes.None, player));
            VariableDefinition resultTuple = new VariableDefinition(resultTupleType); VariableDefinition resultPacket = new VariableDefinition(packetType);
            sendResult.Body.Variables.Add(resultTuple); sendResult.Body.Variables.Add(resultPacket); glove.Methods.Add(sendResult); il = sendResult.Body.GetILProcessor();
            il.Emit(OpCodes.Ldloca, resultTuple); il.Emit(OpCodes.Initobj, resultTupleType);
            il.Emit(OpCodes.Ldloca, resultTuple); il.Emit(OpCodes.Ldc_I4, -200); il.Emit(OpCodes.Stfld, resultCode);
            il.Emit(OpCodes.Ldloca, resultTuple); il.Emit(OpCodes.Ldarg_2); il.Emit(OpCodes.Call, getPersonal); il.Emit(OpCodes.Ldfld, result); il.Emit(OpCodes.Stfld, resultText);
            il.Emit(OpCodes.Ldloca, resultPacket); il.Emit(OpCodes.Initobj, packetType);
            il.Emit(OpCodes.Ldloca, resultPacket); il.Emit(OpCodes.Ldc_I4_M1); il.Emit(OpCodes.Stfld, packetToolId);
            il.Emit(OpCodes.Ldloca, resultPacket); il.Emit(OpCodes.Ldloc, resultTuple); il.Emit(OpCodes.Box, resultTupleType); il.Emit(OpCodes.Stfld, packetData);
            il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Callvirt, serverNetwork); il.Emit(OpCodes.Callvirt, serverNet); il.Emit(OpCodes.Ldloc, resultPacket); il.Emit(OpCodes.Box, packetType); il.Emit(OpCodes.Ldarg_2); il.Emit(OpCodes.Ldfld, connectionId); il.Emit(OpCodes.Ldc_I4_S, (sbyte)67); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Callvirt, sendRpc); il.Emit(OpCodes.Ret);

            MethodDefinition receiveResult = new MethodDefinition("RiftRevivalReceiveBackpackResult", Mono.Cecil.MethodAttributes.Public, module.TypeSystem.Boolean);
            receiveResult.Parameters.Add(new ParameterDefinition("controllers", Mono.Cecil.ParameterAttributes.None, types.Single(t => t.FullName == "Game.Client.ControllerManager")));
            receiveResult.Parameters.Add(new ParameterDefinition("data", Mono.Cecil.ParameterAttributes.None, module.TypeSystem.Object));
            VariableDefinition boxedResult = new VariableDefinition(module.TypeSystem.Object); VariableDefinition clientTuple = new VariableDefinition(resultTupleType);
            receiveResult.Body.Variables.Add(boxedResult); receiveResult.Body.Variables.Add(clientTuple); glove.Methods.Add(receiveResult); il = receiveResult.Body.GetILProcessor();
            Instruction resultReceived = Instruction.Create(OpCodes.Nop), resultUnhandled = Instruction.Create(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldarg_2); il.Emit(OpCodes.Isinst, resultTupleType); il.Emit(OpCodes.Stloc, boxedResult); il.Emit(OpCodes.Ldloc, boxedResult); il.Emit(OpCodes.Brtrue, resultReceived); il.Append(resultUnhandled); il.Emit(OpCodes.Ret);
            il.Append(resultReceived); il.Emit(OpCodes.Ldloc, boxedResult); il.Emit(OpCodes.Unbox_Any, resultTupleType); il.Emit(OpCodes.Stloc, clientTuple);
            il.Emit(OpCodes.Ldloca, clientTuple); il.Emit(OpCodes.Ldfld, resultCode); il.Emit(OpCodes.Ldc_I4, -200); il.Emit(OpCodes.Bne_Un, resultUnhandled);
            MethodReference clientPlayer = references.First(r => r.FullName == "Game.Client.PlayerController Game.Client.ControllerManager::get_Player()");
            MethodReference personalState = references.First(r => r.FullName == "Game.ClientServer.Classes.ClSvPlayerState_Personal Game.Client.PlayerController::get_PersonalState()");
            il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Callvirt, clientPlayer); il.Emit(OpCodes.Callvirt, personalState); il.Emit(OpCodes.Ldloca, clientTuple); il.Emit(OpCodes.Ldfld, resultText); il.Emit(OpCodes.Stfld, result); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Ret);

            MethodDefinition receiveServerState = glove.Methods.Single(m => m.Name == "ReceiveServerState");
            Instruction receiveFirst = receiveServerState.Body.Instructions.First(); ILProcessor receiveIl = receiveServerState.Body.GetILProcessor();
            receiveIl.InsertBefore(receiveFirst, Instruction.Create(OpCodes.Ldarg_0)); receiveIl.InsertBefore(receiveFirst, Instruction.Create(OpCodes.Ldarg_1)); receiveIl.InsertBefore(receiveFirst, Instruction.Create(OpCodes.Ldarg_2)); receiveIl.InsertBefore(receiveFirst, Instruction.Create(OpCodes.Call, receiveResult)); receiveIl.InsertBefore(receiveFirst, Instruction.Create(OpCodes.Brfalse, receiveFirst)); receiveIl.InsertBefore(receiveFirst, Instruction.Create(OpCodes.Ret));

            MethodDefinition serverData = glove.Methods.Single(m => m.Name == "Server_OnData"); Instruction serverSwitch = serverData.Body.Instructions.Single(i => i.OpCode == OpCodes.Switch);
            Instruction[] oldTargets = (Instruction[])serverSwitch.Operand;
            if (oldTargets.Length != 13 && oldTargets.Length != 16) throw new InvalidDataException("Power-glove command table changed before backpack commands.");
            ILProcessor serverIl = serverData.Body.GetILProcessor();
            Instruction equipHandler = AppendBackpackSlotHandler(serverIl, serverData, equipBackpack, sendResult, module);
            Instruction removeHandler = Instruction.Create(OpCodes.Ldloc_0); serverIl.Append(removeHandler); serverIl.Emit(OpCodes.Call, removeBackpack); serverIl.Emit(OpCodes.Pop); serverIl.Emit(OpCodes.Ldarg_0); serverIl.Emit(OpCodes.Ldarg_1); serverIl.Emit(OpCodes.Ldloc_0); serverIl.Emit(OpCodes.Call, sendResult); serverIl.Emit(OpCodes.Ret);
            Instruction storeHandler = AppendBackpackSlotHandler(serverIl, serverData, storeTool, sendResult, module);
            Instruction retrieveHandler = AppendBackpackSlotHandler(serverIl, serverData, retrieveTool, sendResult, module);
            Instruction retrieveResourceHandler = AppendBackpackSlotHandler(serverIl, serverData, retrieveResource, sendResult, module);
            Instruction storeResourceHandler = AppendBackpackSlotHandler(serverIl, serverData, storeResource, sendResult, module);
            Instruction[] expandedTargets = new Instruction[22];
            Array.Copy(oldTargets, expandedTargets, oldTargets.Length);
            Instruction unused = oldTargets[0]; for (int command = oldTargets.Length; command < expandedTargets.Length; command++) expandedTargets[command] = unused;
            expandedTargets[16] = equipHandler; expandedTargets[17] = removeHandler; expandedTargets[18] = storeHandler; expandedTargets[19] = retrieveHandler; expandedTargets[20] = retrieveResourceHandler; expandedTargets[21] = storeResourceHandler; serverSwitch.Operand = expandedTargets;

            TypeDefinition helpers = types.Single(t => t.FullName == "Game.ClientServer.Classes.Tools.PlayerEquipmentHelpers");
            TypeDefinition mountPoint = types.Single(t => t.FullName == "Game.ClientServer.Classes.Tools.PlayerEquipmentHelpers/PlayerEquipmentMountPoint");
            TypeDefinition equipmentType = types.Single(t => t.FullName == "Game.ClientServer.Classes.Tools.PlayerEquipmentHelpers/PlayerEquipmentType");
            mountPoint.Fields.Add(new FieldDefinition("PEMP_Backpack", Mono.Cecil.FieldAttributes.Public | Mono.Cecil.FieldAttributes.Static | Mono.Cecil.FieldAttributes.Literal | Mono.Cecil.FieldAttributes.HasDefault, mountPoint) { Constant = 11 });
            for (int index = 0; index < 4; index++)
                equipmentType.Fields.Add(new FieldDefinition("PET_BackpackTier" + index, Mono.Cecil.FieldAttributes.Public | Mono.Cecil.FieldAttributes.Static | Mono.Cecil.FieldAttributes.Literal | Mono.Cecil.FieldAttributes.HasDefault, equipmentType) { Constant = 16 + index });

            MethodDefinition helperCtor = helpers.Methods.Single(m => m.IsConstructor && m.IsStatic);
            FieldDefinition statsDictionary = helpers.Fields.Single(f => f.Name == "s_playerEquipmentStats");
            FieldDefinition propertiesDictionary = helpers.Fields.Single(f => f.Name == "s_playerEquipmentProperties");
            FieldDefinition recipes = helpers.Fields.Single(f => f.Name == "s_playerEquipmentRecipes");
            MethodReference tupleCtor = helperCtor.Body.Instructions.Select(i => i.Operand as MethodReference).First(r => r != null && r.Name == ".ctor" && r.DeclaringType.FullName.StartsWith("System.Tuple`2<Game.ClientServer.Classes.Tools.PlayerEquipmentHelpers/PlayerEquipmentMountPoint"));
            MethodReference statsAdd = helperCtor.Body.Instructions.Select(i => i.Operand as MethodReference).First(r => r != null && r.Name == "Add" && r.DeclaringType.FullName == statsDictionary.FieldType.FullName);
            MethodReference propertiesAdd = helperCtor.Body.Instructions.Select(i => i.Operand as MethodReference).First(r => r != null && r.Name == "Add" && r.DeclaringType.FullName == propertiesDictionary.FieldType.FullName);
            MethodReference resourceDictCtor = helperCtor.Body.Instructions.Select(i => i.Operand as MethodReference).First(r => r != null && r.Name == ".ctor" && r.DeclaringType.FullName.StartsWith("System.Collections.Generic.Dictionary`2<Game.ClientServer.Classes.Economics.ResourceTypes,System.Int32>"));
            MethodReference resourceAdd = helperCtor.Body.Instructions.Select(i => i.Operand as MethodReference).First(r => r != null && r.Name == "Add" && r.DeclaringType.FullName.StartsWith("System.Collections.Generic.Dictionary`2<Game.ClientServer.Classes.Economics.ResourceTypes,System.Int32>"));
            MethodReference recipeCtor = helperCtor.Body.Instructions.Select(i => i.Operand as MethodReference).First(r => r != null && r.Name == ".ctor" && r.DeclaringType.FullName == "Game.ClientServer.Classes.Tools.ToolRecipe");
            MethodReference recipeAdd = helperCtor.Body.Instructions.Select(i => i.Operand as MethodReference).First(r => r != null && r.Name == "Add" && r.DeclaringType.FullName == recipes.FieldType.FullName);
            MethodDefinition addRecipes = new MethodDefinition("RiftRevivalAddBackpackRecipes", Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Static, module.TypeSystem.Void);
            helpers.Methods.Add(addRecipes); il = addRecipes.Body.GetILProcessor();
            int[][] resourceIds = { new[] { 20, 18, 27 }, new[] { 20, 19, 26, 24 }, new[] { 60, 56, 25, 28 }, new[] { 94, 77, 29, 102 } };
            int[][] resourceAmounts = { new[] { 50, 30, 15 }, new[] { 50, 40, 30, 20 }, new[] { 50, 40, 30, 30 }, new[] { 50, 40, 35, 35 } };
            for (int backpackTier = 0; backpackTier < 4; backpackTier++)
            {
                il.Emit(OpCodes.Ldsfld, recipes);
                il.Emit(OpCodes.Ldc_I4, 11); il.Emit(OpCodes.Ldc_I4, 16 + backpackTier); il.Emit(OpCodes.Newobj, tupleCtor);
                il.Emit(OpCodes.Ldc_I4, 1000); il.Emit(OpCodes.Ldc_I4, 25); il.Emit(OpCodes.Newobj, resourceDictCtor);
                for (int ingredient = 0; ingredient < resourceIds[backpackTier].Length; ingredient++)
                {
                    il.Emit(OpCodes.Dup); il.Emit(OpCodes.Ldc_I4, resourceIds[backpackTier][ingredient]); il.Emit(OpCodes.Ldc_I4, resourceAmounts[backpackTier][ingredient]); il.Emit(OpCodes.Callvirt, resourceAdd);
                }
                il.Emit(OpCodes.Ldc_I4, 11); il.Emit(OpCodes.Ldc_I4, 16 + backpackTier); il.Emit(OpCodes.Newobj, recipeCtor);
                il.Emit(OpCodes.Callvirt, recipeAdd);
            }
            il.Emit(OpCodes.Ret);
            Instruction helperRet = helperCtor.Body.Instructions.Last(i => i.OpCode == OpCodes.Ret);
            helperCtor.Body.GetILProcessor().InsertBefore(helperRet, Instruction.Create(OpCodes.Call, addRecipes));

            TypeDefinition statsType = types.Single(t => t.FullName == "Game.ClientServer.Classes.Tools.PlayerEquipmentHelpers/PlayerEquipmentStats");
            FieldDefinition statsIcon = statsType.Fields.Single(f => f.Name == "IconCode");
            FieldDefinition statsColor = statsType.Fields.Single(f => f.Name == "IconColorString");
            FieldDefinition statsName = statsType.Fields.Single(f => f.Name == "Name");
            FieldDefinition statsScenes = statsType.Fields.Single(f => f.Name == "EquipmentScenes");
            FieldDefinition statsSceneHand = statsType.Fields.Single(f => f.Name == "EquipmentSceneHand");
            FieldDefinition statsScenePrint = statsType.Fields.Single(f => f.Name == "EquipmentScenePrint");
            MethodReference scenesCtor = helperCtor.Body.Instructions.Select(i => i.Operand as MethodReference).First(r => r != null && r.Name == ".ctor" && r.DeclaringType.FullName.StartsWith("System.Collections.Generic.List`1<Game.ClientServer.Classes.Tools.PlayerEquipmentHelpers/PlayerEquipmentScene>"));
            MethodReference effectsCtor = helperCtor.Body.Instructions.Select(i => i.Operand as MethodReference).First(r => r != null && r.Name == ".ctor" && r.DeclaringType.FullName.StartsWith("System.Collections.Generic.Dictionary`2<Game.ClientServer.Classes.Tools.PlayerEquipmentHelpers/PlayerEquipmentEffectType"));
            MethodDefinition addMetadata = new MethodDefinition("RiftRevivalAddBackpackMetadata", Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Static, module.TypeSystem.Void);
            VariableDefinition statsValue = new VariableDefinition(statsType); VariableDefinition propertiesValue = new VariableDefinition(propertiesType);
            addMetadata.Body.Variables.Add(statsValue); addMetadata.Body.Variables.Add(propertiesValue); helpers.Methods.Add(addMetadata); il = addMetadata.Body.GetILProcessor();
            for (int backpackTier = 0; backpackTier < 4; backpackTier++)
            {
                il.Emit(OpCodes.Ldsfld, statsDictionary); il.Emit(OpCodes.Ldc_I4, 11); il.Emit(OpCodes.Ldc_I4, 16 + backpackTier); il.Emit(OpCodes.Newobj, tupleCtor);
                il.Emit(OpCodes.Ldloca, statsValue); il.Emit(OpCodes.Initobj, statsType);
                il.Emit(OpCodes.Ldloca, statsValue); il.Emit(OpCodes.Ldstr, "⟅"); il.Emit(OpCodes.Stfld, statsIcon);
                il.Emit(OpCodes.Ldloca, statsValue); il.Emit(OpCodes.Ldstr, backpackTier == 0 ? "FFFFFF" : backpackTier == 1 ? "44FF44" : backpackTier == 2 ? "4488FF" : "AA55FF"); il.Emit(OpCodes.Stfld, statsColor);
                il.Emit(OpCodes.Ldloca, statsValue); il.Emit(OpCodes.Ldstr, "Backpack Tier " + backpackTier); il.Emit(OpCodes.Stfld, statsName);
                il.Emit(OpCodes.Ldloca, statsValue); il.Emit(OpCodes.Newobj, scenesCtor); il.Emit(OpCodes.Stfld, statsScenes);
                // Reuse the game's neutral chest equipment container for the
                // printer tray and held pickup. Backpacks deliberately keep an
                // empty worn-scene list so they do not occupy an armour slot or
                // draw armour geometry on the character.
                il.Emit(OpCodes.Ldloca, statsValue); il.Emit(OpCodes.Ldstr, "Scenes/ArmorContainerScenes/ArmorContainer_Chest_FirstPerson_01.mfs"); il.Emit(OpCodes.Stfld, statsSceneHand);
                il.Emit(OpCodes.Ldloca, statsValue); il.Emit(OpCodes.Ldstr, "Scenes/ArmorContainerScenes/ArmorContainer_Chest_01.mfs"); il.Emit(OpCodes.Stfld, statsScenePrint);
                il.Emit(OpCodes.Ldloc, statsValue); il.Emit(OpCodes.Callvirt, statsAdd);

                il.Emit(OpCodes.Ldsfld, propertiesDictionary); il.Emit(OpCodes.Ldc_I4, 11); il.Emit(OpCodes.Ldc_I4, 16 + backpackTier); il.Emit(OpCodes.Newobj, tupleCtor);
                il.Emit(OpCodes.Ldloca, propertiesValue); il.Emit(OpCodes.Initobj, propertiesType);
                il.Emit(OpCodes.Ldloca, propertiesValue); il.Emit(OpCodes.Ldc_I4, 11); il.Emit(OpCodes.Stfld, propertyMount);
                il.Emit(OpCodes.Ldloca, propertiesValue); il.Emit(OpCodes.Ldc_I4, 16 + backpackTier); il.Emit(OpCodes.Stfld, propertyType);
                il.Emit(OpCodes.Ldloca, propertiesValue); il.Emit(OpCodes.Newobj, effectsCtor); il.Emit(OpCodes.Stfld, propertiesType.Fields.Single(f => f.Name == "EquipmentEffects"));
                il.Emit(OpCodes.Ldloc, propertiesValue); il.Emit(OpCodes.Callvirt, propertiesAdd);
            }
            il.Emit(OpCodes.Ret);
            helperCtor.Body.GetILProcessor().InsertBefore(helperRet, Instruction.Create(OpCodes.Call, addMetadata));

            // Four legal printer ingredients fit the device, but the stock
            // cost mirror is slightly too far right to display the final count.
            // Move that mirror left by forty screen pixels at the game's 0.5 UI
            // scale, leaving the recipe name and all other printer geometry in
            // place.
            TypeDefinition toolFactoryScreen = types.Single(t => t.FullName == "Game.Client.AgosStateToolFactory");
            MethodDefinition toolFactoryCtor = toolFactoryScreen.Methods.Single(m => m.IsConstructor);
            Instruction recipeCostOffset = toolFactoryCtor.Body.Instructions.Single(i => i.OpCode == OpCodes.Ldc_R4 && Convert.ToSingle(i.Operand) == 1040f);
            recipeCostOffset.Operand = 960f;

            // Add the backpack equipment rows using the same two stock lists used
            // by armour. The storage grid is added separately on the right.
            TypeDefinition equipmentScreen = types.Single(t => t.FullName == "Game.Client.AgosStateCharacterCustomizationMenu");
            TypeDefinition baseState = types.Single(t => t.FullName == "Game.Client.BaseAgosState");
            FieldDefinition controllersField = baseState.Fields.Single(f => f.Name == "m_controllers");
            FieldDefinition equippedListBox = equipmentScreen.Fields.Single(f => f.Name == "m_equippedList");
            FieldDefinition unequippedListBox = equipmentScreen.Fields.Single(f => f.Name == "m_unequippedList");
            FieldDefinition equippedItems = equipmentScreen.Fields.Single(f => f.Name == "m_equippedItemsList");
            FieldDefinition unequippedItems = equipmentScreen.Fields.Single(f => f.Name == "m_unequippedItemsList");
            TypeReference intListType = module.ImportReference(typeof(System.Collections.Generic.List<int>));
            FieldDefinition backpackSlots = new FieldDefinition("RiftRevivalBackpackSlots", Mono.Cecil.FieldAttributes.Private, intListType);
            FieldDefinition equippedRow = new FieldDefinition("RiftRevivalBackpackEquippedRow", Mono.Cecil.FieldAttributes.Private, module.TypeSystem.Int32);
            TypeDefinition encyclopediaItem = types.Single(t => t.FullName == "Game.Client.EncyclopediaItem");
            TypeDefinition encyclopediaTool = types.Single(t => t.FullName == "Game.Client.EncyclopediaItem_Tool");
            TypeDefinition encyclopediaResource = types.Single(t => t.FullName == "Game.Client.EncyclopediaItem_Resource");
            TypeDefinition encyclopediaWindow = types.Single(t => t.FullName == "Game.Client.EncyclopediaItemWindow");
            FieldDefinition encyclopediaSlot = new FieldDefinition("RiftRevivalBackpackSlot", Mono.Cecil.FieldAttributes.Public, module.TypeSystem.Int32); encyclopediaItem.Fields.Add(encyclopediaSlot);
            GenericInstanceType windowListType = new GenericInstanceType(module.ImportReference(typeof(System.Collections.Generic.List<>))); windowListType.GenericArguments.Add(encyclopediaWindow);
            FieldDefinition backpackWindows = new FieldDefinition("RiftRevivalBackpackWindows", Mono.Cecil.FieldAttributes.Private, windowListType);
            FieldDefinition gridCount = new FieldDefinition("RiftRevivalBackpackGridCount", Mono.Cecil.FieldAttributes.Private, module.TypeSystem.Int32);
            FieldDefinition gridTier = new FieldDefinition("RiftRevivalBackpackGridTier", Mono.Cecil.FieldAttributes.Private, module.TypeSystem.Int32);
            equipmentScreen.Fields.Add(backpackSlots); equipmentScreen.Fields.Add(equippedRow); equipmentScreen.Fields.Add(backpackWindows); equipmentScreen.Fields.Add(gridCount); equipmentScreen.Fields.Add(gridTier);
            MethodReference intListCtor = module.ImportReference(typeof(System.Collections.Generic.List<int>).GetConstructor(Type.EmptyTypes));
            MethodReference intListClear = module.ImportReference(typeof(System.Collections.Generic.List<int>).GetMethod("Clear"));
            MethodReference intListAdd = module.ImportReference(typeof(System.Collections.Generic.List<int>).GetMethod("Add"));
            MethodReference intListCount = module.ImportReference(typeof(System.Collections.Generic.List<int>).GetProperty("Count").GetGetMethod());
            MethodReference intListItem = module.ImportReference(typeof(System.Collections.Generic.List<int>).GetProperty("Item").GetGetMethod());
            MethodReference windowListCtor = module.ImportReference(typeof(System.Collections.Generic.List<>).GetConstructor(Type.EmptyTypes)); windowListCtor.DeclaringType = windowListType;
            MethodReference windowListAdd = module.ImportReference(typeof(System.Collections.Generic.List<>).GetMethod("Add")); windowListAdd.DeclaringType = windowListType;
            MethodReference windowListCount = module.ImportReference(typeof(System.Collections.Generic.List<>).GetProperty("Count").GetGetMethod()); windowListCount.DeclaringType = windowListType;
            MethodReference windowListItem = module.ImportReference(typeof(System.Collections.Generic.List<>).GetProperty("Item").GetGetMethod()); windowListItem.DeclaringType = windowListType;
            MethodReference windowListClear = module.ImportReference(typeof(System.Collections.Generic.List<>).GetMethod("Clear")); windowListClear.DeclaringType = windowListType;
            MethodDefinition fillLists = equipmentScreen.Methods.Single(m => m.Name == "i_fillLists");
            MethodReference listBoxItems = fillLists.Body.Instructions.Select(i => i.Operand as MethodReference).First(r => r != null && r.Name == "get_Items" && r.DeclaringType.FullName == "Game.Framework.CGuiListBox");
            MethodReference stringCollectionAdd = fillLists.Body.Instructions.Select(i => i.Operand as MethodReference).First(r => r != null && r.Name == "Add" && r.DeclaringType.FullName.StartsWith("System.Collections.ObjectModel.Collection`1<System.String>"));
            MethodReference stringCollectionCount = new MethodReference("get_Count", module.TypeSystem.Int32, listBoxItems.ReturnType) { HasThis = true };
            MethodReference equipmentListCount = equipmentScreen.Methods.Single(m => m.Name == "OnHandleButtonEvents").Body.Instructions.Select(i => i.Operand as MethodReference).First(r => r != null && r.Name == "get_Count" && r.DeclaringType.FullName.StartsWith("System.Collections.Generic.List`1<Game.Client.AgosStateCharacterCustomizationMenu/EquippableItem>"));
            MethodReference equipmentListItem = equipmentScreen.Methods.Single(m => m.Name == "OnHandleButtonEvents").Body.Instructions.Select(i => i.Operand as MethodReference).First(r => r != null && r.Name == "get_Item" && r.DeclaringType.FullName.StartsWith("System.Collections.Generic.List`1<Game.Client.AgosStateCharacterCustomizationMenu/EquippableItem>"));
            MethodReference selectedIndex = equipmentScreen.Methods.Single(m => m.Name == "OnHandleButtonEvents").Body.Instructions.Select(i => i.Operand as MethodReference).First(r => r != null && r.Name == "get_SelectedIndex");
            TypeDefinition equippableItem = equipmentScreen.NestedTypes.Single(t => t.Name == "EquippableItem");
            FieldDefinition equippableTool = equippableItem.Fields.Single(f => f.Name == "ToolItem");
            MethodReference toolInventoryIndexOf = equipmentScreen.Methods.Single(m => m.Name == "OnHandleButtonEvents").Body.Instructions.Select(i => i.Operand as MethodReference).First(r => r != null && r.Name == "IndexOf" && r.DeclaringType.FullName.StartsWith("System.Collections.ObjectModel.Collection`1<Game.ClientServer.Classes.Tools.ClSvPlayerTool>"));
            FieldDefinition personalGlove = personal.Fields.Single(f => f.Name == "PowerGlove");
            MethodDefinition sendCommand = types.Single(t => t.FullName == "Game.ClientServer.Classes.Tools.ClSvPlayerTool").Methods.Single(m => m.Name == "SendCommand");

            MethodDefinition equipmentCtor = equipmentScreen.Methods.Single(m => m.IsConstructor);
            Instruction equipmentCtorRet = equipmentCtor.Body.Instructions.Last(i => i.OpCode == OpCodes.Ret); ILProcessor equipmentCtorIl = equipmentCtor.Body.GetILProcessor();
            equipmentCtorIl.InsertBefore(equipmentCtorRet, Instruction.Create(OpCodes.Ldarg_0)); equipmentCtorIl.InsertBefore(equipmentCtorRet, Instruction.Create(OpCodes.Newobj, intListCtor)); equipmentCtorIl.InsertBefore(equipmentCtorRet, Instruction.Create(OpCodes.Stfld, backpackSlots));
            equipmentCtorIl.InsertBefore(equipmentCtorRet, Instruction.Create(OpCodes.Ldarg_0)); equipmentCtorIl.InsertBefore(equipmentCtorRet, Instruction.Create(OpCodes.Ldc_I4_M1)); equipmentCtorIl.InsertBefore(equipmentCtorRet, Instruction.Create(OpCodes.Stfld, equippedRow));
            equipmentCtorIl.InsertBefore(equipmentCtorRet, Instruction.Create(OpCodes.Ldarg_0)); equipmentCtorIl.InsertBefore(equipmentCtorRet, Instruction.Create(OpCodes.Newobj, windowListCtor)); equipmentCtorIl.InsertBefore(equipmentCtorRet, Instruction.Create(OpCodes.Stfld, backpackWindows));
            equipmentCtorIl.InsertBefore(equipmentCtorRet, Instruction.Create(OpCodes.Ldarg_0)); equipmentCtorIl.InsertBefore(equipmentCtorRet, Instruction.Create(OpCodes.Ldc_I4_M1)); equipmentCtorIl.InsertBefore(equipmentCtorRet, Instruction.Create(OpCodes.Stfld, gridCount));
            equipmentCtorIl.InsertBefore(equipmentCtorRet, Instruction.Create(OpCodes.Ldarg_0)); equipmentCtorIl.InsertBefore(equipmentCtorRet, Instruction.Create(OpCodes.Ldc_I4_M1)); equipmentCtorIl.InsertBefore(equipmentCtorRet, Instruction.Create(OpCodes.Stfld, gridTier));

            MethodDefinition fillBackpackRows = new MethodDefinition("RiftRevivalFillBackpackRows", Mono.Cecil.MethodAttributes.Public, module.TypeSystem.Void);
            VariableDefinition rowSlot = new VariableDefinition(module.TypeSystem.Int32); VariableDefinition rowBag = new VariableDefinition(equipmentTool);
            fillBackpackRows.Body.Variables.Add(rowSlot); fillBackpackRows.Body.Variables.Add(rowBag); equipmentScreen.Methods.Add(fillBackpackRows); il = fillBackpackRows.Body.GetILProcessor();
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, backpackSlots); il.Emit(OpCodes.Callvirt, intListClear);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldc_I4_M1); il.Emit(OpCodes.Stfld, equippedRow);
            Instruction noEquippedBag = Instruction.Create(OpCodes.Nop);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, controllersField); il.Emit(OpCodes.Callvirt, clientPlayer); il.Emit(OpCodes.Callvirt, personalState); il.Emit(OpCodes.Ldfld, equipped); il.Emit(OpCodes.Stloc, rowBag); il.Emit(OpCodes.Ldloc, rowBag); il.Emit(OpCodes.Brfalse, noEquippedBag);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, equippedListBox); il.Emit(OpCodes.Callvirt, listBoxItems); il.Emit(OpCodes.Callvirt, stringCollectionCount); il.Emit(OpCodes.Stfld, equippedRow);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, equippedListBox); il.Emit(OpCodes.Callvirt, listBoxItems); il.Emit(OpCodes.Ldstr, "⟅  Backpack Tier {0} ({1}/{2})");
            il.Emit(OpCodes.Ldloc, rowBag); il.Emit(OpCodes.Ldfld, tier); il.Emit(OpCodes.Box, module.TypeSystem.Int32);
            il.Emit(OpCodes.Ldloc, rowBag); il.Emit(OpCodes.Call, itemCount); il.Emit(OpCodes.Box, module.TypeSystem.Int32);
            il.Emit(OpCodes.Ldloc, rowBag); il.Emit(OpCodes.Call, capacity); il.Emit(OpCodes.Box, module.TypeSystem.Int32);
            il.Emit(OpCodes.Call, module.ImportReference(typeof(string).GetMethod("Format", new[] { typeof(string), typeof(object), typeof(object), typeof(object) }))); il.Emit(OpCodes.Callvirt, stringCollectionAdd);
            il.Append(noEquippedBag); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Stloc, rowSlot);
            Instruction rowTest = Instruction.Create(OpCodes.Nop), rowBody = Instruction.Create(OpCodes.Nop), rowNext = Instruction.Create(OpCodes.Nop), rowsDone = Instruction.Create(OpCodes.Ret);
            il.Emit(OpCodes.Br, rowTest); il.Append(rowBody);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, controllersField); il.Emit(OpCodes.Callvirt, clientPlayer); il.Emit(OpCodes.Callvirt, personalState); il.Emit(OpCodes.Ldfld, toolInventory); il.Emit(OpCodes.Ldloc, rowSlot); il.Emit(OpCodes.Callvirt, collectionItem); il.Emit(OpCodes.Isinst, equipmentTool); il.Emit(OpCodes.Stloc, rowBag); il.Emit(OpCodes.Ldloc, rowBag); il.Emit(OpCodes.Brfalse, rowNext);
            il.Emit(OpCodes.Ldloc, rowBag); il.Emit(OpCodes.Ldflda, properties); il.Emit(OpCodes.Ldfld, propertyMount); il.Emit(OpCodes.Ldc_I4, 11); il.Emit(OpCodes.Bne_Un, rowNext);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, backpackSlots); il.Emit(OpCodes.Ldloc, rowSlot); il.Emit(OpCodes.Callvirt, intListAdd);
            il.Append(rowNext); il.Emit(OpCodes.Ldloc, rowSlot); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Add); il.Emit(OpCodes.Stloc, rowSlot); il.Append(rowTest);
            il.Emit(OpCodes.Ldloc, rowSlot); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, controllersField); il.Emit(OpCodes.Callvirt, clientPlayer); il.Emit(OpCodes.Callvirt, personalState); il.Emit(OpCodes.Ldfld, toolInventory); il.Emit(OpCodes.Callvirt, collectionCount); il.Emit(OpCodes.Blt, rowBody); il.Append(rowsDone);

            TypeDefinition toolHelpers = types.Single(t => t.FullName == "Game.ClientServer.Classes.Tools.ToolHelpers");
            MethodDefinition toolName = toolHelpers.Methods.Single(m => m.Name == "GetToolNameString" && m.Parameters.Count == 1 && m.Parameters[0].ParameterType.FullName.EndsWith("ClSvPlayerTool"));
            MethodDefinition toolIcon = toolHelpers.Methods.Single(m => m.Name == "GetToolIconCode" && m.Parameters.Count == 1 && m.Parameters[0].ParameterType.FullName.EndsWith("ClSvPlayerTool"));
            MethodDefinition toolColor = toolHelpers.Methods.Single(m => m.Name == "GetToolColorString" && m.Parameters.Count == 1 && m.Parameters[0].ParameterType.FullName.EndsWith("ClSvPlayerTool"));
            FieldDefinition itemName = encyclopediaItem.Fields.Single(f => f.Name == "Name");
            FieldDefinition itemToolType = encyclopediaTool.Fields.Single(f => f.Name == "ToolType"); FieldDefinition itemIcon = encyclopediaTool.Fields.Single(f => f.Name == "Icon"); FieldDefinition itemColor = encyclopediaTool.Fields.Single(f => f.Name == "Color");
            FieldDefinition baseWindow = baseState.Fields.Single(f => f.Name == "stateWindow"); FieldDefinition guiScale = equipmentScreen.Fields.Single(f => f.Name == "m_guiScale");
            MethodDefinition itemCtor = encyclopediaItem.Methods.Single(m => m.IsConstructor); MethodDefinition toolItemCtor = encyclopediaTool.Methods.Single(m => m.IsConstructor);
            MethodDefinition resourceItemCtor = encyclopediaResource.Methods.Single(m => m.IsConstructor);
            FieldDefinition itemResourceType = encyclopediaResource.Fields.Single(f => f.Name == "ResourceType"); FieldDefinition itemResourceAmount = encyclopediaResource.Fields.Single(f => f.Name == "ResourceAmount");
            MethodDefinition windowCtor = encyclopediaWindow.Methods.Single(m => m.IsConstructor); MethodDefinition windowInit = encyclopediaWindow.Methods.Single(m => m.Name == "Initialize"); MethodDefinition windowPosition = encyclopediaWindow.Methods.Single(m => m.Name == "SetPosition"); MethodDefinition windowRemove = encyclopediaWindow.Methods.Single(m => m.Name == "Remove");
            FieldDefinition windowClicked = encyclopediaWindow.Fields.Single(f => f.Name == "OnClicked");
            MethodReference clickedDelegateCtor = windowCtor.Body.Instructions.Select(i => i.Operand as MethodReference).First(r => r != null && r.Name == ".ctor" && r.DeclaringType.FullName == windowClicked.FieldType.FullName);
            MethodReference combineDelegate = module.ImportReference(typeof(Delegate).GetMethod("Combine", new[] { typeof(Delegate), typeof(Delegate) }));
            FieldDefinition baseToolType = types.Single(t => t.FullName == "Game.ClientServer.Classes.Tools.ClSvPlayerTool").Fields.Single(f => f.Name == "toolType");

            MethodDefinition cellClicked = new MethodDefinition("RiftRevivalOnBackpackCellClicked", Mono.Cecil.MethodAttributes.Public, module.TypeSystem.Void);
            cellClicked.Parameters.Add(new ParameterDefinition("sender", Mono.Cecil.ParameterAttributes.None, module.TypeSystem.Object)); cellClicked.Parameters.Add(new ParameterDefinition("item", Mono.Cecil.ParameterAttributes.None, encyclopediaItem)); VariableDefinition selectedResourceSlot = new VariableDefinition(module.TypeSystem.Int32); cellClicked.Body.Variables.Add(selectedResourceSlot); cellClicked.Body.InitLocals = true; equipmentScreen.Methods.Add(cellClicked); il = cellClicked.Body.GetILProcessor();
            Instruction storeSelectedTool = Instruction.Create(OpCodes.Nop), retrieveResourceCell = Instruction.Create(OpCodes.Nop);
            il.Emit(OpCodes.Ldarg_2); il.Emit(OpCodes.Ldfld, encyclopediaSlot); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Blt, storeSelectedTool);
            il.Emit(OpCodes.Ldarg_2); il.Emit(OpCodes.Isinst, encyclopediaResource); il.Emit(OpCodes.Brtrue, retrieveResourceCell);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, controllersField); il.Emit(OpCodes.Callvirt, clientPlayer); il.Emit(OpCodes.Callvirt, personalState); il.Emit(OpCodes.Ldfld, personalGlove);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, controllersField); il.Emit(OpCodes.Ldc_I4, 19); il.Emit(OpCodes.Ldarg_2); il.Emit(OpCodes.Ldfld, encyclopediaSlot); il.Emit(OpCodes.Box, module.TypeSystem.Int32); il.Emit(OpCodes.Ldc_I4_M1); il.Emit(OpCodes.Callvirt, sendCommand); il.Emit(OpCodes.Ret);
            il.Append(retrieveResourceCell); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, controllersField); il.Emit(OpCodes.Callvirt, clientPlayer); il.Emit(OpCodes.Callvirt, personalState); il.Emit(OpCodes.Ldfld, personalGlove);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, controllersField); il.Emit(OpCodes.Ldc_I4, 20); il.Emit(OpCodes.Ldarg_2); il.Emit(OpCodes.Ldfld, encyclopediaSlot); il.Emit(OpCodes.Box, module.TypeSystem.Int32); il.Emit(OpCodes.Ldc_I4_M1); il.Emit(OpCodes.Callvirt, sendCommand); il.Emit(OpCodes.Ret);
            MethodReference getHud = references.First(r => r.FullName == "Game.Client.ClHudController Game.Client.ControllerManager::get_HUD()");
            MethodReference getPlayerInventory = references.First(r => r.FullName == "Game.Client.UIComponents.ClPlayerInventory Game.Client.ClHudController::get_PlayerInventory()");
            MethodReference getSelectedTool = references.First(r => r.FullName == "System.Int32 Game.Client.UIComponents.ClPlayerInventory::get_CurrentSelectedToolInventoryItem()");
            MethodReference getSelectedResource = references.First(r => r.FullName == "System.Int32 Game.Client.UIComponents.ClPlayerInventory::get_CurrentSelectedResourceInventoryItem()");
            Instruction noSelectedResource = Instruction.Create(OpCodes.Nop);
            il.Append(storeSelectedTool); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, controllersField); il.Emit(OpCodes.Callvirt, getHud); il.Emit(OpCodes.Callvirt, getPlayerInventory); il.Emit(OpCodes.Callvirt, getSelectedResource); il.Emit(OpCodes.Stloc, selectedResourceSlot); il.Emit(OpCodes.Ldloc, selectedResourceSlot); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Blt, noSelectedResource);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, controllersField); il.Emit(OpCodes.Callvirt, clientPlayer); il.Emit(OpCodes.Callvirt, personalState); il.Emit(OpCodes.Ldfld, personalGlove);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, controllersField); il.Emit(OpCodes.Ldc_I4, 21); il.Emit(OpCodes.Ldloc, selectedResourceSlot); il.Emit(OpCodes.Box, module.TypeSystem.Int32); il.Emit(OpCodes.Ldc_I4_M1); il.Emit(OpCodes.Callvirt, sendCommand); il.Emit(OpCodes.Ret);
            il.Append(noSelectedResource); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, controllersField); il.Emit(OpCodes.Callvirt, clientPlayer); il.Emit(OpCodes.Callvirt, personalState); il.Emit(OpCodes.Ldfld, personalGlove);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, controllersField); il.Emit(OpCodes.Ldc_I4, 18); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, controllersField); il.Emit(OpCodes.Callvirt, getHud); il.Emit(OpCodes.Callvirt, getPlayerInventory); il.Emit(OpCodes.Callvirt, getSelectedTool); il.Emit(OpCodes.Box, module.TypeSystem.Int32); il.Emit(OpCodes.Ldc_I4_M1); il.Emit(OpCodes.Callvirt, sendCommand); il.Emit(OpCodes.Ret);

            MethodDefinition refreshGrid = new MethodDefinition("RiftRevivalRefreshBackpackGrid", Mono.Cecil.MethodAttributes.Public, module.TypeSystem.Void);
            VariableDefinition gridBag = new VariableDefinition(equipmentTool), desiredCapacity = new VariableDefinition(module.TypeSystem.Int32), desiredCount = new VariableDefinition(module.TypeSystem.Int32), gridIndex = new VariableDefinition(module.TypeSystem.Int32), gridToolCount = new VariableDefinition(module.TypeSystem.Int32), gridTool = new VariableDefinition(storeCandidate.VariableType), gridResource = new VariableDefinition(resourceCrate), gridItem = new VariableDefinition(encyclopediaItem), gridWindow = new VariableDefinition(encyclopediaWindow);
            foreach (VariableDefinition v in new[] { gridBag, desiredCapacity, desiredCount, gridIndex, gridToolCount, gridTool, gridResource, gridItem, gridWindow }) refreshGrid.Body.Variables.Add(v); refreshGrid.Body.InitLocals = true; equipmentScreen.Methods.Add(refreshGrid); il = refreshGrid.Body.GetILProcessor();
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, controllersField); il.Emit(OpCodes.Callvirt, clientPlayer); il.Emit(OpCodes.Callvirt, personalState); il.Emit(OpCodes.Ldfld, equipped); il.Emit(OpCodes.Stloc, gridBag);
            Instruction gridHasBag = Instruction.Create(OpCodes.Nop), gridStateReady = Instruction.Create(OpCodes.Nop), rebuildGrid = Instruction.Create(OpCodes.Nop);
            il.Emit(OpCodes.Ldloc, gridBag); il.Emit(OpCodes.Brtrue, gridHasBag); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Stloc, desiredCapacity); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Stloc, desiredCount); il.Emit(OpCodes.Br, gridStateReady);
            il.Append(gridHasBag); il.Emit(OpCodes.Ldloc, gridBag); il.Emit(OpCodes.Call, capacity); il.Emit(OpCodes.Stloc, desiredCapacity); il.Emit(OpCodes.Ldloc, gridBag); il.Emit(OpCodes.Call, itemCount); il.Emit(OpCodes.Stloc, desiredCount);
            il.Append(gridStateReady); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, gridCount); il.Emit(OpCodes.Ldloc, desiredCount); il.Emit(OpCodes.Bne_Un, rebuildGrid); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, gridTier); il.Emit(OpCodes.Ldloc, desiredCapacity); il.Emit(OpCodes.Bne_Un, rebuildGrid); il.Emit(OpCodes.Ret);
            il.Append(rebuildGrid); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Stloc, gridIndex);
            Instruction removeTest = Instruction.Create(OpCodes.Nop), removeBody = Instruction.Create(OpCodes.Nop), removedAll = Instruction.Create(OpCodes.Nop);
            il.Emit(OpCodes.Br, removeTest); il.Append(removeBody); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, backpackWindows); il.Emit(OpCodes.Ldloc, gridIndex); il.Emit(OpCodes.Callvirt, windowListItem); il.Emit(OpCodes.Callvirt, windowRemove); il.Emit(OpCodes.Ldloc, gridIndex); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Add); il.Emit(OpCodes.Stloc, gridIndex); il.Append(removeTest); il.Emit(OpCodes.Ldloc, gridIndex); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, backpackWindows); il.Emit(OpCodes.Callvirt, windowListCount); il.Emit(OpCodes.Blt, removeBody); il.Append(removedAll); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, backpackWindows); il.Emit(OpCodes.Callvirt, windowListClear);
            il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Stloc, gridIndex);
            il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Stloc, gridToolCount); Instruction noToolCount = Instruction.Create(OpCodes.Nop); il.Emit(OpCodes.Ldloc, gridBag); il.Emit(OpCodes.Brfalse, noToolCount); il.Emit(OpCodes.Ldloc, gridBag); il.Emit(OpCodes.Ldfld, inventory); il.Emit(OpCodes.Callvirt, collectionCount); il.Emit(OpCodes.Stloc, gridToolCount); il.Append(noToolCount);
            Instruction cellTest = Instruction.Create(OpCodes.Nop), cellBody = Instruction.Create(OpCodes.Nop), resourceCell = Instruction.Create(OpCodes.Nop), emptyCell = Instruction.Create(OpCodes.Nop), itemReady = Instruction.Create(OpCodes.Nop);
            il.Emit(OpCodes.Br, cellTest); il.Append(cellBody); il.Emit(OpCodes.Ldloc, gridIndex); il.Emit(OpCodes.Ldloc, desiredCount); il.Emit(OpCodes.Bge, emptyCell);
            il.Emit(OpCodes.Ldloc, gridIndex); il.Emit(OpCodes.Ldloc, gridToolCount); il.Emit(OpCodes.Bge, resourceCell);
            il.Emit(OpCodes.Ldloc, gridBag); il.Emit(OpCodes.Ldfld, inventory); il.Emit(OpCodes.Ldloc, gridIndex); il.Emit(OpCodes.Callvirt, collectionItem); il.Emit(OpCodes.Stloc, gridTool); il.Emit(OpCodes.Newobj, toolItemCtor); il.Emit(OpCodes.Dup); il.Emit(OpCodes.Ldloc, gridIndex); il.Emit(OpCodes.Stfld, encyclopediaSlot); il.Emit(OpCodes.Dup); il.Emit(OpCodes.Ldloc, gridTool); il.Emit(OpCodes.Call, toolName); il.Emit(OpCodes.Stfld, itemName); il.Emit(OpCodes.Dup); il.Emit(OpCodes.Ldloc, gridTool); il.Emit(OpCodes.Ldfld, baseToolType); il.Emit(OpCodes.Stfld, itemToolType); il.Emit(OpCodes.Dup); il.Emit(OpCodes.Ldloc, gridTool); il.Emit(OpCodes.Call, toolIcon); il.Emit(OpCodes.Stfld, itemIcon); il.Emit(OpCodes.Dup); il.Emit(OpCodes.Ldloc, gridTool); il.Emit(OpCodes.Call, toolColor); il.Emit(OpCodes.Stfld, itemColor); il.Emit(OpCodes.Stloc, gridItem); il.Emit(OpCodes.Br, itemReady);
            il.Append(resourceCell); il.Emit(OpCodes.Ldloc, gridBag); il.Emit(OpCodes.Ldfld, resources); il.Emit(OpCodes.Ldloc, gridIndex); il.Emit(OpCodes.Ldloc, gridToolCount); il.Emit(OpCodes.Sub); il.Emit(OpCodes.Callvirt, backpackResourceItem); il.Emit(OpCodes.Stloc, gridResource); il.Emit(OpCodes.Newobj, resourceItemCtor); il.Emit(OpCodes.Dup); il.Emit(OpCodes.Ldloc, gridIndex); il.Emit(OpCodes.Ldloc, gridToolCount); il.Emit(OpCodes.Sub); il.Emit(OpCodes.Stfld, encyclopediaSlot); il.Emit(OpCodes.Dup); il.Emit(OpCodes.Ldloc, gridResource); il.Emit(OpCodes.Ldfld, resourceType); il.Emit(OpCodes.Stfld, itemResourceType); il.Emit(OpCodes.Dup); il.Emit(OpCodes.Ldloc, gridResource); il.Emit(OpCodes.Ldfld, resourceAmount); il.Emit(OpCodes.Stfld, itemResourceAmount); il.Emit(OpCodes.Stloc, gridItem); il.Emit(OpCodes.Br, itemReady);
            il.Append(emptyCell); il.Emit(OpCodes.Newobj, itemCtor); il.Emit(OpCodes.Dup); il.Emit(OpCodes.Ldc_I4_M1); il.Emit(OpCodes.Stfld, encyclopediaSlot); il.Emit(OpCodes.Stloc, gridItem);
            il.Append(itemReady); il.Emit(OpCodes.Ldloc, gridItem); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, baseWindow); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, controllersField); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, guiScale); il.Emit(OpCodes.Ldc_I4, 84); il.Emit(OpCodes.Newobj, windowCtor); il.Emit(OpCodes.Stloc, gridWindow);
            // Empty slots are real interactive cells too.  Leaving them
            // uninitialized made an equipped, empty backpack look like a blank
            // Stats panel and also prevented the click-to-store interaction.
            il.Emit(OpCodes.Ldloc, gridWindow); il.Emit(OpCodes.Callvirt, windowInit);
            il.Emit(OpCodes.Ldloc, gridWindow); il.Emit(OpCodes.Ldloc, gridWindow); il.Emit(OpCodes.Ldfld, windowClicked); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldftn, cellClicked); il.Emit(OpCodes.Newobj, clickedDelegateCtor); il.Emit(OpCodes.Call, combineDelegate); il.Emit(OpCodes.Castclass, windowClicked.FieldType); il.Emit(OpCodes.Stfld, windowClicked);
            il.Emit(OpCodes.Ldloc, gridWindow); il.Emit(OpCodes.Ldc_I4, 1040); il.Emit(OpCodes.Ldloc, gridIndex); il.Emit(OpCodes.Ldc_I4_4); il.Emit(OpCodes.Rem); il.Emit(OpCodes.Ldc_I4, 94); il.Emit(OpCodes.Mul); il.Emit(OpCodes.Add); il.Emit(OpCodes.Ldc_I4, 250); il.Emit(OpCodes.Ldloc, gridIndex); il.Emit(OpCodes.Ldc_I4_4); il.Emit(OpCodes.Div); il.Emit(OpCodes.Ldc_I4, 94); il.Emit(OpCodes.Mul); il.Emit(OpCodes.Add); il.Emit(OpCodes.Callvirt, windowPosition);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, backpackWindows); il.Emit(OpCodes.Ldloc, gridWindow); il.Emit(OpCodes.Callvirt, windowListAdd); il.Emit(OpCodes.Ldloc, gridIndex); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Add); il.Emit(OpCodes.Stloc, gridIndex); il.Append(cellTest); il.Emit(OpCodes.Ldloc, gridIndex); il.Emit(OpCodes.Ldloc, desiredCapacity); il.Emit(OpCodes.Blt, cellBody);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldloc, desiredCount); il.Emit(OpCodes.Stfld, gridCount); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldloc, desiredCapacity); il.Emit(OpCodes.Stfld, gridTier); il.Emit(OpCodes.Ret);

            MethodDefinition clearGrid = new MethodDefinition("RiftRevivalClearBackpackGrid", Mono.Cecil.MethodAttributes.Public, module.TypeSystem.Void);
            VariableDefinition clearIndex = new VariableDefinition(module.TypeSystem.Int32); clearGrid.Body.Variables.Add(clearIndex); equipmentScreen.Methods.Add(clearGrid); il = clearGrid.Body.GetILProcessor();
            il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Stloc, clearIndex); Instruction clearTest = Instruction.Create(OpCodes.Nop), clearBody = Instruction.Create(OpCodes.Nop);
            il.Emit(OpCodes.Br, clearTest); il.Append(clearBody); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, backpackWindows); il.Emit(OpCodes.Ldloc, clearIndex); il.Emit(OpCodes.Callvirt, windowListItem); il.Emit(OpCodes.Callvirt, windowRemove); il.Emit(OpCodes.Ldloc, clearIndex); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Add); il.Emit(OpCodes.Stloc, clearIndex); il.Append(clearTest); il.Emit(OpCodes.Ldloc, clearIndex); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, backpackWindows); il.Emit(OpCodes.Callvirt, windowListCount); il.Emit(OpCodes.Blt, clearBody);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, backpackWindows); il.Emit(OpCodes.Callvirt, windowListClear); il.Emit(OpCodes.Ret);
            MethodDefinition unloadScreen = equipmentScreen.Methods.Single(m => m.Name == "Unload"); Instruction unloadFirst = unloadScreen.Body.Instructions.First(); ILProcessor unloadIl = unloadScreen.Body.GetILProcessor(); unloadIl.InsertBefore(unloadFirst, Instruction.Create(OpCodes.Ldarg_0)); unloadIl.InsertBefore(unloadFirst, Instruction.Create(OpCodes.Call, clearGrid));

            MethodDefinition equipmentUpdate = equipmentScreen.Methods.Single(m => m.Name == "Update"); ILProcessor updateIl = equipmentUpdate.Body.GetILProcessor();
            foreach (Instruction updateRet in equipmentUpdate.Body.Instructions.Where(i => i.OpCode == OpCodes.Ret).ToArray()) { updateIl.InsertBefore(updateRet, Instruction.Create(OpCodes.Ldarg_0)); updateIl.InsertBefore(updateRet, Instruction.Create(OpCodes.Call, fillBackpackRows)); updateIl.InsertBefore(updateRet, Instruction.Create(OpCodes.Ldarg_0)); updateIl.InsertBefore(updateRet, Instruction.Create(OpCodes.Call, refreshGrid)); }

            MethodDefinition handleBackpackClick = new MethodDefinition("RiftRevivalHandleBackpackClick", Mono.Cecil.MethodAttributes.Public, module.TypeSystem.Boolean);
            handleBackpackClick.Parameters.Add(new ParameterDefinition("eventCode", Mono.Cecil.ParameterAttributes.None, equipmentScreen.Methods.Single(m => m.Name == "OnHandleButtonEvents").Parameters[0].ParameterType));
            handleBackpackClick.Parameters.Add(new ParameterDefinition("eventId", Mono.Cecil.ParameterAttributes.None, module.TypeSystem.Int32));
            VariableDefinition selected = new VariableDefinition(module.TypeSystem.Int32), selectedTool = new VariableDefinition(equipmentTool); handleBackpackClick.Body.Variables.Add(selected); handleBackpackClick.Body.Variables.Add(selectedTool); handleBackpackClick.Body.InitLocals = true; equipmentScreen.Methods.Add(handleBackpackClick); il = handleBackpackClick.Body.GetILProcessor();
            Instruction tryInventoryRow = Instruction.Create(OpCodes.Nop), tryAddedInventoryRow = Instruction.Create(OpCodes.Nop), sendInventoryEquip = Instruction.Create(OpCodes.Nop), clickUnhandled = Instruction.Create(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldc_I4, 202); il.Emit(OpCodes.Bne_Un, tryInventoryRow);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, equippedListBox); il.Emit(OpCodes.Callvirt, selectedIndex); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, equippedRow); il.Emit(OpCodes.Bne_Un, clickUnhandled);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, controllersField); il.Emit(OpCodes.Callvirt, clientPlayer); il.Emit(OpCodes.Callvirt, personalState); il.Emit(OpCodes.Ldfld, personalGlove); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, controllersField); il.Emit(OpCodes.Ldc_I4, 17); il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Ldc_I4_M1); il.Emit(OpCodes.Callvirt, sendCommand); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Ret);
            il.Append(tryInventoryRow); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldc_I4, 203); il.Emit(OpCodes.Bne_Un, clickUnhandled);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, unequippedListBox); il.Emit(OpCodes.Callvirt, selectedIndex); il.Emit(OpCodes.Stloc, selected);
            il.Emit(OpCodes.Ldloc, selected); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Blt, clickUnhandled);
            il.Emit(OpCodes.Ldloc, selected); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, unequippedItems); il.Emit(OpCodes.Callvirt, equipmentListCount); il.Emit(OpCodes.Bge, tryAddedInventoryRow);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, unequippedItems); il.Emit(OpCodes.Ldloc, selected); il.Emit(OpCodes.Callvirt, equipmentListItem); il.Emit(OpCodes.Ldfld, equippableTool); il.Emit(OpCodes.Stloc, selectedTool);
            il.Emit(OpCodes.Ldloc, selectedTool); il.Emit(OpCodes.Ldflda, properties); il.Emit(OpCodes.Ldfld, propertyMount); il.Emit(OpCodes.Ldc_I4, 11); il.Emit(OpCodes.Bne_Un, clickUnhandled);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, controllersField); il.Emit(OpCodes.Callvirt, clientPlayer); il.Emit(OpCodes.Callvirt, personalState); il.Emit(OpCodes.Ldfld, toolInventory); il.Emit(OpCodes.Ldloc, selectedTool); il.Emit(OpCodes.Callvirt, toolInventoryIndexOf); il.Emit(OpCodes.Stloc, selected);
            il.Emit(OpCodes.Br, sendInventoryEquip);
            il.Append(tryAddedInventoryRow); il.Emit(OpCodes.Ldloc, selected); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, unequippedItems); il.Emit(OpCodes.Callvirt, equipmentListCount); il.Emit(OpCodes.Sub); il.Emit(OpCodes.Stloc, selected);
            il.Emit(OpCodes.Ldloc, selected); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Blt, clickUnhandled); il.Emit(OpCodes.Ldloc, selected); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, backpackSlots); il.Emit(OpCodes.Callvirt, intListCount); il.Emit(OpCodes.Bge, clickUnhandled);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, backpackSlots); il.Emit(OpCodes.Ldloc, selected); il.Emit(OpCodes.Callvirt, intListItem); il.Emit(OpCodes.Stloc, selected);
            il.Append(sendInventoryEquip); il.Emit(OpCodes.Ldloc, selected); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Blt, clickUnhandled);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, controllersField); il.Emit(OpCodes.Callvirt, clientPlayer); il.Emit(OpCodes.Callvirt, personalState); il.Emit(OpCodes.Ldfld, personalGlove); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, controllersField); il.Emit(OpCodes.Ldc_I4, 16); il.Emit(OpCodes.Ldloc, selected); il.Emit(OpCodes.Box, module.TypeSystem.Int32); il.Emit(OpCodes.Ldc_I4_M1); il.Emit(OpCodes.Callvirt, sendCommand); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Ret);
            il.Append(clickUnhandled); il.Emit(OpCodes.Ret);

            MethodDefinition equipmentEvents = equipmentScreen.Methods.Single(m => m.Name == "OnHandleButtonEvents"); Instruction eventFirst = equipmentEvents.Body.Instructions.First(); ILProcessor eventIl = equipmentEvents.Body.GetILProcessor();
            eventIl.InsertBefore(eventFirst, Instruction.Create(OpCodes.Ldarg_0)); eventIl.InsertBefore(eventFirst, Instruction.Create(OpCodes.Ldarg_1)); eventIl.InsertBefore(eventFirst, Instruction.Create(OpCodes.Ldarg_2)); eventIl.InsertBefore(eventFirst, Instruction.Create(OpCodes.Call, handleBackpackClick)); eventIl.InsertBefore(eventFirst, Instruction.Create(OpCodes.Brfalse, eventFirst)); eventIl.InsertBefore(eventFirst, Instruction.Create(OpCodes.Ret));
        }

        private static void EmitResult(ILProcessor il, MethodDefinition finishAction, bool success, string message)
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(success ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldstr, message);
            il.Emit(OpCodes.Call, finishAction);
            il.Emit(OpCodes.Ret);
        }

        private static Instruction AppendBackpackSlotHandler(ILProcessor il, MethodDefinition serverData, MethodDefinition action, MethodDefinition sendResult, ModuleDefinition module)
        {
            Instruction handler = Instruction.Create(OpCodes.Ldarg_S, serverData.Parameters[3]);
            Instruction valid = Instruction.Create(OpCodes.Nop);
            il.Append(handler); il.Emit(OpCodes.Isinst, module.TypeSystem.Int32); il.Emit(OpCodes.Brtrue, valid); il.Emit(OpCodes.Ret); il.Append(valid);
            il.Emit(OpCodes.Ldloc_0); il.Emit(OpCodes.Ldarg_S, serverData.Parameters[3]); il.Emit(OpCodes.Unbox_Any, module.TypeSystem.Int32); il.Emit(OpCodes.Call, action); il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldloc_0); il.Emit(OpCodes.Call, sendResult); il.Emit(OpCodes.Ret);
            return handler;
        }
    }
}
