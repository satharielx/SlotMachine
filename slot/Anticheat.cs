using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Security.Permissions;
using System.Security.Cryptography;
using System.Security.AccessControl;
using Microsoft.SqlServer.Server;
using System.Threading;

namespace slot
{
    public static class AnticheatTypes
    {
        public struct Keychain
        {
            public byte[] raw;
            public string hash;
            int salt;
            
        }
        public struct ActionPassword
        {
            int Seed;
            byte[] hash;
        }
    }
    public class Anticheat {
        private Dictionary<string, Thread> procedures = new Dictionary<string, Thread>();
        public Anticheat()
        {
            Program.PrintLine("Anti Cheat developed by Sathariel, loaded successfully!");
        }
        public void AddProcedure(string procedureName, Thread threaded) {
            if (!procedures.ContainsKey(procedureName))
            {
                procedures.Add(procedureName, threaded);
                procedures[procedureName].Start();
            }
            else Program.PrintLine($"{procedureName} already exists!");
        }
        public void SuspendProcedure(string procedureName) {
            if (procedures.ContainsKey(procedureName))
            {
                procedures[procedureName].Abort();
                procedures.Remove(procedureName);
            }
            else Program.PrintLine($"{procedureName} does not exist!");
        }
        public void ShowAllProcedures() {
            Program.PrintLine(new string('*', 12));
            Program.PrintLine("[*] Procedures list");
            foreach (var val in procedures) {
                Program.PrintLine($"- {val.Key} - Status at: {val.Value.IsAlive.ToString()}");
            }
            Program.PrintLine(new string('*', 12));
        }
    }
    
    static class Misc {

        public static string AsString(this AnticheatTypes.Keychain key)
        {
            string result = "";
            for (int i = 0; i < key.raw.Length; i++) {
                result += key.raw[i].ToString("x2");
            }
            return result;
        }
        [System.Security.SecuritySafeCritical]
        public static AnticheatTypes.Keychain ToKeychain<T>(this T @object) where T : struct
        {
            byte[] code = BitConverter.GetBytes(@object.GetHashCode());
            if (BitConverter.IsLittleEndian)
                Array.Reverse(code);
            SHA256Managed engine = new SHA256Managed();
            byte[] result = engine.ComputeHash(code, 0, code.Length);
            AnticheatTypes.Keychain ret = new AnticheatTypes.Keychain();
            ret.raw = result;
            ret.hash = ret.AsString();
            return ret;
        }
        
    }
}
