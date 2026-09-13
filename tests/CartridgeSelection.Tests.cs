using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Collections;
using RiftRevival.Launcher;

public class CartridgeRuntimeCheck : MarshalByRefObject
{
    public int Check(string exe,string dependencies,int mask)
    {
        AppDomain.CurrentDomain.AssemblyResolve+=delegate(object sender,ResolveEventArgs e){string p=Path.Combine(dependencies,new AssemblyName(e.Name).Name+".dll");return File.Exists(p)?Assembly.LoadFrom(p):null;};
        Assembly game=Assembly.LoadFile(exe);
        Type cart=game.GetType("Game.ClientServer.Classes.Tools.ClSvPlayerTool_Cartridge",true);
        BindingFlags flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        MethodInfo dispatch=cart.GetMethod("RiftRevivalApplyCartridgeSelection");
        RuntimeHelpers.PrepareMethod(dispatch.MethodHandle);
        RuntimeHelpers.PrepareMethod(game.GetType("Game.Server.SvCommands",true).GetMethod("c_giveUpgradeCartridge",BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static).MethodHandle);
        Type kind=dispatch.GetParameters()[0].ParameterType;
        FieldInfo values=cart.GetField("m_upgradeValues",flags),duration=cart.BaseType.GetField("m_durability",flags),maximum=cart.BaseType.GetField("m_maxDurability",flags),timer=cart.GetField("m_syncTimer",flags),slot=cart.GetField("m_tarSlotId",flags);
        int checks=0;
        foreach(ModDefinition spec in ModCatalog.All.Skip(1)) {
            object item=FormatterServices.GetUninitializedObject(cart); IDictionary dict=(IDictionary)Activator.CreateInstance(values.FieldType);values.SetValue(item,dict);
            Type fp=values.FieldType.GetGenericArguments()[1];object initial=fp.GetMethod("CD",new[]{typeof(double)}).Invoke(null,new object[]{.5});object unrelated=Enum.Parse(kind,"UT_Scanners_ScanPower");dict.Add(unrelated,initial);
            duration.SetValue(item,321);maximum.SetValue(item,654);timer.SetValue(item,99);slot.SetValue(item,7);
            dispatch.Invoke(item,new[]{Enum.Parse(kind,spec.Selector)});
            bool on=(mask&spec.Bit)!=0;
            if(on) {
                if(dict.Count!=spec.Effects.Length)throw new Exception("Unexpected effect count: "+spec.Name);
                for(int i=0;i<spec.Effects.Length;i++) {object key=Enum.Parse(kind,spec.Effects[i]);if(!dict.Contains(key))throw new Exception("Missing effect");double actual=(double)fp.GetMethod("ToDouble").Invoke(dict[key],null);if(Math.Abs(actual-spec.Values[i])>.0001)throw new Exception("Wrong effect");}
                if((int)duration.GetValue(item)!=spec.Ticks || (int)maximum.GetValue(item)!=spec.Ticks || (int)timer.GetValue(item)!=0 || (int)slot.GetValue(item)!=-1)throw new Exception("Wrong research state");
            } else if(dict.Count!=1 || !dict.Contains(unrelated) || (int)duration.GetValue(item)!=321 || (int)maximum.GetValue(item)!=654 || (int)timer.GetValue(item)!=99 || (int)slot.GetValue(item)!=7)throw new Exception("Disabled cartridge changed an item: "+spec.Name);
            // Unknown selectors are always untouched, even when all mods are enabled.
            int oldCount=dict.Count,oldDuration=(int)duration.GetValue(item);dispatch.Invoke(item,new[]{unrelated});
            if(dict.Count!=oldCount || (int)duration.GetValue(item)!=oldDuration)throw new Exception("Unrelated selector changed");
            checks++;
        }
        return checks;
    }
}
class CartridgeSelectionTests
{
    static int Main(string[] args) {
        try {
            string root=Path.GetFullPath(args[1]);Directory.CreateDirectory(root);
            byte[] baseline=RecipePatch.SelectRecipe(RecipePatch.Apply(File.ReadAllBytes(Path.Combine(args[0],"IR.exe"))),false);int checks=0;
            for(int mask=2;mask<64;mask++) {
                byte[] bytes=CartridgePatch.Apply(baseline,mask);string path=Path.Combine(root,"selection.exe");File.WriteAllBytes(path,bytes);
                AppDomain domain=AppDomain.CreateDomain("cartridge-"+mask);
                try {CartridgeRuntimeCheck test=(CartridgeRuntimeCheck)domain.CreateInstanceFromAndUnwrap(Assembly.GetExecutingAssembly().Location,typeof(CartridgeRuntimeCheck).FullName);checks+=test.Check(path,Path.GetFullPath(args[0]),mask);}finally{AppDomain.Unload(domain);}
                if(mask%10==0)Console.WriteLine("Verified combinations through "+mask);
            }
            Console.WriteLine("PASS: "+checks+" cartridge selector checks across 62 combinations; enabled effects, disabled preservation, research state, unrelated selectors and admin JIT. No game started.");return 0;
        }catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }
}
