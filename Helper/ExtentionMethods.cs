﻿// Cypher (c) by Tangram Inc
// 
// Cypher is licensed under a
// Creative Commons Attribution-NonCommercial-NoDerivatives 4.0 International License.
// 
// You should have received a copy of the license along with this
// work. If not, see <http://creativecommons.org/licenses/by-nc-nd/4.0/>.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using Newtonsoft.Json;
using Sodium;
using TangramCypher.ApplicationLayer.Coin;

namespace TangramCypher.Helper
{
    public static class ExtentionMethods
    {
        public static StringContent AsJson(this object o)
          => new StringContent(JsonConvert.SerializeObject(o), Encoding.UTF8, "application/json");
        public static string ToHex(this byte[] data) => Utilities.BinaryToHex(data);
        public static byte[] FromHex(this string hex) => Utilities.HexToBinary(hex);
        public static string ToBase64(this byte[] data) => Convert.ToBase64String(Encoding.UTF8.GetBytes(Utilities.BinaryToHex(data)));
        public static byte[] ToByteArrayWithPadding(this string str)
        {
            const int BlockingSize = 16;
            int byteLength = ((str.Length / BlockingSize) + 1) * BlockingSize;
            byte[] toEncrypt = new byte[byteLength];
            Encoding.ASCII.GetBytes(str).CopyTo(toEncrypt, 0);
            return toEncrypt;
        }
        public static string RemovePadding(this String str)
        {
            char paddingChar = '\0';
            int indexOfFirstPadding = str.IndexOf(paddingChar);
            string cleanString = str.Remove(indexOfFirstPadding);
            return cleanString;
        }
        public static void ExecuteInConstrainedRegion(this Action action)
        {
            RuntimeHelpers.PrepareConstrainedRegions();
            try
            {
            }
            finally
            {
                action();
            }
        }
        public static SecureString ToSecureString(this string value)
        {
            var secureString = new SecureString();
            Array.ForEach(value.ToArray(), secureString.AppendChar);
            secureString.MakeReadOnly();
            return secureString;
        }
        public static string ToUnSecureString(this SecureString secureString)
        {
            IntPtr unmanagedString = IntPtr.Zero;
            try
            {
                unmanagedString = Marshal.SecureStringToGlobalAllocUnicode(secureString);
                return Marshal.PtrToStringUni(unmanagedString);
            }
            finally
            {
                Marshal.ZeroFreeGlobalAllocUnicode(unmanagedString);
            }
        }
        public static byte[] ToArray(this SecureString s)
        {
            if (s == null)
                throw new NullReferenceException();
            if (s.Length == 0)
                return new byte[0];
            List<byte> result = new List<byte>();
            IntPtr ptr = SecureStringMarshal.SecureStringToGlobalAllocAnsi(s);
            try
            {
                int i = 0;
                do
                {
                    byte b = Marshal.ReadByte(ptr, i++);
                    if (b == 0)
                        break;
                    result.Add(b);
                } while (true);
            }
            finally
            {
                Marshal.ZeroFreeGlobalAllocAnsi(ptr);
            }
            return result.ToArray();
        }
        public static CoinDto FormatCoinToBase64(this CoinDto coin)
        {
            var formattedCoin = new CoinDto
            {
                Envelope = new EnvelopeDto()
                {
                    Commitment = Convert.ToBase64String(Encoding.UTF8.GetBytes(coin.Envelope.Commitment)),
                    Proof = Convert.ToBase64String(Encoding.UTF8.GetBytes(coin.Envelope.Proof)),
                    PublicKey = Convert.ToBase64String(Encoding.UTF8.GetBytes(coin.Envelope.PublicKey)),
                    Signature = Convert.ToBase64String(Encoding.UTF8.GetBytes(coin.Envelope.Signature))
                }
            };
            formattedCoin.Hash = Convert.ToBase64String(Encoding.UTF8.GetBytes(coin.Hash));
            formattedCoin.Hint = Convert.ToBase64String(Encoding.UTF8.GetBytes(coin.Hint));
            formattedCoin.Keeper = Convert.ToBase64String(Encoding.UTF8.GetBytes(coin.Keeper));
            formattedCoin.Network = Convert.ToBase64String(Encoding.UTF8.GetBytes(coin.Network));
            formattedCoin.Principle = Convert.ToBase64String(Encoding.UTF8.GetBytes(coin.Principle));
            formattedCoin.Stamp = Convert.ToBase64String(Encoding.UTF8.GetBytes(coin.Stamp));
            formattedCoin.Version = coin.Version;

            return formattedCoin;
        }
        public static CoinDto FormatCoinFromBase64(this CoinDto coin)
        {
            var formattedCoin = new CoinDto
            {
                Envelope = new EnvelopeDto()
                {
                    Commitment = Encoding.UTF8.GetString(Convert.FromBase64String(coin.Envelope.Commitment)),
                    Proof = Encoding.UTF8.GetString(Convert.FromBase64String(coin.Envelope.Proof)),
                    PublicKey = Encoding.UTF8.GetString(Convert.FromBase64String(coin.Envelope.PublicKey)),
                    Signature = Encoding.UTF8.GetString(Convert.FromBase64String(coin.Envelope.Signature))
                }
            };
            formattedCoin.Hash = Encoding.UTF8.GetString(Convert.FromBase64String(coin.Hash));
            formattedCoin.Hint = Encoding.UTF8.GetString(Convert.FromBase64String(coin.Hint));
            formattedCoin.Keeper = Encoding.UTF8.GetString(Convert.FromBase64String(coin.Keeper));
            // formattedCoin.Network = Encoding.UTF8.GetString(Convert.FromBase64String(coin.Network));
            formattedCoin.Principle = Encoding.UTF8.GetString(Convert.FromBase64String(coin.Principle));
            formattedCoin.Stamp = Encoding.UTF8.GetString(Convert.FromBase64String(coin.Stamp));
            formattedCoin.Version = coin.Version;

            return formattedCoin;
        }
    }
}
