using System;
using System.IO;
using System.Linq;
using Mono.Cecil;
using RiftRevival.Launcher;
class CartridgeHashes
{
    static int Main(string[] args) {
        byte[] baseline=RecipePatch.SelectRecipe(RecipePatch.Apply(File.ReadAllBytes(args[0])),false);
        string[] hashes=new string[256]; string[] networkTypes=null;
        for(int mask=0;mask<256;mask++) {
            byte[] bytes=CartridgePatch.Build(baseline,mask); hashes[mask]=RecipePatch.Hash(bytes);
            if(mask>=2) using(ModuleDefinition m=ModuleDefinition.ReadModule(new MemoryStream(bytes))) {
                string[] types=m.GetTypes().OrderBy(t=>t.MetadataToken.ToInt32()).Select(t=>t.FullName).ToArray();
                if(networkTypes==null) networkTypes=types;
                else if(!types.SequenceEqual(networkTypes)) throw new Exception("Type ordering changed between selections.");
            }
            if(mask==2 || mask==62 || mask==63 || mask==64 || mask==127 || mask==128 || mask==192 || mask==255) { if(hashes[mask]!=RecipePatch.Hash(CartridgePatch.Build(baseline,mask))) throw new Exception("Build is not deterministic."); File.WriteAllBytes(Path.Combine(args[2],"selection-"+mask+".exe"),bytes); }
        }
        File.WriteAllLines(args[1],hashes);
        Console.WriteLine("PASS: all 256 selections built; game type order identical; repeated glove/backpack builds deterministic.");return 0;
    }
}
