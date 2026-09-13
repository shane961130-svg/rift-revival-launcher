using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace RiftRevival.Launcher
{
    // Build from the exact supported original, never from an earlier rewritten binary.
    // Every cartridge combination has identical types and method declarations.
    public static class CartridgePatch
    {
        private static string[] hashes;
        public static string LegacyHash(int mask) {
            ModCatalog.Ids(mask);
            if(mask>=64)return "";
            using(Stream s=Assembly.GetExecutingAssembly().GetManifestResourceStream("Mods.legacyhashes"))
            using(StreamReader r=new StreamReader(s))return r.ReadToEnd().Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries)[mask];
        }
        public static bool IsPreviousHash(int mask,string value) {
            foreach(string backpackResource in new[]{"Mods.backpackv1hashes","Mods.backpackprinterv1hashes"})
                using(Stream backpackStream=Assembly.GetExecutingAssembly().GetManifestResourceStream(backpackResource))
                using(StreamReader backpackReader=new StreamReader(backpackStream))
                    if(value==backpackReader.ReadToEnd().Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries)[mask])return true;
            if(value==LegacyHash(mask))return true;
            if(mask>=64)return false;
            foreach(string resource in new[]{"Mods.spacingv1hashes","Mods.spacingv2hashes","Mods.detailsv1hashes"})
                using(Stream s=Assembly.GetExecutingAssembly().GetManifestResourceStream(resource))
                using(StreamReader r=new StreamReader(s))if(value==r.ReadToEnd().Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries)[mask])return true;
            return false;
        }
        public static string ExpectedHash(int mask)
        {
            ModCatalog.Ids(mask);
            if(mask==0) return RecipePatch.DisabledHash;
            if(mask==1) return RecipePatch.InstalledHash;
            if(hashes==null) using(Stream s=Assembly.GetExecutingAssembly().GetManifestResourceStream("Mods.hashes"))
                using(StreamReader r=new StreamReader(s)) hashes=r.ReadToEnd().Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries);
            if(hashes.Length!=256) throw new InvalidDataException("Mod verification table is missing.");
            return hashes[mask];
        }
        public static byte[] Apply(byte[] baseline,int mask)
        {
            byte[] result=Build(baseline,mask);
            if(RecipePatch.Hash(result)!=ExpectedHash(mask)) throw new InvalidDataException("Selected cartridge mods failed verification.");
            return result;
        }
        public static byte[] Build(byte[] baseline,int mask)
        {
            ModCatalog.Ids(mask);
            if(RecipePatch.Hash(baseline)!=RecipePatch.DisabledHash) throw new InvalidDataException("The original mod-copy baseline is not recognized.");
            byte[] recipe=RecipePatch.SelectRecipe(baseline,(mask&1)!=0);
            if(mask<2) return recipe;
            using(MemoryStream input=new MemoryStream(recipe,false))
            using(ModuleDefinition m=ModuleDefinition.ReadModule(input)) {
                TypeDefinition[] types=m.GetTypes().ToArray();
                if((mask&64)!=0)GloveBatteryPatch.Apply(m);
                if((mask&128)!=0)BackpackPatch.Apply(m);
                UpgradeDetailsPatch.Apply(m);
                // Only the existing Cartridge Options button: keep it below the effect rows,
                // above the power/eject row. No new UI helper, labels, panels or types.
                MethodDefinition layout=types.Single(t=>t.FullName=="Game.Client.AgosStateDataCoreTerminal/CartridgeDetailsWindow_Cartridge").Methods.Single(t=>t.Name=="Initialize");
                int[] offsets={0x77,0x91,0xcf};float[] previous={704,144,100},replacement={790,86,60};
                for(int index=0;index<offsets.Length;index++) {
                    Instruction site=layout.Body.Instructions.Single(i=>i.Offset==offsets[index]);
                    if(site.OpCode!=OpCodes.Ldc_R4||(float)site.Operand!=previous[index])throw new InvalidDataException("Cartridge spacing layout has changed.");
                    site.Operand=replacement[index];
                }
                TypeDefinition cart=types.Single(t=>t.FullName=="Game.ClientServer.Classes.Tools.ClSvPlayerTool_Cartridge");
                TypeDefinition parent=types.Single(t=>t.FullName=="Game.ClientServer.Classes.Tools.ClSvPlayerTool_ExecutableTool");
                TypeDefinition kind=types.Single(t=>t.FullName=="Game.Ship.Lockstep.State.UpgradesHelper/UpgradeTypes");
                MethodDefinition admin=types.Single(t=>t.FullName=="Game.Server.SvCommands").Methods.Single(t=>t.Name=="c_giveUpgradeCartridge");
                MethodDefinition generator=cart.Methods.Single(t=>t.Name=="GenerateCartridgeStats_unReliable");
                MethodReference setItem=(MethodReference)generator.Body.Instructions.First(i=>i.Operand is MethodReference && ((MethodReference)i.Operand).Name=="set_Item").Operand;
                MethodReference cd=(MethodReference)generator.Body.Instructions.First(i=>i.Operand is MethodReference && ((MethodReference)i.Operand).FullName.Contains("FP::CD")).Operand;
                FieldDefinition values=cart.Fields.Single(f=>f.Name=="m_upgradeValues");
                MethodReference clear=new MethodReference("Clear",m.TypeSystem.Void,values.FieldType) { HasThis=true };
                MethodDefinition dispatch=new MethodDefinition("RiftRevivalApplyCartridgeSelection",Mono.Cecil.MethodAttributes.Public,m.TypeSystem.Void);
                dispatch.Parameters.Add(new ParameterDefinition("selector",Mono.Cecil.ParameterAttributes.None,kind));
                cart.Methods.Add(dispatch);
                ILProcessor il=dispatch.Body.GetILProcessor();
                foreach(ModDefinition spec in ModCatalog.All.Skip(1).Where(s=>s.Selector!=null)) {
                    MethodDefinition preset=new MethodDefinition("RiftRevival"+spec.Name.Replace(" ",""),Mono.Cecil.MethodAttributes.Public,m.TypeSystem.Void);
                    cart.Methods.Add(preset); ILProcessor p=preset.Body.GetILProcessor();
                    p.Emit(OpCodes.Ldarg_0); p.Emit(OpCodes.Ldfld,values); p.Emit(OpCodes.Callvirt,clear);
                    for(int i=0;i<spec.Effects.Length;i++) {
                        p.Emit(OpCodes.Ldarg_0); p.Emit(OpCodes.Ldfld,values);
                        p.Emit(OpCodes.Ldc_I4,(int)kind.Fields.Single(f=>f.Name==spec.Effects[i]).Constant);
                        p.Emit(OpCodes.Ldc_R8,spec.Values[i]); p.Emit(OpCodes.Call,cd); p.Emit(OpCodes.Callvirt,setItem);
                    }
                    FieldDefinition[] fields={cart.Fields.Single(f=>f.Name=="m_syncTimer"),cart.Fields.Single(f=>f.Name=="m_tarSlotId"),parent.Fields.Single(f=>f.Name=="m_durability"),parent.Fields.Single(f=>f.Name=="m_maxDurability")};
                    int[] numbers={0,-1,spec.Ticks,spec.Ticks};
                    for(int i=0;i<fields.Length;i++){p.Emit(OpCodes.Ldarg_0);p.Emit(OpCodes.Ldc_I4,numbers[i]);p.Emit(OpCodes.Stfld,fields[i]);}
                    p.Emit(OpCodes.Ret);
                    Instruction next=Instruction.Create(OpCodes.Nop);
                    il.Emit(OpCodes.Ldc_I4,(mask&spec.Bit)!=0?1:0); il.Emit(OpCodes.Brfalse,next);
                    il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldc_I4,(int)kind.Fields.Single(f=>f.Name==spec.Selector).Constant); il.Emit(OpCodes.Bne_Un,next);
                    il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call,preset); il.Emit(OpCodes.Ret); il.Append(next);
                }
                il.Emit(OpCodes.Ret);
                if(admin.Body.Variables[6].VariableType.FullName!=kind.FullName) throw new InvalidDataException("Cartridge command selector changed.");
                Instruction anchor=admin.Body.Instructions.Single(i=>i.Operand is MethodReference && ((MethodReference)i.Operand).FullName.Contains("ToolHelpers::GenerateTool("));
                ILProcessor a=admin.Body.GetILProcessor();
                foreach(Instruction i in new[]{Instruction.Create(OpCodes.Dup),Instruction.Create(OpCodes.Castclass,cart),Instruction.Create(OpCodes.Ldloc,admin.Body.Variables[6]),Instruction.Create(OpCodes.Call,dispatch)}) { a.InsertAfter(anchor,i);anchor=i; }
                using(MemoryStream output=new MemoryStream()) { m.Write(output,new WriterParameters{Timestamp=0}); return output.ToArray(); }
            }
        }
    }
}

