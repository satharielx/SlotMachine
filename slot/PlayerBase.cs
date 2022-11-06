using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Threading;

namespace slot
{
    public class PlayerBase
    {
        protected double previous = 0.0;
        protected double current = 0.0;
        protected AnticheatTypes.Keychain previosKey = new AnticheatTypes.Keychain();
        protected Anticheat anticheat = new Anticheat();
        public double Balance {
            get { return current; } 
            set 
            {
                previous = current;
                previosKey = previous.ToKeychain<double>();
                current = value;
            } 
        }
        protected AnticheatTypes.Keychain HashKeyStroke;
        public string Username { get; private set; }
        public PlayerBase() {
            HashKeyStroke = Balance.ToKeychain<double>();
            Thread antiCheating = new Thread(OnChange);
            anticheat.AddProcedure("BalanceCheck", antiCheating);
            
        }
        public void PrintProcedures() => anticheat.ShowAllProcedures();
        public void UpdateBalance(double amount) {
           
            Balance += amount;
            HashKeyStroke = Balance.ToKeychain<double>();
        }
        public void SetBalance(double amount)
        {
            
            Balance = amount;
            HashKeyStroke = Balance.ToKeychain<double>();
        }
        public override string ToString()
        {
            return $"Player has ${Balance} in their account.\nHash key of value: {HashKeyStroke.hash}";
        }
        protected void OnChange() {
            while (true) {
                AnticheatTypes.Keychain temp = new AnticheatTypes.Keychain();
                temp = Balance.ToKeychain<double>();
                if (temp.hash != HashKeyStroke.hash)
                {
                    Program.PrintLine($"[*] Anti Cheat Measures: A violation was detected. Previous value was set.");
                    SetBalance(previous);
                    Program.PrintLine(this.ToString());
                }
                Thread.Sleep(1000);
            }
            
        }
    }
}
