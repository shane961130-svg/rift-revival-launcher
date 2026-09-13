using System;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace RiftRevival.Launcher
{
    // Adds saved, dedicated battery storage to the already serialized power glove.
    // The stock glove cell remains a separate 1,000-unit emergency reserve.
    internal static class GloveBatteryPatch
    {
        private static MethodDefinition AddMethod(TypeDefinition owner, string name, TypeReference result)
        {
            MethodDefinition method = new MethodDefinition(name, Mono.Cecil.MethodAttributes.Public, result);
            owner.Methods.Add(method);
            method.Body.InitLocals = true;
            return method;
        }

        private static void InsertBeforeReturns(MethodDefinition method, params Instruction[] template)
        {
            ILProcessor il = method.Body.GetILProcessor();
            foreach (Instruction target in method.Body.Instructions.Where(i => i.OpCode == OpCodes.Ret).ToArray())
                foreach (Instruction source in template)
                    il.InsertBefore(target, Instruction.Create(source.OpCode, source.Operand as MethodReference));
        }

        public static void Apply(ModuleDefinition module)
        {
            TypeDefinition[] types = module.GetTypes().ToArray();
            TypeDefinition glove = types.Single(t => t.FullName == "Game.ClientServer.Classes.Tools.ClSvPlayerTool_PowerGlove");
            TypeDefinition battery = types.Single(t => t.FullName == "Game.ClientServer.Classes.Tools.ClSvPlayerTool_ToolPortableBattery");

            if (glove.Fields.Any(f => f.Name == "RiftRevivalBattery"))
                throw new InvalidDataException("The glove battery patch is already present.");

            FieldDefinition installed = new FieldDefinition("RiftRevivalBattery", Mono.Cecil.FieldAttributes.Public, battery);
            FieldDefinition reserve = new FieldDefinition("RiftRevivalReserve", Mono.Cecil.FieldAttributes.Public, module.TypeSystem.Single);
            FieldDefinition initialized = new FieldDefinition("RiftRevivalBatteryStateInitialized", Mono.Cecil.FieldAttributes.Public, module.TypeSystem.Boolean);
            FieldDefinition clientInstalled = new FieldDefinition("RiftRevivalClientBatteryInstalled", Mono.Cecil.FieldAttributes.Public | Mono.Cecil.FieldAttributes.NotSerialized, module.TypeSystem.Boolean);
            FieldDefinition clientName = new FieldDefinition("RiftRevivalClientBatteryName", Mono.Cecil.FieldAttributes.Public | Mono.Cecil.FieldAttributes.NotSerialized, module.TypeSystem.String);
            FieldDefinition clientDetails = new FieldDefinition("RiftRevivalClientBatteryDetails", Mono.Cecil.FieldAttributes.Public | Mono.Cecil.FieldAttributes.NotSerialized, module.TypeSystem.String);
            FieldDefinition clientMessage = new FieldDefinition("RiftRevivalClientBatteryMessage", Mono.Cecil.FieldAttributes.Public | Mono.Cecil.FieldAttributes.NotSerialized, module.TypeSystem.String);
            glove.Fields.Add(installed);
            glove.Fields.Add(reserve);
            glove.Fields.Add(initialized);
            glove.Fields.Add(clientInstalled);
            glove.Fields.Add(clientName);
            glove.Fields.Add(clientDetails);
            glove.Fields.Add(clientMessage);

            FieldDefinition ammo = glove.Fields.Single(f => f.Name == "ammo");
            FieldDefinition maxAmmo = glove.Fields.Single(f => f.Name == "maxAmmo");
            MethodDefinition getPower = battery.Methods.Single(m => m.Name == "GetPower" && !m.HasParameters);
            MethodDefinition getMaxPower = battery.Methods.Single(m => m.Name == "GetMaxPower" && !m.HasParameters);
            MethodDefinition addPower = battery.Methods.Single(m => m.Name == "AddPower" && m.Parameters.Count == 1);
            MethodDefinition takePower = battery.Methods.Single(m => m.Name == "TakePower" && m.Parameters.Count == 1);
            MethodReference[] references = types.SelectMany(t => t.Methods).Where(m => m.HasBody).SelectMany(m => m.Body.Instructions).Select(i => i.Operand as MethodReference).Where(r => r != null).ToArray();
            MethodReference toFloat = references.First(r => r.FullName == "System.Single Game.Framework.FP::ToFloat()");
            MethodReference fromDouble = references.First(r => r.FullName == "Game.Framework.FP Game.Framework.FP::CD(System.Double)");
            MethodReference min = module.ImportReference(typeof(Math).GetMethod("Min", new[] { typeof(float), typeof(float) }));
            MethodReference max = module.ImportReference(typeof(Math).GetMethod("Max", new[] { typeof(float), typeof(float) }));
            MethodReference toolName = references.First(r => r.FullName == "System.String Game.ClientServer.Classes.Tools.ToolHelpers::GetToolNameString(Game.ClientServer.Classes.Tools.ClSvPlayerTool)");

            TypeDefinition batteryTier1 = types.Single(t => t.FullName == "Game.ClientServer.Classes.Tools.ClSvPlayerTool_ToolPortableBattery_Tier1");
            TypeDefinition batteryTier2 = types.Single(t => t.FullName == "Game.ClientServer.Classes.Tools.ClSvPlayerTool_ToolPortableBattery_Tier2");
            TypeDefinition batteryTier3 = types.Single(t => t.FullName == "Game.ClientServer.Classes.Tools.ClSvPlayerTool_ToolPortableBattery_Tier3");

            MethodDefinition gloveCapacity = new MethodDefinition("RiftRevivalGetGloveBatteryCapacity", Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Static, module.TypeSystem.Single);
            gloveCapacity.Parameters.Add(new ParameterDefinition("battery", Mono.Cecil.ParameterAttributes.None, battery)); glove.Methods.Add(gloveCapacity);
            ILProcessor helperIl = gloveCapacity.Body.GetILProcessor();
            Instruction capacityTier2 = Instruction.Create(OpCodes.Ldarg_0), capacityTier1 = Instruction.Create(OpCodes.Ldarg_0), capacityTier0 = Instruction.Create(OpCodes.Ldc_R4, 1000f);
            helperIl.Emit(OpCodes.Ldarg_0); helperIl.Emit(OpCodes.Isinst, batteryTier3); helperIl.Emit(OpCodes.Brfalse, capacityTier2); helperIl.Emit(OpCodes.Ldc_R4, 10000f); helperIl.Emit(OpCodes.Ret);
            helperIl.Append(capacityTier2); helperIl.Emit(OpCodes.Isinst, batteryTier2); helperIl.Emit(OpCodes.Brfalse, capacityTier1); helperIl.Emit(OpCodes.Ldc_R4, 5000f); helperIl.Emit(OpCodes.Ret);
            helperIl.Append(capacityTier1); helperIl.Emit(OpCodes.Isinst, batteryTier1); helperIl.Emit(OpCodes.Brfalse, capacityTier0); helperIl.Emit(OpCodes.Ldc_R4, 2500f); helperIl.Emit(OpCodes.Ret);
            helperIl.Append(capacityTier0); helperIl.Emit(OpCodes.Ret);

            MethodDefinition batteryDisplayName = new MethodDefinition("RiftRevivalGetBatteryDisplayName", Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Static, module.TypeSystem.String);
            batteryDisplayName.Parameters.Add(new ParameterDefinition("battery", Mono.Cecil.ParameterAttributes.None, battery)); glove.Methods.Add(batteryDisplayName); helperIl = batteryDisplayName.Body.GetILProcessor();
            Instruction nameTier2 = Instruction.Create(OpCodes.Ldarg_0), nameTier1 = Instruction.Create(OpCodes.Ldarg_0), nameTier0 = Instruction.Create(OpCodes.Ldstr, "~-Cffffffᓚ Battery Tier 0");
            helperIl.Emit(OpCodes.Ldarg_0); helperIl.Emit(OpCodes.Isinst, batteryTier3); helperIl.Emit(OpCodes.Brfalse, nameTier2); helperIl.Emit(OpCodes.Ldstr, "~-Cffffffᓝ Battery Tier 3"); helperIl.Emit(OpCodes.Ret);
            helperIl.Append(nameTier2); helperIl.Emit(OpCodes.Isinst, batteryTier2); helperIl.Emit(OpCodes.Brfalse, nameTier1); helperIl.Emit(OpCodes.Ldstr, "~-Cffffffᓜ Battery Tier 2"); helperIl.Emit(OpCodes.Ret);
            helperIl.Append(nameTier1); helperIl.Emit(OpCodes.Isinst, batteryTier1); helperIl.Emit(OpCodes.Brfalse, nameTier0); helperIl.Emit(OpCodes.Ldstr, "~-Cffffffᓛ Battery Tier 1"); helperIl.Emit(OpCodes.Ret);
            helperIl.Append(nameTier0); helperIl.Emit(OpCodes.Ret);

            MethodDefinition availablePower = new MethodDefinition("RiftRevivalGetGloveBatteryPower", Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Static, module.TypeSystem.Single);
            availablePower.Parameters.Add(new ParameterDefinition("battery", Mono.Cecil.ParameterAttributes.None, battery)); availablePower.Body.InitLocals = true;
            VariableDefinition availableFixed = new VariableDefinition(getPower.ReturnType); availablePower.Body.Variables.Add(availableFixed); glove.Methods.Add(availablePower); helperIl = availablePower.Body.GetILProcessor();
            helperIl.Emit(OpCodes.Ldarg_0); helperIl.Emit(OpCodes.Callvirt, getPower); helperIl.Emit(OpCodes.Stloc, availableFixed); helperIl.Emit(OpCodes.Ldloca, availableFixed); helperIl.Emit(OpCodes.Call, toFloat);
            helperIl.Emit(OpCodes.Ldarg_0); helperIl.Emit(OpCodes.Callvirt, getMaxPower); helperIl.Emit(OpCodes.Stloc, availableFixed); helperIl.Emit(OpCodes.Ldloca, availableFixed); helperIl.Emit(OpCodes.Call, toFloat); helperIl.Emit(OpCodes.Div);
            helperIl.Emit(OpCodes.Ldarg_0); helperIl.Emit(OpCodes.Call, gloveCapacity); helperIl.Emit(OpCodes.Mul); helperIl.Emit(OpCodes.Ret);

            MethodDefinition spendPower = new MethodDefinition("RiftRevivalTakeGloveBatteryPower", Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Static, module.TypeSystem.Void);
            spendPower.Parameters.Add(new ParameterDefinition("battery", Mono.Cecil.ParameterAttributes.None, battery)); spendPower.Parameters.Add(new ParameterDefinition("amount", Mono.Cecil.ParameterAttributes.None, module.TypeSystem.Single)); spendPower.Body.InitLocals = true;
            VariableDefinition spendFixed = new VariableDefinition(getPower.ReturnType); spendPower.Body.Variables.Add(spendFixed); glove.Methods.Add(spendPower); helperIl = spendPower.Body.GetILProcessor();
            helperIl.Emit(OpCodes.Ldarg_1); helperIl.Emit(OpCodes.Ldarg_0); helperIl.Emit(OpCodes.Callvirt, getMaxPower); helperIl.Emit(OpCodes.Stloc, spendFixed); helperIl.Emit(OpCodes.Ldloca, spendFixed); helperIl.Emit(OpCodes.Call, toFloat); helperIl.Emit(OpCodes.Mul);
            helperIl.Emit(OpCodes.Ldarg_0); helperIl.Emit(OpCodes.Call, gloveCapacity); helperIl.Emit(OpCodes.Div); helperIl.Emit(OpCodes.Conv_R8); helperIl.Emit(OpCodes.Call, fromDouble); helperIl.Emit(OpCodes.Stloc, spendFixed);
            helperIl.Emit(OpCodes.Ldarg_0); helperIl.Emit(OpCodes.Ldloc, spendFixed); helperIl.Emit(OpCodes.Callvirt, takePower); helperIl.Emit(OpCodes.Ret);

            MethodDefinition refillAfterRespawn = AddMethod(glove, "RiftRevivalRefillAfterRespawn", module.TypeSystem.Void);
            VariableDefinition refillMax = new VariableDefinition(getMaxPower.ReturnType); refillAfterRespawn.Body.Variables.Add(refillMax); helperIl = refillAfterRespawn.Body.GetILProcessor();
            Instruction noInstalledBattery = Instruction.Create(OpCodes.Nop);
            helperIl.Emit(OpCodes.Ldarg_0); helperIl.Emit(OpCodes.Ldfld, installed); helperIl.Emit(OpCodes.Brfalse, noInstalledBattery);
            helperIl.Emit(OpCodes.Ldarg_0); helperIl.Emit(OpCodes.Ldfld, installed); helperIl.Emit(OpCodes.Dup); helperIl.Emit(OpCodes.Callvirt, getMaxPower); helperIl.Emit(OpCodes.Stloc, refillMax); helperIl.Emit(OpCodes.Ldloc, refillMax); helperIl.Emit(OpCodes.Callvirt, addPower);
            helperIl.Append(noInstalledBattery);
            helperIl.Emit(OpCodes.Ldarg_0); helperIl.Emit(OpCodes.Ldc_R4, 1000f); helperIl.Emit(OpCodes.Stfld, reserve);
            helperIl.Emit(OpCodes.Ldarg_0); helperIl.Emit(OpCodes.Ldc_I4_1); helperIl.Emit(OpCodes.Stfld, initialized);
            helperIl.Emit(OpCodes.Ldarg_0); helperIl.Emit(OpCodes.Ldc_R4, 1000f); helperIl.Emit(OpCodes.Ldarg_0); helperIl.Emit(OpCodes.Ldfld, installed); Instruction refillStoreAmmo = Instruction.Create(OpCodes.Stfld, ammo); helperIl.Emit(OpCodes.Brfalse, refillStoreAmmo); helperIl.Emit(OpCodes.Ldarg_0); helperIl.Emit(OpCodes.Ldfld, installed); helperIl.Emit(OpCodes.Call, gloveCapacity); helperIl.Emit(OpCodes.Add); helperIl.Append(refillStoreAmmo);
            helperIl.Emit(OpCodes.Ldarg_0); helperIl.Emit(OpCodes.Ldc_R4, 1000f); helperIl.Emit(OpCodes.Ldarg_0); helperIl.Emit(OpCodes.Ldfld, installed); Instruction refillStoreMax = Instruction.Create(OpCodes.Stfld, maxAmmo); helperIl.Emit(OpCodes.Brfalse, refillStoreMax); helperIl.Emit(OpCodes.Ldarg_0); helperIl.Emit(OpCodes.Ldfld, installed); helperIl.Emit(OpCodes.Call, gloveCapacity); helperIl.Emit(OpCodes.Add); helperIl.Append(refillStoreMax); helperIl.Emit(OpCodes.Ret);

            MethodDefinition sync = AddMethod(glove, "RiftRevivalSyncBatteryEnergy", module.TypeSystem.Void);
            VariableDefinition batteryPower = new VariableDefinition(module.TypeSystem.Single);
            VariableDefinition delta = new VariableDefinition(module.TypeSystem.Single);
            VariableDefinition drain = new VariableDefinition(module.TypeSystem.Single);
            VariableDefinition fixedPoint = new VariableDefinition(getPower.ReturnType);
            sync.Body.Variables.Add(batteryPower);
            sync.Body.Variables.Add(delta);
            sync.Body.Variables.Add(drain);
            sync.Body.Variables.Add(fixedPoint);
            ILProcessor il = sync.Body.GetILProcessor();

            Instruction initializedState = Instruction.Create(OpCodes.Nop);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, initialized); il.Emit(OpCodes.Brtrue, initializedState);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, ammo); il.Emit(OpCodes.Ldc_R4, 1000f); il.Emit(OpCodes.Call, min); il.Emit(OpCodes.Stfld, reserve);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Stfld, initialized);
            il.Append(initializedState);

            Instruction noBattery = Instruction.Create(OpCodes.Ldc_R4, 0f);
            Instruction haveBatteryPower = Instruction.Create(OpCodes.Stloc, batteryPower);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, installed); il.Emit(OpCodes.Brfalse, noBattery);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, installed); il.Emit(OpCodes.Call, availablePower); il.Emit(OpCodes.Br, haveBatteryPower);
            il.Append(noBattery); il.Append(haveBatteryPower);

            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, ammo); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, reserve); il.Emit(OpCodes.Ldloc, batteryPower); il.Emit(OpCodes.Add); il.Emit(OpCodes.Sub); il.Emit(OpCodes.Stloc, delta);
            Instruction positive = Instruction.Create(OpCodes.Nop);
            Instruction reconciled = Instruction.Create(OpCodes.Nop);
            il.Emit(OpCodes.Ldloc, delta); il.Emit(OpCodes.Ldc_R4, 0f); il.Emit(OpCodes.Bge, positive);

            Instruction skipBatteryDrain = Instruction.Create(OpCodes.Nop);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, installed); il.Emit(OpCodes.Brfalse, skipBatteryDrain);
            il.Emit(OpCodes.Ldloc, batteryPower); il.Emit(OpCodes.Ldloc, delta); il.Emit(OpCodes.Neg); il.Emit(OpCodes.Call, min); il.Emit(OpCodes.Stloc, drain);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, installed); il.Emit(OpCodes.Ldloc, drain); il.Emit(OpCodes.Call, spendPower);
            il.Emit(OpCodes.Ldloc, delta); il.Emit(OpCodes.Ldloc, drain); il.Emit(OpCodes.Add); il.Emit(OpCodes.Stloc, delta);
            il.Append(skipBatteryDrain);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldc_R4, 0f); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, reserve); il.Emit(OpCodes.Ldloc, delta); il.Emit(OpCodes.Add); il.Emit(OpCodes.Call, max); il.Emit(OpCodes.Stfld, reserve);
            il.Emit(OpCodes.Br, reconciled);

            il.Append(positive);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldc_R4, 1000f); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, reserve); il.Emit(OpCodes.Ldloc, delta); il.Emit(OpCodes.Add); il.Emit(OpCodes.Call, min); il.Emit(OpCodes.Stfld, reserve);
            il.Append(reconciled);

            Instruction noBatteryAfter = Instruction.Create(OpCodes.Ldc_R4, 0f);
            Instruction storeBatteryAfter = Instruction.Create(OpCodes.Stloc, batteryPower);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, installed); il.Emit(OpCodes.Brfalse, noBatteryAfter);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, installed); il.Emit(OpCodes.Call, availablePower); il.Emit(OpCodes.Br, storeBatteryAfter);
            il.Append(noBatteryAfter); il.Append(storeBatteryAfter);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, reserve); il.Emit(OpCodes.Ldloc, batteryPower); il.Emit(OpCodes.Add); il.Emit(OpCodes.Stfld, ammo);

            Instruction reserveOnlyMax = Instruction.Create(OpCodes.Ldc_R4, 1000f);
            Instruction storeMax = Instruction.Create(OpCodes.Stfld, maxAmmo);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, installed); il.Emit(OpCodes.Brfalse, reserveOnlyMax);
            il.Emit(OpCodes.Ldc_R4, 1000f); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, installed); il.Emit(OpCodes.Call, gloveCapacity); il.Emit(OpCodes.Add); il.Emit(OpCodes.Br, storeMax);
            il.Append(reserveOnlyMax); il.Append(storeMax); il.Emit(OpCodes.Ret);

            // Reconcile before and after stock update logic. This keeps the stock passive
            // recharge confined to the reserve and makes stock TakePower drain the battery first.
            foreach (string name in new[] { "Server_OnUpdate" })
            {
                MethodDefinition update = glove.Methods.Single(m => m.Name == name);
                ILProcessor updateIl = update.Body.GetILProcessor();
                Instruction first = update.Body.Instructions.First();
                updateIl.InsertBefore(first, Instruction.Create(OpCodes.Ldarg_0));
                updateIl.InsertBefore(first, Instruction.Create(OpCodes.Call, sync));
                foreach (Instruction ret in update.Body.Instructions.Where(i => i.OpCode == OpCodes.Ret).ToArray())
                {
                    updateIl.InsertBefore(ret, Instruction.Create(OpCodes.Ldarg_0));
                    updateIl.InsertBefore(ret, Instruction.Create(OpCodes.Call, sync));
                }
            }
            foreach (string name in new[] { "TakePower" })
            {
                MethodDefinition method = glove.Methods.Single(m => m.Name == name);
                ILProcessor methodIl = method.Body.GetILProcessor();
                foreach (Instruction ret in method.Body.Instructions.Where(i => i.OpCode == OpCodes.Ret).ToArray())
                {
                    methodIl.InsertBefore(ret, Instruction.Create(OpCodes.Ldarg_0));
                    methodIl.InsertBefore(ret, Instruction.Create(OpCodes.Call, sync));
                }
            }

            TypeDefinition player = types.Single(t => t.FullName == "Game.Server.Player");
            MethodDefinition getTool = player.Methods.Single(m => m.Name == "GetToolInInventorySlot");
            MethodDefinition grabTool = player.Methods.Single(m => m.Name == "GrabToolItemFromInventory");
            MethodDefinition addTool = player.Methods.Single(m => m.Name == "AddToolItemToInventory");

            MethodDefinition killPlayer = player.Methods.Single(m => m.Name == "Kill");
            // Kill is large and contains short branches whose targets can cross
            // the sbyte limit when this hook is inserted. Expand them before
            // editing so Cecil cannot emit a wrapped branch displacement.
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
            TypeReference deathToolListType = killPlayer.Body.Variables[3].VariableType;
            MethodReference deathToolListAdd = killPlayer.Body.Instructions.Select(i => i.Operand as MethodReference).First(r => r != null && r.Name == "Add" && r.DeclaringType.FullName.StartsWith("System.Collections.Generic.List`1<Game.ClientServer.Classes.Tools.ClSvPlayerTool>"));
            MethodDefinition dropBattery = AddMethod(glove, "RiftRevivalDropBatteryOnDeath", module.TypeSystem.Void);
            dropBattery.Parameters.Add(new ParameterDefinition("tools", Mono.Cecil.ParameterAttributes.None, deathToolListType)); il = dropBattery.Body.GetILProcessor();
            Instruction noDeathBattery = Instruction.Create(OpCodes.Ret);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, installed); il.Emit(OpCodes.Brfalse, noDeathBattery);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, sync);
            il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, installed); il.Emit(OpCodes.Callvirt, deathToolListAdd);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Stfld, installed);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, reserve); il.Emit(OpCodes.Stfld, ammo);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldc_R4, 1000f); il.Emit(OpCodes.Stfld, maxAmmo);
            il.Append(noDeathBattery);

            MethodReference deathGetPersonal = player.Methods.Single(m => m.Name == "get_PersonalState" && !m.HasParameters);
            TypeDefinition deathPersonal = types.Single(t => t.FullName == "Game.ClientServer.Classes.ClSvPlayerState_Personal");
            FieldDefinition deathPersonalGlove = deathPersonal.Fields.Single(f => f.Name == "PowerGlove");
            Instruction inventoryToolsCaptured = killPlayer.Body.Instructions.First(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "AddRange" && ((MethodReference)i.Operand).DeclaringType.FullName.StartsWith("System.Collections.Generic.List`1<Game.ClientServer.Classes.Tools.ClSvPlayerTool>"));
            Instruction afterInventoryToolsCaptured = inventoryToolsCaptured.Next; ILProcessor killIl = killPlayer.Body.GetILProcessor();
            killIl.InsertBefore(afterInventoryToolsCaptured, Instruction.Create(OpCodes.Ldarg_0));
            killIl.InsertBefore(afterInventoryToolsCaptured, Instruction.Create(OpCodes.Call, deathGetPersonal));
            killIl.InsertBefore(afterInventoryToolsCaptured, Instruction.Create(OpCodes.Ldfld, deathPersonalGlove));
            killIl.InsertBefore(afterInventoryToolsCaptured, Instruction.Create(OpCodes.Ldloc_3));
            killIl.InsertBefore(afterInventoryToolsCaptured, Instruction.Create(OpCodes.Call, dropBattery));

            TypeDefinition personalForRespawn = types.Single(t => t.FullName == "Game.ClientServer.Classes.ClSvPlayerState_Personal");
            MethodDefinition getPersonalForRespawn = player.Methods.Single(m => m.Name == "get_PersonalState" && !m.HasParameters);
            FieldDefinition personalGloveForRespawn = personalForRespawn.Fields.Single(f => f.Name == "PowerGlove");
            TypeDefinition respawnClosure = types.Single(t => t.FullName == "Game.Server.Player/<>c__DisplayClass119_0");
            FieldDefinition respawningPlayer = respawnClosure.Fields.Single(f => f.Name == "<>4__this");
            MethodDefinition respawnCompleted = respawnClosure.Methods.Single(m => m.Name == "<Respawn>b__0");
            Instruction startingConditions = respawnCompleted.Body.Instructions.Single(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "SetStartingConditions");
            ILProcessor respawnIl = respawnCompleted.Body.GetILProcessor(); Instruction afterStartingConditions = startingConditions.Next;
            respawnIl.InsertBefore(afterStartingConditions, Instruction.Create(OpCodes.Ldarg_0));
            respawnIl.InsertBefore(afterStartingConditions, Instruction.Create(OpCodes.Ldfld, respawningPlayer));
            respawnIl.InsertBefore(afterStartingConditions, Instruction.Create(OpCodes.Call, getPersonalForRespawn));
            respawnIl.InsertBefore(afterStartingConditions, Instruction.Create(OpCodes.Ldfld, personalGloveForRespawn));
            respawnIl.InsertBefore(afterStartingConditions, Instruction.Create(OpCodes.Call, refillAfterRespawn));

            MethodDefinition installBattery = new MethodDefinition("RiftRevivalInstallBattery", Mono.Cecil.MethodAttributes.Public, module.TypeSystem.Boolean);
            installBattery.Parameters.Add(new ParameterDefinition("player", Mono.Cecil.ParameterAttributes.None, player));
            installBattery.Parameters.Add(new ParameterDefinition("slot", Mono.Cecil.ParameterAttributes.None, module.TypeSystem.Int32));
            installBattery.Body.InitLocals = true;
            VariableDefinition candidate = new VariableDefinition(battery);
            VariableDefinition previous = new VariableDefinition(battery);
            VariableDefinition installPower = new VariableDefinition(getPower.ReturnType);
            installBattery.Body.Variables.Add(candidate); installBattery.Body.Variables.Add(previous); installBattery.Body.Variables.Add(installPower);
            glove.Methods.Add(installBattery); il = installBattery.Body.GetILProcessor();
            Instruction validCandidate = Instruction.Create(OpCodes.Nop);
            il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldarg_2); il.Emit(OpCodes.Callvirt, getTool); il.Emit(OpCodes.Isinst, battery); il.Emit(OpCodes.Stloc, candidate);
            il.Emit(OpCodes.Ldloc, candidate); il.Emit(OpCodes.Brtrue, validCandidate); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ret); il.Append(validCandidate);
            // Reconcile the old battery/reserve total before changing slots.
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, sync);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, installed); il.Emit(OpCodes.Stloc, previous);
            il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldarg_2); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Callvirt, grabTool); il.Emit(OpCodes.Castclass, battery); il.Emit(OpCodes.Stloc, candidate);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldloc, candidate); il.Emit(OpCodes.Stfld, installed);
            // Installing a battery increases the glove total by that battery's
            // existing charge; it must not be interpreted as newly spent power.
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, reserve);
            il.Emit(OpCodes.Ldloc, candidate); il.Emit(OpCodes.Call, availablePower); il.Emit(OpCodes.Add); il.Emit(OpCodes.Stfld, ammo);
            Instruction noPrevious = Instruction.Create(OpCodes.Nop);
            il.Emit(OpCodes.Ldloc, previous); il.Emit(OpCodes.Brfalse, noPrevious);
            il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldloc, previous); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Callvirt, addTool); il.Emit(OpCodes.Pop);
            il.Append(noPrevious); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, sync); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Ret);

            MethodDefinition removeBattery = new MethodDefinition("RiftRevivalRemoveBattery", Mono.Cecil.MethodAttributes.Public, module.TypeSystem.Boolean);
            removeBattery.Parameters.Add(new ParameterDefinition("player", Mono.Cecil.ParameterAttributes.None, player));
            glove.Methods.Add(removeBattery); il = removeBattery.Body.GetILProcessor();
            Instruction hasInstalled = Instruction.Create(OpCodes.Nop);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, installed); il.Emit(OpCodes.Brtrue, hasInstalled); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ret); il.Append(hasInstalled);
            // Capture all power spent since the last update before detaching the
            // battery, then leave only the reserve in the glove's stock total.
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, sync);
            il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, installed); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Callvirt, addTool);
            Instruction added = Instruction.Create(OpCodes.Nop);
            il.Emit(OpCodes.Brtrue, added); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ret); il.Append(added);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Stfld, installed);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, reserve); il.Emit(OpCodes.Stfld, ammo);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, sync); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Ret);

            TypeDefinition packetType = types.Single(t => t.FullName == "Game.ClientServer.Packets.ServerPlayerToolState");
            TypeDefinition tupleType = types.Single(t => t.FullName == "Game.ClientServer.Packets.NetTupleIntStringString");
            TypeDefinition gaugeTupleType = types.Single(t => t.FullName == "Game.ClientServer.Packets.NetTupleIntInt");
            FieldDefinition packetToolId = packetType.Fields.Single(f => f.Name == "toolID");
            FieldDefinition packetData = packetType.Fields.Single(f => f.Name == "Data");
            FieldDefinition tupleInstalled = tupleType.Fields.Single(f => f.Name == "Item1");
            FieldDefinition tupleName = tupleType.Fields.Single(f => f.Name == "Item2");
            FieldDefinition tupleDetails = tupleType.Fields.Single(f => f.Name == "Item3");
            FieldDefinition gaugeCurrent = gaugeTupleType.Fields.Single(f => f.Name == "Item1");
            FieldDefinition gaugeMaximum = gaugeTupleType.Fields.Single(f => f.Name == "Item2");
            FieldDefinition connectionId = player.Fields.Single(f => f.Name == "ConnectionId");
            MethodReference serverNetwork = references.First(r => r.FullName == "Game.Server.NetworkController Game.Server.ControllerManager::get_Network()");
            MethodReference serverNet = references.First(r => r.FullName == "Game.Networking.NetServer Game.Server.NetworkController::get_Net()");
            MethodReference sendRpc = references.First(r => r.FullName == "System.Void Game.Networking.NetClientServer::SendRPC(System.Object,System.Int64,Game.Networking.NetWrapOrdering,System.Byte)");

            MethodDefinition sendBatteryState = AddMethod(glove, "RiftRevivalSendBatteryState", module.TypeSystem.Void);
            sendBatteryState.Parameters.Add(new ParameterDefinition("controllers", Mono.Cecil.ParameterAttributes.None, types.Single(t => t.FullName == "Game.Server.ControllerManager")));
            sendBatteryState.Parameters.Add(new ParameterDefinition("player", Mono.Cecil.ParameterAttributes.None, player));
            VariableDefinition stateTuple = new VariableDefinition(tupleType);
            VariableDefinition statePacket = new VariableDefinition(packetType);
            VariableDefinition statePower = new VariableDefinition(getPower.ReturnType);
            VariableDefinition stateMaxPower = new VariableDefinition(getPower.ReturnType);
            VariableDefinition stateBatteryPercent = new VariableDefinition(module.TypeSystem.Int32);
            VariableDefinition stateReservePercent = new VariableDefinition(module.TypeSystem.Int32);
            VariableDefinition gaugeTuple = new VariableDefinition(gaugeTupleType);
            sendBatteryState.Body.Variables.Add(stateTuple); sendBatteryState.Body.Variables.Add(statePacket); sendBatteryState.Body.Variables.Add(statePower); sendBatteryState.Body.Variables.Add(stateMaxPower); sendBatteryState.Body.Variables.Add(stateBatteryPercent); sendBatteryState.Body.Variables.Add(stateReservePercent); sendBatteryState.Body.Variables.Add(gaugeTuple);
            il = sendBatteryState.Body.GetILProcessor();
            il.Emit(OpCodes.Ldloca, stateTuple); il.Emit(OpCodes.Initobj, tupleType);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, reserve); il.Emit(OpCodes.Ldc_R4, 10f); il.Emit(OpCodes.Div); il.Emit(OpCodes.Conv_I4); il.Emit(OpCodes.Stloc, stateReservePercent);
            Instruction stateEmpty = Instruction.Create(OpCodes.Nop); Instruction stateReady = Instruction.Create(OpCodes.Nop);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, installed); il.Emit(OpCodes.Brfalse, stateEmpty);
            il.Emit(OpCodes.Ldloca, stateTuple); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Stfld, tupleInstalled);
            il.Emit(OpCodes.Ldloca, stateTuple); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, installed); il.Emit(OpCodes.Call, batteryDisplayName); il.Emit(OpCodes.Stfld, tupleName);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, installed); il.Emit(OpCodes.Callvirt, getPower); il.Emit(OpCodes.Stloc, statePower);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, installed); il.Emit(OpCodes.Callvirt, getMaxPower); il.Emit(OpCodes.Stloc, stateMaxPower);
            il.Emit(OpCodes.Ldloca, statePower); il.Emit(OpCodes.Call, toFloat); il.Emit(OpCodes.Ldloca, stateMaxPower); il.Emit(OpCodes.Call, toFloat); il.Emit(OpCodes.Div); il.Emit(OpCodes.Ldc_R4, 100f); il.Emit(OpCodes.Mul); il.Emit(OpCodes.Conv_I4); il.Emit(OpCodes.Stloc, stateBatteryPercent);
            il.Emit(OpCodes.Ldloca, stateTuple); il.Emit(OpCodes.Ldstr, "Battery {0}% | Reserve {1}%"); il.Emit(OpCodes.Ldloc, stateBatteryPercent); il.Emit(OpCodes.Box, module.TypeSystem.Int32); il.Emit(OpCodes.Ldloc, stateReservePercent); il.Emit(OpCodes.Box, module.TypeSystem.Int32); il.Emit(OpCodes.Call, module.ImportReference(typeof(string).GetMethod("Format", new[] { typeof(string), typeof(object), typeof(object) }))); il.Emit(OpCodes.Stfld, tupleDetails); il.Emit(OpCodes.Br, stateReady);
            il.Append(stateEmpty); il.Emit(OpCodes.Ldloca, stateTuple); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Stfld, tupleInstalled); il.Emit(OpCodes.Ldloca, stateTuple); il.Emit(OpCodes.Ldstr, ""); il.Emit(OpCodes.Stfld, tupleName); il.Emit(OpCodes.Ldloca, stateTuple); il.Emit(OpCodes.Ldstr, "Reserve {0}%"); il.Emit(OpCodes.Ldloc, stateReservePercent); il.Emit(OpCodes.Box, module.TypeSystem.Int32); il.Emit(OpCodes.Call, module.ImportReference(typeof(string).GetMethod("Format", new[] { typeof(string), typeof(object) }))); il.Emit(OpCodes.Stfld, tupleDetails);
            il.Append(stateReady); il.Emit(OpCodes.Ldloca, statePacket); il.Emit(OpCodes.Initobj, packetType); il.Emit(OpCodes.Ldloca, statePacket); il.Emit(OpCodes.Ldc_I4_M1); il.Emit(OpCodes.Stfld, packetToolId); il.Emit(OpCodes.Ldloca, statePacket); il.Emit(OpCodes.Ldloc, stateTuple); il.Emit(OpCodes.Box, tupleType); il.Emit(OpCodes.Stfld, packetData);
            il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Callvirt, serverNetwork); il.Emit(OpCodes.Callvirt, serverNet); il.Emit(OpCodes.Ldloc, statePacket); il.Emit(OpCodes.Box, packetType); il.Emit(OpCodes.Ldarg_2); il.Emit(OpCodes.Ldfld, connectionId); il.Emit(OpCodes.Ldc_I4_S, (sbyte)67); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Callvirt, sendRpc);
            // Send the exact combined current and maximum values separately so the
            // stock G.R.I.P gauge renders battery capacity plus the reserve.
            il.Emit(OpCodes.Ldloca, gaugeTuple); il.Emit(OpCodes.Initobj, gaugeTupleType);
            il.Emit(OpCodes.Ldloca, gaugeTuple); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, ammo); il.Emit(OpCodes.Conv_I4); il.Emit(OpCodes.Stfld, gaugeCurrent);
            il.Emit(OpCodes.Ldloca, gaugeTuple); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, maxAmmo); il.Emit(OpCodes.Conv_I4); il.Emit(OpCodes.Stfld, gaugeMaximum);
            il.Emit(OpCodes.Ldloca, statePacket); il.Emit(OpCodes.Ldloc, gaugeTuple); il.Emit(OpCodes.Box, gaugeTupleType); il.Emit(OpCodes.Stfld, packetData);
            il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Callvirt, serverNetwork); il.Emit(OpCodes.Callvirt, serverNet); il.Emit(OpCodes.Ldloc, statePacket); il.Emit(OpCodes.Box, packetType); il.Emit(OpCodes.Ldarg_2); il.Emit(OpCodes.Ldfld, connectionId); il.Emit(OpCodes.Ldc_I4_S, (sbyte)67); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Callvirt, sendRpc); il.Emit(OpCodes.Ret);

            MethodDefinition receiveBatteryState = AddMethod(glove, "RiftRevivalReceiveBatteryState", module.TypeSystem.Boolean);
            receiveBatteryState.Parameters.Add(new ParameterDefinition("data", Mono.Cecil.ParameterAttributes.None, module.TypeSystem.Object));
            VariableDefinition boxedTuple = new VariableDefinition(module.TypeSystem.Object); VariableDefinition receivedTuple = new VariableDefinition(tupleType); VariableDefinition receivedGauge = new VariableDefinition(gaugeTupleType);
            receiveBatteryState.Body.Variables.Add(boxedTuple); receiveBatteryState.Body.Variables.Add(receivedTuple); receiveBatteryState.Body.Variables.Add(receivedGauge); il = receiveBatteryState.Body.GetILProcessor();
            Instruction received = Instruction.Create(OpCodes.Nop); Instruction notGauge = Instruction.Create(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Isinst, tupleType); il.Emit(OpCodes.Stloc, boxedTuple); il.Emit(OpCodes.Ldloc, boxedTuple); il.Emit(OpCodes.Brtrue, received);
            il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Isinst, gaugeTupleType); il.Emit(OpCodes.Stloc, boxedTuple); il.Emit(OpCodes.Ldloc, boxedTuple); il.Emit(OpCodes.Brfalse, notGauge);
            il.Emit(OpCodes.Ldloc, boxedTuple); il.Emit(OpCodes.Unbox_Any, gaugeTupleType); il.Emit(OpCodes.Stloc, receivedGauge);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldloca, receivedGauge); il.Emit(OpCodes.Ldfld, gaugeCurrent); il.Emit(OpCodes.Conv_R4); il.Emit(OpCodes.Stfld, ammo);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldloca, receivedGauge); il.Emit(OpCodes.Ldfld, gaugeMaximum); il.Emit(OpCodes.Conv_R4); il.Emit(OpCodes.Stfld, maxAmmo); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Ret);
            il.Append(notGauge); il.Emit(OpCodes.Ret); il.Append(received);
            il.Emit(OpCodes.Ldloc, boxedTuple); il.Emit(OpCodes.Unbox_Any, tupleType); il.Emit(OpCodes.Stloc, receivedTuple);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldloca, receivedTuple); il.Emit(OpCodes.Ldfld, tupleInstalled); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Cgt); il.Emit(OpCodes.Stfld, clientInstalled);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldloca, receivedTuple); il.Emit(OpCodes.Ldfld, tupleName); il.Emit(OpCodes.Stfld, clientName);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldloca, receivedTuple); il.Emit(OpCodes.Ldfld, tupleDetails); il.Emit(OpCodes.Stfld, clientDetails);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Stfld, clientMessage); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Ret);

            MethodDefinition receiveServerState = glove.Methods.Single(m => m.Name == "ReceiveServerState");
            Instruction originalDataCheck = receiveServerState.Body.Instructions.First();
            ILProcessor receiveIl = receiveServerState.Body.GetILProcessor();
            receiveIl.InsertBefore(originalDataCheck, Instruction.Create(OpCodes.Ldarg_0)); receiveIl.InsertBefore(originalDataCheck, Instruction.Create(OpCodes.Ldarg_2)); receiveIl.InsertBefore(originalDataCheck, Instruction.Create(OpCodes.Call, receiveBatteryState)); receiveIl.InsertBefore(originalDataCheck, Instruction.Create(OpCodes.Brfalse, originalDataCheck)); receiveIl.InsertBefore(originalDataCheck, Instruction.Create(OpCodes.Ret));

            // Extend the existing power-glove command switch with server-authoritative
            // install and remove operations. Install receives a normal inventory index.
            MethodDefinition serverData = glove.Methods.Single(m => m.Name == "Server_OnData");
            Instruction switchInstruction = serverData.Body.Instructions.Single(i => i.OpCode == OpCodes.Switch);
            Instruction[] oldTargets = (Instruction[])switchInstruction.Operand;
            if (oldTargets.Length != 13) throw new InvalidDataException("Power-glove command table changed.");
            ILProcessor serverIl = serverData.Body.GetILProcessor();
            Instruction installHandler = Instruction.Create(OpCodes.Ldarg_S, serverData.Parameters[3]);
            serverIl.Append(installHandler); serverIl.Emit(OpCodes.Isinst, module.TypeSystem.Int32);
            Instruction installTypeOk = Instruction.Create(OpCodes.Nop);
            serverIl.Emit(OpCodes.Brtrue, installTypeOk); serverIl.Emit(OpCodes.Ret); serverIl.Append(installTypeOk);
            serverIl.Emit(OpCodes.Ldarg_0); serverIl.Emit(OpCodes.Ldloc_0); serverIl.Emit(OpCodes.Ldarg_S, serverData.Parameters[3]); serverIl.Emit(OpCodes.Unbox_Any, module.TypeSystem.Int32); serverIl.Emit(OpCodes.Call, installBattery); serverIl.Emit(OpCodes.Pop); serverIl.Emit(OpCodes.Ldarg_0); serverIl.Emit(OpCodes.Ldarg_1); serverIl.Emit(OpCodes.Ldloc_0); serverIl.Emit(OpCodes.Call, sendBatteryState); serverIl.Emit(OpCodes.Ret);
            Instruction removeHandler = Instruction.Create(OpCodes.Ldarg_0);
            serverIl.Append(removeHandler); serverIl.Emit(OpCodes.Ldloc_0); serverIl.Emit(OpCodes.Call, removeBattery); serverIl.Emit(OpCodes.Pop); serverIl.Emit(OpCodes.Ldarg_0); serverIl.Emit(OpCodes.Ldarg_1); serverIl.Emit(OpCodes.Ldloc_0); serverIl.Emit(OpCodes.Call, sendBatteryState); serverIl.Emit(OpCodes.Ret);
            Instruction stateHandler = Instruction.Create(OpCodes.Ldarg_0); serverIl.Append(stateHandler); serverIl.Emit(OpCodes.Ldarg_1); serverIl.Emit(OpCodes.Ldloc_0); serverIl.Emit(OpCodes.Call, sendBatteryState); serverIl.Emit(OpCodes.Ret);
            Instruction[] expanded = new Instruction[16]; Array.Copy(oldTargets, expanded, oldTargets.Length); expanded[13] = installHandler; expanded[14] = removeHandler; expanded[15] = stateHandler; switchInstruction.Operand = expanded;

            TypeDefinition screen = types.Single(t => t.FullName == "Game.Client.AgosStatePowerGlove");
            TypeDefinition baseState = types.Single(t => t.FullName == "Game.Client.BaseAgosState");
            TypeReference buttonType = screen.Fields.Single(f => f.Name == "m_powerGloveTransferButton").FieldType;
            TypeReference labelType = screen.Fields.Single(f => f.Name == "m_powerGloveTransferbuttonLabel").FieldType;
            TypeDefinition personal = types.Single(t => t.FullName == "Game.ClientServer.Classes.ClSvPlayerState_Personal");
            FieldDefinition controllers = baseState.Fields.Single(f => f.Name == "m_controllers");
            FieldDefinition personalGlove = personal.Fields.Single(f => f.Name == "PowerGlove");
            FieldDefinition inventory = personal.Fields.Single(f => f.Name == "ToolInventory");
            MethodReference clientPlayer = references.First(r => r.FullName == "Game.Client.PlayerController Game.Client.ControllerManager::get_Player()");
            MethodReference personalState = references.First(r => r.FullName == "Game.ClientServer.Classes.ClSvPlayerState_Personal Game.Client.PlayerController::get_PersonalState()");
            MethodDefinition sendCommand = types.Single(t => t.FullName == "Game.ClientServer.Classes.Tools.ClSvPlayerTool").Methods.Single(m => m.Name == "SendCommand");
            MethodReference inventoryCount = (MethodReference)getTool.Body.Instructions.First(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "get_Count").Operand;
            MethodReference inventoryItem = (MethodReference)getTool.Body.Instructions.First(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "get_Item").Operand;
            FieldDefinition batteryButton = new FieldDefinition("RiftRevivalBatteryButton", Mono.Cecil.FieldAttributes.Private, buttonType);
            FieldDefinition batteryLabel = new FieldDefinition("RiftRevivalBatteryLabel", Mono.Cecil.FieldAttributes.Private, labelType);
            screen.Fields.Add(batteryButton); screen.Fields.Add(batteryLabel);

            // Ask the authoritative server for the saved glove-battery state whenever
            // the G.R.I.P interface opens. This also recovers batteries installed by
            // an earlier build whose client label never updated.
            MethodDefinition clientActivate = glove.Methods.Single(m => m.Name == "Client_OnActivate");
            Instruction activateFirst = clientActivate.Body.Instructions.First();
            ILProcessor activateIl = clientActivate.Body.GetILProcessor();
            activateIl.InsertBefore(activateFirst, Instruction.Create(OpCodes.Ldarg_0));
            activateIl.InsertBefore(activateFirst, Instruction.Create(OpCodes.Ldarg_1));
            activateIl.InsertBefore(activateFirst, Instruction.Create(OpCodes.Ldc_I4, 15));
            activateIl.InsertBefore(activateFirst, Instruction.Create(OpCodes.Ldnull));
            activateIl.InsertBefore(activateFirst, Instruction.Create(OpCodes.Ldc_I4_M1));
            activateIl.InsertBefore(activateFirst, Instruction.Create(OpCodes.Callvirt, sendCommand));

            MethodDefinition toggle = AddMethod(screen, "RiftRevivalToggleBattery", module.TypeSystem.Void);
            VariableDefinition slot = new VariableDefinition(module.TypeSystem.Int32); toggle.Body.Variables.Add(slot); il = toggle.Body.GetILProcessor();
            Instruction findBattery = Instruction.Create(OpCodes.Nop); Instruction nextSlot = Instruction.Create(OpCodes.Nop); Instruction searchTest = Instruction.Create(OpCodes.Nop); Instruction toggleDone = Instruction.Create(OpCodes.Ret);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, controllers); il.Emit(OpCodes.Callvirt, clientPlayer); il.Emit(OpCodes.Callvirt, personalState); il.Emit(OpCodes.Ldfld, personalGlove); il.Emit(OpCodes.Ldfld, clientInstalled); il.Emit(OpCodes.Brfalse, findBattery);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, controllers); il.Emit(OpCodes.Callvirt, clientPlayer); il.Emit(OpCodes.Callvirt, personalState); il.Emit(OpCodes.Ldfld, personalGlove); il.Emit(OpCodes.Ldstr, "Removing battery..."); il.Emit(OpCodes.Stfld, clientMessage);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, controllers); il.Emit(OpCodes.Callvirt, clientPlayer); il.Emit(OpCodes.Callvirt, personalState); il.Emit(OpCodes.Ldfld, personalGlove);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, controllers); il.Emit(OpCodes.Ldc_I4, 14); il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Ldc_I4_M1); il.Emit(OpCodes.Callvirt, sendCommand); il.Emit(OpCodes.Br, toggleDone);
            il.Append(findBattery); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Stloc, slot); il.Emit(OpCodes.Br, searchTest);
            Instruction searchBody = Instruction.Create(OpCodes.Ldarg_0); il.Append(searchBody); il.Emit(OpCodes.Ldfld, controllers); il.Emit(OpCodes.Callvirt, clientPlayer); il.Emit(OpCodes.Callvirt, personalState); il.Emit(OpCodes.Ldfld, inventory); il.Emit(OpCodes.Ldloc, slot); il.Emit(OpCodes.Callvirt, inventoryItem); il.Emit(OpCodes.Isinst, battery); il.Emit(OpCodes.Brfalse, nextSlot);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, controllers); il.Emit(OpCodes.Callvirt, clientPlayer); il.Emit(OpCodes.Callvirt, personalState); il.Emit(OpCodes.Ldfld, personalGlove);
            il.Emit(OpCodes.Dup); il.Emit(OpCodes.Ldstr, "Installing battery..."); il.Emit(OpCodes.Stfld, clientMessage);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, controllers); il.Emit(OpCodes.Ldc_I4, 13); il.Emit(OpCodes.Ldloc, slot); il.Emit(OpCodes.Box, module.TypeSystem.Int32); il.Emit(OpCodes.Ldc_I4_M1); il.Emit(OpCodes.Callvirt, sendCommand); il.Emit(OpCodes.Br, toggleDone);
            il.Append(nextSlot); il.Emit(OpCodes.Ldloc, slot); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Add); il.Emit(OpCodes.Stloc, slot); il.Append(searchTest);
            il.Emit(OpCodes.Ldloc, slot); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, controllers); il.Emit(OpCodes.Callvirt, clientPlayer); il.Emit(OpCodes.Callvirt, personalState); il.Emit(OpCodes.Ldfld, inventory); il.Emit(OpCodes.Callvirt, inventoryCount); il.Emit(OpCodes.Blt, searchBody);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, controllers); il.Emit(OpCodes.Callvirt, clientPlayer); il.Emit(OpCodes.Callvirt, personalState); il.Emit(OpCodes.Ldfld, personalGlove); il.Emit(OpCodes.Ldstr, "No portable battery in tool inventory"); il.Emit(OpCodes.Stfld, clientMessage); il.Append(toggleDone);

            MethodDefinition screenCtor = screen.Methods.Single(m => m.IsConstructor);
            Instruction ctorRet = screenCtor.Body.Instructions.Last(i => i.OpCode == OpCodes.Ret);
            Instruction transferStore = screenCtor.Body.Instructions.Single(i => i.OpCode == OpCodes.Stfld && i.Operand is FieldReference && ((FieldReference)i.Operand).Name == "m_powerGloveTransferButton");
            MethodDefinition initBatteryUi = AddMethod(screen, "RiftRevivalInitBatteryUi", module.TypeSystem.Void);
            VariableDefinition uiColor = new VariableDefinition(screenCtor.Body.Variables[0].VariableType);
            VariableDefinition uiButton = new VariableDefinition(buttonType);
            VariableDefinition uiRectangle = new VariableDefinition(screenCtor.Body.Variables[4].VariableType);
            VariableDefinition uiLabel = new VariableDefinition(labelType);
            initBatteryUi.Body.Variables.Add(uiColor); initBatteryUi.Body.Variables.Add(uiButton); initBatteryUi.Body.Variables.Add(uiRectangle); initBatteryUi.Body.Variables.Add(uiLabel);
            ILProcessor initUiIl = initBatteryUi.Body.GetILProcessor();
            Instruction buttonStart = screenCtor.Body.Instructions.Single(i => i.Offset == 0x01fb);
            Instruction buttonEnd = screenCtor.Body.Instructions.Single(i => i.Offset == 0x0282);
            for (Instruction source = buttonStart; ; source = source.Next)
            {
                Instruction copy = Instruction.Create(OpCodes.Nop); copy.OpCode = source.OpCode; copy.Operand = source.Operand;
                if (source.Operand == screenCtor.Body.Variables[0]) copy.Operand = uiColor;
                if (source.Operand == screenCtor.Body.Variables[3]) copy.Operand = uiButton;
                if (source.OpCode == OpCodes.Stloc_3) { copy.OpCode = OpCodes.Stloc; copy.Operand = uiButton; }
                if (source.OpCode == OpCodes.Ldloc_3) { copy.OpCode = OpCodes.Ldloc; copy.Operand = uiButton; }
                if (source.Offset == 0x0207) copy.Operand = 700f;
                if (source.Offset == 0x0214) copy.Operand = 100f;
                if (source.Offset == 0x0221) copy.Operand = 750f;
                if (source.Offset == 0x022e) copy.Operand = 120f;
                if (source.Offset == 0x0245) { copy.OpCode = OpCodes.Ldc_I4; copy.Operand = 900; }
                if (source == transferStore) copy.Operand = batteryButton;
                initUiIl.Append(copy);
                if (source == buttonEnd) break;
            }
            Instruction labelStart = screenCtor.Body.Instructions.Single(i => i.Offset == 0x0283);
            Instruction labelEnd = screenCtor.Body.Instructions.Single(i => i.Offset == 0x031c);
            for (Instruction source = labelStart; ; source = source.Next)
            {
                Instruction copy = Instruction.Create(OpCodes.Nop); copy.OpCode = source.OpCode; copy.Operand = source.Operand;
                if (source.Operand == screenCtor.Body.Variables[0]) copy.Operand = uiColor;
                if (source.Operand == screenCtor.Body.Variables[4]) copy.Operand = uiRectangle;
                if (source.Operand == screenCtor.Body.Variables[5]) copy.Operand = uiLabel;
                if (source.Offset == 0x028f) copy.Operand = 700f;
                if (source.Offset == 0x029c) copy.Operand = 120f;
                if (source.Offset == 0x02a9) copy.Operand = 750f;
                if (source.Offset == 0x02b6) copy.Operand = 120f;
                if (source.Offset == 0x02c8) copy.Operand = "";
                if (source.Offset == 0x02cd) { copy.OpCode = OpCodes.Ldstr; copy.Operand = "Glove battery"; }
                if (source.Offset == 0x02d2 || source.Offset == 0x02d7 || source.Offset == 0x02dc) { copy.OpCode = OpCodes.Nop; copy.Operand = null; }
                if (source.OpCode == OpCodes.Stfld && source.Operand is FieldReference && ((FieldReference)source.Operand).Name == "m_powerGloveTransferbuttonLabel") copy.Operand = batteryLabel;
                initUiIl.Append(copy);
                if (source == labelEnd) break;
            }
            initUiIl.Emit(OpCodes.Ret);
            // The battery is equipped from Status -> Equipment. Keep the device
            // manager's stock layout and power gauge untouched.

            MethodReference setText = references.First(r => r.FullName == "System.Void Game.Framework.CGuiLabel::set_CurrentText(System.String)");
            MethodReference formatTwo = module.ImportReference(typeof(string).GetMethod("Format", new[] { typeof(string), typeof(object), typeof(object) }));
            MethodDefinition refresh = AddMethod(screen, "RiftRevivalRefreshBattery", module.TypeSystem.Void);
            VariableDefinition shownGlove = new VariableDefinition(glove); refresh.Body.Variables.Add(shownGlove); il = refresh.Body.GetILProcessor();
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, controllers); il.Emit(OpCodes.Callvirt, clientPlayer); il.Emit(OpCodes.Callvirt, personalState); il.Emit(OpCodes.Ldfld, personalGlove); il.Emit(OpCodes.Stloc, shownGlove);
            Instruction noMessage = Instruction.Create(OpCodes.Nop);
            il.Emit(OpCodes.Ldloc, shownGlove); il.Emit(OpCodes.Ldfld, clientMessage); il.Emit(OpCodes.Brfalse, noMessage); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, batteryLabel); il.Emit(OpCodes.Ldstr, "Glove battery: {0}"); il.Emit(OpCodes.Ldloc, shownGlove); il.Emit(OpCodes.Ldfld, clientMessage); il.Emit(OpCodes.Call, module.ImportReference(typeof(string).GetMethod("Format", new[] { typeof(string), typeof(object) }))); il.Emit(OpCodes.Callvirt, setText); il.Emit(OpCodes.Ret); il.Append(noMessage);
            Instruction showInstalled = Instruction.Create(OpCodes.Nop);
            il.Emit(OpCodes.Ldloc, shownGlove); il.Emit(OpCodes.Ldfld, clientInstalled); il.Emit(OpCodes.Brtrue, showInstalled);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, batteryLabel); il.Emit(OpCodes.Ldstr, "Empty | {0} - click to install"); il.Emit(OpCodes.Ldloc, shownGlove); il.Emit(OpCodes.Ldfld, clientDetails); il.Emit(OpCodes.Call, module.ImportReference(typeof(string).GetMethod("Format", new[] { typeof(string), typeof(object) }))); il.Emit(OpCodes.Callvirt, setText); il.Emit(OpCodes.Ret);
            il.Append(showInstalled);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, batteryLabel); il.Emit(OpCodes.Ldstr, "{0} | {1} - click to remove");
            il.Emit(OpCodes.Ldloc, shownGlove); il.Emit(OpCodes.Ldfld, clientName);
            il.Emit(OpCodes.Ldloc, shownGlove); il.Emit(OpCodes.Ldfld, clientDetails);
            il.Emit(OpCodes.Call, formatTwo); il.Emit(OpCodes.Callvirt, setText); il.Emit(OpCodes.Ret);
            TypeDefinition equipmentScreen = types.Single(t => t.FullName == "Game.Client.AgosStateCharacterCustomizationMenu");
            FieldDefinition equippedListBox = equipmentScreen.Fields.Single(f => f.Name == "m_equippedList");
            FieldDefinition unequippedListBox = equipmentScreen.Fields.Single(f => f.Name == "m_unequippedList");
            FieldDefinition equippedItems = equipmentScreen.Fields.Single(f => f.Name == "m_equippedItemsList");
            FieldDefinition unequippedItems = equipmentScreen.Fields.Single(f => f.Name == "m_unequippedItemsList");
            TypeReference intListType = module.ImportReference(typeof(System.Collections.Generic.List<int>));
            FieldDefinition batterySlots = new FieldDefinition("RiftRevivalBatterySlots", Mono.Cecil.FieldAttributes.Private, intListType);
            equipmentScreen.Fields.Add(batterySlots);
            MethodReference intListCtor = module.ImportReference(typeof(System.Collections.Generic.List<int>).GetConstructor(Type.EmptyTypes));
            MethodReference intListClear = module.ImportReference(typeof(System.Collections.Generic.List<int>).GetMethod("Clear"));
            MethodReference intListAdd = module.ImportReference(typeof(System.Collections.Generic.List<int>).GetMethod("Add"));
            MethodReference intListCount = module.ImportReference(typeof(System.Collections.Generic.List<int>).GetProperty("Count").GetGetMethod());
            MethodReference intListItem = module.ImportReference(typeof(System.Collections.Generic.List<int>).GetProperty("Item").GetGetMethod());
            MethodDefinition fillLists = equipmentScreen.Methods.Single(m => m.Name == "i_fillLists");
            MethodReference listBoxItems = fillLists.Body.Instructions.Select(i => i.Operand as MethodReference).First(r => r != null && r.Name == "get_Items" && r.DeclaringType.FullName == "Game.Framework.CGuiListBox");
            MethodReference stringCollectionAdd = fillLists.Body.Instructions.Select(i => i.Operand as MethodReference).First(r => r != null && r.Name == "Add" && r.DeclaringType.FullName.StartsWith("System.Collections.ObjectModel.Collection`1<System.String>"));
            MethodReference equipmentListCount = equipmentScreen.Methods.Single(m => m.Name == "OnHandleButtonEvents").Body.Instructions.Select(i => i.Operand as MethodReference).First(r => r != null && r.Name == "get_Count" && r.DeclaringType.FullName.StartsWith("System.Collections.Generic.List`1<Game.Client.AgosStateCharacterCustomizationMenu/EquippableItem>"));
            MethodReference selectedIndex = equipmentScreen.Methods.Single(m => m.Name == "OnHandleButtonEvents").Body.Instructions.Select(i => i.Operand as MethodReference).First(r => r != null && r.Name == "get_SelectedIndex");

            MethodDefinition equipmentCtor = equipmentScreen.Methods.Single(m => m.IsConstructor);
            Instruction equipmentCtorRet = equipmentCtor.Body.Instructions.Last(i => i.OpCode == OpCodes.Ret);
            ILProcessor equipmentCtorIl = equipmentCtor.Body.GetILProcessor();
            equipmentCtorIl.InsertBefore(equipmentCtorRet, Instruction.Create(OpCodes.Ldarg_0)); equipmentCtorIl.InsertBefore(equipmentCtorRet, Instruction.Create(OpCodes.Newobj, intListCtor)); equipmentCtorIl.InsertBefore(equipmentCtorRet, Instruction.Create(OpCodes.Stfld, batterySlots));

            MethodDefinition fillBatteryRows = AddMethod(equipmentScreen, "RiftRevivalFillBatteryRows", module.TypeSystem.Void);
            VariableDefinition batterySlot = new VariableDefinition(module.TypeSystem.Int32); VariableDefinition inventoryBattery = new VariableDefinition(battery); VariableDefinition rowPower = new VariableDefinition(getPower.ReturnType);
            fillBatteryRows.Body.Variables.Add(batterySlot); fillBatteryRows.Body.Variables.Add(inventoryBattery); fillBatteryRows.Body.Variables.Add(rowPower); il = fillBatteryRows.Body.GetILProcessor();
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, batterySlots); il.Emit(OpCodes.Callvirt, intListClear); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Stloc, batterySlot);
            Instruction batteryLoopTest = Instruction.Create(OpCodes.Nop); Instruction batteryLoopBody = Instruction.Create(OpCodes.Nop); Instruction batteryLoopNext = Instruction.Create(OpCodes.Nop); Instruction batteryLoopDone = Instruction.Create(OpCodes.Nop);
            il.Emit(OpCodes.Br, batteryLoopTest); il.Append(batteryLoopBody);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, controllers); il.Emit(OpCodes.Callvirt, clientPlayer); il.Emit(OpCodes.Callvirt, personalState); il.Emit(OpCodes.Ldfld, inventory); il.Emit(OpCodes.Ldloc, batterySlot); il.Emit(OpCodes.Callvirt, inventoryItem); il.Emit(OpCodes.Isinst, battery); il.Emit(OpCodes.Stloc, inventoryBattery); il.Emit(OpCodes.Ldloc, inventoryBattery); il.Emit(OpCodes.Brfalse, batteryLoopNext);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, batterySlots); il.Emit(OpCodes.Ldloc, batterySlot); il.Emit(OpCodes.Callvirt, intListAdd);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, unequippedListBox); il.Emit(OpCodes.Callvirt, listBoxItems); il.Emit(OpCodes.Ldstr, "{0} ({1:0}%)"); il.Emit(OpCodes.Ldloc, inventoryBattery); il.Emit(OpCodes.Call, batteryDisplayName); il.Emit(OpCodes.Ldloc, inventoryBattery); il.Emit(OpCodes.Callvirt, getPower); il.Emit(OpCodes.Stloc, rowPower); il.Emit(OpCodes.Ldloca, rowPower); il.Emit(OpCodes.Call, toFloat); il.Emit(OpCodes.Ldloc, inventoryBattery); il.Emit(OpCodes.Callvirt, getMaxPower); il.Emit(OpCodes.Stloc, rowPower); il.Emit(OpCodes.Ldloca, rowPower); il.Emit(OpCodes.Call, toFloat); il.Emit(OpCodes.Div); il.Emit(OpCodes.Ldc_R4, 100f); il.Emit(OpCodes.Mul); il.Emit(OpCodes.Box, module.TypeSystem.Single); il.Emit(OpCodes.Call, formatTwo); il.Emit(OpCodes.Callvirt, stringCollectionAdd);
            il.Append(batteryLoopNext); il.Emit(OpCodes.Ldloc, batterySlot); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Add); il.Emit(OpCodes.Stloc, batterySlot); il.Append(batteryLoopTest);
            il.Emit(OpCodes.Ldloc, batterySlot); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, controllers); il.Emit(OpCodes.Callvirt, clientPlayer); il.Emit(OpCodes.Callvirt, personalState); il.Emit(OpCodes.Ldfld, inventory); il.Emit(OpCodes.Callvirt, inventoryCount); il.Emit(OpCodes.Blt, batteryLoopBody);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, controllers); il.Emit(OpCodes.Callvirt, clientPlayer); il.Emit(OpCodes.Callvirt, personalState); il.Emit(OpCodes.Ldfld, personalGlove); il.Emit(OpCodes.Ldfld, clientInstalled); il.Emit(OpCodes.Brfalse, batteryLoopDone);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, equippedListBox); il.Emit(OpCodes.Callvirt, listBoxItems); il.Emit(OpCodes.Ldstr, "{0} | {1}"); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, controllers); il.Emit(OpCodes.Callvirt, clientPlayer); il.Emit(OpCodes.Callvirt, personalState); il.Emit(OpCodes.Ldfld, personalGlove); il.Emit(OpCodes.Ldfld, clientName); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, controllers); il.Emit(OpCodes.Callvirt, clientPlayer); il.Emit(OpCodes.Callvirt, personalState); il.Emit(OpCodes.Ldfld, personalGlove); il.Emit(OpCodes.Ldfld, clientDetails); il.Emit(OpCodes.Call, formatTwo); il.Emit(OpCodes.Callvirt, stringCollectionAdd);
            il.Append(batteryLoopDone); il.Emit(OpCodes.Ret);
            MethodDefinition equipmentUpdate = equipmentScreen.Methods.Single(m => m.Name == "Update");
            ILProcessor equipmentUpdateIl = equipmentUpdate.Body.GetILProcessor();
            foreach (Instruction ret in equipmentUpdate.Body.Instructions.Where(i => i.OpCode == OpCodes.Ret).ToArray()) { equipmentUpdateIl.InsertBefore(ret, Instruction.Create(OpCodes.Ldarg_0)); equipmentUpdateIl.InsertBefore(ret, Instruction.Create(OpCodes.Call, fillBatteryRows)); }

            MethodDefinition handleBatteryClick = AddMethod(equipmentScreen, "RiftRevivalHandleBatteryClick", module.TypeSystem.Boolean);
            handleBatteryClick.Parameters.Add(new ParameterDefinition("eventCode", Mono.Cecil.ParameterAttributes.None, equipmentScreen.Methods.Single(m => m.Name == "OnHandleButtonEvents").Parameters[0].ParameterType));
            handleBatteryClick.Parameters.Add(new ParameterDefinition("eventId", Mono.Cecil.ParameterAttributes.None, module.TypeSystem.Int32));
            VariableDefinition chosenIndex = new VariableDefinition(module.TypeSystem.Int32); handleBatteryClick.Body.Variables.Add(chosenIndex); il = handleBatteryClick.Body.GetILProcessor();
            Instruction tryInventoryBattery = Instruction.Create(OpCodes.Nop); Instruction clickUnhandled = Instruction.Create(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldc_I4, 202); il.Emit(OpCodes.Bne_Un, tryInventoryBattery);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, equippedListBox); il.Emit(OpCodes.Callvirt, selectedIndex); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, equippedItems); il.Emit(OpCodes.Callvirt, equipmentListCount); il.Emit(OpCodes.Blt, clickUnhandled);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, controllers); il.Emit(OpCodes.Callvirt, clientPlayer); il.Emit(OpCodes.Callvirt, personalState); il.Emit(OpCodes.Ldfld, personalGlove); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, controllers); il.Emit(OpCodes.Ldc_I4, 14); il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Ldc_I4_M1); il.Emit(OpCodes.Callvirt, sendCommand); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Ret);
            il.Append(tryInventoryBattery); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldc_I4, 203); il.Emit(OpCodes.Bne_Un, clickUnhandled);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, unequippedListBox); il.Emit(OpCodes.Callvirt, selectedIndex); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, unequippedItems); il.Emit(OpCodes.Callvirt, equipmentListCount); il.Emit(OpCodes.Sub); il.Emit(OpCodes.Stloc, chosenIndex);
            il.Emit(OpCodes.Ldloc, chosenIndex); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Blt, clickUnhandled); il.Emit(OpCodes.Ldloc, chosenIndex); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, batterySlots); il.Emit(OpCodes.Callvirt, intListCount); il.Emit(OpCodes.Bge, clickUnhandled);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, controllers); il.Emit(OpCodes.Callvirt, clientPlayer); il.Emit(OpCodes.Callvirt, personalState); il.Emit(OpCodes.Ldfld, personalGlove); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, controllers); il.Emit(OpCodes.Ldc_I4, 13); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, batterySlots); il.Emit(OpCodes.Ldloc, chosenIndex); il.Emit(OpCodes.Callvirt, intListItem); il.Emit(OpCodes.Box, module.TypeSystem.Int32); il.Emit(OpCodes.Ldc_I4_M1); il.Emit(OpCodes.Callvirt, sendCommand); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Ret);
            il.Append(clickUnhandled); il.Emit(OpCodes.Ret);

            MethodDefinition equipmentEvents = equipmentScreen.Methods.Single(m => m.Name == "OnHandleButtonEvents"); Instruction equipmentEventsFirst = equipmentEvents.Body.Instructions.First(); ILProcessor equipmentEventsIl = equipmentEvents.Body.GetILProcessor();
            equipmentEventsIl.InsertBefore(equipmentEventsFirst, Instruction.Create(OpCodes.Ldarg_0)); equipmentEventsIl.InsertBefore(equipmentEventsFirst, Instruction.Create(OpCodes.Ldarg_1)); equipmentEventsIl.InsertBefore(equipmentEventsFirst, Instruction.Create(OpCodes.Ldarg_2)); equipmentEventsIl.InsertBefore(equipmentEventsFirst, Instruction.Create(OpCodes.Call, handleBatteryClick)); equipmentEventsIl.InsertBefore(equipmentEventsFirst, Instruction.Create(OpCodes.Brfalse, equipmentEventsFirst)); equipmentEventsIl.InsertBefore(equipmentEventsFirst, Instruction.Create(OpCodes.Ret));
        }
    }
}