namespace RiftRevival.Launcher
{
    // UI-only additions to existing types; no new game types or serialized state.
    internal static class UpgradeDetailsPatch
    {
        static MethodReference Ref(TypeReference owner,string name,TypeReference result,params TypeReference[] args) {
            var r=new MethodReference(name,result,owner){HasThis=true};
            foreach(var a in args)r.Parameters.Add(new ParameterDefinition(a));return r;
        }
        static MethodDefinition Method(TypeDefinition owner,string name,TypeReference result,params TypeReference[] args) {
            var r=new MethodDefinition(name,Mono.Cecil.MethodAttributes.Public,result);
            foreach(var a in args)r.Parameters.Add(new ParameterDefinition(a));owner.Methods.Add(r);r.Body.InitLocals=true;return r;
        }
        static void Before(MethodDefinition method,Instruction anchor,params Instruction[] additions) {
            foreach(var i in additions)method.Body.GetILProcessor().InsertBefore(anchor,i);
        }
        public static void Apply(ModuleDefinition m) {
            var types=m.GetTypes().ToArray();
            var overview=types.Single(t=>t.FullName=="Game.Client.AgosStateDataCoreOverview");
            var tile=types.Single(t=>t.FullName=="Game.Client.DataCore_UpgradeWindow");
            var item=types.Single(t=>t.FullName=="Game.Client.AgosGui_UpgradeItem");
            var popup=types.Single(t=>t.FullName=="Game.Client.ConfirmPopup");
            var basis=types.Single(t=>t.FullName=="Game.Client.BaseAgosState");
            var popupBasis=types.Single(t=>t.FullName=="Game.Client.AgosGui_Popup");
            var original=overview.Methods.ToArray();
            var all=types.SelectMany(t=>t.Methods).Where(t=>t.HasBody).SelectMany(t=>t.Body.Instructions).ToArray();
            Func<string,string,MethodReference> call=(owner,name)=>all.Select(i=>i.Operand as MethodReference).First(r=>r!=null&&r.DeclaringType.FullName==owner&&r.Name==name);
            var state=basis.Fields.Single(f=>f.Name=="stateWindow");
            var controllers=basis.Fields.Single(f=>f.Name=="m_controllers");
            var scale=overview.Fields.Single(f=>f.Name=="m_guiScale");
            var engine=types.Single(t=>t.FullName=="Game.Configuration.EngineProvider").Fields.Single(f=>f.Name=="Engine");
            var placeholder=call("Microsoft.Xna.Framework.Rectangle","get_Placeholder");
            var windowCtor=call("Game.Framework.CGuiWindow",".ctor");
            var addWindow=call("Game.Framework.CGuiWindow","AddWindow");
            var list=tile.Fields.Single(f=>f.Name=="m_list");
            var count=call(list.FieldType.FullName,"get_Count");
            var getItem=call(list.FieldType.FullName,"get_Item");
            var upgradeType=item.Fields.Single(f=>f.Name=="UpgradeType");
            var amount=item.Fields.Single(f=>f.Name=="UpgradeAmount");
            var getStats=call("Game.Ship.Lockstep.State.UpgradesHelper","GetUpgradeStats");
            var statText=types.Single(t=>t.FullName==getStats.ReturnType.FullName).Fields.Single(f=>f.Name=="stringRepresentation");
            var content=new FieldDefinition("RiftRevivalDetailsContent",Mono.Cecil.FieldAttributes.Private,state.FieldType);overview.Fields.Add(content);
            var panel=new FieldDefinition("RiftRevivalDetailsPopup",Mono.Cecil.FieldAttributes.Private,popup);overview.Fields.Add(panel);
            var selected=new FieldDefinition("RiftRevivalDetailsTile",Mono.Cecil.FieldAttributes.Private,tile);overview.Fields.Add(selected);
            var offset=new FieldDefinition("RiftRevivalDetailsOffset",Mono.Cecil.FieldAttributes.Private,m.TypeSystem.Int32);overview.Fields.Add(offset);
            // Keep the original overview under its own window, with the popup as a sibling.
            var init=Method(overview,"RiftRevivalInitDetails",m.TypeSystem.Void);var il=init.Body.GetILProcessor();
            il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Ldsfld,engine);il.Emit(OpCodes.Call,placeholder);il.Emit(OpCodes.Ldc_I4_0);il.Emit(OpCodes.Newobj,windowCtor);il.Emit(OpCodes.Stfld,content);
            il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Ldfld,state);il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Ldfld,content);il.Emit(OpCodes.Callvirt,addWindow);il.Emit(OpCodes.Ret);
            foreach(var fn in original)if(fn.HasBody) {
                foreach(var ins in fn.Body.Instructions)if(ins.OpCode==OpCodes.Ldfld&&ins.Operand is FieldReference&&((FieldReference)ins.Operand).FullName==state.FullName)ins.Operand=content;
                if(fn.IsConstructor) {
                    var anchor=fn.Body.Instructions.Single(i=>i.OpCode==OpCodes.Call&&i.Operand is MethodReference&&((MethodReference)i.Operand).DeclaringType.FullName==basis.FullName&&((MethodReference)i.Operand).Name==".ctor").Next;
                    Before(fn,anchor,Instruction.Create(OpCodes.Ldarg_0),Instruction.Create(OpCodes.Call,init));
                }
            }
            var length=Method(tile,"RiftRevivalEffectCount",m.TypeSystem.Int32);il=length.Body.GetILProcessor();il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Ldfld,list);il.Emit(OpCodes.Callvirt,count);il.Emit(OpCodes.Ret);
            // Three rows per page, leaving room for larger text. Values come from the tile's live list, never the preset catalog.
            var text=Method(tile,"RiftRevivalEffectText",m.TypeSystem.String,m.TypeSystem.Int32);
            var sb=new VariableDefinition(m.ImportReference(typeof(System.Text.StringBuilder)));var n=new VariableDefinition(m.TypeSystem.Int32);var current=new VariableDefinition(item);var label=new VariableDefinition(m.TypeSystem.String);
            text.Body.Variables.Add(sb);text.Body.Variables.Add(n);text.Body.Variables.Add(current);text.Body.Variables.Add(label);il=text.Body.GetILProcessor();
            il.Emit(OpCodes.Ldstr,"Applied upgrades\n");il.Emit(OpCodes.Newobj,m.ImportReference(typeof(System.Text.StringBuilder).GetConstructor(new[]{typeof(string)})));il.Emit(OpCodes.Stloc,sb);
            il.Emit(OpCodes.Ldarg_1);il.Emit(OpCodes.Stloc,n);var test=Instruction.Create(OpCodes.Nop);var done=Instruction.Create(OpCodes.Nop);il.Emit(OpCodes.Br,test);
            var loop=Instruction.Create(OpCodes.Ldarg_0);il.Append(loop);il.Emit(OpCodes.Ldfld,list);il.Emit(OpCodes.Ldloc,n);il.Emit(OpCodes.Callvirt,getItem);il.Emit(OpCodes.Stloc,current);
            il.Emit(OpCodes.Ldloc,current);il.Emit(OpCodes.Ldfld,upgradeType);il.Emit(OpCodes.Call,getStats);il.Emit(OpCodes.Ldfld,statText);il.Emit(OpCodes.Stloc,label);
            var enums=types.Single(t=>t.FullName==upgradeType.FieldType.FullName);
            var names=new System.Collections.Generic.Dictionary<string,string>();
            foreach(var mod in ModCatalog.All.Skip(1))for(int j=0;j<mod.Effects.Length;j++) {
                string line=mod.Description.Split('\n')[j];int cut=line.LastIndexOf(' ');names[mod.Effects[j]]=line.Substring(0,cut);
            }
            foreach(var pair in names.OrderBy(p=>p.Key,StringComparer.Ordinal)) {
                var next=Instruction.Create(OpCodes.Nop);il.Emit(OpCodes.Ldloc,current);il.Emit(OpCodes.Ldfld,upgradeType);il.Emit(OpCodes.Ldc_I4,(int)enums.Fields.Single(f=>f.Name==pair.Key).Constant);il.Emit(OpCodes.Bne_Un,next);il.Emit(OpCodes.Ldstr,pair.Value);il.Emit(OpCodes.Stloc,label);il.Append(next);
            }
                        var negative=Instruction.Create(OpCodes.Ldstr,"~-CFFFFFF{0}: ~-C0000FF{1:+0.##;-0.##;0}%~-CFFFFFF");
            var formatReady=Instruction.Create(OpCodes.Nop);
            il.Emit(OpCodes.Ldloc,sb);il.Emit(OpCodes.Ldloc,current);il.Emit(OpCodes.Ldfld,amount);il.Emit(OpCodes.Ldc_R4,0f);il.Emit(OpCodes.Blt,negative);
            il.Emit(OpCodes.Ldstr,"~-CFFFFFF{0}: ~-C00FF00{1:+0.##;-0.##;0}%~-CFFFFFF");il.Emit(OpCodes.Br,formatReady);il.Append(negative);il.Append(formatReady);il.Emit(OpCodes.Ldloc,label);il.Emit(OpCodes.Ldloc,current);il.Emit(OpCodes.Ldfld,amount);il.Emit(OpCodes.Ldc_R4,100f);il.Emit(OpCodes.Mul);il.Emit(OpCodes.Box,m.TypeSystem.Single);
            il.Emit(OpCodes.Call,m.ImportReference(typeof(string).GetMethod("Format",new[]{typeof(string),typeof(object),typeof(object)})));il.Emit(OpCodes.Callvirt,m.ImportReference(typeof(System.Text.StringBuilder).GetMethod("AppendLine",new[]{typeof(string)})));il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ldloc,n);il.Emit(OpCodes.Ldc_I4_1);il.Emit(OpCodes.Add);il.Emit(OpCodes.Stloc,n);il.Append(test);
            il.Emit(OpCodes.Ldloc,n);il.Emit(OpCodes.Ldarg_1);il.Emit(OpCodes.Ldc_I4_3);il.Emit(OpCodes.Add);il.Emit(OpCodes.Bge,done);
            il.Emit(OpCodes.Ldloc,n);il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Ldfld,list);il.Emit(OpCodes.Callvirt,count);il.Emit(OpCodes.Blt,loop);il.Append(done);
            il.Emit(OpCodes.Ldloc,sb);il.Emit(OpCodes.Callvirt,m.ImportReference(typeof(object).GetMethod("ToString")));il.Emit(OpCodes.Ret);
            // Configure only this popup instance; ordinary confirmation dialogs remain unchanged.
            var configure=Method(popup,"RiftRevivalConfigureDetails",m.TypeSystem.Void,m.TypeSystem.Boolean);il=configure.Body.GetILProcessor();
                        var confirm=popup.Fields.Single(f=>f.Name=="m_confirmbutton");
            var textLabel=popup.Fields.Single(f=>f.Name=="m_textLabel");
            il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Ldfld,textLabel);il.Emit(OpCodes.Ldc_I4_0);il.Emit(OpCodes.Callvirt,Ref(textLabel.FieldType,"set_IsCentered",m.TypeSystem.Void,m.TypeSystem.Boolean));
            il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Ldfld,textLabel);il.Emit(OpCodes.Ldc_I4_0);il.Emit(OpCodes.Callvirt,Ref(textLabel.FieldType,"set_IsCenteredVertically",m.TypeSystem.Void,m.TypeSystem.Boolean));
            il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Ldstr,"Next effects");il.Emit(OpCodes.Call,popup.Methods.Single(f=>f.Name=="SetConfirmButtonTextOverride"));
            il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Ldstr,"Close");il.Emit(OpCodes.Call,popup.Methods.Single(f=>f.Name=="SetCancelButtonTextOverride"));
            il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Ldfld,confirm);il.Emit(OpCodes.Ldarg_1);il.Emit(OpCodes.Callvirt,call("Game.Framework.CGuiImage","SetVisibleAndEnabled"));il.Emit(OpCodes.Ret);
            var refresh=Method(overview,"RiftRevivalRefreshDetails",m.TypeSystem.Void);il=refresh.Body.GetILProcessor();
            il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Ldfld,panel);il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Ldfld,selected);il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Ldfld,offset);il.Emit(OpCodes.Callvirt,text);il.Emit(OpCodes.Callvirt,popup.Methods.Single(f=>f.Name=="SetText"));
            il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Ldfld,panel);il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Ldfld,selected);il.Emit(OpCodes.Callvirt,length);il.Emit(OpCodes.Ldc_I4_3);il.Emit(OpCodes.Cgt);il.Emit(OpCodes.Callvirt,configure);
            il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Ldfld,panel);il.Emit(OpCodes.Ldc_I4_1);il.Emit(OpCodes.Callvirt,popup.Methods.Single(f=>f.Name=="SwitchActive"));il.Emit(OpCodes.Ret);
            var objectArgs=types.Single(t=>t.FullName=="Game.Client.ObjectEventArgs");
            var nextPage=Method(overview,"RiftRevivalNextDetails",m.TypeSystem.Void,m.TypeSystem.Object,objectArgs);il=nextPage.Body.GetILProcessor();
            il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Ldfld,offset);il.Emit(OpCodes.Ldc_I4_3);il.Emit(OpCodes.Add);il.Emit(OpCodes.Stfld,offset);
            var showNext=Instruction.Create(OpCodes.Ldarg_0);il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Ldfld,offset);il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Ldfld,selected);il.Emit(OpCodes.Callvirt,length);il.Emit(OpCodes.Blt,showNext);
            il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Ldc_I4_0);il.Emit(OpCodes.Stfld,offset);il.Append(showNext);il.Emit(OpCodes.Call,refresh);il.Emit(OpCodes.Ret);
            var show=Method(overview,"RiftRevivalShowDetails",m.TypeSystem.Void,m.TypeSystem.Object,item);il=show.Body.GetILProcessor();
            il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Ldarg_1);il.Emit(OpCodes.Castclass,tile);il.Emit(OpCodes.Stfld,selected);il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Ldc_I4_0);il.Emit(OpCodes.Stfld,offset);
            var ready=Instruction.Create(OpCodes.Ldarg_0);il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Ldfld,panel);il.Emit(OpCodes.Brtrue,ready);
            il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Ldsfld,engine);il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Ldfld,controllers);il.Emit(OpCodes.Call,placeholder);il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Ldfld,content);il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Ldfld,scale);il.Emit(OpCodes.Ldc_R4,65f);il.Emit(OpCodes.Ldc_I4_1);il.Emit(OpCodes.Ldc_I4_0);il.Emit(OpCodes.Newobj,popup.Methods.Single(f=>f.IsConstructor));il.Emit(OpCodes.Stfld,panel);
            il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Ldfld,state);il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Ldfld,panel);il.Emit(OpCodes.Callvirt,addWindow);
            var addNext=popup.Methods.Single(f=>f.Name=="add_ConfirmButtonClicked");
            il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Ldfld,panel);il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Ldftn,nextPage);il.Emit(OpCodes.Newobj,Ref(addNext.Parameters[0].ParameterType,".ctor",m.TypeSystem.Void,m.TypeSystem.Object,m.TypeSystem.IntPtr));il.Emit(OpCodes.Callvirt,addNext);
            il.Append(ready);il.Emit(OpCodes.Call,refresh);il.Emit(OpCodes.Ret);
            var update=overview.Methods.Single(f=>f.Name=="i_updateWindows");var creation=update.Body.Instructions.Single(i=>i.OpCode==OpCodes.Newobj&&i.Operand is MethodReference&&((MethodReference)i.Operand).DeclaringType.FullName==tile.FullName);
            if(creation.Next.OpCode!=OpCodes.Stloc_3)throw new InvalidDataException("Overview tile construction changed.");
            var addClick=tile.Methods.Single(f=>f.Name=="add_OnClicked");
            Before(update,creation.Next.Next,Instruction.Create(OpCodes.Ldloc_3),Instruction.Create(OpCodes.Ldarg_0),Instruction.Create(OpCodes.Ldftn,show),Instruction.Create(OpCodes.Newobj,Ref(addClick.Parameters[0].ParameterType,".ctor",m.TypeSystem.Void,m.TypeSystem.Object,m.TypeSystem.IntPtr)),Instruction.Create(OpCodes.Callvirt,addClick));
            // Expansion can push existing short branches out of range.
            foreach(var fn in original.Where(f=>f.IsConstructor||f.Name=="i_updateWindows"))if(fn.HasBody)foreach(var ins in fn.Body.Instructions) {
                if(ins.OpCode==OpCodes.Br_S)ins.OpCode=OpCodes.Br;else if(ins.OpCode==OpCodes.Blt_S)ins.OpCode=OpCodes.Blt;else if(ins.OpCode==OpCodes.Bge_S)ins.OpCode=OpCodes.Bge;else if(ins.OpCode==OpCodes.Ble_S)ins.OpCode=OpCodes.Ble;else if(ins.OpCode==OpCodes.Leave_S)ins.OpCode=OpCodes.Leave;
            }
        }
    }
}
