using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace RiftRevival.Launcher
{
    // This first catalog entry contains data edits only. It never loads game code.
    public static class RecipePatch
    {
        public const string ModId = "crystal-50-50-v1";
        public const string Version = "0.1.0";
        public const string Profile = "RiftRevivalPlay1";
        public const string StockHash = "D5FC85E8FEAA39593EC1FCFDB4AD1E68D59B47CF0F21F475F1CB1FF08CE77A3D";
        public const string RecipeOnlyHash = "5E0B85922758214BD350843ED2FD22362B1B7AAE183A446F784283FEB58BBBDF";
        // Independently reproduced with the existing PowerShell recipe/profile tools.
        public const string InstalledHash = "6D7C67D993EE2C20E891CCBB231DFF32E2EBD25BF27484B69B645343F3D8EE0B";
        public const string DisabledHash = "694CB588F38C85BBE00E64CBD144CDA1DA2735466E7CA3CC59B8459E584D48E4";

        public static byte[] SelectRecipe(byte[] installed, bool enabled)
        {
            string hash = Hash(installed);
            if (hash != InstalledHash && hash != DisabledHash) throw new InvalidDataException("The managed game copy changed; verify it before changing mods.");
            byte[] result = (byte[])installed.Clone();
            PeData pe = new PeData(result);
            int operand = pe.MethodCode(3386392) + 1958;
            if (result[operand - 1] != 0x1f || (result[operand] != 10 && result[operand] != 50)) throw new InvalidDataException("Recipe instruction changed.");
            result[operand] = (byte)(enabled ? 50 : 10);
            if (Hash(result) != (enabled ? InstalledHash : DisabledHash)) throw new InvalidDataException("Selected recipe failed verification.");
            return result;
        }

        public static string Hash(byte[] bytes)
        {
            using (SHA256 sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "");
        }

        public static string FileHash(string path)
        {
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (SHA256 sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
        }

        public static byte[] Apply(byte[] original)
        {
            if (Hash(original) != StockHash)
                throw new InvalidDataException("This game version is not supported by the recipe mod. No Steam files were changed.");
            byte[] bytes = (byte[])original.Clone();
            PeData pe = new PeData(bytes);
            int code = pe.MethodCode(3386392);
            if (bytes[code + 1946] != 0x1f || bytes[code + 1947] != 50 ||
                bytes[code + 1957] != 0x1f || bytes[code + 1958] != 10)
                throw new InvalidDataException("Recipe instruction validation failed.");
            bytes[code + 1958] = 50;
            if (Hash(bytes) != RecipeOnlyHash)
                throw new InvalidDataException("Recipe patch verification failed.");
            // Equal-length string edits preserve all metadata tokens and network type ordering.
            pe.ReplaceString(43928, 61, "InterstellarRift", Profile);
            pe.ReplaceString(44852, 137, @"${AppData}\\InterstellarRift\\Logs", @"${AppData}\\" + Profile + @"\\Logs");
            if (Hash(bytes) != InstalledHash)
                throw new InvalidDataException("Isolated recipe/profile verification failed.");
            return bytes;
        }

        private sealed class PeData
        {
            private readonly byte[] bytes;
            private readonly int sections;
            private readonly int sectionCount;
            private readonly int userStrings;

            public PeData(byte[] data)
            {
                bytes = data;
                int pe = BitConverter.ToInt32(bytes, 0x3c);
                int optional = pe + 24;
                int magic = BitConverter.ToUInt16(bytes, optional);
                int directory = optional + (magic == 0x20b ? 112 : magic == 0x10b ? 96 : 0);
                if (directory == optional) throw new InvalidDataException("Unsupported executable format.");
                sectionCount = BitConverter.ToUInt16(bytes, pe + 6);
                sections = optional + BitConverter.ToUInt16(bytes, pe + 20);
                int cli = Offset(BitConverter.ToUInt32(bytes, directory + 14 * 8));
                int metadata = Offset(BitConverter.ToUInt32(bytes, cli + 8));
                int cursor = (metadata + 16 + BitConverter.ToInt32(bytes, metadata + 12) + 3) & ~3;
                int streams = BitConverter.ToUInt16(bytes, cursor + 2);
                cursor += 4;
                for (int i = 0; i < streams; i++)
                {
                    int relative = BitConverter.ToInt32(bytes, cursor);
                    int start = cursor + 8;
                    int end = start;
                    while (bytes[end] != 0) end++;
                    if (Encoding.ASCII.GetString(bytes, start, end - start) == "#US") userStrings = metadata + relative;
                    cursor = (end + 4) & ~3;
                }
                if (userStrings == 0) throw new InvalidDataException("Missing executable string metadata.");
            }

            private int Offset(uint rva)
            {
                for (int i = 0; i < sectionCount; i++)
                {
                    int section = sections + i * 40;
                    uint address = BitConverter.ToUInt32(bytes, section + 12);
                    uint size = BitConverter.ToUInt32(bytes, section + 16);
                    if (rva >= address && rva - address < size)
                        return checked((int)(BitConverter.ToUInt32(bytes, section + 20) + rva - address));
                }
                throw new InvalidDataException("Executable address is not mapped.");
            }

            public int MethodCode(uint rva)
            {
                int method = Offset(rva);
                ushort flags = BitConverter.ToUInt16(bytes, method);
                if ((flags & 3) != 3) throw new InvalidDataException("Unexpected method header.");
                return method + ((flags >> 12) * 4);
            }

            public void ReplaceString(uint methodRva, int instructionOffset, string oldValue, string newValue)
            {
                if (oldValue.Length != newValue.Length) throw new InvalidDataException("Profile strings must keep their original size.");
                int instruction = MethodCode(methodRva) + instructionOffset;
                if (bytes[instruction] != 0x72) throw new InvalidDataException("Profile instruction changed.");
                uint token = BitConverter.ToUInt32(bytes, instruction + 1);
                if ((token & 0xff000000) != 0x70000000) throw new InvalidDataException("Invalid profile string token.");
                int entry = userStrings + (int)(token & 0x00ffffff);
                int size = oldValue.Length * 2;
                if (size + 1 >= 128 || bytes[entry] != size + 1 || Encoding.Unicode.GetString(bytes, entry + 1, size) != oldValue)
                    throw new InvalidDataException("Profile string validation failed.");
                Buffer.BlockCopy(Encoding.Unicode.GetBytes(newValue), 0, bytes, entry + 1, size);
            }
        }
    }
}
